using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Pulswerk.Dashboard.Tests
{
    public class TranslationCoverageTests
    {
        private static readonly string RootPath = FindRootPath();
        private static readonly string WwwRoot = Path.Combine(RootPath, "src", "Pulswerk.Dashboard", "wwwroot");
        private static readonly string PagesRoot = Path.Combine(RootPath, "src", "Pulswerk.Dashboard", "Pages");

        private static string FindRootPath()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Pulswerk.sln")))
            {
                dir = dir.Parent;
            }
            return dir?.FullName ?? throw new Exception("Could not find Pulswerk.sln");
        }

        [Fact]
        public void All_I18n_Keys_Should_Be_Translated_In_All_Languages()
        {
            // 1. Get defined keys from i18n.js
            var i18nJsPath = Path.Combine(WwwRoot, "js", "i18n.js");
            Assert.True(File.Exists(i18nJsPath), "i18n.js not found at " + i18nJsPath);
            var i18nContent = File.ReadAllText(i18nJsPath);

            var enKeys = ExtractKeysFromJs(i18nContent, "'en'");
            var deKeys = ExtractKeysFromJs(i18nContent, "'de'");

            Assert.NotEmpty(enKeys);
            Assert.NotEmpty(deKeys);
            Assert.Equal(enKeys.OrderBy(x => x), deKeys.OrderBy(x => x));

            // 2. Scan for used keys in .cshtml and .js files
            var usedKeys = new HashSet<string>();
            var filesToScan = Directory.GetFiles(PagesRoot, "*.cshtml", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(WwwRoot, "*.js", SearchOption.AllDirectories))
                .Where(f => !f.EndsWith("i18n.js") && !f.Contains("lib"));

            foreach (var file in filesToScan)
            {
                var content = File.ReadAllText(file);

                if (file.EndsWith(".cshtml"))
                {
                    // Matches data-i18n="key" and data-i18n-title="key"
                    var matches = Regex.Matches(content, @"data-i18n(?:-title)?=[""']([^""']+)[""']");
                    foreach (Match match in matches)
                    {
                        usedKeys.Add(match.Groups[1].Value);
                    }
                }

                // Matches t('key') or t("key") in both .cshtml and .js
                var tMatches = Regex.Matches(content, @"\bt\([""']([^""']+)[""']\)");
                foreach (Match match in tMatches)
                {
                    usedKeys.Add(match.Groups[1].Value);
                }
            }

            // 3. Verify coverage
            var missingInEn = usedKeys.Where(k => !enKeys.Contains(k)).ToList();
            var missingInDe = usedKeys.Where(k => !deKeys.Contains(k)).ToList();

            Assert.Empty(missingInEn);
            Assert.Empty(missingInDe);
        }

        private HashSet<string> ExtractKeysFromJs(string content, string langHeader)
        {
            var keys = new HashSet<string>();
            var lines = content.Split('\n');
            bool inBlock = false;

            foreach (var line in lines)
            {
                if (line.Contains(langHeader + ": {") || line.Contains(langHeader + " : {"))
                {
                    inBlock = true;
                    continue;
                }
                if (inBlock && (line.Trim() == "}," || line.Trim() == "}"))
                {
                    inBlock = false;
                    continue;
                }

                if (inBlock)
                {
                    var match = Regex.Match(line, @"['""]([^'""\s]+)['""]\s*:");
                    if (match.Success)
                    {
                        keys.Add(match.Groups[1].Value);
                    }
                }
            }

            return keys;
        }
    }
}
