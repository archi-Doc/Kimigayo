// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using System.Text.RegularExpressions;
using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>A location in a syntax case's source: a unique substring, or the insertion point after one.</summary>
/// <param name="At">The substring the span starts at; it occurs exactly once in the source.</param>
/// <param name="After">The substring the insertion point follows; it occurs exactly once in the source.</param>
/// <param name="Skip">Characters skipped from the start of <paramref name="At"/>.</param>
/// <param name="Length">The span length; the rest of <paramref name="At"/> by default, zero after <paramref name="After"/>.</param>
internal sealed record SyntaxLocation(string? At = null, string? After = null, int Skip = 0, int? Length = null)
{
    /// <summary>Resolves the location in a source.</summary>
    /// <param name="source">The source text.</param>
    /// <returns>The span.</returns>
    public SourceSpan Resolve(string source)
    {
        var anchor = this.At ?? this.After ?? throw new InvalidOperationException("A location names At or After.");
        var index = source.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(index >= 0 && index == source.LastIndexOf(anchor, StringComparison.Ordinal), $"'{anchor}' must occur exactly once.");
        return this.At is null ? new(index + anchor.Length, 0) : new(index + this.Skip, this.Length ?? anchor.Length - this.Skip);
    }
}

/// <summary>A related location a syntax record must carry.</summary>
/// <param name="Role">The role, such as <c>opening delimiter</c>.</param>
/// <param name="At">The substring the span starts at.</param>
/// <param name="Skip">Characters skipped from the start of <paramref name="At"/>.</param>
/// <param name="Length">The span length; the rest of the substring by default.</param>
internal sealed record SyntaxRelated(string Role, string At, int Skip = 0, int? Length = null)
{
    /// <summary>Gets the location.</summary>
    public SyntaxLocation Location => new(this.At, null, this.Skip, this.Length);
}

/// <summary>One edit of an expected repair candidate: its replacement text at a location of the case's source.</summary>
/// <param name="Text">The replacement text.</param>
/// <param name="At">The substring the replaced span starts at.</param>
/// <param name="After">The substring the insertion point follows.</param>
/// <param name="Skip">Characters skipped from the start of <paramref name="At"/>.</param>
/// <param name="Length">The replaced length; the rest of <paramref name="At"/> by default.</param>
internal sealed record SyntaxEdit(string Text, string? At = null, string? After = null, int Skip = 0, int? Length = null)
{
    /// <summary>Gets the location.</summary>
    public SyntaxLocation Location => new(this.At, this.After, this.Skip, this.Length);
}

/// <summary>One repair candidate a syntax record must offer (SPEC 23.1.3), as its kind and located edits.</summary>
/// <param name="Kind">The stable kind name, such as <c>Repair.ReplaceToken</c>.</param>
/// <param name="Edits">The edits in position order.</param>
internal sealed record SyntaxRepair(string Kind, SyntaxEdit[] Edits);

/// <summary>One record a syntax case publishes, written independently of the parser (docs/dev/DIAGNOSTICS.md §9.2).</summary>
/// <param name="Code">The code name.</param>
/// <param name="Form">The syntax form, as named in <c>SyntaxForm</c>; <see langword="null"/> for a record of another kind.</param>
/// <param name="At">The substring the primary span starts at.</param>
/// <param name="After">The substring the primary insertion point follows.</param>
/// <param name="Skip">Characters skipped from the start of <paramref name="At"/>.</param>
/// <param name="Length">The primary span length.</param>
/// <param name="Found">The token text an <c>ExpectedSyntax_Kd</c> record names.</param>
/// <param name="Related">The related locations the record must carry, in order.</param>
/// <param name="Repairs">The repair candidates the record must offer, in order; none when omitted.</param>
internal sealed record SyntaxRecord(string Code, string? Form = null, string? At = null, string? After = null, int Skip = 0, int? Length = null, string? Found = null, SyntaxRelated[]? Related = null, SyntaxRepair[]? Repairs = null)
{
    /// <summary>Gets the primary location.</summary>
    public SyntaxLocation Location => new(this.At, this.After, this.Skip, this.Length);
}

