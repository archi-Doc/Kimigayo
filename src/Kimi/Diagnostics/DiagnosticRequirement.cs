// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1402 // The requirement and its vocabulary are one unit.

using System.Diagnostics.CodeAnalysis;
using Kimi.Compiler;

namespace Kimi.Diagnostics;

/// <summary>
/// The requirement a fact concerns (docs/dev/DIAGNOSTICS.md §4.1): the partition of the phase that judges it and the
/// analysis-specific kind. Its stable name, such as <c>Binding.MissingName</c>, is public vocabulary like a code name.
/// </summary>
/// <param name="Partition">The owning partition.</param>
/// <param name="Kind">The kind within the partition: an analysis failure enumeration value, or zero.</param>
public readonly record struct DiagnosticRequirement(DiagnosticPartition Partition, ushort Kind)
{
    /// <summary>Gets the requirement that inputs and configuration are established.</summary>
    public static DiagnosticRequirement Input => new(DiagnosticPartition.Input, 0);

    /// <summary>Gets the requirement that the source follows the grammar.</summary>
    public static DiagnosticRequirement Syntax => new(DiagnosticPartition.Syntax, 0);

    /// <summary>Gets the requirement of a valid startup.</summary>
    public static DiagnosticRequirement Startup => new(DiagnosticPartition.Startup, 0);

    /// <summary>Gets the requirement of the control-flow and operand checks.</summary>
    public static DiagnosticRequirement ControlFlow => new(DiagnosticPartition.ControlFlow, 0);

    /// <summary>Gets the requirement that generation succeeds.</summary>
    public static DiagnosticRequirement Emission => new(DiagnosticPartition.Emission, 0);

    /// <summary>Gets the requirement of an ownership failure.</summary>
    /// <param name="failure">The failure.</param>
    /// <returns>The requirement.</returns>
    public static DiagnosticRequirement Ownership(OwnershipFailure failure) => new(DiagnosticPartition.Ownership, (ushort)failure);

    /// <summary>Gets the stable name.</summary>
    public string Name => DiagnosticRequirements.NameOf(this);

    public override string ToString() => this.Name;

    /// <summary>Gets the requirement of a Binding failure.</summary>
    /// <param name="failure">The failure.</param>
    /// <returns>The requirement.</returns>
    internal static DiagnosticRequirement Binding(BindingFailure failure) => new(DiagnosticPartition.Binding, (ushort)failure);
}

/// <summary>The requirement vocabulary: stable names from each phase's failure enumeration and their descriptions, validated when loaded.</summary>
public static class DiagnosticRequirements
{
    private const string ResourceName = "Diagnostics.DiagnosticRequirement.tinyhand";

    private static readonly Dictionary<string, string> Descriptions;

    static DiagnosticRequirements()
    {
        var assembly = typeof(DiagnosticRequirements).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetName().Name + "." + ResourceName);
        byte[]? bytes = null;
        if (stream is not null)
        {
            bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
        }

        (Descriptions, Anomalies) = Load(bytes);
    }

    /// <summary>Gets every requirement a phase can judge.</summary>
    public static IEnumerable<DiagnosticRequirement> All
    {
        get
        {
            yield return DiagnosticRequirement.Input;
            yield return DiagnosticRequirement.Syntax;
            foreach (var failure in Enum.GetValues<BindingFailure>())
            {
                yield return DiagnosticRequirement.Binding(failure);
            }

            yield return DiagnosticRequirement.Startup;
            yield return DiagnosticRequirement.ControlFlow;
            foreach (var failure in Enum.GetValues<OwnershipFailure>())
            {
                yield return DiagnosticRequirement.Ownership(failure);
            }

            yield return DiagnosticRequirement.Emission;
        }
    }

    /// <summary>Gets the anomalies found when the description table loaded; a valid table has none.</summary>
    public static IReadOnlyList<string> Anomalies { get; }

    /// <summary>Gets the stable name of a requirement.</summary>
    /// <param name="requirement">The requirement.</param>
    /// <returns>The name.</returns>
    public static string NameOf(DiagnosticRequirement requirement) => requirement.Partition switch
    {
        DiagnosticPartition.Input => "Input",
        DiagnosticPartition.Syntax => "Syntax",
        DiagnosticPartition.Binding => requirement.Kind == 0 ? "Binding" : "Binding." + (BindingFailure)requirement.Kind,
        DiagnosticPartition.Startup => "Startup",
        DiagnosticPartition.ControlFlow => "ControlFlow",
        DiagnosticPartition.Ownership => "Ownership." + (OwnershipFailure)requirement.Kind,
        _ => "Emission",
    };

    /// <summary>Gets the short description of a requirement.</summary>
    /// <param name="requirement">The requirement.</param>
    /// <param name="description">The description.</param>
    /// <returns><see langword="true"/> when the table describes the requirement.</returns>
    public static bool TryGetDescription(DiagnosticRequirement requirement, [MaybeNullWhen(false)] out string description)
        => Descriptions.TryGetValue(requirement.Name, out description);

    /// <summary>Loads a description table and lists its anomalies: unknown, duplicate and missing names and empty descriptions.</summary>
    /// <param name="utf8">The table text, or <see langword="null"/> when the resource is missing.</param>
    /// <returns>The descriptions by stable name and the anomalies.</returns>
    internal static (Dictionary<string, string> Descriptions, string[] Anomalies) Load(byte[]? utf8)
    {
        var descriptions = new Dictionary<string, string>(StringComparer.Ordinal);
        var anomalies = new List<string>();
        DiagnosticRequirementEntry[]? entries = null;
        if (utf8 is null)
        {
            anomalies.Add("The requirement table is missing.");
        }
        else
        {
            try
            {
                entries = TinyhandSerializer.DeserializeFromUtf8<DiagnosticRequirementEntry[]>(utf8);
            }
            catch (Exception ex)
            {
                anomalies.Add("The requirement table cannot be read: " + ex.Message);
            }
        }

        var names = new HashSet<string>(All.Select(static x => x.Name), StringComparer.Ordinal);
        foreach (var entry in entries ?? [])
        {
            if (!names.Contains(entry.Name))
            {
                anomalies.Add($"{entry.Name}: no requirement has this name.");
            }
            else if (entry.Description.Length == 0)
            {
                anomalies.Add($"{entry.Name}: the requirement has no description.");
            }
            else if (!descriptions.TryAdd(entry.Name, entry.Description))
            {
                anomalies.Add($"{entry.Name}: the entry is duplicated.");
            }
        }

        if (entries is not null)
        {
            foreach (var name in names)
            {
                if (!descriptions.ContainsKey(name))
                {
                    anomalies.Add($"{name}: the requirement has no entry.");
                }
            }
        }

        return (descriptions, anomalies.ToArray());
    }
}

/// <summary>One entry of the requirement description table.</summary>
[TinyhandObject(ImplicitMemberNameAsKey = true)]
internal sealed partial class DiagnosticRequirementEntry
{
    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;
}
