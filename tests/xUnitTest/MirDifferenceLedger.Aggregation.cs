// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using System.Text.RegularExpressions;

namespace XunitTest;

// The ledger's aggregation (DECISIONS R4-M17, R4-M46, R4-M47, R5-M56): the differences of the raw rows by signature, their
// classification by the tracked table, and the ledger.tsv and summary.md they make.
public sealed partial class MirDifferenceLedger
{
    /// <summary>Gets the tracked classification table (R4-M16, R4-M46): per row <c>id section kind side analysis code requirement form
    /// category clause reference note</c>, where each field matches an equal one or is <c>*</c> (a side matches only itself: mir never
    /// matches both), the form is a regular expression and the clause names the SPEC sections deciding the row (- for none).</summary>
    internal static string[][] Table
        => [.. File.ReadLines(DiagnosticCorpus.RepositoryPath("tests", "xUnitTest", "TestData", "mir-diff", "classification.tsv")).Skip(1)
            .Where(static x => x.Length != 0).Select(static x => x.Split('\t'))];

    /// <summary>Lists the differences of raw rows by signature (section, kind, side, analysis, code, requirement, form): per event and
    /// body, an unmatched old and new row of one code pair into one <c>location</c> difference with the old row's facts, and the other
    /// unmatched rows are <c>missing</c> (old) or <c>new</c>; an event whose A-owned records are on one side only adds an
    /// <c>acceptance</c> difference with that side's first row. A matched row whose requirements differ (<c>old&gt;mir</c>) is a
    /// <c>requirement</c> difference; other matched rows are no difference. The side is the path whose records a difference reports:
    /// old for missing, mir for new, both for location and requirement, and for acceptance the side that holds the event's records.
    /// A missing row is unsound, the old path rejecting what the MIR accepts (plan §8 of the Complete Polonius A5 Plan, R4-M47), when its
    /// compilation's Binding is complete and its body keeps no unpaired MIR record, which would make it a code choice.</summary>
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
                differences.Add(Difference(old ?? mir!, "acceptance", old is null ? "mir" : "old", unsound: false));
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

            differences.AddRange(group.Where(static x => x[Side] == "both" && x[Requirement].Contains('>'))
                .Select(static x => Difference(x, "requirement", "both", unsound: false)));
            var complete = group.First()[Section] == "complete";
            differences.AddRange(olds.Select(x => x.Paired ? Difference(x.Row, "location", "both", unsound: false)
                : Difference(x.Row, "missing", "old", complete && !news.Exists(y => y[Body] == x.Row[Body]))));
            differences.AddRange(news.Select(static x => Difference(x, "new", "mir", unsound: false)));
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
                return (Signature: x.Key, Count: x.Count(), Blocking: blocking, Category: row?[8] ?? "?", Clause: row?[9] ?? "-",
                    Reference: row?[10] ?? "-", Example: x.First().Sha);
            }).OrderByDescending(static x => x.Count).ThenBy(static x => x.Signature, StringComparer.Ordinal).ToList();
        File.WriteAllLines(
            Path.Combine(directory, "ledger.tsv"),
            [
                "section\tkind\tside\tanalysis\tcode\trequirement\tform\tcount\tblocking\tcategory\tclause\treference\texample",
                .. ledger.Select(static x => $"{x.Signature}\t{x.Count}\t{x.Blocking.Count}\t{x.Category}\t{x.Clause}\t{x.Reference}\t{x.Example}"),
            ],
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
        foreach (var split in open.GroupBy(static x => (Analysis: x.Signature.Split('\t')[3], Section: x.Signature.Split('\t')[0])).OrderBy(static x => x.Key))
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

    /// <summary>Gets whether a classification row matches a signature: each field is * or equal, a side included (a row of side mir
    /// never matches a both signature), and the form, a regular expression, matches the whole form.</summary>
    /// <param name="row">The classification row.</param>
    /// <param name="signature">The signature (<see cref="Differences"/>).</param>
    /// <returns>Whether it matches.</returns>
    internal static bool Matches(string[] row, string signature)
    {
        var fields = signature.Split('\t');
        return Enumerable.Range(0, 6).All(i => row[i + 1] is "*" || row[i + 1] == fields[i]) &&
            (row[7] is "*" || Regex.IsMatch(fields[6], $"^(?:{row[7]})$", RegexOptions.CultureInvariant));
    }

    private static (string Signature, string Sha, string Location, bool Unsound) Difference(string[] row, string kind, string side, bool unsound)
        => (string.Join('\t', row[Section], kind, side, row[Analysis], row[Code], row[Requirement], row[Form]), row[Sha], row[Location], unsound);
}