/// <summary>One case of the syntax corpus: a small program and every record its complete check publishes.</summary>
/// <param name="Name">The case name.</param>
/// <param name="Source">The source text.</param>
/// <param name="Intended">The intended problem, or why the program is valid.</param>
/// <param name="Records">The expected records in result order; empty for a valid counterpart.</param>
internal sealed record SyntaxCase(string Name, string Source, string Intended, SyntaxRecord[] Records);

/// <summary>One mutation of a known-valid milestone program (docs/dev/DIAGNOSTICS.md §9.2).</summary>
/// <param name="Name">The case name.</param>
/// <param name="Program">The milestone program that is mutated.</param>
/// <param name="Before">The text replaced; it occurs exactly once in the program.</param>
/// <param name="After">The replacement.</param>
/// <param name="Intended">The intended problem.</param>
/// <param name="Expected">The codes of the expected primary records, written independently of the compiler.</param>
internal sealed record MutationCase(string Name, int Program, string Before, string After, string Intended, string[] Expected);

/// <summary>One SPEC example with the codes the SPEC gives it (plan §8 of the Complete Polonius A5 Plan; DECISIONS R4-M45): the
/// <c>kimi</c> block of a section that contains an anchor. Moved lines keep the row; a changed block, anchor or verdict fails it.</summary>
/// <param name="Name">The case name.</param>
/// <param name="Section">The number of the section whose own text holds the block, such as <c>15.6.7</c>.</param>
/// <param name="Anchor">Text that exactly one <c>kimi</c> block of the section contains.</param>
/// <param name="Family">The analysis or A5 family (plan §6.1) that decides the variants' verdicts, unless a variant names its own.</param>
/// <param name="Prelude">What the example assumes: declarations of the names it leaves undeclared, and the startup item <c>()</c> for an
/// example without runtime items (SPEC 22.4).</param>
/// <param name="Variants">The block as written, and with one commented line uncommented.</param>
/// <param name="Indent">The spaces before each line of the block, for a body fragment whose enclosing header ends the prelude.</param>
/// <param name="Omitted">The commented error lines without a variant, by their text after <c>// </c>, each with the reason.</param>
internal sealed record SpecCase(string Name, string Section, string Anchor, string Family, string Prelude, SpecVariant[] Variants, int Indent = 0, Dictionary<string, string>? Omitted = null);

/// <summary>One variant of a SPEC example and the Error records the SPEC gives it, written from SPEC and DIAGNOSTICS independently of the
/// compiler (docs/dev/DIAGNOSTICS.md §9.2).</summary>
/// <param name="Uncomment">The text after <c>// </c> of the one commented line the variant uncomments; null for the block as written.</param>
/// <param name="Expected">The Error records; none for an accepted variant.</param>
/// <param name="Family">The deciding analysis or family when it is not the case's.</param>
/// <param name="Old">The old path's records where they differ from the SPEC (plan §9.2).</param>
/// <param name="Mir">The MIR's records where they differ from the SPEC, for a landed family.</param>
internal sealed record SpecVariant(string? Uncomment, SpecRecord[] Expected, string? Family = null, SpecDifference? Old = null, SpecDifference? Mir = null);

/// <summary>One Error record of a SPEC example: its code, the code named by DIAGNOSTICS or by the catalog entry whose message states the
/// rule, and the text that locates the line holding its primary location.</summary>
/// <param name="Code">The code name.</param>
/// <param name="Line">Text that occurs exactly once in the variant's source.</param>
/// <param name="Dependent">Whether SPEC 23.1.2 leaves its report implementation-defined (only another expected failure causes it, or it is
/// another record of one), so that it may be absent.</param>
/// <param name="Basis">Why the code, when neither DIAGNOSTICS nor a catalog message states the rule.</param>
internal sealed record SpecRecord(string Code, string Line, bool Dependent = false, string? Basis = null);

/// <summary>A path's records of a variant where they differ from the SPEC, with the class the difference ledger would give the difference
/// ((a) an old-path error, which goes with the old path at R6-check; (b) a new-path error, including Binding's, which both paths share;
/// (c) a SPEC ambiguity; (d) an intended change, a rule the SPEC changed that MIR alone implements, plan §9.2; or (e) a form MIR does not
/// build yet) and the SPEC clause.</summary>
/// <param name="Records">The path's Error records.</param>
/// <param name="Category">The class.</param>
/// <param name="Clause">The SPEC sections deciding the variant, separated by spaces.</param>
/// <param name="Note">The difference.</param>
internal sealed record SpecDifference(SpecRecord[] Records, string Category, string Clause, string Note);

