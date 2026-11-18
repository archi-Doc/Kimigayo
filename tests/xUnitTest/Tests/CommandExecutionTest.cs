// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Command;
using Kimi.Compiler;
using Kimi.Diagnostics;
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

    // kimi test's selection (IMPL 20.6.4: "2: invalid options, settings or selection"): a missing input and a solution file nested deeper
    // than the configuration reader reads (the file's failure, never the runner's 3).
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
}
