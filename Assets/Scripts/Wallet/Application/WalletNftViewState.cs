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

        public bool UpdateFilters(string filterName, int filterTypeIndex, string filterType, int filterRarity, int filterMinted)
        {
            var changed = FilterName != filterName ||
                          FilterTypeIndex != filterTypeIndex ||
                          FilterType != filterType ||
                          FilterRarity != filterRarity ||
                          FilterMinted != filterMinted;

            if (!changed)
            {
                return false;
            }

            FilterName = filterName;
            FilterTypeIndex = filterTypeIndex;
            FilterType = filterType;
            FilterRarity = filterRarity;
            FilterMinted = filterMinted;

            ResetPagination();
            return true;
        }

        public void ApplyPagination(int totalCount, int pageCount, int pageNumber)
        {
            TotalCount = totalCount;
            PageCount = pageCount;
            PageNumber = ClampPageNumber(pageNumber);
        }

        public void GoToFirstPage()
        {
            PageNumber = 0;
        }

        public void GoToPreviousPage()
        {
            PageNumber = ClampPageNumber(PageNumber - 1);
        }

        public void GoToNextPage()
        {
            PageNumber = ClampPageNumber(PageNumber + 1);
        }

        public void GoToLastPage()
        {
            PageNumber = PageCount > 0 ? PageCount - 1 : 0;
        }

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

        private int ClampPageNumber(int pageNumber)
        {
            if (PageCount <= 0)
            {
                return 0;
            }

            if (pageNumber < 0)
            {
                return 0;
            }

            if (pageNumber >= PageCount)
            {
                return PageCount - 1;
            }

            return pageNumber;
        }
    }
}
