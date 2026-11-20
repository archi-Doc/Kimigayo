// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1208, SA1210 // Using directives should be ordered by namespace (the global ones come first)

global using Arc;
global using Arc.Threading;
global using Arc.Unit;
global using Kimi;
global using Microsoft.Extensions.DependencyInjection;
global using Tinyhand;
global using ValueLink;
using System.Runtime.InteropServices;
using Kimi.Command;
using Kimi.Unit;
using SimpleCommandLine;

namespace Kimi;

public partial class Program
{
    private static ExecutionRoot? root;

    // Whether the default action has run once (Tatara design A4 §2.2): set by its first call alone, never by Main's or AppCloseHandler's
    // own termination requests, and never reset.
    private static int pressed;

    public static bool SuppressConsoleOutput { get; private set; }

    /// <summary>Gets or sets what a Ctrl+C does: the process's one action. The default's first call requests the root's termination
    /// (the command's cancellation: 130); a later call ends the process at once with 130 (<see cref="EndProcess"/>; the owner's O-460
    /// (a)). A session of <c>kimi tatara</c> replaces it while it runs and restores it at its end (Tatara design A3 §2.4);
    /// <c>check --Format json</c> holds it empty while it publishes (SPEC 23.2.3).</summary>
    internal static Action CancelKey { get; set; } = static () =>
    {
        if (Interlocked.Exchange(ref pressed, 1) != 0)
        {
            EndProcess();
        }

        root?.RequestTermination();
    };

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

    /// <summary>Ends this process at once with 130: TerminateProcess, not Environment.Exit, which would run tatara.dll's detach, and that
    /// can wait on a lock a stuck thread holds. Off Windows, where no tatara.dll is loaded, Environment.Exit.</summary>
    internal static void EndProcess()
    {
        if (!OperatingSystem.IsWindows())
        {
            Environment.Exit(130);
        }

        TerminateProcess(GetCurrentProcess(), 130);
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(nint process, uint exitCode);
}
