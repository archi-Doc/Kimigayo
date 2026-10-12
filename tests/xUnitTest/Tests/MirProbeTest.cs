// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>The MIR probe of the R4 analysis units, a no-op without <c>KIMI_MIR_PROBE</c>: for that <c>.kimi</c> program, or each one in
/// that directory, it writes <c>&lt;name&gt;.probe.txt</c> into <c>KIMI_MIR_PROBE_OUT</c> (default: the program's directory) with the
/// published diagnostics of the process's pipeline and, from one analysis under mir, the old path's records (<see cref="LegacyRecords"/>),
/// the MIR of the source bodies with A2's drops, the construction records and validator findings (<see cref="MirDump"/>), the analyses'
/// problems, A5-1's Loans per body view with those active before each location (<see cref="MirBorrows.Replay"/>) and the difference
/// ledger's rows and classified differences (<see cref="MirDifferenceLedger"/>). A4's summaries are private to <see cref="MirEffects"/>
/// and not shown.</summary>
public sealed class MirProbeTest
{
    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("KIMI_MIR_PROBE") is not { Length: > 0 } input)
        {
            return;
        }

        string[] paths = Directory.Exists(input) ? [.. Directory.GetFiles(input, "*.kimi").Order(StringComparer.Ordinal)] : [input];
        var output = Environment.GetEnvironmentVariable("KIMI_MIR_PROBE_OUT") is { Length: > 0 } value ? Directory.CreateDirectory(value).FullName : null;
        var failed = new List<string>();
        foreach (var path in paths)
        {
            var text = new StringBuilder();
            try
            {
                Write(File.ReadAllText(path), text);
            }
            catch (Exception exception)
            {
                failed.Add(path); // The other programs are still probed; the fault is in this one's output and fails the test below.
                text.Append("== fault\n").Append(exception).Append('\n');
            }

            var directory = output ?? Path.GetDirectoryName(Path.GetFullPath(path))!;
            File.WriteAllText(Path.Combine(directory, Path.GetFileNameWithoutExtension(path) + ".probe.txt"), text.ToString());
        }

        Assert.Empty(failed);
    }

    // The probe of one program: the published check, then one Binding pass under mir, analyzed once under a fresh ledger observer.
    private static void Write(string source, StringBuilder text)
    {
        var check = DiagnosticCorpus.Check(source);
        text.Append($"== published diagnostics ({Compilation.DefaultPipeline} pipeline, CheckService)\n");
        foreach (var d in check.Diagnostics)
        {
            var document = d.Source < 0 ? null : check.Sources[d.Source];
            var at = document is null ? "-" : d.Span is { } span && document.IsInput ? Where(source, span.Start, span.Length) : document.Path;
            text.Append($"{d.Severity} {d.Code} at {at}\n");
        }

        var c = MirBuildTest.Compile(source);
        c.Pipeline = CompilerPipeline.Mir; // Revokes the Binding pass: bound again under mir, which the ledger compares.
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        var ledger = Ledger(c);
        text.Append("== old records after the analysis (Binding, control flow, ownership): requirement code at subject\n");
        foreach (var (requirement, code, node) in LegacyRecords.Of(c))
        {
            text.Append($"{requirement.Name} {code} at {Where(source, node)}\n");
        }

        var program = c.BuildMir();
        text.Append("== MIR of the source bodies with A2's drops, then the construction records and validator findings\n");
        MirDump.Write(program, 0, text);
        text.Append("== MIR problems (A1-A5): code at subject, views\n");
        var library = 0;
        foreach (var problem in program.Problems.All)
        {
            if (problem.Subject.Module != 0)
            {
                library++;
                continue;
            }

            var views = problem.Views == ulong.MaxValue ? "all" : "0x" + problem.Views.ToString("x", CultureInfo.InvariantCulture);
            text.Append($"{problem.Code} at {Where(source, c.SyntaxOf(problem.Subject))}, views {views}\n");
        }

        Loans(c, program, text.Append($"library problems: {library}\n"));
        text.Append(ledger);
    }

    // A5-1 per source body and view, as MirBorrowTest analyzes one: its Loans (Ln = kind, * for a referent, Place, where formed), then the
    // Loans active before each location that has any, with the first line of the location's subject.
    private static void Loans(Compilation c, MirProgram program, StringBuilder text)
    {
        text.Append("== A5-1 Loans per source body view, then the Loans active before each location with any\n");
        foreach (var body in program.Bodies)
        {
            for (var view = 0L; program.Findings.Count == 0 && body.Source.Module == 0 && view < MirCases.Count(body); view++)
            {
                var (borrows, _) = MirBorrowTest.Analyze(c, program, body, view);
                var name = body.Key.Decl?.Name ?? "top-level"; // As the dump's header names the body, with its @id.
                text.Append($"{body.Key.Kind.ToString().ToLowerInvariant()}{(name.Length == 0 ? string.Empty : " " + name)} @{body.Source.Id} view {view}:");
                var loans = borrows.Loans;
                for (var i = 0; i < loans.Length; i++)
                {
                    var (at, place) = (loans[i].At, loans[i].Place);
                    var root = (loans[i].Referent ? "*" : string.Empty) + (place.IsStatic ? body.Statics[~place.Root].Symbol.Name : "_" + place.Root);
                    text.Append($" L{i}={loans[i].Kind} {root}{(place.Projection == 0 ? string.Empty : "/p" + place.Projection)} at bb{at.Block}[{at.Index}]");
                }

                var replay = new Replay(c, body, text.Append('\n'));
                borrows.Replay(ref replay);
            }
        }
    }

    // The difference ledger's own comparison of the program (R4-M17): a fresh observer over a scratch directory, as KIMI_MIR_DIFF makes
    // one, records the analysis; its raw rows without the comparison and the hash, then its differences with their classification.
    private static string Ledger(Compilation c)
    {
        var directory = Path.Combine(Path.GetTempPath(), "kimi-mir-probe-" + Guid.NewGuid().ToString("N"));
        var previous = Environment.GetEnvironmentVariable("KIMI_MIR_DIFF");
        Environment.SetEnvironmentVariable("KIMI_MIR_DIFF", directory);
        try
        {
            using (new MirDifferenceLedger())
            {
                c.Analyze();
            }

            var text = new StringBuilder("== difference ledger rows: section side analysis code requirement location form body\n");
            foreach (var row in File.ReadLines(Directory.GetFiles(directory, "raw-*.tsv").Single()))
            {
                text.AppendJoin(' ', row.Split('\t')[1..^1]).Append('\n');
            }

            text.Append("== differences: section kind analysis code requirement form count category reference\n");
            foreach (var row in File.ReadLines(Path.Combine(directory, "ledger.tsv")).Skip(1))
            {
                text.AppendJoin(' ', row.Split('\t')[..^1]).Append('\n');
            }

            return text.ToString();
        }
        finally
        {
            Environment.SetEnvironmentVariable("KIMI_MIR_DIFF", previous);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    // A program node's line, column and first line of text; the location of a node outside the program.
    private static string Where(string source, Koto node)
        => LegacyRecords.Location(node) is var location && location.StartsWith("Program.kimi:", StringComparison.Ordinal)
            ? Where(source, node.Span.Start, node.Span.Length) : location;

    private static string Where(string source, int start, int length)
    {
        var line = source.AsSpan(0, start).Count('\n') + 1;
        var column = start - (start == 0 ? 0 : source.LastIndexOf('\n', start - 1) + 1) + 1;
        return $"{line}:{column} '{source.Substring(start, Math.Min(length, source.Length - start)).Split('\n')[0].Trim()}'";
    }

    // Writes the Loans active before each location that has any, with the first line of the location's subject.
    private readonly struct Replay(Compilation c, MirBody body, StringBuilder text) : IMirLoans
    {
        public void At(MirLocation location, ReadOnlySpan<ulong> active)
        {
            if (!active.ContainsAnyExcept(0UL))
            {
                return;
            }

            text.Append($"  bb{location.Block}[{location.Index}]");
            for (var i = 0; i < active.Length * 64; i++)
            {
                text.Append((active[i >> 6] & (1UL << (i & 63))) != 0 ? $" L{i}" : string.Empty);
            }

            ref readonly var block = ref body.Blocks[location.Block];
            var source = location.Index < block.Count ? body.Statements[block.Start + location.Index].Source : block.Terminator.Source;
            text.Append(" '").Append(MirBorrowTest.Subject(c, body, source)).Append("'\n");
        }
    }
}
