// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using System.Text.RegularExpressions;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// README is the source/native status record. A failed target must retain its recorded failure stage
// and diagnostic anchor; it cannot pass merely because some Binding error still exists.
public class MilestoneSourcesTest
{
    private static readonly string DirectoryPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../milestones"));

    [Fact]
    public void AuthoredProgramsMatchTheirRecordedStageAndDiagnostic()
    {
        var readme = File.ReadAllText(Path.Combine(DirectoryPath, "README.md"));
        var rows = Regex.Matches(readme, @"^\| (\d+) \| (YES|NO \(planned\)) \| ([^|]+) \|", RegexOptions.Multiline);
        Assert.Equal(Enumerable.Range(1, 38), rows.Select(x => int.Parse(x.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)));
        var baselines = JsonSerializer.Deserialize<Baseline[]>(File.ReadAllText(Path.Combine(DirectoryPath, "stage-baselines.json")))!;
        Assert.Equal(baselines.Length, baselines.Select(x => x.Program).Distinct().Count());
        var pending = baselines.ToDictionary(x => x.Program);
        var authored = new List<int>();
        var differences = new List<string>();
        foreach (Match row in rows)
        {
            var number = int.Parse(row.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var path = Path.Combine(DirectoryPath, $"Milestone{number}.kimi");
            var created = row.Groups[2].Value == "YES";
            Assert.Equal(created, File.Exists(path));
            if (!created)
            {
                Assert.False(pending.ContainsKey(number));
                continue;
            }

            authored.Add(number);
            var source = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
            var c = Bind(source, Path.GetFileName(path));
            Assert.Equal(c.Binding.Result.IsComplete ? "PASS" : "FAIL", row.Groups[3].Value.Trim());
            if (!pending.Remove(number, out var expected))
            {
                Assert.StartsWith("PASS", row.Groups[3].Value.Trim());
                Assert.True(c.Binding.Result.IsComplete, $"Milestone{number}: {string.Join("\n", c.Binding.Issues)}");
                continue;
            }

            var stage = "Binding";
            string diagnostic;
            string anchor;
            if (c.Binding.Result.IsComplete)
            {
                stage = "Ownership";
                Assert.False(c.Ownership.Analyze().IsVerified, $"Milestone{number} advanced: update its verified stage and README.");
                Assert.NotEmpty(c.Ownership.Issues);
                var primary = c.Ownership.Issues[0];
                diagnostic = primary.Code.ToString();
                anchor = primary.Source.ToString().Split('\n')[0];
            }
            else
            {
                Assert.NotEmpty(c.Binding.Issues);
                var primary = c.Binding.Issues[0];
                diagnostic = primary.Code.ToString();
                anchor = primary.Node.ToString().Split('\n')[0];
            }

            if (expected.Stage != stage || expected.Diagnostic != diagnostic || expected.Anchor != anchor)
            {
                differences.Add($"Milestone{number}: expected {expected.Stage} {expected.Diagnostic} at {expected.Anchor}; actual {stage} {diagnostic} at {anchor}");
            }

            // Verify the already-supported declaration subset independently. A pending target must
            // not hide regressions in declarations before its first unsupported feature.
            var end = source.IndexOf(expected.SupportedPrefixEnd, StringComparison.Ordinal);
            Assert.True(end > 0, $"Milestone{number}: missing subset anchor {expected.SupportedPrefixEnd}");
            Assert.Equal(end, source.LastIndexOf(expected.SupportedPrefixEnd, StringComparison.Ordinal));
            var subset = Bind(source[..end], Path.GetFileName(path));
            Assert.True(subset.Binding.Result.IsComplete, $"Milestone{number} supported subset: {string.Join("\n", subset.Binding.Issues)}");
        }

        Assert.Empty(pending);
        Assert.True(differences.Count == 0, string.Join("\n", differences));
        var files = Directory.GetFiles(DirectoryPath, "Milestone*.kimi")
            .Select(x => int.Parse(Path.GetFileNameWithoutExtension(x)["Milestone".Length..], System.Globalization.CultureInfo.InvariantCulture)).Order();
        Assert.Equal(authored, files); // A removed, renamed or unrecorded source is never accepted by a minimum count.
    }

    private static Compilation Bind(string source, string path)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument(path, source));
        Assert.False(c.Kotonoha.DiagnosticCollection.HasErrors);
        c.Bind();
        return c;
    }

    private sealed record Baseline(int Program, string Stage, string Diagnostic, string Anchor, string SupportedPrefixEnd);
}
