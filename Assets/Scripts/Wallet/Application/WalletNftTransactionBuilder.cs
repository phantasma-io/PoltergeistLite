using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.VM;
using Poltergeist;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Builds NFT transaction scripts (burn/transfer) so UI layers stay thin.
    /// </summary>
    public sealed class WalletNftTransactionBuilder
    {
        private readonly Func<AccountManager> _accountProvider;

        public WalletNftTransactionBuilder(Func<AccountManager> accountProvider)
        {
            _accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
        }

        public byte[] BuildBurnScript(string symbol, IEnumerable<string> nftIds, Address source)
        {
            var accountManager = _accountProvider();
            if (accountManager == null)
            {
                throw new InvalidOperationException("Account manager is not available.");
            }

            var ids = nftIds?.Where(x => !string.IsNullOrEmpty(x)).ToList() ?? new List<string>();
            if (ids.Count == 0)
            {
                throw new ArgumentException("No NFT ids provided.", nameof(nftIds));
            }

            var sb = new ScriptBuilder();
            sb.AllowGas(source, Address.Null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit);

            foreach (var nftId in ids)
            {
                sb.CallInterop("Runtime.BurnToken", source, symbol, BigInteger.Parse(nftId));
            }

            sb.SpendGas(source);
            return sb.EndScript();
        }

        public IReadOnlyList<byte[]> BuildTransferScripts(string symbol, Address source, Address destination, IEnumerable<string> nftIds, out string description)
        {
            var accountManager = _accountProvider();
            if (accountManager == null)
            {
                throw new InvalidOperationException("Account manager is not available.");
            }

            var ids = nftIds?.Where(x => !string.IsNullOrEmpty(x)).ToList() ?? new List<string>();
            if (ids.Count == 0)
            {
                throw new ArgumentException("No NFTs provided.", nameof(nftIds));
            }

            const int transferLimit = 100;
            var scripts = new List<byte[]>();
            var descBuilder = new StringBuilder();
            descBuilder.AppendLine($"Transfer {symbol} NFTs");

            foreach (var chunk in Chunk(ids, transferLimit))
            {
                var sb = new ScriptBuilder();
                sb.AllowGas(source, Address.Null, accountManager.Settings.feePrice, accountManager.Settings.feeLimit);

                foreach (var nftId in chunk)
                {
                    sb.TransferNFT(symbol, source, destination, BigInteger.Parse(nftId));

                    var nftDescription = string.Empty;
                    if (string.Equals(symbol, "TTRS", StringComparison.OrdinalIgnoreCase))
                    {
                        var item = TtrsStore.GetNft(nftId);
                        if (!string.IsNullOrEmpty(item.id))
                        {
                            if (!string.IsNullOrEmpty(item.item_info.name_english))
                            {
                                var name = item.item_info.name_english;
                                nftDescription = " " + (name.Length > 25 ? name.Substring(0, 22) + "..." : name);
                            }

                            nftDescription += " Minted " + item.timestamp.ToString("dd.MM.yy") + " #" + item.mint;
                        }
                    }

                    descBuilder.AppendLine($"#{nftId.Substring(0, 5)}...{nftId.Substring(nftId.Length - 5)}{nftDescription}");
                }

                sb.SpendGas(source);
                scripts.Add(sb.EndScript());
            }

            descBuilder.Append($"to {destination}.");
            description = descBuilder.ToString();
            return scripts;
        }

        private static IEnumerable<List<T>> Chunk<T>(IReadOnlyList<T> source, int size)
        {
            for (var i = 0; i < source.Count; i += size)
            {
                var length = Math.Min(size, source.Count - i);
                var chunk = new List<T>(length);
                for (var j = 0; j < length; j++)
                {
                    chunk.Add(source[i + j]);
                }

                yield return chunk;
            }
        }
    }
}
