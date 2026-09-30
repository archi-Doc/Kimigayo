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

    // SPEC 23.4.7: message and code, the primary location, the underlined excerpt with its label, related locations, then
    // Note and Advice.
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

        if (diagnostic.Advice is not null || diagnostic.Note is not null)
        {
            this.consoleService.WriteLine();
            if (diagnostic.Note is not null)
            {
                this.consoleService.WriteLine($"Note: {diagnostic.Note}");
            }

            if (diagnostic.Advice is not null)
            {
                this.consoleService.WriteLine($"Advice: {diagnostic.Advice}");
            }
        }

        this.consoleService.WriteLine();
    }
}
