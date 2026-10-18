// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1600, SA1118, SA1513

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// Scratch census of A5 step 2 unit 2a0-0 (kept in draft/Sketches/A5 Step 2/f4-census-probe, never in tests/). With F4_OUT naming a
// file, compiles every input on the default pipeline (Bind, startup check, Analyze) and writes one tab-separated line per old-path
// record, per extra Binding Origin record, per Binding obligation row and per F4Probe event of the input's own nodes (the library's
// for the library input). F4_SETS selects the input sets; F4_FIXDIR and F4_LEDGER name source folders in the ledger's format.
public sealed class F4CensusProbe
{
    private static readonly Regex Header = new(@"^// (\S*)$", RegexOptions.CultureInvariant);

    [Fact]
    public void Census()
    {
        var output = Environment.GetEnvironmentVariable("F4_OUT");
        if (output is null)
        {
            return;
        }

        var sets = (Environment.GetEnvironmentVariable("F4_SETS") ?? "library,milestone,golden,spec").Split(',');
        var text = new StringBuilder();
        if (sets.Contains("library"))
        {
            Run(text, "library", "empty", [("Program.kimi", "func main() => ()\n")], library: true);
        }

        if (sets.Contains("milestone"))
        {
            foreach (var file in Directory.GetFiles(DiagnosticCorpus.RepositoryPath("tests", "milestones"), "Milestone*.kimi").Order(StringComparer.Ordinal))
            {
                Run(text, "milestone", Path.GetFileName(file), [(Path.GetFileName(file), File.ReadAllText(file))]);
            }
        }

        if (sets.Contains("golden"))
        {
            foreach (var file in Directory.GetFiles(DiagnosticCorpus.RepositoryPath("tests", "mir"), "*.kimi").Order(StringComparer.Ordinal))
            {
                Run(text, "golden", Path.GetFileName(file), [("Program.kimi", File.ReadAllText(file))]);
            }
        }

        if (sets.Contains("spec"))
        {
            foreach (var example in DiagnosticCorpus.SpecCases)
            {
                for (var i = 0; i < example.Variants.Length; i++)
                {
                    var variant = example.Variants[i];
                    var expected = string.Join(",", variant.Expected.Select(static x => x.Code));
                    Run(text, "spec", $"{example.Name}/{i}[{expected}]", [("Hello.kimi", DiagnosticCorpus.Source(example, variant))]);
                }
            }
        }

        foreach (var (set, variable) in new[] { ("fixture", "F4_FIXDIR"), ("ledger", "F4_LEDGER") })
        {
            if (sets.Contains(set) && Environment.GetEnvironmentVariable(variable) is { } folder)
            {
                foreach (var file in Directory.GetFiles(folder, "*.kimi").Order(StringComparer.Ordinal))
                {
                    if (Documents(File.ReadAllText(file).ReplaceLineEndings("\n")) is { } documents)
                    {
                        Run(text, set, Path.GetFileNameWithoutExtension(file), documents);
                    }
                    else
                    {
                        text.Append(set).Append('\t').Append(Path.GetFileNameWithoutExtension(file)).Append("\t-\tSKIP\tunparsed\n");
                    }
                }
            }
        }

        File.WriteAllText(output, text.ToString(), new UTF8Encoding(false));
    }

