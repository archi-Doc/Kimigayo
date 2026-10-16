// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

[assembly: AssemblyFixture(typeof(XunitTest.MirDifferenceLedger))]

namespace XunitTest;

/// <summary>The MIR difference ledger (docs/dev/DECISIONS.md R4-M17; R4–R6). With <c>KIMI_MIR_DIFF</c> naming a directory, the test
/// assembly observes every analysis (<see cref="Compilation.Analyzed"/>) and, once per mir-pipeline compilation and Binding pass, matches
/// the old path's A-owned records (<see cref="LegacyRecords"/>, the compared set of R4-M4) with the MIR's construction records and
/// problems on code and subject node, appending one row per record to <c>raw-&lt;pid&gt;.tsv</c> there and saving the source once as
/// <c>sources/&lt;sha&gt;.kimi</c>. Disposal aggregates the rows (<see cref="Aggregate"/>). Without the variable nothing is observed.
/// </summary>
/// <remarks>A raw row is <c>event section side analysis code requirement location form body sha</c>: the comparison, complete or
/// incomplete Binding (R4-M5), both, old or new, the owning analysis (A1, A3, A4, A5, U for an old Unsupported gate, - for the expansion
/// limit, B for a MIR construction record; a MIR problem takes its code's), the code, the old requirement (- on the MIR side, R4-M3a), the
/// subject's document, offset and length, its syntax form, the location of its enclosing definition and the source's hash.</remarks>
public sealed class MirDifferenceLedger : IDisposable
{
    internal const int Event = 0;
    internal const int Section = 1;
    internal const int Side = 2;
    internal const int Analysis = 3;
    internal const int Code = 4;
    internal const int Requirement = 5;
    internal const int Form = 7;
    internal const int Body = 8;
    internal const int Sha = 9;

