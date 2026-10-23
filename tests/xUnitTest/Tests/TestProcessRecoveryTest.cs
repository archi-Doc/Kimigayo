// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Kimi.Testing;
using Xunit;

namespace XunitTest;

public class TestProcessRecoveryTest
{
    [Theory]
    [InlineData("exit", 0, "normal", 3)]
    [InlineData("exit", 3000, "normal", 3)]
    [InlineData("sleep", 0, "timeout", 1)]
    [InlineData("cancel", 3000, "cancelled", 130)]
    [InlineData("descendant", 0, "normal", 3)]
    public async Task RecoversManagedProcessesAndRejectsMissingCompletion(string mode, int delayMilliseconds, string termination, int exitCode)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows job management test.");
        var root = Path.Combine(Path.GetTempPath(), "kimi-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var compilation = TestExecutionAnalysisTest.Analyze("#Test\nfunc sample() => ()");
            compilation.Tests.Discover(compilation);
            var outcome = new TestOutcome(compilation.Tests.Cases[0], compilation.Tests);
            await RunProbe(mode, delayMilliseconds, root, outcome);
            Assert.True(outcome.Started);
            Assert.False(outcome.Completed);
            Assert.Equal(termination, outcome.Termination);
            Assert.Equal(exitCode, outcome.ExitCode);
            Assert.DoesNotContain(outcome.Errors, x => x.Contains("recovery", StringComparison.OrdinalIgnoreCase));
            if (mode != "sleep")
            {
                var temporary = File.ReadAllText(outcome.Stdout!).Trim();
                Assert.StartsWith(Path.GetTempPath(), temporary, StringComparison.OrdinalIgnoreCase);
                Assert.False(Directory.Exists(temporary));
            }

            if (mode == "descendant")
            {
                var pid = int.Parse(File.ReadAllText(outcome.Stderr!).Trim(), System.Globalization.CultureInfo.InvariantCulture);
                try
                {
                    using var child = Process.GetProcessById(pid);
                    Assert.True(child.HasExited);
                }
                catch (ArgumentException)
                {
                    // The job reaped the descendant before the query.
                }
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    // The ordinary test apphost doubles as an isolated process fixture. No shell or
    // external build is needed, and child behavior is selected only by this test.
    [ModuleInitializer]
    internal static void ProcessProbe()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var mode = Environment.GetEnvironmentVariable("KIMI_TEST_PROCESS_PROBE");
        if (mode is not ("exit" or "sleep" or "cancel" or "descendant"))
        {
            return;
        }

        if (Environment.GetEnvironmentVariable("KIMI_TEST_PROCESS_DELAY") is { } delay)
        {
            // Deliberately longer than the old normal-exit deadline and cancellation delay.
            Thread.Sleep(int.Parse(delay, System.Globalization.CultureInfo.InvariantCulture));
        }

        if (mode == "descendant")
        {
            StartDescendant();
        }

        if (mode != "sleep")
        {
            if (Console.In.Read() != -1)
            {
                Environment.Exit(125);
            }

            Console.WriteLine(Environment.GetEnvironmentVariable("TEMP"));
        }

        using (var ready = EventWaitHandle.OpenExisting(Environment.GetEnvironmentVariable("KIMI_TEST_PROCESS_READY")!))
        {
            ready.Set();
        }

        if (mode is "exit" or "descendant")
        {
            Environment.Exit(0);
        }

        Thread.Sleep(Timeout.Infinite);
    }

    private static async Task RunProbe(string mode, int delayMilliseconds, string root, TestOutcome outcome)
    {
        var environment = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry item in Environment.GetEnvironmentVariables())
        {
            environment[(string)item.Key] = (string)item.Value!;
        }

        var readyName = "kimi-recovery-ready-" + Guid.NewGuid().ToString("N");
        using var ready = new EventWaitHandle(initialState: false, EventResetMode.ManualReset, readyName);
        environment["KIMI_TEST_PROCESS_PROBE"] = mode;
        environment["KIMI_TEST_PROCESS_DELAY"] = delayMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        environment["KIMI_TEST_PROCESS_READY"] = readyName;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var executable = Path.ChangeExtension(typeof(TestProcessRecoveryTest).Assembly.Location, ".exe");
        var settings = new TestSettings { Timeout = mode == "sleep" ? "2s" : "30s", RecoveryGrace = "30s" };
        var worker = TestWorker.Run(
            executable, root, root, 0, settings, environment, outcome, new(TestOptions.Parse([])), cancellation.Token);
        try
        {
            if (mode == "cancel")
            {
                var signaled = await Task.Run(() => ready.WaitOne(TimeSpan.FromSeconds(30)), TestContext.Current.CancellationToken);
                Assert.True(signaled, "The child did not become ready.");
                cancellation.Cancel();
            }

            await worker;
        }
        finally
        {
            cancellation.Cancel();
            await worker;
        }
    }

    private static void StartDescendant()
    {
        var readyName = "kimi-recovery-child-" + Guid.NewGuid().ToString("N");
        using var ready = new EventWaitHandle(initialState: false, EventResetMode.ManualReset, readyName);
        var start = new ProcessStartInfo(Path.ChangeExtension(typeof(TestProcessRecoveryTest).Assembly.Location, ".exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.Environment["KIMI_TEST_PROCESS_PROBE"] = "sleep";
        start.Environment["KIMI_TEST_PROCESS_READY"] = readyName;
        using var child = Process.Start(start)!;
        if (!ready.WaitOne(TimeSpan.FromSeconds(30)))
        {
            Environment.Exit(124);
        }

        Console.Error.WriteLine(child.Id);
    }
}
