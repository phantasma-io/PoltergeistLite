using System.Collections.Generic;
using System.Linq;

namespace Poltergeist.Wallet
{
    /// <summary>
    /// Mutable view state for NFT filters, pagination and selection shared across UI layers.
    /// </summary>
    public sealed class WalletNftViewState
    {
        private readonly HashSet<string> _selectedIds = new HashSet<string>();

        public string FilterName = string.Empty;
        public int FilterTypeIndex = 0;
        public string FilterType = "All";
        public int FilterRarity = 0;
        public int FilterMinted = 0;

        public int PageSize = 25;
        public int PageNumber = 0;
        public int PageCount = 0;
        public int TotalCount = 0;

        public IReadOnlyCollection<string> SelectedIds => _selectedIds;
        public int SelectedCount => _selectedIds.Count;

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

        public void ClearSelection()
        {
            _selectedIds.Clear();
        }

        public void Select(IEnumerable<string> ids)
        {
            if (ids == null)
            {
                return;
            }

            foreach (var id in ids)
            {
                if (!string.IsNullOrEmpty(id))
                {
                    _selectedIds.Add(id);
                }
            }
        }

        public void InvertSelection(IEnumerable<string> ids)
        {
            if (ids == null)
            {
                return;
            }

            foreach (var id in ids)
            {
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                if (!_selectedIds.Add(id))
                {
                    _selectedIds.Remove(id);
                }
            }
        }

        public bool ToggleSelection(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            if (_selectedIds.Contains(id))
            {
                _selectedIds.Remove(id);
                return false;
            }

            _selectedIds.Add(id);
            return true;
        }

        public bool IsSelected(string id)
        {
            return !string.IsNullOrEmpty(id) && _selectedIds.Contains(id);
        }

        public void PruneSelection(IEnumerable<string> validIds)
        {
            if (validIds == null)
            {
                ClearSelection();
                return;
            }

            var valid = new HashSet<string>(validIds.Where(x => !string.IsNullOrEmpty(x)));
            _selectedIds.RemoveWhere(x => !valid.Contains(x));
        }

        public IReadOnlyList<string> SelectionSnapshot()
        {
            return _selectedIds.ToList();
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
