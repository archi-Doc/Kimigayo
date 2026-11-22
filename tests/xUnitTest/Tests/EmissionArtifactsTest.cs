// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kimi;
using Kimi.Build;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public sealed class EmissionArtifactsTest : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "kimi-emission-" + Guid.NewGuid().ToString("N"));

    public EmissionArtifactsTest() => Directory.CreateDirectory(this.directory);

    public void Dispose() => Directory.Delete(this.directory, true);

    [Fact]
    public void PublishesTheMatchedPair()
    {
        var c = this.Create();
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
        Assert.Equal(catalog.RootElement.GetProperty("llvmVersion").GetString(), ToolchainIdentity.LlvmVersion);
        Assert.Equal(ToolchainIdentity.LlvmVersion, root.GetProperty("codegen").GetProperty("llvmVersion").GetString());
        Assert.Equal(ToolchainIdentity.BackendSha256, root.GetProperty("backendSupport").GetProperty("artifactSha256").GetString());
        var libraries = root.GetProperty("libraries");
        Assert.Equal(2, libraries.GetArrayLength());
        Assert.Equal("kernel32", libraries[0].GetProperty("name").GetString());
        Assert.Equal(4, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("llvm-dlltool", libraries[0].GetProperty("generator").GetString());
        Assert.Equal(ToolchainIdentity.Kernel32DefinitionSha256, libraries[0].GetProperty("definitionSha256").GetString());
        Assert.False(libraries[0].TryGetProperty("input", out _));
        Assert.Equal("kimi_backend", libraries[1].GetProperty("name").GetString());
        Assert.Equal("toolchain", libraries[1].GetProperty("resolution").GetString());
        Assert.False(libraries[1].TryGetProperty("input", out _));
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
    [InlineData("kimi_backend", "static", "backend.dll")]
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

    // IMPL 20.4 (Tatara B6a, decision 20): kimi_backend is the compiler's supply, so a project's own entry fails at preparation as
    // InvalidDependencyConfiguration_Kd with the load's note (NativeConfiguration); the rule's rows are DependencyConfigurationTest's.
    [Fact]
    public void AProjectsOwnBackendEntryCannotPrepare()
    {
        var c = Compilation.CreateForTest();
        c.Project.ProjectFile.NativeLibraries[WindowsProfile.Target] = new() { ["kimi_backend"] = new() { Kind = "static", Input = "vendor/kimi_backend.lib" } };
        Assert.False(c.Prepare(WindowsProfile.Target));
        Assert.Contains(
            TestDiagnostics.Of(c),
            static x => x.Code == nameof(DiagnosticCode.InvalidDependencyConfiguration_Kd) &&
                x.Note == "kimi_backend is supplied by the compiler. Remove the kimi_backend entry from NativeLibraries.");
    }

    [Fact]
    public void DefaultManifestNeedsNoToolchainAndRejectsOldSchema()
    {
        var c = this.Create();
        Assert.True(EmissionArtifacts.Publish(c, null, out var path), MinimalEmissionTest.Describe(c));
        var json = File.ReadAllText(Path.ChangeExtension(path!, ".link.json"));
        using var manifest = JsonDocument.Parse(json);
        Assert.False(manifest.RootElement.TryGetProperty("toolchain", out _));
        NativeToolchain.ValidateManifest(manifest.RootElement);
        using var oldManifest = JsonDocument.Parse(json.Replace("\"schemaVersion\": 4", "\"schemaVersion\": 3", StringComparison.Ordinal));
        Assert.Contains("'schemaVersion' is not this compiler's", Assert.Throws<InvalidDataException>(() => NativeToolchain.ValidateManifest(oldManifest.RootElement)).Message);
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

    // A replacement that keeps failing (ArtifactFiles.Replace; PLAN G1257) is one IOException naming its destination once, with the
    // system's message and the move's failure inside, and keeps both files: a destination held with or without delete sharing, read-only,
    // a directory, or in a missing directory (the move names no path on Windows, so every caller's message gains it here).
    [Theory]
    [InlineData("held", typeof(UnauthorizedAccessException))]
    [InlineData("held without delete sharing", typeof(UnauthorizedAccessException))]
    [InlineData("read-only", typeof(UnauthorizedAccessException))]
    [InlineData("a directory", typeof(UnauthorizedAccessException))]
    [InlineData("in a missing directory", typeof(DirectoryNotFoundException))]
    public void ReplaceReportsAPersistentFailureNamingItsDestinationAndKeepsBothFiles(string row, Type failure)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows rename semantics test.");
        var destination = Path.Combine(this.directory, row == "in a missing directory" ? "missing" : string.Empty, "Hello.link.build.json");
        var source = Path.Combine(this.directory, "Hello.link.build.json.tmp");
        var old = row is "a directory" or "in a missing directory" ? null : "old";
        File.WriteAllText(source, "new");
        if (old is not null)
        {
            File.WriteAllText(destination, old);
        }
        else if (row == "a directory")
        {
            Directory.CreateDirectory(destination);
        }

        var sharing = row == "held" ? FileShare.Read | FileShare.Delete : FileShare.Read;
        using var held = row.StartsWith("held", StringComparison.Ordinal) ? new FileStream(destination, FileMode.Open, FileAccess.Read, sharing) : null;
        if (row == "read-only")
        {
            File.SetAttributes(destination, FileAttributes.ReadOnly);
        }

        IOException ex;
        try
        {
            ex = Assert.Throws<IOException>(() => ArtifactFiles.Replace(source, destination));
        }
        finally
        {
            held?.Dispose();
            if (row == "read-only")
            {
                File.SetAttributes(destination, FileAttributes.Normal); // Before Dispose deletes the directory.
            }
        }

        Assert.IsType(failure, ex.InnerException);
        Assert.Equal($"The file could not be replaced: {destination}. {ex.InnerException!.Message}", ex.Message);
        Assert.Equal("new", File.ReadAllText(source));
        Assert.Equal(old, File.Exists(destination) ? File.ReadAllText(destination) : null);
    }

    [Fact]
    public void FailedSemanticGatePreservesOldOutputsWithoutClaimingThem()
    {
        var c = this.Create();
        Assert.True(EmissionArtifacts.Publish(c, null, out var path), MinimalEmissionTest.Describe(c));
        var previous = File.ReadAllBytes(path!);
        c.Bind(); // The earlier analysis and startup check cannot authorize another publication.
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

    // PLAN G157 (7), DECISIONS R5-M181: code generation never reads the native configuration, so a publication-time configuration
    // failure (an unused native input that is not a .lib) and a generation failure (a runtime Drop without a verdict, InternalInvariant_Kd
    // at the Drop) are independent, and both are reported.
    [Fact]
    public void AConfigurationFailureLeavesGenerationsRecords()
    {
        var c = this.Create();
        c.Project.ProjectFile.NativeLibraries[WindowsProfile.Target] = new() { ["unused"] = new() { Kind = "import", Input = "unused.dll" } };
        FunctionCodegenTest.RemoveTheDropVerdict(c);
        Assert.False(EmissionArtifacts.Publish(c, null, out var path));
        Assert.Null(path);
        Assert.Equal(
            [("ArtifactPublicationFailed_Kd", (string?)null), ("InternalInvariant_Kd", "\"Hello, world!\"")],
            TestDiagnostics.Of(c).Select(static x => (x.Code, x.Text)).Order());
        Assert.Contains(".lib", PublicationFailure(c), StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(this.directory, "*", SearchOption.AllDirectories));
    }

    // DECISIONS R5-M169: a command ends a result that an exception ends as Faulted (Project.Faults): one CheckFaulted_Kd (Exception) at
    // the project file, and the command fails and publishes nothing. No production seam: the front end reads a null source path, a caller
    // contract no command input reaches (BuildTarget's fault); publication checks a null NativeLibraries input that preparation validated
    // (ArtifactFiles.CheckPath, PublishArtifacts' fault). Code generation's row, a source path with an embedded NUL, left with G346's fix
    // (DECISIONS R5-M175: naming a source never throws); generation runs outside Publish's classifying catches (R5-M169's amendment),
    // and no program makes it throw without a production seam.
    // The toolchain boundary (R9-B2): the project's own result (Project.OwnResult) has the same fault path, one CheckFaulted_Kd, in
    // check and emit alike: its checks read a null target list (it escaped Check and Generate), and the host's console throws
    // InvalidOperationException at the report of the published pair (it escaped Generate; the pair stays, published before the report).
    // The test unit's own result (PrepareTests through Project.OwnResult, PLAN G1011) has the same fault path: its checks read a null
    // target list (it escaped the preparation; kimi test printed "Internal test runner error", 3).
    [Theory]
    [InlineData("check: project checks", 0)]
    [InlineData("test: project checks", 0)]
    [InlineData("project checks", 0)]
    [InlineData("front end", 0)]
    [InlineData("report", 2)]
    [InlineData("publication", 0)]
    public async Task AnExceptionEndsTheCommandsResultAsFaulted(string phase, int published)
    {
        var file = Path.Combine(this.directory, "Hello.kimiproj");
        File.WriteAllText(file, $"Targets={{\"{WindowsProfile.Target}\"}} OutputPath=\"out/Hello.ll\"");
        File.WriteAllText(Path.Combine(this.directory, "main.kimi"), "::Kimi.Console.writeLine(\"Hello\")");
        var console = new DiagnosticContractTest.DiagnosticConsole { LineAction = phase == "report" ? ("Generated LLVM inputs", DiagnosticContractTest.DiagnosticConsole.Fail) : null };
        Assert.True(Project.TryCreate(new Kimigayo(console), null, file, out var project));
        if (phase.EndsWith("project checks", StringComparison.Ordinal))
        {
            project.ProjectFile.Targets = null!;
        }
        else if (phase == "front end")
        {
            project.AddKimiFile(null!);
        }

        var ct = TestContext.Current.CancellationToken;
        if (phase == "publication")
        {
            var c = this.Create();
            c.Project.ProjectFile.NativeLibraries[WindowsProfile.Target] = new() { ["unused"] = new() { Kind = "import", Input = null! } };

            Assert.False(project.PublishArtifacts(c, null, null, out var path));
            Assert.Null(path);
        }
        else if (phase.StartsWith("test", StringComparison.Ordinal))
        {
            Assert.Null(project.PrepareTests(ct));
        }
        else
        {
            Assert.False(await (phase.StartsWith("check", StringComparison.Ordinal) ? project.Check(ct) : project.Generate(ct)));
        }

        Assert.Equal(1, console.Text.Split(nameof(DiagnosticCode.CheckFaulted_Kd)).Length - 1);
        Assert.Contains(DiagnosticFaults.Describe(DiagnosticFault.Exception), console.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(DiagnosticCode.ArtifactPublicationFailed_Kd), console.Text, StringComparison.Ordinal);
        Assert.Contains("Hello.kimiproj", console.Text, StringComparison.Ordinal);
        Assert.Equal(published, Directory.EnumerateFiles(this.directory, "*.l*", SearchOption.AllDirectories).Count());
    }

    // PLAN G346 (DECISIONS R5-M175): a source path with an embedded NUL inside the project is published, its location escaped (IMPL
    // 21.4.2), where its location text faulted the command before.
    [Fact]
    public void ANulInsideTheProjectIsPublished()
    {
        var file = Path.Combine(this.directory, "Hello.kimiproj");
        File.WriteAllText(file, $"Targets={{\"{WindowsProfile.Target}\"}} OutputPath=\"out/Hello.ll\"");
        var console = new DiagnosticContractTest.DiagnosticConsole();
        Assert.True(Project.TryCreate(new Kimigayo(console), null, file, out var project));
        var c = this.Create("var x: i32 = 1\nx = x + 1\nrequire x == 2 else => $abort(\"two\")\n", Path.Combine(this.directory, "He\0llo.kimi"));
        Assert.True(project.PublishArtifacts(c, null, null, out var path), console.Text);
        Assert.Equal("He\\u{0}llo.kimi:3:24", c.Locations.Text(SourceRef.Of(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<MacroKoto>().Single())));
        Assert.DoesNotContain(nameof(DiagnosticCode.CheckFaulted_Kd), console.Text, StringComparison.Ordinal);
        Assert.True(File.Exists(path));
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

    // R9-B1: the manifest states the generation result's external declarations, which the native build's object inspection reads in
    // place of the IR (SPEC 22.6: an import is referenced through __imp_ when dllimport), in declaration order; an uncalled import's
    // library is still required and listed (SPEC 22.6, IMPL 20.4; RequiredNameWithoutSupplyCannotPublish: it needs its supply).
    [Fact]
    public void TheManifestStatesTheModulesExternalDeclarations()
    {
        var c = this.CreateForeign(
            "group Native\n    #LibraryImport(\"codec\", \"encode\")\n    public unsafe func encode(value: i32) -> i32\n" +
            "    #LibraryImport(\"zlib\", \"inflate\")\n    public unsafe func inflate(value: i32) -> i32\n" +
            "    #LibraryImport(\"kernel32\", \"GetLastError\")\n    public unsafe func lastError() -> u32\n" +
            "public func main()\n    var v: i32 = 0\n    var e: u32 = 0\n    unsafe => v = Native.encode(1)\n    unsafe => e = Native.lastError()",
            settings => settings.NativeLibraries[WindowsProfile.Target] = new()
            {
                ["codec"] = new() { Kind = "static", Input = "codec.lib" },
                ["zlib"] = new() { Kind = "import", Input = "zlib.lib" },
            });
        Assert.True(EmissionArtifacts.Publish(c, null, out var path), MinimalEmissionTest.Describe(c));
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.ChangeExtension(path!, ".link.json")));
        var root = manifest.RootElement;
        Assert.Equal(
            [("encode", false), ("GetLastError", true)],
            root.GetProperty("externalDeclarations").EnumerateArray().Select(static x => (x.GetProperty("symbol").GetString(), x.GetProperty("dllimport").GetBoolean())));
        Assert.Equal(["codec", "kernel32", "kimi_backend", "zlib"], root.GetProperty("libraries").EnumerateArray().Select(static x => x.GetProperty("name").GetString()));
        NativeToolchain.ValidateManifest(root);
    }

    // R9-B1: a dependency module's called import (Kimi.Time's Windows counter functions, SPEC 22.8.3) is one of the generation result's
    // external declarations and the manifest's, so the object inspection allows its __imp_ reference.
    [Fact]
    public void ADependencysCalledImportIsDeclared()
    {
        var c = this.Create("var watch = Kimi.Time.Stopwatch.init()\nwatch.start()\nwatch.stop()");
        Assert.True(EmissionArtifacts.Publish(c, null, out var path), MinimalEmissionTest.Describe(c));
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.ChangeExtension(path!, ".link.json")));
        var declared = manifest.RootElement.GetProperty("externalDeclarations").EnumerateArray()
            .Select(static x => (x.GetProperty("symbol").GetString()!, x.GetProperty("dllimport").GetBoolean())).ToArray();
        Assert.Equal(c.Generated.Externals.Select(static x => (x.Symbol, x.DllImport)), declared);
        Assert.Contains(("QueryPerformanceCounter", true), declared);
        Assert.Contains(("QueryPerformanceFrequency", true), declared);
    }

    // R9-B1: a manifest whose external declarations are not each a nonempty symbol, declared once, with a boolean dllimport form is
    // rejected before any tool.
    [Theory]
    [InlineData("[{\"symbol\": \"\", \"dllimport\": true}]", "Invalid external declaration")]
    [InlineData("[{\"symbol\": \"encode\", \"dllimport\": \"yes\"}]", "'externalDeclarations.dllimport' is missing or not a JSON Boolean")]
    [InlineData("[{\"symbol\": \"encode\", \"dllimport\": true}, {\"symbol\": \"encode\", \"dllimport\": false}]", "Duplicate external declaration")]
    public void AMalformedExternalDeclarationIsRejected(string declarations, string failure)
    {
        var c = this.Create();
        Assert.True(EmissionArtifacts.Publish(c, null, out var path), MinimalEmissionTest.Describe(c));
        var json = File.ReadAllText(Path.ChangeExtension(path!, ".link.json"));
        Assert.Contains("\"externalDeclarations\": []", json, StringComparison.Ordinal);
        using var malformed = JsonDocument.Parse(json.Replace("\"externalDeclarations\": []", "\"externalDeclarations\": " + declarations, StringComparison.Ordinal));
        Assert.Contains(failure, Assert.Throws<InvalidDataException>(() => NativeToolchain.ValidateManifest(malformed.RootElement)).Message);
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
