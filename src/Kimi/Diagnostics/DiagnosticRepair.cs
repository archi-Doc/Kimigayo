// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1402 // The repair vocabulary is one unit.
#pragma warning disable SA1649 // File name should match first type name

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace Kimi.Diagnostics;

/// <summary>A condition a repair candidate rests on (SPEC 23.3.6.9): the closed vocabulary, in display order.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RepairCondition>))]
public enum RepairCondition : byte
{
    /// <summary>The Place offers Take and is a Movable Place (SPEC 15.1.5).</summary>
    Take,

    /// <summary>The lending point is exclusively writable (SPEC 15.1.5).</summary>
    ExclusiveAccess,

    /// <summary>The edited operation and every later use of the affected Place satisfy the usage conditions of SPEC 10.6.</summary>
    UsageLegality,

    /// <summary>The visibility of Names, the order of destruction and defer, the Context and result supply of each expression and the targets of control transfers are unchanged.</summary>
    Structure,
}

/// <summary>The kind of a repair candidate (SPEC 23.3.6.9), in catalog order; its stable name is <c>Repair.&lt;Kind&gt;</c>.</summary>
public enum RepairKind : byte
{
    /// <summary>Append <c>@move</c> to transfer a Place.</summary>
    Transfer,

    /// <summary>Append <c>@ref</c> to borrow a Place.</summary>
    Borrow,

    /// <summary>Append <c>@uniq</c> or <c>@objuniq</c> to borrow a Place exclusively.</summary>
    BorrowExclusively,

    /// <summary>Remove an Unsafe Block and keep its statements.</summary>
    RemoveUnsafe,

    /// <summary>Replace one token with another.</summary>
    ReplaceToken,

    /// <summary>Insert a token at an insertion point.</summary>
    InsertToken,

    /// <summary>Propagate a failure with <c>try</c> and discard the success value.</summary>
    PropagateFailure,

    /// <summary>Discard a result explicitly.</summary>
    ExplicitDiscard,

    /// <summary>The number of kinds; not a kind.</summary>
    Count,
}

/// <summary>One edit of a repair candidate (SPEC 23.3.6.9): a span of a recorded input's immutable text and its replacement.</summary>
/// <param name="Source">The source table index of the recorded input.</param>
/// <param name="Span">The replaced span in UTF-16 code units; an empty span is an insertion point.</param>
/// <param name="Range">The span as lines and UTF-16 characters, for display.</param>
/// <param name="Text">The replacement text; empty for a deletion.</param>
/// <param name="Replaced">The bounded text the span covers, for display; <see langword="null"/> for an insertion.</param>
public readonly record struct RepairEdit(int Source, SourceSpan Span, SourceRange? Range, string Text, string? Replaced = null);

/// <summary>A relevant condition the check could not decide, with the phrase formed from the condition's template and the candidate's facts.</summary>
/// <param name="Condition">The condition.</param>
/// <param name="Phrase">The phrase, such as <c>x offers Take and is a Movable Place</c>.</param>
public readonly record struct RequiredCondition(RepairCondition Condition, string Phrase);

/// <summary>A structured edit that resolves a record's problem, with the conditions its acceptance rests on (SPEC 23.3.6.9).</summary>
/// <param name="Kind">The stable name of the kind, such as <c>Repair.Transfer</c>.</param>
/// <param name="Title">One sentence formed from the kind's template and the facts.</param>
/// <param name="Facts">The kind's typed facts, as in a Reason; <see langword="null"/> when the kind has none.</param>
/// <param name="Edits">The edits, in position order, none overlapping.</param>
/// <param name="Verified">The relevant conditions the check established.</param>
/// <param name="Required">The relevant conditions the check could not decide.</param>
public sealed record RepairCandidate(string Kind, string Title, DiagnosticValue[]? Facts, RepairEdit[] Edits, RepairCondition[] Verified, RequiredCondition[] Required)
{
    public bool Equals(RepairCandidate? other)
        => other is not null && this.Kind == other.Kind && this.Title == other.Title && (this.Facts ?? []).AsSpan().SequenceEqual(other.Facts ?? []) &&
            this.Edits.AsSpan().SequenceEqual(other.Edits) && this.Verified.AsSpan().SequenceEqual(other.Verified) && this.Required.AsSpan().SequenceEqual(other.Required);

    public override int GetHashCode()
        => HashCode.Combine(this.Kind, this.Title, this.Edits.Length);
}

