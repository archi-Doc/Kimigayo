// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi;

using Kimi.Diagnostics;

public class Kimigayo
{
    private readonly IConsoleService consoleService;

    public KimiSettings Settings { get; }

    /// <summary>Gets a value indicating whether finalized diagnostics are rendered to the console.</summary>
    /// <remarks>The language server disables rendering: its standard output carries only protocol frames.</remarks>
    internal bool RendersDiagnostics { get; }

    public Kimigayo(IConsoleService consoleService)
        : this(consoleService, new(), true)
    {
    }

    private Kimigayo(IConsoleService consoleService, KimiSettings settings, bool rendersDiagnostics)
    {
        this.consoleService = consoleService;
        this.Settings = settings;
        this.RendersDiagnostics = rendersDiagnostics;
    }

    /// <summary>Renders a finalized result in its order (SPEC 23.3.6.8); a silent service renders nothing.</summary>
    /// <param name="result">The finalized diagnostics.</param>
    /// <param name="baseDirectory">The directory display paths are relative to, or an empty string.</param>
    public void Render(DiagnosticResult result, string baseDirectory)
    {
        if (!this.RendersDiagnostics)
        {
            return;
        }

        foreach (var diagnostic in result.Diagnostics)
        {
            this.Render(diagnostic, result.Sources, baseDirectory);
        }
    }

    public ConsoleColor SeverityToColor(DiagnosticSeverity logLevel) => logLevel switch
    {
        DiagnosticSeverity.Error => this.Settings.Color.Error,
        DiagnosticSeverity.Warning => this.Settings.Color.Warning,
        DiagnosticSeverity.Information => this.Settings.Color.Information,
        _ => this.Settings.Color.Information,
    };

    public void WriteLine(DiagnosticSeverity severity, string? message = null)
        => this.consoleService.WriteLine(message, this.SeverityToColor(severity));

    public void WriteLine(DiagnosticSeverity severity, ReadOnlySpan<char> message)
        => this.consoleService.WriteLine(message, this.SeverityToColor(severity));

    /// <summary>Creates a service that writes nothing and renders no diagnostic.</summary>
    /// <returns>A silent compiler service.</returns>
    internal static Kimigayo CreateSilent()
        => new(new EmptyConsoleService(), new(), false);

    private static string DisplayPath(string path, string baseDirectory)
    {
        if (baseDirectory.Length == 0 || !Path.IsPathFullyQualified(path))
        {
            return path;
        }

        var relative = Path.GetRelativePath(baseDirectory, path);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathFullyQualified(relative) ? path : relative;
    }

    // Line breaks and tabs in an edit's text are shown as escapes, so one edit stays on one line.
    private static string Escape(string text)
        => text.AsSpan().IndexOfAny('\n', '\r', '\t') < 0 ? text : text.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal);

    // SPEC 23.4.7: message and code, the primary location, the underlined excerpt with its label, related locations, then
    // the Note and repair candidates.
    private void Render(CheckDiagnostic diagnostic, DiagnosticSource[] sources, string baseDirectory)
    {
        var source = diagnostic.Source < 0 ? null : sources[diagnostic.Source];
        this.consoleService.Write(diagnostic.Message);
        this.consoleService.Write(" : ");
        this.WriteLine(diagnostic.Severity, diagnostic.Code);
        if (source is not null)
        {
            var path = DisplayPath(source.Path, baseDirectory);
            this.consoleService.WriteLine(diagnostic.Display?.Range is { } range ? $" --> {path}:{range.Start.Line + 1}:{range.Start.Character + 1}" : $" --> {path}");
        }

        if (diagnostic.Display is { Excerpt.Length: > 0 } display)
        {
            var width = display.Excerpt[^1].Line.ToString().Length;
            var margin = new string(' ', width + 1);
            this.consoleService.WriteLine($"{margin}|");
            for (var i = 0; i < display.Excerpt.Length; i++)
            {
                var line = display.Excerpt[i];
                this.consoleService.WriteLine($"{line.Line.ToString().PadLeft(width)} | {line.Text}");
                this.consoleService.Write($"{margin}| ");
                this.consoleService.Write(new string(' ', line.Start));
                var carets = new string(Constants.CaretChar, line.Length);
                this.WriteLine(diagnostic.Severity, i == display.Excerpt.Length - 1 && diagnostic.Label is { } label ? $"{carets} {label}" : carets);
            }

            this.consoleService.WriteLine($"{margin}|");
        }

        if (diagnostic.Related is { Length: > 0 } related)
        {
            foreach (var item in related)
            {
                this.consoleService.WriteLine($" = {item.Describe(item.Source < 0 ? null : DisplayPath(sources[item.Source].Path, baseDirectory))}");
            }
        }

        foreach (var omission in diagnostic.Omissions ?? [])
        {
            this.consoleService.WriteLine($" = {omission}");
        }

        if (diagnostic.Note is not null || diagnostic.Repairs is { Length: > 0 })
        {
            this.consoleService.WriteLine();
            if (diagnostic.Note is not null)
            {
                this.consoleService.WriteLine($"Note: {diagnostic.Note}");
            }

            // SPEC 23.3.6.8, 23.3.6.9: each candidate as its title, one line per edit and its verified and required conditions.
            foreach (var repair in diagnostic.Repairs ?? [])
            {
                this.consoleService.WriteLine($"Repair: {repair.Title}");
                foreach (var edit in repair.Edits)
                {
                    var path = DisplayPath(sources[edit.Source].Path, baseDirectory);
                    var at = edit.Range is { } range ? $"{path}:{range.Start.Line + 1}:{range.Start.Character + 1}" : path;
                    var operation = edit.Replaced is null ? $"insert '{Escape(edit.Text)}'" : edit.Text.Length == 0 ? $"delete '{Escape(edit.Replaced)}'" : $"replace '{Escape(edit.Replaced)}' with '{Escape(edit.Text)}'";
                    this.consoleService.WriteLine($" = {at}: {operation}");
                }

                if (repair.Verified.Length > 0 || repair.Required.Length > 0)
                {
                    var verified = repair.Verified.Length == 0 ? null : "verified: " + string.Join(", ", repair.Verified);
                    var required = repair.Required.Length == 0 ? null : "requires: " + string.Join("; ", repair.Required.Select(static x => x.Phrase));
                    this.consoleService.WriteLine($" = {string.Join("; ", new[] { verified, required }.Where(static x => x is not null))}");
                }
            }
        }

        this.consoleService.WriteLine();
    }
}
