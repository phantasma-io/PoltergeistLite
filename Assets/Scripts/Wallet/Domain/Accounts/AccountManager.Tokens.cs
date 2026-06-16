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
        public string GetTokenWorth(string symbol, BigInteger amount, uint decimals)
        {
            bool hasLocalCurrency = !string.IsNullOrEmpty(CurrentTokenCurrency) && _currencyMap.ContainsKey(CurrentTokenCurrency);
            if (!_tokenPrices.ContainsKey(symbol) || !hasLocalCurrency)
            {
                return null;
            }

            // First try the exact conversion. If the token uses an extreme decimals value (for example 64), the
            // exact decimal path can reject it because decimal cannot represent the intermediate 10^decimals scale.
            // In that case we degrade to a bounded approximation that is sufficient for a short fiat estimate.
            if (!WalletAmountFormatter.TryToDecimal(amount, decimals, out var decimalAmount) &&
                !WalletAmountFormatter.TryToApproxDecimal(amount, decimals, 18, out decimalAmount))
            {
                return null;
            }

            var price = _tokenPrices[symbol] * decimalAmount;
            var ch = _currencyMap[CurrentTokenCurrency];
            return $"{WalletAmountFormatter.Format(price, MoneyFormatType.Short)} {ch}";
        }

        private async Task FetchTokenPricesAsync(IEnumerable<TokenResult> tokens, string currency, CancellationToken cancellationToken)
        {
            var separator = "%2C";
            var url = "https://api.coingecko.com/api/v3/simple/price?ids=" + string.Join(separator, tokens.Where(x => Tokens.HasCGSymbol(x)).Select(x => Tokens.GetCGSymbol(x)).Distinct().ToList()) + "&vs_currencies=" + currency;
            try
            {
                var response = await WebClientAsync.GetAsync<Dictionary<string, Dictionary<string, decimal>>>(
                    url,
                    WebClient.DefaultTimeout,
                    NetworkRetryPolicy.Retries,
                    NetworkRetryPolicy.RetryDelay,
                    cancellationToken);
                foreach (var token in tokens)
                {
                    var cgSymbol = Tokens.GetCGSymbol(token);
                    var node = response.Where(x => x.Key.ToUpperInvariant() == cgSymbol.ToUpperInvariant()).Select(x => x.Value).FirstOrDefault();
                    if (node != default)
                    {
                        var price = node.Where(x => x.Key.ToUpperInvariant() == currency.ToUpperInvariant()).Select(x => x.Value).FirstOrDefault();

                        SetTokenPrice(token.Symbol, price);
                    }
                    else
                    {
                        Log.Write($"Cannot get price for '{cgSymbol}'.");
                    }
                }

                // GOATI token price is pegged to 0.1$.
                SetTokenPrice("GOATI", Convert.ToDecimal(0.1));

                // Prices updated: refresh balances so fiat values appear without manual actions.
                if (HasSelection)
                {
                    RefreshBalances(false);
                }
            }
            catch (Exception e)
            {
                Log.WriteWarning(e.ToString());
            }
        }

        private void SetTokenPrice(string symbol, decimal price)
        {
            Log.Write($"Got price for {symbol} => {price}");
            _tokenPrices[symbol] = price;
        }

        private async Task<TokenResult[]> GetTokensAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                try
                {
                    return await AsyncPhantasma.FromApi<TokenResult[]>(
                        (onSuccess, onError) => phantasmaApi.GetTokens(true, onSuccess, onError, 10, NetworkRetryPolicy.Retries),
                        cancellationToken);
                }
                catch (PhantasmaRequestException ex)
                {
                    if (rpcAvailablePhantasma > 0 && Settings.nexusKind == NexusKind.Main_Net && ex.ErrorType == EPHANTASMA_SDK_ERROR_TYPE.WEB_REQUEST_ERROR)
                    {
                        ChangeFaultyRPCURL(PlatformKind.Phantasma);
                        continue;
                    }

                    CurrentTokenCurrency = "";
                    Settings.settingRequireReconfiguration = true;
                    Status = "ok"; // We are launching with uninitialized tokens,
                                   // to allow user to edit settings.

                    Log.WriteWarning("Error: Launching with uninitialized tokens.");
                    Log.WriteWarning("Tokens initialization error: " + ex.Message);
                    return Array.Empty<TokenResult>();
                }
            }
        }

        // Lazily fetches a single token by its Carbon id and adds it to the in-memory token list.
        // Used when Carbon description parsing references a token created after the wallet last
        // loaded its full token list (e.g. a brand-new marketplace listing currency), so a single
        // new token does not hard-block the WalletLink signing prompt. Returns true once the token
        // is available; false leaves the original token mapping error to surface.
        public async Task<bool> TryLoadMissingTokenByCarbonIdAsync(ulong carbonId, CancellationToken cancellationToken)
        {
            // Another path may have already loaded it (e.g. a concurrent token reinit).
            lock (Tokens.__lockObj)
            {
                if (Tokens.TryGetTokenByCarbonId(carbonId, out _))
                {
                    return true;
                }
            }

            if (phantasmaApi == null)
            {
                return false;
            }

            TokenResult token;
            try
            {
                // An empty symbol selects the token by its Carbon id (see PhantasmaAPI.GetToken).
                token = await AsyncPhantasma.FromApi<TokenResult>(
                    (onSuccess, onError) => phantasmaApi.GetToken("", true, carbonId, onSuccess, onError, WebClient.DefaultTimeout, NetworkRetryPolicy.Retries),
                    cancellationToken);
            }
            catch (PhantasmaRequestException ex)
            {
                Log.WriteWarning($"Lazy token fetch by carbon id {carbonId} failed: {ex.Message}");
                return false;
            }

            // Guard against a node returning an unexpected or mismatched token.
            if (token == null || string.IsNullOrWhiteSpace(token.CarbonId) ||
                !ulong.TryParse(token.CarbonId, out var parsed) || parsed != carbonId)
            {
                return false;
            }

            lock (Tokens.__lockObj)
            {
                // Re-check after the await in case the token was added meanwhile, to avoid a duplicate.
                if (Tokens.TryGetTokenByCarbonId(carbonId, out _))
                {
                    return true;
                }

                Tokens.AddToken(token);
            }

            return true;
        }

        private void TokensReinit()
        {
            if (tokensReinitInProgress)
                return;

            TokensReinitRoutineAsync(CancellationToken.None).Forget(LogTaskException);
        }

        private async Task TokensReinitRoutineAsync(CancellationToken cancellationToken)
        {
            tokensReinitInProgress = true;
            var tokens = await GetTokensAsync(cancellationToken);
            if (tokens.Length > 0)
            {
                lock (Tokens.__lockObj)
                {
                    Tokens.Init(tokens);
                }

                if (TextureProvider.Instance != null)
                {
                    TextureProvider.Instance.UnloadTokens();
                }

            }

            CurrentTokenCurrency = "";

            Status = "ok";
            tokensReinitInProgress = false;

            // Token metadata is ready again; refresh prices so fiat values can be shown promptly.
            RefreshTokenPrices();

            if (refreshBalancesAfterTokenReload)
            {
                var refreshForce = refreshBalancesAfterTokenReloadForce;
                var refreshPlatforms = refreshBalancesAfterTokenReloadPlatforms;
                var refreshCallback = refreshBalancesAfterTokenReloadCallback;
                refreshBalancesAfterTokenReload = false;
                refreshBalancesAfterTokenReloadForce = false;
                refreshBalancesAfterTokenReloadPlatforms = PlatformKind.None;
                refreshBalancesAfterTokenReloadCallback = null;

                if (HasSelection)
                {
                    RefreshBalances(refreshForce, refreshPlatforms, refreshCallback);
                }
            }
        }

        public void RequestTokensReload()
        {
            ScheduleBalanceRefreshAfterTokens();
            TokensReinit();
        }

        private void ScheduleBalanceRefreshAfterTokens(bool force = true, PlatformKind platforms = PlatformKind.None, Action callback = null)
        {
            refreshBalancesAfterTokenReload = true;
            refreshBalancesAfterTokenReloadForce = force || refreshBalancesAfterTokenReloadForce;
            // If caller specifies a platform set, keep the most recent explicit one; otherwise leave previous value.
            if (platforms != PlatformKind.None)
            {
                refreshBalancesAfterTokenReloadPlatforms = platforms;
            }

            if (callback != null)
            {
                refreshBalancesAfterTokenReloadCallback += callback;
            }
        }

        public void RefreshTokenPrices()
        {
            // Refresh when the display currency changed or the cached prices are at least 5 minutes
            // old; otherwise the previous fetch still stands.
            var currencyChanged = CurrentTokenCurrency != Settings.currency;
            var pricesStale = DateTime.UtcNow - _lastPriceUpdate >= TimeSpan.FromMinutes(5);
            if (!currencyChanged && !pricesStale)
            {
                return;
            }

            CurrentTokenCurrency = Settings.currency;
            _lastPriceUpdate = DateTime.UtcNow;
            FetchTokenPricesAsync(Tokens.GetTokensForCoingecko(), CurrentTokenCurrency, CancellationToken.None).Forget(LogTaskException);
        }

        public void UpdateAPIs(bool possibleNexusChange = false)
        {
            Log.Write("reinit APIs => " + Settings.phantasmaRPCURL);
            phantasmaApi = new PhantasmaAPI(Settings.phantasmaRPCURL);

            if (possibleNexusChange)
            {
                // Network switch: reload token list and ensure balances retry once tokens are back.
                ScheduleBalanceRefreshAfterTokens();
                TokensReinit();
            }
        }

        private void LoadNexus()
        {
            UpdateAPIs(true);
        }

        private static BigInteger ParseTokenAmount(string amount, uint decimals)
        {
            if (string.IsNullOrWhiteSpace(amount))
            {
                return BigInteger.Zero;
            }

            if (amount.Contains(".") || amount.Contains(","))
            {
                throw new FormatException($"Unexpected fractional amount '{amount}' for token with {decimals} decimals.");
            }

            if (!BigInteger.TryParse(amount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw))
            {
                throw new FormatException($"Cannot parse amount '{amount}'");
            }

            return raw;
        }

    }
}
