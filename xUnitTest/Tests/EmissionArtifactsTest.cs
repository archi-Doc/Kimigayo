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
        c.Project.ProjectFile.NativeLibraries[WindowsProfile.Target] = new()
        {
            ["unused"] = new() { Kind = "import", Input = "unused.lib" },
            Packaged = [new() { Name = "codec", Package = new() { PackageId = "example.codec", PackageVersion = "1" }, Input = "native/codec.lib" }],
        };
        Assert.True(EmissionArtifacts.Publish(c, out var path, out var error), error);
        Assert.Equal(Path.Combine(this.directory, "out", "Hello.ll"), path);
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.ChangeExtension(path!, ".link.json")));
        var root = manifest.RootElement;
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path!))), root.GetProperty("irSha256").GetString());
        Assert.Equal("Hello.ll", root.GetProperty("irFile").GetString());
        using var catalog = JsonDocument.Parse(File.ReadAllBytes(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../backend/windows-x64/profile.json"))));
        Assert.Equal(catalog.RootElement.GetProperty("llvmVersion").GetString(), WindowsProfile.LlvmVersion);
        Assert.Equal(WindowsProfile.LlvmVersion, root.GetProperty("codegen").GetProperty("llvmVersion").GetString());
        Assert.Equal(WindowsProfile.BackendSha256, root.GetProperty("backendSupport").GetProperty("artifactSha256").GetString());
        var libraries = root.GetProperty("libraries");
        Assert.Equal(2, libraries.GetArrayLength());
        Assert.Equal("kernel32", libraries[0].GetProperty("name").GetString());
        Assert.Equal(3, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("llvm-dlltool", libraries[0].GetProperty("generator").GetString());
        Assert.Equal(Kernel32Imports.DefinitionSha256, libraries[0].GetProperty("definitionSha256").GetString());
        Assert.False(libraries[0].TryGetProperty("input", out _));
        Assert.Equal("kimi_backend", libraries[1].GetProperty("name").GetString());
        Assert.Equal("toolchain", libraries[1].GetProperty("resolution").GetString());
        Assert.False(libraries[1].TryGetProperty("input", out _));
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
    [InlineData("unused", null, "unused.lib")]
    public void InvalidNativeInputsAreRejectedEvenWhenUnused(string name, string? kind, string input)
    {
        var c = this.Create();
        c.Project.ProjectFile.NativeLibraries[WindowsProfile.Target] = new() { [name] = new() { Kind = kind, Input = input } };
        Assert.False(EmissionArtifacts.Publish(c, out var path, out var error));
        Assert.Null(path);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData(null, "codec.dll")]
    [InlineData(null, "/DEFAULTLIB:evil.lib")]
    [InlineData("static", "codec.lib")]
    public void InvalidPackageTargetedSuppliesAreRejectedEvenWhenUnused(string? kind, string input)
    {
        var c = this.Create();
        c.Project.ProjectFile.NativeLibraries[WindowsProfile.Target] = new() { Packaged = [new() { Name = "codec", Package = new() { PackageId = "example.codec", PackageVersion = "1" }, Kind = kind, Input = input }] };
        Assert.False(EmissionArtifacts.Publish(c, out var path, out var error));
        Assert.Null(path);
        Assert.NotNull(error);
    }

    [Fact]
    public void BackendSupplyExpandsItsSelfTargetedRequirement()
    {
        var c = this.Create();
        var settings = c.Project.ProjectFile;
        settings.NativeRequirements[WindowsProfile.Target] = new(StringComparer.Ordinal) { ["kimi_backend"] = new() { Kind = "static" } };
        settings.NativeLibraries[WindowsProfile.Target] = new() { ["kimi_backend"] = new() { Input = "kimi_backend.lib" } };
        Assert.True(EmissionArtifacts.Publish(c, out _, out var error), error);

        settings.NativeLibraries[WindowsProfile.Target]["kimi_backend"].Sha256 = new string('0', 64);
        Assert.False(EmissionArtifacts.Publish(c, out _, out error));
        Assert.Contains("Sha256 assertion", error);

        settings.NativeLibraries[WindowsProfile.Target]["kimi_backend"] = new() { Kind = "import", Input = "kimi_backend.lib" };
        Assert.False(EmissionArtifacts.Publish(c, out _, out error));
        Assert.Contains("disagrees", error);
    }

    [Fact]
    public void ExplicitKernel32PathRequiresMigration()
    {
        var c = this.Create();
        c.Project.ProjectFile.NativeLibraries[WindowsProfile.Target] = new()
        {
            ["kernel32"] = new() { Kind = "import", Input = "sdk/kernel32.lib" },
        };
        Assert.False(EmissionArtifacts.Publish(c, out _, out var error));
        Assert.Contains("Remove the kernel32 entry", error);
        Assert.False(Directory.Exists(Path.Combine(this.directory, "out")));
    }

    [Fact]
    public void DefaultManifestNeedsNoToolchainAndRejectsOldSchema()
    {
        var c = this.Create();
        c.Project.KimiOptions.ToolchainRoot = Path.Combine(this.directory, "absent tools");
        Assert.True(EmissionArtifacts.Publish(c, out var path, out var error), error);
        var json = File.ReadAllText(Path.ChangeExtension(path!, ".link.json"));
        using var manifest = JsonDocument.Parse(json);
        Assert.False(manifest.RootElement.TryGetProperty("toolchain", out _));
        NativeToolchain.ValidateManifest(manifest.RootElement);
        using var oldManifest = JsonDocument.Parse(json.Replace("\"schemaVersion\": 3", "\"schemaVersion\": 2", StringComparison.Ordinal));
        Assert.Contains("schema 3", Assert.Throws<InvalidDataException>(() => NativeToolchain.ValidateManifest(oldManifest.RootElement)).Message);
    }

    [Fact]
    public void BackendHashMustMatchTheEmbeddedCatalog()
    {
        var c = this.Create();
        var archive = Path.Combine(this.directory, "fake.lib");
        File.WriteAllText(archive, "not the adopted archive");
        c.Project.ProjectFile.NativeLibraries[WindowsProfile.Target] = new()
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
        c.Project.AddSource("Hello.kimi", "::Kimi.Console.writeLine(\"Hello, world!\")");
        Assert.True(await c.Project.Generate(TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Combine(this.directory, "out", "Hello.link.json")));
    }

    [Fact]
    public async Task CorrectingSettingsAllowsANewGenerationAttempt()
    {
        var c = this.Create();
        c.Project.AddSource("Hello.kimi", "::Kimi.Console.writeLine(\"Hello, world!\")");
        c.Project.ProjectFile.Optimization = "O3";
        Assert.False(await c.Project.Generate(TestContext.Current.CancellationToken));
        c.Project.ProjectFile.Optimization = "O2";
        Assert.True(await c.Project.Generate(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void LogicalSourcePathsAreIndependentOfCheckoutDirectory()
    {
        string Emit(string checkout)
        {
            var c = MinimalEmissionTest.Analyze("Console.writeLine(\"日本語\")", Path.Combine(checkout, "Hello.kimi"));
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

    [Fact]
    public void ImportedSuppliesAreListedInOrdinalOrder()
    {
        var c = this.CreateForeign(
            "group Native\n    #LibraryImport(\"zlib\", \"inflate\")\n    public unsafe func inflate(value: i32) -> i32\n" +
            "    #LibraryImport(\"codec\", \"encode\")\n    public unsafe func encode(value: i32) -> i32\n    #LibraryImport(\"codec\", \"decode\")\n    public unsafe func decode(value: i32) -> i32\n" +
            "public func main()\n    var v: i32 = 0\n    unsafe => v = Native.inflate(Native.encode(Native.decode(1)))",
            settings => settings.NativeLibraries[WindowsProfile.Target] = new()
            {
                ["zlib"] = new() { Kind = "import", Input = "zlib.lib" },
                ["codec"] = new() { Kind = "static", Input = "native/codec.lib" },
                ["unused"] = new() { Kind = "static", Input = "unused.lib" },
            });
        Assert.True(EmissionArtifacts.Publish(c, out var path, out var error), error);
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.ChangeExtension(path!, ".link.json")));
        var libraries = manifest.RootElement.GetProperty("libraries");
        Assert.Equal(["codec", "kernel32", "kimi_backend", "zlib"], libraries.EnumerateArray().Select(x => x.GetProperty("name").GetString()));
        Assert.Equal(("static", Path.Combine("..", "native", "codec.lib")), (libraries[0].GetProperty("kind").GetString(), libraries[0].GetProperty("input").GetString()));
        Assert.Equal(("import", "zlib.lib"), (libraries[3].GetProperty("kind").GetString(), libraries[3].GetProperty("input").GetString()));
        Assert.Contains("declare dllimport i32 @inflate(i32)\n", File.ReadAllText(path!));
    }

    [Fact]
    public void RequiredNameWithoutSupplyCannotPublish()
    {
        var c = this.CreateForeign(
            "group Native\n    #LibraryImport(\"codec\", \"encode\")\n    public unsafe func encode(value: i32) -> i32\npublic func main() => ()",
            settings => settings.NativeRequirements[WindowsProfile.Target] = new(StringComparer.Ordinal) { ["codec"] = new() { Kind = "static" } });
        Assert.False(EmissionArtifacts.Publish(c, out var path, out var error));
        Assert.Null(path);
        Assert.Contains("has no NativeLibraries supply", error);
        Assert.Empty(Directory.EnumerateFiles(this.directory, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void DependencyModuleSuppliesAreNotLinkedYet()
    {
        var c = ModuleBindingTest.Create(
            "public func main() => ()",
            "group Native\n    #LibraryImport(\"codec\", \"encode\")\n    public unsafe func encode(value: i32) -> i32",
            configure: (root, library) => library.NativeRequirements[WindowsProfile.Target] = new(StringComparer.Ordinal) { ["codec"] = new() { Kind = "static" } });
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        c.Ownership.Analyze();
        c.Project.Directory = this.directory;
        c.Project.Name = "Hello";
        c.Project.ProjectFile.OutputPath = "out/Hello.ll";
        Assert.False(EmissionArtifacts.Publish(c, out var path, out var error));
        Assert.Null(path);
        Assert.Contains("dependency modules", error);
        Assert.Empty(Directory.EnumerateFiles(this.directory, "*", SearchOption.AllDirectories));
    }

    private Compilation CreateForeign(string source, Action<ProjectFile> configure)
    {
        var c = Compilation.CreateForTest();
        configure(c.Project.ProjectFile);
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", source));
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Ownership.Analyze();
        c.Project.Directory = this.directory;
        c.Project.Name = "Hello";
        c.Project.ProjectFile.OutputPath = "out/Hello.ll";
        return c;
    }

    private Compilation Create()
    {
        var c = MinimalEmissionTest.Analyze("::Kimi.Console.writeLine(\"Hello, world!\")");
        c.Project.Directory = this.directory;
        c.Project.Name = "Hello";
        c.Project.ProjectFile.OutputPath = "out/Hello.ll";
        return c;
    }
}
