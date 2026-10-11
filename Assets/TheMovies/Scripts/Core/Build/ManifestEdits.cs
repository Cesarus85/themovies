using System.Text.RegularExpressions;

namespace TheMovies.Core.Build
{
    /// <summary>
    /// Text edits on the Android manifest / network security config of the generated Gradle project
    /// (used by the editor build step, kept here so they can be unit tested without Unity).
    /// </summary>
    public static class ManifestEdits
    {
        static readonly Regex k_ApplicationTag = new Regex(@"<application(?=[\s>/])");
        static readonly Regex k_NetworkConfig = new Regex(@"android:networkSecurityConfig\s*=\s*""@xml/([^""]+)""");
        static readonly Regex k_CleartextAttribute = new Regex(@"android:usesCleartextTraffic\s*=\s*""(true|false)""");

        /// <summary>Adds a &lt;uses-feature&gt; before &lt;application&gt; unless the manifest already names it.</summary>
        public static string AddUsesFeature(string manifest, string feature, bool required)
        {
            if (manifest.Contains(feature)) return manifest;
            var match = k_ApplicationTag.Match(manifest);
            if (!match.Success) return manifest;
            return manifest.Insert(match.Index,
                $"<uses-feature android:name=\"{feature}\" android:required=\"{(required ? "true" : "false")}\" />\n    ");
        }

        /// <summary>The name of the network security config the manifest points to (without "@xml/"), or null.</summary>
        public static string NetworkSecurityConfigName(string manifest)
        {
            var m = k_NetworkConfig.Match(manifest);
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>Sets android:usesCleartextTraffic="true" on &lt;application&gt; (replacing "false", adding it if missing).</summary>
        public static string AllowCleartext(string manifest, bool addIfMissing)
        {
            if (k_CleartextAttribute.IsMatch(manifest))
                return k_CleartextAttribute.Replace(manifest, "android:usesCleartextTraffic=\"true\"");
            if (!addIfMissing) return manifest;
            var match = k_ApplicationTag.Match(manifest);
            if (!match.Success) return manifest;
            return manifest.Insert(match.Index + match.Length, " android:usesCleartextTraffic=\"true\"");
        }

        /// <summary>Makes a network_security_config allow cleartext HTTP by default (base-config).</summary>
        public static string AllowCleartextInConfig(string config)
        {
            config = Regex.Replace(config, @"cleartextTrafficPermitted\s*=\s*""false""", "cleartextTrafficPermitted=\"true\"");
            var baseConfig = new Regex(@"<base-config(?=[\s>/])");
            var match = baseConfig.Match(config);
            if (match.Success)
            {
                int end = config.IndexOf('>', match.Index);
                string tag = end > 0 ? config.Substring(match.Index, end - match.Index) : string.Empty;
                if (tag.Contains("cleartextTrafficPermitted")) return config;
                return config.Insert(match.Index + match.Length, " cleartextTrafficPermitted=\"true\"");
            }
            var root = new Regex(@"<network-security-config[^>]*?>");
            var rootMatch = root.Match(config);
            if (!rootMatch.Success || rootMatch.Value.EndsWith("/>"))
                return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<network-security-config>\n    <base-config cleartextTrafficPermitted=\"true\" />\n</network-security-config>\n";
            return config.Insert(rootMatch.Index + rootMatch.Length, "\n    <base-config cleartextTrafficPermitted=\"true\" />");
        }
    }
}
