// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>The one form of a test that needs the state of a fresh process (a type initializer not yet run, the pools of a High memory
/// pressure): an Explicit test of this assembly, run alone in a child process of the test apphost. Never a collectible load context:
/// its collection flushes process-wide runtime caches and charges the rebuild to another test's measuring thread
/// (<c>ArchitectureRulesTest</c> forbids it).</summary>
internal static class FreshProcess
{
    /// <summary>Runs an Explicit test in a new process of the test apphost, which inherits the environment, and asserts that the process
    /// ran that test as its only case and that it passed.</summary>
    /// <param name="test">The class declaring the Explicit test.</param>
    /// <param name="method">The name of the Explicit test method.</param>
    /// <param name="environment">The variables the process gets in addition to the inherited ones.</param>
    /// <returns>A task that completes when the process has exited.</returns>
    internal static async Task RunExplicit(Type test, string method, params (string Name, string Value)[] environment)
    {
        var start = new ProcessStartInfo(Path.ChangeExtension(test.Assembly.Location, ".exe"), ["-method", $"{test.FullName}.{method}", "-explicit", "only"])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        using var probe = Process.Start(start)!;
        var report = probe.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        if (!probe.WaitForExit(TimeSpan.FromMinutes(5)))
        {
            probe.Kill(entireProcessTree: true);
            probe.WaitForExit();
        }

        var text = await report;
        Assert.True(probe.ExitCode == 0 && text.Contains("Total: 1, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0", StringComparison.Ordinal), text);
    }
}