    // R4-M4: the compared old records, by requirement and each code it reports (OwnershipIssue.Code, Binding's code of the failure), with
    // the analysis that owns them; a MIR problem takes its code's analysis. Every other record passes through: TransferRequired and
    // Internal, control flow's other codes and Binding's other failures, CaseLimit included. Binding's IncompatibleImplementation and
    // Unsupported records are compared only where an effect check made them (Binding.IsEffectRecord, R4-M23).
    internal static readonly (DiagnosticRequirement Requirement, DiagnosticCode Code, string Analysis)[] Compared =
    [
        (DiagnosticRequirement.Ownership(OwnershipFailure.UninitializedUse), DiagnosticCode.UninitializedPlace_Kd, "A1"),
        (DiagnosticRequirement.Ownership(OwnershipFailure.PossiblyMovedUse), DiagnosticCode.MovedPlace_Kd, "A1"),
        (DiagnosticRequirement.Ownership(OwnershipFailure.ReassignedLet), DiagnosticCode.ReassignedLet_Kd, "A1"),
        (DiagnosticRequirement.Ownership(OwnershipFailure.StaticMovePathRequired), DiagnosticCode.StaticMovePathRequired_Kd, "A1"),
        (DiagnosticRequirement.Ownership(OwnershipFailure.DefaultArgumentMove), DiagnosticCode.DefaultArgumentMove_Kd, "A1"),
        (DiagnosticRequirement.ControlFlow, DiagnosticCode.RequireFallthrough_Kd, "A3"),
        (DiagnosticRequirement.Ownership(OwnershipFailure.EffectBound), DiagnosticCode.IncompatibleContractImplementation_Kd, "A4"),
        (DiagnosticRequirement.Ownership(OwnershipFailure.CallableEffectBound), DiagnosticCode.UnsatisfiedEffectBound_Kd, "A4"),
        (DiagnosticRequirement.Ownership(OwnershipFailure.VirtualEffectBound), DiagnosticCode.UnsatisfiedEffectBound_Kd, "A4"),
        (DiagnosticRequirement.Binding(BindingFailure.VirtualEffectBound), DiagnosticCode.UnsatisfiedEffectBound_Kd, "A4"),
        (DiagnosticRequirement.Binding(BindingFailure.IncompatibleImplementation), DiagnosticCode.IncompatibleContractImplementation_Kd, "A4"),
        (DiagnosticRequirement.Binding(BindingFailure.Unsupported), DiagnosticCode.Unsupported_Kd, "U"),
        (DiagnosticRequirement.Ownership(OwnershipFailure.ComparisonLoanConflict), DiagnosticCode.ComparisonLoanConflict_Kd, "A5"),
        (DiagnosticRequirement.Ownership(OwnershipFailure.ComparisonLoanConflict), DiagnosticCode.CallReservationConflict_Kd, "A5"),
        (DiagnosticRequirement.Ownership(OwnershipFailure.ComparisonLoanConflict), DiagnosticCode.CallActivationConflict_Kd, "A5"),
        (DiagnosticRequirement.Ownership(OwnershipFailure.ComparisonLoanConflict), DiagnosticCode.PlacementActivationConflict_Kd, "A5"),
        (DiagnosticRequirement.Ownership(OwnershipFailure.CallEffectConflict), DiagnosticCode.CallEffectConflict_Kd, "A5"),
        (DiagnosticRequirement.Ownership(OwnershipFailure.DefaultArgumentAccess), DiagnosticCode.DefaultArgumentAccess_Kd, "A5"),
        (DiagnosticRequirement.Ownership(OwnershipFailure.DefaultArgumentBorrow), DiagnosticCode.DefaultArgumentBorrow_Kd, "A5"),
        (DiagnosticRequirement.Ownership(OwnershipFailure.UnprovenOrigin), DiagnosticCode.UnsatisfiedOriginRelation_Kd, "A5"),
        (DiagnosticRequirement.Ownership(OwnershipFailure.UnprovenOrigin), DiagnosticCode.UnprovenOriginRelation_Kd, "A5"),
        (DiagnosticRequirement.Ownership(OwnershipFailure.UnprovenOrigin), DiagnosticCode.UnprovenConstraint_Kd, "A5"),
        (DiagnosticRequirement.Binding(BindingFailure.OriginRelation), DiagnosticCode.UnsatisfiedOriginRelation_Kd, "A5"),
        (DiagnosticRequirement.Binding(BindingFailure.OriginRelation), DiagnosticCode.UnprovenOriginRelation_Kd, "A5"),
        (DiagnosticRequirement.Ownership(OwnershipFailure.StorageLimit), DiagnosticCode.OwnershipStorageLimit_Kd, "A5"),
        (DiagnosticRequirement.Ownership(OwnershipFailure.Unsupported), DiagnosticCode.Unsupported_Kd, "U"),
        (DiagnosticRequirement.Ownership(OwnershipFailure.ExpansionLimit), DiagnosticCode.DeferredExpansionLimit_Kd, "-"),
    ];

    private readonly string? directory = Environment.GetEnvironmentVariable("KIMI_MIR_DIFF") is { Length: > 0 } value ? value : null;
    private readonly ConditionalWeakTable<Compilation, StrongBox<ulong>> versions = new(); // The Binding pass each compilation was compared at.
    private readonly HashSet<string> sources = new(StringComparer.Ordinal); // The sources this process saved; also the lock of the files.
    private readonly StreamWriter? raw;
    private int events;

    public MirDifferenceLedger()
    {
        if (this.directory is null)
        {
            return;
        }

        Directory.CreateDirectory(Path.Combine(this.directory, "sources"));
        this.raw = new(Path.Combine(this.directory, $"raw-{Environment.ProcessId}.tsv"), append: true, new UTF8Encoding(false)) { AutoFlush = true };
        Compilation.Analyzed += this.Record;
    }

