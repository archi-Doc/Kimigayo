// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
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
    //   restore's input, an unknown command), run's selection, the toolchain's calls (a missing manifest, run --no-build without a build
    //   on a host that runs its Windows target, a missing executable), restore's lock write (an output failure naming the lock, then the
    //   system's message, which is the host's: PLAN G1257), a malformed solution file and two files nested deeper than the configuration reader reads (the parse of a user's
    //   file is the file's failure), and a project file check cannot read (the load's reason, PLAN G1010).
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
    [InlineData("restore output", null, false, "Hello.kimi.lock.json. ")]
    [InlineData("malformed solution", null, false, "Cannot load solution")]
    [InlineData("deep solution", null, false, "Cannot load solution")]
    [InlineData("deep project", null, false, "indentation depth")]
    [InlineData("check missing", null, false, "Input not found")]
    [InlineData("check unreadable", null, false, "' could not be loaded: ")]
    [InlineData("unknown command", null, false, "Unknown command 'bulid'")]
    [InlineData("restore without input", null, false, "Restore requires one .kimiproj input.")]
    [InlineData("restore source", null, false, "Restore requires a .kimiproj input.")]
    [InlineData("restore missing", null, false, "Input not found")]
    [InlineData("missing executable", null, false, "Executable not found")]
    [InlineData("selection", "Project: ", true, null)]
    [InlineData("selection, unlisted", "Project: ", true, null)]
    [InlineData("after the projects", "No projects loaded", true, null)]
    [InlineData("toolchain's message", "Could not find file", true, null)]
    [InlineData("check selection", "Project: ", true, null)]
    [InlineData("emit selection", "Project: ", true, null)]
    [InlineData("restore lock line", "Dependency lock", true, null)]
    public async Task ACommandFailsByItsInputItsToolchainOrItsOwnFault(string origin, string? failingLine, bool faulted, string? message)
    {
        Assert.SkipUnless(origin != "no build" || TargetProfile.Host == TargetProfile.Windows, "The run needs a host that runs the Windows target.");
        Action fail = origin == "selection, unlisted" ? () => throw new FormatException("The host's console failed.") : DiagnosticContractTest.DiagnosticConsole.Fail;
        var console = new DiagnosticContractTest.DiagnosticConsole { LineAction = failingLine is null ? null : (failingLine, fail) };
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
        await new BuildCommand(null!, kimigayo, new Solution(kimigayo)).Execute([this.Write("Hello")], cancelled.Token);
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
        await new RunCommand(null!, kimigayo, new Solution(kimigayo)).Execute([project], TestContext.Current.CancellationToken);
        var exit = Environment.ExitCode;
        Environment.ExitCode = 0;
        Assert.Equal(1, exit);
        Assert.Equal(1, calls);
    }

    // The Tatara plan's B7b8 (IMPL 20.3, SPEC 22 ¶1, ruling 85; design row 19's run half): run of a target whose executables this host
    // does not run is refused before any build or record write, with or without --no-build: the location-only Unsupported_Kd at the
    // project, exit 1, no bin\ directory. Several selected targets are run's input error. Every host: no host runs linux-x64's
    // executables until B8, nor a staged row's (none-x64). Fail-before: the build's "Artifact production currently requires exactly one
    // configured Windows x64 target." (a record under bin\x86_64-pc-windows-msvc\), or with --no-build "Run requires the Windows x64
    // target.".
    [Theory]
    [InlineData("\"x86_64-unknown-linux-musl\"", false, null)]
    [InlineData("\"x86_64-unknown-none\"", false, null)]
    [InlineData("\"x86_64-unknown-linux-musl\"", true, null)]
    [InlineData("\"x86_64-pc-windows-msvc\", \"x86_64-unknown-linux-musl\"", true, "Run requires one selected target; select one with --Target.")]
    public async Task RunRefusesATargetThisHostDoesNotRunBeforeAnyBuild(string targets, bool noBuild, string? message)
    {
        var console = new DiagnosticContractTest.DiagnosticConsole();
        var kimigayo = new Kimigayo(console);
        var project = Path.Combine(this.directory, "Hello.kimiproj");
        File.WriteAllText(project, $"Targets={{{targets}}}");
        File.WriteAllText(Path.Combine(this.directory, "Hello.kimi"), "::Kimi.Console.writeLine(\"Hello\")");
        Environment.ExitCode = 0;
        await new RunCommand(null!, kimigayo, new Solution(kimigayo)).Execute(noBuild ? ["--no-build", project] : [project], TestContext.Current.CancellationToken);
        var exit = Environment.ExitCode;
        Environment.ExitCode = 0;
        Assert.Equal(1, exit);
        Assert.Contains(message ?? nameof(DiagnosticCode.Unsupported_Kd), console.Text, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(this.directory, "bin")));
    }

    // The Tatara plan's B7b8 (IMPL 20.3.1, 20.3.2; design row 22): an implicit project builds for the host's target, and with --Target
    // for linux-x64 a static ELF under bin\x86_64-unknown-linux-musl\ (libc-free, entry __kimi_start). Fail-before: the linux-x64 half
    // is "Artifact production currently requires exactly one configured Windows x64 target.", exit 1.
    [Theory]
    [InlineData(null, "x86_64-pc-windows-msvc", "A.O2.exe")]
    [InlineData("x86_64-unknown-linux-musl", "x86_64-unknown-linux-musl", "A.O2")]
    public async Task AnImplicitProjectBuildsForItsSelectedTarget(string? target, string folder, string executable)
    {
        Assert.SkipUnless(Repository.NativeBuilds, Repository.NoNativeBuilds);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        var kimigayo = new Kimigayo(console);
        var source = Path.Combine(this.directory, "A.kimi");
        File.WriteAllText(source, LinuxPair.Program("Hello", "Hello.kimi"));
        Environment.ExitCode = 0;
        await new BuildCommand(null!, kimigayo, new Solution(kimigayo)).Execute(target is null ? [source] : ["--Target", target, source], TestContext.Current.CancellationToken);
        var exit = Environment.ExitCode;
        Environment.ExitCode = 0;
        Assert.True(exit == 0, console.Text);
        var built = Path.Combine(this.directory, "bin", folder, executable);
        Assert.Equal(folder, Path.GetFileName(Assert.Single(Directory.GetDirectories(Path.Combine(this.directory, "bin")))));
        if (target is not null)
        {
            ElfFile.Read(built).AssertStatic("__kimi_start");
        }
        else
        {
            Assert.True(File.Exists(built));
        }
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
                await build.Execute([Path.Combine(this.directory, "missing.kimi")], token);
                break;
            case "no project":
                await build.Execute([Directory.CreateDirectory(Path.Combine(this.directory, "empty")).FullName], token);
                break;
            case "build usage":
                await build.Execute(["--Manifest", "app.link.json", project], token);
                break;
            case "toolchain usage":
                await new ToolchainCommand(kimigayo).Execute(["check"], token);
                break;
            case "run selection":
                this.Write("Other");
                await new RunCommand(null!, kimigayo, solution).Execute(["--no-build", this.directory], token);
                break;
            case "manifest":
            case "toolchain's message":
                await build.Execute(["--Manifest", Path.Combine(this.directory, "missing.link.json")], token);
                break;
            case "no build":
                await new RunCommand(null!, kimigayo, solution).Execute(["--no-build", project], token);
                break;
            case "restore output":
                Directory.CreateDirectory(Path.ChangeExtension(project, "kimi.lock.json"));
                await new RestoreCommand(kimigayo).Execute([project], token);
                break;
            case "malformed solution":
                var solutionFile = Path.Combine(this.directory, "App.kimisln");
                File.WriteAllText(solutionFile, "Projects = true");
                await build.Execute([solutionFile], token);
                break;
            case "deep solution":
                var deep = Path.Combine(this.directory, "Deep.kimisln");
                File.WriteAllText(deep, "Projects = " + new string('{', 200) + new string('}', 200));
                await build.Execute([deep], token);
                break;
            case "deep project":
                var deepProject = Path.Combine(this.directory, "Deep.kimiproj");
                File.WriteAllText(deepProject, "Targets = " + new string('{', 200) + new string('}', 200));
                await new RestoreCommand(kimigayo).Execute([deepProject], token);
                break;
            case "check missing":
                await new CheckCommand(null!, kimigayo, solution).Execute([Path.Combine(this.directory, "missing.kimi")], token);
                break;
            case "check unreadable":
                using (new FileStream(project, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    await new CheckCommand(null!, kimigayo, solution).Execute([project], token);
                }

                break;
            case "unknown command":
                await new DefaultCommand(kimigayo).Execute(["bulid"], token);
                break;
            case "restore without input":
                await new RestoreCommand(kimigayo).Execute([], token);
                break;
            case "restore source":
                await new RestoreCommand(kimigayo).Execute([Path.ChangeExtension(project, ".kimi")], token);
                break;
            case "restore missing":
                await new RestoreCommand(kimigayo).Execute([Path.Combine(this.directory, "missing.kimiproj")], token);
                break;
            case "missing executable":
                await new RunCommand(null!, kimigayo, solution).Execute([Path.Combine(this.directory, "missing.exe")], token);
                break;
            case "check selection":
                await new CheckCommand(null!, kimigayo, solution).Execute([project], token);
                break;
            case "emit selection":
                await new EmitCommand(null!, kimigayo, solution).Execute([project], token);
                break;
            case "restore lock line":
                await new RestoreCommand(kimigayo).Execute([project], token);
                break;
            case "after the projects":
                var broken = Path.Combine(this.directory, "broken");
                Directory.CreateDirectory(broken);
                File.WriteAllText(Path.Combine(broken, "Broken.kimiproj"), "invalid project contents");
                await build.Execute([broken], token);
                break;
            default:
                await build.Execute([project], token);
                break;
        }

        var exit = Environment.ExitCode;
        Environment.ExitCode = 0;
        return exit;
    }

    private string Write(string name)
    {
        var file = Path.Combine(this.directory, name + ".kimiproj");
        File.WriteAllText(file, $"Targets={{\"{TargetProfile.Windows.Target}\"}} OutputPath=\"out/{name}.ll\"");
        File.WriteAllText(Path.Combine(this.directory, name + ".kimi"), "::Kimi.Console.writeLine(\"Hello\")");
        return file;
    }

    // kimi test runs in process and redirects the process-global Console.Out while it runs, so its rows run alone (ConsoleIsolationCollection).
    [Collection<ConsoleIsolationCollection>]
    public sealed class ConsoleTests : IDisposable
    {
        private const string Passes = "#Test\nfunc passes()\n    $require(1 == 1)\n";

        // IMPL 20.3: an implicit project takes the host's target; a host without one (linux x64 until B8) selects no implicit project.
        private const string NoHostTarget = "An implicit project takes the host's target, and this host has none.";

        private readonly string directory = Path.Combine(Path.GetTempPath(), "kimi-command-" + Guid.NewGuid().ToString("N"));

        public ConsoleTests() => Directory.CreateDirectory(this.directory);

        public void Dispose() => Directory.Delete(this.directory, true);

        // The removed options (Tatara B6b4, decision 20; design §5.2 rows 1, 2 and 4, C4, N4-2, N4-3; C-449 B1, N1): a command other than
        // test whose arguments name --ToolchainRoot or --LlvmBin, in any spelling the reader names an option by (KimiOptions.Read),
        // fails before its work as its input error (CommandExecution), toolchain verify included: each occurrence's text on standard
        // error, nothing on standard output (check --Format json writes no document), no file written, exit 1. The reader reads an option
        // word after -- (an input word: no Kimi command forwards arguments after it, run takes no program arguments, IMPL 20.3; the
        // owner's O-526), and the parser hands on nothing after an unenclosed | (it parses the first command only).
        [Theory]
        [InlineData("build <lib> --ToolchainRoot C:\\tc", "--ToolchainRoot")]
        [InlineData("toolchain verify --ToolchainRoot x", "--ToolchainRoot")]
        [InlineData("lsp --ToolchainRoot x --debugwait false", "--ToolchainRoot")]
        [InlineData("lsp --debugwait false --LlvmBin x --ToolchainRoot y", "--LlvmBin", "--ToolchainRoot")]
        [InlineData("run <lib> --LlvmBin x", "--LlvmBin")]
        [InlineData("build <lib> -ToolchainRoot x", "--ToolchainRoot")]
        [InlineData("build <lib> --TOOLCHAINROOT x", "--ToolchainRoot")]
        [InlineData("build <lib> --LlvmBin y --ToolchainRoot x", "--LlvmBin", "--ToolchainRoot")]
        [InlineData("check <lib> --Format json --ToolchainRoot y", "--ToolchainRoot")]
        [InlineData("build <lib> -- --ToolchainRoot", "--ToolchainRoot")]
        [InlineData("build <lib> | --ToolchainRoot x")]
        public async Task ARemovedOptionFailsTheLineBeforeItsCommand(string line, params string[] removed)
        {
            var folder = Path.Combine(this.directory, "Lib");
            TataraCommandTest.WriteLibrary(folder);
            var files = TataraCommandTest.Files(this.directory);
            var console = new TataraCommandTest.SessionConsole();
            var product = TataraCommandTest.Unit(console);
            var (output, error) = (Console.Out, Console.Error);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            Console.SetOut(stdout);
            Console.SetError(stderr);
            try
            {
                Environment.ExitCode = 0;
                await product.Execute(line.Replace("<lib>", Path.Combine(folder, "Lib.kimiproj"), StringComparison.Ordinal), product.Context.ServiceProvider, TestContext.Current.CancellationToken);
            }
            finally
            {
                Console.SetOut(output);
                Console.SetError(error);
            }

            var exit = Environment.ExitCode;
            Environment.ExitCode = 0;
            var texts = stderr.ToString().Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Where(x => x.Contains(" was removed: ", StringComparison.Ordinal));
            Assert.Equal(removed.Select(x => x + " was removed: Kimi carries its toolchain."), texts);
            if (removed.Length != 0)
            {
                Assert.Equal(1, exit);
                Assert.Equal(string.Empty, stdout.ToString() + console.Text);
                Assert.Equal(files, TataraCommandTest.Files(this.directory));
            }
        }

        // The removal names only the removed options (C-447 N8): toolchain verify --Report <path> binds its option and writes its report
        // (scripts/build-kimi.ps1 reads it).
        [Fact]
        public async Task ToolchainVerifyStillBindsItsReport()
        {
            Assert.SkipUnless(Repository.NativeBuilds, Repository.NoNativeBuilds);
            var report = Path.Combine(this.directory, "verify report.json");
            var product = TataraCommandTest.Unit(new());
            Environment.ExitCode = 0;
            await product.Execute($"toolchain verify --Report \"{report}\"", product.Context.ServiceProvider, TestContext.Current.CancellationToken);
            var exit = Environment.ExitCode;
            Environment.ExitCode = 0;
            Assert.Equal(0, exit);
            using var record = JsonDocument.Parse(File.ReadAllText(report));
            Assert.Equal("passed", record.RootElement.GetProperty("status").GetString());
        }

        // test reports a removed option as its invalid option (Tatara B6b4, C-449 B1; IMPL 20.6.4: 2 for invalid options; 20.6.2: the
        // result document under --Format json, read in any spelling): the removal's text where test reports its errors, on standard
        // error or in the document, and no file written.
        [Theory]
        [InlineData("--LlvmBin x", false)]
        [InlineData("--format json -llvmbin x", true)]
        public async Task ARemovedOptionIsAnInvalidTestOption(string options, bool json)
        {
            var folder = Path.Combine(this.directory, "Lib");
            TataraCommandTest.WriteLibrary(folder);
            var files = TataraCommandTest.Files(this.directory);
            var product = TataraCommandTest.Unit(new());
            var (output, error) = (Console.Out, Console.Error);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            Console.SetOut(stdout);
            Console.SetError(stderr);
            try
            {
                Environment.ExitCode = 0;
                await product.Execute($"test {Path.Combine(folder, "Lib.kimiproj")} {options}", product.Context.ServiceProvider, TestContext.Current.CancellationToken);
            }
            finally
            {
                Console.SetOut(output);
                Console.SetError(error);
            }

            var exit = Environment.ExitCode;
            Environment.ExitCode = 0;
            Assert.Equal(2, exit);
            const string text = "--LlvmBin was removed: Kimi carries its toolchain.";
            if (json)
            {
                using var document = JsonDocument.Parse(stdout.ToString());
                Assert.Equal(text, Assert.Single(document.RootElement.GetProperty("errors").EnumerateArray()).GetString());
                Assert.Equal(2, document.RootElement.GetProperty("exitCode").GetInt32());
                Assert.DoesNotContain(text, stderr.ToString(), StringComparison.Ordinal);
            }
            else
            {
                Assert.Equal(text, stderr.ToString().Trim());
            }

            Assert.Equal(files, TataraCommandTest.Files(this.directory));
        }

        // The one option table (KimiOptions; IMPL 20.3, 20.3.2, 20.5, 20.6.2; the owner's O-525, O-526 and O-527; PLAN G1236, G1237) at the
        // command line, on a Windows x64 library project with one test case: each row's exit code and a text of the stream it writes. test
        // reads the table as every command does (names in any case, with any dashes); an invalid option (unknown, outside its command's
        // rows, repeated, without its value or with a value outside its listed set) is reported on standard error where it is read: by
        // every other command each, before its work (1), by test its first (2); --filter takes no option word as its value; the default
        // command and tatara take no option; a word of dashes alone is an input and an option after it is read; a quoted option word is an
        // option (the parser hands it on unwrapped); command names ignore case (the Middle's R4). Standard error is the row's whole text.
        [Theory]
        [InlineData("test <lib> --Format json --list", 0, "stdout", "\"operation\": \"list\"")]
        [InlineData("test <lib> --Format JSON --list", 0, "stdout", "\"operation\": \"list\"")]
        [InlineData("test <lib> --target <win> --list", 0, "stdout", "Lib.kimiproj::passes")]
        [InlineData("test <lib> -list", 0, "stdout", "Lib.kimiproj::passes")]
        [InlineData("test <lib> --list true", 2, "stderr", "Test accepts one project, source, solution or directory input.")]
        [InlineData("test <lib> --Formats json --list", 2, "stderr", "Unknown option: --Formats")]
        [InlineData("test <lib> --bogus", 2, "stderr", "Unknown option: --bogus")]
        [InlineData("test <lib> --filter --LlvmBin --list", 2, "stderr", "Missing value: --filter")]
        [InlineData("check <lib> --bogus", 1, "stderr", "Unknown option: --bogus")]
        [InlineData("check <lib> --Target=x", 1, "stderr", "Unknown option: --Target=x")]
        [InlineData("check <lib> --Target <win> --Target <win>", 1, "stderr", "Repeated option: --Target")]
        [InlineData("check <lib> --Debug true --debug false", 1, "stderr", "Repeated option: --Debug")]
        [InlineData("build <lib> --Format json", 1, "stderr", "Unknown option: --Format")]
        [InlineData("restore <lib> --Format json", 1, "stderr", "Unknown option: --Format")]
        [InlineData("restore <lib> --Debug true", 1, "stderr", "Unknown option: --Debug")]
        [InlineData("restore <lib> --locked true", 1, "stderr", "Unknown option: --locked")]
        [InlineData("check <lib> --Target --LlvmBin", 1, "stderr", "Missing value: --Target\n--LlvmBin was removed: Kimi carries its toolchain.")]
        [InlineData("check <lib> --Debug", 1, "stderr", "Missing value: --Debug")]
        [InlineData("check <lib> --Debug yes", 1, "stderr", "Invalid value: --Debug yes")]
        [InlineData("--Target x", 1, "stderr", "Unknown option: --Target")]
        [InlineData("tatara --Target x", 1, "stderr", "Unknown option: --Target")]
        [InlineData("run <lib> -- --no-build", 1, "console", "Input not found")]
        [InlineData("check <lib> \"--Target\" <win>", 0, "console", "")]
        [InlineData("CHECK <lib>", 0, "console", "")]
        public async Task AnOptionIsReadByTheOneTable(string line, int expected, string stream, string text)
        {
            var project = WriteTests(Path.Combine(this.directory, "Lib", "Lib.kimiproj"), string.Empty);
            var console = new TataraCommandTest.SessionConsole();
            var product = TataraCommandTest.Unit(console);
            var (output, error) = (Console.Out, Console.Error);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            Console.SetOut(stdout);
            Console.SetError(stderr);
            try
            {
                Environment.ExitCode = 0;
                line = line.Replace("<lib>", project, StringComparison.Ordinal).Replace("<win>", TargetProfile.Windows.Target, StringComparison.Ordinal);
                await product.Execute(line, product.Context.ServiceProvider, TestContext.Current.CancellationToken);
            }
            finally
            {
                Console.SetOut(output);
                Console.SetError(error);
            }

            var exit = Environment.ExitCode;
            Environment.ExitCode = 0;
            Assert.Equal(expected, exit);
            if (stream == "stderr")
            {
                Assert.Equal(text, stderr.ToString().ReplaceLineEndings("\n").TrimEnd());
            }
            else
            {
                Assert.Contains(text, (stream == "stdout" ? stdout.ToString() : console.Text).ReplaceLineEndings("\n"), StringComparison.Ordinal);
            }
        }

        // A help request runs no command (T-M15 consequence (2)) and lists no option: a command's options are KimiOptions' rows, which the
        // parser does not read. The parser writes its help to the service provider's console.
        [Fact]
        public async Task HelpListsNoOption()
        {
            var console = new TataraCommandTest.SessionConsole();
            var product = TataraCommandTest.Unit(console);
            Environment.ExitCode = 0;
            await product.Execute("help build", product.Context.ServiceProvider, TestContext.Current.CancellationToken);
            var exit = Environment.ExitCode;
            Environment.ExitCode = 0;
            Assert.Equal(0, exit);
            Assert.Contains("build", console.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Target", console.Text, StringComparison.OrdinalIgnoreCase);
        }

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

        // kimi test's failures (IMPL 20.6.4; DECISIONS R9-M12; PLAN G1012), the expected outcome first. Exit 2 with its message: an empty
        // selection (O2); two projects with one identity, the later one's line, the loop going on to the next project, whose case is
        // listed (O3); an empty --results-dir, an option error at Parse before any project is prepared, also where no run follows (O5,
        // O5b); a project file the load cannot read, its reason through the service (L1, PLAN G1010). Exit 3 with its message: a results
        // directory that cannot be created, a file in its place (R1, the runner's storage), on a host that runs its Windows target, since
        // the host rule comes before the run directory (TestRefusesATargetThisHostDoesNotRunAfterListing).
        // Exit 2, the command's own fault (DIAGNOSTICS rule 9): one CheckFaulted_Kd rendered and its code and message as the result's one
        // errors entry, for the host's console throwing at the "Project: " line (F1, in text and in the document) and at the native
        // build's "Built: " report (F2). No production seam: the console is the host's.
        [Theory]
        [InlineData("project", 2, "No test cases match the selection.", "--list", "--filter", "zzz")]
        [InlineData("identity", 2, "B: Conflicting project identity; configure distinct TestProjectId values: Same", "--list")]
        [InlineData("project", 2, "Invalid path: --results-dir: ", "--results-dir", "")]
        [InlineData("project", 2, "Invalid path: --results-dir: ", "--list", "--results-dir", "")]
        [InlineData("project", 2, "Invalid path: --results-dir: ", "--allow-empty", "--filter", "zzz", "--results-dir", "")]
        [InlineData("unreadable", 2, "Every selected project must load successfully.", "--list")]
        [InlineData("results file", 3, null)]
        [InlineData("Project: ", 2, null, "--list")]
        [InlineData("Project: ", 2, null, "--list", "--format", "json")]
        [InlineData("Built: ", 2, null)]
        public async Task TestFailsByItsInputItsRunnerOrItsOwnFault(string origin, int expected, string? message, params string[] options)
        {
            Assert.SkipUnless(origin != "Built: " || Repository.NativeBuilds, Repository.NoNativeBuilds);
            Assert.SkipUnless(origin != "results file" || TargetProfile.Host == TargetProfile.Windows, "The run needs a host that runs the Windows target.");
            var input = WriteTests(Path.Combine(this.directory, "A", "A.kimiproj"), origin == "identity" ? "TestProjectId = \"Same\"\n" : string.Empty);
            if (origin == "identity")
            {
                WriteTests(Path.Combine(this.directory, "B", "B.kimiproj"), "TestProjectId = \"Same\"\n");
                WriteTests(Path.Combine(this.directory, "C", "C.kimiproj"), string.Empty);
                input = Path.Combine(this.directory, "S.kimisln");
                File.WriteAllText(input, "Projects = { \"A/A.kimiproj\", \"B/B.kimiproj\", \"C/C.kimiproj\" }\n");
            }
            else if (origin == "results file")
            {
                options = ["--results-dir", Path.Combine(this.directory, "A", "tests.kimi")];
            }

            using var held = origin == "unreadable" ? new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.None) : null;
            var console = new DiagnosticContractTest.DiagnosticConsole { LineAction = origin.EndsWith(": ", StringComparison.Ordinal) ? (origin, DiagnosticContractTest.DiagnosticConsole.Fail) : null };
            var kimigayo = new Kimigayo(console);
            var (output, error) = (Console.Out, Console.Error);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            Console.SetOut(stdout);
            Console.SetError(stderr);
            try
            {
                Environment.ExitCode = 0;
                await new TestCommand(kimigayo, new Solution(kimigayo)).Execute([input, .. options], TestContext.Current.CancellationToken);
            }
            finally
            {
                Console.SetOut(output);
                Console.SetError(error);
            }

            var exit = Environment.ExitCode;
            Environment.ExitCode = 0;
            Assert.Equal(expected, exit);
            using var document = options.Contains("json") ? JsonDocument.Parse(stdout.ToString()) : null;
            var errors = document?.RootElement.GetProperty("errors").EnumerateArray().Select(x => x.GetString()!).ToArray() ??
                stderr.ToString().Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (origin.EndsWith(": ", StringComparison.Ordinal))
            {
                Assert.Equal(2, console.Text.Split(nameof(DiagnosticCode.CheckFaulted_Kd)).Length);
                Assert.Equal(nameof(DiagnosticCode.CheckFaulted_Kd) + ": " + DiagnosticFaults.MessagePrefix + DiagnosticFaults.Describe(DiagnosticFault.Exception), Assert.Single(errors));
            }
            else
            {
                Assert.DoesNotContain(nameof(DiagnosticCode.CheckFaulted_Kd), console.Text + stderr, StringComparison.Ordinal);
                Assert.StartsWith(message ?? string.Empty, errors[0], StringComparison.Ordinal);
                Assert.True(message is null || errors.Length == 1);
            }

            Assert.True(message?.StartsWith("Invalid path", StringComparison.Ordinal) != true || console.Text.Length == 0, console.Text);
            Assert.Equal(origin == "unreadable", console.Text.Contains("' could not be loaded: ", StringComparison.Ordinal));
            Assert.Equal(origin == "identity", stdout.ToString().Contains(" C.kimiproj::passes", StringComparison.Ordinal));
        }

        // The Tatara plan's B7b8 (IMPL 20.6.1, 20.6.4: 2; design row 19's test half, notes D1): a test project for a target this host does
        // not run is listed (nothing runs), and its run is refused after preparation, before the run directory and any generation or
        // native build: the location-only Unsupported_Kd at the project, one errors entry naming the project, exit 2, no bin\ directory.
        // Fail-before: both rows are preparation's "Testing requires a configured Windows x64 target.", exit 2; the errors entry, commit 2's
        // empty errors (C-575 N2).
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task TestRefusesATargetThisHostDoesNotRunAfterListing(bool list)
        {
            var input = WriteTests(Path.Combine(this.directory, "A", "A.kimiproj"), string.Empty);
            File.WriteAllText(input, File.ReadAllText(input).Replace(TargetProfile.Windows.Target, TargetProfile.Linux.Target, StringComparison.Ordinal));
            var console = new DiagnosticContractTest.DiagnosticConsole();
            var kimigayo = new Kimigayo(console);
            var (output, error) = (Console.Out, Console.Error);
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            Console.SetOut(stdout);
            Console.SetError(stderr);
            try
            {
                Environment.ExitCode = 0;
                await new TestCommand(kimigayo, new Solution(kimigayo)).Execute(list ? ["--list", input] : [input], TestContext.Current.CancellationToken);
            }
            finally
            {
                Console.SetOut(output);
                Console.SetError(error);
            }

            var exit = Environment.ExitCode;
            Environment.ExitCode = 0;
            Assert.Equal(list ? 0 : 2, exit);
            Assert.Contains("::passes", stdout.ToString(), StringComparison.Ordinal); // Listed, or reported NOT STARTED by the run.
            Assert.Equal(!list, stdout.ToString().Contains("NOT STARTED ", StringComparison.Ordinal));
            Assert.Equal(!list, console.Text.Contains(nameof(DiagnosticCode.Unsupported_Kd), StringComparison.Ordinal));
            string[] errors = list ? [] : ["This host does not run the test target: A"]; // The result's one errors entry (C-575 N2).
            Assert.Equal(errors, stderr.ToString().Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            Assert.False(Directory.Exists(Path.Combine(this.directory, "A", "bin")));
        }

        // kimi test takes its arguments from the command line as every command does (PLAN G1214): the parser splits the line and unwraps
        // a quoted argument, so a quoted input, with a space or without, and a quoted option value arrive without their quotes, a bare
        // --locked means true and --locked false is the Boolean option's value, not the input (IMPL 20.3; KimiOptions.Read); an unquoted
        // input is unchanged. --list builds nothing natively.
        [Theory]
        [InlineData("test --list \"<input>\"", "with space")]
        [InlineData("test --list \"<input>\"", "nospace")]
        [InlineData("test --list <input>", "nospace")]
        [InlineData("test --list --filter \"passes\" --locked <input>", "nospace")]
        [InlineData("test --list --locked false <input>", "nospace")]
        public async Task TestTakesItsArgumentsFromTheCommandLineAsEveryCommand(string line, string folder)
        {
            var project = WriteTests(Path.Combine(this.directory, folder, "Tests.kimiproj"), string.Empty);
            var product = TataraCommandTest.Unit(new());
            var console = Console.Out;
            using var output = new StringWriter();
            Console.SetOut(output);
            try
            {
                Environment.ExitCode = 0;
                await product.Execute(line.Replace("<input>", project, StringComparison.Ordinal), product.Context.ServiceProvider, TestContext.Current.CancellationToken);
            }
            finally
            {
                Console.SetOut(console);
            }

            var exit = Environment.ExitCode;
            Environment.ExitCode = 0;
            Assert.Equal(0, exit);
            Assert.Contains("Tests.kimiproj::passes", output.ToString(), StringComparison.Ordinal);
        }

        // The project identity (IMPL 20.6.2: "the project file name with extension; an implicit project uses its source file name") is
        // the file name of the project's input as its path spells it, and the result document's path is that input (Project.Input; PLAN
        // G1273): an implicit A.KIMI is A.KIMI, never A.kimi, and its path the source file, never its folder; a project file unchanged.
        [Theory]
        [InlineData("A.KIMI")]
        [InlineData("A.kimi")]
        [InlineData("Tests.kimiproj")]
        public async Task TheProjectIdentityAndTheResultPathAreTheInputs(string file)
        {
            Assert.SkipUnless(file == "Tests.kimiproj" || TargetProfile.Host is not null, NoHostTarget);
            var input = Path.Combine(this.directory, file);
            File.WriteAllText(Path.Combine(this.directory, file == "Tests.kimiproj" ? "tests.kimi" : file), Passes);
            if (file == "Tests.kimiproj")
            {
                File.WriteAllText(input, $"Targets = {{ \"{TargetProfile.Windows.Target}\" }}\nOutputKind = \"Library\"\nTestSources = {{ \"tests.kimi\" }}\n");
            }

            var (exit, document) = await this.List(input);
            using (document)
            {
                Assert.Equal(0, exit);
                var project = Assert.Single(document.RootElement.GetProperty("projects").EnumerateArray());
                Assert.Equal((file, input), (project.GetProperty("identity").GetString(), project.GetProperty("path").GetString()));
                Assert.Equal(file, Assert.Single(document.RootElement.GetProperty("cases").EnumerateArray()).GetProperty("projectId").GetString());
            }
        }

        // Two selected Projects with one identity are an error (IMPL 20.6.2; 20.6.4: 2), compared as the identities are spelled, and the
        // result document lists the projects in their inputs' order (PLAN G1273): a solution's implicit A.kimi and A.KIMI are two
        // identities, in two folders or as one folder's two spellings (one file on a case-insensitive disk, selected twice: PLAN G1289);
        // two A.kimi are one; b/A.kimi follows a/B.kimi.
        [Theory]
        [InlineData("a/A.kimi", "b/A.KIMI", 0, "A.kimi A.KIMI")]
        [InlineData("a/A.kimi", "b/A.kimi", 2, "A.kimi")]
        [InlineData("a/A.kimi", "a/A.KIMI", 0, "A.KIMI A.kimi")]
        [InlineData("b/A.kimi", "a/B.kimi", 0, "B.kimi A.kimi")]
        public async Task SolutionProjectsConflictByTheirSpelledIdentitiesAndFollowTheirInputs(string first, string second, int expected, string identities)
        {
            Assert.SkipUnless(TargetProfile.Host is not null, NoHostTarget);
            foreach (var source in new[] { first, second })
            {
                Directory.CreateDirectory(Path.Combine(this.directory, Path.GetDirectoryName(source)!));
                File.WriteAllText(Path.Combine(this.directory, source), Passes);
            }

            var solution = Path.Combine(this.directory, "S.kimisln");
            File.WriteAllText(solution, $"Projects = {{ \"{first}\", \"{second}\" }}\n");
            var (exit, document) = await this.List(solution);
            using (document)
            {
                Assert.Equal(expected, exit);
                Assert.Equal(identities, string.Join(' ', document.RootElement.GetProperty("projects").EnumerateArray().Select(x => x.GetProperty("identity").GetString())));
            }
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
            var run = new RunCommand(null!, kimigayo, new Solution(kimigayo)).Execute([executable], cancel.Token);
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

        // A Windows x64 library project with one passing test case, its own settings after the target's.
        private static string WriteTests(string project, string settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(project)!);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(project)!, "tests.kimi"), Passes);
            File.WriteAllText(project, $"Targets = {{ \"{TargetProfile.Windows.Target}\" }}\nOutputKind = \"Library\"\nTestSources = {{ \"tests.kimi\" }}\n" + settings);
            return project;
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

        // test --list --format json of the input (no native build): the exit code and the result document.
        private async Task<(int Exit, JsonDocument Document)> List(string input)
        {
            var product = TataraCommandTest.Unit(new());
            var console = Console.Out;
            using var output = new StringWriter();
            Console.SetOut(output);
            try
            {
                Environment.ExitCode = 0;
                await product.Execute($"test --list --format json \"{input}\"", product.Context.ServiceProvider, TestContext.Current.CancellationToken);
            }
            finally
            {
                Console.SetOut(console);
            }

            var exit = Environment.ExitCode;
            Environment.ExitCode = 0;
            return (exit, JsonDocument.Parse(output.ToString()));
        }
    }
}
