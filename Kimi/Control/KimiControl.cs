// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo;

using System.Collections.Concurrent;
using System.Text.Json;
using Kimigayo.Diagnostics;

public class KimiControl
{
    private const string GlobalName = "Global";
    private readonly IConsoleService consoleService;
    private readonly ConcurrentDictionary<string, FileDiagnostic> fileDiagnostics;

    public KimiSettings Settings { get; }

    public FileDiagnostic GlobalDiagnostic { get; }

    public KimiControl(IConsoleService consoleService)
    {
        this.consoleService = consoleService;
        this.Settings = new();

        this.fileDiagnostics = new();
        this.GlobalDiagnostic = new(GlobalName);
        this.fileDiagnostics.TryAdd(this.GlobalDiagnostic.Url, this.GlobalDiagnostic);
    }

    public FileDiagnostic GetOrAddFileDiagnostic(string url)
    {
        return this.fileDiagnostics.GetOrAdd(url, x => new(x));
    }

    public void DumpToConsole()
    {
        var array = this.fileDiagnostics.ToArray();
        foreach (var x in array)
        {
            this.WriteLine(LogLevel.Warning, x.Key);
            var st = JsonSerializer.Serialize(x.Value.GetArray());
            this.WriteLine(LogLevel.Information, st);

            /*foreach (var y in x.Value.GetArray())
            {
            }*/
        }
    }

    public ConsoleColor LogToColor(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Debug => this.Settings.Color.Debug,
        LogLevel.Information => this.Settings.Color.Information,
        LogLevel.Warning => this.Settings.Color.Warning,
        LogLevel.Error => this.Settings.Color.Error,
        LogLevel.Fatal => this.Settings.Color.Fatal,
        _ => this.Settings.Color.Information,
    };

    public Task<InputResult> ReadLine(CancellationToken cancellationToken = default)
        => this.consoleService.ReadLine(cancellationToken);

    public void WriteLine(LogLevel logLevel, string? message = null)
        => this.consoleService.WriteLine(message, this.LogToColor(logLevel));

    public void WriteLine(LogLevel logLevel, ReadOnlySpan<char> message)
        => this.consoleService.WriteLine(message, this.LogToColor(logLevel));

    public void WriteLine(LogLevel logLevel, ulong hash)
        => this.WriteLine(logLevel, HashedString.Get(hash));

    public void WriteLine(LogLevel logLevel, ulong hash, object obj1)
        => this.WriteLine(logLevel, HashedString.Get(hash, obj1));

    public void WriteLine(LogLevel logLevel, ulong hash, object obj1, object obj2)
        => this.WriteLine(logLevel, HashedString.Get(hash, obj1, obj2));
}