    /// <summary>Gets the tracked classification table (R4-M16): <c>id section kind analysis code requirement form category reference
    /// note</c> per row, where <c>*</c> matches anything and the form is a regular expression.</summary>
    internal static string[][] Table
        => [.. File.ReadLines(DiagnosticCorpus.RepositoryPath("tests", "xUnitTest", "TestData", "mir-diff", "classification.tsv")).Skip(1)
            .Where(static x => x.Length != 0).Select(static x => x.Split('\t'))];

    public void Dispose()
    {
        if (this.raw is null)
        {
            return;
        }

        Compilation.Analyzed -= this.Record;
        lock (this.sources)
        {
            this.raw.Dispose();
        }

        Aggregate(this.directory!);
    }

    /// <summary>Gets the analysis that owns an old record of the compared set (R4-M4), or null for a record that passes through.</summary>
    /// <param name="requirement">The record's requirement.</param>
    /// <param name="code">The record's code.</param>
    /// <returns>The analysis.</returns>
    internal static string? AnalysisOf(DiagnosticRequirement requirement, DiagnosticCode code)
        => Array.Find(Compared, x => x.Requirement == requirement && x.Code == code).Analysis;

    /// <summary>Gets the analysis that owns a MIR problem: its code's in the compared set (R4-M4), where each code names one analysis.</summary>
    /// <param name="code">The problem's code.</param>
    /// <returns>The analysis.</returns>
    /// <exception cref="InvalidOperationException">The code is outside the compared set, which no analysis reports.</exception>
    internal static string AnalysisOf(DiagnosticCode code)
        => Array.Find(Compared, x => x.Code == code).Analysis ?? throw new InvalidOperationException($"{code} is outside the compared set (R4-M4).");

    /// <summary>Matches the records of both sides as multisets on code and subject node, by reference: each MIR entry takes the first
    /// unmatched old entry of its code and node. Old entries come first in their order, on side both (with the old entry's facts) or
    /// old, then the unmatched MIR entries on side new.</summary>
    /// <param name="old">The old path's A-owned records.</param>
    /// <param name="mir">The MIR's records and problems.</param>
    /// <returns>The sides and entries.</returns>
    internal static List<(string Side, Entry Entry)> Match(List<Entry> old, List<Entry> mir)
    {
        var matched = new bool[old.Count];
        var unmatched = new List<Entry>();
        foreach (var entry in mir)
        {
            var index = 0;
            while (index < old.Count && (matched[index] || old[index].Code != entry.Code || !ReferenceEquals(old[index].Node, entry.Node)))
            {
                index++;
            }

            if (index < old.Count)
            {
                matched[index] = true;
            }
            else
            {
                unmatched.Add(entry);
            }
        }

        return [.. old.Select((x, i) => (matched[i] ? "both" : "old", x)), .. unmatched.Select(static x => ("new", x))];
    }

    /// <summary>Lists the differences of raw rows by signature (section, kind, analysis, code, requirement, form): per event and body, an
    /// unmatched old and new row of one code pair into one <c>location</c> difference with the old row's facts, and the other unmatched
    /// rows are <c>missing</c> (old) or <c>new</c>; an event whose A-owned records are on one side only adds an <c>acceptance</c>
    /// difference with that side's first row. Matched rows are no difference.</summary>
    /// <param name="rows">The raw rows.</param>
    /// <returns>Each difference's signature and its source's hash.</returns>
    internal static List<(string Signature, string Sha)> Differences(IEnumerable<string[]> rows)
    {
        var differences = new List<(string Signature, string Sha)>();
        foreach (var group in rows.GroupBy(static x => x[Event]))
        {
            var news = group.Where(static x => x[Side] == "new").ToList();
            var (old, mir) = (group.FirstOrDefault(static x => x[Side] != "new"), group.FirstOrDefault(static x => x[Side] != "old"));
            if ((old is null) != (mir is null))
            {
                differences.Add(Difference(old ?? mir!, "acceptance"));
            }

            foreach (var row in group.Where(static x => x[Side] == "old"))
            {
                var pair = news.FindIndex(x => x[Body] == row[Body] && x[Code] == row[Code]);
                differences.Add(Difference(row, pair < 0 ? "missing" : "location"));
                if (pair >= 0)
                {
                    news.RemoveAt(pair);
                }
            }

            differences.AddRange(news.Select(static x => Difference(x, "new")));
        }

        return differences;
    }

