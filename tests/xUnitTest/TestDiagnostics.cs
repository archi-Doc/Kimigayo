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
/// <param name="Advice">The Advice.</param>
internal sealed record TestDiagnostic(string Code, DiagnosticSeverity Severity, string Message, string? Path, SourceSpan Span, string? Text, string? Note, string? Advice)
{
    /// <summary>Gets the message, Note and Advice on separate lines, for tests that look for an explanation wherever it is placed.</summary>
    public string Explanation => string.Join('\n', new[] { this.Message, this.Note, this.Advice }.Where(static x => x is not null));

    public override string ToString()
        => $"[{this.Severity}] {this.Path}{this.Span} {this.Code}: {this.Message}";
}

/// <summary>The one way tests read published diagnostics, so the compiler's diagnostic storage can change behind it.</summary>
internal static class TestDiagnostics
{
    /// <summary>Gets every published diagnostic of a compilation in result order, finalizing every partition.</summary>
    /// <param name="compilation">The compilation.</param>
    /// <returns>The diagnostics.</returns>
    internal static TestDiagnostic[] Of(Compilation compilation)
        => Collect(compilation.Diagnostics, null);

    /// <summary>Gets the published diagnostics of one source of a compilation.</summary>
    /// <param name="compilation">The compilation.</param>
    /// <param name="path">The source path as the compilation names it, such as <c>Hello.kimi</c>.</param>
    /// <returns>The diagnostics.</returns>
    internal static TestDiagnostic[] Of(Compilation compilation, string path)
        => Collect(compilation.Diagnostics, path);

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

    private static TestDiagnostic[] Collect(DiagnosticOwner owner, string? path)
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
            var text = diagnostic.Span is { } primary && source is not null && owner.FindDocument(source) is { } document ? document.SourceText.Substring(primary.Start, primary.Length) : null;
            records.Add(new(diagnostic.Code, diagnostic.Severity, diagnostic.Message, source, span, text, diagnostic.Note, diagnostic.Advice));
        }

        return records.ToArray();
    }
}
