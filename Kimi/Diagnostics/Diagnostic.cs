// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json.Serialization;
using Kimi.Compiler;

namespace Kimi.Diagnostics;

[ValueLinkObject(Isolation = IsolationLevel.Serializable)]
public sealed partial record class Diagnostic
{
    public SourceSpan Span { get; init; }

    public DiagnosticEntry Entry { get; init; }

    [JsonIgnore]
    public SourceDocument? SourceDocument { get; init; }

    public string Message { get; init; } = string.Empty;

    [Link(Primary = true, Unique = true, Type = ChainType.Ordered)]
    [JsonIgnore]
    public int StartPosition => this.Span.Start;

    [JsonIgnore]
    public partial GoshujinClass? Goshujin { get; set; }

    public Diagnostic(SourceSpan range, DiagnosticEntry entry, SourceDocument? sourceDocument = default)
    {
        this.Span = range;
        this.Entry = entry;
        this.SourceDocument = sourceDocument;
        this.Message = entry.Message;
    }

    public override string ToString()
    {
        return this.ToString(string.Empty);
    }

    public string ToString(string url)
    {
        if (!string.IsNullOrEmpty(url))
        {
            var range = this.SourceDocument?.GetSourceRange(this.Span).ToString() ?? this.Span.ToString();
            return $"[{this.Entry.Severity.ToString()}] {url}{range} {this.Message}";
        }
        else
        {
            return $"[{this.Entry.Severity.ToString()}] {this.Message}";
        }
    }
}
