using System.Collections.Generic;
using PhantasmaPhoenix.RPC.Models;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Immutable view snapshot for NFT list with filtering and pagination metadata.
    /// </summary>
    public sealed class WalletNftViewSnapshot
    {
        public WalletNftViewSnapshot(bool isRefreshing, bool hasError, string errorMessage, int totalCount, int pageNumber, int pageCount, IReadOnlyList<TokenDataResult> filteredTokens, IReadOnlyList<string> pageIds)
        {
            IsRefreshing = isRefreshing;
            HasError = hasError;
            ErrorMessage = errorMessage ?? string.Empty;
            TotalCount = totalCount;
            PageNumber = pageNumber;
            PageCount = pageCount;
            FilteredTokens = filteredTokens ?? new List<TokenDataResult>();
            PageIds = pageIds ?? new List<string>();
        }

        public bool IsRefreshing { get; }
        public bool HasError { get; }
        public string ErrorMessage { get; }
        public int TotalCount { get; }
        public int PageNumber { get; }
        public int PageCount { get; }
        public IReadOnlyList<TokenDataResult> FilteredTokens { get; }
        public IReadOnlyList<string> PageIds { get; }
    }
}