    // The documents of a source in the ledger's format: "// name" then the text, per document.
    private static (string Path, string Text)[]? Documents(string all)
    {
        var lines = all.Split('\n');
        if (lines.Length == 0 || Header.Match(lines[0]) is not { Success: true } first)
        {
            return null;
        }

        var documents = new List<(string, string)>();
        var name = first.Groups[1].Value;
        var start = 1;
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("// ", StringComparison.Ordinal) && lines[i].EndsWith(".kimi", StringComparison.Ordinal) && !lines[i][3..].Contains(' ', StringComparison.Ordinal))
            {
                documents.Add((name, string.Join('\n', lines[start..i])));
                name = lines[i][3..];
                start = i + 1;
            }
        }

        // The writer appends "\n" after each document's text.
        var body = string.Join('\n', lines[start..]);
        documents.Add((name, body.EndsWith('\n') ? body[..^1] : body));
        return [.. documents];
    }

    private static void Run(StringBuilder text, string set, string name, (string Path, string Text)[] documents, bool library = false)
    {
        lock (F4Probe.Events)
        {
            F4Probe.Events.Clear();
        }

        var prefix = string.Empty;
        try
        {
            F4Probe.Enabled = true;
            var c = Compilation.CreateForTest();
            var test = documents.Any(static x => x.Text.Contains("#Test", StringComparison.Ordinal));
            c.IsTestBuild = test;
            Assert.True(c.Prepare(WindowsProfile.Target));
            foreach (var (path, source) in documents)
            {
                if (path.Length == 0)
                {
                    c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
                }
                else
                {
                    c.Kotonoha.AddSource(new SourceDocument(path, source));
                }
            }

            c.Bind();
            _ = test ? c.Binding.CheckTestStartup() : c.Binding.CheckStartup(OutputKind.Application);
            c.Analyze();
            c.Binding.ProbeIndependentFunctions();
            F4Probe.Enabled = false;
            var hashed = new StringBuilder();
            foreach (var (path, source) in documents)
            {
                hashed.Append("// ").Append(Path.GetFileName(path)).Append('\n').Append(source).Append('\n');
            }

            var sha = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(hashed.ToString())))[..12];
            prefix = $"{set}\t{name}\t{sha}\t";
            bool InScope(Koto? node) => node?.CodeContext?.SourceDocument?.Path?.StartsWith("compiler://", StringComparison.Ordinal) == true ? library : !library;
            foreach (var (requirement, code, node) in LegacyRecords.Of(c))
            {
                if (InScope(node))
                {
                    text.Append(prefix).Append("REC\t").Append(requirement.Name).Append('\t').Append(code).Append('\t').Append(Where(node)).Append('\n');
                }
            }

            foreach (var issue in c.Binding.Issues)
            {
                if (InScope(issue.Node) && c.Binding.ProbeMoreRelations(issue.Node) is { } more)
                {
                    foreach (var fact in more)
                    {
                        text.Append(prefix).Append("MORE\t").Append(issue.Code).Append('\t').Append(Binding.OriginRelationCode(fact)).Append('\t').Append(Where(issue.Node))
                            .Append('\t').Append(F4Probe.Show(fact.Longer)).Append('\t').Append(F4Probe.Show(fact.Shorter)).Append('\n');
                    }
                }
            }

            foreach (var obligation in c.Binding.Obligations)
            {
                if (!InScope(obligation.Use))
                {
                    continue;
                }

                var verdict = "-";
                if (obligation is { Kind: BindingObligationKind.OriginOutlives, Longer: not null, Shorter: not null })
                {
                    verdict = c.Binding.IsVerifiedOriginObligation(obligation) ? "Verified" : c.Binding.JudgeOriginObligation(obligation, out var reversed) + (reversed ? "(rev)" : string.Empty);
                }

                text.Append(prefix).Append("ROW\t").Append(obligation.Kind).Append('\t').Append(obligation.Discharge).Append('\t').Append(obligation.Deadline).Append('\t')
                    .Append(obligation.Clause is null ? "-" : "clause").Append('\t').Append(obligation.WellFormed ? "wf" : "-").Append('\t').Append(obligation.Equality ? "==" : ">=").Append('\t')
                    .Append(F4Probe.Class(obligation.Longer)).Append('\t').Append(F4Probe.Class(obligation.Shorter)).Append('\t').Append(verdict).Append('\t')
                    .Append(Where(obligation.Use)).Append('\t').Append(F4Probe.Show(obligation.Longer)).Append('\t').Append(F4Probe.Show(obligation.Shorter)).Append('\n');
            }

            (string, Koto?, string)[] events;
            lock (F4Probe.Events)
            {
                events = [.. F4Probe.Events.Distinct()];
            }

            foreach (var (kind, node, detail) in events)
            {
                if (InScope(node))
                {
                    text.Append(prefix).Append("EV\t").Append(kind).Append('\t').Append(Where(node)).Append('\t').Append(detail).Append('\n');
                }
            }

            text.Append(prefix).Append("SUM\tcomplete=").Append(c.Binding.Result.IsComplete).Append('\n');
        }
        catch (Exception e)
        {
            text.Append(set).Append('\t').Append(name).Append("\t-\tEXC\t").Append(e.GetType().Name).Append('\t').Append(e.Message.Split('\n')[0]).Append('\n');
        }
        finally
        {
            F4Probe.Enabled = false;
        }
    }

    // A node's ledger location (document:start+length), line, form, enclosing function and first source line.
    private static string Where(Koto? node)
    {
        if (node is null)
        {
            return "?\t-\t-\t-\t-";
        }

        var line = node.CodeContext?.SourceDocument is { } document ? (document.GetPosition(node.Span.Start).Line + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : "-";
        var shown = (node.ToString() ?? "?").Split('\n')[0].Trim();
        if (shown.Length > 80)
        {
            shown = shown[..80];
        }

        return $"{LegacyRecords.Location(node)}+{node.Span.Length}\tL{line}\t{node.GetType().Name}\t{F4Probe.Function(node)}\t{shown.Replace('\t', ' ')}";
    }
}
