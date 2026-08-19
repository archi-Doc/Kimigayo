// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi;

using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Kimi.Diagnostics;

public class Kimigayo
{
    internal const string GlobalName = "Global";
    internal const string ErrorPrefix = "KimiError";

    private readonly IConsoleService consoleService;
    private readonly ConcurrentDictionary<string, DiagnosticCollection> diagnosticCollections;

    public KimiSettings Settings { get; }

    public DiagnosticCollection GlobalDiagnosticCollection { get; }

    public Kimigayo(IConsoleService consoleService)
    {
        this.consoleService = consoleService;
        this.Settings = new();

        this.diagnosticCollections = new();
        this.GlobalDiagnosticCollection = this.GetOrAddDiagnosticCollection(GlobalName);
        // this.PointerSize = IntPtr.Size;
    }

    public void ReportDiagnostic(string url, Diagnostic diagnostic)
    {
        var entry = diagnostic.Entry;
        var fixOrNote = entry.Fix is not null || entry.Note is not null;

        // Message : Name
        this.consoleService.Write(diagnostic.Message);
        this.consoleService.Write(" : ");
        this.WriteLine(entry.Severity, entry.Name);

        if (diagnostic.SourceDocument is { } sourceDocument)
        {
            var start = sourceDocument.GetPosition(diagnostic.Span.Start);
            this.consoleService.WriteLine($" --> {url}:{start.Line + 1}:{start.Character + 1}");
            this.WriteSourceRange(diagnostic);
        }
        else
        {
            this.consoleService.WriteLine($" --> {url}:@{diagnostic.Span.Start}");
        }

        if (fixOrNote)
        {
            this.consoleService.WriteLine();
            if (entry.Fix is not null)
            {
                this.consoleService.WriteLine($"Fix: {entry.Fix}");
            }

            if (entry.Note is not null)
            {
                this.consoleService.WriteLine($"Note: {entry.Note}");
            }
        }

        this.consoleService.WriteLine();
    }

    public DiagnosticCollection GetOrAddDiagnosticCollection(string url)
    {
        return this.diagnosticCollections.GetOrAdd(url, x => new(this, x));
    }

    public void DumpToConsole()
    {
        var array = this.diagnosticCollections.ToArray();
        foreach (var x in array)
        {
            this.WriteLine(LogLevel.Warning, x.Key);

            foreach (var y in x.Value.GetArray())
            {
                this.WriteLine(y.Entry.Severity, JsonSerializer.Serialize(y));
            }
        }
    }

    public ConsoleColor LogLevelToColor(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Debug => this.Settings.Color.Debug,
        LogLevel.Information => this.Settings.Color.Information,
        LogLevel.Warning => this.Settings.Color.Warning,
        LogLevel.Error => this.Settings.Color.Error,
        LogLevel.Fatal => this.Settings.Color.Fatal,
        _ => this.Settings.Color.Information,
    };

    public ConsoleColor SeverityToColor(DiagnosticSeverity logLevel) => logLevel switch
    {
        DiagnosticSeverity.Error => this.Settings.Color.Error,
        DiagnosticSeverity.Warning => this.Settings.Color.Warning,
        DiagnosticSeverity.Information => this.Settings.Color.Information,
        _ => this.Settings.Color.Information,
    };

    public Task<InputResult> ReadLine(CancellationToken cancellationToken = default)
        => this.consoleService.ReadLine(cancellationToken);

    public void WriteLine(DiagnosticSeverity severity, string? message = null)
        => this.consoleService.WriteLine(message, this.SeverityToColor(severity));

    public void WriteLine(DiagnosticSeverity severity, ReadOnlySpan<char> message)
        => this.consoleService.WriteLine(message, this.SeverityToColor(severity));

    public void WriteLine(LogLevel logLevel, string? message = null)
        => this.consoleService.WriteLine(message, this.LogLevelToColor(logLevel));

