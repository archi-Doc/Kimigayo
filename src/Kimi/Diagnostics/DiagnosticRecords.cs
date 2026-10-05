// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1402 // The published record vocabulary is one unit.
#pragma warning disable SA1649 // File name should match first type name

using System.Text.Json.Serialization;

namespace Kimi.Diagnostics;

/// <summary>The kind of a Reason value (SPEC 23.3.6.2).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DiagnosticValueKind>))]
public enum DiagnosticValueKind : byte
{
    /// <summary>An exact number.</summary>
    Number,

    /// <summary>An enumeration value by its stable name.</summary>
    Enumeration,

    /// <summary>A Boolean.</summary>
    Boolean,

    /// <summary>A bounded display value, such as a Type; it may be elided.</summary>
    Text,

    /// <summary>A requirement by its stable name, such as <c>Syntax.Expression</c>; a message displays its phrase.</summary>
    Requirement,

    /// <summary>An Origin display (SPEC 23.3.6.5): its bounded string, with its display kind in <see cref="DiagnosticValue.Origin"/>.</summary>
    Origin,
}

/// <summary>An Origin as a Reason names it (SPEC 23.3.6.5): a display kind and a bounded string.</summary>
/// <param name="Kind">The display kind: <c>expression</c>, <c>borrow</c>, <c>omitted</c> or <c>closure</c>.</param>
/// <param name="Text">The display string, such as <c>x</c>, <c>(a and b)</c>, <c>local@ref</c> or <c>call receiver</c>.</param>
public readonly record struct DiagnosticOrigin(string Kind, string Text)
{
    /// <inheritdoc/>
    public override string ToString() => this.Text;
}

/// <summary>One typed fact of a record's Reason (SPEC 23.3.6.2).</summary>
/// <param name="Name">The fact's name, fixed by its code.</param>
/// <param name="Kind">The kind of value.</param>
/// <param name="Value">The exact value, or the bounded display text.</param>
/// <param name="Elided">Whether a bounded display value omits part of its text.</param>
/// <param name="Origin">The display kind of an Origin value (SPEC 23.3.6.5); present exactly when <paramref name="Kind"/> is Origin.</param>
public readonly record struct DiagnosticValue(string Name, DiagnosticValueKind Kind, string Value, bool Elided = false, string? Origin = null)
{
    /// <summary>Creates an enumeration value.</summary>
    /// <typeparam name="T">The enumeration.</typeparam>
    /// <param name="name">The fact's name.</param>
    /// <param name="value">The value.</param>
    /// <returns>The fact.</returns>
    public static DiagnosticValue Enumeration<T>(string name, T value)
        where T : struct, Enum
        => new(name, DiagnosticValueKind.Enumeration, value.ToString());
}

/// <summary>A part of a record that a limit omitted (SPEC 23.3.6.2).</summary>
/// <param name="Part">What was omitted, such as <c>related locations</c> or <c>excerpt lines</c>.</param>
/// <param name="Count">How many were omitted.</param>
public readonly record struct DiagnosticOmission(string Part, int Count)
{
    /// <summary>Gets the rendered text of the omission.</summary>
    /// <returns>The text, such as <c>3 more related locations omitted</c>.</returns>
    public override string ToString() => $"{this.Count} more {this.Part} omitted";
}

/// <summary>One source a result's records name (SPEC 23.3.6.3).</summary>
/// <param name="Path">The display path: a file path, a <c>compiler://</c> identity, or a name given by the source's creator.</param>
/// <param name="IsInput">Whether the source is an input the check read, so its spans are positions in that input.</param>
public sealed record DiagnosticSource(string Path, bool IsInput);

/// <summary>One line of a bounded source excerpt, with the underlined columns of the primary span.</summary>
/// <param name="Line">The one-based line number.</param>
/// <param name="Text">The line text with tabs expanded, possibly clipped around the underline.</param>
/// <param name="Start">The first underlined display column in <paramref name="Text"/>.</param>
/// <param name="Length">The underlined width, at least one column.</param>
public readonly record struct DiagnosticExcerptLine(int Line, string Text, int Start, int Length);

/// <summary>Presentation data computed from the immutable source when a result is finalized; never used for decisions.</summary>
/// <param name="Range">The primary span as lines and UTF-16 characters, or <see langword="null"/> without a span.</param>
/// <param name="Excerpt">The bounded source excerpt around the primary span.</param>
public sealed record DiagnosticDisplay(SourceRange? Range, DiagnosticExcerptLine[] Excerpt)
{
    public bool Equals(DiagnosticDisplay? other)
        => other is not null && this.Range == other.Range && this.Excerpt.AsSpan().SequenceEqual(other.Excerpt);

    public override int GetHashCode()
        => HashCode.Combine(this.Range, this.Excerpt.Length);
}

