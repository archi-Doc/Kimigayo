// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;

namespace Kimi.Diagnostics;

/// <summary>One named, typed fact of a code: a message argument or evidence a report adds to its Reason (SPEC 23.3.6.2).</summary>
/// <param name="Name">The fact's name, fixed by its code.</param>
/// <param name="Kind">The kind of value in the Reason.</param>
/// <param name="IsType">Whether the value displays a Type; the Types of one record are bounded as a pair, keeping their differences.</param>
public readonly record struct DiagnosticParameter(string Name, DiagnosticValueKind Kind, bool IsType);

/// <summary>The catalog definition of one diagnostic code (SPEC 23.3.6.1).</summary>
[TinyhandObject(ImplicitMemberNameAsKey = true, EnumAsString = true)]
public partial record class DiagnosticEntry
{
    public string Name { get; init; } = string.Empty;

    public DiagnosticSeverity Severity { get; init; } = DiagnosticSeverity.Error;

    /// <summary>Gets the category. The catalog must state it; an entry without one is a catalog anomaly.</summary>
    public DiagnosticCategory Category { get; init; } = (DiagnosticCategory)byte.MaxValue;

    public string Message { get; init; } = string.Empty;

    /// <summary>Gets the label of the primary span. It may reference the Reason's facts in order, arguments then evidence ({0}, {1}, ...);
    /// a record whose report lacks a referenced fact has no label.</summary>
    public string? Label { get; init; }

    /// <summary>Gets conditional repair advice. It is prose: no edit or guarantee is inferred from it.</summary>
    public string? Advice { get; init; }

    public string? Note { get; init; }

    /// <summary>Gets the message arguments as <c>name:Kind</c> pairs separated by commas, one for each argument. A kind is
    /// <c>Number</c>, <c>Enumeration</c>, <c>Boolean</c>, <c>Text</c>, <c>Type</c> or <c>Requirement</c>.</summary>
    public string? Arguments { get; init; }

    /// <summary>Gets the facts that a report may add to the Reason beyond the message arguments, in the form of <see cref="Arguments"/>.
    /// Alternatives are separated by <c>|</c>; a report supplies all facts of one alternative or none.</summary>
    public string? Evidence { get; init; }

    /// <summary>Gets the number of message arguments, fixed when the catalog loads.</summary>
    [IgnoreMember]
    public int Arity => this.format?.MinimumArgumentCount ?? 0;

    /// <summary>Gets the message arguments' names and kinds, parsed when the catalog loads.</summary>
    [IgnoreMember]
    public DiagnosticParameter[] ArgumentSchema { get; private set; } = [];

    /// <summary>Gets the evidence names and kinds of the first alternative, parsed when the catalog loads.</summary>
    [IgnoreMember]
    public DiagnosticParameter[] EvidenceSchema { get; private set; } = [];

    /// <summary>Gets every evidence alternative, the first being <see cref="EvidenceSchema"/>.</summary>
    [IgnoreMember]
    public DiagnosticParameter[][] EvidenceAlternatives { get; private set; } = [[]];

    [IgnoreMember]
    private CompositeFormat? format;

    [IgnoreMember]
    private CompositeFormat? labelFormat;

    public DiagnosticEntry(string name, DiagnosticSeverity diagnosticSeverity, string message, DiagnosticCategory category = DiagnosticCategory.Language, string? label = default, string? advice = default, string? note = default)
    {
        this.Name = name;
        this.Severity = diagnosticSeverity;
        this.Category = category;
        this.Message = message;
        this.Label = label;
        this.Advice = advice;
        this.Note = note;
    }

    /// <summary>Formats the message with its arguments, which must match <see cref="Arity"/>.</summary>
    /// <param name="first">The first argument.</param>
    /// <param name="second">The second argument.</param>
    /// <returns>The message.</returns>
    public string FormatMessage(object? first, object? second)
        => this.format is not { } composite ? this.Message :
            composite.MinimumArgumentCount == 1 ? string.Format(CultureInfo.InvariantCulture, composite, first) :
            string.Format(CultureInfo.InvariantCulture, composite, first, second);

    /// <summary>Formats the label from the Reason's display values, arguments then evidence.</summary>
    /// <param name="values">The display values; a missing fact is <see langword="null"/> or beyond the end.</param>
    /// <returns>The label, or <see langword="null"/> when it references a missing fact.</returns>
    public string? FormatLabel(object?[] values)
    {
        if (this.labelFormat is not { } composite)
        {
            return this.Label;
        }

        for (var i = 0; i < composite.MinimumArgumentCount; i++)
        {
            if (i >= values.Length || values[i] is null)
            {
                return null;
            }
        }

        return string.Format(CultureInfo.InvariantCulture, composite, values);
    }

