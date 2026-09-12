// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public sealed class EmissionArtifactsTest : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "kimi-emission-" + Guid.NewGuid().ToString("N"));

    public EmissionArtifactsTest() => Directory.CreateDirectory(this.directory);

    public void Dispose() => Directory.Delete(this.directory, true);

    [Fact]
    public void PublishesMatchedPairAndRebasesConfiguredPaths()
    {
        var c = this.Create();
        c.Project.ProjectFile.LlvmBin = "tools/LLVM bin";
        c.Project.ProjectFile.NativeLibraries[WindowsProfile.Target] = new(StringComparer.Ordinal)
        {
            ["kernel32"] = new() { Input = "sdk/kernel32.lib" },
            ["unused"] = new() { Input = "unused.lib" },
        };
        Assert.True(EmissionArtifacts.Publish(c, out var path, out var error), error);
        Assert.Equal(Path.Combine(this.directory, "out", "Hello.ll"), path);
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.ChangeExtension(path!, ".link.json")));
        var root = manifest.RootElement;
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path!))), root.GetProperty("irSha256").GetString());
        Assert.Equal("Hello.ll", root.GetProperty("irFile").GetString());
        Assert.Equal(WindowsProfile.BackendSha256, root.GetProperty("backendSupport").GetProperty("artifactSha256").GetString());
        var libraries = root.GetProperty("libraries");
        Assert.Equal(2, libraries.GetArrayLength());
        Assert.Equal("kernel32", libraries[0].GetProperty("name").GetString());
        Assert.Equal(Path.Combine("..", "sdk", "kernel32.lib"), libraries[0].GetProperty("input").GetString());
        Assert.Equal("kimi_backend", libraries[1].GetProperty("name").GetString());
        Assert.Equal(Path.Combine("..", "tools", "LLVM bin"), root.GetProperty("toolchain").GetProperty("llvmBin").GetString());
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(path!)!, "*.tmp"));
    }

    [Theory]
    [InlineData("O3", "out/Hello.ll")]
    [InlineData("o2", "out/Hello.ll")]
    [InlineData("O2", "out/Hello.exe")]
    [InlineData("O2", "")]
    [InlineData("O2", "-out.ll")]
    [InlineData("O2", "out/\0.ll")]
    public void InvalidSettingsCannotPublish(string optimization, string outputPath)
    {
        var c = this.Create();
        c.Project.ProjectFile.Optimization = optimization;
        c.Project.ProjectFile.OutputPath = outputPath;
        Assert.False(EmissionArtifacts.Publish(c, out var path, out var error));
        Assert.Null(path);
        Assert.NotNull(error);
        Assert.Empty(Directory.EnumerateFiles(this.directory, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("kernel32", "static", "kernel32.lib")]
    [InlineData("kimi_backend", "import", "backend.lib")]
    [InlineData("kernel32", "import", "kernel32.dll")]
    [InlineData("kernel32", "import", "/DEFAULTLIB:evil.lib")]
    [InlineData("kernel32", "import", "kernel32.lib\0")]
    [InlineData("kernel32", "import", "")]
    [InlineData("unused", "unknown", "unused.lib")]
    public void InvalidNativeInputsAreRejectedEvenWhenUnused(string name, string kind, string input)
    {
        var c = this.Create();
        c.Project.ProjectFile.NativeLibraries[WindowsProfile.Target] = new(StringComparer.Ordinal) { [name] = new() { Kind = kind, Input = input } };
        Assert.False(EmissionArtifacts.Publish(c, out var path, out var error));
        Assert.Null(path);
        Assert.NotNull(error);
    }

    [Fact]
    public void BackendHashMustMatchTheEmbeddedCatalog()
    {
        var c = this.Create();
        var archive = Path.Combine(this.directory, "fake.lib");
        File.WriteAllText(archive, "not the adopted archive");
        c.Project.ProjectFile.NativeLibraries[WindowsProfile.Target] = new(StringComparer.Ordinal)
        {
            ["kimi_backend"] = new() { Kind = "static", Input = archive },
        };
        Assert.False(EmissionArtifacts.Publish(c, out _, out var error));
        Assert.Contains("SHA-256", error);
    }

    [Fact]
    public void ManifestPublicationFailureDoesNotClaimSuccessOrLeaveTemporaryFiles()
    {
        var c = this.Create();
        var output = Path.Combine(this.directory, "out");
        Directory.CreateDirectory(Path.Combine(output, "Hello.link.json"));
        File.WriteAllText(Path.Combine(output, "Hello.ll"), "old IR");
        Assert.False(EmissionArtifacts.Publish(c, out var path, out var error));
        Assert.Null(path);
        Assert.NotNull(error);
        Assert.Empty(Directory.EnumerateFiles(output, "*.tmp"));
    }

    [Fact]
    public void FailedSemanticGatePreservesOldOutputsWithoutClaimingThem()
    {
        var c = this.Create();
        Assert.True(EmissionArtifacts.Publish(c, out var path, out var error), error);
        var previous = File.ReadAllBytes(path!);
        c.Bind(); // Old ownership and startup cannot authorize another publication.
        Assert.False(EmissionArtifacts.Publish(c, out var failedPath, out _));
        Assert.Null(failedPath);
        Assert.Equal(previous, File.ReadAllBytes(path!));
    }

    [Fact]
    public async Task GenerateConnectsProjectLoadingAnalysisAndPublication()
    {
        var c = this.Create();
        c.Project.AddSource("Hello.kimi", "::Core.writeLine(\"Hello, world!\")");
        Assert.True(await c.Project.Generate());
        Assert.True(File.Exists(Path.Combine(this.directory, "out", "Hello.link.json")));
    }

    [Fact]
    public async Task CorrectingSettingsAllowsANewGenerationAttempt()
    {
        var c = this.Create();
        c.Project.AddSource("Hello.kimi", "::Core.writeLine(\"Hello, world!\")");
        c.Project.ProjectFile.Optimization = "O3";
        Assert.False(await c.Project.Generate());
        c.Project.ProjectFile.Optimization = "O2";
        Assert.True(await c.Project.Generate());
    }

    [Fact]
    public void LogicalSourcePathsAreIndependentOfCheckoutDirectory()
    {
        string Emit(string checkout)
        {
            var c = MinimalEmissionTest.Analyze("writeLine(\"日本語\")", Path.Combine(checkout, "Hello.kimi"));
            c.Project.Directory = checkout;
            using var writer = new StringWriter();
            Assert.True(c.Emission.WriteIr(writer, out var error), error);
            return writer.ToString();
        }

        Assert.Equal(Emit(Path.Combine(this.directory, "first")), Emit(Path.Combine(this.directory, "second")));
    }

    [Fact]
    public void DiagnosticErrorStateTracksRemovalAndClear()
    {
        var diagnostics = Compilation.CreateForTest().Kotonoha.DiagnosticCollection;
        Assert.False(diagnostics.HasErrors);
        diagnostics.Add(default, DiagnosticCode.GenerationFailed_Kd, "test");
        Assert.True(diagnostics.HasErrors);
        Assert.True(diagnostics.Remove(0));
        Assert.False(diagnostics.HasErrors);
        diagnostics.Add(default, DiagnosticCode.GenerationFailed_Kd, "test");
        diagnostics.ClearDiagnostic();
        Assert.False(diagnostics.HasErrors);
    }

    private Compilation Create()
    {
        var c = MinimalEmissionTest.Analyze("::Core.writeLine(\"Hello, world!\")");
        c.Project.Directory = this.directory;
        c.Project.Name = "Hello";
        c.Project.ProjectFile.OutputPath = "out/Hello.ll";
        return c;
    }
}
