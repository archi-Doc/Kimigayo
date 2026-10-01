// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using System.Text.Json;
using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// docs/dev/DIAGNOSTICS.md §9.1: the corpus snapshot. Every case is checked through the shared check entry,
// and its published records, outcome and acceptance are compared with a baseline by kind of difference.
public sealed class DiagnosticSnapshotTest : IDisposable
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string directory = Path.Combine(Path.GetTempPath(), "kimi-snapshot-" + Guid.NewGuid().ToString("N"));

    public DiagnosticSnapshotTest() => Directory.CreateDirectory(this.directory);

    /// <summary>Gets the kinds of difference between two snapshots, in the order they are judged.</summary>
    internal static string[] Kinds { get; } = ["case", "acceptance", "code", "severity", "attribution", "location", "text", "order"];

    public void Dispose() => Directory.Delete(this.directory, true);

    [Fact]
    public void SnapshotsAreDeterministic()
    {
        string[] names = ["milestone/Milestone1", "milestone/Milestone24", "mutation/let-counter", "example/Counter"];
        var first = this.Take(names);
        var second = this.Take(names);
        Assert.Equal(Serialize(first), Serialize(second));
        Assert.Contains(first, static x => x.Accepted);
        Assert.Contains(first, static x => !x.Accepted && x.Diagnostics.Length != 0);
    }

    [Fact]
    public void DifferencesAreClassifiedByKind()
    {
        SnapshotDiagnostic Record(string code, string path = "a.kimi", string range = "1:1-1:2", string message = "m", string severity = "Error")
            => new(code, severity, message, path, range);
        SnapshotCase[] baseline =
        [
            new("same", "Completed", false, [Record("A_Kd"), Record("B_Kd", range: "2:1-2:2")]),
            new("moved", "Completed", false, [Record("A_Kd")]),
            new("gone", "Completed", true, []),
        ];
        SnapshotCase[] current =
        [
            new("same", "Completed", false, [Record("B_Kd", range: "2:1-2:2"), Record("A_Kd")]),
            new("moved", "Completed", true, [Record("A_Kd", path: "b.kimi"), Record("C_Kd", message: "new")]),
        ];

        var differences = Classify(baseline, current).Select(static x => string.Join(' ', x.Split('\t')[..2])).ToArray();
        Assert.Equal(["case gone", "acceptance moved", "attribution moved", "code moved", "order same"], differences);
    }

    /// <summary>
    /// Writes the corpus snapshot to <c>KIMI_DIAGNOSTIC_SNAPSHOT</c> and, when <c>KIMI_DIAGNOSTIC_BASELINE</c> names an
    /// earlier snapshot, fails on differences whose kind <c>KIMI_DIAGNOSTIC_ALLOWED</c> (comma-separated) does not list.
    /// <c>scripts/verify.ps1 -DiagnosticSnapshot</c> sets these; without them nothing is requested and the test does nothing.
    /// </summary>
    [Fact]
    public void RequestedSnapshotMatchesTheBaseline()
    {
        var output = Environment.GetEnvironmentVariable("KIMI_DIAGNOSTIC_SNAPSHOT");
        if (string.IsNullOrEmpty(output))
        {
            return;
        }

        var snapshot = this.Take(CaseNames());
        File.WriteAllText(output, Serialize(snapshot), new UTF8Encoding(false));
        if (Environment.GetEnvironmentVariable("KIMI_DIAGNOSTIC_BASELINE") is not { Length: > 0 } baselinePath)
        {
            return;
        }

        var baseline = JsonSerializer.Deserialize<SnapshotCase[]>(File.ReadAllText(baselinePath))!;
        var differences = Classify(baseline, snapshot);
        File.WriteAllLines(Path.ChangeExtension(output, ".differences.txt"), differences, new UTF8Encoding(false));
        var allowed = (Environment.GetEnvironmentVariable("KIMI_DIAGNOSTIC_ALLOWED") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.All(allowed, static x => Assert.Contains(x, Kinds));
        var unexpected = differences.Where(x => !allowed.Contains(x.Split('\t')[0])).ToArray();
        Assert.True(unexpected.Length == 0, string.Join("\n", unexpected.Take(50)));
    }

    /// <summary>Lists the corpus: every milestone program, every example with a project file, every mutation case and every syntax case.</summary>
    /// <returns>The case names.</returns>
    internal static string[] CaseNames()
    {
        var milestones = Directory.GetFiles(DiagnosticCorpus.RepositoryPath("tests", "milestones"), "Milestone*.kimi")
            .Select(static x => "milestone/" + Path.GetFileNameWithoutExtension(x));
        var examples = Directory.GetDirectories(DiagnosticCorpus.RepositoryPath("docs", "examples"))
            .Where(static x => Directory.GetFiles(x, "*.kimiproj").Length == 1)
            .Select(static x => "example/" + Path.GetFileName(x));
        var mutations = DiagnosticCorpus.Mutations.Select(static x => "mutation/" + x.Name);
        var syntax = DiagnosticCorpus.SyntaxCases.Select(static x => "syntax/" + x.Name);
        return [.. milestones.Concat(examples).Concat(mutations).Concat(syntax).Order(StringComparer.Ordinal)];
    }

    /// <summary>Lists the differences between two snapshots as <c>kind TAB case TAB detail</c> lines.</summary>
    /// <param name="baseline">The earlier snapshot.</param>
    /// <param name="current">The current snapshot.</param>
    /// <returns>The differences.</returns>
    internal static List<string> Classify(IReadOnlyList<SnapshotCase> baseline, IReadOnlyList<SnapshotCase> current)
    {
        var differences = new List<string>();
        var earlier = baseline.ToDictionary(static x => x.Name, StringComparer.Ordinal);
        var later = current.ToDictionary(static x => x.Name, StringComparer.Ordinal);
        foreach (var name in earlier.Keys.Union(later.Keys).Order(StringComparer.Ordinal))
        {
            if (!earlier.TryGetValue(name, out var before) || !later.TryGetValue(name, out var after))
            {
                differences.Add($"case\t{name}\t{(before is null ? "added" : "removed")}");
                continue;
            }

            if (before.Outcome != after.Outcome || before.Accepted != after.Accepted)
            {
                differences.Add($"acceptance\t{name}\t{before.Outcome}/{before.Accepted} -> {after.Outcome}/{after.Accepted}");
            }

            if (before.Diagnostics.SequenceEqual(after.Diagnostics))
            {
                continue;
            }

            var removed = before.Diagnostics.ToList();
            var added = new List<SnapshotDiagnostic>();
            foreach (var diagnostic in after.Diagnostics)
            {
                if (!removed.Remove(diagnostic))
                {
                    added.Add(diagnostic);
                }
            }

            if (removed.Count == 0 && added.Count == 0)
            {
                differences.Add($"order\t{name}\t{after.Diagnostics.Length} records");
                continue;
            }

            // Pair a removed and an added record of the same code, then name the first field that differs.
            foreach (var diagnostic in added)
            {
                var index = removed.FindIndex(x => x.Code == diagnostic.Code);
                if (index < 0)
                {
                    differences.Add($"code\t{name}\tadded {diagnostic}");
                    continue;
                }

                var old = removed[index];
                removed.RemoveAt(index);
                var kind = old.Severity != diagnostic.Severity ? "severity" : old.Path != diagnostic.Path ? "attribution" : old.Range != diagnostic.Range ? "location" : "text";
                differences.Add($"{kind}\t{name}\t{old} -> {diagnostic}");
            }

            foreach (var diagnostic in removed)
            {
                differences.Add($"code\t{name}\tremoved {diagnostic}");
            }
        }

        return differences;
    }

    private static string Serialize(SnapshotCase[] snapshot)
        => JsonSerializer.Serialize(snapshot, Options);

    private static string Relative(string root, string? path)
        => path is null ? string.Empty : !Path.IsPathFullyQualified(path) ? path : Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string Format(SourceRange? range)
        => range is { } value ? $"{value.Start.Line + 1}:{value.Start.Character + 1}-{value.End.Line + 1}:{value.End.Character + 1}" : string.Empty;

    private SnapshotCase[] Take(IEnumerable<string> names)
        => [.. names.Select(this.Take)];

    private SnapshotCase Take(string name)
    {
        var kind = name[..name.IndexOf('/')];
        var subject = name[(kind.Length + 1)..];
        string project;
        if (kind == "example")
        {
            project = Directory.GetFiles(DiagnosticCorpus.RepositoryPath("docs", "examples", subject), "*.kimiproj").Single();
        }
        else
        {
            var source = kind switch
            {
                "milestone" => File.ReadAllText(DiagnosticCorpus.RepositoryPath("tests", "milestones", subject + ".kimi")),
                "syntax" => DiagnosticCorpus.Syntax(subject).Source,
                _ => DiagnosticCorpus.Apply(DiagnosticCorpus.Mutation(subject)),
            };
            var folder = Path.Combine(this.directory, kind, subject);
            Directory.CreateDirectory(folder);
            project = Path.Combine(folder, "Program.kimiproj");
            File.WriteAllText(project, $"OutputKind=\"Application\" Targets={{\"{WindowsProfile.Target}\"}}");
            File.WriteAllText(Path.Combine(folder, "Program.kimi"), source);
        }

        Assert.True(Project.TryCreate(Kimigayo.CreateSilent(), null, project, CheckInputSource.Disk, out var loaded, out var failure), failure);
        var output = CheckService.Run(loaded, WindowsProfile.Target, CheckMode.Product, false, CheckInputSource.Disk, TestContext.Current.CancellationToken);
        var root = Path.GetDirectoryName(project)!;
        var diagnostics = output.Diagnostics.Select(x => new SnapshotDiagnostic(x.Code, x.Severity.ToString(), x.Message, Relative(root, x.Source < 0 ? null : output.Sources[x.Source].Path), Format(x.Display?.Range))).ToArray();
        return new(name, output.Outcome.ToString(), output.Accepted, diagnostics);
    }

    /// <summary>One case of a snapshot.</summary>
    /// <param name="Name">The case name.</param>
    /// <param name="Outcome">The outcome.</param>
    /// <param name="Accepted">Whether the result is accepted.</param>
    /// <param name="Diagnostics">The published records in result order.</param>
    internal sealed record SnapshotCase(string Name, string Outcome, bool Accepted, SnapshotDiagnostic[] Diagnostics);

    /// <summary>One published record as the snapshot compares it.</summary>
    /// <param name="Code">The code.</param>
    /// <param name="Severity">The severity.</param>
    /// <param name="Message">The message.</param>
    /// <param name="Path">The source path relative to the project, a built-in identity, or empty.</param>
    /// <param name="Range">The one-based range, or empty.</param>
    internal sealed record SnapshotDiagnostic(string Code, string Severity, string Message, string Path, string Range)
    {
        public override string ToString()
            => $"{this.Code} {this.Path}:{this.Range} {this.Severity} \"{this.Message}\"";
    }
}
