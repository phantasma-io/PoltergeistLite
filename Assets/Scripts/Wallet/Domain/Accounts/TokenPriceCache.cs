using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Poltergeist.Wallet;
using PhantasmaPhoenix.RPC.Models;
using PhantasmaPhoenix.Unity.Core;
using PhantasmaPhoenix.Unity.Core.Logging;

namespace Poltergeist
{
    // Caches token fiat prices (CoinGecko) and the active display currency, and formats fiat worth.
    // Pulled out of AccountManager; it depends on it only through two injected hooks: the current
    // settings currency, and a "prices changed" callback used to refresh balances.
    public class TokenPriceCache
    {
        private readonly Dictionary<string, decimal> _prices = new Dictionary<string, decimal>();
        private readonly Dictionary<string, string> _currencyMap = new Dictionary<string, string>();
        private DateTime _lastPriceUpdate = DateTime.MinValue;

        private readonly Func<string> _settingsCurrency;
        private readonly Action _onPricesUpdated;

        public string CurrentTokenCurrency { get; private set; }
        public IEnumerable<string> Currencies => _currencyMap.Keys;

        public TokenPriceCache(Func<string> settingsCurrency, Action onPricesUpdated)
        {
            _settingsCurrency = settingsCurrency;
            _onPricesUpdated = onPricesUpdated;

            _currencyMap["AUD"] = "A$";
            _currencyMap["CAD"] = "C$";
            _currencyMap["EUR"] = "€";
            _currencyMap["GBP"] = "£";
            _currencyMap["RUB"] = "₽";
            _currencyMap["USD"] = "$";
            _currencyMap["JPY"] = "¥";
        }

        // Clears the active currency so the next refresh re-reads it (used when token init fails).
        public void ResetCurrency()
        {
            CurrentTokenCurrency = "";
        }

        public string GetTokenWorth(string symbol, BigInteger amount, uint decimals)
        {
            bool hasLocalCurrency = !string.IsNullOrEmpty(CurrentTokenCurrency) && _currencyMap.ContainsKey(CurrentTokenCurrency);
            if (!_prices.ContainsKey(symbol) || !hasLocalCurrency)
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

            var price = _prices[symbol] * decimalAmount;
            var ch = _currencyMap[CurrentTokenCurrency];
            return $"{WalletAmountFormatter.Format(price, MoneyFormatType.Short)} {ch}";
        }

        // Refresh when the display currency changed or the cached prices are at least 5 minutes old;
        // otherwise the previous fetch still stands.
        public void Refresh()
        {
            var currency = _settingsCurrency();
            var currencyChanged = CurrentTokenCurrency != currency;
            var pricesStale = DateTime.UtcNow - _lastPriceUpdate >= TimeSpan.FromMinutes(5);
            if (!currencyChanged && !pricesStale)
            {
                return;
            }

            CurrentTokenCurrency = currency;
            _lastPriceUpdate = DateTime.UtcNow;
            FetchTokenPricesAsync(Tokens.GetTokensForCoingecko(), CurrentTokenCurrency, CancellationToken.None)
                .Forget(ex => Log.WriteWarning(ex.ToString()));
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

                        SetPrice(token.Symbol, price);
                    }
                    else
                    {
                        Log.Write($"Cannot get price for '{cgSymbol}'.");
                    }
                }

                // GOATI token price is pegged to 0.1$.
                SetPrice("GOATI", Convert.ToDecimal(0.1));

                // Prices updated: let the owner refresh balances so fiat values appear automatically.
                _onPricesUpdated?.Invoke();
            }
            catch (Exception e)
            {
                Log.WriteWarning(e.ToString());
            }
        }

        private void SetPrice(string symbol, decimal price)
        {
            Log.Write($"Got price for {symbol} => {price}");
            _prices[symbol] = price;
        }
    }
}
