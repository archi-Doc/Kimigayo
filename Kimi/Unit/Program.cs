// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc;
using Arc.Threading;
using Arc.Unit;
using Kimigayo.Lsp;
using Microsoft.Extensions.DependencyInjection;
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
        var builder = new ConsoleUnit.Builder();
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
