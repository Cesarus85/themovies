using NUnit.Framework;
using TheMovies.Core.Build;

namespace TheMovies.Tests
{
    public class ManifestEditTests
    {
        const string Manifest =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\" package=\"com.unity3d.player\">\n" +
            "  <uses-feature android:glEsVersion=\"0x00030000\" />\n" +
            "  <application android:extractNativeLibs=\"true\">\n" +
            "    <meta-data android:name=\"unity.splash-mode\" android:value=\"0\" />\n" +
            "  </application>\n</manifest>\n";

        [Test]
        public void AddsPassthroughFeatureOnce()
        {
            string once = ManifestEdits.AddUsesFeature(Manifest, "com.oculus.feature.PASSTHROUGH", false);
            StringAssert.Contains("<uses-feature android:name=\"com.oculus.feature.PASSTHROUGH\" android:required=\"false\" />", once);
            Assert.Less(once.IndexOf("PASSTHROUGH"), once.IndexOf("<application"));
            Assert.AreEqual(once, ManifestEdits.AddUsesFeature(once, "com.oculus.feature.PASSTHROUGH", false));
        }

        [Test]
        public void AllowsCleartext()
        {
            string added = ManifestEdits.AllowCleartext(Manifest, true);
            StringAssert.Contains("<application android:usesCleartextTraffic=\"true\" android:extractNativeLibs=\"true\">", added);
            Assert.AreEqual(added, ManifestEdits.AllowCleartext(added, true));
            string replaced = ManifestEdits.AllowCleartext(Manifest.Replace("<application ", "<application android:usesCleartextTraffic=\"false\" "), false);
            StringAssert.Contains("usesCleartextTraffic=\"true\"", replaced);
            StringAssert.DoesNotContain("\"false\"", replaced);
            Assert.AreEqual(Manifest, ManifestEdits.AllowCleartext(Manifest, false));
        }

        [Test]
        public void FindsNetworkSecurityConfig()
        {
            Assert.IsNull(ManifestEdits.NetworkSecurityConfigName(Manifest));
            Assert.AreEqual("network_sec_config", ManifestEdits.NetworkSecurityConfigName(
                Manifest.Replace("<application ", "<application android:networkSecurityConfig=\"@xml/network_sec_config\" ")));
        }

        [TestCase("<network-security-config><base-config cleartextTrafficPermitted=\"false\"><trust-anchors/></base-config></network-security-config>")]
        [TestCase("<network-security-config><base-config><trust-anchors/></base-config></network-security-config>")]
        [TestCase("<network-security-config><domain-config cleartextTrafficPermitted=\"true\"><domain>x</domain></domain-config></network-security-config>")]
        [TestCase("<network-security-config/>")]
        public void ConfigAllowsCleartext(string config)
        {
            string result = ManifestEdits.AllowCleartextInConfig(config);
            StringAssert.Contains("<base-config cleartextTrafficPermitted=\"true\"", result);
            StringAssert.DoesNotContain("\"false\"", result);
            Assert.AreEqual(result, ManifestEdits.AllowCleartextInConfig(result));
        }
    }
}
