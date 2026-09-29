// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;

namespace Kimi.Diagnostics;

/// <summary>The catalog definition of one diagnostic code (SPEC 23.3.6.1).</summary>
[TinyhandObject(ImplicitMemberNameAsKey = true, EnumAsString = true)]
public partial record class DiagnosticEntry
{
    public string Name { get; init; } = string.Empty;

    public DiagnosticSeverity Severity { get; init; } = DiagnosticSeverity.Error;

    /// <summary>Gets the category. The catalog must state it; an entry without one is a catalog anomaly.</summary>
    public DiagnosticCategory Category { get; init; } = (DiagnosticCategory)byte.MaxValue;

    public string Message { get; init; } = string.Empty;

    public string? Label { get; init; }

    /// <summary>Gets conditional repair advice. It is prose: no edit or guarantee is inferred from it.</summary>
    public string? Advice { get; init; }

    public string? Note { get; init; }

    /// <summary>Gets the number of message arguments, fixed when the catalog loads.</summary>
    [IgnoreMember]
    public int Arity => this.format?.MinimumArgumentCount ?? 0;

    [IgnoreMember]
    private CompositeFormat? format;

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

    /// <summary>Parses the message template once; a template that is not a valid composite format throws.</summary>
    /// <exception cref="FormatException">The message is not a valid composite format.</exception>
    internal void Prepare()
    {
        var composite = CompositeFormat.Parse(this.Message);
        this.format = composite.MinimumArgumentCount == 0 ? null : composite;
    }
}
