using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using PhantasmaPhoenix.RPC.Models;
using PhantasmaPhoenix.Protocol;
using PhantasmaPhoenix.NFT.Extensions;
using Poltergeist.Wallet;

namespace Poltergeist.UiToolkit.Balances
{
    /// <summary>
    /// Detail view slice of the NFT dashboard extracted to keep the main file lean.
    /// </summary>
    public sealed partial class WalletNftDashboardView
    {
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

            detailScroll = new ScrollView(ScrollViewMode.Vertical)
            {
                style =
                {
                    flexGrow = 1,
                    paddingLeft = 4,
                    paddingRight = 4,
                    paddingBottom = 90f
                },
                horizontalScrollerVisibility = ScrollerVisibility.Hidden
            };
            WalletUiCommon.ApplyDefaultFont(detailScroll);

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
                    whiteSpace = WhiteSpace.Normal
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
                    minHeight = 0,
                    minWidth = new Length(100, LengthUnit.Percent)
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

            container.Add(detailScroll);
            return container;
        }

        private VisualElement CreateDetailHero()
        {
            var card = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    flexWrap = Wrap.Wrap,
                    alignItems = Align.Stretch,
                    paddingLeft = 14,
                    paddingRight = 14,
                    paddingTop = 12,
                    paddingBottom = 12,
                    marginBottom = 12,
                    overflow = Overflow.Hidden,
                    width = new Length(100, LengthUnit.Percent)
                }
            };
            WalletUiCommon.ApplyDefaultFont(card);
            WalletUiCommon.ApplyCardStyle(card, WalletUiTheme.GetCardGradientTexture(), WalletUiTheme.RadiusMedium);

            detailImage = new Image
            {
                scaleMode = ScaleMode.ScaleToFit,
                style =
                {
                    width = 220,
                    height = 220,
                    marginRight = 16,
                    backgroundColor = WalletUiTheme.PanelBackground,
                    borderTopLeftRadius = WalletUiTheme.RadiusMedium,
                    borderTopRightRadius = WalletUiTheme.RadiusMedium,
                    borderBottomLeftRadius = WalletUiTheme.RadiusMedium,
                    borderBottomRightRadius = WalletUiTheme.RadiusMedium,
                    overflow = Overflow.Hidden,
                    flexShrink = 0
                }
            };
            card.Add(detailImage);

            var info = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column,
                    flexGrow = 1,
                    flexShrink = 1,
                    flexBasis = 0,
                    minHeight = 220,
                    minWidth = 0
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
                    marginTop = 10,
                    minWidth = 0
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
                    var rom = nftSource.GetNftRom(tokenId);
                    mintDate = rom?.GetDate() ?? DateTime.MinValue;
                }
            }
            catch
            {
                mintDate = metadata.MintDate;
            }

            var title = !string.IsNullOrWhiteSpace(metadata.Name) ? metadata.Name : token?.GetPropertyValue("Name");
            detailTitleLabel.text = string.IsNullOrWhiteSpace(title) ? BuildNftTitle(tokenId, metadata) : title;
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

            void AddProp(string label, string value)
            {
                if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                if (added.Contains(label))
                {
                    return;
                }

                detailPropertiesContainer.Add(CreateInfoChip(label, value));
                added.Add(label);
            }

            AddProp("Token ID", tokenId);
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
                AddProp("Owner", FormatId(token.OwnerAddress, 8));
            }

            if (!string.IsNullOrWhiteSpace(token?.CreatorAddress))
            {
                AddProp("Creator", FormatId(token.CreatorAddress, 8));
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
                    AddProp(property.Key?.Replace("_", " "), property.Value);
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
                    marginBottom = 8
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
                    flexGrow = 1
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
                    flexShrink = 0
                }
            };
            WalletUiCommon.ApplyDefaultFont(actions);

            var viewButton = WalletUiCommon.CreateOutlineButton("View", () => OpenNftDetails(symbol, tokenId, true, false), 14, 30);
            viewButton.style.minWidth = 90;
            actions.Add(viewButton);

            card.Add(actions);
            return card;
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

        private VisualElement CreateInfoChip(string label, string value)
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
                    maxWidth = 420,
                    overflow = Overflow.Hidden
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

            var valueEl = new Label(value)
            {
                style =
                {
                    color = WalletUiTheme.TextPrimary,
                    fontSize = 14,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    whiteSpace = WhiteSpace.Normal,
                    overflow = Overflow.Hidden
                }
            };
            WalletUiCommon.ApplyDefaultFont(valueEl);

            chip.Add(labelEl);
            chip.Add(valueEl);
            return chip;
        }

        private bool TryFindNft(string symbol, string tokenId, out TokenDataResult token)
        {
            _ = symbol;
            token = null;
            if (string.IsNullOrWhiteSpace(tokenId))
            {
                return false;
            }

            var current = nftSource.CurrentNfts?.FirstOrDefault(x => string.Equals(x.Id, tokenId, StringComparison.OrdinalIgnoreCase));
            if (current != null && !string.IsNullOrEmpty(current.Id))
            {
                token = current;
                return true;
            }

            var fallback = nftSource.GetNft(tokenId);
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