/// <summary>The shared diagnostic corpus: milestone programs, examples and mutation cases.</summary>
internal static class DiagnosticCorpus
{
    private static readonly Lazy<MutationCase[]> MutationCases = new(static () =>
        JsonSerializer.Deserialize<MutationCase[]>(File.ReadAllText(RepositoryPath("tests", "diagnostics", "mutations.json")))!);

    private static readonly Lazy<SyntaxCase[]> SyntaxCorpus = new(static () =>
        JsonSerializer.Deserialize<SyntaxCase[]>(File.ReadAllText(RepositoryPath("tests", "diagnostics", "syntax.json")))!);

    private static readonly Lazy<SpecCase[]> SpecCaseCorpus = new(static () =>
        JsonSerializer.Deserialize<SpecCase[]>(File.ReadAllText(RepositoryPath("tests", "diagnostics", "spec-cases.json")))!);

    private static readonly Lazy<((string Name, string Section, string Source)[] Blocks, HashSet<string> Sections)> SpecCorpus = new(ReadSpec);

    /// <summary>Gets the mutation cases of <c>tests/diagnostics/mutations.json</c>.</summary>
    internal static IReadOnlyList<MutationCase> Mutations => MutationCases.Value;

    /// <summary>Gets the syntax cases of <c>tests/diagnostics/syntax.json</c>.</summary>
    internal static IReadOnlyList<SyntaxCase> SyntaxCases => SyntaxCorpus.Value;

    /// <summary>Gets the SPEC examples of <c>tests/diagnostics/spec-cases.json</c>.</summary>
    internal static IReadOnlyList<SpecCase> SpecCases => SpecCaseCorpus.Value;

    /// <summary>Gets every <c>kimi</c> code block of the specification (<c>docs/spec/**/*.md</c>), named by its file below <c>docs/spec</c>
    /// without the extension, a slash and the line of its opening fence, with the number of the section whose own text holds it.</summary>
    internal static IReadOnlyList<(string Name, string Section, string Source)> SpecBlocks => SpecCorpus.Value.Blocks;

    /// <summary>Gets the numbers of the specification's numbered sections, such as <c>15.6.7</c> and <c>D.1</c>.</summary>
    internal static IReadOnlySet<string> SpecSections => SpecCorpus.Value.Sections;

    /// <summary>Gets a syntax case by name.</summary>
    /// <param name="name">The case name.</param>
    /// <returns>The case.</returns>
    internal static SyntaxCase Syntax(string name)
        => SyntaxCases.Single(x => x.Name == name);

    /// <summary>Gets the source of a specification block by name.</summary>
    /// <param name="name">The block name.</param>
    /// <returns>The source text.</returns>
    internal static string Spec(string name)
        => SpecBlocks.Single(x => x.Name == name).Source;

    /// <summary>Gets the specification block of a section that contains an anchor, requiring it to be the only one.</summary>
    /// <param name="section">The section number.</param>
    /// <param name="anchor">The anchor text.</param>
    /// <returns>The source text.</returns>
    internal static string SpecExample(string section, string anchor)
    {
        var blocks = SpecBlocks.Where(x => x.Section == section && x.Source.Contains(anchor, StringComparison.Ordinal)).ToArray();
        Assert.True(blocks.Length == 1, $"Exactly one kimi block of SPEC {section} must contain '{anchor}'; {blocks.Length} do.");
        return blocks[0].Source;
    }

    /// <summary>Gets a SPEC example variant's source: the case's prelude, then its block, indented, with the variant's commented line uncommented.</summary>
    /// <param name="example">The case.</param>
    /// <param name="variant">The variant.</param>
    /// <returns>The source text.</returns>
    internal static string Source(SpecCase example, SpecVariant variant)
    {
        var block = SpecExample(example.Section, example.Anchor);
        if (variant.Uncomment is { } line)
        {
            block = block.Remove(new SyntaxLocation("// " + line).Resolve(block).Start, 3);
        }

        return example.Prelude + string.Join('\n', block.Split('\n').Select(x => x.Length == 0 ? x : new string(' ', example.Indent) + x)) + "\n";
    }

