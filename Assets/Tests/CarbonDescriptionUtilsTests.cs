using NUnit.Framework;
using PhantasmaPhoenix.Protocol.Carbon;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain;
using PhantasmaPhoenix.Protocol.Carbon.Blockchain.Modules;
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Phantasma.Tests
{
    public class CarbonDescriptionUtilsTests
    {
        private static Type DescriptionUtilsType => FindType("Poltergeist.DescriptionUtils");

        private static Type TokensType => FindType("Tokens");

        private static Type TokenResultType => FindType("PhantasmaPhoenix.RPC.Models.TokenResult");

        private static Type FindType(string fullName)
        {
            return AppDomain.CurrentDomain
                .GetAssemblies()
                .Select(assembly => assembly.GetType(fullName))
                .FirstOrDefault(type => type != null);
        }

        private static void ResetTokens()
        {
            Assert.NotNull(TokensType, "Failed to resolve Tokens from Assembly-CSharp.");
            TokensType.GetMethod("Reset", BindingFlags.Public | BindingFlags.Static).Invoke(null, Array.Empty<object>());
        }

        private static void AddToken(string symbol, uint decimals, ulong carbonId, string flags = "Fungible")
        {
            Assert.NotNull(TokensType, "Failed to resolve Tokens from Assembly-CSharp.");
            Assert.NotNull(TokenResultType, "Failed to resolve PhantasmaPhoenix.RPC.Models.TokenResult.");

            var token = Activator.CreateInstance(TokenResultType);
            TokenResultType.GetProperty("Symbol").SetValue(token, symbol);
            TokenResultType.GetProperty("Name").SetValue(token, symbol);
            TokenResultType.GetProperty("Decimals").SetValue(token, decimals);
            TokenResultType.GetProperty("CarbonId").SetValue(token, carbonId.ToString());
            TokenResultType.GetProperty("Flags").SetValue(token, flags);

            TokensType.GetMethod("AddToken", BindingFlags.Public | BindingFlags.Static).Invoke(null, new[] { token });
        }

        // Sets (or clears with null) the static DescriptionUtils.MissingCarbonTokenLoader hook so
        // tests can simulate a lazy single-token fetch without a live RPC or AccountManager.
        private static void SetMissingCarbonTokenLoader(Func<ulong, CancellationToken, Task<bool>> loader)
        {
            Assert.NotNull(DescriptionUtilsType, "Failed to resolve Poltergeist.DescriptionUtils from Assembly-CSharp.");
            var field = DescriptionUtilsType.GetField("MissingCarbonTokenLoader", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(field, "Failed to find DescriptionUtils.MissingCarbonTokenLoader.");
            field.SetValue(null, loader);
        }

        [SetUp]
        public void SetUp()
        {
            ResetTokens();
            AddToken("KCAL", 10, 1);
            AddToken("SOUL", 8, 2);
            AddToken("GHOST", 0, 11, "Transferable, Burnable");
            // Default to no lazy loader so seeded-token tests exercise the direct path.
            SetMissingCarbonTokenLoader(null);
        }

        [TearDown]
        public void TearDown()
        {
            // Avoid leaking the hook into other tests.
            SetMissingCarbonTokenLoader(null);
            ResetTokens();
        }

        private static async Task<(string Description, string Error)> GetCarbonDescriptionAsync(TxMsg tx)
        {
            Assert.NotNull(DescriptionUtilsType, "Failed to resolve Poltergeist.DescriptionUtils from Assembly-CSharp.");

            var method = DescriptionUtilsType.GetMethod("GetCarbonDescriptionAsync", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(method, "Failed to find DescriptionUtils.GetCarbonDescriptionAsync.");

            var task = (Task<(string Description, string Error)>)method.Invoke(null, new object[] { tx, false, CancellationToken.None });
            return await task;
        }

        private static Bytes32 Bytes32From(byte start)
        {
            return new Bytes32(Enumerable.Range(start, 32).Select(x => (byte)x).ToArray());
        }

        [Test]
        public async Task GetCarbonDescriptionAsync_FormatsMarketplaceBuyAndFeeTransfer()
        {
            /*
             * Token.TransferFungible inside a contract call uses TransferFungibleArgs, not the shorter
             * top-level TxMsgTransferFungible payload used by TxTypes.TransferFungible. Market.BuyToken
             * does not carry a max price, so the signing prompt must not display a fetched price as if
             * it was part of the signed transaction.
             */
            var kcalTransfer = CarbonBlob.Serialize(new TransferFungibleArgs
            {
                to = Bytes32From(97),
                from = Bytes32From(65),
                tokenId = 1,
                amount = new IntX(1234567890)
            });
            var buyArgs = CarbonBlob.Serialize(new MarketBuyTokenArgs
            {
                from = Bytes32From(65),
                tokenId = 11,
                instanceId = 4294973013
            });

            var tx = new TxMsg
            {
                type = TxTypes.Call_Multi,
                expiry = 0,
                maxGas = 1000,
                maxData = 0,
                gasFrom = Bytes32From(129),
                payload = new SmallString("test"),
                msg = new TxMsgCall_Multi
                {
                    calls = new[]
                    {
                        new TxMsgCall
                        {
                            moduleId = (uint)ModuleId.Market,
                            methodId = (uint)MarketContract_Methods.BuyToken,
                            args = buyArgs
                        },
                        new TxMsgCall
                        {
                            moduleId = (uint)ModuleId.Token,
                            methodId = (uint)TokenContract_Methods.TransferFungible,
                            args = kcalTransfer
                        }
                    }
                }
            };

            var (description, error) = await GetCarbonDescriptionAsync(tx);

            Assert.IsNull(error);
            Assert.That(description, Does.Contain("Buy GHOST NFT #4294973013."));
            Assert.That(description, Does.Contain("Transfer 0.123456789 KCAL"));
            Assert.That(description.Split(new[] { "\u2605 Transfer" }, StringSplitOptions.None).Length - 1, Is.EqualTo(1));
            Assert.That(description, Does.Not.Contain("Fetched"));
            Assert.That(description, Does.Not.Contain("SOUL"));
            Assert.That(description, Does.Not.Contain("tokenId:"));
            Assert.That(description, Does.Not.Contain("amount: 1234567890"));
        }

        [Test]
        public async Task GetCarbonDescriptionAsync_LazilyLoadsMissingTokenByCarbonId()
        {
            /*
             * A token referenced by its Carbon id may not be in the loaded token list yet (for
             * example a token created after the wallet last loaded its full token list). The
             * description layer must lazily load just that token via MissingCarbonTokenLoader and
             * still produce a description, instead of hard-failing the signing prompt.
             */
            const ulong missingCarbonId = 105;
            var loaderCalls = 0;

            SetMissingCarbonTokenLoader((carbonId, cancellationToken) =>
            {
                loaderCalls++;
                // Simulate a successful single-token fetch by adding the token to the list.
                if (carbonId == missingCarbonId)
                {
                    AddToken("ALIEN", 18, missingCarbonId, "Transferable, Burnable, Fungible, Divisible, Finite");
                    return Task.FromResult(true);
                }

                return Task.FromResult(false);
            });

            var alienTransfer = CarbonBlob.Serialize(new TransferFungibleArgs
            {
                to = Bytes32From(97),
                from = Bytes32From(65),
                tokenId = missingCarbonId,
                amount = new IntX(1234567890)
            });

            var tx = new TxMsg
            {
                type = TxTypes.Call,
                expiry = 0,
                maxGas = 1000,
                maxData = 0,
                gasFrom = Bytes32From(129),
                payload = new SmallString("test"),
                msg = new TxMsgCall
                {
                    moduleId = (uint)ModuleId.Token,
                    methodId = (uint)TokenContract_Methods.TransferFungible,
                    args = alienTransfer
                }
            };

            var (description, error) = await GetCarbonDescriptionAsync(tx);

            Assert.IsNull(error);
            Assert.AreEqual(1, loaderCalls, "Loader should be invoked exactly once for the missing token.");
            Assert.That(description, Does.Contain("ALIEN"));
            Assert.That(description, Does.Contain("★ Transfer"));
        }

        [Test]
        public void GetCarbonDescriptionAsync_SurfacesMappingErrorWhenLazyLoadFails()
        {
            /*
             * When the missing token cannot be lazily loaded (genuinely unknown id or fetch
             * failure), the original token mapping error must still surface rather than being
             * silently swallowed, so the user is never shown a misleading partial description.
             */
            const ulong missingCarbonId = 105;
            var loaderCalls = 0;

            SetMissingCarbonTokenLoader((carbonId, cancellationToken) =>
            {
                loaderCalls++;
                return Task.FromResult(false);
            });

            var alienTransfer = CarbonBlob.Serialize(new TransferFungibleArgs
            {
                to = Bytes32From(97),
                from = Bytes32From(65),
                tokenId = missingCarbonId,
                amount = new IntX(1234567890)
            });

            var tx = new TxMsg
            {
                type = TxTypes.Call,
                expiry = 0,
                maxGas = 1000,
                maxData = 0,
                gasFrom = Bytes32From(129),
                payload = new SmallString("test"),
                msg = new TxMsgCall
                {
                    moduleId = (uint)ModuleId.Token,
                    methodId = (uint)TokenContract_Methods.TransferFungible,
                    args = alienTransfer
                }
            };

            // Type is resolved by name because the test assembly does not reference Assembly-CSharp.
            var exception = Assert.CatchAsync(async () => await GetCarbonDescriptionAsync(tx));
            Assert.AreEqual("TokenMappingException", exception.GetType().Name);
            Assert.AreEqual(1, loaderCalls, "Loader should be attempted exactly once.");

            var carbonIdValue = exception.GetType().GetProperty("CarbonId").GetValue(exception);
            Assert.AreEqual((ulong)missingCarbonId, carbonIdValue);
        }
    }
}
