// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>The one form of a test that needs the state of a fresh process (a type initializer not yet run, the pools under High memory
/// pressure): an Explicit test of this assembly, run alone in a child process of the test apphost. Never a collectible load context:
/// its collection flushes process-wide runtime caches and charges the rebuild to another test's measuring thread
/// (<c>ArchitectureRulesTest</c> forbids it). The child runs that one test with the parent's pipeline selection and takes no part in the
/// run's evidence collection (<c>KIMI_MIR_DIFF</c>, snapshots, fixture directories), which belongs to the parent run: its assembly
/// fixtures would run <c>Compilation</c>'s type initializer before the test and write into the parent's evidence.</summary>
internal static class FreshProcess
{
    // The variables of the KIMI_ namespace a child keeps, a whitelist: the pipeline selection (src/Kimi's Compilation reads it, and the
    // children's tests compare their default with it). Every other KIMI_ variable selects evidence or a location of the run.
    private static readonly string[] SelectionVariables = ["KIMI_PIPELINE"];

    /// <summary>Gets the environment of a child process.</summary>
    /// <param name="parent">The parent's environment.</param>
    /// <returns>Every variable of the parent that is not in the KIMI_ namespace, and its pipeline selection. Names keep the parent's
    /// identity: distinct by case outside Windows (<c>HTTPS_PROXY</c> beside <c>https_proxy</c>), so they are compared ordinally.</returns>
    internal static Dictionary<string, string?> ChildEnvironment(IDictionary<string, string?> parent)
        => parent.Where(static x => !x.Key.StartsWith("KIMI_", StringComparison.OrdinalIgnoreCase)
                || SelectionVariables.Contains(x.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(static x => x.Key, static x => x.Value, StringComparer.Ordinal);

    /// <summary>Runs an Explicit test in a new process of the test apphost and asserts that the process ran that test as its only case
    /// and that it passed.</summary>
    /// <param name="test">The class declaring the Explicit test.</param>
    /// <param name="method">The name of the Explicit test method.</param>
    /// <param name="environment">The variables the process gets in addition to <see cref="ChildEnvironment"/>.</param>
    /// <returns>A task that completes when the process has exited.</returns>
    internal static async Task RunExplicit(Type test, string method, params (string Name, string Value)[] environment)
    {
        var apphost = Path.ChangeExtension(test.Assembly.Location, OperatingSystem.IsWindows() ? ".exe" : null);
        var start = new ProcessStartInfo(apphost, ["-method", $"{test.FullName}.{method}", "-explicit", "only"])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        var child = ChildEnvironment(start.Environment);
        start.Environment.Clear();
        foreach (var (name, value) in child)
        {
            start.Environment[name] = value;
        }

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
