namespace Poltergeist
{
    public struct TransferRequest
    {
        public PlatformKind platform;
        public string destination;
        public string symbol;
        public System.Numerics.BigInteger amount;
        public string interop;
    }
}