    /// <summary>Parses the templates and schemas once; a message that is not a valid composite format throws.</summary>
    /// <returns>An anomaly of the schema or label, or <see langword="null"/>.</returns>
    /// <exception cref="FormatException">The message or label is not a valid composite format.</exception>
    internal string? Prepare()
    {
        var composite = CompositeFormat.Parse(this.Message);
        this.format = composite.MinimumArgumentCount == 0 ? null : composite;
        if (this.Arity > 2)
        {
            return "the message takes more than two arguments.";
        }

        if (this.Arity == 0 && this.Message.AsSpan().IndexOfAny('{', '}') >= 0)
        {
            return "a message without arguments contains a brace.";
        }

        var written = string.IsNullOrWhiteSpace(this.Evidence) ? [string.Empty] : this.Evidence.Split('|');
        var alternatives = new DiagnosticParameter[written.Length][];
        if (ParseSchema(this.Arguments) is not { } arguments)
        {
            return "a fact is not written as name:Kind.";
        }

        for (var i = 0; i < written.Length; i++)
        {
            if (ParseSchema(written[i]) is not { } alternative || (alternative.Length == 0 && written.Length > 1))
            {
                return "a fact is not written as name:Kind.";
            }

            if (arguments.Concat(alternative).Select(static x => x.Name).Distinct(StringComparer.Ordinal).Count() != arguments.Length + alternative.Length)
            {
                return "fact names must be unique within the code.";
            }

            alternatives[i] = alternative;
        }

        var evidence = alternatives[0];
        this.ArgumentSchema = arguments;
        this.EvidenceSchema = evidence;
        this.EvidenceAlternatives = alternatives;

        if (arguments.Length != this.Arity)
        {
            return $"Arguments must name each message argument ({this.Arity}), not {arguments.Length}.";
        }

        if (this.Label is { } label)
        {
            var labelComposite = CompositeFormat.Parse(label);
            this.labelFormat = labelComposite.MinimumArgumentCount == 0 ? null : labelComposite;
            if (labelComposite.MinimumArgumentCount > arguments.Length + evidence.Length)
            {
                return "the label references a fact that the code does not name.";
            }
        }

        return null;
    }

    internal void ValidateValue(DiagnosticParameter parameter, object? value)
        => ValidateValue(this.Name, parameter, value);

    /// <summary>Selects the evidence alternative that a report's facts satisfy: the first whose arity and kinds they match.</summary>
    /// <param name="evidence">The reported facts.</param>
    /// <returns>The alternative's index, or -1 when none matches.</returns>
    internal int EvidenceAlternative(object?[] evidence)
    {
        for (var i = 0; i < this.EvidenceAlternatives.Length; i++)
        {
            var alternative = this.EvidenceAlternatives[i];
            if (alternative.Length != evidence.Length)
            {
                continue;
            }

            var matches = true;
            for (var j = 0; j < alternative.Length && matches; j++)
            {
                matches = IsValid(alternative[j], evidence[j]);
            }

            if (matches)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Checks that a reported value has the kind its definition names; a mismatch is a contract violation.</summary>
    /// <param name="owner">The name of the code or repair kind that defines the fact.</param>
    /// <param name="parameter">The fact's definition.</param>
    /// <param name="value">The reported value.</param>
    internal static void ValidateValue(string owner, DiagnosticParameter parameter, object? value)
    {
        if (!IsValid(parameter, value))
        {
            throw new DiagnosticContractException(DiagnosticFault.InvalidArgument, $"{owner}: {parameter.Name} requires {parameter.Kind}.");
        }
    }

    /// <summary>Parses a schema of <c>name:Kind</c> pairs, as a code's Arguments and Evidence and a repair kind's Facts are written.</summary>
    /// <param name="schema">The schema text; empty for no facts.</param>
    /// <returns>The parameters, or <see langword="null"/> when a pair is malformed.</returns>
    internal static DiagnosticParameter[]? ParseSchema(string? schema)
    {
        if (string.IsNullOrWhiteSpace(schema))
        {
            return [];
        }

        var parts = schema.Split(',', StringSplitOptions.TrimEntries);
        var parameters = new DiagnosticParameter[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            var separator = parts[i].IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                return null;
            }

            var name = parts[i][..separator];
            var kind = parts[i][(separator + 1)..];
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            parameters[i] = kind switch
            {
                "Type" => new(name, DiagnosticValueKind.Text, true),
                _ when Enum.TryParse<DiagnosticValueKind>(kind, false, out var value) && Enum.IsDefined(value) && value.ToString() == kind => new(name, value, false),
                _ => default,
            };

            if (parameters[i].Name is null)
            {
                return null;
            }
        }

        return parameters;
    }

    private static bool IsValid(DiagnosticParameter parameter, object? value)
        => parameter.Kind switch
        {
            DiagnosticValueKind.Number => value is byte or sbyte or short or ushort or int or uint or long or ulong or Int128 or UInt128 or decimal ||
                (value is double d && double.IsFinite(d)) || (value is float f && float.IsFinite(f)),
            DiagnosticValueKind.Boolean => value is bool,
            DiagnosticValueKind.Enumeration => value is Enum e && Enum.IsDefined(e.GetType(), e),
            DiagnosticValueKind.Text => value is not null,
            DiagnosticValueKind.Requirement => value is DiagnosticRequirement requirement && DiagnosticRequirements.TryGetPhrase(requirement, out _),
            DiagnosticValueKind.Origin => value is DiagnosticOrigin { Kind: "expression" or "borrow" or "omitted" or "closure" },
            _ => false,
        };
}
