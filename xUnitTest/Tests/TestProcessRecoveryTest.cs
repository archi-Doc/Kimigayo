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
    [InlineData("exit", "normal", 3)]
    [InlineData("sleep", "timeout", 1)]
    [InlineData("cancel", "cancelled", 130)]
    [InlineData("descendant", "normal", 3)]
    public async Task RecoversManagedProcessesAndRejectsMissingCompletion(string mode, string termination, int exitCode)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows job management test.");
        var root = Path.Combine(Path.GetTempPath(), "kimi-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var compilation = TestExecutionAnalysisTest.Analyze("#Test\nfunc sample() => ()");
            compilation.Tests.Discover(compilation);
            var outcome = new TestOutcome(compilation.Tests.Cases[0], compilation.Tests);
            var environment = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry item in Environment.GetEnvironmentVariables())
            {
                environment[(string)item.Key] = (string)item.Value!;
            }

            environment["KIMI_TEST_PROCESS_PROBE"] = mode;
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            if (mode == "cancel")
            {
                cancellation.CancelAfter(200);
            }

            var executable = Path.ChangeExtension(typeof(TestProcessRecoveryTest).Assembly.Location, ".exe");
            await TestWorker.Run(executable, root, root, 0, new() { Timeout = "2s", RecoveryGrace = "3s" }, environment, outcome, new(TestOptions.Parse([])), cancellation.Token);
            Assert.True(outcome.Started);
            Assert.False(outcome.Completed);
            Assert.Equal(termination, outcome.Termination);
            Assert.Equal(exitCode, outcome.ExitCode);
            Assert.DoesNotContain(outcome.Errors, x => x.Contains("recovery", StringComparison.OrdinalIgnoreCase));
            if (mode is "exit" or "descendant")
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
        var mode = Environment.GetEnvironmentVariable("KIMI_TEST_PROCESS_PROBE");
        if (mode is not ("exit" or "sleep" or "cancel" or "descendant"))
        {
            return;
        }

        if (mode == "descendant")
        {
            var start = new ProcessStartInfo(Path.ChangeExtension(typeof(TestProcessRecoveryTest).Assembly.Location, ".exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.Environment["KIMI_TEST_PROCESS_PROBE"] = "sleep";
            using var child = Process.Start(start)!;
            Console.Error.WriteLine(child.Id);
        }

        if (mode is "exit" or "descendant")
        {
            if (Console.In.Read() != -1)
            {
                Environment.Exit(125);
            }

            Console.WriteLine(Environment.GetEnvironmentVariable("TEMP"));
        }

        if (mode is "exit" or "descendant")
        {
            Environment.Exit(0);
        }

        Thread.Sleep(Timeout.Infinite);
    }
}
