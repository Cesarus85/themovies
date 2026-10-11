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

namespace TheMovies.Tests
{
    using NUnit.Framework;
    using TheMovies.Core.Build;

    public class GradleEditTests
    {
        const string Gradle =
            "apply plugin: 'com.android.library'\n\n" +
            "dependencies {\n    implementation fileTree(dir: 'libs', include: ['*.jar'])\n}\n\n" +
            "android {\n    namespace \"com.unity3d.player\"\n}\n";

        [Test]
        public void AddsDependenciesOnce()
        {
            string once = GradleEdits.AddDependencies(Gradle, "androidx.media3:media3-exoplayer:1.4.1", "org.jellyfin.media3:media3-ffmpeg-decoder:1.4.1+1");
            StringAssert.Contains("implementation 'androidx.media3:media3-exoplayer:1.4.1'", once);
            StringAssert.Contains("implementation 'org.jellyfin.media3:media3-ffmpeg-decoder:1.4.1+1'", once);
            Assert.Less(once.IndexOf("media3-exoplayer"), once.IndexOf("android {"));
            Assert.AreEqual(once, GradleEdits.AddDependencies(once, "androidx.media3:media3-exoplayer:1.4.1"));
            // An existing different version is left alone.
            Assert.AreEqual(once, GradleEdits.AddDependencies(once, "androidx.media3:media3-exoplayer:1.5.0"));
            StringAssert.Contains("dependencies {", GradleEdits.AddDependencies("apply plugin: 'x'", "a:b:1"));
        }

        [Test]
        public void SetsProperties()
        {
            Assert.AreEqual("a=1\nandroid.useAndroidX=true\n", GradleEdits.SetProperty("a=1", "android.useAndroidX", "true"));
            Assert.AreEqual("android.useAndroidX=true\nb=2", GradleEdits.SetProperty("android.useAndroidX=false\nb=2", "android.useAndroidX", "true"));
        }

        [Test]
        public void AddsRulesOnce()
        {
            string rule = "-keep class com.themovies.player.** { *; }";
            string once = GradleEdits.AddRule("-keep class x", rule);
            Assert.AreEqual(once, GradleEdits.AddRule(once, rule));
        }
    }
}
