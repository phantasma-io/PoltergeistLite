using UnityEngine;
using Poltergeist;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PhantasmaIntegration;
using PhantasmaPhoenix.Unity.Core.Logging;

public static class Tokens
{
    public static List<Token> SupportedTokens = new();
    public static object __lockObj = new object();

    // Key: symbol, Value: coingeckoApiSymbol
    public static Dictionary<string, string> CoingeckoApiSymbols = new();
    public static void AddCGSymbol(string tokenSymbol, string cgSymbol)
    {
        CoingeckoApiSymbols[tokenSymbol] = cgSymbol;
    }
    public static void AddCGSymbol(Token token, string cgSymbol)
    {
        AddCGSymbol(token.symbol, cgSymbol);
    }
    public static string GetCGSymbol(Token token)
    {
        return CoingeckoApiSymbols.TryGetValue(token.symbol, out var result) ? result : "";
    }
    public static bool HasCGSymbol(Token token)
    {
        return GetCGSymbol(token) != "";
    }

    public static void Reset()
    {
        SupportedTokens.Clear();
    }
    
    public static void AddTokens(Token[] tokens)
    {
        SupportedTokens.AddRange(tokens);
    }
    
    public static void AddToken(Token token)
    {
        SupportedTokens.Add(token);
    }
    
    public static void LoadCoinGeckoSymbols()
    {
        // First we init all fungible token API IDs with default values.
        SupportedTokens.ForEach(x => { if (!HasCGSymbol(x) && x.IsFungible()) { AddCGSymbol(x, x.symbol.ToLower()); } });

        // Then apply IDs from config.
        var resource = Resources.Load<TextAsset>("Tokens.CoinGecko");

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
    public static void Init(Token[] mainnetTokens)
    {
            Tokens.Reset();

            Tokens.AddTokens(mainnetTokens);

            Tokens.LoadCoinGeckoSymbols();

            Log.Write($"{Tokens.GetTokens().Length} tokens supported");

            Tokens.ToLog();
    }

    public static Token[] GetTokens(string symbol)
    {
        return SupportedTokens.Where(x => x.symbol.ToUpper() == symbol.ToUpper())
            .ToArray();
    }
    public static Token GetToken(string symbol, PlatformKind platform)
    {
        return SupportedTokens.Where(x => x.symbol.ToUpper() == symbol.ToUpper() &&
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
    public static bool GetToken(string symbol, PlatformKind platform, out Token token)
    {
        token = GetToken(symbol, platform);
        if (token != default(Token))
        {
            return true;
        }

        token = new Token();
        return false;
    }
    public static Token[] GetTokens()
    {
        return SupportedTokens.ToArray();
    }
    public static Token[] GetTokens(PlatformKind platform)
    {
        return SupportedTokens.Where(x => platform == PlatformKind.Phantasma /*||
            (platform != PlatformKind.Phantasma && x.external != null && x.external.Any(y => y.platform.ToUpper() == platform.ToString().ToUpper()))*/)
            .ToArray();
    }
    public static Token[] GetTokensForCoingecko()
    {
        return SupportedTokens.Where(x => HasCGSymbol(x))
            .ToArray();
    }
    public static int GetTokenDecimals(string symbol, PlatformKind platform)
    {
        var token = GetToken(symbol, platform);
        if (token != default(Token))
        {
            return token.decimals;
        }

        return -1;
    }
    public static string GetTokenHash(string symbol, PlatformKind platform)
    {
        /*var token = GetToken(symbol, platform);
        if (token != default(Token))
        {
            if (token.external == null)
                return null;

            var hash = token.external.Where(x => x.platform.ToUpper() == platform.ToString().ToUpper()).SingleOrDefault()?.hash;

            if (hash != null && hash.StartsWith("0x"))
                hash = hash.Substring(2);

            return hash;
        }*/

        return null;
    }
    public static string GetTokenHash(Token token, PlatformKind platform)
    {
        /*if (token != default(Token))
        {
            if (token.external == null)
                return null;

            return token.external.Where(x => x.platform.ToUpper() == platform.ToString().ToUpper()).SingleOrDefault()?.hash;
        }*/

        return null;
    }

    public static void ToLog()
    {
        var tokens = "";
        foreach (var token in SupportedTokens)
        {
            tokens += $"Symbol {token.symbol} ({token.name}), decimals {token.decimals}, supplies {token.currentSupply}/{token.maxSupply}/{token.burnedSupply}, flags '{token.flags}', coinGeckoId '{GetCGSymbol(token)}'\n";
        }
        Log.Write("Supported tokens:\n" + tokens);
    }
}
