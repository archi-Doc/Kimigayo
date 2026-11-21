// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1210 // Using directives should be ordered alphabetically by namespace

global using Arc;
global using Arc.Threading;
global using Arc.Unit;
global using Kimi;
global using Microsoft.Extensions.DependencyInjection;
global using Tinyhand;
global using ValueLink;
using Kimi.Command;
using Kimi.Unit;
using SimpleCommandLine;

namespace Kimi;

public class Program
{
    public static bool SuppressConsoleOutput { get; private set; }

    /// <summary>Gets or sets what a Ctrl+C does: the process's one action, by default the root's termination. A session of
    /// <c>kimi tatara</c> replaces it while it runs and restores it at its end (Tatara design A3 §2.4).</summary>
    internal static Action CancelKey { get; set; } = static () => root?.RequestTermination();

    private static ExecutionRoot? root;

    public static async Task Main()
    {
        AppCloseHandler.Register(() =>
        {// Closing the console window or terminating the process.
            root?.RequestTermination(); // Send a termination signal to the root.
            root?.WaitForTerminationAsync(TimeSpan.FromSeconds(2)).Wait();
        });

        Console.CancelKeyPress += (s, e) =>
        {// Ctrl+C pressed.
            e.Cancel = true;
            CancelKey();
        };

        var args = SimpleParserHelper.GetCommandLineArguments();
        SuppressConsoleOutput = args.StartsWith($"{LspCommand.Name}", StringComparison.OrdinalIgnoreCase);
        var builder = new CommandUnit.Builder();
        var unit = builder.Build();
        root = unit.Context.ExecutionRoot;

        await unit.RunAsync(new(args));

        root.RequestTermination();
        await root.WaitForTerminationAsync(); // Wait for the termination infinitely.
        if (unit.Context.ServiceProvider.GetService<LogUnit>() is { } unitLogger)
        {
            await unitLogger.FlushAndTerminateAsync();
        }
    }
}
