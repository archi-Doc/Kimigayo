// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json.Serialization;

namespace Kimigayo.Diagnostics;

[ValueLinkObject(Isolation = IsolationLevel.Serializable)]
public sealed partial record class Diagnostic
{
    public SourceRange Range { get; init; }

    public DiagnosticSeverity Severity { get; init; }

    public string? Code { get; init; }

    // public string? CodeDescription { get; init; }

    public string? Source { get; init; }

    public string Message { get; init; } = string.Empty;

    [Link(Primary = true, Unique = true, Type = ChainType.Ordered)]
    [JsonIgnore]
    public SourcePosition StartPosition => this.Range.Start;

    [JsonIgnore]
    public partial GoshujinClass? Goshujin { get; set; }

    public Diagnostic(SourceRange range, DiagnosticSeverity severity, string message)
    {
        this.Range = range;
        this.Severity = severity;
        this.Message = message;
    }

    public override string ToString()
    {
        return this.ToString(string.Empty);
    }

    public string ToString(string url)
    {
        if (!string.IsNullOrEmpty(url))
        {
            return $"[{this.Severity.ToString()}] {url}{this.Range.ToString()} {this.Message}";
        }
        else
        {
            return $"[{this.Severity.ToString()}] {this.Message}";
        }
    }
}
