using UnityEngine;
using Poltergeist;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PhantasmaPhoenix.Unity.Core.Logging;
using PhantasmaPhoenix.RPC.Models;

public static class Tokens
{
    public static List<TokenResult> SupportedTokens = new();
    public static object __lockObj = new object();

    // Key: symbol, Value: coingeckoApiSymbol
    public static Dictionary<string, string> CoingeckoApiSymbols = new();
    public static void AddCGSymbol(string tokenSymbol, string cgSymbol)
    {
        CoingeckoApiSymbols[tokenSymbol] = cgSymbol;
    }
    public static void AddCGSymbol(TokenResult token, string cgSymbol)
    {
        AddCGSymbol(token.Symbol, cgSymbol);
    }
    public static string GetCGSymbol(TokenResult token)
    {
        return CoingeckoApiSymbols.TryGetValue(token.Symbol, out var result) ? result : "";
    }
    public static bool HasCGSymbol(TokenResult token)
    {
        return GetCGSymbol(token) != "";
    }

    public static void Reset()
    {
        TokenIconCache.Reset();
        SupportedTokens.Clear();
        CoingeckoApiSymbols.Clear();
    }

    public static void AddTokens(TokenResult[] tokens)
    {
        SupportedTokens.AddRange(tokens);
    }

    public static void AddToken(TokenResult token)
    {
        SupportedTokens.Add(token);
    }

    public static void LoadCoinGeckoSymbols()
    {
        var resource = Resources.Load<TextAsset>("Common/Tokens.CoinGecko");

        if (resource == null || string.IsNullOrEmpty(resource.text))
        {
            Log.WriteWarning("Cannot load CoinGecko symbols.");
            return;
        }

        var tokenApiSymbols = JsonConvert.DeserializeObject<JArray>(resource.text);

        if (tokenApiSymbols == null)
        {
            Log.WriteWarning("Cannot load CoinGecko symbols - file is corrupted.");
            return;
        }

        foreach (var tokenApiSymbol in tokenApiSymbols)
        {
            var symbol = tokenApiSymbol.Value<string>("symbol");
            var apiSymbol = tokenApiSymbol.Value<string>("apiSymbol");
            AddCGSymbol(symbol, apiSymbol == "-" ? "" : apiSymbol);
        }
    }
    public static void Init(TokenResult[] mainnetTokens)
    {
        Tokens.Reset();

        Tokens.AddTokens(mainnetTokens);
        TokenIconCache.RebuildFromTokens(mainnetTokens);

        Tokens.LoadCoinGeckoSymbols();

        Log.Write($"{Tokens.GetTokens().Length} tokens supported", Log.Level.Debug1);

        Tokens.ToLog();
    }

    public static TokenResult[] GetTokens(string symbol)
    {
        return SupportedTokens.Where(x => x.Symbol.ToUpper() == symbol.ToUpper())
            .ToArray();
    }
    public static TokenResult GetToken(string symbol, PlatformKind platform)
    {
        return SupportedTokens.Where(x => x.Symbol.ToUpper() == symbol.ToUpper() &&
            ((platform == PlatformKind.Phantasma) /*||
            (platform != PlatformKind.Phantasma && x.external != null && x.external.Any(y => y.platform.ToUpper() == platform.ToString().ToUpper()))*/))
            .SingleOrDefault();
    }
    public static bool HasSwappableToken(string symbol, PlatformKind platform)
    {
        return false;
        /*return SupportedTokens.Any(x => x.symbol.ToUpper() == symbol.ToUpper() &&
            ((platform == PlatformKind.Phantasma && x.IsSwappable()) ||
            (platform != PlatformKind.Phantasma && x.IsSwappable() && x.external != null && x.external.Any(y => y.platform.ToUpper() == platform.ToString().ToUpper()))));*/
    }
    public static bool GetToken(string symbol, PlatformKind platform, out TokenResult token)
    {
        token = GetToken(symbol, platform);
        if (token != default(TokenResult))
        {
            return true;
        }

        token = new TokenResult();
        return false;
    }
    public static TokenResult[] GetTokens()
    {
        return SupportedTokens.ToArray();
    }
    public static TokenResult[] GetTokens(PlatformKind platform)
    {
        return SupportedTokens.Where(x => platform == PlatformKind.Phantasma /*||
            (platform != PlatformKind.Phantasma && x.external != null && x.external.Any(y => y.platform.ToUpper() == platform.ToString().ToUpper()))*/)
            .ToArray();
    }
    public static TokenResult[] GetTokensForCoingecko()
    {
        return SupportedTokens.Where(x => HasCGSymbol(x))
            .ToArray();
    }
    public static uint GetTokenDecimals(string symbol, PlatformKind platform)
    {
        var token = GetToken(symbol, platform);
        if (token != default(TokenResult))
        {
            return token.Decimals;
        }

        throw new System.Exception($"Cannot load token decimals for {symbol}");
    }
    public static ulong GetTokenCarbonId(string symbol, PlatformKind platform)
    {
        var token = GetToken(symbol, platform);
        if (token != default(TokenResult))
        {
            return ulong.Parse(token.CarbonId);
        }

        throw new System.Exception($"Cannot load token carbon ID for {symbol}");
    }
    // Non-throwing lookup of a supported token by its Carbon id. Returns false when no
    // loaded token carries that Carbon id. Shared by GetTokenByCarbonId and by the lazy
    // single-token re-fetch path (to check whether a fetch is still needed) so the parse
    // loop is not duplicated.
    public static bool TryGetTokenByCarbonId(ulong carbonId, out TokenResult token)
    {
        token = null;

        if (SupportedTokens == null || SupportedTokens.Count == 0)
        {
            return false;
        }

        foreach (var entry in SupportedTokens)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.CarbonId))
            {
                continue;
            }

            if (ulong.TryParse(entry.CarbonId, out var parsed) && parsed == carbonId)
            {
                token = entry;
                return true;
            }
        }

        return false;
    }

    public static TokenResult GetTokenByCarbonId(ulong carbonId, PlatformKind platform)
    {
        // Carbon IDs are required for supported tokens; missing mapping indicates corrupted data.
        if (platform != PlatformKind.Phantasma)
        {
            // Not tied to an individually fetchable Phantasma token id, so not re-fetchable.
            throw new TokenMappingException($"Cannot load token for carbon ID {carbonId} on platform {platform}");
        }

        if (SupportedTokens == null || SupportedTokens.Count == 0)
        {
            // Carry the carbon id so callers can still attempt a targeted lazy fetch even
            // when the full token list failed to load.
            throw new TokenMappingException($"Cannot load token for carbon ID {carbonId} (token list is empty)", carbonId);
        }

        if (TryGetTokenByCarbonId(carbonId, out var token))
        {
            return token;
        }

        // Token id is valid but not in the currently loaded list (e.g. a token created after
        // the last token-list load); carry the carbon id so callers can lazily re-fetch just
        // this one token and retry instead of hard-failing.
        throw new TokenMappingException($"Cannot load token for carbon ID {carbonId}", carbonId);
    }
    public static void ToLog()
    {
        var tokens = "";
        foreach (var token in SupportedTokens)
        {
            tokens += $"Symbol {token.Symbol} ({token.Name}), decimals {token.Decimals}, supplies {token.CurrentSupply}/{token.MaxSupply}/{token.BurnedSupply}, flags '{token.Flags}', coinGeckoId '{GetCGSymbol(token)}'\n";
        }
        Log.Write("Supported tokens:\n" + tokens, Log.Level.Debug1);
    }
}
