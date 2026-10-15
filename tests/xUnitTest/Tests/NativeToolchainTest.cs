// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using System.Xml.Linq;
using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public sealed class NativeToolchainTest : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "kimi-native-" + Guid.NewGuid().ToString("N"));

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
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => NativeToolchain.BuildManifest(paths.Manifest, this.directory, null, (_, _) => { }, TestContext.Current.CancellationToken));
        Assert.Contains("IR/manifest SHA-256", error.Message);
        using var record = JsonDocument.Parse(File.ReadAllText(paths.Record));
        Assert.Equal("incomplete", record.RootElement.GetProperty("status").GetString());
        Assert.Equal("previous executable", File.ReadAllText(paths.Executable));
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
        Assert.Equal(expected, WindowsProfile.BackendVersion);
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

    [Fact]
    public async Task EmitDoesNotRequireLlvmButBuildDoesAndInvalidatesPreviousSuccess()
    {
        var project = this.Create();
        project.ProjectFile.LlvmBin = "missing LLVM directory";
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
        await Assert.ThrowsAsync<InvalidDataException>(() => project.Run(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LibraryBuildRecordsProductChecksWithoutEmissionOrTools()
    {
        var file = Path.Combine(this.directory, "Library.kimiproj");
        File.WriteAllText(file, "Targets = { \"x86_64-pc-windows-msvc\" }\nOutputKind = \"Library\"\nTestSources = { \"tests.kimi\" }");
        File.WriteAllText(Path.Combine(this.directory, "product.kimi"), "public group Example");
        File.WriteAllText(Path.Combine(this.directory, "tests.kimi"), "let broken =");
        Assert.True(Project.TryCreate(Compilation.CreateForTest().Kimigayo, null, file, out var project));
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
        await Assert.ThrowsAsync<InvalidDataException>(() => project.Run(TestContext.Current.CancellationToken));
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
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => NativeToolchain.BuildManifest(paths.Manifest, this.directory, null, (_, _) => { }, TestContext.Current.CancellationToken));
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
        await Assert.ThrowsAsync<InvalidDataException>(() => project.Run(TestContext.Current.CancellationToken));
        Assert.False(File.Exists(paths.Ir));
        Directory.CreateDirectory(Path.GetDirectoryName(paths.Record)!);
        File.WriteAllText(paths.Executable, "changed executable");
        File.WriteAllText(paths.Record, "{\"status\":\"linked\",\"optimization\":\"O2\",\"executableSha256\":\"wrong\"}");
        await Assert.ThrowsAsync<InvalidDataException>(() => project.Run(TestContext.Current.CancellationToken));
        Assert.False(File.Exists(paths.Ir));
    }

    [Fact]
    public async Task EmptySolutionFailsBuildAndEmitAndCannotRun()
    {
        var solution = new Solution(Compilation.CreateForTest().Kimigayo);
        Assert.False(await solution.Generate(TestContext.Current.CancellationToken));
        Assert.False(await solution.Build(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidDataException>(() => solution.Run(TestContext.Current.CancellationToken));
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

    private Project Create(string source = "::Kimi.Console.writeLine(\"Hello\")")
    {
        var project = Compilation.CreateForTest().Project;
        project.Name = "Native test";
        project.Directory = this.directory;
        project.AddSource("main.kimi", source);
        return project;
    }
}
