namespace Poltergeist.Wallet
{
    /// <summary>
    /// Mutable view state for NFT filters and pagination shared across UI layers.
    /// </summary>
    public sealed class WalletNftViewState
    {
        public string FilterName = string.Empty;
        public int FilterTypeIndex = 0;
        public string FilterType = "All";
        public int FilterRarity = 0;
        public int FilterMinted = 0;

        public int PageSize = 25;
        public int PageNumber = 0;
        public int PageCount = 0;
        public int TotalCount = 0;

        public void ResetFilters()
        {
            FilterName = string.Empty;
            FilterTypeIndex = 0;
            FilterType = "All";
            FilterRarity = 0;
            FilterMinted = 0;
        }

        public void ResetPagination()
        {
            PageNumber = 0;
            PageCount = 0;
            TotalCount = 0;
        }
    }
}

