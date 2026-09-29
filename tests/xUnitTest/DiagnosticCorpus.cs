// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Xunit;

namespace XunitTest;

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

    /// <summary>Gets the mutation cases of <c>tests/diagnostics/mutations.json</c>.</summary>
    internal static IReadOnlyList<MutationCase> Mutations => MutationCases.Value;

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
