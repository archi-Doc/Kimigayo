// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Checking;

#pragma warning disable SA1402 // The unit model is one vocabulary.

namespace Kimi.Lsp;

/// <summary>The kind of a language-server unit (SPEC 23.4.4).</summary>
internal enum UnitKind : byte
{
    /// <summary>A product unit.</summary>
    Product,

    /// <summary>A test unit.</summary>
    Test,

    /// <summary>A Blocked unit for a root that fails to load.</summary>
    FailedRoot,
}

/// <summary>Identifies a unit: its owner (a project file or an implicit source), kind and target.</summary>
/// <param name="Owner">The project file or implicit source.</param>
/// <param name="Kind">The kind.</param>
/// <param name="Target">The target, or empty when none could be selected.</param>
internal readonly record struct UnitKey(SourceIdentity Owner, UnitKind Kind, string Target) : IComparable<UnitKey>
{
    /// <inheritdoc/>
    public int CompareTo(UnitKey other)
    {
        var order = this.Owner.CompareTo(other.Owner);
        if (order == 0)
        {
            order = this.Kind.CompareTo(other.Kind);
        }

        return order == 0 ? string.CompareOrdinal(this.Target, other.Target) : order;
    }
}

/// <summary>A loaded project (SPEC 23.4.3): a derived item that records its project file and source listing.</summary>
internal sealed class LoadedProject : DerivedItem
{
    /// <summary>Gets the project file.</summary>
    public required SourceIdentity Path { get; init; }

    /// <summary>Gets the loaded project, or null when the load failed.</summary>
    public Project? Project { get; init; }

    /// <summary>Gets the load failure message.</summary>
    public string? Failure { get; init; }

    /// <summary>Gets the members: the discovered sources plus the <c>TestSources</c>.</summary>
    public HashSet<SourceIdentity> Members { get; init; } = [];

    /// <summary>Gets the ordinary sources selected by Product units, excluding TestSources-only members.</summary>
    public HashSet<SourceIdentity> ProductMembers { get; init; } = [];

    /// <summary>Gets the project files of the product dependencies.</summary>
    public string[] ProductReferences { get; init; } = [];

    /// <summary>Gets the project files of the test dependencies.</summary>
    public string[] TestReferences { get; init; } = [];

    /// <summary>Gets a value indicating whether the project lists <c>TestSources</c>.</summary>
    public bool HasTestSources { get; init; }
}

/// <summary>The candidate listings discovery read, kept as a derived item so re-validation watches them.</summary>
internal sealed class DiscoveryRecord : DerivedItem
{
}

/// <summary>What the worker checks for one unit: its key, project and mode (a prepared unit).</summary>
/// <param name="Key">The unit key.</param>
/// <param name="Project">The loaded project, or null for an implicit project or a failed unit.</param>
/// <param name="Display">The file where diagnostics without a usable location are shown.</param>
/// <param name="Mode">The check mode.</param>
/// <param name="Members">The member sources, which are known inputs of the unit.</param>
internal sealed record UnitPlan(UnitKey Key, LoadedProject? Project, SourceIdentity Display, CheckMode Mode, SourceIdentity[] Members);

/// <summary>One unit's adopted result: a derived item with its check output and the diagnostics it reports to each URI.</summary>
internal sealed class UnitResult : DerivedItem
{
    /// <summary>Gets the unit key.</summary>
    public required UnitKey Key { get; init; }

    /// <summary>Gets the check output.</summary>
    public required CheckOutput Output { get; init; }

    /// <summary>Gets the diagnostics per report URI (SPEC 23.4.7), each sorted; a checked source without diagnostics has an empty array.</summary>
    public required Dictionary<SourceIdentity, LspDiagnostic[]> Reports { get; init; }

    /// <summary>Gets the repair candidates per report URI whose edits all lie in that document (SPEC 23.4.8); a URI without candidates has no entry.</summary>
    public Dictionary<SourceIdentity, LspRepair[]> Repairs { get; init; } = [];
}