    public void WriteLine(LogLevel logLevel, ReadOnlySpan<char> message)
        => this.consoleService.WriteLine(message, this.LogLevelToColor(logLevel));

    public void WriteLine(LogLevel logLevel, ulong hash)
        => this.WriteLine(logLevel, HashedString.Get(hash));

    public void WriteLine(LogLevel logLevel, ulong hash, object obj1)
        => this.WriteLine(logLevel, HashedString.Get(hash, obj1));

    public void WriteLine(LogLevel logLevel, ulong hash, object obj1, object obj2)
        => this.WriteLine(logLevel, HashedString.Get(hash, obj1, obj2));

    public void WriteLine(DiagnosticSeverity severity, ulong hash, object obj1, object obj2)
        => this.WriteLine(severity, HashedString.Get(hash, obj1, obj2));

    public void WriteLine(DiagnosticSeverity severity, ulong hash)
        => this.WriteLine(severity, HashedString.Get(hash));

    public void WriteLine(DiagnosticSeverity severity, ulong hash, object obj1)
        => this.WriteLine(severity, HashedString.Get(hash, obj1));

    private static int GetDisplayWidth(ReadOnlySpan<char> sourceText)
    {
        var width = 0;
        foreach (var c in sourceText)
        {
            width += c == '\t' ? Constants.IndentationSpaces - (width % Constants.IndentationSpaces) : 1;
        }

        return width;
    }

    private void WriteSourceRange(Diagnostic diagnostic)
    {
        var sourceDocument = diagnostic.SourceDocument!;
        var range = sourceDocument.GetSourceRange(diagnostic.Span);
        var startLine = range.Start.Line;

        if (startLine < 0 || startLine >= sourceDocument.LineCount)
        {
            return;
        }

        var endLine = Math.Clamp(range.End.Line, startLine, sourceDocument.LineCount - 1);
        if (endLine > startLine && range.End.Character == 0)
        {
            endLine--;
        }

        var lineNumberWidth = (endLine + 1).ToString().Length;
        var margin = new string(' ', lineNumberWidth + 1);
        this.consoleService.WriteLine($"{margin}|");

        for (var lineIndex = startLine; lineIndex <= endLine; lineIndex++)
        {
            var sourceLine = sourceDocument.GetLineSpan(lineIndex);
            var startCharacter = lineIndex == startLine ? Math.Clamp(range.Start.Character, 0, sourceLine.Length) : 0;
            var endCharacter = lineIndex == range.End.Line ? Math.Clamp(range.End.Character, 0, sourceLine.Length) : sourceLine.Length;
            var caretStart = GetDisplayWidth(sourceLine[..startCharacter]);
            var caretEnd = GetDisplayWidth(sourceLine[..endCharacter]);
            var caretLength = Math.Max(1, caretEnd - caretStart);

            this.consoleService.Write($"{(lineIndex + 1).ToString().PadLeft(lineNumberWidth)} | ");
            this.WriteSourceLine(sourceLine);

            this.consoleService.Write($"{margin}| ");
            this.consoleService.Write(new string(' ', caretStart));
            this.WriteLine(diagnostic.Entry.Severity, new string(Constants.CaretChar, caretLength));
        }

        this.consoleService.WriteLine($"{margin}|");
    }

    private void WriteSourceLine(ReadOnlySpan<char> sourceLine)
    {
        if (sourceLine.IndexOf('\t') < 0)
        {
            this.consoleService.WriteLine(sourceLine);
            return;
        }

        var builder = new StringBuilder(sourceLine.Length);
        var column = 0;
        foreach (var c in sourceLine)
        {
            if (c == '\t')
            {
                var spaces = Constants.IndentationSpaces - (column % Constants.IndentationSpaces);
                builder.Append(' ', spaces);
                column += spaces;
            }
            else
            {
                builder.Append(c);
                column++;
            }
        }

        this.consoleService.WriteLine(builder.ToString());
    }
}