    /// <summary>Writes the ledger of a directory's raw rows: <c>ledger.tsv</c>, one row per signature with its count, the category and
    /// reference of the first classification row that matches it and an example source, and <c>summary.md</c> with the P9 number (the
    /// unclassified signatures), the open (b) signatures, the unclassified split by analysis and section, and the table rows nothing
    /// matched.</summary>
    /// <param name="directory">The directory of the raw rows.</param>
    internal static void Aggregate(string directory)
    {
        var rows = Directory.GetFiles(directory, "raw-*.tsv").Order(StringComparer.Ordinal).SelectMany(File.ReadLines).Select(static x => x.Split('\t')).ToList();
        var table = Table;
        var used = new HashSet<string>(StringComparer.Ordinal);
        var ledger = Differences(rows).GroupBy(static x => x.Signature, StringComparer.Ordinal).Select(x =>
        {
            var row = Array.Find(table, y => Matches(y, x.Key));
            used.Add(row?[0] ?? string.Empty);
            return (Signature: x.Key, Count: x.Count(), Category: row?[7] ?? "?", Reference: row?[8] ?? "-", Example: x.First().Sha);
        }).OrderByDescending(static x => x.Count).ThenBy(static x => x.Signature, StringComparer.Ordinal).ToList();
        File.WriteAllLines(
            Path.Combine(directory, "ledger.tsv"),
            ["section\tkind\tanalysis\tcode\trequirement\tform\tcount\tcategory\treference\texample", .. ledger.Select(static x => $"{x.Signature}\t{x.Count}\t{x.Category}\t{x.Reference}\t{x.Example}")],
            new UTF8Encoding(false));
        var open = ledger.Where(static x => x.Category is "?" or "b").ToList();
        var summary = new StringBuilder("# MIR difference ledger\n\n")
            .Append($"- Comparisons: {rows.Select(static x => x[Event]).Distinct().Count()}; rows: {rows.Count} (both {rows.Count(static x => x[Side] == "both")}, ")
            .Append($"old {rows.Count(static x => x[Side] == "old")}, new {rows.Count(static x => x[Side] == "new")}); signatures: {ledger.Count}\n")
            .Append($"- P9 (unclassified signatures): {open.Count(static x => x.Category == "?")} ({open.Where(static x => x.Category == "?").Sum(static x => x.Count)} rows)\n")
            .Append($"- Open (b): {open.Count(static x => x.Category == "b")} ({open.Where(static x => x.Category == "b").Sum(static x => x.Count)} rows)\n")
            .Append($"- Table rows nothing matched: {string.Join(", ", table.Select(static x => x[0]).Where(x => !used.Contains(x)).DefaultIfEmpty("none"))}\n\n")
            .Append("| analysis | section | unclassified signatures | rows |\n| --- | --- | ---: | ---: |\n");
        foreach (var split in open.Where(static x => x.Category == "?").GroupBy(static x => (Analysis: x.Signature.Split('\t')[2], Section: x.Signature.Split('\t')[0])).OrderBy(static x => x.Key))
        {
            summary.Append($"| {split.Key.Analysis} | {split.Key.Section} | {split.Count()} | {split.Sum(static x => x.Count)} |\n");
        }

        File.WriteAllText(Path.Combine(directory, "summary.md"), summary.ToString(), new UTF8Encoding(false));
    }

    private static (string Signature, string Sha) Difference(string[] row, string kind)
        => (string.Join('\t', row[Section], kind, row[Analysis], row[Code], row[Requirement], row[Form]), row[Sha]);

