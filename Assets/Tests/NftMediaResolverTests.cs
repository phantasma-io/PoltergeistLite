using NUnit.Framework;
using System;
using System.Reflection;

namespace Phantasma.Tests
{
    public class NftMediaResolverTests
    {
        private static Type ResolverType => Type.GetType("Poltergeist.Wallet.NftMediaResolver, Assembly-CSharp");

        private static object ResolveSource(string source, string declaredKindName = null)
        {
            Assert.NotNull(ResolverType, "Failed to resolve Poltergeist.Wallet.NftMediaResolver from Assembly-CSharp.");

            var kindType = Type.GetType("Poltergeist.Wallet.NftMediaKind, Assembly-CSharp");
            Assert.NotNull(kindType, "Failed to resolve Poltergeist.Wallet.NftMediaKind from Assembly-CSharp.");

            var method = ResolverType.GetMethod("ResolveSource", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(method, "Failed to find NftMediaResolver.ResolveSource.");

            var declaredKind = declaredKindName == null
                ? Enum.ToObject(kindType, 0)
                : Enum.Parse(kindType, declaredKindName);

            return method.Invoke(null, new[] { source, declaredKind });
        }

        private static object ResolvePreviewSource(string imageSource, string videoSource = null, string audioSource = null)
        {
            Assert.NotNull(ResolverType, "Failed to resolve Poltergeist.Wallet.NftMediaResolver from Assembly-CSharp.");

            var method = ResolverType.GetMethod("ResolvePreviewSource", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(method, "Failed to find NftMediaResolver.ResolvePreviewSource.");

            return method.Invoke(null, new object[] { imageSource, videoSource, audioSource });
        }

        private static string GetStringProperty(object instance, string propertyName)
        {
            var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(property, $"Missing property '{propertyName}'.");
            return (string)property.GetValue(instance);
        }

        private static bool GetBoolProperty(object instance, string propertyName)
        {
            var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(property, $"Missing property '{propertyName}'.");
            return (bool)property.GetValue(instance);
        }

        private static string GetEnumName(object instance, string propertyName)
        {
            var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(property, $"Missing property '{propertyName}'.");
            var value = property.GetValue(instance);
            return value?.ToString() ?? string.Empty;
        }

        [Test]
        public void ResolveSource_MapsIpfsVideoToGatewayUrl()
        {
            var media = ResolveSource("ipfs-video://bafybeigdyrztw3/video.mp4");

            Assert.AreEqual("Video", GetEnumName(media, "Kind"));
            Assert.AreEqual("ipfs-video://bafybeigdyrztw3/video.mp4", GetStringProperty(media, "Source"));
            Assert.AreEqual("https://gateway.ipfs.io/ipfs/bafybeigdyrztw3/video.mp4", GetStringProperty(media, "OpenUrl"));
            Assert.IsTrue(GetBoolProperty(media, "CanOpenExternally"));
            Assert.IsFalse(GetBoolProperty(media, "IsInlineImage"));
        }

        [Test]
        public void ResolveSource_MapsLegacyIpfsVidToGatewayUrl()
        {
            var media = ResolveSource("ipfs-vid://QmLegacyVideoCid");

            Assert.AreEqual("Video", GetEnumName(media, "Kind"));
            Assert.AreEqual("https://gateway.ipfs.io/ipfs/QmLegacyVideoCid", GetStringProperty(media, "OpenUrl"));
            Assert.IsTrue(GetBoolProperty(media, "CanOpenExternally"));
        }

        [Test]
        public void ResolveSource_RecognizesInlineImages()
        {
            var media = ResolveSource("data:image/png;base64,AAAA");

            Assert.AreEqual("Image", GetEnumName(media, "Kind"));
            Assert.IsTrue(GetBoolProperty(media, "IsInlineImage"));
            Assert.AreEqual(string.Empty, GetStringProperty(media, "OpenUrl"));
            Assert.IsFalse(GetBoolProperty(media, "CanOpenExternally"));
        }

        [Test]
        public void ResolveSource_AllowsExternalImages()
        {
            var media = ResolveSource("ipfs://bafybeigposter/poster.png", "Image");

            Assert.AreEqual("Image", GetEnumName(media, "Kind"));
            Assert.AreEqual("https://gateway.ipfs.io/ipfs/bafybeigposter/poster.png", GetStringProperty(media, "OpenUrl"));
            Assert.IsTrue(GetBoolProperty(media, "CanOpenExternally"));
            Assert.IsFalse(GetBoolProperty(media, "IsInlineImage"));
        }

        [Test]
        public void ResolveSource_RejectsUnsafeSchemesForExternalOpen()
        {
            var media = ResolveSource("javascript:alert(1)", "Video");

            Assert.AreEqual("Video", GetEnumName(media, "Kind"));
            Assert.AreEqual(string.Empty, GetStringProperty(media, "OpenUrl"));
            Assert.IsFalse(GetBoolProperty(media, "CanOpenExternally"));
        }

        [Test]
        public void ResolveSource_UsesHttpsForBareHostMediaLinks()
        {
            var media = ResolveSource("cdn.example.com/media/demo.webm");

            Assert.AreEqual("Video", GetEnumName(media, "Kind"));
            Assert.AreEqual("https://cdn.example.com/media/demo.webm", GetStringProperty(media, "OpenUrl"));
            Assert.IsTrue(GetBoolProperty(media, "CanOpenExternally"));
        }

        [Test]
        public void ResolvePreviewSource_PrefersImageOverVideo()
        {
            var media = ResolvePreviewSource("https://cdn.example.com/poster.png", "ipfs-video://bafybeigdyrztw3/video.mp4");

            Assert.AreEqual("Image", GetEnumName(media, "Kind"));
            Assert.AreEqual("https://cdn.example.com/poster.png", GetStringProperty(media, "Source"));
            Assert.IsFalse(GetBoolProperty(media, "CanOpenExternally"));
        }
    }
}
