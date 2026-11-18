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
    // - The message, no code: a missing input, a directory without projects, two usage errors, run's selection, a missing manifest
    //   (the toolchain's call), run --no-build without a build (the toolchain's call), restore's lock write (an output failure), a
    //   malformed solution file (the selection's read).
    // - CheckFaulted_Kd: the host's console throwing at the selected project's line (a type the toolchain's classification lists, which
    //   printed as a plain failure, and one it does not, which escaped the command), at the line after the projects (printed as a plain
    //   failure), and at the line that renders a toolchain failure (escaped the command).
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
    [InlineData("selection", "Project: ", true, null)]
    [InlineData("selection, unlisted", "Project: ", true, null)]
    [InlineData("after the projects", "No projects loaded", true, null)]
    [InlineData("toolchain's message", "Could not find file", true, null)]
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
