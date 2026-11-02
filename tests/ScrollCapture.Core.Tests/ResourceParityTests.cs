using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace ScrollCapture.Core.Tests
{
    /// <summary>
    /// Every UI string exists in all languages with the same placeholders, and every option
    /// and stop reason the UI shows has a string.
    /// </summary>
    public class ResourceParityTests
    {
        private static readonly string[] Files = { "Strings.resx", "Strings.uk.resx" };

        private static readonly Regex Placeholder = new Regex(@"\{\d+\}");

        private static string RepositoryRoot()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ScrollCapture.sln")))
            {
                dir = dir.Parent;
            }

            Assert.NotNull(dir);
            return dir.FullName;
        }

        private static Dictionary<string, string> Load(string file)
        {
            var doc = XDocument.Load(Path.Combine(RepositoryRoot(), "src", "ScrollCapture.App", "Resources", file));
            return doc.Root.Elements("data").ToDictionary(e => (string)e.Attribute("name"), e => (string)e.Element("value"));
        }

        // Member names of an enum declared in the application sources (not referenced by the tests).
        private static IEnumerable<string> EnumMembers(string relativePath, string enumName)
        {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "ScrollCapture.App", relativePath));
            var body = Regex.Match(source, @"enum\s+" + enumName + @"\s*\{(?<body>[^}]*)\}").Groups["body"].Value;
            body = Regex.Replace(body, @"//[^\n]*|\[[^\]]*\]", string.Empty);
            var names = Regex.Matches(body, @"\b([A-Z]\w*)\b\s*(,|$)").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            Assert.NotEmpty(names);
            return names;
        }

        [Fact]
        public void AllLanguages_HaveIdenticalKeys_AndNoEmptyValues()
        {
            var english = Load(Files[0]);
            Assert.NotEmpty(english);
            foreach (var file in Files)
            {
                var table = Load(file);
                Assert.Equal(english.Keys.OrderBy(k => k, StringComparer.Ordinal), table.Keys.OrderBy(k => k, StringComparer.Ordinal));
                Assert.All(table, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), file + ": empty " + pair.Key));
            }
        }

        [Fact]
        public void Placeholders_MatchEnglish()
        {
            var english = Load(Files[0]);
            foreach (var file in Files.Skip(1))
            {
                var table = Load(file);
                foreach (var pair in english)
                {
                    var expected = Placeholder.Matches(pair.Value).Cast<Match>().Select(m => m.Value).OrderBy(v => v);
                    var actual = Placeholder.Matches(table[pair.Key]).Cast<Match>().Select(m => m.Value).OrderBy(v => v);
                    Assert.True(expected.SequenceEqual(actual), file + ": placeholders differ in " + pair.Key);
                }
            }
        }

        [Fact]
        public void FileDialogFilter_KeepsItsPatterns()
        {
            var english = Load(Files[0])["Dialog_ImageFilter"].Split('|').Where((p, i) => i % 2 == 1).ToArray();
            Assert.Equal(new[] { "*.png", "*.jpg", "*.bmp", "*.tif" }, english);
            foreach (var file in Files)
            {
                var parts = Load(file)["Dialog_ImageFilter"].Split('|');
                Assert.Equal(english, parts.Where((p, i) => i % 2 == 1).ToArray());
            }
        }

        [Fact]
        public void EveryDisplayedEnumValue_HasAString()
        {
            var english = Load(Files[0]);
            var required = EnumMembers(@"Capture\Scroller.cs", "ScrollMethod").Select(n => "Scroll_" + n)
                .Concat(EnumMembers(@"Capture\FrameGrabber.cs", "CaptureMethod").Select(n => "Capture_" + n))
                .Concat(EnumMembers(@"Capture\CaptureSession.cs", "StopReason").Select(n => "Stop_" + n));
            foreach (var key in required)
            {
                Assert.True(english.ContainsKey(key), "Missing string " + key);
            }
        }
    }
}
