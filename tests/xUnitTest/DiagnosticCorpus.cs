// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
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

/// <summary>One repair candidate a syntax record must offer (SPEC 23.3.6.9), as its kind and located edits.</summary>
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

/// <summary>The shared diagnostic corpus: milestone programs, examples and mutation cases.</summary>
internal static class DiagnosticCorpus
{
    private static readonly Lazy<MutationCase[]> MutationCases = new(static () =>
        JsonSerializer.Deserialize<MutationCase[]>(File.ReadAllText(RepositoryPath("tests", "diagnostics", "mutations.json")))!);

    private static readonly Lazy<SyntaxCase[]> SyntaxCorpus = new(static () =>
        JsonSerializer.Deserialize<SyntaxCase[]>(File.ReadAllText(RepositoryPath("tests", "diagnostics", "syntax.json")))!);

    /// <summary>Gets the mutation cases of <c>tests/diagnostics/mutations.json</c>.</summary>
    internal static IReadOnlyList<MutationCase> Mutations => MutationCases.Value;

    /// <summary>Gets the syntax cases of <c>tests/diagnostics/syntax.json</c>.</summary>
    internal static IReadOnlyList<SyntaxCase> SyntaxCases => SyntaxCorpus.Value;

    /// <summary>Gets a syntax case by name.</summary>
    /// <param name="name">The case name.</param>
    /// <returns>The case.</returns>
    internal static SyntaxCase Syntax(string name)
        => SyntaxCases.Single(x => x.Name == name);

    /// <summary>Checks one source as the product of a minimal Application project through the shared check entry (SPEC 23.3.2).</summary>
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
}
