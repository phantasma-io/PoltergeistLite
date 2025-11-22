using System.Linq;
using UnityEngine;
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

            public void Render(WalletBalanceViewSnapshot model, ref Vector2 scroll, int startY, int endY)
            {
                if (model.IsRefreshing)
                {
                    gui.DrawCenteredText("Fetching balances...");
                    return;
                }

                if (model.HasError)
                {
                    gui.DrawCenteredText(model.ErrorMessage);
                    return;
                }

                if (model.Balances == null || model.Balances.Count == 0)
                {
                    gui.DrawCenteredText($"No assets found in this {AccountManager.Instance.CurrentPlatform} account.");
                    return;
                }

                var items = model.Balances.Where(x => x.Total >= 0.001m);
                var count = gui.DoScrollArea(ref scroll, startY, endY, gui.VerticalLayout ? Units(7) : Units(6), items, gui.DoBalanceEntry);

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

            public void Render(WalletHistoryViewSnapshot model, ref Vector2 scroll, int startY, int endY)
            {
                if (model.IsRefreshing)
                {
                    gui.DrawCenteredText("Fetching history...");
                    return;
                }

                if (model.HasError)
                {
                    gui.DrawCenteredText(model.ErrorMessage);
                    return;
                }

                var history = model.Entries;
                var count = gui.DoScrollArea(ref scroll, startY, endY, gui.VerticalLayout ? Units(4) : Units(3), history, gui.DoHistoryEntry);

                if (count == 0)
                {
                    gui.DrawCenteredText($"No transactions found for this {AccountManager.Instance.CurrentPlatform} account.");
                }
            }
        }
    }
}