    /// <summary>Checks one source as the product of a minimal Application project through the shared check entry (SPEC 23.2.2).</summary>
    /// <param name="source">The source text of <c>Program.kimi</c>.</param>
    /// <returns>The finalized output.</returns>
    internal static CheckOutput Check(string source)
    {
        var folder = Path.Combine(Path.GetTempPath(), "kimi-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var project = Path.Combine(folder, "Program.kimiproj");
            File.WriteAllText(project, $"OutputKind=\"Application\" Targets={{\"{WindowsProfile.Target}\"}}");
            File.WriteAllText(Path.Combine(folder, "Program.kimi"), source);
            Assert.True(Project.TryCreate(Kimigayo.CreateSilent(), null, project, CheckInputSource.Disk, out var loaded, out var failure), failure);
            return CheckService.Run(loaded, WindowsProfile.Target, CheckMode.Product, false, CheckInputSource.Disk, TestContext.Current.CancellationToken);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>Gets a path in the repository.</summary>
    /// <param name="parts">The path parts below the repository root.</param>
    /// <returns>The full path.</returns>
    internal static string RepositoryPath(params string[] parts)
        => Path.GetFullPath(Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "..", .. parts]));

    /// <summary>Reads a milestone program.</summary>
    /// <param name="number">The program number.</param>
    /// <returns>The source text.</returns>
    internal static string Milestone(int number)
        => File.ReadAllText(RepositoryPath("tests", "milestones", $"Milestone{number}.kimi"));

    /// <summary>Gets a mutation case by name.</summary>
    /// <param name="name">The case name.</param>
    /// <returns>The case.</returns>
    internal static MutationCase Mutation(string name)
        => Mutations.Single(x => x.Name == name);

    /// <summary>Applies a mutation to a copy of its program, requiring the replaced text to occur exactly once.</summary>
    /// <param name="mutation">The mutation.</param>
    /// <returns>The mutated source.</returns>
    internal static string Apply(MutationCase mutation)
    {
        var source = Milestone(mutation.Program);
        var index = source.IndexOf(mutation.Before, StringComparison.Ordinal);
        Assert.True(index >= 0 && index == source.LastIndexOf(mutation.Before, StringComparison.Ordinal), $"{mutation.Name}: the replaced text must occur exactly once.");
        return string.Concat(source.AsSpan(0, index), mutation.After, source.AsSpan(index + mutation.Before.Length));
    }

    // A fenced block (CommonMark, as docs/spec/02-source-and-lexical-structure.md states) opens at a line that begins with three or more backticks
    // or tildes and closes at the next line that is the same fence; the content of any block, kimi or not, is not searched for further
    // fences or headings. A heading outside blocks that begins with a section number (`### 15.6.7. Call reservations`) starts that section.
    private static ((string Name, string Section, string Source)[] Blocks, HashSet<string> Sections) ReadSpec()
    {
        var root = RepositoryPath("docs", "spec");
        var blocks = new List<(string Name, string Section, string Source)>();
        var sections = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(root, "*.md", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var name = Path.ChangeExtension(Path.GetRelativePath(root, file), null).Replace('\\', '/');
            var lines = File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            var section = string.Empty;
            for (var line = 0; line < lines.Length; line++)
            {
                if (Regex.Match(lines[line], @"^#+ ([0-9]+(?:\.[0-9]+)*|[A-Z](?:\.[0-9]+)+)\. ") is { Success: true } heading)
                {
                    section = heading.Groups[1].Value;
                    sections.Add(section);
                    continue;
                }

                var marker = lines[line].StartsWith('~') ? '~' : '`';
                var fence = lines[line][..(lines[line].Length - lines[line].TrimStart(marker).Length)];
                if (fence.Length < 3)
                {
                    continue;
                }

                var end = Array.FindIndex(lines, line + 1, x => x.TrimEnd() == fence);
                end = end < 0 ? lines.Length : end;
                if (lines[line][fence.Length..].Trim() == "kimi")
                {
                    blocks.Add(($"{name}/{line + 1}", section, string.Join('\n', lines[(line + 1)..end])));
                }

                line = end;
            }
        }

        return ([.. blocks], sections);
    }
}
