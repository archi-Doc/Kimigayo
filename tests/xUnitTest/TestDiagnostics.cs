// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Diagnostics;

namespace XunitTest;

/// <summary>A published diagnostic as tests observe it: its code, severity, message, source path and primary span.</summary>
/// <param name="Code">The code name.</param>
/// <param name="Severity">The severity.</param>
/// <param name="Message">The message.</param>
/// <param name="Path">The source path, or <see langword="null"/> without a source.</param>
/// <param name="Span">The primary span; the default value without a span.</param>
/// <param name="Text">The source text under the primary span, or <see langword="null"/> without a source.</param>
/// <param name="Note">The Note.</param>
/// <param name="Label">The label of the primary span.</param>
/// <param name="Repairs">The repair candidates (SPEC 23.1.3), or <see langword="null"/> for none.</param>
internal sealed record TestDiagnostic(string Code, DiagnosticSeverity Severity, string Message, string? Path, SourceSpan Span, string? Text, string? Note, string? Label = null, RepairCandidate[]? Repairs = null)
{
    /// <summary>Gets the message and Note on separate lines, for tests that look for an explanation wherever it is placed.</summary>
    public string Explanation => string.Join('\n', new[] { this.Message, this.Note }.Where(static x => x is not null));

    /// <summary>Gets the candidates as kind, title, edits relative to the primary span and judged conditions (SPEC 23.1.3), fixed when the
    /// record is collected, so two records of one problem compare equal wherever the source text shifts (docs/dev/DIAGNOSTICS.md §9.3)
    /// and differ when a candidate changes.</summary>
    internal string? RepairSummary { get; init; }

    public bool Equals(TestDiagnostic? other)
        => other is not null && this.Code == other.Code && this.Severity == other.Severity && this.Message == other.Message && this.Path == other.Path && this.Span == other.Span &&
            this.Text == other.Text && this.Note == other.Note && this.Label == other.Label && this.RepairSummary == other.RepairSummary;

    public override int GetHashCode()
        => HashCode.Combine(this.Code, this.Severity, this.Message, this.Path, this.Span, this.Text, this.Label);

    public override string ToString()
        => $"[{this.Severity}] {this.Path}{this.Span} {this.Code}: {this.Message}";

    /// <summary>Summarizes candidates relative to a primary span start.</summary>
    /// <param name="repairs">The candidates.</param>
    /// <param name="start">The primary span start.</param>
    /// <returns>The summary, or <see langword="null"/> for none.</returns>
    internal static string? Summarize(RepairCandidate[]? repairs, int start)
        => repairs is null ? null : string.Join('\n', repairs.Select(x =>
            $"{x.Kind}|{x.Title}|{string.Join(',', x.Edits.Select(e => $"{e.Span.Start - start}+{e.Span.Length}:{e.Text}"))}|{string.Join(',', x.Verified)}|{string.Join(',', x.Required.Select(static c => c.Condition))}"));
}

/// <summary>The one way tests read published diagnostics, so the compiler's diagnostic storage can change behind it.</summary>
internal static class TestDiagnostics
{
    /// <summary>Gets every published diagnostic of a compilation in result order, finalizing every partition.</summary>
    /// <param name="compilation">The compilation.</param>
    /// <returns>The diagnostics.</returns>
    internal static TestDiagnostic[] Of(Compilation compilation)
        => Collect(compilation.Diagnostics, null, null);

    /// <summary>Gets the published diagnostics of one source of a compilation.</summary>
    /// <param name="compilation">The compilation.</param>
    /// <param name="path">The source path as the compilation names it, such as <c>Hello.kimi</c>.</param>
    /// <returns>The diagnostics.</returns>
    internal static TestDiagnostic[] Of(Compilation compilation, string path)
        => Collect(compilation.Diagnostics, path, null);

    /// <summary>Gets every published diagnostic of a compilation that holds two snapshots of one path, reading the text under the spans
    /// of that path from the snapshot the test names, since the path cannot tell which one a record names (PLAN G175).</summary>
    /// <param name="compilation">The compilation.</param>
    /// <param name="snapshot">The snapshot whose records the test reads.</param>
    /// <returns>The diagnostics.</returns>
    internal static TestDiagnostic[] Of(Compilation compilation, SourceDocument snapshot)
        => Collect(compilation.Diagnostics, null, snapshot);

    /// <summary>Gets every published diagnostic of the compilation that owns a source unit.</summary>
    /// <param name="kotonoha">The source unit.</param>
    /// <returns>The diagnostics.</returns>
    internal static TestDiagnostic[] Of(Kotonoha kotonoha)
        => Of(kotonoha.Compilation);

    /// <summary>Gets every published diagnostic of the compilation that owns a parsing context.</summary>
    /// <param name="context">The parsing context.</param>
    /// <returns>The diagnostics.</returns>
    internal static TestDiagnostic[] Of(CodeContext context)
        => Of(context.Kotonoha.Compilation);

    /// <summary>Reads the source text under a span of a finalized record, the one way tests read it by path.</summary>
    /// <param name="owner">The owner that finalized the record.</param>
    /// <param name="path">The path of the record's source.</param>
    /// <param name="span">The span.</param>
    /// <returns>The text, or <see langword="null"/> when no document has that path.</returns>
    /// <exception cref="InvalidOperationException">The owner holds two snapshots of the path (PLAN G175).</exception>
    internal static string? Text(DiagnosticOwner owner, string path, SourceSpan span)
        => owner.FindDocument(path)?.SourceText.Substring(span.Start, span.Length);

    private static TestDiagnostic[] Collect(DiagnosticOwner owner, string? path, SourceDocument? snapshot)
    {
        var result = owner.Finalize(DiagnosticPartition.Input, DiagnosticPartition.Emission);
        var records = new List<TestDiagnostic>(result.Diagnostics.Length);
        foreach (var diagnostic in result.Diagnostics)
        {
            var source = diagnostic.Source < 0 ? null : result.Sources[diagnostic.Source].Path;
            if (path is not null && source != path)
            {
                continue;
            }

            var span = diagnostic.Span ?? default;
            var text = diagnostic.Span is not { } primary || source is null ? null
                : snapshot is not null && snapshot.Path == source ? snapshot.SourceText.Substring(primary.Start, primary.Length) : Text(owner, source, primary);
            records.Add(new(diagnostic.Code, diagnostic.Severity, diagnostic.Message, source, span, text, diagnostic.Note, diagnostic.Label, diagnostic.Repairs) { RepairSummary = TestDiagnostic.Summarize(diagnostic.Repairs, span.Start) });
        }

        return records.ToArray();
    }
}
