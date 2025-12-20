using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using PhantasmaPhoenix.RPC.Models;
using PhantasmaPhoenix.NFT.Extensions;
using Poltergeist.Wallet;

namespace Poltergeist.UiToolkit.Balances
{
    /// <summary>
    /// Detail view slice of the NFT dashboard extracted to keep the main file lean.
    /// </summary>
    public sealed partial class WalletNftDashboardView
    {
        private const float CompactDetailWidth = 1300f;
        private const int AttributeMonolithicMax = 56;
        private const int AttributeMonolithicCompactMax = 36;
        private const int AttributeTextMax = 140;
        private const int AttributeTextCompactMax = 90;

        private VisualElement detailContainer;
        private ScrollView detailScroll;
        private Image detailImage;
        private Label detailTitleLabel;
        private Label detailSubtitleLabel;
        private Label detailLockLabel;
        private VisualElement detailTagContainer;
        private ScrollView detailPropertiesScroll;
        private VisualElement detailActionsRow;
        private Button detailSendButton;
        private Button detailBurnButton;
        private Button detailExplorerButton;
        private Label detailDescriptionLabel;
        private VisualElement detailPropertiesContainer;
        private VisualElement detailFungibleContainer;
        private VisualElement detailFungibleList;
        private VisualElement detailNftContainer;
        private VisualElement detailNftList;

        private VisualElement BuildDetailContainer()
        {
            var container = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    flexGrow = 1
                }
            };
            WalletUiCommon.ApplyDefaultFont(container);

            var scrollWrapper = WalletUiCommon.BuildScrollContainer(
                out detailScroll,
                onScrollChanged: null,
                shouldBlockWheel: () => modalHost?.Overlay != null && modalHost.Overlay.style.display == DisplayStyle.Flex,
                paddingLeft: 4f,
                paddingRight: 4f,
                paddingTop: 0f,
                paddingBottom: 100f,
                marginTop: 0f,
                marginBottom: 0f,
                maxWidth: 1680f,
                alignSelf: Align.Center);
            detailScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            WalletUiCommon.ApplyDefaultFont(detailScroll);

            // IMPORTANT: Without following lines mobile view will be broken.
            var cc = detailScroll.contentContainer;
            cc.style.flexGrow = 0;
            cc.style.flexShrink = 0;

            var hero = CreateDetailHero();
            detailScroll.Add(hero);

