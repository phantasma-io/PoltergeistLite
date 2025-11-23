using System;
using System.Numerics;
using Poltergeist.Wallet;

namespace Poltergeist
{
    // Token balance with raw integer amounts to avoid decimal overflow; decimal properties are derived safely.
    public class Balance
    {
        public string Symbol;
        public BigInteger Available;
        public BigInteger Staked;
        public BigInteger Claimable;
        public string Chain;
        public uint Decimals;
        public bool Burnable;
        public bool Fungible;
        public string PendingPlatform;
        public string PendingHash;
        public string[] Ids;

        private static int DisplayPrecision => AccountManager.Instance?.Settings?.balanceDisplayPrecision ?? 4;
        public string AvailableText => WalletAmountFormatter.Format(Available, Decimals, DisplayPrecision);
        public string StakedText => WalletAmountFormatter.Format(Staked, Decimals, DisplayPrecision);
        public string ClaimableText => WalletAmountFormatter.Format(Claimable, Decimals, DisplayPrecision);
        public BigInteger Total => Available + Staked + Claimable;
    }
}