/// <summary>The catalog of repair kinds (SPEC 23.3.6.9), validated once when it loads: each kind's title template, facts and relevant conditions.</summary>
public static class RepairKinds
{
    private const string ResourceName = "Diagnostics.RepairKind.tinyhand";

    private static readonly RepairKindEntry?[] Table;

    static RepairKinds()
    {
        var assembly = typeof(RepairKinds).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetName().Name + "." + ResourceName);
        byte[]? bytes = null;
        if (stream is not null)
        {
            bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
        }

        (Table, Anomalies) = Load(bytes);
    }

    /// <summary>Gets the anomalies found when the catalog loaded; a valid catalog has none, and any anomaly faults every report that offers a candidate.</summary>
    public static IReadOnlyList<string> Anomalies { get; }

    /// <summary>Gets the stable name of a kind.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The name, such as <c>Repair.Transfer</c>.</returns>
    public static string NameOf(RepairKind kind) => "Repair." + kind;

    /// <summary>Gets the catalog entry of a kind.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="entry">The entry.</param>
    /// <returns><see langword="true"/> when the catalog defines the kind.</returns>
    public static bool TryGet(RepairKind kind, [MaybeNullWhen(false)] out RepairKindEntry entry)
    {
        entry = kind >= 0 && kind < RepairKind.Count ? Table[(int)kind] : null;
        return entry is not null;
    }

    /// <summary>Loads a catalog and lists its anomalies: an unreadable resource, unknown, duplicate and missing entries, invalid
    /// templates and schemas, unknown conditions, and a condition whose phrase needs a fact the kind does not name.</summary>
    /// <param name="utf8">The catalog text, or <see langword="null"/> when the resource is missing.</param>
    /// <returns>The entries by kind and the anomalies.</returns>
    internal static (RepairKindEntry?[] Table, string[] Anomalies) Load(byte[]? utf8)
    {
        var table = new RepairKindEntry?[(int)RepairKind.Count];
        var anomalies = new List<string>();
        RepairKindEntry[]? entries = null;
        if (utf8 is null)
        {
            anomalies.Add("The repair catalog resource is missing.");
        }
        else
        {
            try
            {
                entries = TinyhandSerializer.DeserializeFromUtf8<RepairKindEntry[]>(utf8);
            }
            catch (Exception ex)
            {
                anomalies.Add("The repair catalog cannot be read: " + ex.Message);
            }
        }

        foreach (var entry in entries ?? [])
        {
            if (!entry.Name.StartsWith("Repair.", StringComparison.Ordinal) || !Enum.TryParse<RepairKind>(entry.Name.AsSpan("Repair.".Length), false, out var kind) ||
                kind < 0 || kind >= RepairKind.Count || NameOf(kind) != entry.Name)
            {
                anomalies.Add($"{entry.Name}: no RepairKind has this name.");
                continue;
            }

            if (table[(int)kind] is not null)
            {
                anomalies.Add($"{entry.Name}: the entry is duplicated.");
                continue;
            }

            if (entry.Title.Length == 0)
            {
                anomalies.Add($"{entry.Name}: the entry has no title.");
                continue;
            }

            string? anomaly;
            try
            {
                anomaly = entry.Prepare();
            }
            catch (FormatException ex)
            {
                anomaly = "the title is not a valid template: " + ex.Message;
            }

            if (anomaly is not null)
            {
                anomalies.Add($"{entry.Name}: {anomaly}");
                continue;
            }

            table[(int)kind] = entry;
        }

        if (entries is not null)
        {
            for (var kind = (RepairKind)0; kind < RepairKind.Count; kind++)
            {
                if (table[(int)kind] is null && !anomalies.Exists(x => x.StartsWith(NameOf(kind) + ":", StringComparison.Ordinal)))
                {
                    anomalies.Add($"{NameOf(kind)}: the kind has no catalog entry.");
                }
            }
        }

        return (table, anomalies.ToArray());
    }
}

/// <summary>The catalog definition of one repair kind (SPEC 23.3.6.9).</summary>
[TinyhandObject(ImplicitMemberNameAsKey = true)]
public sealed partial class RepairKindEntry
{
    [IgnoreMember]
    private CompositeFormat? format;

    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the title template; it may reference the facts in order ({0}, {1}, ...).</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Gets the facts as <c>name:Kind</c> pairs separated by commas, in the form of a code's Arguments; a candidate supplies all of them.</summary>
    public string? Facts { get; init; }

    /// <summary>Gets the relevant conditions, separated by commas; a candidate judges each as verified or required.</summary>
    public string? Conditions { get; init; }

