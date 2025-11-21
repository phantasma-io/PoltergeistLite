using System;
using System.Linq;
using UnityEngine;
using Poltergeist.Wallet;

namespace Poltergeist
{
    public partial class WalletGUI
    {
        private void RenderModalContent(WalletModalContext ctx)
        {
            float curY = Units(4);

            var rect = new Rect(Units(1), curY, modalRect.width - Units(2), modalRect.height - Units(2));

            var captionHeight = GUI.skin.label.CalcHeight(new GUIContent(ctx.Caption), rect.width);

            // Calculating, how much space caption can occupy vertically.
            // Subtracting space for buttons: Units(8).
            float captionAvailableHeight = (int)rect.height - Units(8);

            if (ctx.State == ModalState.Input || ctx.State == ModalState.Password)
            {
                // Subtracting space for input field: Units(2) * ctx.MaxLines + Units(2).
                captionAvailableHeight -= Units(2) * ctx.MaxLines + Units(2);
            }

            var captionDisplayedHeight = Math.Min(captionAvailableHeight, captionHeight);

            int captionWidth = (int)rect.width;

            var insideRect = new Rect(0, 0, captionWidth, captionHeight);
            var outsideRect = new Rect(rect.x, curY, captionWidth, captionDisplayedHeight);

            bool needsScroll = insideRect.height > outsideRect.height;
            if (needsScroll)
            {
                captionWidth -= Border;
                insideRect.width = captionWidth;
            }

            ctx.CaptionScroll = GUI.BeginScrollView(outsideRect, ctx.CaptionScroll, insideRect);

            GUI.Label(insideRect, ctx.Caption);

            GUI.EndScrollView();

            if (ctx.PromptPicture != null)
            {
                GUI.DrawTexture(new Rect(16, curY - 32, 32, 32), ctx.PromptPicture, ScaleMode.ScaleToFit, true);
            }

            curY += Units(2);

            var fieldWidth = rect.width;

            bool hasHints = ctx.Hints != null && ctx.Hints.Count > 0;
            int hintWidth = Units(10);

            if (hasHints && !VerticalLayout)
            {
                fieldWidth -= hintWidth + Units(1);
            }

            curY += captionDisplayedHeight;

            float hintY = VerticalLayout ? curY + Units(2) : curY;

            var enterPressed = false;
            var escapePressed = false;
            var e = UnityEngine.Event.current;
            if (e.type == EventType.KeyUp && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                enterPressed = true;
            }
            else if (e.type == EventType.KeyUp && e.keyCode == KeyCode.Escape)
            {
                escapePressed = true;
            }

            if (ctx.State == ModalState.Input)
            {
                if (ctx.MaxLines > 1)
                {
                    GUI.SetNextControlName("PoltergeistModalTextArea");
                    ctx.Input = GUI.TextArea(new Rect(rect.x, curY, fieldWidth, Units(2) * ctx.MaxLines), ctx.Input, ctx.MaxInputLength);
                    GUI.FocusControl("PoltergeistModalTextArea");
                }
                else
                {
                    GUI.SetNextControlName("PoltergeistModalTextField");
                    ctx.Input = GUI.TextField(new Rect(rect.x, curY, fieldWidth, Units(2)), ctx.Input, ctx.MaxInputLength);
                    GUI.FocusControl("PoltergeistModalTextField");
                }
            }
            else if (ctx.State == ModalState.Password)
            {
                GUI.SetNextControlName("PoltergeistModalPasswordField");
                ctx.Input = GUI.PasswordField(new Rect(rect.x, curY, fieldWidth, Units(2)), ctx.Input, '*', ctx.MaxInputLength);
                GUI.FocusControl("PoltergeistModalPasswordField");
            }

            int btnWidth = VerticalLayout ? (Units(7) + 8) : Units(11);

            curY = (int)(rect.height - Units(2));

            if (ctx.Options == ModalHexWifCancel)
            {
                int thirdOfWidth = (int)(modalRect.width / 3);

                DoButton(true,
                    escapePressed,
                    new Rect((thirdOfWidth - btnWidth) / 2, curY, btnWidth, Units(2)), ctx.Options[2], () =>
                    {
                        ctx.Result = PromptResult.Custom_3;
                    });

                DoButton(true,
                    false,
                    new Rect(thirdOfWidth + (thirdOfWidth - btnWidth) / 2, curY, btnWidth, Units(2)), ctx.Options[1], () =>
                    {
                        ctx.Result = PromptResult.Custom_2;
                    });

                DoButton(true,
                    false,
                    new Rect(thirdOfWidth * 2 + (thirdOfWidth - btnWidth) / 2, curY, btnWidth, Units(2)), ctx.Options[0], () =>
                    {
                        ctx.Result = PromptResult.Custom_1;
                    });
            }
            else if (ctx.Options.Length > 1)
            {
                int halfWidth = (int)(modalRect.width / 2);

                var isCopyOption = ctx.Options == ModalOkCopy || ctx.Options == ModalOkCopy_NoAutoCopy;

                DoButton((!hasHints || !hintComboBox.DropDownIsOpened()),
                    escapePressed && (ctx.Options == ModalConfirmCancel || ctx.Options == ModalSendCancel || ctx.Options == ModalYesNo),
                    new Rect((halfWidth - btnWidth) / 2, curY, btnWidth, Units(2)), ctx.Options[1], () =>
                {
                    if (isCopyOption)
                    {
                        if (ctx.OnCopy != null)
                        {
                            ctx.OnCopy();
                        }
                        else if (ctx.Options == ModalOkCopy)
                        {
                            var caption = ctx.Caption;
                            if (caption.Contains("The account was migrated."))
                            {
                                caption = caption.Split(":")[1];
                            }

                            GUIUtility.systemCopyBuffer = caption;
                        }

                        if (ctx.CloseOnCopy)
                        {
                            ctx.Result = PromptResult.Failure;
                        }

                        return;
                    }

                    if (ctx.Options == ModalOkView)
                    {
                        ctx.Result = PromptResult.Failure;
                    }
                    else
                    {
                        ctx.Result = PromptResult.Failure;
                    }
                });

                DoButton((!hasHints || !hintComboBox.DropDownIsOpened()) && Time.time - ctx.Time >= ctx.ConfirmDelay && ((ctx.State != ModalState.Input && ctx.State != ModalState.Password) || ctx.Input.Length >= ctx.MinInputLength),
                    enterPressed && (ctx.Options == ModalOkCopy || ctx.Options == ModalOkView || ctx.Options == ModalConfirmCancel || ctx.Options == ModalSendCancel || ctx.Options == ModalYesNo),
                    new Rect(halfWidth + (halfWidth - btnWidth) / 2, curY, btnWidth, Units(2)), (ctx.ConfirmDelay > 0 && (Time.time - ctx.Time < ctx.ConfirmDelay)) ? ctx.Options[0] + " (" + (ctx.ConfirmDelay - Math.Floor(Time.time - ctx.Time)) + ")" : ctx.Options[0], () =>
                {
                    ctx.Result = PromptResult.Success;
                });
            }
            else if (ctx.Options.Length > 0)
            {
                DoButton(true,
                    (escapePressed || enterPressed) && ctx.Options == ModalOk,
                    new Rect((modalRect.width - btnWidth) / 2, curY, btnWidth, Units(2)), ctx.Options[0], () =>
                {
                    ctx.Result = PromptResult.Success;
                });
            }

            if (hasHints)
            {
                curY = hintY;

                int dropHeight;
                var hintList = ctx.Hints.Keys.ToList();

                var prevHint = hintComboBox.SelectedItemIndex;
                var hintIndex = hintComboBox.Show(new Rect(rect.width - hintWidth + 8, curY, hintWidth, Units(2)), hintList, (int)(modalRect.height - (curY + Units(2)) - Border), out dropHeight, ctx.HintsLabel);
                if (prevHint != hintIndex && hintIndex >= 0)
                {
                    var key = hintList[hintIndex];
                    if (ctx.Hints.ContainsKey(key))
                    {
                        var temp = ctx.Hints[key];
                        if (temp.StartsWith("|"))
                        {
                            temp = temp.Substring(1);
                            GUIState state;

                            if (Enum.TryParse(temp, out state))
                            {
                                ctx.Redirected = true;
                                PushState(state);
                            }
                            else
                            {
                                MessageBox(MessageKind.Error, "Internal error decoding hint redirection.\nContact the developers.");
                            }
                        }
                        else
                        {
                            ctx.Input = temp;
                            ctx.InputKey = key;
                        }
                    }
                }
            }
        }
    }
}
