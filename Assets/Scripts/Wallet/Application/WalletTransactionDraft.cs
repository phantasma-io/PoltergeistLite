using System;
using System.Collections.Generic;
using System.Numerics;
using PhantasmaPhoenix.Cryptography;
using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.Protocol.Carbon;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using Poltergeist;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Immutable description of a prepared transaction (scripts or carbon payload) before sending.
    /// </summary>
    public sealed class WalletTransactionDraft
    {
        private WalletTransactionDraft(string description, string chain, BigInteger gasPrice, BigInteger gasLimit, ProofOfWork pow, byte[] script, IReadOnlyList<byte[]> scripts, TxMsg? carbonTx, byte[] payload, TransferRequest? transferRequest)
        {
            Description = description ?? string.Empty;
            Chain = chain ?? string.Empty;
            GasPrice = gasPrice;
            GasLimit = gasLimit;
            PoW = pow;
            Script = script;
            Scripts = scripts;
            CarbonTx = carbonTx;
            Payload = payload;
            TransferRequest = transferRequest;
        }

        public string Description { get; }
        public string Chain { get; }
        public BigInteger GasPrice { get; }
        public BigInteger GasLimit { get; }
        public ProofOfWork PoW { get; }
        public byte[] Script { get; }
        public IReadOnlyList<byte[]> Scripts { get; }
        public TxMsg? CarbonTx { get; }
        public byte[] Payload { get; }
        public TransferRequest? TransferRequest { get; }

        public bool IsCarbonTransaction => CarbonTx.HasValue;
        public bool HasScripts => Script != null || (Scripts != null && Scripts.Count > 0);

        public static WalletTransactionDraft ForSingleScript(string description, byte[] script, string chain, BigInteger gasPrice, BigInteger gasLimit, ProofOfWork pow, byte[] payload = null, TransferRequest? transferRequest = null)
        {
            return new WalletTransactionDraft(description, chain, gasPrice, gasLimit, pow, script, null, null, payload, transferRequest);
        }

        public static WalletTransactionDraft ForScripts(string description, IReadOnlyList<byte[]> scripts, string chain, BigInteger gasPrice, BigInteger gasLimit, ProofOfWork pow, byte[] payload = null, TransferRequest? transferRequest = null)
        {
            return new WalletTransactionDraft(description, chain, gasPrice, gasLimit, pow, null, scripts, null, payload, transferRequest);
        }

        public static WalletTransactionDraft ForCarbon(string description, TxMsg tx, string chain, BigInteger gasPrice, BigInteger gasLimit, byte[] payload = null)
        {
            return new WalletTransactionDraft(description, chain, gasPrice, gasLimit, ProofOfWork.None, null, null, tx, payload, null);
        }
    }

    public sealed class WalletTransactionDraftResult
    {
        private WalletTransactionDraftResult(bool success, WalletTransactionDraft draft, string error, decimal amount)
        {
            Success = success;
            Draft = draft;
            Error = error ?? string.Empty;
            Amount = amount;
        }

        public bool Success { get; }
        public WalletTransactionDraft Draft { get; }
        public string Error { get; }
        public decimal Amount { get; }

        public static WalletTransactionDraftResult CreateSuccess(WalletTransactionDraft draft, decimal amount = 0)
        {
            return new WalletTransactionDraftResult(true, draft, null, amount);
        }

        public static WalletTransactionDraftResult Fail(string error)
        {
            return new WalletTransactionDraftResult(false, null, error, 0);
        }
    }
}
