using System;
using System.Collections.Generic;
using System.Text;
using Tinyhand;

namespace Kimi.Diagnostics;

[TinyhandObject(ImplicitMemberNameAsKey = true, EnumAsString = true)]
public partial record class DiagnosticEntry
{
    public string Name { get; init; } = string.Empty;

    public DiagnosticSeverity Severity { get; init; } = DiagnosticSeverity.Error;

    public string Message { get; init; } = string.Empty;

    public string? Label { get; init; }

    public string? Fix { get; init; }

    public string? Note { get; init; }

    public DiagnosticEntry(string name, DiagnosticSeverity diagnosticSeverity, string message, string? label = default, string? fix = default, string? note = default)
    {
        this.Name = name;
        this.Severity = diagnosticSeverity;
        this.Message = message;
        this.Label = label;
        this.Fix = fix;
        this.Note = note;
    }
}
