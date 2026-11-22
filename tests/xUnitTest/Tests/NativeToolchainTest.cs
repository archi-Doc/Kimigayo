// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Kimi;
using Kimi.Build;
using Kimi.Command;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public sealed class NativeToolchainTest : IDisposable
{
    private const string Windows = "Native builds run on Windows.";

    private readonly string directory = Path.Combine(Path.GetTempPath(), "kimi-native-" + Guid.NewGuid().ToString("N"));

    // The host's console of the test's projects, which reads a refusal's message.
    private readonly DiagnosticContractTest.DiagnosticConsole console = new();

    public NativeToolchainTest() => Directory.CreateDirectory(this.directory);

    public void Dispose() => Directory.Delete(this.directory, true);

    [Fact]
    public void NativeInputSnapshotDoesNotFollowChangesToTheOriginal()
    {
        var input = Path.Combine(this.directory, "input.lib");
        File.WriteAllText(input, "first");
        var staged = Stage(out var hash);
        Assert.Equal(ArtifactFiles.Hash(input), hash);
        File.WriteAllText(input, "second");
        Assert.Equal("first", File.ReadAllText(staged));
        Assert.Equal(hash, ArtifactFiles.Hash(staged));
        File.WriteAllText(input, "first");
        File.WriteAllText(staged, "damaged staged copy");
        Assert.Equal(staged, Stage(out var repairedHash));
        Assert.Equal(hash, repairedHash);
        Assert.Equal("first", File.ReadAllText(staged));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(staged)!, "*.tmp"));

        string Stage(out string stagedHash)
        {
            using var stream = File.OpenRead(input);
            return NativeToolchain.StageInput(stream, Path.Combine(this.directory, "output"), out stagedHash);
        }
    }

    // IMPL 20.4 (Tatara B6a): the compiler carries its supplies' bytes. The rows are the two reserved names in Ordinal order, each row's
    // SHA-256 the catalog's, and each embedded resource has that SHA-256 and is the file committed beside the catalog, so a build
    // stages the adopted bytes.
    [Fact]
    public void EachCompilerSupplyIsTheCatalogsCommittedFile()
    {
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/backend/windows-x64"));
        using var catalog = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "profile.json")));
        Assert.Equal(
            [
                "kernel32 " + catalog.RootElement.GetProperty("kernel32").GetProperty("sha256").GetString(),
                "kimi_backend " + catalog.RootElement.GetProperty("artifactSha256").GetString(),
            ],
            ToolchainIdentity.Supplies.Select(static x => x.Supply.Name + " " + x.Sha256));
        foreach (var (supply, file) in ToolchainIdentity.Supplies.Zip(new[] { "kernel32.lib", "kimi_backend_windows_x64_v1.lib" }))
        {
            using var stream = supply.Open();
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            Assert.Equal(supply.Sha256, Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
            Assert.Equal(File.ReadAllBytes(Path.Combine(directory, file)), bytes.ToArray());
        }
    }

    [Fact]
    public async Task ManifestFailureInvalidatesPreviousSuccessBeforeLoadingTools()
    {
        var project = this.Create();
        Assert.True(await project.Generate(TestContext.Current.CancellationToken));
        var paths = ArtifactPaths.Create(project);
        File.WriteAllText(paths.Executable, "previous executable");
        File.WriteAllText(paths.Record, "{\"status\":\"linked\"}");
        File.AppendAllText(paths.Ir, "; changed");
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => NativeToolchain.BuildManifest(paths.Manifest, (_, _) => { }, TestContext.Current.CancellationToken));
        Assert.Contains("IR/manifest SHA-256", error.Message);
        using var record = JsonDocument.Parse(File.ReadAllText(paths.Record));
        Assert.Equal("incomplete", record.RootElement.GetProperty("status").GetString());
        Assert.Equal("previous executable", File.ReadAllText(paths.Executable));
    }

    // R9-B1 part 2 (form J, PLAN G983's manifest share): a manifest field that is missing, of another JSON kind or, for an Int32 row,
    // another number fails at its read with InvalidDataException naming the field, before Tatara is loaded and with the record left
    // incomplete; a fixed field with another value is not this compiler's, and a removed member (Tatara B6b5: the toolchain, whatever its
    // content) asks to re-emit. Each row edits one field of an emitted Application's manifest (a JSON value, or null to remove it). The
    // libraries are read after Tatara is loaded, so their rows need a toolchain.
    [Theory]
    [InlineData("toolchain", "{\"llvmBin\": \"../tools\"}", "The link manifest's 'toolchain.llvmBin' was removed: Kimi carries its toolchain. Re-emit the manifest.")]
    [InlineData("schemaVersion", "3.5", "'schemaVersion' is not this compiler's")]
    [InlineData("schemaVersion", "\"4\"", "'schemaVersion' is missing or not a JSON Number")]
    [InlineData("backendSupport.abiVersion", "2.5", "'backendSupport.abiVersion' is not this compiler's")]
    [InlineData("codegen.cpu", "\"native\"", "'codegen.cpu' is not this compiler's")]
    [InlineData("codegen.features", "\"+sse2\"", "'codegen.features' is missing or not a JSON Array")]
    [InlineData("codegen", null, "'codegen' is missing or not a JSON Object")]
    [InlineData("externalDeclarations", null, "'externalDeclarations' is missing or not a JSON Array")]
    [InlineData("irFile", "7", "'irFile' is missing or not a JSON String")]
    [InlineData("optimization", null, "'optimization' is missing or not a JSON String")]
    [InlineData("irSha256", "1", "'irSha256' is missing or not a JSON String")]
    public async Task AManifestFieldFailsAtItsRead(string field, string? value, string failure)
    {
        var project = this.Create();
        Assert.True(await project.Generate(TestContext.Current.CancellationToken));
        var paths = ArtifactPaths.Create(project);
        var manifest = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(paths.Manifest))!.AsObject();
        var (section, name) = field.Contains('.') ? (manifest[field[..field.IndexOf('.')]]!.AsObject(), field[(field.IndexOf('.') + 1)..]) : (manifest, field);
        if (value is null)
        {
            Assert.True(section.Remove(name));
        }
        else
        {
            section[name] = System.Text.Json.Nodes.JsonNode.Parse(value);
        }

        File.WriteAllText(paths.Manifest, manifest.ToJsonString());
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => NativeToolchain.BuildManifest(paths.Manifest, (_, _) => { }, TestContext.Current.CancellationToken));
        Assert.Contains(failure, error.Message);
        using var record = JsonDocument.Parse(File.ReadAllText(paths.Record));
        Assert.Equal("incomplete", record.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void CompilerAndBackendUseBuildPropsRelease()
    {
        var props = XDocument.Load(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Directory.Build.props")));
        var expected = props.Descendants().Single(x => x.Name.LocalName == "Version").Value;
        Assert.Equal(expected, CompilerRelease.Version);
        Assert.Equal(expected, ToolchainIdentity.BackendVersion);
        Assert.StartsWith(expected + " (", Compilation.CompilerVersion, StringComparison.Ordinal);
    }

    // toolchain verify (Tatara B6b3; decision 20 Q3 (a)): a self-check of what the compiler carries. It takes no toolchain directory: it
    // reports the loaded Tatara and each supply's hash, the catalog's, as Information lines, and its report is the self-check's record
    // (passed; Tatara's build, ABI and LLVM version; each supply's hash against its row's). That it reads no root and runs no tool is
    // test-cli's row (a copy of the compiler outside any checkout, PATH clean).
    [Fact]
    public void VerificationIsASelfCheckOfWhatTheCompilerCarries()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var report = Path.Combine(this.directory, "evidence", "toolchain.json");
        var lines = new List<string>();
        NativeToolchain.Verify(report, (severity, line) => lines.Add($"{severity}: {line}"), TestContext.Current.CancellationToken);
        var build = Tatara.Instance.Build;
        string[] expected =
            [$"Information: Tatara: build {build}, ABI {Tatara.ExpectedAbi}, LLVM {ToolchainIdentity.LlvmVersion}", .. ToolchainIdentity.Supplies.Select(static x => $"Information: {x.Supply.Name}: {x.Sha256} (the catalog's)")];
        Assert.Equal(expected, lines);
        using var record = JsonDocument.Parse(File.ReadAllText(report));
        var root = record.RootElement;
        Assert.Equal("status,compilerVersion,llvmVersion,tatara,supplies,seconds", string.Join(',', root.EnumerateObject().Select(static x => x.Name)));
        Assert.Equal("passed", root.GetProperty("status").GetString());
        Assert.Equal(CompilerRelease.Version, root.GetProperty("compilerVersion").GetString());
        Assert.Equal(ToolchainIdentity.LlvmVersion, root.GetProperty("llvmVersion").GetString());
        var tatara = root.GetProperty("tatara");
        Assert.Equal("build,abi,llvmVersion", string.Join(',', tatara.EnumerateObject().Select(static x => x.Name)));
        Assert.Equal(build, tatara.GetProperty("build").GetString());
        Assert.Equal(Tatara.ExpectedAbi, tatara.GetProperty("abi").GetString());
        Assert.Equal(ToolchainIdentity.LlvmVersion, tatara.GetProperty("llvmVersion").GetString());
        Assert.Equal(
            ToolchainIdentity.Supplies.Select(static x => $"{x.Supply.Name} {x.Sha256} {x.Sha256} True"),
            root.GetProperty("supplies").EnumerateArray().Select(static x =>
                $"{x.GetProperty("name").GetString()} {x.GetProperty("sha256").GetString()} {x.GetProperty("expectedSha256").GetString()} {x.GetProperty("hashMatched").GetBoolean()}"));
    }

    // A supply whose embedded bytes are not its row's SHA-256 (only a defective build of Kimi carries one) is the toolchain's failure,
    // its statement kept in the report's supplies.
    [Fact]
    public void VerificationRejectsASupplyThatIsNotItsCatalogsBytes()
    {
        var supplies = new System.Text.Json.Nodes.JsonArray();
        var supply = ToolchainIdentity.Supplies[1] with { Sha256 = new string('0', 64) };
        var error = Assert.Throws<InvalidDataException>(() => NativeToolchain.VerifySupply(supply, supplies));
        Assert.Equal("The compiler's kimi_backend does not match its catalog SHA-256.", error.Message);
        Assert.True(NativeToolchain.IsToolchainFailure(error));
        Assert.Equal(ToolchainIdentity.BackendSha256, supplies[0]!["sha256"]!.GetValue<string>());
        Assert.False(supplies[0]!["hashMatched"]!.GetValue<bool>());
    }

    [Fact]
    public async Task LibraryBuildRecordsProductChecksWithoutEmissionOrTools()
    {
        var file = Path.Combine(this.directory, "Library.kimiproj");
        File.WriteAllText(file, "Targets = { \"x86_64-pc-windows-msvc\" }\nOutputKind = \"Library\"\nTestSources = { \"tests.kimi\" }");
        File.WriteAllText(Path.Combine(this.directory, "product.kimi"), "public group Example");
        File.WriteAllText(Path.Combine(this.directory, "tests.kimi"), "let broken =");
        Assert.True(Project.TryCreate(new Kimigayo(this.console), null, file, out var project));
        project.KimiOptions.Debug = true;
        Assert.True(await project.Build(TestContext.Current.CancellationToken));
        var paths = ArtifactPaths.Create(project);
        using var record = JsonDocument.Parse(File.ReadAllText(paths.Record));
        var root = record.RootElement;
        Assert.Equal("checked", root.GetProperty("status").GetString());
        Assert.Equal("Library", root.GetProperty("outputKind").GetString());
        Assert.Equal(file, root.GetProperty("input").GetString());
        Assert.Equal(WindowsProfile.Target, root.GetProperty("target").GetString());
        Assert.True(root.GetProperty("debug").GetBoolean());
        Assert.Equal(Compilation.CurrentLanguageVersion, root.GetProperty("languageVersion").GetString());
        Assert.Equal(Compilation.CompilerVersion, root.GetProperty("compilerVersion").GetString());
        Assert.False(File.Exists(paths.Ir));
        Assert.False(File.Exists(paths.Manifest));
        Assert.False(File.Exists(paths.Executable));
        await this.AssertRunRefused(project, "No built Application is available. Run 'kimi build' first.");
    }

    [Theory]
    [InlineData(OutputKind.Application)]
    [InlineData(OutputKind.Library)]
    public async Task SemanticFailureInvalidatesPreviousBuildBeforeInvokingTools(OutputKind kind)
    {
        var project = this.Create("let broken =");
        project.ProjectFile.OutputKind = kind;
        var paths = ArtifactPaths.Create(project);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.Record)!);
        File.WriteAllText(paths.Record, "{\"status\":\"linked\"}");
        Assert.False(await project.Build(TestContext.Current.CancellationToken));
        using var record = JsonDocument.Parse(File.ReadAllText(paths.Record));
        Assert.Equal("incomplete", record.RootElement.GetProperty("status").GetString());
        Assert.False(File.Exists(paths.Ir));
    }

    [Fact]
    public async Task LibraryManifestCannotReplaceProductChecks()
    {
        var project = this.Create("public group Example");
        project.ProjectFile.OutputKind = OutputKind.Library;
        Assert.True(await project.Generate(TestContext.Current.CancellationToken));
        var paths = ArtifactPaths.Create(project);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => NativeToolchain.BuildManifest(paths.Manifest, (_, _) => { }, TestContext.Current.CancellationToken));
        Assert.Contains("Application", error.Message);
        using var record = JsonDocument.Parse(File.ReadAllText(paths.Record));
        Assert.Equal("incomplete", record.RootElement.GetProperty("status").GetString());
        Assert.False(File.Exists(paths.Executable));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("aarch64-pc-windows-msvc", false)]
    [InlineData("x86_64-pc-windows-msvc", true)]
    public async Task LibraryBuildRequiresOneSelectedSupportedTarget(string selected, bool accepted)
    {
        var project = this.Create("public group Example");
        project.ProjectFile.OutputKind = OutputKind.Library;
        project.ProjectFile.Targets = ["aarch64-pc-windows-msvc", WindowsProfile.Target];
        project.KimiOptions.Target = selected;
        Assert.Equal(accepted, await project.Build(TestContext.Current.CancellationToken));
        using var record = JsonDocument.Parse(File.ReadAllText(ArtifactPaths.Create(project).Record));
        Assert.Equal(accepted ? "checked" : "incomplete", record.RootElement.GetProperty("status").GetString());
        if (accepted)
        {
            Assert.Equal(selected, record.RootElement.GetProperty("target").GetString());
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SolutionBuildChecksEveryLibraryEvenAfterFailure(bool valid)
    {
        var solution = new Solution(Compilation.CreateForTest().Kimigayo);
        var first = this.Create(valid ? "public group First" : "let broken =");
        first.Name = "First";
        first.ProjectFile.OutputKind = OutputKind.Library;
        var second = this.Create("public group Second");
        second.Name = "Second";
        second.ProjectFile.OutputKind = OutputKind.Library;
        solution.Projects.Add("first.kimiproj", first);
        solution.Projects.Add("second.kimiproj", second);
        Assert.Equal(valid, await solution.Build(TestContext.Current.CancellationToken));
        using var firstRecord = JsonDocument.Parse(File.ReadAllText(ArtifactPaths.Create(first).Record));
        using var secondRecord = JsonDocument.Parse(File.ReadAllText(ArtifactPaths.Create(second).Record));
        Assert.Equal(valid ? "checked" : "incomplete", firstRecord.RootElement.GetProperty("status").GetString());
        Assert.Equal("checked", secondRecord.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task RunRequiresBuiltUnmodifiedExecutableWithoutCreatingArtifacts()
    {
        var project = this.Create();
        var paths = ArtifactPaths.Create(project);
        await this.AssertRunRefused(project, "No built Application is available. Run 'kimi build' first.");
        Assert.False(File.Exists(paths.Ir));
        Directory.CreateDirectory(Path.GetDirectoryName(paths.Record)!);
        File.WriteAllText(paths.Executable, "changed executable");
        File.WriteAllText(paths.Record, "{\"status\":\"linked\",\"optimization\":\"O2\",\"executableSha256\":\"wrong\"}");
        await this.AssertRunRefused(project, "The latest build failed or the binary was changed. Run 'kimi build' first.");
        Assert.False(File.Exists(paths.Ir));
    }

    // PLAN G983 (form J): a build record that is not the JSON its reader expects (a field missing or of another kind, or not an object)
    // records no successful build of this executable, so the run is refused with that reason (IMPL 20.3.2), never a JSON accessor's
    // failure printed as the toolchain's.
    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"status\":7,\"optimization\":\"O2\"}")]
    [InlineData("{\"status\":\"linked\",\"optimization\":\"O2\"}")]
    public async Task RunRefusesAMalformedBuildRecord(string record)
    {
        var project = this.Create();
        var paths = ArtifactPaths.Create(project);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.Record)!);
        File.WriteAllText(paths.Executable, "executable");
        File.WriteAllText(paths.Record, record);
        await this.AssertRunRefused(project, "The latest build failed or the binary was changed. Run 'kimi build' first.");
    }

    // PLAN G1188: run's program and every descendant end with the run. Kimi is a child process, the test apphost's "run" probe
    // (TestProcessRecoveryTest.ProcessProbe), which runs RunExecutable on the apphost in the row's probe mode; the row writes Kimi's
    // input and reads its output and exit code, which are the run's: each output line the program's TEMP, each error line a process of
    // the program's tree (its id and start time), which has ended when Kimi has. "escape" leaves a descendant that holds the program's
    // output and whose parent exited first, so a kill of the program's tree misses it; once the row has read the tree's lines, the probe
    // cancels the run or ends its process as a second Ctrl+C does. On dev 43fc18f6c the descendant keeps the run waiting, so Kimi does
    // not end in a minute. "no input" and "invalid input" give Kimi no standard input, or one that is no open handle of its own, before
    // the run: the program reads end of input at once (JobProcess's closed input), never its hidden console's input; on 8653cc017 it
    // read the console's and Kimi did not end in a minute.
    [Theory]
    [InlineData("exit", "x", null, 123, 0)] // The program reads Kimi's input (Tatara decision 15); its exit code is the run's (a kill's is 125).
    [InlineData("descendant", "", null, 0, 1)] // The program's output is the run's; the descendant it left ends with the run.
    [InlineData("escape", "", "cancel", 130, 2)] // A cancelled run ends though the escaped descendant holds the program's output.
    [InlineData("escape", "", "end", 130, 2)] // Kimi's end (Program.EndProcess) ends the program and its descendant.
    [InlineData("exit", "", "no input", 0, 1)] // A program run while Kimi has no standard input reads end of input.
    [InlineData("exit", "", "invalid input", 0, 1)] // So does one run while Kimi's standard input is no open handle of its own.
    public async Task ARunsProgramAndItsDescendantsEndWithTheRun(string program, string input, string? action, int exitCode, int lines)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The probe runs on Windows.");
        var name = "kimi-run-" + Guid.NewGuid().ToString("N");
        using var ready = new EventWaitHandle(initialState: false, EventResetMode.ManualReset, name + "-ready");
        using var go = new EventWaitHandle(initialState: false, EventResetMode.ManualReset, name + "-go");
        var start = new ProcessStartInfo(Path.ChangeExtension(typeof(NativeToolchainTest).Assembly.Location, ".exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = this.directory,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.Environment["KIMI_TEST_PROCESS_PROBE"] = "run";
        start.Environment["KIMI_TEST_PROCESS_PROGRAM"] = program;
        start.Environment["KIMI_TEST_PROCESS_READY"] = name + "-ready";
        start.Environment["KIMI_TEST_PROCESS_GO"] = name + "-go";
        if (action is not null)
        {
            start.Environment["KIMI_TEST_PROCESS_ACTION"] = action;
        }

        // A printed process is held from its line on when its id still names it (TestProcessRecoveryTest.TryIdentify), so only the
        // run's own processes are awaited and, should one outlive the row, killed; one that already ended is not held.
        var processes = program switch { "descendant" => 1, "escape" => 3, _ => 0 };
        var printed = new List<string>();
        var other = new List<string>();
        var held = new List<Process>();
        var ids = 0;
        var ended = false;
        using var kimi = new Process { StartInfo = start };
        kimi.OutputDataReceived += (_, e) => Read(e.Data, error: false);
        kimi.ErrorDataReceived += (_, e) => Read(e.Data, error: true);
        kimi.Start();
        kimi.BeginOutputReadLine();
        kimi.BeginErrorReadLine();
        await kimi.StandardInput.WriteAsync(input);
        kimi.StandardInput.Close();
        try
        {
            await kimi.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
            Assert.Equal(exitCode, kimi.ExitCode);
            Assert.Equal(Enumerable.Repeat(Environment.GetEnvironmentVariable("TEMP")!, lines), printed);
            Assert.Empty(other);
            Assert.Equal(processes, ids);
            Assert.All(held, static x => Assert.True(x.WaitForExit(TimeSpan.FromSeconds(10)), $"Process {x.Id} is still running."));
        }
        finally
        {
            lock (held)
            {
                ended = true; // No later line reads the row's state.
                foreach (var process in held.Append(kimi).Where(static x => !x.HasExited))
                {
                    process.Kill();
                    process.WaitForExit(TimeSpan.FromSeconds(10)); // Before Dispose removes the processes' working directory.
                }

                held.ForEach(static x => x.Dispose());
            }
        }

        void Read(string? line, bool error)
        {
            lock (held)
            {
                if (line is null || ended)
                {
                    return;
                }
                else if (!error)
                {
                    printed.Add(line);
                }
                else if (!TestProcessRecoveryTest.TryIdentify(line, out var process))
                {
                    other.Add(line);
                }
                else
                {
                    ids++;
                    if (process is not null)
                    {
                        held.Add(process);
                    }
                }

                if (ids == processes && printed.Count == lines)
                {
                    go.Set();
                }
            }
        }
    }

    // The toolchain boundary's known limit closes (R9-M10, PLAN G983): inside the toolchain's call, an exception the toolchain does not
    // raise as its failure, here the host's console throwing InvalidOperationException at the native build's "Built: " report, ends the
    // build's own result as Faulted (one CheckFaulted_Kd at the project file), never the toolchain's message. The failed attempt (v2,
    // after a successful v1) publishes nothing (IMPL 20.3.2; PLAN G985: the record was linked before the report; G1045: v2's executable
    // was in place before it), so run --no-build refuses v1's executable. The builds use the installed toolchain.
    [Fact]
    public async Task AFailingBuiltReportFaultsTheBuildAndLeavesNoSuccessfulRecord()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var (_, _, v1) = await this.BuildHello();
        this.console.LineAction = ("Built: ", DiagnosticContractTest.DiagnosticConsole.Fail);
        var project = this.Hello("Hi");
        Assert.False(await project.Build(TestContext.Current.CancellationToken), this.console.Text);
        Assert.Null(this.console.LineAction);
        Assert.Equal(1, this.console.Text.Split(nameof(DiagnosticCode.CheckFaulted_Kd)).Length - 1);
        Assert.Contains(DiagnosticFaults.Describe(DiagnosticFault.Exception), this.console.Text, StringComparison.Ordinal);
        Assert.Contains("Hello.kimiproj", this.console.Text, StringComparison.Ordinal);
        await this.AssertNothingPublished(project, v1);
    }

    // G1045 (IMPL 20.3.2: an Application publishes its executable only on success; Tatara decision 18): the build writes the success
    // record, naming the private file's hash, and then moves the executable into place. After a successful v1, a v2 cancelled at its
    // "Built: " report, whose success record cannot be written (the record held without write sharing at the report) or whose
    // executable cannot be moved into place (the executable held without delete sharing at the report; the record is invalidated again)
    // publishes nothing, so run --no-build refuses v1's executable. The builds use the installed toolchain.
    [Theory]
    [InlineData("cancel")]
    [InlineData("record")]
    [InlineData("executable")]
    public async Task AFailureAtTheCommitPublishesNoExecutable(string point)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var (_, paths, v1) = await this.BuildHello();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        FileStream? held = null;
        this.console.LineAction = ("Built: ", () =>
        {
            if (point == "cancel")
            {
                cancellation.Cancel();
            }
            else
            {
                held = new FileStream(point == "record" ? paths.Record : paths.Executable, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
        });
        var project = this.Hello("Hi");
        try
        {
            if (point == "cancel")
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => project.Build(cancellation.Token));
            }
            else
            {
                Assert.False(await project.Build(cancellation.Token), this.console.Text);
            }
        }
        finally
        {
            held?.Dispose();
        }

        Assert.Null(this.console.LineAction);
        await this.AssertNothingPublished(project, v1);
    }

    // The output's lock (IMPL 20.3.2; NativeToolchain.Begin): while another build holds the lock of a build's record
    // (Name.link.build.lock, which the test holds without sharing), the build waits and reports it once, and builds after the release;
    // a cancellation ends the wait, and the record (v1's success) and the executable stay as they were. O2 and O0 share the record, so
    // O0 of a project whose O2 record exists waits; build --Manifest waits for the lock of the source build's record. Each row first
    // builds Hello at O2 (v1) on the installed toolchain.
    [Theory]
    [InlineData("O2", "release")]
    [InlineData("O2", "cancel")]
    [InlineData("O0", "release")]
    [InlineData("manifest", "release")]
    public async Task ABuildWaitsForTheLockOfItsRecord(string build, string action)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var (project, paths, v1) = await this.BuildHello();
        var record = File.ReadAllText(paths.Record);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var lockPath = Path.ChangeExtension(paths.Record, ".lock");
        using var held = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Action release = action == "release" ? held.Dispose : cancellation.Cancel;
        this.console.LineAction = ("Waiting for another build: " + lockPath, release);
        project.ProjectFile.Optimization = build == "O0" ? "O0" : "O2";
        var built = build == "manifest"
            ? NativeToolchain.BuildManifest(paths.Manifest, (_, message) => this.console.WriteLine(message), cancellation.Token)
            : project.Build(cancellation.Token);
        if (action == "cancel")
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => built);
            Assert.Equal(record, File.ReadAllText(paths.Record));
            Assert.Equal(v1, ArtifactFiles.Hash(paths.Executable));
        }
        else
        {
            await built; // A failed source build returns false, and its record is not linked.
            using var linked = JsonDocument.Parse(File.ReadAllText(paths.Record));
            Assert.Equal("linked", linked.RootElement.GetProperty("status").GetString());
            Assert.Equal(project.ProjectFile.Optimization, linked.RootElement.GetProperty("optimization").GetString());
        }

        Assert.Null(this.console.LineAction);
        Assert.Equal(1, this.console.Text.Split("Waiting for another build: ").Length - 1);
    }

    // A build that cannot begin (NativeToolchain.Begin; PLAN G1205, O-491 (d)): after a successful v1, a v2 whose first invalidation
    // cannot replace the record (read-only, or held without delete sharing) or whose lock cannot be opened (read-only) fails saying that
    // the build could not begin and that its record, which it names, keeps its last state; the record is v1's success (IMPL 20.3.2: "the
    // earlier success when the first invalidation fails"), so run --no-build runs v1's executable. On the installed toolchain.
    [Theory]
    [InlineData("record read-only")]
    [InlineData("record held")]
    [InlineData("lock read-only")]
    public async Task ABuildThatCannotBeginNamesItsRecordWhichKeepsItsLastState(string row)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var (_, paths, v1) = await this.BuildHello();
        var record = File.ReadAllText(paths.Record);
        var file = row == "lock read-only" ? Path.ChangeExtension(paths.Record, ".lock") : paths.Record;
        var project = this.Hello("Hi");
        using var held = row == "record held" ? new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
        if (held is null)
        {
            File.SetAttributes(file, FileAttributes.ReadOnly);
        }

        try
        {
            Assert.False(await project.Build(TestContext.Current.CancellationToken), this.console.Text);
        }
        finally
        {
            held?.Dispose();
            File.SetAttributes(file, FileAttributes.Normal); // Before Dispose deletes the directory.
        }

        Assert.Contains($"The build could not begin, so its record keeps its last state: {paths.Record}. ", this.console.Text, StringComparison.Ordinal);
        Assert.Equal(record, File.ReadAllText(paths.Record));
        Assert.Equal(v1, ArtifactFiles.Hash(paths.Executable));
        Assert.Equal(0, await project.Run(TestContext.Current.CancellationToken));
    }

    // A build waits only while another build holds its record's lock (NativeToolchain.Begin; C-433 B2, N6): after a build in this process
    // whose invalidation failed (its record's path a directory, an output failure: the lock was closed again), and while another
    // output's lock is held, Hello builds with no waiting line (a wait would fail the build at the console instead of hanging the row).
    // On the installed toolchain.
    [Theory]
    [InlineData("failed invalidation")]
    [InlineData("another output")]
    public async Task ABuildWaitsForNoLockButItsHeldOne(string row)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var project = this.Hello("Hello");
        var paths = ArtifactPaths.Create(project);
        var output = Directory.CreateDirectory(Path.GetDirectoryName(paths.Record)!).FullName;
        using var other = row == "another output"
            ? new FileStream(Path.Combine(output, "Other.link.build.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)
            : null;
        this.console.LineAction = ("Waiting for another build: ", DiagnosticContractTest.DiagnosticConsole.Fail);
        if (row == "failed invalidation")
        {
            Directory.CreateDirectory(paths.Record);
            Assert.False(await project.Build(TestContext.Current.CancellationToken), this.console.Text);
            Assert.Equal(0, this.console.Text.Split(nameof(DiagnosticCode.CheckFaulted_Kd)).Length - 1);
            Directory.Delete(paths.Record);
        }

        Assert.True(await project.Build(TestContext.Current.CancellationToken), this.console.Text);
        Assert.NotNull(this.console.LineAction);
    }

    // IMPL 20.4 (Tatara B6a): a build stages the compiler's supplies as it stages a program's input and links those snapshots. The
    // record's libraries rows name the staged files (<stem>.native/<hash>.lib) with the catalog's hashes, and it has no kernel32 member;
    // a second build, after the staged kernel32 file was overwritten, replaces it and links the first build's executable. On the
    // installed toolchain.
    [Fact]
    public async Task ABuildLinksTheCompilersSuppliesAsStagedSnapshots()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var (project, paths, v1) = await this.BuildHello();
        var staged = ToolchainIdentity.Supplies.Select(x => (x.Supply.Name, Path: Path.Combine(paths.Stem + ".native", x.Sha256 + ".lib"), x.Sha256)).ToArray();
        using (var record = JsonDocument.Parse(File.ReadAllText(paths.Record)))
        {
            Assert.False(record.RootElement.TryGetProperty("kernel32", out _));
            Assert.Equal(
                staged.Select(x => x.Name + " " + NativeToolchain.Redact(x.Path, this.directory) + " " + x.Sha256),
                record.RootElement.GetProperty("libraries").EnumerateArray().Select(static x =>
                    x.GetProperty("name").GetString() + " " + x.GetProperty("path").GetString() + " " + x.GetProperty("sha256").GetString()));
        }

        Assert.All(staged, static x => Assert.Equal(x.Sha256, ArtifactFiles.Hash(x.Path)));
        File.WriteAllText(staged[0].Path, "overwritten");
        Assert.True(await project.Build(TestContext.Current.CancellationToken), this.console.Text);
        Assert.Equal(staged[0].Sha256, ArtifactFiles.Hash(staged[0].Path));
        Assert.Equal(v1, ArtifactFiles.Hash(paths.Executable));
    }

    // IMPL 20.4 "its hash assertions are checked" (Tatara B6a): a compiler's supply's manifest entry may assert a SHA-256 as a program's
    // may, and the build checks it against the staged bytes: the catalog's hash builds, another fails before the link. On the installed
    // toolchain.
    [Theory]
    [InlineData("kernel32", true)]
    [InlineData("kernel32", false)]
    [InlineData("kimi_backend", true)]
    [InlineData("kimi_backend", false)]
    public async Task ACompilerSupplysHashAssertionIsChecked(string name, bool adopted)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var project = this.Create();
        Assert.True(await project.Generate(TestContext.Current.CancellationToken));
        var paths = ArtifactPaths.Create(project);
        var manifest = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(paths.Manifest))!.AsObject();
        var entry = manifest["libraries"]!.AsArray().Single(x => x!["name"]!.GetValue<string>() == name)!;
        entry["sha256"] = adopted ? Array.Find(ToolchainIdentity.Supplies, x => x.Supply.Name == name)!.Sha256 : new string('0', 64);
        File.WriteAllText(paths.Manifest, manifest.ToJsonString());
        var build = NativeToolchain.BuildManifest(paths.Manifest, (_, _) => { }, TestContext.Current.CancellationToken);
        if (adopted)
        {
            await build;
            Assert.True(File.Exists(paths.Executable));
        }
        else
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => build);
            Assert.Equal($"Native library '{name}' does not match its manifest Sha256 assertion.", error.Message);
            Assert.False(File.Exists(paths.Executable));
        }
    }

    [Fact]
    public async Task EmptySolutionFailsBuildAndEmitAndCannotRun()
    {
        var solution = new Solution(Compilation.CreateForTest().Kimigayo);
        Assert.False(await solution.Generate(TestContext.Current.CancellationToken));
        Assert.False(await solution.Build(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<CommandInputException>(() => solution.Run(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(OutputKind.Application)]
    [InlineData(OutputKind.Library)]
    public async Task UnsupportedTargetCannotEmitAndCancelledBuildCannotSucceed(OutputKind kind)
    {
        var project = this.Create();
        project.ProjectFile.OutputKind = kind;
        project.KimiOptions.Target = "aarch64-pc-windows-msvc";
        Assert.False(await project.Generate(TestContext.Current.CancellationToken));
        project.KimiOptions.Target = string.Empty;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => project.Build(cancelled.Token));
        using var record = JsonDocument.Parse(File.ReadAllText(ArtifactPaths.Create(project).Record));
        Assert.Equal("incomplete", record.RootElement.GetProperty("status").GetString());
    }

    // The toolchain boundary (R9-B2): under Project.Build the toolchain's classification applies only around the toolchain's own calls,
    // whose failures fail the build with their message (IMPL 20.3; SPEC 23.2.2: a toolchain, output or configuration failure); any other
    // exception, a compiler step's outside generation among them, ends its result as Faulted: one CheckFaulted_Kd (Exception) at the
    // project file (DIAGNOSTICS rule 9). Rows, the expected record first:
    // - CheckFaulted_Kd, the project's own result (BuildTargetsAndPublish): "project checks", which read a null target list, a caller
    //   contract no command input reaches (it escaped the build); "report", the host's console throwing InvalidOperationException, a type
    //   the toolchain's JSON reads throw, at the report of the published pair, after generation and before the toolchain (it printed as a
    //   toolchain failure).
    // - CheckFaulted_Kd, the build's own result (Build's outer catch): "toolchain's message", the host's console throwing at the line that
    //   renders a toolchain failure, outside the toolchain's call (it escaped the build).
    // - The toolchain's message and no code: a program's supply whose bytes do not match its Sha256 assertion (the native build's
    //   supply check), a configuration value the artifact paths read.
    // The supply's two rows skip off Windows, where the native build's platform check comes first (PLAN G1174); Tatara's load failure is
    // test-cli.ps1's row (a test host holds its one instance). No production seam: the console is the host's, and the project file's
    // values are the caller's.
    [Theory]
    [InlineData("project checks", null, null)]
    [InlineData("report", "Generated LLVM inputs", null)]
    [InlineData("toolchain's message", "Native library 'codec' does not match", null)]
    [InlineData("toolchain", null, "Native library 'codec' does not match its NativeLibraries Kind or Sha256 assertion.")]
    [InlineData("configuration", null, "Optimization must be O0 or O2.")]
    public async Task ABuildFailsByTheToolchainBoundarysRule(string origin, string? failingLine, string? toolchainFailure)
    {
        var supply = origin is "toolchain's message" or "toolchain";
        Assert.SkipUnless(OperatingSystem.IsWindows() || !supply, Windows);
        this.console.LineAction = failingLine is null ? null : (failingLine, DiagnosticContractTest.DiagnosticConsole.Fail);
        var project = this.Hello("Hello");
        if (origin == "project checks")
        {
            project.ProjectFile.Targets = null!;
        }
        else if (origin == "configuration")
        {
            project.ProjectFile.Optimization = "O3";
        }
        else if (supply)
        {
            File.WriteAllText(Path.Combine(this.directory, "main.kimi"), "group Native\n    #LibraryImport(\"codec\", \"f\")\n    public unsafe func f() -> i32\npublic func main()\n    var x: i32 = 0\n    unsafe => x = Native.f()\n");
            Directory.CreateDirectory(Path.Combine(this.directory, "native"));
            File.WriteAllText(Path.Combine(this.directory, "native", "codec.lib"), "not the asserted bytes");
            project.ProjectFile.NativeLibraries[WindowsProfile.Target] = new() { ["codec"] = new() { Kind = "static", Input = "native/codec.lib", Sha256 = new string('0', 64) } };
        }

        Assert.False(await project.Build(TestContext.Current.CancellationToken), this.console.Text);
        Assert.Equal(toolchainFailure is null ? 1 : 0, this.console.Text.Split(nameof(DiagnosticCode.CheckFaulted_Kd)).Length - 1);
        if (toolchainFailure is null)
        {
            Assert.Contains(DiagnosticFaults.Describe(DiagnosticFault.Exception), this.console.Text, StringComparison.Ordinal);
            Assert.Contains("Hello.kimiproj", this.console.Text, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains(toolchainFailure, this.console.Text, StringComparison.Ordinal);
        }

        Assert.False(File.Exists(Path.Combine(this.directory, "out", "Hello.O2.exe")));
    }

    // The toolchain refuses the run before launch with this reason (IMPL 20.3, 20.3.2): exit 1, and the refusal, not another failure.
    private async Task AssertRunRefused(Project project, string reason)
    {
        Assert.Equal(1, await project.Run(TestContext.Current.CancellationToken));
        Assert.Contains(reason, this.console.Text, StringComparison.Ordinal);
    }

    // After a v2 that failed or was cancelled, past a successful v1: the executable is v1's and the only one in its directory (no private
    // file remains), the record is incomplete and run --no-build refuses the executable.
    private async Task AssertNothingPublished(Project project, string v1)
    {
        var paths = ArtifactPaths.Create(project);
        Assert.Equal(v1, ArtifactFiles.Hash(paths.Executable));
        Assert.Equal(paths.Executable, Assert.Single(Directory.GetFiles(Path.GetDirectoryName(paths.Executable)!, "*.exe")));
        using (var record = JsonDocument.Parse(File.ReadAllText(paths.Record)))
        {
            Assert.Equal("incomplete", record.RootElement.GetProperty("status").GetString());
        }

        await this.AssertRunRefused(project, "The latest build failed or the binary was changed. Run 'kimi build' first.");
    }

    // v1: Hello built at O2 on the installed toolchain, with its paths and its executable's hash.
    private async Task<(Project Project, ArtifactPaths Paths, string Executable)> BuildHello()
    {
        var project = this.Hello("Hello");
        Assert.True(await project.Build(TestContext.Current.CancellationToken), this.console.Text);
        var paths = ArtifactPaths.Create(project);
        return (project, paths, ArtifactFiles.Hash(paths.Executable));
    }

    // Hello.kimiproj at O2 into out/Hello.ll, its main.kimi writing the text, on the test's console.
    private Project Hello(string text)
    {
        var file = Path.Combine(this.directory, "Hello.kimiproj");
        File.WriteAllText(file, $"Targets={{\"{WindowsProfile.Target}\"}} OutputPath=\"out/Hello.ll\"");
        File.WriteAllText(Path.Combine(this.directory, "main.kimi"), $"::Kimi.Console.writeLine(\"{text}\")");
        Assert.True(Project.TryCreate(new Kimigayo(this.console), null, file, out var project));
        return project;
    }

    private Project Create(string source = "::Kimi.Console.writeLine(\"Hello\")")
    {
        var project = new Project(new Kimigayo(this.console));
        project.Name = "Native test";
        project.Directory = this.directory;
        project.AddSource("main.kimi", source);
        return project;
    }
}
