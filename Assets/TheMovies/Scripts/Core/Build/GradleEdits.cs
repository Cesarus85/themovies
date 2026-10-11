using System;
using System.Text;
using System.Text.RegularExpressions;

namespace TheMovies.Core.Build
{
    /// <summary>Text edits on the generated Gradle project (dependencies, properties, ProGuard rules).</summary>
    public static class GradleEdits
    {
        /// <summary>Adds "implementation '…'" lines to the first top-level dependencies block (only missing ones).</summary>
        public static string AddDependencies(string buildGradle, params string[] coordinates)
        {
            var block = new Regex(@"(^|\n)dependencies\s*\{");
            var match = block.Match(buildGradle);
            var sb = new StringBuilder();
            foreach (string c in coordinates)
            {
                string artifact = c.Substring(0, c.LastIndexOf(':')); // group:name without version
                if (buildGradle.Contains("'" + artifact + ":") || buildGradle.Contains("\"" + artifact + ":")) continue;
                sb.Append("\n    implementation '").Append(c).Append('\'');
            }
            if (sb.Length == 0) return buildGradle;
            if (!match.Success)
                return buildGradle.TrimEnd() + "\n\ndependencies {" + sb + "\n}\n";
            int insertAt = match.Index + match.Length;
            return buildGradle.Insert(insertAt, sb.ToString());
        }

        /// <summary>Sets key=value in gradle.properties (replaces an existing value).</summary>
        public static string SetProperty(string properties, string key, string value)
        {
            properties = properties ?? string.Empty;
            var line = new Regex(@"(?m)^\s*" + Regex.Escape(key) + @"\s*=.*$");
            if (line.IsMatch(properties))
                return line.Replace(properties, key + "=" + value);
            if (properties.Length > 0 && !properties.EndsWith("\n", StringComparison.Ordinal)) properties += "\n";
            return properties + key + "=" + value + "\n";
        }

        /// <summary>Appends a ProGuard rule unless it is already there.</summary>
        public static string AddRule(string proguard, string rule)
        {
            proguard = proguard ?? string.Empty;
            if (proguard.Contains(rule)) return proguard;
            if (proguard.Length > 0 && !proguard.EndsWith("\n", StringComparison.Ordinal)) proguard += "\n";
            return proguard + rule + "\n";
        }
    }
}
