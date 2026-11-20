// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Kimi;
using Kimi.Command;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace XunitTest;

public sealed class CommandExecutionTest : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "kimi-command-" + Guid.NewGuid().ToString("N"));

    public CommandExecutionTest() => Directory.CreateDirectory(this.directory);

    public void Dispose() => Directory.Delete(this.directory, true);

    // The command layer (PLAN G984): a command's own Input error (SPEC 23.2.3: a failure to select its inputs; IMPL 20.3: a usage error)
    // and a failure of a toolchain call (SPEC 23.2.2: a toolchain, output or launch failure) fail the command with their message and no
    // code, exit 1; an exception no result owns ends the command's own result as Faulted: one CheckFaulted_Kd (Exception), exit 1
    // (SPEC 23.2.3: an internal selection failure is Faulted; DIAGNOSTICS rule 9); cancellation returns 130. Rows, the expected record
    // first:
    // - The message, no code: a missing input (build, check, restore), a directory without projects, the usage errors (build, toolchain,
    //   check's format, restore's input, an unknown command), run's selection, the toolchain's calls (a missing manifest, run --no-build
    //   without a build, a missing executable, a missing toolchain root), restore's lock write (an output failure), a malformed solution
    //   file and two files nested deeper than the configuration reader reads (the parse of a user's file is the file's failure).
    // - CheckFaulted_Kd: the host's console throwing at the selected project's line (build with a type the toolchain's classification
    //   lists, which printed as a plain failure, and with one it does not, which escaped the command; check and emit), at the line after
    //   the projects (printed as a plain failure), at restore's lock line, and at the line that renders a toolchain failure (escaped).
    // No production seam: the console is the host's.
    [Theory]
    [InlineData("missing input", null, false, "Input not found")]
    [InlineData("no project", null, false, "No solution or project files found")]
    [InlineData("build usage", null, false, "Use kimi build --Manifest")]
    [InlineData("toolchain usage", null, false, "Use kimi toolchain verify")]
    [InlineData("run selection", null, false, "Run requires exactly one")]
    [InlineData("manifest", null, false, "missing.link.json")]
    [InlineData("no build", null, false, "No built Application is available.")]
    [InlineData("restore output", null, false, "Access to the path is denied.")]
    [InlineData("malformed solution", null, false, "Cannot load solution")]
    [InlineData("deep solution", null, false, "Cannot load solution")]
    [InlineData("deep project", null, false, "indentation depth")]
    [InlineData("check missing", null, false, "Input not found")]
    [InlineData("check format", null, false, "Unknown --Format 'yaml'")]
    [InlineData("unknown command", null, false, "Unknown command 'bulid'")]
    [InlineData("restore without input", null, false, "Restore requires one .kimiproj input.")]
    [InlineData("restore source", null, false, "Restore requires a .kimiproj input.")]
    [InlineData("restore missing", null, false, "Input not found")]
    [InlineData("missing executable", null, false, "Executable not found")]
    [InlineData("missing toolchain", null, false, "Toolchain file not found")]
    [InlineData("selection", "Project: ", true, null)]
    [InlineData("selection, unlisted", "Project: ", true, null)]
    [InlineData("after the projects", "No projects loaded", true, null)]
    [InlineData("toolchain's message", "Could not find file", true, null)]
    [InlineData("check selection", "Project: ", true, null)]
    [InlineData("emit selection", "Project: ", true, null)]
    [InlineData("restore lock line", "Dependency lock", true, null)]
    public async Task ACommandFailsByItsInputItsToolchainOrItsOwnFault(string origin, string? failingLine, bool faulted, string? message)
    {
        var console = new DiagnosticContractTest.DiagnosticConsole
        {
            FailingLine = failingLine,
            Failure = origin == "selection, unlisted" ? new FormatException("The host's console failed.") : null,
        };
        var kimigayo = new Kimigayo(console);
        var project = this.Write("Hello");
        var exit = await this.Execute(origin, kimigayo, project);

        Assert.Equal(1, exit);
        Assert.Equal(faulted ? 1 : 0, console.Text.Split(nameof(DiagnosticCode.CheckFaulted_Kd)).Length - 1);
        if (faulted)
        {
            Assert.Contains(DiagnosticFaults.Describe(DiagnosticFault.Exception), console.Text, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains(message!, console.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ACancelledCommandReturns130()
    {
        var console = new DiagnosticContractTest.DiagnosticConsole();
        var kimigayo = new Kimigayo(console);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await new BuildCommand(null!, kimigayo, new Solution(kimigayo)).Execute(new(), [this.Write("Hello")], cancelled.Token);
        var exit = Environment.ExitCode;
        Environment.ExitCode = 0;
        Assert.Equal(130, exit);
        Assert.Contains("Command cancelled.", console.Text, StringComparison.Ordinal);
    }

    // Kimigayo.RunEnded (Tatara unit SP1) runs once per run attempt, also when no program ran: here the build fails.
    [Fact]
    public async Task ARunWhoseBuildFailsCallsRunEndedOnce()
    {
        var kimigayo = new Kimigayo(new DiagnosticContractTest.DiagnosticConsole());
        var calls = 0;
        kimigayo.RunEnded = () => calls++;
        var project = this.Write("Hello");
        File.WriteAllText(Path.ChangeExtension(project, ".kimi"), "func f( => ()");
        Environment.ExitCode = 0;
        await new RunCommand(null!, kimigayo, new Solution(kimigayo)).Execute(new(), [project], TestContext.Current.CancellationToken);
        var exit = Environment.ExitCode;
        Environment.ExitCode = 0;
        Assert.Equal(1, exit);
        Assert.Equal(1, calls);
    }

    private async Task<int> Execute(string origin, Kimigayo kimigayo, string project)
    {
        var token = TestContext.Current.CancellationToken;
        var solution = new Solution(kimigayo);
        var build = new BuildCommand(null!, kimigayo, solution);
        Environment.ExitCode = 0;
        switch (origin)
        {
            case "missing input":
                await build.Execute(new(), [Path.Combine(this.directory, "missing.kimi")], token);
                break;
            case "no project":
                await build.Execute(new(), [Directory.CreateDirectory(Path.Combine(this.directory, "empty")).FullName], token);
                break;
            case "build usage":
                await build.Execute(new() { Manifest = "app.link.json" }, [project], token);
                break;
            case "toolchain usage":
                await new ToolchainCommand(kimigayo).Execute(new(), ["check"], token);
                break;
            case "run selection":
                this.Write("Other");
                await new RunCommand(null!, kimigayo, solution).Execute(new() { NoBuild = true }, [this.directory], token);
                break;
            case "manifest":
            case "toolchain's message":
                await build.Execute(new() { Manifest = Path.Combine(this.directory, "missing.link.json") }, [], token);
                break;
            case "no build":
                await new RunCommand(null!, kimigayo, solution).Execute(new() { NoBuild = true }, [project], token);
                break;
            case "restore output":
                Directory.CreateDirectory(Path.ChangeExtension(project, "kimi.lock.json"));
                await new RestoreCommand(kimigayo).Execute(new(), [project], token);
                break;
            case "malformed solution":
                var solutionFile = Path.Combine(this.directory, "App.kimisln");
                File.WriteAllText(solutionFile, "Projects = true");
                await build.Execute(new(), [solutionFile], token);
                break;
            case "deep solution":
                var deep = Path.Combine(this.directory, "Deep.kimisln");
                File.WriteAllText(deep, "Projects = " + new string('{', 200) + new string('}', 200));
                await build.Execute(new(), [deep], token);
                break;
            case "deep project":
                var deepProject = Path.Combine(this.directory, "Deep.kimiproj");
                File.WriteAllText(deepProject, "Targets = " + new string('{', 200) + new string('}', 200));
                await new RestoreCommand(kimigayo).Execute(new(), [deepProject], token);
                break;
            case "check missing":
                await new CheckCommand(null!, kimigayo, solution).Execute(new(), [Path.Combine(this.directory, "missing.kimi")], token);
                break;
            case "check format":
                await new CheckCommand(null!, kimigayo, solution).Execute(new() { Format = "yaml" }, [project], token);
                break;
            case "unknown command":
                await new DefaultCommand(kimigayo).Execute(["bulid"], token);
                break;
            case "restore without input":
                await new RestoreCommand(kimigayo).Execute(new(), [], token);
                break;
            case "restore source":
                await new RestoreCommand(kimigayo).Execute(new(), [Path.ChangeExtension(project, ".kimi")], token);
                break;
            case "restore missing":
                await new RestoreCommand(kimigayo).Execute(new(), [Path.Combine(this.directory, "missing.kimiproj")], token);
                break;
            case "missing executable":
                await new RunCommand(null!, kimigayo, solution).Execute(new(), [Path.Combine(this.directory, "missing.exe")], token);
                break;
            case "missing toolchain":
                await new ToolchainCommand(kimigayo).Execute(new() { ToolchainRoot = Path.Combine(this.directory, "no toolchain") }, ["verify"], token);
                break;
            case "check selection":
                await new CheckCommand(null!, kimigayo, solution).Execute(new(), [project], token);
                break;
            case "emit selection":
                await new EmitCommand(null!, kimigayo, solution).Execute(new(), [project], token);
                break;
            case "restore lock line":
                await new RestoreCommand(kimigayo).Execute(new(), [project], token);
                break;
            case "after the projects":
                var broken = Path.Combine(this.directory, "broken");
                Directory.CreateDirectory(broken);
                File.WriteAllText(Path.Combine(broken, "Broken.kimiproj"), "invalid project contents");
                await build.Execute(new(), [broken], token);
                break;
            default:
                await build.Execute(new(), [project], token);
                break;
        }

        var exit = Environment.ExitCode;
        Environment.ExitCode = 0;
        return exit;
    }

    private string Write(string name)
    {
        var file = Path.Combine(this.directory, name + ".kimiproj");
        File.WriteAllText(file, $"Targets={{\"{WindowsProfile.Target}\"}} OutputPath=\"out/{name}.ll\"");
        File.WriteAllText(Path.Combine(this.directory, name + ".kimi"), "::Kimi.Console.writeLine(\"Hello\")");
        return file;
    }

    // kimi test runs in process and redirects the process-global Console.Out while it runs, so its rows run alone (ConsoleIsolationCollection).
    [Collection<ConsoleIsolationCollection>]
    public sealed class ConsoleTests : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "kimi-command-" + Guid.NewGuid().ToString("N"));

        public ConsoleTests() => Directory.CreateDirectory(this.directory);

        public void Dispose() => Directory.Delete(this.directory, true);

        // kimi test's selection (IMPL 20.6.4: "2: invalid options, settings or selection"): a missing input and a solution file nested
        // deeper than the configuration reader reads (the file's failure, never the runner's 3).
        [Theory]
        [InlineData("missing input")]
        [InlineData("deep solution")]
        public async Task ATestSelectionFailureReturns2(string origin)
        {
            var kimigayo = new Kimigayo(new DiagnosticContractTest.DiagnosticConsole());
            var input = Path.Combine(this.directory, "missing.kimi");
            if (origin == "deep solution")
            {
                input = Path.Combine(this.directory, "Deep.kimisln");
                File.WriteAllText(input, "Projects = " + new string('{', 200) + new string('}', 200));
            }

            Environment.ExitCode = 0;
            await new TestCommand(kimigayo, new Solution(kimigayo)).Execute([input], TestContext.Current.CancellationToken);
            var exit = Environment.ExitCode;
            Environment.ExitCode = 0;
            Assert.Equal(2, exit);
        }

        // Kimigayo.RunEnded (Tatara unit SP1): a cancelled run <file.exe> calls it once, after its program ended (RunExecutable kills the
        // tree and drains the output first) and before "Command cancelled.", and returns 130 (after a cancellation the outcome is the
        // cancellation). The program is the test apphost's sleeping probe (TestProcessRecoveryTest.ProcessProbe): run passes no argument,
        // so the row sets the probe's environment while the program starts (this collection runs alone).
        [Fact]
        public async Task ACancelledRunCallsRunEndedAfterItsProgramAndBeforeItsResult()
        {
            Assert.SkipUnless(OperatingSystem.IsWindows(), "The probe runs on Windows.");
            var executable = Path.ChangeExtension(typeof(CommandExecutionTest).Assembly.Location, ".exe");
            var name = Path.GetFileNameWithoutExtension(executable);
            var before = Process.GetProcessesByName(name).Select(static x => x.Id).ToHashSet();
            var readyName = "kimi-run-ready-" + Guid.NewGuid().ToString("N");
            using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, readyName);
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var console = new DiagnosticContractTest.DiagnosticConsole();
            var kimigayo = new Kimigayo(console);
            Process? program = null;
            bool? ended = null;
            kimigayo.RunEnded = () =>
            {
                ended = program?.HasExited;
                console.WriteLine("RunEnded");
            };

            Environment.SetEnvironmentVariable("KIMI_TEST_PROCESS_PROBE", "sleep");
            Environment.SetEnvironmentVariable("KIMI_TEST_PROCESS_READY", readyName);
            Environment.ExitCode = 0;
            var run = new RunCommand(null!, kimigayo, new Solution(kimigayo)).Execute(new(), [executable], cancel.Token);
            try
            {
                Assert.True(await Task.Run(() => ready.WaitOne(TimeSpan.FromSeconds(30)), TestContext.Current.CancellationToken), "The program did not start.");
                program = Assert.Single(Process.GetProcessesByName(name), x => !before.Contains(x.Id) && Runs(x, executable));
                _ = program.SafeHandle; // Held, so HasExited reads this process after it ends.
            }
            finally
            {
                Environment.SetEnvironmentVariable("KIMI_TEST_PROCESS_PROBE", null);
                Environment.SetEnvironmentVariable("KIMI_TEST_PROCESS_READY", null);
                cancel.Cancel();
                await run;
            }

            var exit = Environment.ExitCode;
            Environment.ExitCode = 0;
            Assert.Equal(130, exit);
            Assert.True(ended);
            Assert.Equal("RunEnded\nCommand cancelled.\n", console.Text);
        }

        // A redirected kimi tatara session installs no Kimigayo.RunEnded (Tatara unit SP1: only a terminal session replaces it): its run
        // line calls the action installed before the session, which is still current at its end.
        [Fact]
        public async Task ARedirectedSessionLeavesRunEnded()
        {
            var provider = TataraCommandTest.Unit(new TataraCommandTest.SessionConsole()).Context.ServiceProvider;
            var kimigayo = provider.GetRequiredService<Kimigayo>();
            var calls = 0;
            Action action = () => calls++;
            kimigayo.RunEnded = action;
            var input = new MemoryStream(Encoding.UTF8.GetBytes($"run \"{this.directory.Replace('\\', '/')}/missing.exe\"\n"));
            Assert.Equal(1, await provider.GetRequiredService<TataraCommand>().Run(input, TestContext.Current.CancellationToken));
            Assert.Equal(1, calls);
            Assert.Same(action, kimigayo.RunEnded);
        }

        // Whether the process runs the file; one that cannot be read (another user's, or one that ended) does not.
        private static bool Runs(Process process, string executable)
        {
            try
            {
                return string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                return false;
            }
        }
    }
}