            var descriptionCard = CreateDetailCard("Story", out var descriptionBody);
            detailDescriptionLabel = new Label(string.Empty)
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 14,
                    unityTextAlign = TextAnchor.UpperLeft,
                    whiteSpace = WhiteSpace.Normal,
                }
            };
            WalletUiCommon.ApplyDefaultFont(detailDescriptionLabel);
            descriptionBody.Add(detailDescriptionLabel);
            detailScroll.Add(descriptionCard);

            var attributesCard = CreateDetailCard("Attributes", out var attributesBody);
            attributesBody.style.minHeight = 0;
            detailPropertiesScroll = new ScrollView(ScrollViewMode.Vertical)
            {
                style =
                {
                    flexGrow = 1,
                    maxHeight = 320,
                    overflow = Overflow.Hidden
                },
                verticalScrollerVisibility = ScrollerVisibility.Auto,
                horizontalScrollerVisibility = ScrollerVisibility.Auto
            };
            WalletUiCommon.ApplyDefaultFont(detailPropertiesScroll);
            detailPropertiesContainer = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    flexWrap = Wrap.Wrap,
                    marginTop = 2,
                    marginRight = 2,
                }
            };
            WalletUiCommon.ApplyDefaultFont(detailPropertiesContainer);
            detailPropertiesScroll.Add(detailPropertiesContainer);
            attributesBody.Add(detailPropertiesScroll);
            detailScroll.Add(attributesCard);

            detailFungibleContainer = CreateDetailCard("Infused tokens", out detailFungibleList);
            detailFungibleList.style.flexDirection = FlexDirection.Column;
            detailFungibleList.style.marginTop = 2;
            detailScroll.Add(detailFungibleContainer);

            detailNftContainer = CreateDetailCard("Infused NFTs", out detailNftList);
            detailNftList.style.flexDirection = FlexDirection.Column;
            detailNftList.style.marginTop = 2;
            detailScroll.Add(detailNftContainer);

            container.Add(scrollWrapper);
            return container;
        }

        private VisualElement CreateDetailHero()
        {
            var card = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    paddingLeft = 14,
                    paddingRight = 14,
                    paddingTop = 12,
                    paddingBottom = 16,
                    marginBottom = 16,
                }
            };

            WalletUiCommon.ApplyDefaultFont(card);
            WalletUiCommon.ApplyCardStyle(card, WalletUiTheme.GetCardGradientTexture(), WalletUiTheme.RadiusMedium);

            detailImage = new Image
            {
                scaleMode = ScaleMode.ScaleToFit,
                style =
                {
                    maxHeight = 320,
                    marginRight = 16,
                    marginBottom = 0,
                    backgroundColor = WalletUiTheme.PanelBackground,
                    borderTopLeftRadius = WalletUiTheme.RadiusMedium,
                    borderTopRightRadius = WalletUiTheme.RadiusMedium,
                    borderBottomLeftRadius = WalletUiTheme.RadiusMedium,
                    borderBottomRightRadius = WalletUiTheme.RadiusMedium,
                    //overflow = Overflow.Hidden,
                    flexShrink = 0
                }
            };
            card.Add(detailImage);

            var info = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                }
            };
            WalletUiCommon.ApplyDefaultFont(info);

            detailLockLabel = new Label("Infused & locked")
            {
                style =
                {
                    color = WalletUiTheme.AccentPrimary,
                    fontSize = 13,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    display = DisplayStyle.None,
                    marginBottom = 4
                }
            };
            WalletUiCommon.ApplyDefaultFont(detailLockLabel);
            info.Add(detailLockLabel);

            detailTitleLabel = new Label("NFT")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 26,
                    color = WalletUiTheme.TextPrimary,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    whiteSpace = WhiteSpace.Normal,
                    overflow = Overflow.Hidden
                }
            };
            WalletUiCommon.ApplyDefaultFont(detailTitleLabel);
            info.Add(detailTitleLabel);

            detailSubtitleLabel = new Label(string.Empty)
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 14,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    marginTop = 4,
                    whiteSpace = WhiteSpace.Normal,
                    overflow = Overflow.Hidden
                }
            };
            WalletUiCommon.ApplyDefaultFont(detailSubtitleLabel);
            info.Add(detailSubtitleLabel);

            detailTagContainer = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    flexWrap = Wrap.Wrap,
                    marginTop = 8,
                    //minWidth = 0
                }
            };
            WalletUiCommon.ApplyDefaultFont(detailTagContainer);
            info.Add(detailTagContainer);

            detailActionsRow = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    flexWrap = Wrap.Wrap,
                    alignItems = Align.Center,
                    marginTop = 12,
                    minWidth = 0
                }
            };
            WalletUiCommon.ApplyDefaultFont(detailActionsRow);

            detailSendButton = WalletUiCommon.CreateSecondaryButton("Send", () => RunSafeAsync(SendCurrentNftAsync).Forget(ex => PhantasmaPhoenix.Unity.Core.Logging.Log.WriteWarning($"{LogPrefix}Detail send failed: {ex}")), 16, 44);
            detailSendButton.style.minWidth = 150;
            detailSendButton.style.marginRight = 8;

            detailBurnButton = WalletUiCommon.CreateSecondaryButton("Burn", () => RunSafeAsync(BurnCurrentNftAsync).Forget(ex => PhantasmaPhoenix.Unity.Core.Logging.Log.WriteWarning($"{LogPrefix}Detail burn failed: {ex}")), 16, 44);
            detailBurnButton.style.minWidth = 130;
            detailBurnButton.style.marginRight = 8;

            detailExplorerButton = WalletUiCommon.CreateSecondaryButton("Explorer", OpenCurrentNftInExplorer, 16, 44);
            detailExplorerButton.style.minWidth = 120;
            detailExplorerButton.style.marginRight = 8;

            detailActionsRow.Add(detailSendButton);
            detailActionsRow.Add(detailBurnButton);
            detailActionsRow.Add(detailExplorerButton);

            info.Add(detailActionsRow);
            card.Add(info);

            card.RegisterCallback<GeometryChangedEvent>(_ => ApplyDetailHeroLayout(card, info, detailActionsRow));
            ApplyDetailHeroLayout(card, info, detailActionsRow);

            return card;
        }

        private VisualElement CreateDetailCard(string title, out VisualElement body)
        {
            var card = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    paddingLeft = 14,
                    paddingRight = 14,
                    paddingTop = 12,
                    paddingBottom = 12,
                    backgroundColor = WalletUiTheme.PanelBackground,
                    borderTopLeftRadius = WalletUiTheme.RadiusMedium,
                    borderTopRightRadius = WalletUiTheme.RadiusMedium,
                    borderBottomLeftRadius = WalletUiTheme.RadiusMedium,
                    borderBottomRightRadius = WalletUiTheme.RadiusMedium,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftColor = WalletUiTheme.ModalBorder,
                    borderRightColor = WalletUiTheme.ModalBorder,
                    borderTopColor = WalletUiTheme.HighlightEdge,
                    borderBottomColor = WalletUiTheme.ModalBorder,
                    marginBottom = 12
                }
            };
            WalletUiCommon.ApplyDefaultFont(card);

            var header = new Label(title)
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 16,
                    color = WalletUiTheme.TextPrimary,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    marginBottom = 6
                }
            };
            WalletUiCommon.ApplyDefaultFont(header);
            card.Add(header);

            body = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column
                }
            };
            WalletUiCommon.ApplyDefaultFont(body);
            card.Add(body);

            return card;
        }

        private void RenderDetailView(AccountManager accountManager)
        {
            var inspectEntry = context.ViewState.PeekNftInspect();
            if (inspectEntry == null)
            {
                ShowListMode();
                return;
            }

            var symbol = inspectEntry.Value.Symbol;
            var tokenId = inspectEntry.Value.TokenId;
            var locked = inspectEntry.Value.Locked;
            context.ViewState.TransferSymbol = symbol;

            TryFindNft(symbol, tokenId, out var token);
            nftSource.TryGetMetadata(symbol, tokenId, out var metadata);
            var mintDate = metadata.MintDate;
            try
            {
                // ROM timestamps are often the only reliable mint date for legacy metadata.
                if (mintDate == DateTime.MinValue)
                {
                    var rom = nftSource.GetNftRom(symbol, tokenId);
                    mintDate = rom?.GetDate() ?? DateTime.MinValue;
                }
            }
            catch
            {
                mintDate = metadata.MintDate;
            }

            var title = !string.IsNullOrWhiteSpace(metadata.Name) ? metadata.Name : token?.GetPropertyValue("Name");
            detailTitleLabel.text = string.IsNullOrWhiteSpace(title) ? BuildNftTitle(tokenId, metadata) : NormalizeNftName(title);
            detailSubtitleLabel.text = BuildDetailSubtitle(symbol, tokenId, token, mintDate);
            detailLockLabel.style.display = locked ? DisplayStyle.Flex : DisplayStyle.None;
            summaryLabel.text = $"Inspecting {symbol} NFT";

            detailTagContainer?.Clear();
            AddDetailTag($"{symbol} #{FormatId(tokenId, 5)}");
            if (!string.IsNullOrWhiteSpace(token?.Mint) && !string.Equals(token.Mint, "0", StringComparison.OrdinalIgnoreCase))
            {
                AddDetailTag($"Mint #{token.Mint}");
            }

            if (mintDate != DateTime.MinValue)
            {
                AddDetailTag(mintDate.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrWhiteSpace(metadata.Type))
            {
                AddDetailTag(metadata.Type);
            }

            if (metadata.Rarity > 0)
            {
                AddDetailTag($"Rarity {metadata.Rarity}");
            }

            if (!string.IsNullOrWhiteSpace(token?.Series))
            {
                AddDetailTag($"Series {token.Series}");
            }

            SetNftImageAsync(detailImage, symbol, token);
            RenderDetailDescription(metadata, token);
            RenderDetailProperties(symbol, tokenId, token, metadata, mintDate, locked);
            UpdateDetailActions(symbol, locked, accountManager);
            RenderFungibleInfusions(token, accountManager);
            RenderInfusedNfts(token, accountManager);
            if (detailScroll?.verticalScroller != null)
            {
                detailScroll.verticalScroller.value = 0f;
            }
        }

        private void ApplyDetailHeroLayout(VisualElement card, VisualElement info, VisualElement actionsRow)
        {
            var compact = WalletUiCommon.IsCompactWidth(card, CompactDetailWidth);

            if (card != null)
            {
                card.style.flexDirection = compact ? FlexDirection.Column : FlexDirection.Row;
                card.style.alignItems = Align.Stretch;
            }

            if (detailImage != null)
            {
                detailImage.style.width = compact ? new Length(100, LengthUnit.Percent) : 220;
                detailImage.style.height = StyleKeyword.Auto;
                detailImage.style.minHeight = compact ? StyleKeyword.Null : 200;
                detailImage.style.maxHeight = compact ? 360 : 320;
                detailImage.style.maxWidth = StyleKeyword.Null;
                detailImage.style.marginRight = compact ? 0 : 16;
                detailImage.style.marginBottom = compact ? 12 : 0;
                detailImage.style.alignSelf = compact ? Align.Stretch : Align.FlexStart;
            }

            if (info != null)
            {
                info.style.width = compact ? new Length(100, LengthUnit.Percent) : StyleKeyword.Auto;
                info.style.minHeight = compact ? StyleKeyword.Null : 220;
                info.style.minWidth = 0;
                info.style.marginLeft = compact ? 0 : 12;
                info.style.marginTop = compact ? 4 : 0;
            }

            if (actionsRow != null)
            {
                actionsRow.style.flexDirection = FlexDirection.Row;
                actionsRow.style.flexWrap = Wrap.Wrap;
                actionsRow.style.alignItems = compact ? Align.Stretch : Align.Center;
                actionsRow.style.justifyContent = compact ? Justify.SpaceBetween : Justify.FlexStart;
                actionsRow.style.width = compact ? new Length(100, LengthUnit.Percent) : StyleKeyword.Auto;
                actionsRow.style.marginTop = compact ? 12 : 12;
            }

            var buttons = new[] { detailSendButton, detailBurnButton, detailExplorerButton };
            foreach (var btn in buttons)
            {
                if (btn == null)
                {
                    continue;
                }

                btn.style.width = compact ? new Length(48, LengthUnit.Percent) : StyleKeyword.Auto;
                btn.style.alignSelf = compact ? Align.Stretch : Align.Center;
                btn.style.marginRight = compact ? 0 : 8;
                btn.style.marginTop = compact ? 6 : 0;
                btn.style.marginBottom = compact ? 6 : 0;
            }

            if (detailTitleLabel != null)
            {
                detailTitleLabel.style.unityTextAlign = compact ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
            }

            if (detailSubtitleLabel != null)
            {
                detailSubtitleLabel.style.unityTextAlign = compact ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
            }

            if (detailTagContainer != null)
            {
                detailTagContainer.style.justifyContent = compact ? Justify.Center : Justify.FlexStart;
            }
        }

        private void RenderDetailDescription(NftMetadata metadata, TokenDataResult token)
        {
            if (detailDescriptionLabel == null)
            {
                return;
            }

            var description = token?.GetPropertyValue("Description");
            if (string.IsNullOrWhiteSpace(description))
            {
                description = "No description available for this NFT.";
                detailDescriptionLabel.style.color = WalletUiTheme.TextSecondary;
            }
            else
            {
                detailDescriptionLabel.style.color = WalletUiTheme.TextPrimary;
            }

            detailDescriptionLabel.text = description;
        }

        private void RenderDetailProperties(string symbol, string tokenId, TokenDataResult token, NftMetadata metadata, DateTime mintDate, bool locked)
        {
            if (detailPropertiesContainer == null)
            {
                return;
            }

            detailPropertiesContainer.Clear();
            var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddProp(string label, string value, string copyValue = null, string copyTooltip = null, string valueTooltip = null)
            {
                if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                if (added.Contains(label))
                {
                    return;
                }

                detailPropertiesContainer.Add(CreateInfoChip(label, value, copyValue, copyTooltip, valueTooltip));
                added.Add(label);
            }

            var tokenIdDisplay = FormatAttributeValue(tokenId, out var tokenIdTruncated);
            AddProp("Token ID", tokenIdDisplay, tokenId, "Copy token ID", tokenIdTruncated ? tokenId : null);
            AddProp("Symbol", symbol);
            if (!string.IsNullOrWhiteSpace(token?.Mint) && !string.Equals(token.Mint, "0", StringComparison.OrdinalIgnoreCase))
            {
                AddProp("Mint #", token.Mint);
            }

            if (mintDate != DateTime.MinValue)
            {
                AddProp("Minted at", mintDate.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrWhiteSpace(metadata.Type))
            {
                AddProp("Type", metadata.Type);
            }

            if (metadata.Rarity > 0)
            {
                AddProp("Rarity", metadata.Rarity.ToString(CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrWhiteSpace(token?.ChainName))
            {
                AddProp("Chain", token.ChainName);
            }

            if (!string.IsNullOrWhiteSpace(token?.OwnerAddress))
            {
                AddProp("Owner", FormatId(token.OwnerAddress, 8), token.OwnerAddress, "Copy owner address", token.OwnerAddress);
            }

            if (!string.IsNullOrWhiteSpace(token?.CreatorAddress))
            {
                AddProp("Creator", FormatId(token.CreatorAddress, 8), token.CreatorAddress, "Copy creator address", token.CreatorAddress);
            }

            if (!string.IsNullOrWhiteSpace(token?.Series))
            {
                AddProp("Series", token.Series);
            }

            AddProp("Status", token?.Status.ToString());

            if (locked)
            {
                AddProp("State", "Locked inside infusion");
            }

            if (token?.Properties != null && token.Properties.Length > 0)
            {
                foreach (var property in token.Properties)
                {
                    var label = property.Key?.Replace("_", " ");
                    var formatted = FormatAttributeValue(property.Value, out var truncated);
                    AddProp(label, formatted, valueTooltip: truncated ? property.Value : null);
                }
            }

            if (detailPropertiesContainer.childCount == 0)
            {
                var placeholder = new Label("No traits available.")
                {
                    style =
                    {
                        color = WalletUiTheme.TextSecondary,
                        fontSize = 13,
                        unityTextAlign = TextAnchor.MiddleLeft,
                        marginTop = 4
                    }
                };
                WalletUiCommon.ApplyDefaultFont(placeholder);
                detailPropertiesContainer.Add(placeholder);
            }
        }

        private void UpdateDetailActions(string symbol, bool locked, AccountManager accountManager)
        {
            var platform = accountManager.CurrentPlatform;
            var settings = accountManager.Settings;
            var devMode = settings?.devMode ?? false;
            var nexusKind = settings?.nexusKind ?? NexusKind.Main_Net;

            var canSend = false;
            if (!locked && platform == PlatformKind.Phantasma && Tokens.GetToken(symbol, platform, out var tokenInfo) && !string.IsNullOrEmpty(tokenInfo.Flags) && tokenInfo.IsTransferable())
            {
                var allowTransfer = tokenInfo.IsFungible()
                    || devMode
                    || nexusKind == NexusKind.Test_Net
                    || nexusKind == NexusKind.Dev_Net; // NFT transfers enabled on test/dev nets or dev mode; fungible transfers always allowed.
                canSend = allowTransfer;
            }

            SetActionButtonState(detailSendButton, canSend);
            detailSendButton.style.display = canSend ? DisplayStyle.Flex : DisplayStyle.None;

            var canBurn = !locked && devMode && platform == PlatformKind.Phantasma;
            SetActionButtonState(detailBurnButton, canBurn);
            detailBurnButton.style.display = devMode ? DisplayStyle.Flex : DisplayStyle.None;

            var inspectEntry = context.ViewState.PeekNftInspect();
            var hasId = inspectEntry.HasValue && !string.IsNullOrWhiteSpace(inspectEntry.Value.TokenId);
            var hasExplorerSetting = !string.IsNullOrWhiteSpace(accountManager.Settings?.phantasmaNftExplorer);
            var hasExplorer = hasId && hasExplorerSetting && !string.IsNullOrWhiteSpace(accountManager.GetPhantasmaNftURL(symbol, inspectEntry.Value.TokenId));
            if (detailExplorerButton != null)
            {
                detailExplorerButton.style.display = hasExplorerSetting ? DisplayStyle.Flex : DisplayStyle.None;
                detailExplorerButton.SetEnabled(hasExplorer);
            }
        }

        private void RenderFungibleInfusions(TokenDataResult token, AccountManager accountManager)
        {
            if (detailFungibleList == null || detailFungibleContainer == null)
            {
                return;
            }

            detailFungibleList.Clear();

            if (token?.Infusion == null || token.Infusion.Length == 0)
            {
                detailFungibleContainer.style.display = DisplayStyle.None;
                return;
            }

            var platform = accountManager.CurrentPlatform;
            var aggregated = new Dictionary<string, BigInteger>(StringComparer.OrdinalIgnoreCase);
            var decimals = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in token.Infusion)
            {
                var symbol = entry.Key;
                var amountOrId = entry.Value;
                if (Tokens.GetToken(symbol, platform, out var tok) && tok.IsFungible())
                {
                    if (!BigInteger.TryParse(amountOrId, out var rawAmount))
                    {
                        rawAmount = BigInteger.Zero;
                    }

                    if (aggregated.ContainsKey(symbol))
                    {
                        aggregated[symbol] += rawAmount;
                    }
                    else
                    {
                        aggregated[symbol] = rawAmount;
                        decimals[symbol] = tok.Decimals;
                    }
                }
            }

            if (aggregated.Count == 0)
            {
                detailFungibleContainer.style.display = DisplayStyle.None;
                return;
            }

            var precision = accountManager.Settings?.balanceDisplayPrecision ?? 4;
            foreach (var kvp in aggregated.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                var symbol = kvp.Key;
                var dec = decimals.ContainsKey(symbol) ? decimals[symbol] : 0;
                var amountFormatted = WalletAmountFormatter.Format(kvp.Value, dec, precision);
                var fiat = accountManager.GetTokenWorth(symbol, kvp.Value, dec);
                detailFungibleList.Add(BuildFungibleRow(symbol, amountFormatted, fiat));
            }

            detailFungibleContainer.style.display = DisplayStyle.Flex;
        }

        private VisualElement BuildFungibleRow(string symbol, string amount, string fiat)
        {
            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.SpaceBetween,
                    paddingLeft = 10,
                    paddingRight = 10,
                    paddingTop = 8,
                    paddingBottom = 8,
                    marginBottom = 6
                }
            };
            WalletUiCommon.ApplyDefaultFont(row);
            WalletUiCommon.ApplyCardStyle(row, null, WalletUiTheme.RadiusSmall, WalletUiTheme.CardBackground, WalletUiTheme.CardBackground, WalletUiTheme.CardBackground, 0f);

            var left = new Label($"{amount} {symbol}")
            {
                style =
                {
                    color = WalletUiTheme.TextPrimary,
                    fontSize = 14,
                    unityTextAlign = TextAnchor.MiddleLeft
                }
            };
            WalletUiCommon.ApplyDefaultFont(left);
            row.Add(left);

            var right = new Label(string.IsNullOrWhiteSpace(fiat) ? string.Empty : fiat)
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 13,
                    unityTextAlign = TextAnchor.MiddleRight
                }
            };
            WalletUiCommon.ApplyDefaultFont(right);
            row.Add(right);

            return row;
        }

        private void RenderInfusedNfts(TokenDataResult token, AccountManager accountManager)
        {
            if (detailNftList == null || detailNftContainer == null)
            {
                return;
            }

            detailNftList.Clear();

            if (token?.Infusion == null || token.Infusion.Length == 0)
            {
                detailNftContainer.style.display = DisplayStyle.None;
                return;
            }

            var platform = accountManager.CurrentPlatform;
            var rendered = 0;
            foreach (var entry in token.Infusion)
            {
                var symbol = entry.Key;
                if (Tokens.GetToken(symbol, platform, out var tok) && tok.IsFungible())
                {
                    continue;
                }

                var infusedId = entry.Value;
                if (string.IsNullOrWhiteSpace(infusedId))
                {
                    continue;
                }

                detailNftList.Add(BuildInfusedNftRow(symbol, infusedId));
                rendered++;
            }

            detailNftContainer.style.display = rendered == 0 ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private VisualElement BuildInfusedNftRow(string symbol, string tokenId)
        {
            var platform = AccountManager.Instance?.CurrentPlatform ?? PlatformKind.None;
            TryFindNft(symbol, tokenId, out var token);
            nftSource.TryGetMetadata(symbol, tokenId, out var metadata);

            var card = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    paddingLeft = 10,
                    paddingRight = 10,
                    paddingTop = 8,
                    paddingBottom = 8,
                    marginBottom = 8,
                }
            };
            WalletUiCommon.ApplyDefaultFont(card);
            WalletUiCommon.ApplyCardStyle(card, WalletUiTheme.GetCardGradientTexture(), WalletUiTheme.RadiusMedium);

            var image = new Image
            {
                scaleMode = ScaleMode.ScaleToFit,
                style =
                {
                    width = 64,
                    height = 64,
                    marginRight = 10
                }
            };
            SetNftImageAsync(image, symbol, token);
            card.Add(image);

            var textBlock = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    flexGrow = 1,
                }
            };
            WalletUiCommon.ApplyDefaultFont(textBlock);

            var title = new Label(BuildNftTitle(tokenId, metadata))
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 15,
                    color = WalletUiTheme.TextPrimary,
                    unityTextAlign = TextAnchor.MiddleLeft
                }
            };
            WalletUiCommon.ApplyDefaultFont(title);
            textBlock.Add(title);

            var detail = BuildDetailLine(token, metadata);
            if (!string.IsNullOrWhiteSpace(detail))
            {
                var detailLabel = new Label(detail)
                {
                    style =
                    {
                        color = WalletUiTheme.TextSecondary,
                        fontSize = 12,
                        unityTextAlign = TextAnchor.MiddleLeft,
                        marginTop = 2
                    }
                };
                WalletUiCommon.ApplyDefaultFont(detailLabel);
                textBlock.Add(detailLabel);
            }

            var infusionLine = BuildInfusionDescription(token, platform);
            if (!string.IsNullOrWhiteSpace(infusionLine))
            {
                var infusion = new Label(infusionLine)
                {
                    style =
                    {
                        color = WalletUiTheme.TextSecondary,
                        fontSize = 11,
                        unityTextAlign = TextAnchor.MiddleLeft,
                        marginTop = 2
                    }
                };
                WalletUiCommon.ApplyDefaultFont(infusion);
                textBlock.Add(infusion);
            }

            card.Add(textBlock);

            var actions = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    alignItems = Align.FlexEnd,
                    justifyContent = Justify.Center,
                    marginLeft = 8,
                    flexShrink = 0,
                }
            };
            WalletUiCommon.ApplyDefaultFont(actions);

            var viewButton = WalletUiCommon.CreateOutlineButton("View", () => OpenNftDetails(symbol, tokenId, true, false), 14, 30);
            viewButton.style.minWidth = 90;
            actions.Add(viewButton);

            card.Add(actions);
            card.RegisterCallback<GeometryChangedEvent>(_ => ApplyInfusedRowLayout(card, image, textBlock, actions, viewButton));
            ApplyInfusedRowLayout(card, image, textBlock, actions, viewButton);
            return card;
        }

        private void ApplyInfusedRowLayout(VisualElement card, Image image, VisualElement textBlock, VisualElement actions, Button viewButton)
        {
            var compact = WalletUiCommon.IsCompactWidth(card, CompactDetailWidth);

            if (card != null)
            {
                card.style.flexDirection = compact ? FlexDirection.Column : FlexDirection.Row;
                card.style.alignItems = compact ? Align.Stretch : Align.Center;
            }

            if (image != null)
            {
                image.style.marginRight = compact ? 0 : 10;
                image.style.marginBottom = compact ? 6 : 0;
                image.style.alignSelf = compact ? Align.Center : Align.FlexStart;
            }

            if (textBlock != null)
            {
                textBlock.style.width = compact ? new Length(100, LengthUnit.Percent) : StyleKeyword.Auto;
                textBlock.style.marginBottom = compact ? 6 : 0;
            }

            if (actions != null)
            {
                actions.style.alignItems = compact ? Align.Stretch : Align.FlexEnd;
                actions.style.width = compact ? new Length(100, LengthUnit.Percent) : StyleKeyword.Auto;
                actions.style.marginLeft = compact ? 0 : 8;
            }

            if (viewButton != null)
            {
                viewButton.style.width = compact ? new Length(100, LengthUnit.Percent) : StyleKeyword.Auto;
                viewButton.style.alignSelf = compact ? Align.Stretch : Align.Center;
            }
        }

        private void AddDetailTag(string text)
        {
            if (detailTagContainer == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            detailTagContainer.Add(CreateDetailTag(text));
        }

        private VisualElement CreateDetailTag(string text)
        {
            var tag = new Label(text)
            {
                style =
                {
                    backgroundColor = WalletUiTheme.PanelBackground,
                    color = WalletUiTheme.TextPrimary,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 12,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    paddingLeft = 10,
                    paddingRight = 10,
                    paddingTop = 4,
                    paddingBottom = 4,
                    borderTopLeftRadius = WalletUiTheme.RadiusSmall,
                    borderTopRightRadius = WalletUiTheme.RadiusSmall,
                    borderBottomLeftRadius = WalletUiTheme.RadiusSmall,
                    borderBottomRightRadius = WalletUiTheme.RadiusSmall,
                    marginRight = 6,
                    marginBottom = 6
                }
            };
            WalletUiCommon.ApplyDefaultFont(tag);
            return tag;
        }

        private VisualElement CreateInfoChip(string label, string value, string copyValue = null, string copyTooltip = null, string valueTooltip = null)
        {
            var chip = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    paddingLeft = 10,
                    paddingRight = 10,
                    paddingTop = 8,
                    paddingBottom = 8,
                    marginRight = 8,
                    marginBottom = 8,
                    minWidth = 140,
                }
            };
            WalletUiCommon.ApplyDefaultFont(chip);
            WalletUiCommon.ApplyCardStyle(chip, null, WalletUiTheme.RadiusSmall, WalletUiTheme.CardBackground, WalletUiTheme.CardBackground, WalletUiTheme.CardBackground, 0f);

            var labelEl = new Label(label)
            {
                style =
                {
                    color = WalletUiTheme.TextSecondary,
                    fontSize = 12,
                    unityTextAlign = TextAnchor.MiddleLeft
                }
            };
            WalletUiCommon.ApplyDefaultFont(labelEl);

            var headerRow = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.SpaceBetween
                }
            };
            WalletUiCommon.ApplyDefaultFont(headerRow);
            labelEl.style.flexGrow = 1;
            headerRow.Add(labelEl);

            if (!string.IsNullOrWhiteSpace(copyValue))
            {
                var copyButton = WalletUiCommon.CreateIconButton(WalletUiCommon.CopyIcon, () => CopyToClipboard(copyValue), 26, 12, copyTooltip ?? "Copy");
                copyButton.style.marginLeft = 6;
                copyButton.style.flexShrink = 0;
                headerRow.Add(copyButton);
            }

            var valueEl = new Label(value)
            {
                style =
                {
                    color = WalletUiTheme.TextPrimary,
                    fontSize = 14,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    whiteSpace = WhiteSpace.Normal
                }
            };
            WalletUiCommon.ApplyDefaultFont(valueEl);
            if (!string.IsNullOrWhiteSpace(valueTooltip))
            {
                valueEl.tooltip = valueTooltip;
            }

            chip.Add(headerRow);
            chip.Add(valueEl);
            return chip;
        }

        private string FormatAttributeValue(string value, out bool truncated)
        {
            truncated = false;
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            var compact = WalletUiCommon.IsCompactWidth(detailScroll, CompactDetailWidth);
            var monolithicMax = compact ? AttributeMonolithicCompactMax : AttributeMonolithicMax;
            var textMax = compact ? AttributeTextCompactMax : AttributeTextMax;
            var hasWhitespace = trimmed.Any(char.IsWhiteSpace);

            // Monolithic strings (no whitespace) must be shortened in the middle to avoid blowing up the layout.
            if (!hasWhitespace && trimmed.Length > monolithicMax)
            {
                truncated = true;
                var head = Math.Max(8, monolithicMax / 2 - 2);
                var tail = Math.Max(8, monolithicMax - head - 3);
                var abbreviated = WalletUiCommon.AbbreviateMiddle(trimmed, head, tail);
                return $"{abbreviated} (len {trimmed.Length})";
            }

            if (hasWhitespace && trimmed.Length > textMax)
            {
                truncated = true;
                var abbreviated = WalletUiCommon.AbbreviateEnd(trimmed, textMax);
                return $"{abbreviated} (len {trimmed.Length})";
            }

            return trimmed;
        }

        private void CopyToClipboard(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            GUIUtility.systemCopyBuffer = value;
        }

        private bool TryFindNft(string symbol, string tokenId, out TokenDataResult token)
        {
            token = null;
            if (string.IsNullOrWhiteSpace(tokenId))
            {
                return false;
            }

            var current = nftSource.GetNfts(symbol)?.FirstOrDefault(x => string.Equals(x.Id, tokenId, StringComparison.OrdinalIgnoreCase));
            if (current != null && !string.IsNullOrEmpty(current.Id))
            {
                token = current;
                return true;
            }

            var fallback = nftSource.GetNft(symbol, tokenId);
            if (fallback != null && !string.IsNullOrEmpty(fallback.Id))
            {
                token = fallback;
                return true;
            }

            return false;
        }

        private Task SendCurrentNftAsync()
        {
            var inspectEntry = context.ViewState.PeekNftInspect();
            if (inspectEntry == null)
            {
                SetStatus("No NFT selected.");
                return Task.CompletedTask;
            }

            return SendAsync(inspectEntry.Value.Symbol, new List<string> { inspectEntry.Value.TokenId });
        }

        private Task BurnCurrentNftAsync()
        {
            var inspectEntry = context.ViewState.PeekNftInspect();
            if (inspectEntry == null)
            {
                SetStatus("No NFT selected.");
                return Task.CompletedTask;
            }

            return BurnAsync(inspectEntry.Value.Symbol, new List<string> { inspectEntry.Value.TokenId });
        }

        private void OpenCurrentNftInExplorer()
        {
            var inspectEntry = context.ViewState.PeekNftInspect();
            if (inspectEntry == null)
            {
                return;
            }

            OpenNftExplorer(inspectEntry.Value.Symbol, inspectEntry.Value.TokenId);
        }
    }
}
