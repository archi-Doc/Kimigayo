// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
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
internal sealed record TestDiagnostic(string Code, DiagnosticSeverity Severity, string Message, string? Path, SourceSpan Span, string? Text)
{
    public override string ToString()
        => $"[{this.Severity}] {this.Path}{this.Span} {this.Code}: {this.Message}";
}

/// <summary>The one way tests read published diagnostics, so the compiler's diagnostic storage can change behind it.</summary>
internal static class TestDiagnostics
{
    /// <summary>Gets every published diagnostic of a compilation, ordered by source and position.</summary>
    /// <param name="compilation">The compilation.</param>
    /// <returns>The diagnostics.</returns>
    internal static TestDiagnostic[] Of(Compilation compilation)
        => Collect(compilation.Kimigayo, null);

    /// <summary>Gets the published diagnostics of one source of a compilation.</summary>
    /// <param name="compilation">The compilation.</param>
    /// <param name="path">The source path as the compilation names it, such as <c>Hello.kimi</c>.</param>
    /// <returns>The diagnostics.</returns>
    internal static TestDiagnostic[] Of(Compilation compilation, string path)
        => Collect(compilation.Kimigayo, path);

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

    private static TestDiagnostic[] Collect(Kimigayo kimigayo, string? path)
    {
        var records = new List<TestDiagnostic>();
        foreach (var collection in kimigayo.DiagnosticCollections.OrderBy(static x => x.Name, StringComparer.Ordinal))
        {
            if (path is null || collection.Name == path)
            {
                foreach (var diagnostic in collection.GetArray())
                {
                    var document = diagnostic.SourceDocument;
                    var text = document?.SourceText.Substring(diagnostic.Span.Start, diagnostic.Span.Length);
                    records.Add(new(diagnostic.Entry.Name, diagnostic.Entry.Severity, diagnostic.Message, document?.Path ?? diagnostic.Location, diagnostic.Span, text));
                }
            }
        }

        return records.ToArray();
    }
}
