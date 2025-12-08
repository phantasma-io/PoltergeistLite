using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Poltergeist.UiToolkit
{
    /// <summary>
    /// Simple form row factories: label above, field below. Minimal styling to avoid layout drift.
    /// </summary>
    public static class WalletUiFormFactory
    {
        public static VisualElement CreateFormSection(string title)
        {
            var section = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    width = new Length(100, LengthUnit.Percent),
                    marginTop = 16,
                    marginBottom = 16
                }
            };

            if (!string.IsNullOrWhiteSpace(title))
            {
                var label = new Label(title)
                {
                    style =
                    {
                        unityFontStyleAndWeight = FontStyle.Bold,
                        fontSize = 16,
                        color = WalletUiTheme.TextPrimary,
                        marginBottom = 8
                    }
                };
                WalletUiCommon.ApplyDefaultFont(label);
                section.Add(label);
            }

            return section;
        }

        public static VisualElement CreateLabeledRow(string labelText, VisualElement field, string hint = null)
        {
            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    width = new Length(100, LengthUnit.Percent),
                    marginBottom = 12,
                    alignItems = Align.Stretch
                }
            };

            if (!string.IsNullOrWhiteSpace(labelText))
            {
                var label = new Label(labelText)
                {
                    style =
                    {
                        color = WalletUiTheme.TextPrimary,
                        fontSize = 14,
                        unityFontStyleAndWeight = FontStyle.Bold,
                        marginBottom = 4
                    }
                };
                WalletUiCommon.ApplyDefaultFont(label);
                row.Add(label);
            }

            if (field != null)
            {
                field.style.marginTop = 0;
                field.style.marginBottom = 0;
                field.style.minHeight = 40;
                row.Add(field);
            }

            if (!string.IsNullOrWhiteSpace(hint))
            {
                var hintLabel = new Label(hint)
                {
                    style =
                    {
                        color = WalletUiTheme.TextSecondary,
                        fontSize = 12,
                        unityTextAlign = TextAnchor.MiddleLeft,
                        marginTop = 4
                    }
                };
                WalletUiCommon.ApplyDefaultFont(hintLabel);
                row.Add(hintLabel);
            }

            return row;
        }

        public static PopupField<string> CreateDropdown(string label, IReadOnlyList<string> options, int selectedIndex, Action<int> onChanged, string hint = null)
        {
            var choices = options == null ? new List<string>() : new List<string>(options);
            var clampedIndex = Mathf.Clamp(selectedIndex, 0, Math.Max(0, (choices.Count == 0 ? 1 : choices.Count) - 1));
            var dropdown = new PopupField<string>(choices, clampedIndex, null, null)
            {
                label = string.Empty,
                style =
                {
                    width = new Length(100, LengthUnit.Percent),
                    marginBottom = 0,
                    minHeight = 40,
                    paddingLeft = 10,
                    paddingRight = 10,
                    paddingTop = 6,
                    paddingBottom = 6,
                    alignItems = Align.Center,
                    justifyContent = Justify.FlexStart,
                    backgroundColor = WalletUiTheme.InputBackground,
                    borderTopLeftRadius = WalletUiTheme.RadiusSmall,
                    borderTopRightRadius = WalletUiTheme.RadiusSmall,
                    borderBottomLeftRadius = WalletUiTheme.RadiusSmall,
                    borderBottomRightRadius = WalletUiTheme.RadiusSmall,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftColor = WalletUiTheme.InputBorder,
                    borderRightColor = WalletUiTheme.InputBorder,
                    borderTopColor = WalletUiTheme.HighlightEdge,
                    borderBottomColor = WalletUiTheme.InputBorder,
                    color = WalletUiTheme.TextPrimary,
                    unityTextAlign = TextAnchor.MiddleLeft
                }
            };
            WalletUiCommon.ApplyDefaultFont(dropdown);
            HideFieldLabel(dropdown);
            dropdown.RegisterValueChangedCallback(_ =>
            {
                var idx = dropdown.index;
                onChanged?.Invoke(idx);
            });
            dropdown.pickingMode = PickingMode.Position;
            // Important: disable UITK's built-in popup (was throwing "Could not find rootVisualContainer..." and sometimes not rendering)
            // and use our own anchored menu instead. We swallow pointer events to prevent the default popup path.
            dropdown.RegisterCallback<PointerDownEvent>(evt =>
            {
                // Swallow default dropdown popup and use our anchored menu instead.
                dropdown?.panel?.focusController?.IgnoreEvent(evt);
                evt.StopImmediatePropagation();
                ShowDropdownMenu(dropdown, onChanged);
            }, TrickleDown.TrickleDown);
            dropdown.RegisterCallback<PointerUpEvent>(evt =>
            {
                dropdown?.panel?.focusController?.IgnoreEvent(evt);
                evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);

            if (!string.IsNullOrWhiteSpace(hint))
            {
                dropdown.tooltip = hint;
            }

            return dropdown;
        }

        public static TextField CreateTextField(string label, string value, Action<string> onChanged, bool multiline = false, string hint = null)
        {
            var field = new TextField(label ?? string.Empty)
            {
                value = value ?? string.Empty,
                multiline = multiline,
                style =
                {
                    width = new Length(100, LengthUnit.Percent),
                    marginBottom = 0,
                    minHeight = multiline ? 52 : 40,
                    paddingLeft = 10,
                    paddingRight = 10,
                    paddingTop = multiline ? 10 : 6,
                    paddingBottom = multiline ? 10 : 6,
                    alignItems = Align.Center,
                    justifyContent = Justify.FlexStart,
                    backgroundColor = WalletUiTheme.InputBackground,
                    borderTopLeftRadius = WalletUiTheme.RadiusSmall,
                    borderTopRightRadius = WalletUiTheme.RadiusSmall,
                    borderBottomLeftRadius = WalletUiTheme.RadiusSmall,
                    borderBottomRightRadius = WalletUiTheme.RadiusSmall,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftColor = WalletUiTheme.InputBorder,
                    borderRightColor = WalletUiTheme.InputBorder,
                    borderTopColor = WalletUiTheme.HighlightEdge,
                    borderBottomColor = WalletUiTheme.InputBorder,
                    color = WalletUiTheme.TextPrimary,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    flexGrow = 1,
                    alignSelf = Align.Stretch
                }
            };
            field.label = string.Empty;
            WalletUiCommon.ApplyDefaultFont(field);
            HideFieldLabel(field);
            var input = field.Q<VisualElement>("unity-text-input");
            if (input != null)
            {
                input.style.marginTop = 4;
                input.style.marginBottom = 4;
                input.style.marginLeft = 0;
                input.style.marginRight = 0;
                input.style.unityTextAlign = TextAnchor.MiddleLeft;
                input.style.color = WalletUiTheme.TextPrimary;
                input.style.flexGrow = 1;
                input.style.alignSelf = Align.Stretch;
                input.style.minHeight = 18;
                input.style.justifyContent = Justify.Center;
                input.style.alignItems = Align.Center;
            }
            field.RegisterValueChangedCallback(evt => onChanged?.Invoke(evt.newValue));
            if (!string.IsNullOrWhiteSpace(hint))
            {
                field.tooltip = hint;
            }
            return field;
        }

        public static Toggle CreateToggle(string label, bool value, Action<bool> onChanged, string hint = null)
        {
            var toggle = new Toggle(string.Empty)
            {
                value = value,
                style =
                {
                    marginBottom = 8,
                    fontSize = 14,
                    color = WalletUiTheme.TextPrimary,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    minHeight = 28
                }
            };
            WalletUiCommon.ApplyDefaultFont(toggle);
            StyleToggleVisual(toggle);

            // UITK ignores margin/padding on the built-in label for Toggle, so we inject our own
            // label element to get predictable spacing without fighting the internal layout.
            var customLabel = new Label(label ?? string.Empty)
            {
                style =
                {
                    marginLeft = 10,
                    paddingLeft = 0,
                    color = WalletUiTheme.TextPrimary,
                    fontSize = 14,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    flexGrow = 0,
                    flexShrink = 1
                }
            };
            WalletUiCommon.ApplyDefaultFont(customLabel);
            customLabel.RegisterCallback<ClickEvent>(_ => toggle.value = !toggle.value);
            toggle.Add(customLabel);

            toggle.RegisterValueChangedCallback(evt => onChanged?.Invoke(evt.newValue));
            if (!string.IsNullOrWhiteSpace(hint))
            {
                toggle.tooltip = hint;
            }

            return toggle;
        }

        private static void StyleToggleVisual(Toggle toggle)
        {
            if (toggle == null)
            {
                return;
            }

            toggle.style.flexDirection = FlexDirection.Row;
            toggle.style.alignItems = Align.Center;
            toggle.style.justifyContent = Justify.FlexStart;
            toggle.style.unityTextAlign = TextAnchor.MiddleLeft;
            toggle.style.width = new StyleLength(StyleKeyword.Auto);
            toggle.style.maxWidth = new StyleLength(StyleKeyword.Auto);
            toggle.style.minWidth = 0;
            toggle.style.alignSelf = Align.FlexStart;
            toggle.style.flexGrow = 0;
            toggle.style.flexShrink = 0;
            var nativeLabel = toggle.labelElement;
            if (nativeLabel != null)
            {
                // We disable the native label because UITK does not honor margins there,
                // which makes alignment brittle across editor/runtime builds.
                nativeLabel.style.display = DisplayStyle.None;
                nativeLabel.style.width = 0;
                nativeLabel.style.height = 0;
            }

            var input = toggle.Q<VisualElement>(className: "unity-toggle__input") ??
                        toggle.Q<VisualElement>("unity-toggle__input") ??
                        toggle.Q<VisualElement>(className: "unity-base-field__input");
            if (input != null)
            {
                input.style.width = 18;
                input.style.height = 18;
                input.style.marginRight = 10;
                input.style.borderLeftWidth = 1;
                input.style.borderRightWidth = 1;
                input.style.borderTopWidth = 1;
                input.style.borderBottomWidth = 1;
                input.style.borderLeftColor = WalletUiTheme.CardBorder;
                input.style.borderRightColor = WalletUiTheme.CardBorder;
                input.style.borderTopColor = WalletUiTheme.HighlightEdge;
                input.style.borderBottomColor = WalletUiTheme.CardBorder;
                input.style.backgroundColor = WalletUiTheme.PanelBackground;
                input.style.justifyContent = Justify.Center;
                input.style.alignItems = Align.Center;
                input.style.flexShrink = 0;
            }

            var check = toggle.Q<VisualElement>(className: "unity-checkmark") ??
                        toggle.Q<VisualElement>("unity-checkmark");

            void UpdateCheck(bool isOn)
            {
                if (check == null)
                {
                    return;
                }

                check.style.width = 10;
                check.style.height = 10;
                check.style.backgroundColor = isOn ? WalletUiTheme.AccentPrimary : Color.clear;
                check.style.visibility = isOn ? Visibility.Visible : Visibility.Hidden;
            }

            UpdateCheck(toggle.value);
            toggle.RegisterValueChangedCallback(evt => UpdateCheck(evt.newValue));
        }

        private static void HideFieldLabel(BaseField<string> field)
        {
            if (field == null)
            {
                return;
            }

            var labelElement = field.labelElement;
            if (labelElement != null)
            {
                labelElement.style.display = DisplayStyle.None;
                labelElement.style.marginLeft = 0;
                labelElement.style.marginRight = 0;
                labelElement.style.marginTop = 0;
                labelElement.style.marginBottom = 0;
            }
        }

        private static VisualElement activeDropdownOverlay;
        private static PopupField<string> activeDropdownField;

        private static void ShowDropdownMenu(PopupField<string> dropdown, Action<int> onChanged)
        {
            if (dropdown == null || dropdown.panel == null)
            {
                return;
            }

            CloseDropdownMenu();

            var root = dropdown.panel.visualTree;
            if (root == null)
            {
                return;
            }

            var overlay = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    left = 0,
                    top = 0,
                    right = 0,
                    bottom = 0,
                    backgroundColor = Color.clear
                }
            };
            overlay.pickingMode = PickingMode.Position;
            overlay.AddManipulator(new Clickable(() => CloseDropdownMenu()));

            var menu = new VisualElement
            {
                style =
                {
                    position = Position.Absolute,
                    flexDirection = FlexDirection.Column,
                    backgroundColor = WalletUiTheme.PanelBackground,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftColor = WalletUiTheme.InputBorder,
                    borderRightColor = WalletUiTheme.InputBorder,
                    borderTopColor = WalletUiTheme.HighlightEdge,
                    borderBottomColor = WalletUiTheme.InputBorder,
                    borderTopLeftRadius = WalletUiTheme.RadiusSmall,
                    borderTopRightRadius = WalletUiTheme.RadiusSmall,
                    borderBottomLeftRadius = WalletUiTheme.RadiusSmall,
                    borderBottomRightRadius = WalletUiTheme.RadiusSmall,
                    paddingTop = 2,
                    paddingBottom = 2,
                    paddingLeft = 2,
                    paddingRight = 2,
                    maxHeight = 240,
                    overflow = Overflow.Hidden
                }
            };
            WalletUiCommon.ApplyDefaultFont(menu);
            menu.pickingMode = PickingMode.Position;

            var scroll = new ScrollView
            {
                style =
                {
                    flexGrow = 1,
                    maxHeight = 236,
                    backgroundColor = Color.clear
                }
            };
            WalletUiCommon.ApplyDefaultFont(scroll);
            scroll.pickingMode = PickingMode.Position;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;

            var choices = dropdown.choices ?? new List<string>();
            if (choices.Count == 0)
            {
                var empty = new Label("No options")
                {
                    style =
                    {
                        paddingLeft = 8,
                        paddingRight = 8,
                        paddingTop = 6,
                        paddingBottom = 6,
                        color = WalletUiTheme.TextSecondary
                    }
                };
                WalletUiCommon.ApplyDefaultFont(empty);
                scroll.Add(empty);
            }
            else
            {
                for (var i = 0; i < choices.Count; i++)
                {
                    var capturedIndex = i;
                    var choice = choices[i] ?? string.Empty;
                    var btn = new Button(() =>
                    {
                        dropdown.index = capturedIndex;
                        onChanged?.Invoke(capturedIndex);
                        CloseDropdownMenu();
                    })
                    {
                        text = choice,
                        style =
                        {
                            height = 30,
                            justifyContent = Justify.FlexStart,
                            paddingLeft = 10,
                            paddingRight = 10,
                            paddingTop = 4,
                            paddingBottom = 4,
                            unityTextAlign = TextAnchor.MiddleLeft,
                            color = WalletUiTheme.TextPrimary,
                            backgroundColor = capturedIndex == dropdown.index ? WalletUiTheme.CardBackground : WalletUiTheme.PanelBackground
                        }
                    };
                    WalletUiCommon.ApplyDefaultFont(btn);
                    scroll.Add(btn);
                }
            }

            menu.Add(scroll);
            overlay.Add(menu);
            root.Add(overlay);

            var worldBound = dropdown.worldBound;
            var rootBound = root.worldBound;
            menu.style.left = worldBound.xMin - rootBound.xMin;
            menu.style.top = worldBound.yMax - rootBound.yMin + 2;
            menu.style.width = worldBound.width;

            activeDropdownOverlay = overlay;
            activeDropdownField = dropdown;
        }

        private static void CloseDropdownMenu()
        {
            if (activeDropdownOverlay != null)
            {
                var parent = activeDropdownOverlay.parent;
                if (parent != null)
                {
                    parent.Remove(activeDropdownOverlay);
                }
                activeDropdownOverlay = null;
            }
            activeDropdownField = null;
        }
    }
}
