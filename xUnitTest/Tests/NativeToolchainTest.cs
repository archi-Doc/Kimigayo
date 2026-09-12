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
        var props = XDocument.Load(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Directory.Build.props")));
        var expected = props.Descendants().Single(x => x.Name.LocalName == "Version").Value;
        Assert.Equal(expected, CompilerRelease.Version);
        Assert.Equal(expected, WindowsProfile.BackendVersion);
        Assert.StartsWith(expected + " (", Compilation.CompilerVersion, StringComparison.Ordinal);
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
    public async Task SemanticFailureInvalidatesPreviousBuildBeforeInvokingTools()
    {
        var project = this.Create("let broken =");
        var paths = ArtifactPaths.Create(project);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.Record)!);
        File.WriteAllText(paths.Record, "{\"status\":\"linked\"}");
        Assert.False(await project.Build(TestContext.Current.CancellationToken));
        using var record = JsonDocument.Parse(File.ReadAllText(paths.Record));
        Assert.Equal("incomplete", record.RootElement.GetProperty("status").GetString());
        Assert.False(File.Exists(paths.Ir));
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

    [Fact]
    public async Task UnsupportedTargetCannotEmitAndCancelledBuildCannotSucceed()
    {
        var project = this.Create();
        project.KimiOptions.Target = "aarch64-pc-windows-msvc";
        Assert.False(await project.Generate(TestContext.Current.CancellationToken));
        project.KimiOptions.Target = string.Empty;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => project.Build(cancelled.Token));
    }

    private Project Create(string source = "::Core.writeLine(\"Hello\")")
    {
        var project = Compilation.CreateForTest().Project;
        project.Name = "Native test";
        project.Directory = this.directory;
        project.AddSource("main.kimi", source);
        return project;
    }
}
