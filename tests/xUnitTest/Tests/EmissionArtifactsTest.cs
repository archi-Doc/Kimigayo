// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
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
        Assert.True(EmissionArtifacts.Publish(c, null, out var path), MinimalEmissionTest.Describe(c));
        Assert.Equal(Path.Combine(this.directory, "out", "Hello.ll"), path);
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.ChangeExtension(path!, ".link.json")));
        var root = manifest.RootElement;
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path!))), root.GetProperty("irSha256").GetString());
        Assert.Equal("Hello.ll", root.GetProperty("irFile").GetString());
        using var catalog = JsonDocument.Parse(File.ReadAllBytes(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/backend/windows-x64/profile.json"))));
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
        Assert.False(EmissionArtifacts.Publish(c, null, out var path));
        Assert.Null(path);
        Assert.NotEmpty(PublicationFailure(c));
        Assert.Empty(Directory.EnumerateFiles(this.directory, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("kimi_backend", "import", "backend.lib")]
    [InlineData("kernel32", "import", "kernel32.dll")]
    [InlineData("kernel32", "import", "/DEFAULTLIB:evil.lib")]
    [InlineData("kernel32", "import", "kernel32.lib\0")]
    [InlineData("kernel32", "import", "")]
    public void InvalidNativeInputsAreRejectedEvenWhenUnused(string name, string? kind, string input)
    {
        var c = this.Create();
        c.Project.ProjectFile.NativeLibraries[WindowsProfile.Target] = new() { [name] = new() { Kind = kind, Input = input } };
        Assert.False(EmissionArtifacts.Publish(c, null, out var path));
        Assert.Null(path);
        Assert.NotEmpty(PublicationFailure(c));
    }

    [Theory]
    [InlineData(null, "codec.dll")]
    [InlineData(null, "/DEFAULTLIB:evil.lib")]
    [InlineData(null, "/defaultlib:evil.lib")]
    [InlineData(null, "/WHOLEARCHIVE:evil.lib")]
    public void InvalidPackageTargetedSuppliesAreRejectedEvenWhenUnused(string? kind, string input)
    {
        var c = this.Create();
        c.Project.ProjectFile.NativeLibraries[WindowsProfile.Target] = new() { Packaged = [new() { Name = "codec", Package = new() { PackageId = "example.codec", PackageVersion = "1" }, Kind = kind, Input = input }] };
        Assert.False(EmissionArtifacts.Publish(c, null, out var path));
        Assert.Null(path);
        Assert.NotEmpty(PublicationFailure(c));
    }

    [Fact]
    public void FullyQualifiedNativePathsRemainValid()
    {
        ArtifactFiles.CheckPath(Path.Combine(this.directory, "codec.lib"));
    }

    [Fact]
    public void BackendSupplyExpandsItsSelfTargetedRequirement()
    {
        var c = this.Create();
        var settings = c.Project.ProjectFile;
        settings.NativeRequirements[WindowsProfile.Target] = new(StringComparer.Ordinal) { ["kimi_backend"] = new() { Kind = "static" } };
        settings.NativeLibraries[WindowsProfile.Target] = new() { ["kimi_backend"] = new() { Input = "kimi_backend.lib" } };
        Assert.True(EmissionArtifacts.Publish(c, null, out _), MinimalEmissionTest.Describe(c));

        settings.NativeLibraries[WindowsProfile.Target]["kimi_backend"].Sha256 = new string('0', 64);
        Assert.False(EmissionArtifacts.Publish(c, null, out _));
        Assert.Contains("Sha256 assertion", PublicationFailure(c));
    }

    [Fact]
    public void DefaultManifestNeedsNoToolchainAndRejectsOldSchema()
    {
        var c = this.Create();
        c.Project.KimiOptions.ToolchainRoot = Path.Combine(this.directory, "absent tools");
        Assert.True(EmissionArtifacts.Publish(c, null, out var path), MinimalEmissionTest.Describe(c));
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
        Assert.False(EmissionArtifacts.Publish(c, null, out _));
        Assert.Contains("SHA-256", PublicationFailure(c));
    }

    [Fact]
    public void ManifestPublicationFailureDoesNotClaimSuccessOrLeaveTemporaryFiles()
    {
        var c = this.Create();
        var output = Path.Combine(this.directory, "out");
        Directory.CreateDirectory(Path.Combine(output, "Hello.link.json"));
        File.WriteAllText(Path.Combine(output, "Hello.ll"), "old IR");
        Assert.False(EmissionArtifacts.Publish(c, null, out var path));
        Assert.Null(path);
        Assert.NotEmpty(PublicationFailure(c));
        Assert.Empty(Directory.EnumerateFiles(output, "*.tmp"));
    }

    [Fact]
    public void ReplaceWaitsOutATransientHoldOnTheDestination()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows rename semantics test.");
        var destination = Path.Combine(this.directory, "Hello.link.build.json");
        var source = destination + ".tmp";
        File.WriteAllText(destination, "old");
        File.WriteAllText(source, "new");

        // A scanner's handle on a just-published record denies an immediate replacement (verify native parallel runs).
        using var hold = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        Assert.Throws<UnauthorizedAccessException>(() => File.Move(source, destination, true));
        // Releasing the hold must not depend on the test runner's available thread-pool workers.
        var release = new Thread(() =>
        {
            Thread.Sleep(30);
            hold.Dispose();
        });
        release.Start();
        try
        {
            ArtifactFiles.Replace(source, destination);
        }
        finally
        {
            release.Join();
        }

        Assert.Equal("new", File.ReadAllText(destination));
        Assert.False(File.Exists(source));
    }

    [Fact]
    public void ReplaceReportsAPersistentHoldAndKeepsBothFiles()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows rename semantics test.");
        var destination = Path.Combine(this.directory, "Hello.link.build.json");
        var source = destination + ".tmp";
        File.WriteAllText(destination, "old");
        File.WriteAllText(source, "new");
        using (new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
        {
            Assert.Throws<UnauthorizedAccessException>(() => ArtifactFiles.Replace(source, destination));
        }

        Assert.Equal("old", File.ReadAllText(destination));
        Assert.Equal("new", File.ReadAllText(source));
    }

    [Fact]
    public void FailedSemanticGatePreservesOldOutputsWithoutClaimingThem()
    {
        var c = this.Create();
        Assert.True(EmissionArtifacts.Publish(c, null, out var path), MinimalEmissionTest.Describe(c));
        var previous = File.ReadAllBytes(path!);
        c.Bind(); // Old ownership and startup cannot authorize another publication.
        Assert.False(EmissionArtifacts.Publish(c, null, out var failedPath));
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

    // DECISIONS R5-M165: a publication failure, here an output directory a file blocks, is ArtifactPublicationFailed_Kd (Input) in the
    // Emission partition, its cause the note, at the project file; kimi build renders it there and publishes nothing.
    [Fact]
    public async Task APublicationFailureIsAnInputRecordAtTheProject()
    {
        var c = this.Create();
        var blocker = Path.Combine(this.directory, "out");
        File.WriteAllText(blocker, "a file where the output directory goes");
        Assert.False(EmissionArtifacts.Publish(c, null, out var path));
        Assert.Null(path);
        var record = Assert.Single(c.Diagnostics.Finalize(DiagnosticPartition.Emission, DiagnosticPartition.Emission).Diagnostics);
        Assert.Equal(("ArtifactPublicationFailed_Kd", DiagnosticCategory.Input, DiagnosticSeverity.Error), (record.Code, record.Category, record.Severity));
        Assert.NotEmpty(PublicationFailure(c));

        var file = Path.Combine(this.directory, "Hello.kimiproj");
        File.WriteAllText(file, $"Targets={{\"{WindowsProfile.Target}\"}} OutputPath=\"out/Hello.ll\"");
        File.WriteAllText(Path.Combine(this.directory, "Hello.kimi"), "::Kimi.Console.writeLine(\"Hello, world!\")");
        var console = new DiagnosticContractTest.DiagnosticConsole();
        Assert.True(Project.TryCreate(new Kimigayo(console), null, file, out var project));
        Assert.False(await project.Generate(TestContext.Current.CancellationToken));
        Assert.Contains("ArtifactPublicationFailed_Kd", console.Text, StringComparison.Ordinal);
        Assert.Contains("The build artifacts cannot be published", console.Text, StringComparison.Ordinal);
        Assert.Contains("Hello.kimiproj", console.Text, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(this.directory, "*.ll", SearchOption.AllDirectories));
    }

    // DECISIONS R5-M169 (G330): while code generation writes the publication's output, only the output's I/O failure is publication's
    // (ArtifactPublicationFailed_Kd with its cause); every other exception is left unclassified, the same instance, with no record, for the
    // command's fault path. A writer throws each kind: no test input makes code generation's own checks throw.
    [Theory]
    [InlineData(typeof(IOException), true)]
    [InlineData(typeof(UnauthorizedAccessException), true)]
    [InlineData(typeof(InvalidOperationException), false)]
    [InlineData(typeof(KeyNotFoundException), false)]
    [InlineData(typeof(IndexOutOfRangeException), false)]
    [InlineData(typeof(ArgumentException), false)]
    [InlineData(typeof(InvalidDataException), false)]
    public void GenerationIntoTheOutputRecordsOnlyTheOutputsFailure(Type kind, bool output)
    {
        var c = this.Create();
        var failure = (Exception)Activator.CreateInstance(kind, "failure")!;
        using var writer = new FailingWriter(failure);
        if (output)
        {
            Assert.False(EmissionArtifacts.GenerateInto(c, writer));
            var record = Assert.Single(c.Diagnostics.Finalize(DiagnosticPartition.Emission, DiagnosticPartition.Emission).Diagnostics);
            Assert.Equal((nameof(DiagnosticCode.ArtifactPublicationFailed_Kd), DiagnosticCategory.Input), (record.Code, record.Category));
            Assert.Equal("failure", PublicationFailure(c));
            return;
        }

        Assert.Same(failure, Assert.Throws(kind, () => EmissionArtifacts.GenerateInto(c, writer)));
        Assert.Empty(c.Diagnostics.Finalize(DiagnosticPartition.Emission, DiagnosticPartition.Emission).Diagnostics);
    }

    // DECISIONS R5-M169: a command ends a result that an exception ends as Faulted (Project.Faults): one CheckFaulted_Kd (Exception) at
    // the project file, and the command fails and publishes nothing. Each row breaks a caller contract no command input reaches, without a
    // production seam: the front end reads a null source path (BuildTarget's fault); publication checks a null NativeLibraries input that
    // preparation validated (ArtifactFiles.CheckPath, PublishArtifacts' fault); code generation writes the location of a source path with
    // an embedded NUL (SourceLocationTable.Text's ArgumentException, which Publish's own mapping would take as an output failure).
    [Theory]
    [InlineData("front end")]
    [InlineData("publication")]
    [InlineData("code generation")]
    public async Task AnExceptionEndsTheCommandsResultAsFaulted(string phase)
    {
        var file = Path.Combine(this.directory, "Hello.kimiproj");
        File.WriteAllText(file, $"Targets={{\"{WindowsProfile.Target}\"}} OutputPath=\"out/Hello.ll\"");
        File.WriteAllText(Path.Combine(this.directory, "Hello.kimi"), "::Kimi.Console.writeLine(\"Hello, world!\")");
        var console = new DiagnosticContractTest.DiagnosticConsole();
        Assert.True(Project.TryCreate(new Kimigayo(console), null, file, out var project));
        if (phase == "front end")
        {
            project.AddKimiFile(null!);
            Assert.False(await project.Generate(TestContext.Current.CancellationToken));
        }
        else
        {
            var c = phase == "publication" ? this.Create() :
                this.Create("var x: i32 = 1\nx = x + 1\nrequire x == 2 else => $abort(\"two\")\n", Path.Combine(this.directory, "He\0llo.kimi"));
            if (phase == "publication")
            {
                c.Project.ProjectFile.NativeLibraries[WindowsProfile.Target] = new() { ["unused"] = new() { Kind = "import", Input = null! } };
            }

            Assert.False(project.PublishArtifacts(c, null, null, out var path));
            Assert.Null(path);
        }

        Assert.Contains(nameof(DiagnosticCode.CheckFaulted_Kd), console.Text, StringComparison.Ordinal);
        Assert.Contains(DiagnosticFaults.Describe(DiagnosticFault.Exception), console.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(DiagnosticCode.ArtifactPublicationFailed_Kd), console.Text, StringComparison.Ordinal);
        Assert.Contains("Hello.kimiproj", console.Text, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(this.directory, "*.l*", SearchOption.AllDirectories));
    }

    [Fact]
    public void LogicalSourcePathsAreIndependentOfCheckoutDirectory()
    {
        string Emit(string checkout)
        {
            var c = MinimalEmissionTest.Analyze("Console.writeLine(\"日本語\")", Path.Combine(checkout, "Hello.kimi"));
            c.Project.Directory = checkout;
            using var writer = new StringWriter();
            Assert.True(c.Generate(writer), MinimalEmissionTest.Describe(c));
            return writer.ToString();
        }

        Assert.Equal(Emit(Path.Combine(this.directory, "first")), Emit(Path.Combine(this.directory, "second")));
    }

    [Fact]
    public void DiagnosticErrorStateFollowsPartitionsNotDisplay()
    {
        var c = Compilation.CreateForTest();
        var diagnostics = c.Diagnostics;
        Assert.False(diagnostics.HasErrors);
        diagnostics.Report(DiagnosticPartition.Emission, DiagnosticCode.ArtifactPublicationFailed_Kd, null, note: "test");
        Assert.True(diagnostics.HasErrorsIn(DiagnosticPartition.Emission));
        Assert.False(diagnostics.HasErrorsThrough(DiagnosticPartition.Ownership));
        diagnostics.Invalidate(DiagnosticPartition.Emission);
        Assert.False(diagnostics.HasErrors);

        // SPEC 23.1.2: two problems at one offset are both published, and a repeated report of one merges.
        var document = new SourceDocument("main.kimi", "x");
        c.Kotonoha.DiagnosticCollection.Add(new(0, 1), DiagnosticCode.TypeMismatch_Kd, sourceDocument: document);
        c.Kotonoha.DiagnosticCollection.Add(new(0, 1), DiagnosticCode.InvalidCharacter_Kd, "x", sourceDocument: document);
        c.Kotonoha.DiagnosticCollection.Add(new(0, 1), DiagnosticCode.InvalidCharacter_Kd, "x", sourceDocument: document);
        Assert.Equal(["InvalidCharacter_Kd", "TypeMismatch_Kd"], TestDiagnostics.Of(c).Select(static x => x.Code));
        Assert.True(diagnostics.HasSyntaxErrors(c.Kotonoha));
        diagnostics.InvalidateSyntax(c.Kotonoha);
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
                ["codec"] = new() { Kind = "static", Input = "native/codec.lib", Sha256 = new string('a', 64) },
                ["unused"] = new() { Kind = "static", Input = "unused.lib" },
            });
        Assert.True(EmissionArtifacts.Publish(c, null, out var path), MinimalEmissionTest.Describe(c));
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.ChangeExtension(path!, ".link.json")));
        var libraries = manifest.RootElement.GetProperty("libraries");
        Assert.Equal(["codec", "kernel32", "kimi_backend", "zlib"], libraries.EnumerateArray().Select(x => x.GetProperty("name").GetString()));
        Assert.Equal(("static", Path.Combine("..", "native", "codec.lib")), (libraries[0].GetProperty("kind").GetString(), libraries[0].GetProperty("input").GetString()));
        Assert.Equal(("import", "zlib.lib"), (libraries[3].GetProperty("kind").GetString(), libraries[3].GetProperty("input").GetString()));
        Assert.Equal(new string('a', 64), libraries[0].GetProperty("sha256").GetString());
        Assert.False(libraries[3].TryGetProperty("sha256", out _));
        Assert.Contains("declare dllimport i32 @inflate(i32)\n", File.ReadAllText(path!));
    }

    [Fact]
    public void RequiredNameWithoutSupplyCannotPublish()
    {
        var c = this.CreateForeign(
            "group Native\n    #LibraryImport(\"codec\", \"encode\")\n    public unsafe func encode(value: i32) -> i32\npublic func main() => ()",
            settings => settings.NativeRequirements[WindowsProfile.Target] = new(StringComparer.Ordinal) { ["codec"] = new() { Kind = "static" } });
        Assert.False(EmissionArtifacts.Publish(c, null, out var path));
        Assert.Null(path);
        Assert.Contains("has no NativeLibraries supply", PublicationFailure(c));
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
        c.Analyze();
        c.Project.Directory = this.directory;
        c.Project.Name = "Hello";
        c.Project.ProjectFile.OutputPath = "out/Hello.ll";
        Assert.False(EmissionArtifacts.Publish(c, null, out var path));
        Assert.Null(path);
        Assert.Contains("dependency modules", PublicationFailure(c));
        Assert.Empty(Directory.EnumerateFiles(this.directory, "*", SearchOption.AllDirectories));
    }

    // DECISIONS R5-M165: a publication failure is one ArtifactPublicationFailed_Kd record at the project; its cause is the note.
    private static string PublicationFailure(Compilation c)
        => Assert.Single(TestDiagnostics.Of(c), static x => x.Code == nameof(DiagnosticCode.ArtifactPublicationFailed_Kd)).Note!;

    private Compilation CreateForeign(string source, Action<ProjectFile> configure)
    {
        var c = Compilation.CreateForTest();
        configure(c.Project.ProjectFile);
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", source));
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Analyze();
        c.Project.Directory = this.directory;
        c.Project.Name = "Hello";
        c.Project.ProjectFile.OutputPath = "out/Hello.ll";
        return c;
    }

    private Compilation Create(string source = "::Kimi.Console.writeLine(\"Hello, world!\")", string path = "Hello.kimi")
    {
        var c = MinimalEmissionTest.Analyze(source, path);
        c.Project.Directory = this.directory;
        c.Project.Name = "Hello";
        c.Project.ProjectFile.OutputPath = "out/Hello.ll";
        return c;
    }

    // A writer whose every write fails with the given exception.
    private sealed class FailingWriter(Exception failure) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value) => throw failure;
    }
}
