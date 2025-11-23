using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Poltergeist
{
    public partial class WalletGUI : MonoBehaviour
    {
        private void DoButton(bool enabled, Rect rect, string text, Action callback)
        {
            var temp = GUI.enabled;
            GUI.enabled = enabled;

            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button)
            {
                padding = new RectOffset(2, 2, 1, 1)
            };

            if (GUI.Button(rect, text, buttonStyle))
            {
                if (currentAnimation == AnimationDirection.None)
                {
                    callback();
                }
            }
            GUI.enabled = temp;
        }
        private void DoButton(bool enabled, bool pressed, Rect rect, string text, Action callback)
        {
            if (enabled && pressed)
            {
                callback();
            }
            else
            {
                var temp = GUI.enabled;
                GUI.enabled = enabled;

                GUIStyle buttonStyle = new GUIStyle(GUI.skin.button)
                {
                    padding = new RectOffset(2, 2, 1, 1)
                };

                if (GUI.Button(rect, text, buttonStyle))
                {
                    if (currentAnimation == AnimationDirection.None)
                    {
                        callback();
                    }
                }
                GUI.enabled = temp;
            }
        }
        private void DoModalWindow(int windowID)
        {
            RenderModalContent(modalContext);
        }

        // returns total items
        private int DoScrollArea<T>(ref Vector2 scroll, int startY, int endY, int panelHeight, IEnumerable<T> items, Action<T, int, int, Rect> callback)
        {
            int panelWidth = (int)(windowRect.width - (Border * 2));

            var itemCount = items != null ? items.Count() : 0;
            var insideRect = new Rect(0, 0, panelWidth, Border + ((panelHeight + Border) * itemCount));
            var outsideRect = new Rect(Border, startY, panelWidth, endY - (startY + Border));

            bool needsScroll = insideRect.height > outsideRect.height;
            if (needsScroll)
            {
                panelWidth -= Border;
                insideRect.width = panelWidth;
            }

            int curY = Border;

            int i = 0;
            scroll = GUI.BeginScrollView(outsideRect, scroll, insideRect);
            if (items != null)
            {
                foreach (var item in items)
                {
                    var rect = new Rect(0, curY, insideRect.width, panelHeight);
                    GUI.Box(rect, "");

                    callback(item, i, curY, rect);

                    curY += panelHeight;
                    curY += Border;
                    i++;
                }
            }
            GUI.EndScrollView();

            return i;
        }

        private void DoButtonGrid<T>(bool showBackground, int buttonCount, int xOffset, int yOffset, out int posY, Func<int, MenuEntry> options, Action<T> callback)
        {
            var border = Units(1);

            int panelHeight = VerticalLayout ? Border * 2 + (Units(2) + 4) * buttonCount : (border + Units(3));
            posY = (int)((windowRect.y + windowRect.height) - (panelHeight + border)) + yOffset;

            var rect = new Rect(border, posY, windowRect.width - border * 2, panelHeight);

            if (showBackground)
            {
                GUI.Box(rect, "");
            }

            int divisionWidth = (int)((windowRect.width - xOffset * 2) / buttonCount);
            int btnWidth = (int)(divisionWidth * 0.8f);

            int maxBtnWidth = Units(8 + buttonCount * 2);
            if (btnWidth > maxBtnWidth)
            {
                btnWidth = maxBtnWidth;
            }

            int padding = (divisionWidth - btnWidth) / 2;

            T selected = default(T);
            bool hasSelection = false;

            for (int i = 0; i < buttonCount; i++)
            {
                var entry = options(i);

                Rect btnRect;

                if (VerticalLayout)
                {
                    btnRect = new Rect(rect.x + border * 2, rect.y + border + i * (Units(2) + 4), rect.width - border * 4, Units(2));
                }
                else
                {
                    btnRect = new Rect(divisionWidth * i + (divisionWidth - btnWidth) / 2 + xOffset, rect.y + border, btnWidth, Units(2));
                }

                DoButton(entry.enabled, btnRect, entry.label, () =>
                {
                    hasSelection = true;
                    selected = (T)entry.value;
                });
            }

            if (hasSelection)
            {
                callback(selected);
            }
        }

        // Methods for creating of NFT tools for toolbar over NFT list - used to create sort/filters combos, select/invert buttons etc.
        private int toolLabelWidth = Units(4) + 8;
        private int toolLabelHeight = Units(2);
        private int toolFieldWidth => (VerticalLayout) ? Units(7) : Units(9);
        private int toolFieldHeight = Units(1);
        private int toolFieldSpacing = Units(1);

        private void DoNftToolLabel(int posX, int posY, string label)
        {
            var style = GUI.skin.label;
            style.fontSize -= 6;
            GUI.Label(new Rect(posX, posY - 10, toolLabelWidth, toolLabelHeight), label);
            style.fontSize += 6;
        }

        private void DoNftToolTextField(int posX, int posY, string label, ref string result)
        {
            DoNftToolLabel(posX, posY, label);

            var style = GUI.skin.textField;
            style.fontSize -= 4;
            result = GUI.TextField(new Rect(posX + toolLabelWidth - 6, posY - 4, toolFieldWidth + 7, toolFieldHeight + 8), result);
            style.fontSize += 4;
        }

        private void DoNftToolComboBox<T>(int posX, int posY, ComboBox comboBox, IList<T> listContent, string label, ref int result)
        {
            DoNftToolLabel(posX, posY, label);

            comboBox.SelectedItemIndex = result;
            int dropHeight;
            result = comboBox.Show(new Rect(posX + toolLabelWidth, posY, toolFieldWidth, toolFieldHeight), listContent, 0, out dropHeight);
        }

        private void DoNftToolButton(int posX, int posY, int width, string label, Action callback)
        {
            var style = GUI.skin.button;
            style.fontSize -= 4;
            DoButton(true, new Rect(posX, posY, width, toolFieldHeight), label, callback);
            style.fontSize += 4;
        }

        private void DrawCenteredText(string caption)
        {
            var style = GUI.skin.label;
            var temp = style.alignment;
            style.alignment = TextAnchor.MiddleCenter;

            GUI.Label(new Rect(0, 0, windowRect.width, windowRect.height), caption);

            style.alignment = temp;
        }

        private void DrawHorizontalCenteredText(int curY, float height, string caption)
        {
            var style = GUI.skin.label;
            var tempAlign = style.alignment;
            var tempRichText = style.richText;

            style.fontSize -= VerticalLayout ? 2 : 4;
            style.alignment = TextAnchor.MiddleCenter;
            style.richText = true;

            GUI.Label(new Rect(0, curY, windowRect.width, height), caption);

            style.fontSize += VerticalLayout ? 2 : 4;
            style.alignment = tempAlign;
            style.richText = tempRichText;
        }

        private void DoBackButton()
        {
            int posY;
            DoButtonGrid<bool>(false, 1, 0, Border, out posY, (index) =>
            {
                return new MenuEntry(true, "Back", true);
            }, (val) =>
            {
                PopState();
            });
        }
        private void DrawDropshadow(Rect rect)
        {
            float percent = 1 / 8f;
            var padX = rect.width * percent;
            var padY = rect.height * percent;
            var dropRect = new Rect(rect.x - padX, rect.y - padY, rect.width + padX * 2, rect.height + padY * 2);
            GUI.DrawTexture(dropRect, ResourceManager.Instance.Dropshadow);
        }
    }

}
