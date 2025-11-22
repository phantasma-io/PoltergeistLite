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

        public decimal AvailableDecimal => WalletAmountFormatter.ToDecimal(Available, Decimals, out _);
        public decimal StakedDecimal => WalletAmountFormatter.ToDecimal(Staked, Decimals, out _);
        public decimal ClaimableDecimal => WalletAmountFormatter.ToDecimal(Claimable, Decimals, out _);
        public bool AvailableOverflow
        {
            get
            {
                WalletAmountFormatter.ToDecimal(Available, Decimals, out var overflowed);
                return overflowed;
            }
        }

        public bool StakedOverflow
        {
            get
            {
                WalletAmountFormatter.ToDecimal(Staked, Decimals, out var overflowed);
                return overflowed;
            }
        }

        public bool ClaimableOverflow
        {
            get
            {
                WalletAmountFormatter.ToDecimal(Claimable, Decimals, out var overflowed);
                return overflowed;
            }
        }
        public string AvailableText => WalletAmountFormatter.Format(Available, Decimals);
        public string StakedText => WalletAmountFormatter.Format(Staked, Decimals);
        public string ClaimableText => WalletAmountFormatter.Format(Claimable, Decimals);
        public decimal Total => WalletAmountFormatter.ToDecimal(Available + Staked + Claimable, Decimals, out _);
    }
}
