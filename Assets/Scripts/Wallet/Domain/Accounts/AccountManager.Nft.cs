using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;
using System.Linq;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.Core;
using PhantasmaPhoenix.VM;
using Poltergeist.Wallet;
using PhantasmaPhoenix.Core.Extensions;
using Newtonsoft.Json.Linq;
using PhantasmaPhoenix.RPC.Models;
using PhantasmaPhoenix.Unity.Core;
using PhantasmaPhoenix.NFT;
using PhantasmaPhoenix.NFT.Extensions;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using PhantasmaPhoenix.Unity.Core.Logging;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Numerics;

namespace Poltergeist
{
    public partial class AccountManager
    {
        public void RefreshNft(bool force, string symbol)
        {
            async Task ExecuteAsync()
            {
                var now = DateTime.UtcNow;
                var session = CaptureAccountSession();
                var normalizedSymbol = NormalizeNftSymbol(symbol);
                var cacheAddress = CurrentState?.address;
                if (string.IsNullOrEmpty(normalizedSymbol))
                {
                    return;
                }

                Log.Write($"[NFT] RefreshNft start force={force} symbol={symbol} currentPlatform={CurrentPlatform}"); //TODO Check if still needed once refactoring is over

                if (force)
                {
                    // On force refresh we clear NFT symbol's cache.
                    if (normalizedSymbol == "TTRS")
                        TtrsStore.Clear();
                    else if (normalizedSymbol == "GAME")
                        GameStore.Clear();
                    else
                        Cache.ClearDataNode("tokens-" + symbol.ToLower(), Cache.FileType.JSON, cacheAddress);

                    NftImages.Clear(symbol);
                }

                var platforms = CurrentAccount.platforms.Split();

                foreach (var platform in platforms)
                {
                    var nftSessionId = AdvanceNftSession(platform, normalizedSymbol);
                    if (!IsNftRequestCurrent(session, platform, normalizedSymbol, nftSessionId))
                    {
                        return;
                    }

                    var refreshStarted = false;
                    var currentState = CurrentState;
                    if (currentState == null)
                    {
                        if (IsNftRequestCurrent(session, platform, normalizedSymbol, nftSessionId))
                        {
                            ReportWalletNft(platform, symbol, session);
                        }
                        continue;
                    }

                    BeginNftRefresh(platform, normalizedSymbol);
                    refreshStarted = true;
                    NftsRefreshStarted?.Invoke(platform, symbol);

                    var nftKey = (platform, normalizedSymbol);
                    List<TokenDataResult> cachedNfts = null;
                    Dictionary<string, IRom> cachedRoms = null;
                    lock (_nftCacheLock)
                    {
                        _nfts.TryGetValue(nftKey, out cachedNfts);
                        _roms.TryGetValue(nftKey, out cachedRoms);
                    }

                    var workingNfts = cachedNfts != null
                        ? new List<TokenDataResult>(cachedNfts)
                        : new List<TokenDataResult>();

                    var workingRoms = cachedRoms != null
                        ? new Dictionary<string, IRom>(cachedRoms)
                        : new Dictionary<string, IRom>();

                    var hasBalanceData = currentState.balances != null;
                    var balanceEntries = hasBalanceData
                        ? currentState.balances.Where(x => string.Equals(x.Symbol, normalizedSymbol, StringComparison.OrdinalIgnoreCase)).ToList()
                        : new List<Balance>();
                    var targetIds = new HashSet<string>(balanceEntries.SelectMany(x => x.Ids ?? Array.Empty<string>()), StringComparer.OrdinalIgnoreCase);

                    // Progressive updates only when there is no cached list for this (platform, symbol).
                    // This avoids flicker when cached data exists, but keeps the UI responsive when the list would be empty for a long time.
                    var progressEligible = (cachedNfts == null || cachedNfts.Count == 0) && targetIds.Count > 0;
                    var progressBatchSize = 5;
                    var progressInterval = TimeSpan.FromMilliseconds(250);
                    var lastProgressAt = DateTime.UtcNow;
                    var pendingProgress = 0;

                    void PublishProgress(bool force = false)
                    {
                        if (!progressEligible)
                        {
                            return;
                        }

                        if (!IsNftRequestCurrent(session, platform, normalizedSymbol, nftSessionId))
                        {
                            return;
                        }

                        pendingProgress++;
                        var now = DateTime.UtcNow;
                        if (!force && pendingProgress < progressBatchSize && now - lastProgressAt < progressInterval)
                        {
                            return;
                        }

                        pendingProgress = 0;
                        lastProgressAt = now;

                        lock (_nftCacheLock)
                        {
                            _nfts[nftKey] = new List<TokenDataResult>(workingNfts);
                            _roms[nftKey] = new Dictionary<string, IRom>(workingRoms);
                        }

                        ReportWalletNft(platform, symbol, session, log: false);
                    }

                    void UpsertNft(TokenDataResult tokenData)
                    {
                        if (tokenData == null || string.IsNullOrEmpty(tokenData.Id))
                        {
                            return;
                        }

                        var idx = workingNfts.FindIndex(x => string.Equals(x.Id, tokenData.Id, StringComparison.OrdinalIgnoreCase));
                        if (idx >= 0)
                        {
                            workingNfts[idx] = tokenData;
                        }
                        else
                        {
                            workingNfts.Add(tokenData);
                        }

                        PublishProgress();
                    }

                    void UpsertRom(string tokenId, IRom rom)
                    {
                        if (string.IsNullOrEmpty(tokenId))
                        {
                            return;
                        }

                        workingRoms[tokenId] = rom;
                    }

                    try
                    {
                        if (Tokens.GetToken(symbol, platform, out var tokenInfo))
                        {
                            switch (platform)
                            {
                                case PlatformKind.Phantasma:
                                    {
                                        if (tokenInfo.IsFungible())
                                        {
                                            break;
                                        }

                                        var cache = Cache.GetTokenCache("tokens-" + symbol.ToLower(), Cache.FileType.JSON, 0, currentState.address);
                                        if (cache == null)
                                        {
                                            cache = Array.Empty<TokenDataResult>();
                                        }

                                        Log.Write("Getting NFTs...");
                                        foreach (var balanceEntry in balanceEntries)
                                        {
                                            SetNftDescriptionsLoaded(platform, normalizedSymbol, false);
                                            var loadedTokenCounter = 0;
                                            var ids = balanceEntry.Ids ?? Array.Empty<string>();

                                            foreach (var id in ids)
                                            {
                                                var tokenData = Cache.FindTokenData(cache, id);

                                                if (tokenData != null)
                                                {
                                                    loadedTokenCounter++;

                                                    var rom = tokenData.ParseRom(symbol);
                                                    UpsertRom(tokenData.Id, rom);
                                                    var (hasError, error) = rom.HasParsingError();
                                                    if (rom.IsEmpty())
                                                    {
                                                        Log.Write($"ROM is null or empty");
                                                    }
                                                    else if (hasError)
                                                    {
                                                        Log.Write(error);
                                                    }

                                                    UpsertNft(tokenData);

                                                    NftImages.DownloadImageAsync(symbol, tokenData.GetPropertyValue("ImageURL"), id, session.Token).Forget(LogTaskException);
                                                }
                                                else if (normalizedSymbol == "TTRS")
                                                {
                                                    loadedTokenCounter++;

                                                    var tokenData2 = workingNfts.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)) ?? new TokenDataResult { Id = id };
                                                    UpsertNft(tokenData2);
                                                }
                                                else
                                                {
                                                    try
                                                    {
                                                        if (progressEligible)
                                                        {
                                                            // Insert a placeholder immediately so the list can start showing progress.
                                                            UpsertNft(new TokenDataResult { Id = id });
                                                        }

                                                        var tokenData2 = await AsyncPhantasma.FromApi<TokenDataResult>(
                                                            (onSuccess, onError) => phantasmaApi.GetNFT(
                                                                symbol,
                                                                id,
                                                                true,
                                                                onSuccess,
                                                                onError,
                                                                timeout: WebClient.DefaultTimeout,
                                                                retries: NetworkRetryPolicy.Retries),
                                                            session.Token);
                                                        if (!IsNftRequestCurrent(session, platform, normalizedSymbol, nftSessionId))
                                                        {
                                                            return;
                                                        }
                                                        var rom = tokenData2.ParseRom(symbol);
                                                        UpsertRom(id, rom);
                                                        var (hasError, error) = rom.HasParsingError();
                                                        if (rom.IsEmpty())
                                                        {
                                                            Log.Write($"ROM is null or empty");
                                                        }
                                                        else if (hasError)
                                                        {
                                                            Log.Write(error);
                                                        }

                                                        NftImages.DownloadImageAsync(symbol, tokenData2.GetPropertyValue("ImageURL"), id, session.Token).Forget(LogTaskException);

                                                        loadedTokenCounter++;

                                                        UpsertNft(tokenData2);
                                                        cache = cache.Where(x => !string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)).Append(tokenData2).ToArray();
                                                    }
                                                    catch (PhantasmaRequestException ex)
                                                    {
                                                        loadedTokenCounter++;
                                                        Log.Write($"NFT loading error for {symbol}/{id}: {ex.Message}");
                                                    }
                                                }
                                            }

                                            PublishProgress(force: true);

                                            if (ids.Length > 0 && loadedTokenCounter == ids.Length)
                                            {
                                                Cache.SaveTokenDatas("tokens-" + symbol.ToLower(), Cache.FileType.JSON, cache, currentState.address);

                                                if (symbol != "TTRS")
                                                {
                                                    SetNftDescriptionsLoaded(platform, normalizedSymbol, true);
                                                }
                                            }

                                            if (ids.Length > 0)
                                            {
                                                if (normalizedSymbol == "TTRS")
                                                {
                                                    await TtrsStore.LoadStoreNftAsync(ids, (item) =>
                                                        {
                                                            NftImages.DownloadImageAsync(symbol, item.item_info.image_url, item.id, session.Token).Forget(LogTaskException);
                                                        }, session.Token);
                                                    if (!IsNftRequestCurrent(session, platform, normalizedSymbol, nftSessionId))
                                                    {
                                                        return;
                                                    }

                                                    SetNftDescriptionsLoaded(platform, normalizedSymbol, true);
                                                    PublishProgress(force: true);
                                                }
                                                else if (normalizedSymbol == "GAME")
                                                {
                                                    await GameStore.LoadStoreNftAsync(ids, (item) =>
                                                        {
                                                            NftImages.DownloadImageAsync(symbol, item.parsed_rom.img_url, item.ID, session.Token).Forget(LogTaskException);
                                                        }, session.Token);
                                                    if (!IsNftRequestCurrent(session, platform, normalizedSymbol, nftSessionId))
                                                    {
                                                        return;
                                                    }

                                                    SetNftDescriptionsLoaded(platform, normalizedSymbol, true);
                                                    PublishProgress(force: true);
                                                }
                                            }
                                        }
                                    }
                                    break;

                                default:
                                    break;
                            }
                        }
                    }
                    finally
                    {
                        if (refreshStarted)
                        {
                            EndNftRefresh(platform, normalizedSymbol);
                        }

                        if (IsNftRequestCurrent(session, platform, normalizedSymbol, nftSessionId))
                        {
                            if (hasBalanceData)
                            {
                                workingNfts.RemoveAll(x => x == null || string.IsNullOrEmpty(x.Id) || !targetIds.Contains(x.Id));
                                var staleRomIds = workingRoms.Keys.Where(id => !targetIds.Contains(id)).ToList();
                                foreach (var stale in staleRomIds)
                                {
                                    workingRoms.Remove(stale);
                                }
                            }

                            lock (_nftCacheLock)
                            {
                                _nfts[nftKey] = workingNfts;
                                _roms[nftKey] = workingRoms;
                            }
                            ReportWalletNft(platform, symbol, session);
                        }
                    }
                }
            }

            ExecuteAsync().Forget(LogTaskException);
        }

        public void ResetNftsSorting()
        {
            currentTtrsNftsSortMode = TtrsNftSortMode.None;
            currentNftsSortMode = NftSortMode.None;
            currentNftsSortDirection = SortDirection.None;
        }

        public void SortTtrsNfts(string symbol)
        {
            var normalizedSymbol = NormalizeNftSymbol(symbol);
            if (string.IsNullOrEmpty(normalizedSymbol))
            {
                return;
            }

            var key = (CurrentPlatform, normalizedSymbol);
            List<TokenDataResult> nfts;
            lock (_nftCacheLock)
            {
                if (!_nfts.TryGetValue(key, out var cachedNfts) || cachedNfts == null)
                {
                    return;
                }
                nfts = new List<TokenDataResult>(cachedNfts);
            }

            if (!AreNftDescriptionsLoaded(CurrentPlatform, normalizedSymbol)) // We should not sort NFTs if there are no attributes available.
            {
                return;
            }

            if (normalizedSymbol == "TTRS")
            {
                if (currentTtrsNftsSortMode == (TtrsNftSortMode)Settings.ttrsNftSortMode && (int)currentNftsSortDirection == Settings.nftSortDirection)
                    return; // Nothing changed, no need to sort again.

                switch ((TtrsNftSortMode)Settings.ttrsNftSortMode)
                {
                    case TtrsNftSortMode.Number_Date:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            nfts = nfts.OrderBy(x => TtrsStore.GetNft(x.Id).mint).ThenBy(x => TtrsStore.GetNft(x.Id).timestamp).ToList();
                        else
                            nfts = nfts.OrderByDescending(x => TtrsStore.GetNft(x.Id).mint).ThenByDescending(x => TtrsStore.GetNft(x.Id).timestamp).ToList();
                        break;
                    case TtrsNftSortMode.Date_Number:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            nfts = nfts.OrderBy(x => TtrsStore.GetNft(x.Id).timestamp).ThenBy(x => TtrsStore.GetNft(x.Id).mint).ToList();
                        else
                            nfts = nfts.OrderByDescending(x => TtrsStore.GetNft(x.Id).timestamp).ThenByDescending(x => TtrsStore.GetNft(x.Id).mint).ToList();
                        break;
                    case TtrsNftSortMode.Type_Number_Date:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            nfts = nfts.OrderByDescending(x => TtrsStore.GetNft(x.Id).item_info.type).ThenBy(x => TtrsStore.GetNft(x.Id).mint).ThenBy(x => TtrsStore.GetNft(x.Id).timestamp).ToList();
                        else
                            nfts = nfts.OrderBy(x => TtrsStore.GetNft(x.Id).item_info.type).ThenByDescending(x => TtrsStore.GetNft(x.Id).mint).ThenByDescending(x => TtrsStore.GetNft(x.Id).timestamp).ToList();
                        break;
                    case TtrsNftSortMode.Type_Date_Number:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            nfts = nfts.OrderByDescending(x => TtrsStore.GetNft(x.Id).item_info.type).ThenBy(x => TtrsStore.GetNft(x.Id).timestamp).ThenBy(x => TtrsStore.GetNft(x.Id).mint).ToList();
                        else
                            nfts = nfts.OrderBy(x => TtrsStore.GetNft(x.Id).item_info.type).ThenByDescending(x => TtrsStore.GetNft(x.Id).timestamp).ThenByDescending(x => TtrsStore.GetNft(x.Id).mint).ToList();
                        break;
                    case TtrsNftSortMode.Type_Rarity: // And also Number and Date as last sorting parameters.
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            nfts = nfts.OrderByDescending(x => TtrsStore.GetNft(x.Id).item_info.type).ThenByDescending(x => TtrsStore.GetNft(x.Id).item_info.rarity).ThenBy(x => TtrsStore.GetNft(x.Id).mint).ThenBy(x => TtrsStore.GetNft(x.Id).timestamp).ToList();
                        else
                            nfts = nfts.OrderBy(x => TtrsStore.GetNft(x.Id).item_info.type).ThenBy(x => TtrsStore.GetNft(x.Id).item_info.rarity).ThenByDescending(x => TtrsStore.GetNft(x.Id).mint).ThenByDescending(x => TtrsStore.GetNft(x.Id).timestamp).ToList();
                        break;
                }

                lock (_nftCacheLock)
                {
                    _nfts[key] = nfts;
                }
                currentTtrsNftsSortMode = (TtrsNftSortMode)Settings.ttrsNftSortMode;
            }
            else if (normalizedSymbol == "GAME")
            {
                if (currentNftsSortMode == (NftSortMode)Settings.nftSortMode && (int)currentNftsSortDirection == Settings.nftSortDirection)
                    return; // Nothing changed, no need to sort again.

                switch ((NftSortMode)Settings.nftSortMode)
                {
                    case NftSortMode.Name:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            nfts = nfts.OrderBy(x => GameStore.GetNft(x.Id).meta?.name_english).ToList();
                        else
                            nfts = nfts.OrderByDescending(x => GameStore.GetNft(x.Id).meta?.name_english).ToList();
                        break;
                    case NftSortMode.Number_Date:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            nfts = nfts.OrderBy(x => GameStore.GetNft(x.Id).mint).ThenBy(x => GameStore.GetNft(x.Id).parsed_rom.timestampDT()).ToList();
                        else
                            nfts = nfts.OrderByDescending(x => GameStore.GetNft(x.Id).mint).ThenByDescending(x => GameStore.GetNft(x.Id).parsed_rom.timestampDT()).ToList();
                        break;
                    case NftSortMode.Date_Number:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            nfts = nfts.OrderBy(x => GameStore.GetNft(x.Id).parsed_rom.timestampDT()).ThenBy(x => GameStore.GetNft(x.Id).mint).ToList();
                        else
                            nfts = nfts.OrderByDescending(x => GameStore.GetNft(x.Id).parsed_rom.timestampDT()).ThenByDescending(x => GameStore.GetNft(x.Id).mint).ToList();
                        break;
                }

                lock (_nftCacheLock)
                {
                    _nfts[key] = nfts;
                }
                currentNftsSortMode = (NftSortMode)Settings.nftSortMode;
            }
            else
            {
                if (currentNftsSortMode == (NftSortMode)Settings.nftSortMode && (int)currentNftsSortDirection == Settings.nftSortDirection)
                    return; // Nothing changed, no need to sort again.

                // Some NFT collections can return missing ROM/metadata (or placeholders during progressive loading).
                // Sorting must stay stable and never throw; use safe fallback keys so items remain visible.
                string GetMintSortKey(string id)
                {
                    var token = GetNft(normalizedSymbol, id);
                    return string.IsNullOrWhiteSpace(token?.Mint) ? "0" : token.Mint;
                }

                string GetNameSortKey(string id)
                {
                    var rom = GetNftRom(normalizedSymbol, id);
                    return rom == null || rom.IsEmpty() ? string.Empty : (rom.GetName() ?? string.Empty);
                }

                DateTime GetDateSortKey(string id)
                {
                    var rom = GetNftRom(normalizedSymbol, id);
                    return rom == null || rom.IsEmpty() ? DateTime.MinValue : rom.GetDate();
                }

                switch ((NftSortMode)Settings.nftSortMode)
                {
                    case NftSortMode.Name:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            nfts = nfts.OrderBy(x => GetNameSortKey(x.Id)).ToList();
                        else
                            nfts = nfts.OrderByDescending(x => GetNameSortKey(x.Id)).ToList();
                        break;
                    case NftSortMode.Number_Date:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            nfts = nfts.OrderBy(x => GetMintSortKey(x.Id)).ThenBy(x => GetDateSortKey(x.Id)).ToList();
                        else
                            nfts = nfts.OrderByDescending(x => GetMintSortKey(x.Id)).ThenByDescending(x => GetDateSortKey(x.Id)).ToList();
                        break;
                    case NftSortMode.Date_Number:
                        if (Settings.nftSortDirection == (int)SortDirection.Ascending)
                            nfts = nfts.OrderBy(x => GetDateSortKey(x.Id)).ThenBy(x => GetMintSortKey(x.Id)).ToList();
                        else
                            nfts = nfts.OrderByDescending(x => GetDateSortKey(x.Id)).ThenByDescending(x => GetMintSortKey(x.Id)).ToList();
                        break;
                }

                lock (_nftCacheLock)
                {
                    _nfts[key] = nfts;
                }
                currentNftsSortMode = (NftSortMode)Settings.nftSortMode;
            }

            currentNftsSortDirection = (SortDirection)Settings.nftSortDirection;
        }

        public TokenDataResult GetNft(string id)
        {
            var nfts = CurrentNfts;
            return nfts?.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public List<TokenDataResult> GetNfts(string symbol)
        {
            return GetNfts(CurrentPlatform, symbol);
        }

        public List<TokenDataResult> GetNfts(PlatformKind platform, string symbol)
        {
            var normalizedSymbol = NormalizeNftSymbol(symbol);
            if (string.IsNullOrEmpty(normalizedSymbol))
            {
                return null;
            }

            var key = (platform, normalizedSymbol);
            lock (_nftCacheLock)
            {
                return _nfts.TryGetValue(key, out var nfts) ? nfts : null;
            }
        }

        public TokenDataResult GetNft(string symbol, string id)
        {
            var nfts = GetNfts(symbol);
            return nfts?.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public IRom GetNftRom(string symbol, string id)
        {
            var normalizedSymbol = NormalizeNftSymbol(symbol);
            if (string.IsNullOrEmpty(normalizedSymbol) || string.IsNullOrEmpty(id))
            {
                return null;
            }

            var key = (CurrentPlatform, normalizedSymbol);
            lock (_nftCacheLock)
            {
                if (_roms.TryGetValue(key, out var roms) && roms != null && roms.TryGetValue(id, out var rom))
                {
                    return rom;
                }
            }

            return null;
        }

        public IRom GetNftRom(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            Dictionary<string, IRom> current = null;
            lock (_nftCacheLock)
            {
                foreach (var entry in _roms)
                {
                    if (entry.Key.platform != CurrentPlatform)
                    {
                        continue;
                    }

                    if (current != null)
                    {
                        return null;
                    }

                    current = entry.Value;
                }
            }

            if (current != null && current.TryGetValue(id, out var rom))
            {
                return rom;
            }

            return null;
        }

        public async Task<ValidationResult<TokenDataResult>> LoadDebugNftAsync(string symbol, string tokenId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(tokenId))
            {
                return ValidationResult<TokenDataResult>.Fail("NFT symbol and token id are required.");
            }

            if (phantasmaApi == null)
            {
                return ValidationResult<TokenDataResult>.Fail("RPC client is not ready.");
            }

            tokenId = tokenId.Trim();

            var normalizedSymbol = NormalizeNftSymbol(symbol);
            if (string.IsNullOrEmpty(normalizedSymbol))
            {
                return ValidationResult<TokenDataResult>.Fail("NFT symbol is invalid.");
            }

            TokenDataResult tokenData;
            try
            {
                tokenData = await AsyncPhantasma.FromApi<TokenDataResult>(
                    (onSuccess, onError) => phantasmaApi.GetNFT(
                        normalizedSymbol,
                        tokenId,
                        true,
                        onSuccess,
                        onError,
                        timeout: WebClient.DefaultTimeout,
                        retries: NetworkRetryPolicy.Retries),
                    cancellationToken);
            }
            catch (PhantasmaRequestException ex)
            {
                Log.WriteWarning($"[NFT][Debug] GetNFT failed for {normalizedSymbol}:{tokenId}: {ex.Message}");
                return ValidationResult<TokenDataResult>.Fail($"Failed to load NFT: {ex.Message}");
            }
            catch (Exception ex)
            {
                Log.WriteWarning($"[NFT][Debug] GetNFT failed for {normalizedSymbol}:{tokenId}: {ex}");
                return ValidationResult<TokenDataResult>.Fail("Failed to load NFT.");
            }

            if (tokenData == null || string.IsNullOrWhiteSpace(tokenData.Id))
            {
                return ValidationResult<TokenDataResult>.Fail("NFT not found.");
            }

            IRom rom = null;
            try
            {
                rom = tokenData.ParseRom(normalizedSymbol);
            }
            catch (Exception ex)
            {
                Log.WriteWarning($"[NFT][Debug] Failed to parse ROM for {normalizedSymbol}:{tokenId}: {ex.Message}");
            }

            var key = (CurrentPlatform, normalizedSymbol);
            lock (_nftCacheLock)
            {
                var workingNfts = _nfts.TryGetValue(key, out var cachedNfts) && cachedNfts != null
                    ? new List<TokenDataResult>(cachedNfts)
                    : new List<TokenDataResult>();

                var idx = workingNfts.FindIndex(x => string.Equals(x.Id, tokenData.Id, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    workingNfts[idx] = tokenData;
                }
                else
                {
                    workingNfts.Add(tokenData);
                }

                _nfts[key] = workingNfts;

                if (rom != null && !rom.IsEmpty())
                {
                    var workingRoms = _roms.TryGetValue(key, out var cachedRoms) && cachedRoms != null
                        ? new Dictionary<string, IRom>(cachedRoms)
                        : new Dictionary<string, IRom>();
                    workingRoms[tokenData.Id] = rom;
                    _roms[key] = workingRoms;
                }
            }

            NftsUpdated?.Invoke(CurrentPlatform, normalizedSymbol);
            return ValidationResult<TokenDataResult>.Ok(tokenData);
        }

    }
}