    /// <summary>Gets the facts' names and kinds, parsed when the catalog loads.</summary>
    [IgnoreMember]
    public DiagnosticParameter[] FactSchema { get; private set; } = [];

    /// <summary>Gets the relevant conditions, parsed when the catalog loads.</summary>
    [IgnoreMember]
    internal RepairConditionSet Relevant { get; private set; }

    /// <summary>Formats the title from the facts' display values.</summary>
    /// <param name="values">The display values, one per fact.</param>
    /// <returns>The title.</returns>
    public string FormatTitle(object?[] values)
        => this.format is { } composite ? string.Format(CultureInfo.InvariantCulture, composite, values) : this.Title;

    /// <summary>Parses the template, the schema and the conditions once.</summary>
    /// <returns>An anomaly, or <see langword="null"/>.</returns>
    /// <exception cref="FormatException">The title is not a valid composite format.</exception>
    internal string? Prepare()
    {
        var composite = CompositeFormat.Parse(this.Title);
        this.format = composite.MinimumArgumentCount == 0 ? null : composite;
        if (DiagnosticEntry.ParseSchema(this.Facts) is not { } facts)
        {
            return "a fact is not written as name:Kind.";
        }

        if (facts.Select(static x => x.Name).Distinct(StringComparer.Ordinal).Count() != facts.Length)
        {
            return "fact names must be unique within the kind.";
        }

        if (composite.MinimumArgumentCount > facts.Length)
        {
            return "the title references a fact that the kind does not name.";
        }

        this.FactSchema = facts;
        var relevant = RepairConditionSet.None;
        foreach (var part in (this.Conditions ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<RepairCondition>(part, false, out var condition) || !Enum.IsDefined(condition) || condition.ToString() != part)
            {
                return $"{part} is not a repair condition.";
            }

            if (RepairConditions.Template(condition).MinimumArgumentCount > facts.Length)
            {
                return $"the phrase of {part} references a fact that the kind does not name.";
            }

            relevant |= RepairConditions.Flag(condition);
        }

        this.Relevant = relevant;
        return null;
    }
}

/// <summary>The relevant conditions of a candidate as a set, judged verified or required by the recorder; a refuted condition withholds the candidate.</summary>
[Flags]
internal enum RepairConditionSet : byte
{
    None = 0,
    Take = 1 << (int)RepairCondition.Take,
    ExclusiveAccess = 1 << (int)RepairCondition.ExclusiveAccess,
    UsageLegality = 1 << (int)RepairCondition.UsageLegality,
    Structure = 1 << (int)RepairCondition.Structure,
}

/// <summary>The closed vocabulary of repair conditions (SPEC 23.3.6.9) and the phrase templates of required conditions over a candidate's facts.</summary>
internal static class RepairConditions
{
    private static readonly CompositeFormat TakePhrase = CompositeFormat.Parse("{0} offers Take and is a Movable Place");
    private static readonly CompositeFormat ExclusiveAccessPhrase = CompositeFormat.Parse("the lending point of {0} is exclusively writable");
    private static readonly CompositeFormat UsageLegalityPhrase = CompositeFormat.Parse("the edited operation and every later use of {0} satisfy the initialization, Loan and lifetime conditions");
    private static readonly CompositeFormat StructurePhrase = CompositeFormat.Parse("the visibility of Names, the order of destruction and defer, the Context of each expression and the targets of control transfers are unchanged");

    /// <summary>Gets the set flag of a condition.</summary>
    /// <param name="condition">The condition.</param>
    /// <returns>The flag.</returns>
    internal static RepairConditionSet Flag(RepairCondition condition) => (RepairConditionSet)(1 << (int)condition);

    /// <summary>Gets the phrase template of a condition.</summary>
    /// <param name="condition">The condition.</param>
    /// <returns>The template over the candidate's facts.</returns>
    internal static CompositeFormat Template(RepairCondition condition) => condition switch
    {
        RepairCondition.Take => TakePhrase,
        RepairCondition.ExclusiveAccess => ExclusiveAccessPhrase,
        RepairCondition.UsageLegality => UsageLegalityPhrase,
        _ => StructurePhrase,
    };

    /// <summary>Forms the phrase of a required condition from the candidate's display values.</summary>
    /// <param name="condition">The condition.</param>
    /// <param name="values">The display values, one per fact.</param>
    /// <returns>The phrase.</returns>
    internal static string Phrase(RepairCondition condition, object?[] values)
        => string.Format(CultureInfo.InvariantCulture, Template(condition), values);
}
