// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

[assembly: AssemblyFixture(typeof(XunitTest.MirDifferenceLedger))]

namespace XunitTest;

/// <summary>The MIR difference ledger (docs/dev/DECISIONS.md R4-M17; R4–R6). With <c>KIMI_MIR_DIFF</c> naming a directory, the test
/// assembly observes every analysis (<see cref="Compilation.Analyzed"/>) and, once per mir-pipeline compilation and Binding pass, matches
/// the old path's A-owned records (<see cref="LegacyRecords"/>, the compared set of R4-M4) with the MIR's construction records, validator
/// findings and problems on code and subject node (<see cref="Sides"/>), appending one row per record to <c>raw-&lt;pid&gt;.tsv</c> there and saving the source once as
/// <c>sources/&lt;sha&gt;.kimi</c>. Disposal aggregates the rows (<see cref="Aggregate"/>). Without the variable nothing is observed.
/// </summary>
/// <remarks>A raw row is <c>event section side analysis code requirement location form body sha</c>: the comparison, complete (Binding
/// accepted the program: a complete result without a lint Error) or incomplete Binding (R4-M5), both, old or new, the owning analysis (A1,
/// A3, A4, A5, U for an old Unsupported gate, - for the expansion limit, B for a MIR construction record or validator finding; a MIR
/// problem takes its code's), the code, the old requirement (- on the MIR side, R4-M3a), the subject's document, offset and length, its
/// syntax form, the location of its enclosing definition and the source's hash.</remarks>
public sealed class MirDifferenceLedger : IDisposable
{
    internal const int Event = 0;
    internal const int Section = 1;
    internal const int Side = 2;
    internal const int Analysis = 3;
    internal const int Code = 4;
    internal const int Requirement = 5;
    internal const int Location = 6;
    internal const int Form = 7;
    internal const int Body = 8;
    internal const int Sha = 9;