    // A classification row matches a signature when each field is * or equal and the form, a regular expression, matches the whole form.
    private static bool Matches(string[] row, string signature)
    {
        var fields = signature.Split('\t');
        return Enumerable.Range(0, 5).All(i => row[i + 1] is "*" || row[i + 1] == fields[i]) &&
            (row[6] is "*" || Regex.IsMatch(fields[5], $"^(?:{row[6]})$", RegexOptions.CultureInvariant));
    }

    // The definition whose body holds a node: the node itself or its nearest function, Property or accessor, else its tree's root.
    private static Koto BodyOf(Koto node)
    {
        while (node is not (FunctionKoto or PropertyKoto or PropertyAccessorKoto) && node.Parent is { } parent)
        {
            node = parent;
        }

        return node;
    }

    private static string Source(Compilation compilation)
    {
        var text = new StringBuilder();
        foreach (var module in compilation.SourceModules)
        {
            foreach (var document in module.SourceDocuments)
            {
                text.Append("// ").Append(Path.GetFileName(document.Path)).Append('\n').Append(document.SourceText).Append('\n');
            }
        }

        return text.ToString();
    }

    // Compares a mir-pipeline compilation once per Binding pass; the observer reads only the compilation's final records.
    private void Record(Compilation compilation)
    {
        if (compilation.Pipeline != CompilerPipeline.Mir)
        {
            return;
        }

        var program = compilation.BuildMir();
        if (Interlocked.Exchange(ref this.versions.GetOrCreateValue(compilation).Value, program.Version) == program.Version)
        {
            return;
        }

        var old = new List<Entry>();
        foreach (var (requirement, code, node) in LegacyRecords.Of(compilation))
        {
            var effectOnly = requirement == DiagnosticRequirement.Binding(BindingFailure.IncompatibleImplementation) ||
                requirement == DiagnosticRequirement.Binding(BindingFailure.Unsupported);
            if (AnalysisOf(requirement, code) is { } analysis && (!effectOnly || compilation.Binding.IsEffectRecord(node)))
            {
                old.Add(new(code, requirement.Name, analysis, node));
            }
        }

        var mir = program.Records.Select(x => new Entry(x.Code, "-", "B", compilation.SyntaxOf(x.Subject))).ToList();
        foreach (var problem in program.Problems.All)
        {
            mir.Add(new(problem.Code, "-", AnalysisOf(problem.Code), compilation.SyntaxOf(problem.Subject)));
        }

        if (old.Count + mir.Count != 0)
        {
            this.Write(compilation, Match(old, mir));
        }
    }

    private void Write(Compilation compilation, List<(string Side, Entry Entry)> matched)
    {
        var source = Source(compilation);
        var sha = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..12];
        var section = compilation.Binding.Result.IsComplete ? "complete" : "incomplete";
        var comparison = $"{Environment.ProcessId}.{Interlocked.Increment(ref this.events)}";
        var rows = new StringBuilder();
        foreach (var (side, (code, requirement, analysis, node)) in matched)
        {
            rows.Append($"{comparison}\t{section}\t{side}\t{analysis}\t{code}\t{requirement}\t{LegacyRecords.Location(node)}+{node.Span.Length}\t")
                .Append($"{node.GetType().Name}\t{LegacyRecords.Location(BodyOf(node))}\t{sha}\n");
        }

        lock (this.sources)
        {
            this.raw!.Write(rows);
            if (this.sources.Add(sha))
            {
                File.WriteAllText(Path.Combine(this.directory!, "sources", sha + ".kimi"), source, new UTF8Encoding(false));
            }
        }
    }

    /// <summary>A record of either side: its code, its requirement's name (- on the MIR side), the analysis owning it and its subject.</summary>
    /// <param name="Code">The code.</param>
    /// <param name="Requirement">The requirement's name.</param>
    /// <param name="Analysis">The analysis.</param>
    /// <param name="Node">The subject.</param>
    internal readonly record struct Entry(DiagnosticCode Code, string Requirement, string Analysis, Koto Node);
}
