using System;
using System.Text;
using System.Text.RegularExpressions;

namespace TheMovies.Core.Build
{
    /// <summary>Text edits on the generated Gradle project (dependencies, properties, ProGuard rules).</summary>
    public static class GradleEdits
    {
        /// <summary>
        /// Makes sure each "group:name:version" is a dependency: missing ones are added to the first top-level
        /// dependencies block, existing ones with another version are switched to the given version (the generated
        /// Gradle project can survive from an earlier build).
        /// </summary>
        public static string AddDependencies(string buildGradle, params string[] coordinates)
        {
            var sb = new StringBuilder();
            foreach (string c in coordinates)
            {
                int lastColon = c.LastIndexOf(':');
                string artifact = c.Substring(0, lastColon);
                string version = c.Substring(lastColon + 1);
                var existing = new Regex(@"(['""])" + Regex.Escape(artifact) + @":([^'""]+)(['""])");
                if (existing.IsMatch(buildGradle))
                {
                    buildGradle = existing.Replace(buildGradle, m => m.Groups[1].Value + artifact + ":" + version + m.Groups[3].Value);
                    continue;
                }
                sb.Append("\n    implementation '").Append(c).Append('\'');
            }
            if (sb.Length == 0) return buildGradle;
            var block = new Regex(@"(^|\n)dependencies\s*\{");
            var match = block.Match(buildGradle);
            if (!match.Success)
                return buildGradle.TrimEnd() + "\n\ndependencies {" + sb + "\n}\n";
            return buildGradle.Insert(match.Index + match.Length, sb.ToString());
        }

        /// <summary>Removes "implementation 'group:name:…'" lines of an artifact (e.g. after switching an option off).</summary>
        public static string RemoveDependency(string buildGradle, string artifact)
        {
            return Regex.Replace(buildGradle, @"[ \t]*implementation\s*\(?\s*['""]" + Regex.Escape(artifact) + @":[^'""]*['""]\s*\)?[ \t]*\r?\n?", string.Empty);
        }

        /// <summary>The compileSdk of a build.gradle ("compileSdk 34", "compileSdkVersion = 34"), or -1.</summary>
        public static int CompileSdk(string buildGradle)
        {
            var m = Regex.Match(buildGradle, @"compileSdk(Version)?\s*=?\s*(\d+)");
            return m.Success ? int.Parse(m.Groups[2].Value) : -1;
        }

        /// <summary>Raises compileSdk to <paramref name="sdk"/> if it is lower (the target SDK stays as it is).</summary>
        public static string RaiseCompileSdk(string buildGradle, int sdk)
        {
            return Regex.Replace(buildGradle, @"(compileSdk(Version)?\s*=?\s*)(\d+)",
                m => int.Parse(m.Groups[3].Value) < sdk ? m.Groups[1].Value + sdk : m.Value);
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