/// <summary>A location related to a record, with its role (SPEC 23.3.6.2).</summary>
/// <param name="Role">The role, such as <c>prerequisite</c> or <c>declaration</c>.</param>
/// <param name="Source">The source table index, or -1 without a source.</param>
/// <param name="Span">The span, or <see langword="null"/> for the whole input.</param>
/// <param name="Range">The span as lines and UTF-16 characters, for display; <see langword="null"/> without a span.</param>
/// <param name="Label">A short description of the location.</param>
public sealed record DiagnosticRelated(string Role, int Source, SourceSpan? Span, SourceRange? Range, string? Label)
{
    /// <summary>Gets the alternative text of the location (SPEC 23.4.7): its role, where it is and its label.</summary>
    /// <param name="path">The displayed path of its source, or <see langword="null"/> without a source.</param>
    /// <returns>The text, such as <c>prerequisite: main.kimi:3:5: the program has a valid startup</c>.</returns>
    public string Describe(string? path)
    {
        var location = path is null ? null : this.Range is { } range ? $"{path}:{range.Start.Line + 1}:{range.Start.Character + 1}" : path;
        return (location, this.Label) switch
        {
            (null, null) => this.Role,
            (null, { } label) => $"{this.Role}: {label}",
            ({ } at, null) => $"{this.Role}: {at}",
            ({ } at, { } label) => $"{this.Role}: {at}: {label}",
        };
    }
}

/// <summary>One published problem with its explanation (SPEC 23.3.6.2). No compiler object escapes into it.</summary>
/// <param name="Code">The code name.</param>
/// <param name="Severity">The severity.</param>
/// <param name="Category">The category.</param>
/// <param name="Message">The self-contained message.</param>
/// <param name="Source">The index of the primary location's source in the result's source table, or -1 without a source.</param>
/// <param name="Span">The primary span in UTF-16 code units, or <see langword="null"/> for the whole input.</param>
public sealed record CheckDiagnostic(string Code, DiagnosticSeverity Severity, DiagnosticCategory Category, string Message, int Source, SourceSpan? Span)
{
    /// <summary>Gets the short description of the primary span.</summary>
    public string? Label { get; init; }

    /// <summary>Gets the code's typed facts.</summary>
    public DiagnosticValue[]? Reason { get; init; }

    /// <summary>Gets the related locations with their roles.</summary>
    public DiagnosticRelated[]? Related { get; init; }

    /// <summary>Gets further explanation.</summary>
    public string? Note { get; init; }

    /// <summary>Gets conditional repair advice; no edit is inferred from it.</summary>
    public string? Advice { get; init; }

    /// <summary>Gets the parts that limits omitted, with their counts.</summary>
    public DiagnosticOmission[]? Omissions { get; init; }

    /// <summary>Gets the presentation data.</summary>
    public DiagnosticDisplay? Display { get; init; }

    /// <summary>Gets the repair candidates (SPEC 23.3.6.9): alternative structured edits that resolve the problem, in catalog order then position.</summary>
    public RepairCandidate[]? Repairs { get; init; }

    public bool Equals(CheckDiagnostic? other)
        => other is not null && this.Code == other.Code && this.Severity == other.Severity && this.Category == other.Category &&
            this.Message == other.Message && this.Source == other.Source && this.Span == other.Span && this.Label == other.Label &&
            this.Note == other.Note && this.Advice == other.Advice && Equals(this.Display, other.Display) &&
            (this.Reason ?? []).AsSpan().SequenceEqual(other.Reason ?? []) && (this.Related ?? []).AsSpan().SequenceEqual(other.Related ?? []) &&
            (this.Omissions ?? []).AsSpan().SequenceEqual(other.Omissions ?? []) && (this.Repairs ?? []).AsSpan().SequenceEqual(other.Repairs ?? []);

    public override int GetHashCode()
        => HashCode.Combine(this.Code, this.Source, this.Span, this.Message);
}

/// <summary>The finalized diagnostics of one result: records in result order and the sources they name (SPEC 23.3.6).</summary>
/// <param name="Diagnostics">The records.</param>
/// <param name="Sources">The source table.</param>
public sealed record DiagnosticResult(CheckDiagnostic[] Diagnostics, DiagnosticSource[] Sources)
{
    /// <summary>Gets an empty result.</summary>
    public static DiagnosticResult Empty { get; } = new([], []);

    /// <summary>Gets a value indicating whether a record is an Error.</summary>
    public bool HasErrors => Array.Exists(this.Diagnostics, static x => x.Severity == DiagnosticSeverity.Error);

    public bool Equals(DiagnosticResult? other)
        => other is not null && this.Diagnostics.AsSpan().SequenceEqual(other.Diagnostics) && this.Sources.AsSpan().SequenceEqual(other.Sources);

    public override int GetHashCode()
        => HashCode.Combine(this.Diagnostics.Length, this.Sources.Length);
}
