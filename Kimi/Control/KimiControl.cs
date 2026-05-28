// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo;

using System.Collections.Concurrent;
using System.Text.Json;
using Kimigayo.Diagnostics;

public class KimiControl
{
    internal const string GlobalName = "Global";
    private readonly IConsoleService consoleService;
    private readonly ConcurrentDictionary<string, FileDiagnostic> fileDiagnostics;

    public KimiSettings Settings { get; }

    public FileDiagnostic GlobalDiagnostic { get; }

    public KimiControl(IConsoleService consoleService)
    {
        this.consoleService = consoleService;
        this.Settings = new();

        this.fileDiagnostics = new();
        this.GlobalDiagnostic = new(this, GlobalName);
        this.fileDiagnostics.TryAdd(this.GlobalDiagnostic.Url, this.GlobalDiagnostic);
    }

    public void ReportDiagnostic(string url, Diagnostic diagnostic)
    {
        this.WriteLine(diagnostic.Severity, diagnostic.ToString(url));
    }

    public FileDiagnostic GetOrAddFileDiagnostic(string url)
    {
        return this.fileDiagnostics.GetOrAdd(url, x => new(this, x));
    }

    public void DumpToConsole()
    {
        var array = this.fileDiagnostics.ToArray();
        foreach (var x in array)
        {
            this.WriteLine(LogLevel.Warning, x.Key);

            foreach (var y in x.Value.GetArray())
            {
                this.WriteLine(y.Severity, JsonSerializer.Serialize(y));
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
}
