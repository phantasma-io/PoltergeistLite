using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using UnityEngine;
using Vector2 = UnityEngine.Vector2;
using Poltergeist.Wallet;

namespace Poltergeist
{
    public partial class WalletGUI
    {
        private sealed class BalanceViewRenderer
        {
            private readonly WalletGUI gui;

            public BalanceViewRenderer(WalletGUI gui)
            {
                this.gui = gui;
            }

            public void Render(WalletBalanceViewSnapshot model, ref float scrollY, int startY, int endY)
            {
                var scroll = new Vector2(0, scrollY);

                if (model.IsRefreshing)
                {
                    gui.DrawCenteredText("Fetching balances...");
                    scrollY = scroll.y;
                    return;
                }

                if (model.HasError)
                {
                    gui.DrawCenteredText(model.ErrorMessage);
                    scrollY = scroll.y;
                    return;
                }

                if (model.Balances == null || model.Balances.Count == 0)
                {
                    gui.DrawCenteredText($"No assets found in this {AccountManager.Instance.CurrentPlatform} account.");
                    return;
                }

                var minBalanceSetting = AccountManager.Instance.Settings.balanceDisplayThreshold;

                var items = model.Balances.Where(x =>
                {
                    var threshold = BigInteger.Zero;
                    if (minBalanceSetting > 0)
                    {
                        var thresholdText = minBalanceSetting.ToString(CultureInfo.InvariantCulture);
                        if (!WalletAmountParser.TryParse(thresholdText, x.Decimals, out threshold))
                        {
                            threshold = BigInteger.Zero;
                        }
                    }

                    var positive = x.Total > BigInteger.Zero;
                    var meetsThreshold = threshold == BigInteger.Zero ? positive : x.Total >= threshold;
                    return meetsThreshold;
                });
                var count = gui.DoScrollArea(ref scroll, startY, endY, gui.VerticalLayout ? Units(7) : Units(6), items, gui.DoBalanceEntry);
                scrollY = scroll.y;

                if (count == 0)
                {
                    gui.DrawCenteredText($"No assets found in this {AccountManager.Instance.CurrentPlatform} account.");
                }
            }
        }

        private sealed class HistoryViewRenderer
        {
            private readonly WalletGUI gui;

            public HistoryViewRenderer(WalletGUI gui)
            {
                this.gui = gui;
            }

            public void Render(WalletHistoryViewSnapshot model, ref float scrollY, int startY, int endY)
            {
                var scroll = new Vector2(0, scrollY);

                if (model.IsRefreshing)
                {
                    gui.DrawCenteredText("Fetching history...");
                    scrollY = scroll.y;
                    return;
                }

                if (model.HasError)
                {
                    gui.DrawCenteredText(model.ErrorMessage);
                    scrollY = scroll.y;
                    return;
                }

                var history = model.Entries;
                var count = gui.DoScrollArea(ref scroll, startY, endY, gui.VerticalLayout ? Units(4) : Units(3), history, gui.DoHistoryEntry);
                scrollY = scroll.y;

                if (count == 0)
                {
                    gui.DrawCenteredText($"No transactions found for this {AccountManager.Instance.CurrentPlatform} account.");
                }
            }
        }

        private sealed class NftListRenderer
        {
            private readonly WalletGUI gui;

            public NftListRenderer(WalletGUI gui)
            {
                this.gui = gui;
            }

            public void Render(WalletNftViewSnapshot nftSnapshot)
            {
                var accountManager = AccountManager.Instance;
                var viewState = gui.nftViewPresenter.State;
                var nfts = accountManager.CurrentNfts;

                if (nftSnapshot.IsRefreshing)
                {
                    var count = nfts?.Count ?? nftSnapshot.TotalCount;
                    gui.DrawCenteredText(count > 0 ? $"Loading NFTs ({count})..." : "Loading NFTs...");
                    return;
                }

                if (nfts == null || nftSnapshot.HasError)
                {
                    gui.DrawCenteredText(nftSnapshot.HasError ? nftSnapshot.ErrorMessage : "Loading...");
                    return;
                }

                var startY = WalletGUI.Units(gui.VerticalLayout ? 11 : 7);
                var nftToolsY = startY;
                startY += (gui.VerticalLayout) ? WalletGUI.Units(6) : WalletGUI.Units(4);

                gui.nftFilteredList = nftSnapshot.FilteredTokens.ToList();
                gui.nftViewPresenter.PruneSelection(nfts.Select(x => x.Id));
                viewState.ApplyPagination(nftSnapshot.TotalCount, nftSnapshot.PageCount, nftSnapshot.PageNumber);

                var endY = gui.DoBottomMenuForNft();

                var nftOnPageCount = gui.DoScrollArea(ref gui.nftScroll, startY, endY, gui.VerticalLayout ? WalletGUI.Units(5) : WalletGUI.Units(4), nftSnapshot.PageIds,
                    gui.DoNftEntry);

                if (nftOnPageCount == 0)
                {
                    gui.DrawCenteredText($"No {gui.transferSymbol} NFTs found for this {accountManager.CurrentPlatform} account.");
                }

                gui.DrawNftTools(nftToolsY);

                gui.DrawPlatformTopMenu(() =>
                {
                    accountManager.RefreshBalances(false, accountManager.CurrentPlatform);
                    gui.nftViewPresenter.Refresh(gui.transferSymbol, false);
                    gui.nftViewPresenter.ResetSorting();
                    gui.MarkBalancesDirty();
                    gui.MarkNftDirty(gui.transferSymbol);
                }, false);
            }
        }

        private sealed class NftTransferListRenderer
        {
            private readonly WalletGUI gui;

            public NftTransferListRenderer(WalletGUI gui)
            {
                this.gui = gui;
            }

            public void Render()
            {
                var accountManager = AccountManager.Instance;

                var startY = gui.DrawPlatformTopMenu(() =>
                {
                }, false);
                var endY = gui.DoBottomMenuForNftTransferList();

                var selectionSnapshot = gui.nftViewPresenter.SelectionSnapshot();
                var selectionSet = new HashSet<string>(selectionSnapshot);
                var orderedSelection = accountManager.CurrentNfts == null
                    ? selectionSnapshot.ToList()
                    : accountManager.CurrentNfts.Where(x => selectionSet.Contains(x.Id)).Select(x => x.Id).ToList();
                gui.nftViewPresenter.PruneSelection(orderedSelection);

                var nftTransferCount = gui.DoScrollArea(ref gui.nftTransferListScroll, startY, endY, gui.VerticalLayout ? WalletGUI.Units(5) : WalletGUI.Units(4), orderedSelection,
                    gui.DoNftEntry);

                if (nftTransferCount == 0)
                {
                    gui.DrawCenteredText($"No NFTs selected for transfer.");
                }
            }
        }
    }
}
