using System;
using System.Collections.Generic;
using PhantasmaPhoenix.NFT;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Fallback metadata provider using ROM data.
    /// </summary>
    public sealed class DefaultNftMetadataProvider : INftMetadataProvider
    {
        private readonly Func<AccountManager> _accountProvider;

        public DefaultNftMetadataProvider(Func<AccountManager> accountProvider)
        {
            _accountProvider = accountProvider ?? throw new ArgumentNullException(nameof(accountProvider));
        }

        public bool CanHandle(string symbol)
        {
            return true;
        }

        public bool TryGet(string symbol, string tokenId, out NftMetadata metadata)
        {
            metadata = NftMetadata.Empty;

            var account = _accountProvider();
            if (account == null || string.IsNullOrEmpty(tokenId))
            {
                return false;
            }

            try
            {
                var rom = account.GetNftRom(symbol, tokenId);
                if (rom == null || rom.IsEmpty())
                {
                    return false;
                }

                var name = rom.GetName() ?? string.Empty;
                var type = string.Empty;
                var rarity = 0;
                var date = rom.GetDate();

                metadata = new NftMetadata(name, type, rarity, date);
                return true;
            }
            catch (KeyNotFoundException)
            {
                return false;
            }
        }
    }
}
