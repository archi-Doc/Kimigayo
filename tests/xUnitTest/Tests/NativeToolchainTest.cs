// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

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
        var staged = NativeToolchain.StageInput(input, Path.Combine(this.directory, "output"), out var hash);
        Assert.Equal(ArtifactFiles.Hash(input), hash);
        File.WriteAllText(input, "second");
        Assert.Equal("first", File.ReadAllText(staged));
        Assert.Equal(hash, ArtifactFiles.Hash(staged));
        File.WriteAllText(input, "first");
        File.WriteAllText(staged, "damaged staged copy");
        Assert.Equal(staged, NativeToolchain.StageInput(input, Path.Combine(this.directory, "output"), out var repairedHash));
        Assert.Equal(hash, repairedHash);
        Assert.Equal("first", File.ReadAllText(staged));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(staged)!, "*.tmp"));
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
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => NativeToolchain.BuildManifest(paths.Manifest, this.directory, (_, _) => { }, TestContext.Current.CancellationToken));
        Assert.Contains("IR/manifest SHA-256", error.Message);
        using var record = JsonDocument.Parse(File.ReadAllText(paths.Record));
        Assert.Equal("incomplete", record.RootElement.GetProperty("status").GetString());
        Assert.Equal("previous executable", File.ReadAllText(paths.Executable));
    }

    // R9-B1 part 2 (form J, PLAN G983's manifest share): a manifest field that is missing, of another JSON kind or, for an Int32 row,
    // another number fails at its read with InvalidDataException naming the field, before Tatara is loaded and with the record left
    // incomplete; a fixed field with another value is not this compiler's. Each row edits one field of an emitted Application's
    // manifest (a JSON value, or null to remove it). The libraries are read after Tatara is loaded, so their rows need a toolchain.
    [Theory]
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
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => NativeToolchain.BuildManifest(paths.Manifest, this.directory, (_, _) => { }, TestContext.Current.CancellationToken));
        Assert.Contains(failure, error.Message);
        using var record = JsonDocument.Parse(File.ReadAllText(paths.Record));
        Assert.Equal("incomplete", record.RootElement.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("LLVM version 22.1.8", "22.1.8")]
    [InlineData("clang version 22.1.9 (release)", "22.1.9")]
    [InlineData("LLD 22.1.8", "22.1.8")]
    [InlineData("Vendor LLVM version 22.1.8git", "22.1.8git")]
    [InlineData("LLVM version 22.1.8-rc1", "22.1.8-rc1")]
    [InlineData("LLVM version 22.1.80\ninstalled under 22.1.8", "22.1.80")]
    public void ParsesActualBannerWithoutLosingSuffix(string output, string expected)
        => Assert.Equal(expected, NativeToolchain.ParseVersion(output));

    [Theory]
    [InlineData("")]
    [InlineData("installed under C:/LLVM-22.1.8/bin")]
    [InlineData("LLVM version 22.1.8\nclang version 22.1.9")]
    public void RejectsUnknownOrAmbiguousVersions(string output)
        => Assert.Throws<InvalidDataException>(() => NativeToolchain.ParseVersion(output));

    [Fact]
    public void CompilerAndBackendUseBuildPropsRelease()
    {
        var props = XDocument.Load(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Directory.Build.props")));
        var expected = props.Descendants().Single(x => x.Name.LocalName == "Version").Value;
        Assert.Equal(expected, CompilerRelease.Version);
        Assert.Equal(expected, ToolchainIdentity.BackendVersion);
        Assert.StartsWith(expected + " (", Compilation.CompilerVersion, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplicitVerificationRetainsFailureWithoutInstallingAnything()
    {
        var report = Path.Combine(this.directory, "evidence", "toolchain.json");
        await Assert.ThrowsAsync<FileNotFoundException>(() => NativeToolchain.Verify(this.directory, null, report, (_, _) => { }, TestContext.Current.CancellationToken));
        using var record = JsonDocument.Parse(File.ReadAllText(report));
        Assert.Equal("incomplete", record.RootElement.GetProperty("status").GetString());
        Assert.Equal("failed", record.RootElement.GetProperty("toolchainVerification").GetString());
        Assert.Equal(JsonValueKind.Null, record.RootElement.GetProperty("reportedVersionsMatched").ValueKind);
        Assert.True(record.RootElement.GetProperty("unverifiedToolchain").GetBoolean());
        Assert.Contains("setup.ps1", record.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(this.directory, "windows_x64")));
    }

    // A root without tatara.dll: emit does not need it, and the build fails before any step, leaving the previous executable and no
    // successful record.
    [Fact]
    public async Task EmitDoesNotRequireTataraButBuildDoesAndInvalidatesPreviousSuccess()
    {
        var project = this.Create();
        project.KimiOptions.ToolchainRoot = Path.Combine(this.directory, "toolchain");
        Assert.True(await project.Generate(TestContext.Current.CancellationToken));
        var paths = ArtifactPaths.Create(project);
        Assert.True(File.Exists(paths.Ir));
        Assert.False(File.Exists(paths.Record));
        File.WriteAllText(paths.Executable, "previous executable");
        File.WriteAllText(paths.Record, "{\"status\":\"linked\"}");
        Assert.False(await project.Build(TestContext.Current.CancellationToken));
        using var record = JsonDocument.Parse(File.ReadAllText(paths.Record));
        Assert.Equal("incomplete", record.RootElement.GetProperty("status").GetString());
        Assert.Equal("previous executable", File.ReadAllText(paths.Executable));
        await this.AssertRunRefused(project, "The latest build failed or the binary was changed. Run 'kimi build' first.");
    }

    [Fact]
    public async Task LibraryBuildRecordsProductChecksWithoutEmissionOrTools()
    {
        var file = Path.Combine(this.directory, "Library.kimiproj");
        File.WriteAllText(file, "Targets = { \"x86_64-pc-windows-msvc\" }\nOutputKind = \"Library\"\nTestSources = { \"tests.kimi\" }");
        File.WriteAllText(Path.Combine(this.directory, "product.kimi"), "public group Example");
        File.WriteAllText(Path.Combine(this.directory, "tests.kimi"), "let broken =");
        Assert.True(Project.TryCreate(new Kimigayo(this.console), null, file, out var project));
        project.ProjectFile.LlvmBin = "missing LLVM directory";
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
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => NativeToolchain.BuildManifest(paths.Manifest, this.directory, (_, _) => { }, TestContext.Current.CancellationToken));
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

    // PLAN G983 (form J; R9-B2's O3 for toolchain verify): an installation record that is not the JSON verification expects (a field
    // missing or of another kind, tatara's sha256 among them, a number that is not an Int32, not an object) is an invalid installation,
    // InvalidDataException with its repair, kept in the report, before any tool; a tool's entry missing or of another kind is that tool's
    // mismatch, after its identity. Each row writes the record into an empty toolchain root; a tool's row verifies the installed tools
    // against it.
    [Theory]
    [InlineData("{\"profile\":\"windows-x64-v1\",\"tools\":{},\"supportingDlls\":{},\"tatara\":{\"sha256\":\"\"}}", null)]
    [InlineData("{\"schemaVersion\":\"1\",\"profile\":\"windows-x64-v1\",\"tools\":{},\"supportingDlls\":{},\"tatara\":{\"sha256\":\"\"}}", null)]
    [InlineData("{\"schemaVersion\":3.5,\"profile\":\"windows-x64-v1\",\"tools\":{},\"supportingDlls\":{},\"tatara\":{\"sha256\":\"\"}}", null)]
    [InlineData("{\"schemaVersion\":1,\"profile\":1,\"tools\":{},\"supportingDlls\":{},\"tatara\":{\"sha256\":\"\"}}", null)]
    [InlineData("{\"schemaVersion\":1,\"profile\":\"windows-x64-v1\",\"tools\":[],\"supportingDlls\":{},\"tatara\":{\"sha256\":\"\"}}", null)]
    [InlineData("{\"schemaVersion\":1,\"profile\":\"windows-x64-v1\",\"tools\":{},\"tatara\":{\"sha256\":\"\"}}", null)]
    [InlineData("{\"schemaVersion\":1,\"profile\":\"windows-x64-v1\",\"tools\":{},\"supportingDlls\":{}}", null)]
    [InlineData("{\"schemaVersion\":1,\"profile\":\"windows-x64-v1\",\"tools\":{},\"supportingDlls\":{},\"tatara\":{\"sha256\":7}}", null)]
    [InlineData("[]", null)]
    [InlineData("{\"schemaVersion\":1,\"profile\":\"windows-x64-v1\",\"tools\":{},\"supportingDlls\":{},\"tatara\":{\"sha256\":\"\"}}", "Tool executable SHA-256 mismatch")]
    [InlineData("{\"schemaVersion\":1,\"profile\":\"windows-x64-v1\",\"tools\":{\"clang\":7},\"supportingDlls\":{},\"tatara\":{\"sha256\":\"\"}}", "Tool executable SHA-256 mismatch")]
    public async Task VerificationRejectsAMalformedInstallationRecord(string installation, string? toolFailure)
    {
        File.WriteAllText(Path.Combine(this.directory, "installation.json"), installation);
        var bin = toolFailure is null ? null : ToolchainResolver.ResolveRoot(null);
        var report = Path.Combine(this.directory, "evidence", "toolchain.json");
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => NativeToolchain.Verify(this.directory, bin, report, (_, _) => { }, TestContext.Current.CancellationToken));
        var failure = toolFailure ?? $"Invalid toolchain installation record. Run {NativeToolchain.SetupCommand} again.";
        Assert.Contains(failure, error.Message, StringComparison.Ordinal);
        using var record = JsonDocument.Parse(File.ReadAllText(report));
        Assert.Contains(failure, record.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
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
        this.console.FailingLine = "Built: ";
        var project = this.Hello("Hi");
        Assert.False(await project.Build(TestContext.Current.CancellationToken), this.console.Text);
        Assert.Null(this.console.FailingLine);
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
    // - The toolchain's message and no code: a root without tatara.dll (the remedy names setup with -LlvmRoot), a tatara.dll that does not
    //   load (not a DLL, so the host maps nothing), a configuration value the artifact paths read.
    // No production seam: the console is the host's, and the project file's values are the caller's.
    [Theory]
    [InlineData("project checks", null, null)]
    [InlineData("report", "Generated LLVM inputs", null)]
    [InlineData("toolchain's message", "Tatara not found", null)]
    [InlineData("toolchain", null, "tatara.dll. Run src/backend/windows-x64/setup.ps1 -LlvmRoot <the LLVM")]
    [InlineData("tatara load", null, "Tatara cannot be loaded")]
    [InlineData("configuration", null, "Optimization must be O0 or O2.")]
    public async Task ABuildFailsByTheToolchainBoundarysRule(string origin, string? failingLine, string? toolchainFailure)
    {
        this.console.FailingLine = failingLine;
        var project = this.Hello("Hello");
        var root = Path.Combine(this.directory, "toolchain");
        project.KimiOptions.ToolchainRoot = root;
        if (origin == "project checks")
        {
            project.ProjectFile.Targets = null!;
        }
        else if (origin == "configuration")
        {
            project.ProjectFile.Optimization = "O3";
        }
        else if (origin == "tatara load")
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "tatara.dll"), "not a DLL");
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