    // R4-M4: the compared old records, by requirement and each code it reports (OwnershipIssue.Code, Binding's code of the failure), with
    // the analysis that owns them; a MIR problem takes its code's analysis. Every other record passes through: TransferRequired and
    // Internal, control flow's other codes and Binding's other failures, CaseLimit included. Binding's IncompatibleImplementation and
    // Unsupported records are compared only where an effect check made them (Binding.IsEffectRecord, R4-M23).
    internal static readonly (DiagnosticRequirement Requirement, DiagnosticCode Code, string Analysis)[] Compared =
    [
        (DiagnosticRequirement.Ownership(OwnershipRequirement.UninitializedUse), DiagnosticCode.UninitializedPlace_Kd, "A1"),
        (DiagnosticRequirement.Ownership(OwnershipRequirement.PossiblyMovedUse), DiagnosticCode.MovedPlace_Kd, "A1"),
        (DiagnosticRequirement.Ownership(OwnershipRequirement.ReassignedLet), DiagnosticCode.ReassignedLet_Kd, "A1"),
        (DiagnosticRequirement.Ownership(OwnershipRequirement.StaticMovePathRequired), DiagnosticCode.StaticMovePathRequired_Kd, "A1"),
        (DiagnosticRequirement.Ownership(OwnershipRequirement.DefaultArgumentMove), DiagnosticCode.DefaultArgumentMove_Kd, "A1"),
        (DiagnosticRequirement.ControlFlow, DiagnosticCode.RequireFallthrough_Kd, "A3"),
        (DiagnosticRequirement.Ownership(OwnershipRequirement.EffectBound), DiagnosticCode.IncompatibleContractImplementation_Kd, "A4"),
        (DiagnosticRequirement.Ownership(OwnershipRequirement.CallableEffectBound), DiagnosticCode.UnsatisfiedEffectBound_Kd, "A4"),
        (DiagnosticRequirement.Ownership(OwnershipRequirement.VirtualEffectBound), DiagnosticCode.UnsatisfiedEffectBound_Kd, "A4"),
        (DiagnosticRequirement.Binding(BindingFailure.VirtualEffectBound), DiagnosticCode.UnsatisfiedEffectBound_Kd, "A4"),
        (DiagnosticRequirement.Binding(BindingFailure.IncompatibleImplementation), DiagnosticCode.IncompatibleContractImplementation_Kd, "A4"),
        (DiagnosticRequirement.Binding(BindingFailure.Unsupported), DiagnosticCode.Unsupported_Kd, "U"),
        (DiagnosticRequirement.Ownership(OwnershipRequirement.ComparisonLoanConflict), DiagnosticCode.ComparisonLoanConflict_Kd, "A5"),
        (DiagnosticRequirement.Ownership(OwnershipRequirement.ComparisonLoanConflict), DiagnosticCode.CallReservationConflict_Kd, "A5"),
        (DiagnosticRequirement.Ownership(OwnershipRequirement.ComparisonLoanConflict), DiagnosticCode.CallActivationConflict_Kd, "A5"),
        (DiagnosticRequirement.Ownership(OwnershipRequirement.ComparisonLoanConflict), DiagnosticCode.PlacementActivationConflict_Kd, "A5"),
        (DiagnosticRequirement.Ownership(OwnershipRequirement.CallEffectConflict), DiagnosticCode.CallEffectConflict_Kd, "A5"),
        (DiagnosticRequirement.Ownership(OwnershipRequirement.DefaultArgumentAccess), DiagnosticCode.DefaultArgumentAccess_Kd, "A5"),
        (DiagnosticRequirement.Ownership(OwnershipRequirement.DefaultArgumentBorrow), DiagnosticCode.DefaultArgumentBorrow_Kd, "A5"),
        (DiagnosticRequirement.Ownership(OwnershipRequirement.UnprovenOrigin), DiagnosticCode.UnsatisfiedOriginRelation_Kd, "A5"),
        (DiagnosticRequirement.Ownership(OwnershipRequirement.UnprovenOrigin), DiagnosticCode.UnprovenOriginRelation_Kd, "A5"),
        (DiagnosticRequirement.Ownership(OwnershipRequirement.UnprovenOrigin), DiagnosticCode.UnprovenConstraint_Kd, "A5"),
        (DiagnosticRequirement.Binding(BindingFailure.OriginRelation), DiagnosticCode.UnsatisfiedOriginRelation_Kd, "A5"),
        (DiagnosticRequirement.Binding(BindingFailure.OriginRelation), DiagnosticCode.UnprovenOriginRelation_Kd, "A5"),
        (DiagnosticRequirement.Ownership(OwnershipRequirement.StorageLimit), DiagnosticCode.OwnershipStorageLimit_Kd, "A5"),
        (DiagnosticRequirement.Ownership(OwnershipRequirement.Unsupported), DiagnosticCode.Unsupported_Kd, "U"),
        (DiagnosticRequirement.Ownership(OwnershipRequirement.ExpansionLimit), DiagnosticCode.DeferredExpansionLimit_Kd, "-"),
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

    /// <summary>Gets the tracked classification table (R4-M16, R4-M46): per row <c>id section kind analysis code requirement form category
    /// clause reference note</c>, where <c>*</c> matches anything, the form is a regular expression and the clause names the SPEC sections
    /// deciding the row (- for none).</summary>
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
    /// difference with that side's first row. Matched rows are no difference. A missing row is unsound, the old path rejecting what the
    /// MIR accepts (plan §8 of the Complete Polonius A5 Plan, R4-M47), when its compilation's Binding is complete and its body keeps no
    /// unpaired MIR record, which would make it a code choice.</summary>
    /// <param name="rows">The raw rows.</param>
    /// <returns>Each difference's signature, its source's hash, its subject's location and whether it is unsound.</returns>
    internal static List<(string Signature, string Sha, string Location, bool Unsound)> Differences(IEnumerable<string[]> rows)
    {
        var differences = new List<(string Signature, string Sha, string Location, bool Unsound)>();
        foreach (var group in rows.GroupBy(static x => x[Event]))
        {
            var news = group.Where(static x => x[Side] == "new").ToList();
            var (old, mir) = (group.FirstOrDefault(static x => x[Side] != "new"), group.FirstOrDefault(static x => x[Side] != "old"));
            if ((old is null) != (mir is null))
            {
                differences.Add(Difference(old ?? mir!, "acceptance", false));
            }

            var olds = new List<(string[] Row, bool Paired)>();
            foreach (var row in group.Where(static x => x[Side] == "old"))
            {
                var pair = news.FindIndex(x => x[Body] == row[Body] && x[Code] == row[Code]);
                olds.Add((row, pair >= 0));
                if (pair >= 0)
                {
                    news.RemoveAt(pair);
                }
            }

            var complete = group.First()[Section] == "complete";
            differences.AddRange(olds.Select(x => x.Paired ? Difference(x.Row, "location", false)
                : Difference(x.Row, "missing", complete && !news.Exists(y => y[Body] == x.Row[Body]))));
            differences.AddRange(news.Select(static x => Difference(x, "new", false)));
        }

        return differences;
    }

    /// <summary>Writes the ledger of a directory's raw rows: <c>ledger.tsv</c>, one row per signature with its count, its blocking rows, the
    /// category, clause and reference of the first classification row that matches it and an example source, and <c>summary.md</c> with
    /// the unclassified and open (b) signatures, plan §8's metrics (R4-M47), the open rows split by analysis and section, and the table
    /// rows nothing matched. A difference blocks the R4 exit when it is unsound (<see cref="Differences"/>), lies in a library body, or
    /// lies in a milestone program (a source that holds a <c>tests/milestones</c> program's whole text) whose Binding is complete; it
    /// stays open unless its signature is classified (a), (c) or (d), each with a clause (R4-M46).</summary>
    /// <param name="directory">The directory of the raw rows.</param>
    internal static void Aggregate(string directory)
    {
        var rows = Directory.GetFiles(directory, "raw-*.tsv").Order(StringComparer.Ordinal).SelectMany(File.ReadLines).Select(static x => x.Split('\t')).ToList();
        var milestones = Directory.GetFiles(DiagnosticCorpus.RepositoryPath("tests", "milestones"), "Milestone*.kimi").Select(static x => File.ReadAllText(x).ReplaceLineEndings("\n")).ToArray();
        var programs = new Dictionary<string, bool>(StringComparer.Ordinal);
        var table = Table;
        var used = new HashSet<string>(StringComparer.Ordinal);
        var ledger = Differences(rows).Select(x => (x.Signature, x.Sha, x.Unsound, Library: x.Location.StartsWith(LegacyRecords.Library, StringComparison.Ordinal),
            Milestone: x.Signature.StartsWith("complete", StringComparison.Ordinal) && Milestone(x.Sha)))
            .GroupBy(static x => x.Signature, StringComparer.Ordinal).Select(x =>
            {
                var row = Array.Find(table, y => Matches(y, x.Key));
                used.Add(row?[0] ?? string.Empty);
                var blocking = x.Where(static y => y.Unsound || y.Library || y.Milestone).ToList();
                return (Signature: x.Key, Count: x.Count(), Blocking: blocking, Category: row?[7] ?? "?", Clause: row?[8] ?? "-", Reference: row?[9] ?? "-", Example: x.First().Sha);
            }).OrderByDescending(static x => x.Count).ThenBy(static x => x.Signature, StringComparer.Ordinal).ToList();
        File.WriteAllLines(
            Path.Combine(directory, "ledger.tsv"),
            ["section\tkind\tanalysis\tcode\trequirement\tform\tcount\tblocking\tcategory\tclause\treference\texample", .. ledger.Select(static x => $"{x.Signature}\t{x.Count}\t{x.Blocking.Count}\t{x.Category}\t{x.Clause}\t{x.Reference}\t{x.Example}")],
            new UTF8Encoding(false));
        var open = ledger.Where(static x => x.Category is not ("a" or "c" or "d")).ToList();
        var blocked = open.SelectMany(static x => x.Blocking).ToList();
        var variants = DiagnosticCorpus.SpecCases.SelectMany(static x => x.Variants.Select(y => y.Family ?? x.Family)).ToList();
        var summary = new StringBuilder("# MIR difference ledger\n\n")
            .Append($"- Comparisons: {rows.Select(static x => x[Event]).Distinct().Count()}; rows: {rows.Count} (both {rows.Count(static x => x[Side] == "both")}, ")
            .Append($"old {rows.Count(static x => x[Side] == "old")}, new {rows.Count(static x => x[Side] == "new")}); signatures: {ledger.Count}\n")
            .Append($"- Unclassified signatures: {open.Count(static x => x.Category == "?")} ({open.Where(static x => x.Category == "?").Sum(static x => x.Count)} rows)\n")
            .Append($"- Open (b): {open.Count(static x => x.Category == "b")} ({open.Where(static x => x.Category == "b").Sum(static x => x.Count)} rows)\n")
            .Append($"- Table rows nothing matched: {string.Join(", ", table.Select(static x => x[0]).Where(x => !used.Contains(x)).DefaultIfEmpty("none"))}\n\n")
            .Append("## Metrics (plan §8)\n\n")
            .Append($"- Families landed on the MIR (plan §6.1): {string.Join(", ", VerifiedSpecExampleTest.Landed.Where(static x => x.StartsWith('F') || x == "A6").DefaultIfEmpty("none"))} of F1–F10 and A6\n")
            .Append($"- SPEC examples: {DiagnosticCorpus.SpecCases.Count} blocks, {variants.Count} variants, {variants.Count(VerifiedSpecExampleTest.Landed.Contains)} checked on the MIR\n")
            .Append($"- Open blocking rows: {blocked.Count} of {ledger.Sum(static x => x.Blocking.Count)} (an old rejection the MIR lacks {blocked.Count(static x => x.Unsound)}, ")
            .Append($"else a library body {blocked.Count(static x => !x.Unsound && x.Library)}, else a milestone {blocked.Count(static x => !x.Unsound && !x.Library)})\n\n")
            .Append("| analysis | section | unclassified signatures | rows | open blocking rows |\n| --- | --- | ---: | ---: | ---: |\n");
        foreach (var split in open.GroupBy(static x => (Analysis: x.Signature.Split('\t')[2], Section: x.Signature.Split('\t')[0])).OrderBy(static x => x.Key))
        {
            var unclassified = split.Where(static x => x.Category == "?").ToList();
            summary.Append($"| {split.Key.Analysis} | {split.Key.Section} | {unclassified.Count} | {unclassified.Sum(static x => x.Count)} | {split.Sum(static x => x.Blocking.Count)} |\n");
        }

        File.WriteAllText(Path.Combine(directory, "summary.md"), summary.ToString(), new UTF8Encoding(false));

        // A milestone program: its saved source holds a milestone's whole text.
        bool Milestone(string sha)
        {
            if (!programs.TryGetValue(sha, out var found))
            {
                var path = Path.Combine(directory, "sources", sha + ".kimi");
                var source = File.Exists(path) ? File.ReadAllText(path).ReplaceLineEndings("\n") : string.Empty;
                programs[sha] = found = Array.Exists(milestones, x => source.Contains(x, StringComparison.Ordinal));
            }

            return found;
        }
    }

    /// <summary>Lists an analyzed compilation's records by side: Binding's records outside the compared set (R4-M4), which both paths
    /// publish; the old path's A-owned records, which the ledger compares; the old analyses' other records, which pass through; and the
    /// MIR's construction records, validator findings (as <c>InternalInvariant_Kd</c>) and problems.</summary>
    /// <param name="compilation">The compilation after <see cref="Compilation.Analyze"/>.</param>
    /// <param name="program">Its MIR (<see cref="Compilation.BuildMir"/>).</param>
    /// <returns>The common, old, passed and MIR records.</returns>
    internal static (List<Entry> Common, List<Entry> Old, List<Entry> Passed, List<Entry> Mir) Sides(Compilation compilation, MirProgram program)
    {
        var (common, old, passed) = (new List<Entry>(), new List<Entry>(), new List<Entry>());
        foreach (var (requirement, code, node) in LegacyRecords.Of(compilation))
        {
            var effectOnly = requirement == DiagnosticRequirement.Binding(BindingFailure.IncompatibleImplementation) ||
                requirement == DiagnosticRequirement.Binding(BindingFailure.Unsupported);
            var analysis = AnalysisOf(requirement, code) is { } owner && (!effectOnly || compilation.Binding.IsEffectRecord(node)) ? owner : null;
            (analysis is not null ? old : requirement.Partition == DiagnosticPartition.Binding ? common : passed).Add(new(code, requirement.Name, analysis ?? "-", node));
        }

        var mir = program.Records.Select(x => new Entry(x.Code, "-", "B", compilation.SyntaxOf(x.Subject)))
            .Concat(program.Findings.Select(x => new Entry(DiagnosticCode.InternalInvariant_Kd, "-", "B", compilation.SyntaxOf(x.Subject)))).ToList();
        foreach (var problem in program.Problems.All)
        {
            mir.Add(new(problem.Code, "-", AnalysisOf(problem.Code), compilation.SyntaxOf(problem.Subject)));
        }

        return (common, old, passed, mir);
    }

    private static (string Signature, string Sha, string Location, bool Unsound) Difference(string[] row, string kind, bool unsound)
        => (string.Join('\t', row[Section], kind, row[Analysis], row[Code], row[Requirement], row[Form]), row[Sha], row[Location], unsound);

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

        var (_, old, _, mir) = Sides(compilation, program);
        if (old.Count + mir.Count != 0)
        {
            this.Write(compilation, Match(old, mir));
        }
    }

    private void Write(Compilation compilation, List<(string Side, Entry Entry)> matched)
    {
        var source = Source(compilation);
        var sha = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..12];
        // Binding accepted the program (R4-M47): a complete result and no lint Error, which leaves it complete (R5-M31).
        var section = compilation.Binding.Result.IsComplete && compilation.Binding.LintErrors == 0 ? "complete" : "incomplete";
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
