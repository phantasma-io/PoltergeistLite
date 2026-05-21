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

        [SetUp]
        public void SetUp()
        {
            ResetTokens();
            AddToken("KCAL", 10, 1);
            AddToken("SOUL", 8, 2);
            AddToken("GHOST", 0, 11, "Transferable, Burnable");
        }

        [TearDown]
        public void TearDown()
        {
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
    }
}
