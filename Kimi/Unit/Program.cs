// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1210 // Using directives should be ordered alphabetically by namespace

global using Arc;
global using Arc.Threading;
global using Arc.Unit;
global using Kimigayo;
global using Microsoft.Extensions.DependencyInjection;
global using Tinyhand;
global using ValueLink;
using Kimigayo.Lsp;
using SimpleCommandLine;

namespace Kimigayo;

public class Program
{
    public static bool SuppressConsoleOutput { get; private set; }

    private static ExecutionRoot? root;

    public static async Task Main()
    {
        AppCloseHandler.Set(() =>
        {// Closing the console window or terminating the process.
            root?.RequestTermination(); // Send a termination signal to the root.
            root?.WaitForTermination(TimeSpan.FromSeconds(2)).Wait();
        });

        Console.CancelKeyPress += (s, e) =>
        {// Ctrl+C pressed.
            e.Cancel = true;
            root?.RequestTermination(); // Send a termination signal to the root.
        };

        var args = SimpleParserHelper.GetCommandLineArguments();
        SuppressConsoleOutput = args.StartsWith($"{LspCommand.Name}", StringComparison.OrdinalIgnoreCase);
        var builder = new CommandUnit.Builder();
        var unit = builder.Build();
        root = unit.Context.Root;

        await unit.RunAsync(new(args));

        root.RequestTermination();
        await root.WaitForTermination(); // Wait for the termination infinitely.
        if (unit.Context.ServiceProvider.GetService<LogUnit>() is { } unitLogger)
        {
            await unitLogger.FlushAndTerminate();
        }
    }
}
