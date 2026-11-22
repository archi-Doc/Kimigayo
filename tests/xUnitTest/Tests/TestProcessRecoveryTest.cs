// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kimi;
using Kimi.Build;
using Kimi.Testing;
using Microsoft.Win32.SafeHandles;
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
    [InlineData("frame", 0, "runnerError", 3)] // A malformed result frame ends the running case before its deadline (PLAN G1243).
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
            if (mode is not ("sleep" or "frame"))
            {
                var temporary = File.ReadAllText(outcome.Stdout!).Trim();
                Assert.StartsWith(Path.GetTempPath(), temporary, StringComparison.OrdinalIgnoreCase);
                Assert.False(Directory.Exists(temporary));
            }

            if (mode == "descendant")
            {
                // The job ended the descendant: its id names it no more (reaped, or reused), or it has exited.
                Assert.True(TryIdentify(File.ReadAllText(outcome.Stderr!).Trim(), out var child));
                using (child)
                {
                    Assert.True(child is null || child.HasExited);
                }
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    // A process line of the probe (its id and start time) as the process it names while that process runs on, else null: an id is
    // reused once its process has ended, and the start time tells the two apart. False for a line that is no process line.
    internal static bool TryIdentify(string line, out Process? process)
    {
        process = null;
        if (line.Split(' ') is not [var id, var started] || !int.TryParse(id, System.Globalization.CultureInfo.InvariantCulture, out var pid) ||
            !long.TryParse(started, System.Globalization.CultureInfo.InvariantCulture, out var ticks))
        {
            return false;
        }

        try
        {
            process = Process.GetProcessById(pid);
            _ = process.SafeHandle; // Held from here on, so its id cannot name another process.
            if (process.StartTime.Ticks == ticks)
            {
                return true;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // It has ended, or the id names another user's process.
        }

        process?.Dispose();
        process = null;
        return true;
    }

    // The ordinary test apphost doubles as an isolated process fixture. No shell or
    // external build is needed, and child behavior is selected only by this test and NativeToolchainTest's run rows ("escape", "run").
    [ModuleInitializer]
    internal static void ProcessProbe()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var mode = Environment.GetEnvironmentVariable("KIMI_TEST_PROCESS_PROBE");
        if (mode == "run")
        {
            RunProgram();
        }
        else if (mode is not ("exit" or "sleep" or "cancel" or "descendant" or "escape" or "frame"))
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
            using var descendant = StartDescendant("sleep");
        }
        else if (mode == "escape")
        {
            // A descendant whose parent exits first: a kill of this process's tree no longer finds it (Tatara SP1's limit, C-427 N7).
            using (var self = Process.GetCurrentProcess())
            {
                Console.Error.WriteLine(Identity(self));
            }

            using var intermediate = StartDescendant("descendant");
            intermediate.WaitForExit();
        }
        else if (mode == "frame")
        {
            // A frame whose length is below its header's on the result channel (TestProcess): the runner's failure, then no exit.
            var handle = (nint)ulong.Parse(Environment.GetEnvironmentVariable("KIMI_TEST_PIPE")!, System.Globalization.CultureInfo.InvariantCulture);
            using var channel = new FileStream(new SafeFileHandle(handle, ownsHandle: false), FileAccess.Write);
            channel.Write([1, 0, 0, 0]);
        }

        if (mode is not ("sleep" or "frame"))
        {
            if (Console.In.Read() != -1)
            {
                Environment.Exit(123); // Not a kill's 125 (JobProcess.Kill).
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

    // A process's line for TryIdentify: its id and start time.
    private static string Identity(Process process) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{process.Id} {process.StartTime.Ticks}");

    // A descendant in the mode, ready, its line (Identity) on the error output. It holds this process's standard output and error: the
    // redirected input makes Process.Start pass them (with the input, closed at once).
    private static Process StartDescendant(string mode)
    {
        var readyName = "kimi-recovery-child-" + Guid.NewGuid().ToString("N");
        using var ready = new EventWaitHandle(initialState: false, EventResetMode.ManualReset, readyName);
        var start = new ProcessStartInfo(Path.ChangeExtension(typeof(TestProcessRecoveryTest).Assembly.Location, ".exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
        };
        start.Environment["KIMI_TEST_PROCESS_PROBE"] = mode;
        start.Environment["KIMI_TEST_PROCESS_READY"] = readyName;
        var child = Process.Start(start)!;
        child.StandardInput.Close();
        if (!ready.WaitOne(TimeSpan.FromSeconds(30)))
        {
            Environment.Exit(124);
        }

        Console.Error.WriteLine(Identity(child));
        return child;
    }

    // Kimi's side of NativeToolchainTest's run rows: runs this apphost as kimi run runs a program, in KIMI_TEST_PROCESS_PROGRAM's mode;
    // KIMI_TEST_PROCESS_ACTION "no input" and "invalid input" set this process's standard input to no handle or to a value no handle of
    // it has (above the handle table's limit) before the run; when the row has read the program's output (KIMI_TEST_PROCESS_GO), "cancel"
    // cancels the run and "end" ends this process as a second Ctrl+C does (Program.EndProcess). Exits with the run's exit code, 130 for a
    // cancellation.
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void RunProgram()
    {
        Environment.SetEnvironmentVariable("KIMI_TEST_PROCESS_PROBE", Environment.GetEnvironmentVariable("KIMI_TEST_PROCESS_PROGRAM"));
        var action = Environment.GetEnvironmentVariable("KIMI_TEST_PROCESS_ACTION");
        if (action is "no input" or "invalid input")
        {
            if (!SetStdHandle(-10, action == "no input" ? 0 : 0x7FFF_FFF8))
            {
                Environment.Exit(122);
            }
        }

        using var cancellation = new CancellationTokenSource();
        var run = NativeToolchain.RunExecutable(Environment.ProcessPath!, Environment.CurrentDirectory, cancellation.Token);
        if (action is "cancel" or "end")
        {
            using (var go = EventWaitHandle.OpenExisting(Environment.GetEnvironmentVariable("KIMI_TEST_PROCESS_GO")!))
            {
                go.WaitOne();
            }

            if (action == "end")
            {
                Program.EndProcess();
            }

            cancellation.Cancel();
        }

        try
        {
            Environment.Exit(run.GetAwaiter().GetResult());
        }
        catch (OperationCanceledException)
        {
            Environment.Exit(130);
        }
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetStdHandle(int kind, nint handle);
}
