// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Kimi;
using Kimi.Build;
using Kimi.Command;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public sealed class NativeToolchainTest : IDisposable
{
    private const string Windows = "Native builds run on Windows.";

    // HelloUnderALinkerVariable's expected executable hash, which its parent passes.
    private const string ExpectedExecutable = "KIMI_TEST_EXPECTED_EXECUTABLE";

    private readonly string directory = Path.Combine(Path.GetTempPath(), "kimi-native-" + Guid.NewGuid().ToString("N"));

    // The host's console of the test's projects, which reads a refusal's message.
    private readonly DiagnosticContractTest.DiagnosticConsole console = new();

    public NativeToolchainTest() => Directory.CreateDirectory(this.directory);

    public void Dispose() => TestFolder.Delete(this.directory);

    // IMPL 20.4: a staged copy is a snapshot named by its hash, held with read sharing only until its caller releases it (StagedHold): a
    // change of the original does not reach it, a damaged copy is staged again and an intact one reused, and no temporary copy remains.
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
        Assert.Equal(staged, Stage(out var reusedHash));
        Assert.Equal(hash, reusedHash);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(staged)!, "*.tmp"));

        // The staged copy's path, after a write to it failed while it was held; its hold ends here.
        string Stage(out string stagedHash)
        {
            using var stream = File.OpenRead(input);
            using var held = NativeToolchain.StageInput(stream, Path.Combine(this.directory, "output.native"), ".lib", out stagedHash);
            Assert.Throws<IOException>(() => File.WriteAllText(held.Name, "written while held"));
            return held.Name;
        }
    }

    // IMPL 20.4 (Tatara B6a, B7b4; one representation since B7b7, C-469 n1): the compiler carries every row's supplies' bytes, one record
    // per (row, supply) with its files. windows-x64's are the two reserved names in Ordinal order, one file each (a COFF file's place is
    // Library); linux-x64's c is musl's startup files and C library in the Linux catalog's order, its link order (crt1.o and crti.o
    // start, libc.a library, crtn.o end), and the catalog's providedSymbols are c's symbols. Each file's SHA-256 is its catalog's, and each
    // embedded resource has that SHA-256 and is the file committed beside the catalog, so a build stages the adopted bytes. musl's notice,
    // which the compiler carries as Kimi.Linux.COPYRIGHT, is the tarball's COPYRIGHT: the catalog's copyrightSha256, which build-musl.ps1
    // checks in the extracted tarball.
    [Fact]
    public void EachCompilerSupplyIsTheCatalogsCommittedFile()
    {
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/backend/windows-x64"));
        var linux = Path.Combine(directory, "../linux-x64");
        using var catalog = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "profile.json")));
        using var musl = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(linux, "profile.json")));
        Assert.Equal(
            [
                $"windows-x64-v1 kernel32 kernel32.lib library {catalog.RootElement.GetProperty("kernel32").GetProperty("sha256").GetString()}",
                $"windows-x64-v1 kimi_backend kimi_backend_windows_x64_v1.lib library {catalog.RootElement.GetProperty("artifactSha256").GetString()}",
                .. musl.RootElement.GetProperty("files").EnumerateArray().Select(static x => $"linux-x64-v1 c {x.GetProperty("name").GetString()} {x.GetProperty("place").GetString()} {x.GetProperty("sha256").GetString()}"),
            ],
            ToolchainIdentity.Supplies.SelectMany(static x => x.Files.Select(f => $"{x.Row.Name} {x.Supply.Name} {f.Name} {f.Place.ToString().ToLowerInvariant()} {f.Sha256}")));
        Assert.Equal(["crt1.o Start", "crti.o Start", "libc.a Library", "crtn.o End"], Assert.Single(ToolchainIdentity.Supplies, static x => x.Row == TargetProfile.Linux).Files.Select(static x => $"{x.Name} {x.Place}"));
        Assert.Equal(TargetProfile.Linux.Supply("c")!.Symbols, musl.RootElement.GetProperty("providedSymbols").EnumerateArray().Select(static x => x.GetString()));
        foreach (var supply in ToolchainIdentity.Supplies)
        {
            foreach (var file in supply.Files)
            {
                AssertCommitted(file.Open(), file.Sha256, Path.Combine(supply.Row == TargetProfile.Linux ? linux : directory, file.Name));
            }
        }

        AssertCommitted(
            typeof(ToolchainIdentity).Assembly.GetManifestResourceStream("Kimi.Linux.COPYRIGHT")!,
            musl.RootElement.GetProperty("musl").GetProperty("copyrightSha256").GetString()!,
            Path.Combine(linux, "COPYRIGHT"));

        static void AssertCommitted(Stream stream, string sha256, string file)
        {
            using var bytes = new MemoryStream();
            using (stream)
            {
                stream.CopyTo(bytes);
            }

            Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
            Assert.Equal(File.ReadAllBytes(file), bytes.ToArray());
        }
    }

    // SupplyFacts (PLAN G1374; design §2, §3, §7 T1): each compiler supply file carries Tatara's inspection of its bytes, read at
    // adoption, as <file>.facts beside it, embedded beside its resource, and the build reads it instead of inspecting the file. This row is
    // the one detector of a changed reading of unchanged bytes (src/Tatara, the pinned LLVM, the facts' codec): it inspects each committed
    // file with the process's Tatara, which reports nothing, and requires the committed facts byte-equal to that inspection written with
    // the SHA-256 of the bytes inspected, that hash the catalog's, the embedded copy the committed file, and the stored inspection parsed as
    // the fresh one is. A malformed file is a fault, never a lenient read: libc.a's facts less their last byte, with one byte more or less
    // their first (a hash line of 63 digits), and a file whose kinds 1 and 2 are swapped (in order, it reads). With KIMI_SUPPLY_FACTS=write
    // the row writes the files after the inspection's check and compares nothing (adoption: write, build Kimi again, run the row, commit
    // the files with the change).
    // Fail-before: no facts file existed.
    [Fact]
    public void EachCompilerSupplysFactsAreItsInspection()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var write = Environment.GetEnvironmentVariable("KIMI_SUPPLY_FACTS") == "write";
        var messages = new List<string>();
        var context = TataraExecutorTest.Context with { Report = (_, message) => messages.Add(message) };
        foreach (var supply in ToolchainIdentity.Supplies)
        {
            foreach (var file in supply.Files)
            {
                var path = Path.Combine(Repository.Root, "src", "backend", supply.Row == TargetProfile.Linux ? "linux-x64" : "windows-x64", file.Name);
                var inspection = Tatara.Instance.InspectTexts(context, path);
                Assert.Empty(messages);
                using var fresh = new MemoryStream();
                inspection.Write(fresh, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
                if (write)
                {
                    File.WriteAllBytes(path + ".facts", fresh.ToArray());
                    continue;
                }

                var committed = File.ReadAllBytes(path + ".facts");
                Assert.True(
                    committed.AsSpan().SequenceEqual(fresh.ToArray()),
                    $"{path}.facts is not Tatara's inspection of {file.Name}: run this row with KIMI_SUPPLY_FACTS=write, build Kimi again, run the row again and commit the files with the change.");
                var (sha256, stored) = Tatara.Inspection.Read(new MemoryStream(committed));
                Assert.Equal(file.Sha256, sha256);
                using var embedded = new MemoryStream();
                using (var stream = file.OpenFacts())
                {
                    stream.CopyTo(embedded);
                }

                Assert.Equal(committed, embedded.ToArray());
                Assert.Equal(TataraExecutorTest.Describe(Tatara.Facts(inspection)), TataraExecutorTest.Describe(Tatara.Facts(stored)));
            }
        }

        if (write)
        {
            return;
        }

        var libc = File.ReadAllBytes(Path.Combine(Repository.Root, "src", "backend", "linux-x64", "libc.a.facts"));
        Assert.Equal(new string('0', 64), Tatara.Inspection.Read(new MemoryStream(Empty("1", "2"))).Sha256);
        byte[][] malformed = [libc[..^1], [.. libc, (byte)'\n'], libc[1..], Empty("2", "1")];
        foreach (var bytes in malformed)
        {
            Assert.Throws<InvalidOperationException>(() => Tatara.Inspection.Read(new MemoryStream(bytes)));
        }

        // An empty inspection's file with its first two kinds in the given order.
        static byte[] Empty(string first, string second) => Encoding.UTF8.GetBytes($"{new string('0', 64)}\nflags 0 0\n{first} 0\n{second} 0\n4 0\n5 0\n6 0\n7 0\n");
    }

    // SupplyFacts (design §4, §7 T2; C-556 B-C2): the dependency step's per-input function takes a compiler supply file's facts from its
    // stored inspection, never from its staged copy, and only when their first line is the staged hash. The input is libc.a's supply file
    // on linux-x64 held as a "staged copy" that is a text file Tatara cannot read: under the catalog's hash (a reused copy's, StageSupply)
    // the function returns libc.a's facts as a live inspection of the committed file reads them, so no supply is inspected at build time;
    // under the text's own hash (a fresh stage of other bytes) it fails as the toolchain's InvalidDataException naming the facts. The
    // store is never read. Fail-before: P3's base row (libc.a's first read 6.1-6.9 ms); the parent has no such function, and its live
    // path cannot read the text file.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACompilerSupplysFactsAreItsCatalogs(bool catalogHash)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var libc = Assert.Single(ToolchainIdentity.Of(TargetProfile.Linux.Supply("c"))!.Files, static x => x.Name == "libc.a");
        var copy = Path.Combine(this.directory, "libc.a");
        File.WriteAllText(copy, "not an archive");
        var hash = catalogHash ? libc.Sha256 : ArtifactFiles.Hash(copy);
        using var held = File.OpenRead(copy);
        var input = new NativeToolchain.StagedLibrary("c", "static", hash, held, libc);
        if (catalogHash)
        {
            var committed = Path.Combine(Repository.Root, "src", "backend", "linux-x64", "libc.a");
            Assert.Equal(
                TataraExecutorTest.Describe(Tatara.Instance.Inspect(TataraExecutorTest.Context, committed)),
                TataraExecutorTest.Describe(NativeToolchain.ReadInputFacts(Tatara.Instance, TataraExecutorTest.LinuxContext, TargetProfile.Linux, input).Facts));
            return;
        }

        var error = Assert.Throws<InvalidDataException>(() => NativeToolchain.ReadInputFacts(Tatara.Instance, TataraExecutorTest.LinuxContext, TargetProfile.Linux, input));
        Assert.Equal("The compiler's linux-x64-v1 c libc.a facts do not describe its bytes.", error.Message);
        Assert.True(NativeToolchain.IsToolchainFailure(error));
    }

    // T's ruling 79 (a): the Linux record's reader places each file Start, Library or End, its one read of a place; a missing or other
    // place is the toolchain's InvalidDataException naming the file. Fail-before: an unknown place read (on 0e7a59618 any string did,
    // and the ELF writer left such a file off its line).
    [Theory]
    [InlineData("\"begin\"")]
    [InlineData("\"Start\"")]
    [InlineData(null)]
    public void ACatalogFileOutsideThePlacesIsRefused(string? place)
    {
        using var valid = JsonDocument.Parse("{\"name\": \"crt1.o\", \"sha256\": \"0\", \"place\": \"start\"}");
        Assert.Equal(SupplyPlace.Start, ToolchainIdentity.ReadFile(valid.RootElement).Place);
        using var file = JsonDocument.Parse("{\"name\": \"crt1.o\", \"sha256\": \"0\"" + (place is null ? string.Empty : ", \"place\": " + place) + "}");
        Assert.Equal(
            "The embedded native supply catalog's file 'crt1.o' has no place on an ELF link line (start, library or end).",
            Assert.Throws<InvalidDataException>(() => ToolchainIdentity.ReadFile(file.RootElement)).Message);
    }

    // The invalidation is the record's incomplete state: its status and the compiler's and LLVM's versions, and no member more (PLAN
    // G1227: the three constant toolchain-verification members are gone).
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
        Assert.Equal("status,compilerVersion,llvmVersion", string.Join(',', record.RootElement.EnumerateObject().Select(static x => x.Name)));
        Assert.Equal("incomplete", record.RootElement.GetProperty("status").GetString());
        Assert.Equal("previous executable", File.ReadAllText(paths.Executable));
    }

    // R9-B1 part 2 (form J, PLAN G983's manifest share): a manifest field that is missing, of another JSON kind or, for an Int32 row,
    // another number fails at its read with InvalidDataException naming the field, before Tatara is loaded and with the record left
    // incomplete; a fixed field with another value is not this compiler's (an ABI 2 manifest, emitted before Tatara B7r5, among them),
    // and a removed member (Tatara B6b5: the toolchain, whatever its content) asks to re-emit. The target is read first: one that names no
    // row of the target table asks to re-emit, and another row's head is checked as that row's (the Tatara plan's B7b7: Windows' manifest
    // under linux-x64's target fails at that row's profile). Each row edits one field of an emitted Application's manifest (a JSON value,
    // or null to remove it). The libraries are read with the other fields, before Tatara is loaded (ValidateManifest; the Tatara plan's
    // B7b7a).
    [Theory]
    [InlineData("toolchain", "{\"llvmBin\": \"../tools\"}", "The link manifest's 'toolchain.llvmBin' was removed: Kimi carries its toolchain. Re-emit the manifest.")]
    [InlineData("target", "\"x86_64-unknown-linux-gnu\"", "The link manifest's 'target' is not this compiler's. Re-emit the manifest.")]
    [InlineData("target", "\"x86_64-unknown-linux-musl\"", "'codegen.profile' is not this compiler's")]
    [InlineData("schemaVersion", "3.5", "'schemaVersion' is not this compiler's")]
    [InlineData("schemaVersion", "\"4\"", "'schemaVersion' is missing or not a JSON Number")]
    [InlineData("backendSupport.abiVersion", "2.5", "'backendSupport.abiVersion' is not this compiler's")]
    [InlineData("backendSupport.abiVersion", "2", "'backendSupport.abiVersion' is not this compiler's")]
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

    // LinkAmbiguity (T's rulings 21 (ii) and 28 (1)): the manifest's external declarations are the native build's one list of the
    // runtime's imports. Hello's manifest without WriteFile's declaration fails the object's inspection at its __imp_ reference, with the
    // record left incomplete. Fail-before: the manifest declared no runtime import, and the build read them from the target row.
    [Fact]
    public async Task TheRuntimesImportsAreTheManifests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var project = this.Hello("Hello");
        Assert.True(await project.Generate(TestContext.Current.CancellationToken));
        var paths = ArtifactPaths.Create(project);
        var manifest = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(paths.Manifest))!.AsObject();
        var declarations = manifest["externalDeclarations"]!.AsArray();
        Assert.True(declarations.Remove(declarations.Single(static x => x!["symbol"]!.GetValue<string>() == "WriteFile")));
        File.WriteAllText(paths.Manifest, manifest.ToJsonString());
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => NativeToolchain.BuildManifest(paths.Manifest, (_, _) => { }, TestContext.Current.CancellationToken));
        Assert.Equal("Unsupported actual object dependency: __imp_WriteFile U 0 0", error.Message);
        using var record = JsonDocument.Parse(File.ReadAllText(paths.Record));
        Assert.Equal("incomplete", record.RootElement.GetProperty("status").GetString());
    }

    // Tatara G1203 (IMPL 20.3.2): build --Manifest takes a manifest named Name.link.json, as emit writes it. A manifest of another name
    // names no output and fails before any write: no record or lock beside it (dev 3245a057d built it, its record Native test.build.json).
    [Fact]
    public async Task AManifestOfAnotherNameFailsBeforeAnyWrite()
    {
        var project = this.Create();
        Assert.True(await project.Generate(TestContext.Current.CancellationToken));
        var renamed = Path.ChangeExtension(ArtifactPaths.Create(project).Ir, ".json");
        File.Copy(ArtifactPaths.Create(project).Manifest, renamed);
        var entries = Directory.GetFileSystemEntries(Path.GetDirectoryName(renamed)!).Order(StringComparer.Ordinal).ToArray();
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => NativeToolchain.BuildManifest(renamed, (_, _) => { }, TestContext.Current.CancellationToken));
        Assert.Equal("A link manifest is named Name.link.json, as emit writes it.", error.Message);
        Assert.Equal(entries, Directory.GetFileSystemEntries(Path.GetDirectoryName(renamed)!).Order(StringComparer.Ordinal));
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
    // reports the loaded Tatara and each compiler supply's file's hash, the catalog's, and that its stored facts name those bytes and
    // parse (SupplyFacts, design §3 (c): ", facts matched"), as Information lines in one form, "<row> <supply> <file>" (B7b7, O5:
    // windows-x64's lines named their supply alone before), and its report is the self-check's record (passed; Tatara's build, ABI and
    // LLVM version; each file's hash against its catalog's and factsMatched). That it reads no root and runs no tool is test-cli's row (a
    // copy of the compiler outside any checkout, PATH clean).
    [Fact]
    public void VerificationIsASelfCheckOfWhatTheCompilerCarries()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var report = Path.Combine(this.directory, "evidence", "toolchain.json");
        var lines = new List<string>();
        NativeToolchain.Verify(report, (severity, line) => lines.Add($"{severity}: {line}"), TestContext.Current.CancellationToken);
        var build = Tatara.Instance.Build;
        string[] expected =
            [
                $"Information: Tatara: build {build}, ABI {Tatara.ExpectedAbi}, LLVM {ToolchainIdentity.LlvmVersion}",
                .. ToolchainIdentity.Supplies.SelectMany(static x => x.Files.Select(f => $"Information: {x.Row.Name} {x.Supply.Name} {f.Name}: {f.Sha256} (the catalog's), facts matched")),
            ];
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
            [
                .. ToolchainIdentity.Supplies.SelectMany(static x => x.Files.Select(f => $"name={x.Supply.Name} row={x.Row.Name} file={f.Name} sha256={f.Sha256} expectedSha256={f.Sha256} hashMatched=True factsMatched=True")),
            ],
            root.GetProperty("supplies").EnumerateArray().Select(static x => string.Join(' ', x.EnumerateObject().Select(static y => $"{y.Name}={y.Value}"))));
    }

    // A supply whose embedded bytes are not its row's SHA-256 (only a defective build of Kimi carries one) is the toolchain's failure,
    // its statement kept in the report's supplies, with its facts unchecked (factsMatched false; SupplyFacts, design §7).
    [Fact]
    public void VerificationRejectsASupplyThatIsNotItsCatalogsBytes()
    {
        var supplies = new System.Text.Json.Nodes.JsonArray();
        var backend = ToolchainIdentity.Supplies[1];
        var error = Assert.Throws<InvalidDataException>(() => NativeToolchain.VerifySupply(backend, backend.Files[0] with { Sha256 = new string('0', 64) }, supplies));
        Assert.Equal("The compiler's windows-x64-v1 kimi_backend kimi_backend_windows_x64_v1.lib does not match its catalog SHA-256.", error.Message);
        Assert.True(NativeToolchain.IsToolchainFailure(error));
        Assert.Equal(ToolchainIdentity.BackendSha256, supplies[0]!["sha256"]!.GetValue<string>());
        Assert.False(supplies[0]!["hashMatched"]!.GetValue<bool>());
        Assert.False(supplies[0]!["factsMatched"]!.GetValue<bool>());
        var c = ToolchainIdentity.Supplies[2];
        error = Assert.Throws<InvalidDataException>(() => NativeToolchain.VerifySupply(c, c.Files[2] with { Sha256 = new string('0', 64) }, supplies));
        Assert.Equal("The compiler's linux-x64-v1 c libc.a does not match its catalog SHA-256.", error.Message);
        Assert.Equal(
            $"c linux-x64-v1 libc.a {c.Files[2].Sha256} false false",
            $"{supplies[1]!["name"]} {supplies[1]!["row"]} {supplies[1]!["file"]} {supplies[1]!["sha256"]} {supplies[1]!["hashMatched"]} {supplies[1]!["factsMatched"]}");
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
        Assert.Equal(TargetProfile.Windows.Target, root.GetProperty("target").GetString());
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
        project.ProjectFile.Targets = ["aarch64-pc-windows-msvc", TargetProfile.Windows.Target];
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

    // IMPL 20.3, 20.3.1 (PLAN G1256): run --no-build runs the latest build only when its record names the project's input, the selected
    // .kimiproj or .kimi file, compared as Windows compares paths. A.kimi, A.kimiproj and B.kimiproj (whose OutputPath is A's) share one
    // record. A row builds one input (a source build, or build --Manifest of its emitted pair, whose manifest names it), or as an earlier
    // compiler did (the record without the member), then runs another: it runs, or the refusal names the record's input. Before G1256
    // every row ran (a record named no input).
    [Theory]
    [InlineData("build", "A.kimi", "A.kimiproj", "A.kimi")] // IMPL 20.3.1's example: the record names A.kimi.
    [InlineData("build", "A.kimiproj", "A.kimi", "A.kimiproj")]
    [InlineData("build", "B.kimiproj", "A.kimiproj", "B.kimiproj")]
    [InlineData("build", "A.kimi", "A.kimi", null)]
    [InlineData("build", "A.kimiproj", "A.kimiproj", null)]
    [InlineData("build", "a.kimi", "A.KIMI", null)] // One file in another case (G1202's ruling Q3).
    [InlineData("manifest", "A.kimiproj", "A.kimiproj", null)]
    [InlineData("manifest", "A.kimiproj", "A.kimi", "A.kimiproj")]
    [InlineData("earlier", "A.kimi", "A.kimi", "")]
    public async Task ARunWithoutBuildRunsOnlyTheBuildOfItsInput(string how, string built, string run, string? recorded)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var settings = $"Targets={{\"{TargetProfile.Windows.Target}\"}}";
        File.WriteAllText(Path.Combine(this.directory, "A.kimi"), "::Kimi.Console.writeLine(\"Hello\")");
        File.WriteAllText(Path.Combine(this.directory, "A.kimiproj"), settings);
        File.WriteAllText(Path.Combine(this.directory, "B.kimiproj"), settings + $" OutputPath=\"bin/{TargetProfile.Windows.Target}/A.ll\"");
        var first = this.Load(built);
        var paths = ArtifactPaths.Create(first);
        if (how == "manifest")
        {
            Assert.True(await first.Generate(TestContext.Current.CancellationToken), this.console.Text);
            await NativeToolchain.BuildManifest(paths.Manifest, (_, _) => { }, TestContext.Current.CancellationToken);
        }
        else
        {
            Assert.True(await first.Build(TestContext.Current.CancellationToken), this.console.Text);
        }

        if (how == "earlier")
        {
            var record = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(paths.Record))!.AsObject();
            Assert.True(record.Remove("input"));
            File.WriteAllText(paths.Record, record.ToJsonString());
        }

        var second = this.Load(run);
        if (recorded is null)
        {
            Assert.Equal(0, await second.Run(TestContext.Current.CancellationToken));
        }
        else
        {
            var names = recorded.Length == 0 ? "no input" : "another input: " + Path.Combine(this.directory, recorded);
            await this.AssertRunRefused(second, $"The latest build record names {names}. Run 'kimi build' first.");
        }
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
    // the build could not begin and that its record keeps its last state, naming the record once: with the lock's failure, which names
    // the lock, and through the replacement's failure, which names its destination (PLAN G1257). The record is v1's success (IMPL 20.3.2:
    // "the earlier success when the first invalidation fails"), so run --no-build runs v1's executable. On the installed toolchain.
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

        var named = row == "lock read-only" ? $": {paths.Record}. " : $". The file could not be replaced: {paths.Record}. ";
        Assert.Contains("The build could not begin, so its record keeps its last state" + named, this.console.Text, StringComparison.Ordinal);
        Assert.Equal(1, this.console.Text.Split(paths.Record).Length - 1);
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

    // Tatara G1202 (IMPL 20.3.2): a native build holds its IR open for reading only from its hash of the pair's IR to its end, so it
    // compiles the bytes its record names. An emit of the same output during the build (at its "Built: " report, inside the hold; the
    // source edited to "Changed") cannot replace the IR and fails (ArtifactPublicationFailed_Kd); the pair stays the checked one, the
    // record names it and the executable prints its text. On dev 3245a057d the emit published "Changed". On the installed toolchain.
    [Theory]
    [InlineData("O2")]
    [InlineData("O0")]
    public async Task AnEmitDuringABuildOfItsOutputFails(string optimization)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var token = TestContext.Current.CancellationToken;
        var project = this.Hello("Checked");
        project.ProjectFile.Optimization = optimization;
        var paths = ArtifactPaths.Create(project);
        string? checkedIr = null;
        bool? emitted = null;
        this.console.LineAction = ("Built: ", () =>
        {
            checkedIr = ArtifactFiles.Hash(paths.Ir);
            var edited = this.Hello("Changed");
            emitted = Task.Run(() => edited.Generate(token)).GetAwaiter().GetResult();
        });
        Assert.True(await project.Build(token), this.console.Text);
        Assert.False(emitted, this.console.Text);
        Assert.Contains(nameof(DiagnosticCode.ArtifactPublicationFailed_Kd), this.console.Text, StringComparison.Ordinal);
        Assert.Equal(checkedIr, ArtifactFiles.Hash(paths.Ir));
        using (var record = JsonDocument.Parse(File.ReadAllText(paths.Record)))
        {
            Assert.Equal(checkedIr, record.RootElement.GetProperty("irSha256").GetString());
        }

        using var run = Process.Start(new ProcessStartInfo(paths.Executable) { RedirectStandardOutput = true, CreateNoWindow = true })!;
        Assert.Equal("Checked", (await run.StandardOutput.ReadToEndAsync(token)).Trim());
        await run.WaitForExitAsync(token);
    }

    // Tatara G1203 (IMPL 20.3.2): build --Manifest builds the pair as emit writes it, the Name.ll beside Name.link.json, so a manifest
    // naming another IR fails at the pair's path check before any IR is opened, and writes only its own record's invalidation and lock.
    // Each row builds an output (OutputPath out/<output>.ll) and copies its manifest to <copy>.link.json, which it builds while the
    // output's lock is held: Alt.link.json names Hello.ll (dev 3245a057d rewrote Hello.O2.exe, .obj and .ll under Hello's held lock);
    // Y.link.json names Y.O2.ll, the optimized IR its own name gives (review C-450 B1; dev built Y.O2.O2.*). Every file of the output
    // keeps its write time. On the installed toolchain.
    [Theory]
    [InlineData("Hello", "Alt")]
    [InlineData("Y.O2", "Y")]
    public async Task AManifestNamingAnotherIrFailsAtThePathCheck(string output, string copy)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var token = TestContext.Current.CancellationToken;
        var project = this.Hello("Hello");
        project.ProjectFile.OutputPath = "out/" + output + ".ll";
        Assert.True(await project.Build(token), this.console.Text);
        var paths = ArtifactPaths.Create(project);
        var directory = Path.GetDirectoryName(paths.Manifest)!;
        var before = Directory.GetFileSystemEntries(directory).ToDictionary(x => x, File.GetLastWriteTimeUtc);
        var manifest = Path.Combine(directory, copy + ".link.json");
        File.Copy(paths.Manifest, manifest);
        Exception? error;
        using (new FileStream(Path.ChangeExtension(paths.Record, ".lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            error = await Record.ExceptionAsync(() => NativeToolchain.BuildManifest(manifest, (_, _) => { }, token));
        }

        Assert.StartsWith("IR/manifest path mismatch", Assert.IsType<InvalidDataException>(error).Message, StringComparison.Ordinal);
        Assert.All(before, x => Assert.Equal(x.Value, File.GetLastWriteTimeUtc(x.Key)));
        string[] written = [manifest, Path.Combine(directory, copy + ".link.build.json"), Path.Combine(directory, copy + ".link.build.lock")];
        Assert.Equal(before.Keys.Concat(written).Order(StringComparer.Ordinal), Directory.GetFileSystemEntries(directory).Order(StringComparer.Ordinal));
    }

    // Tatara G1203's case rule: the pair's path check compares as Windows compares paths, so an emitted pair named in another case builds
    // into the same files, the executable unchanged: the manifest given as hello.link.json (its IR hello.ll), and the manifest
    // Hello.link.json of OutputPath Hello.LL (its IR Hello.ll, which the manifest names Hello.LL). On the installed toolchain.
    [Theory]
    [InlineData("Hello.ll", "hello.link.json")]
    [InlineData("Hello.LL", "Hello.link.json")]
    public async Task AManifestNamingItsIrInAnotherCaseBuilds(string ir, string manifest)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var token = TestContext.Current.CancellationToken;
        var project = this.Hello("Hello");
        project.ProjectFile.OutputPath = "out/" + ir;
        Assert.True(await project.Build(token), this.console.Text);
        var paths = ArtifactPaths.Create(project);
        var (executable, written) = (ArtifactFiles.Hash(paths.Executable), File.GetLastWriteTimeUtc(paths.Executable));
        await NativeToolchain.BuildManifest(Path.Combine(Path.GetDirectoryName(paths.Manifest)!, manifest), (_, _) => { }, token);
        Assert.NotEqual(written, File.GetLastWriteTimeUtc(paths.Executable));
        Assert.Equal(executable, ArtifactFiles.Hash(paths.Executable));
    }

    // IMPL 21.4, read as the ABI's unwind information (T's ruling): an entry for each function that allocates stack, saves a nonvolatile
    // register or calls, none for a frameless leaf; so the object inspection judges no unwind entry and keeps its other refusals. Each row
    // builds at O2: forever's loop folds __kimi_start into a frameless leaf, its IR without the memory part (the runtime's memcmp,
    // memcpy, memmove and memset, which every module defines and which save nonvolatile registers at O2; Tatara B7r4) built from its
    // manifest, so no function of the object has an entry: its executable has no exception table, and it builds (dev 9ec1e3d02 refused
    // it: "Invalid _fltused definition, unwind information or hidden default library."); its twin that calls builds with entries; that
    // twin's IR naming a default library (a linker option), built from its manifest, is refused before the link, its object released
    // (Intermediates). An edited IR's manifest takes its irSha256. On the installed toolchain.
    [Theory]
    [InlineData("leaf")]
    [InlineData("call")]
    [InlineData("default library")]
    public async Task TheObjectInspectionJudgesNoUnwindEntry(string row)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var token = TestContext.Current.CancellationToken;
        var project = row == "leaf" ? this.Main("func forever(a: ref/string) -> Never\n    loop => ()\nlet a = (\"held\", 0)\nforever(a.0)\n") : this.Hello("Hello");
        var paths = ArtifactPaths.Create(project);
        Assert.True(row == "call" ? await project.Build(token) : await project.Generate(token), this.console.Text);
        if (row != "call")
        {
            var ir = File.ReadAllText(paths.Ir);
            var edited = row == "leaf"
                ? ir.Replace(LlvmModuleText.MemoryRuntime, string.Empty, StringComparison.Ordinal)
                : ir + "\n!llvm.linker.options = !{!9000}\n!9000 = !{!\"/DEFAULTLIB:libcmt.lib\"}\n";
            Assert.NotEqual(ir, edited);
            File.WriteAllText(paths.Ir, edited);
            var manifest = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(paths.Manifest))!.AsObject();
            manifest["irSha256"] = ArtifactFiles.Hash(paths.Ir);
            File.WriteAllText(paths.Manifest, manifest.ToJsonString());
        }

        if (row != "default library")
        {
            if (row == "leaf")
            {
                await NativeToolchain.BuildManifest(paths.Manifest, (_, _) => { }, token);
            }

            using var executable = new PEReader(File.OpenRead(paths.Executable));
            Assert.Equal(row == "call", executable.PEHeaders.PEHeader!.ExceptionTableDirectory.Size != 0);
            return;
        }

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => NativeToolchain.BuildManifest(paths.Manifest, (_, _) => { }, token));
        Assert.Equal("Invalid _fltused definition or hidden default library.", error.Message);
        Assert.False(File.Exists(paths.Executable));
        File.Delete(paths.Stem + ".obj"); // The failed build released its object (Intermediates).
    }

    // IMPL 20.3.2 and 20.4 (a native build reads no toolchain root and uses no search path; T's ruling 1 on LC3 Revision 1, a defect
    // fix): the executable is the one the record's inputs determine, whatever LINK or _LINK_ holds; LLD otherwise adds either to its
    // arguments (LC3's P-D: _LINK_=/filealign:4096 linked 74bdf2c3... against e201e8ab...). The environment is the process's and other
    // tests link in this one, so the child HelloUnderALinkerVariable builds Hello under the variable and expects this process's
    // executable (O2: the folder enters no byte). On the installed toolchain.
    [Theory]
    [InlineData("_LINK_")]
    [InlineData("LINK")]
    public async Task TheLinkReadsNoLinkerVariable(string variable)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var (_, _, v1) = await this.BuildHello();
        await FreshProcess.RunExplicit(typeof(NativeToolchainTest), nameof(NativeToolchainTest.HelloUnderALinkerVariable), (variable, "/filealign:4096"), (ExpectedExecutable, v1));
    }

    // TheLinkReadsNoLinkerVariable's child: Hello built in its own folder links the executable its parent passes.
    [Fact(Explicit = true)]
    public async Task HelloUnderALinkerVariable()
    {
        var (_, _, executable) = await this.BuildHello();
        Assert.Equal(Environment.GetEnvironmentVariable(ExpectedExecutable), executable);
    }

    // IMPL 20.4 (Tatara B6a): a build stages the compiler's supplies as it stages a program's input and links those snapshots. The
    // record's libraries rows name the staged files (<stem>.native/<hash>.lib) with the catalog's hashes, and it has no kernel32 member.
    // A second build reuses both copies by their catalog hashes and writes nothing in the folder (LC3 S1): denied creating files there,
    // it builds with the first build's rows and executable (the base failed creating its temporary copy). A third, after the staged
    // kernel32 file was overwritten, replaces it and links the first build's executable. The success record's members are exactly these
    // (PLAN G1227: no toolchain-verification constants). On the installed toolchain.
    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task ABuildLinksTheCompilersSuppliesAsStagedSnapshots()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var (project, paths, v1) = await this.BuildHello();
        var staged = Windows64Supplies().Select(x => (x.Supply.Name, Path: Path.Combine(paths.Native, x.Files[0].Sha256 + ".lib"), x.Files[0].Sha256)).ToArray();
        string libraries;
        using (var record = JsonDocument.Parse(File.ReadAllText(paths.Record)))
        {
            Assert.Equal(
                "status,compilerVersion,llvmVersion,input,tools,irSha256,optimization,libraries,executable,executableSha256,objectUndefinedSymbols",
                string.Join(',', record.RootElement.EnumerateObject().Select(static x => x.Name)));
            Assert.Equal(
                staged.Select(x => x.Name + " " + NativeToolchain.Redact(x.Path, this.directory) + " " + x.Sha256),
                record.RootElement.GetProperty("libraries").EnumerateArray().Select(static x =>
                    x.GetProperty("name").GetString() + " " + x.GetProperty("path").GetString() + " " + x.GetProperty("sha256").GetString()));
            libraries = record.RootElement.GetProperty("libraries").GetRawText();
        }

        Assert.All(staged, static x => Assert.Equal(x.Sha256, ArtifactFiles.Hash(x.Path)));
        var allow = DenyCreatingFiles(new DirectoryInfo(paths.Native));
        try
        {
            Assert.True(await project.Build(TestContext.Current.CancellationToken), this.console.Text);
            using var record = JsonDocument.Parse(File.ReadAllText(paths.Record));
            Assert.Equal(libraries, record.RootElement.GetProperty("libraries").GetRawText());
            Assert.Equal(v1, record.RootElement.GetProperty("executableSha256").GetString());
            Assert.Equal(v1, ArtifactFiles.Hash(paths.Executable));
        }
        finally
        {
            allow();
        }

        File.WriteAllText(staged[0].Path, "overwritten");
        Assert.True(await project.Build(TestContext.Current.CancellationToken), this.console.Text);
        Assert.Equal(staged[0].Sha256, ArtifactFiles.Hash(staged[0].Path));
        Assert.Equal(v1, ArtifactFiles.Hash(paths.Executable));
    }

    // IMPL 20.4 "Each input is hashed when read" (LC3 S1's boundary, its design §5; C-508 M1): a program's supply is copied into its
    // snapshot before its hash is known, so a later build denied creating files in <stem>.native fails creating that copy, an output
    // failure, where the compiler's supplies alone are reused without one (ABuildLinksTheCompilersSuppliesAsStagedSnapshots). On the
    // installed toolchain.
    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task AProgramsSupplyIsCopiedBeforeItIsHashed()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var project = await this.Codec("codec", true);
        Assert.True(await project.Build(TestContext.Current.CancellationToken), this.console.Text);
        var folder = ArtifactPaths.Create(project).Native;
        var allow = DenyCreatingFiles(new DirectoryInfo(folder));
        try
        {
            Assert.False(await project.Build(TestContext.Current.CancellationToken), this.console.Text);
        }
        finally
        {
            allow();
        }

        Assert.Matches($@"Access to the path '{Regex.Escape(folder)}\\[0-9a-f]{{32}}\.tmp' is denied\.", this.console.Text);
    }

    // IMPL 20.4 "its hash assertions are checked" (Tatara B6a): a compiler's supply's manifest entry may assert a SHA-256 as a program's
    // may, and the build checks it against the staged bytes: the catalog's hash builds, another fails before the link. Also on a build
    // that reuses the copies an earlier build staged (LC3 S1: the hash is then the catalog's; C-508 M2). A failed build releases the
    // copies it held, so their folder can be deleted (StagedHold). On the installed toolchain.
    [Theory]
    [InlineData("kernel32", true, false)]
    [InlineData("kernel32", false, false)]
    [InlineData("kimi_backend", true, false)]
    [InlineData("kimi_backend", false, false)]
    [InlineData("kimi_backend", true, true)]
    [InlineData("kimi_backend", false, true)]
    public async Task ACompilerSupplysHashAssertionIsChecked(string name, bool adopted, bool reused)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var project = this.Create();
        Assert.True(await project.Generate(TestContext.Current.CancellationToken));
        var paths = ArtifactPaths.Create(project);
        if (reused)
        {
            await NativeToolchain.BuildManifest(paths.Manifest, (_, _) => { }, TestContext.Current.CancellationToken);
            File.Delete(paths.Executable);
        }

        var manifest = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(paths.Manifest))!.AsObject();
        var entry = manifest["libraries"]!.AsArray().Single(x => x!["name"]!.GetValue<string>() == name)!;
        entry["sha256"] = adopted ? Array.Find(ToolchainIdentity.Supplies, x => x.Supply.Name == name)!.Files[0].Sha256 : new string('0', 64);
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
            Directory.Delete(paths.Native, recursive: true);
        }
    }

    // IMPL 20.4 "the linker receives those bytes" (D-1173; StagedHold): a native build holds each staged library with read sharing only
    // from its check to its end, so no foreign change of the copy falls between the hash its record names and LLD's open. A first build
    // holds the copy it has just staged, a later build the copy it reuses. The hook: the pair's IR asks LLVM to warn of every stack frame
    // (WarnAtEmit), and Tatara reports emit's warning when emit returns, after the staging and before the link; there the staged kernel32
    // is written, replaced (a rename over it) or locked (opened without read sharing until the build returns). The change is refused and
    // the build links the held bytes: the rows name the catalog's hashes, which the copies still have, and a later build links the first
    // build's executable; after the build the same change succeeds. Fail-before (dev a61063573): every change succeeded, and LLD then
    // failed on the changed kernel32 or, locked, linked without it (an input it cannot open is dropped without an error) and failed on
    // its imports. On the installed toolchain.
    [Theory]
    [InlineData("write", false)]
    [InlineData("replace", true)]
    [InlineData("lock", false)]
    public async Task AStagedLibraryIsHeldFromItsCheckThroughTheLink(string change, bool later)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var token = TestContext.Current.CancellationToken;
        var project = this.Hello("Held");
        project.ProjectFile.Optimization = "O0";
        Assert.True(await project.Generate(token), this.console.Text);
        var paths = ArtifactPaths.Create(project);
        WarnAtEmit(paths);
        var kernel32 = Path.Combine(paths.Native, Array.Find(ToolchainIdentity.Supplies, x => x.Supply.Name == "kernel32")!.Files[0].Sha256 + ".lib");
        string? v1 = null;
        if (later)
        {
            await NativeToolchain.BuildManifest(paths.Manifest, (_, _) => { }, token);
            v1 = ArtifactFiles.Hash(paths.Executable);
        }

        var hooked = false;
        Exception? refused = null;
        FileStream? locked = null;
        Exception? error;
        try
        {
            error = await Record.ExceptionAsync(() => NativeToolchain.BuildManifest(
                paths.Manifest,
                (severity, _) =>
                {
                    if (severity == DiagnosticSeverity.Warning && !hooked)
                    {
                        hooked = true;
                        refused = Record.Exception(() => { locked = Change(); });
                    }
                },
                token));
        }
        finally
        {
            locked?.Dispose();
        }

        Assert.True(hooked, "LLVM reported no stack-size warning.");
        Assert.True(refused is not null && ArtifactFiles.IsOutputFailure(refused), $"The {change} of the staged kernel32 was not refused: {error?.Message ?? "the build succeeded."}");
        Assert.Null(error?.Message);
        using (var record = JsonDocument.Parse(File.ReadAllText(paths.Record)))
        {
            Assert.Equal(Windows64Supplies().Select(static x => x.Files[0].Sha256), record.RootElement.GetProperty("libraries").EnumerateArray().Select(static x => x.GetProperty("sha256").GetString()));
        }

        Assert.All(Windows64Supplies(), x => Assert.Equal(x.Files[0].Sha256, ArtifactFiles.Hash(Path.Combine(paths.Native, x.Files[0].Sha256 + ".lib"))));
        if (later)
        {
            Assert.Equal(v1, ArtifactFiles.Hash(paths.Executable));
        }

        Change()?.Dispose();

        // The foreign change of the staged kernel32; a lock is returned open.
        FileStream? Change()
        {
            if (change == "lock")
            {
                return new FileStream(kernel32, FileMode.Open, FileAccess.Read, FileShare.None);
            }

            var written = change == "write" ? kernel32 : Path.Combine(this.directory, "changed.lib");
            File.WriteAllText(written, "changed");
            if (written != kernel32)
            {
                File.Move(written, kernel32, overwrite: true);
            }

            return null;
        }
    }

    // IMPL 20.3.2 "A native build compiles the IR its record names" (Intermediates): the build writes its O2 text and object from the
    // bytes Tatara returns and holds each from its creation, the text until emit has read it and the object to the build's end, so
    // emit, the inspection and LLD read what the build wrote. Each row builds Changed at its level and keeps its object, then builds
    // Held from its manifest, its IR asking LLVM to warn of every stack frame (WarnAtEmit): Tatara reports emit's warning when emit
    // returns. "obj": at the warning (before the build creates the object) and at "Built: " (after the link, the object still held)
    // Changed's object is written over Held's; "ll": at the warning (the text still held over emit's call) the text is overwritten.
    // The last change is refused and the build read its own bytes: the executable prints Held, or the text is the redacted optimized
    // module; after the build the same change succeeds. Fail-before (StagedHold 6402e91e3): every change succeeded, and the obj rows'
    // executables printed Changed while their records named Held's IR. On the installed toolchain.
    [Theory]
    [InlineData("O0", "obj")]
    [InlineData("O2", "obj")]
    [InlineData("O2", "ll")]
    public async Task AnIntermediateIsHeldFromItsCreationThroughItsLastRead(string level, string file)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var token = TestContext.Current.CancellationToken;
        var changed = this.Hello("Changed");
        changed.ProjectFile.Optimization = level;
        Assert.True(await changed.Build(token), this.console.Text);
        var project = this.Hello("Held");
        project.ProjectFile.Optimization = level;
        Assert.True(await project.Generate(token), this.console.Text);
        var paths = ArtifactPaths.Create(project);
        WarnAtEmit(paths);
        var intermediate = paths.Stem + "." + file;
        var bytes = file == "obj" ? File.ReadAllBytes(intermediate) : "changed"u8.ToArray();
        var hooked = false;
        Exception? refused = null;
        var error = await Record.ExceptionAsync(() => NativeToolchain.BuildManifest(
            paths.Manifest,
            (severity, text) =>
            {
                if (severity == DiagnosticSeverity.Warning && !hooked)
                {
                    hooked = true;
                    refused = Record.Exception(Change);
                }
                else if (file == "obj" && text.StartsWith("Built: ", StringComparison.Ordinal))
                {
                    refused = Record.Exception(Change);
                }
            },
            token));

        Assert.True(hooked, "LLVM reported no stack-size warning.");
        Assert.Null(error?.Message);
        // What the build read: the executable's text (the object LLD linked) or the text's first line (the text emit compiled).
        string read;
        if (file == "obj")
        {
            using var run = Process.Start(new ProcessStartInfo(paths.Executable) { RedirectStandardOutput = true, CreateNoWindow = true })!;
            read = (await run.StandardOutput.ReadToEndAsync(token)).Trim();
            await run.WaitForExitAsync(token);
        }
        else
        {
            read = File.ReadLines(intermediate).First();
        }

        Assert.True(
            refused is not null && ArtifactFiles.IsOutputFailure(refused) && read.StartsWith(file == "obj" ? "Held" : "; ModuleID = '/_/project", StringComparison.Ordinal),
            $"The {file}'s last change was {(refused is null ? "accepted" : "refused")}, and the build read \"{read}\".");
        Change();

        // The foreign change of the intermediate, Changed's object or another text, by a writer that shares everything (as an editor's or
        // a sync client's may), so only the hold's own sharing refuses it.
        void Change()
        {
            using var writer = new FileStream(intermediate, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            writer.Write(bytes);
        }
    }

    // IMPL 20.4, 21.4.7 (the Tatara plan's decision 24, change W5; B7r4): a supplied static library's member that the link pulls (for
    // codec_add) and that gives a non-weak memcpy beside the module's fails the link whatever the library's name, before kimi_backend in
    // name order or after it: LLD's duplicate between the module's object and the staged library's member; the member that only defines
    // memset, never pulled, adds nothing. The same member's weak memcpy yields to the module's, and the program runs (C-445 C3's probe).
    // The program's Array growth copies through memcpy. Fail-before: codec built with the library's memcpy, and zlib's duplicate was the
    // backend archive's member's.
    [Theory]
    [InlineData("codec", false)]
    [InlineData("zlib", false)]
    [InlineData("codec", true)]
    public async Task ASuppliedMemoryFunctionIsADuplicateOfTheModules(string name, bool weak)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var project = await this.Codec(name, weak);
        var built = await project.Build(TestContext.Current.CancellationToken);
        if (weak)
        {
            Assert.True(built, this.console.Text);
            var start = new ProcessStartInfo(ArtifactPaths.Create(project).Executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            using var process = Process.Start(start)!;
            Assert.Equal("ffi ok\n", await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken));
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(0, process.ExitCode);
            return;
        }

        Assert.False(built, this.console.Text);
        var text = this.console.Text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var module = Path.Combine(this.directory, "out", "Hello.O2.obj");
        var staged = ArtifactFiles.Hash(Path.Combine(this.directory, "native", name + ".lib")) + ".lib"; // LLD names the staged copy by its file name.
        Assert.Contains($"Tatara failed: link\nlld-link: error: duplicate symbol: memcpy\n>>> defined at {module}\n>>> defined at {staged}(needed.obj)\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("memset", text, StringComparison.Ordinal);
        Assert.False(File.Exists(ArtifactPaths.Create(project).Executable));
    }

    // LinkAmbiguity (IMPL 20.4: an ambiguous native dependency is an error, never resolved by link order; design §1, Revisions 0-3a, its
    // labels and base cells; fail-before: artifacts\tatara\linkambiguity-b\fail-before.txt): a symbol the link depends on, which no file
    // loaded whole defines and two or more libraries define (each library's pick: the member its index names), fails the build before
    // the link with one line per symbol, the record left incomplete and no executable, whatever the names (before and after kernel32
    // and kimi_backend in name order) and at either level (rows 29, 30). Each row's libraries are Dependency's; row 22, which also has
    // a Kind line, is AnInputInconsistentWithItsKindFailsBeforeTheLink's. LinkRoots' rows (LinkRoots design §1, Revision 1a, its labels
    // and base cells; fail-before: artifacts\tatara\linkroots\fail-before.txt): a name a loaded member's /export: roots (31; 31b, clang's
    // /EXPORT: of a dllexport definition: T's ruling 64), the link's own root _load_config_used (32: R-L) and a strong reference of the
    // member that root loads (33); on the base each built, with the first library's definition.
    [Theory]
    [InlineData("3", "codec", "O2", "'GetLastError': defined by the libraries codec and kernel32.")]
    [InlineData("3", "zlib", "O2", "'GetLastError': defined by the libraries kernel32 and zlib.")]
    [InlineData("4", "codec", "O2", "'__imp_WriteFile': defined by the libraries codec and kernel32.")]
    [InlineData("4", "zlib", "O2", "'__imp_WriteFile': defined by the libraries kernel32 and zlib.")]
    [InlineData("6", "codec", "O2", "'__chkstk': defined by the libraries codec and kimi_backend.")]
    [InlineData("6", "zlib", "O2", "'__chkstk': defined by the libraries kimi_backend and zlib.")]
    [InlineData("7", "alpha", "O2", "'codec_add': defined by the libraries alpha and codec.")]
    [InlineData("7", "zlib", "O2", "'codec_add': defined by the libraries codec and zlib.")]
    [InlineData("8", "alpha", "O2", "'f': defined by the libraries alpha and codec.")]
    [InlineData("15", "alpha", "O2", "'f': defined by the libraries alpha and codec.")]
    [InlineData("17", "codec", "O2", "'codec_add': defined by the libraries codec and codec2.\nAmbiguous native symbol 'codec_sub': defined by the libraries codec and codec2.")]
    [InlineData("23", "codec", "O2", "'__imp_VirtualProtect': defined by the libraries codec and kernel32.")]
    [InlineData("24", "codec", "O2", "'VirtualProtect': defined by the libraries codec and kernel32.")]
    [InlineData("24", "zlib", "O2", "'VirtualProtect': defined by the libraries kernel32 and zlib.")]
    [InlineData("25b", "codec", "O2", "'VirtualProtect': defined by the libraries kernel32 and zlib.")]
    [InlineData("26", "alpha", "O2", "'g': defined by the libraries alpha and codec.")]
    [InlineData("27", "alpha", "O2", "'f': defined by the libraries alpha and codec.")]
    [InlineData("28", "alpha", "O2", "'b': defined by the libraries alpha and codec.")]
    [InlineData("29", "alpha", "O0", "'codec_add': defined by the libraries alpha and codec.")]
    [InlineData("29", "alpha", "O2", "'codec_add': defined by the libraries alpha and codec.")]
    [InlineData("30", "codec", "O0", "'__chkstk': defined by the libraries codec and kimi_backend.")]
    [InlineData("30", "codec", "O2", "'__chkstk': defined by the libraries codec and kimi_backend.")]
    [InlineData("31", "alpha", "O2", "'foo': defined by the libraries alpha and codec.")]
    [InlineData("31b", "alpha", "O2", "'foo': defined by the libraries alpha and carrier.")]
    [InlineData("32", "alpha", "O2", "'_load_config_used': defined by the libraries alpha and codec.")]
    [InlineData("33", "alpha", "O2", "'lc_helper': defined by the libraries alpha and codec.")]
    public async Task AnAmbiguousDependencyFailsBeforeTheLink(string row, string name, string level, string line)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        await this.AssertFailsBeforeTheLink(await this.Dependency(row, name, level), "Ambiguous native symbol " + line);
    }

    // Kind (IMPL 20.4: a native dependency inconsistent with its declared Kind is an error; kind-design.md §1, Revision 0 with its
    // corrections 1-3, its labels and base cells; fail-before: artifacts\tatara\linkkind\fail-before.txt): a unit the link loads (a
    // library input that is no archive, each library's pick) that its library's Kind does not admit fails the build before the link with
    // every line in its order (§3), the record left incomplete and no executable. Row 22 is LinkAmbiguity's (a DLL as static zlib), which
    // gains its Kind line. k5w is row 5's object as the input itself (an import member the link loads whole); k2p is row 2 twice (two
    // static import libraries, each defining both called symbols: a line per library naming both, and the ambiguity lines). k9's second
    // line is NamedProvider's rule P (provider-design.md §1 row 23: K and P are two failures). On the base rows k2, k3, k5, k5w, k9 and
    // k11 built (k9 with LLD's LNK4217 warning), k14 failed in LLD (an unknown file type), and 22 and k2p lacked their Kind lines.
    // LinkRoots' k3x and k11lc (LinkRoots design §1, K's note): an import member loaded only through a loaded member's /export: (k3x)
    // and code loaded only through the link's root _load_config_used (k11lc) are judged as every pick is; on the base both built.
    // k11lcb (C-572 N2) is k11lc with its stub's member LLVM bitcode, unsupported (B) and invalid (Kind): a bitcode unit keeps class
    // Code, so K judges it apart from B and its library gets both lines.
    [Theory]
    [InlineData("k2", "codec", "Native library 'codec' is declared static, but the link would load import members from it for 'codec_add'.")]
    [InlineData("k2p", "codec", "Native library 'codec' is declared static, but the link would load import members from it for 'codec_add' and 'codec_sub'.\nNative library 'zlib' is declared static, but the link would load import members from it for 'codec_add' and 'codec_sub'.\nAmbiguous native symbol 'codec_add': defined by the libraries codec and zlib.\nAmbiguous native symbol 'codec_sub': defined by the libraries codec and zlib.")]
    [InlineData("k3", "codec", "Native library 'codec' is declared static, but the link would load import members from it for 'GetTickCount'.")]
    [InlineData("k5", "codec", "Native library 'codec' is declared static, but the link would load import members from it for 'codec_add'.")]
    [InlineData("k5w", "codec", "Native library 'codec' is declared static, but its input is an import member, which the link loads whole.")]
    [InlineData("k9", "codec", "Native library 'codec' is declared import, but its input is an object, which the link loads whole.\nNative function 'codec_add' is imported from codec, which does not define '__imp_codec_add'; codec defines 'codec_add'.")]
    [InlineData("k11", "codec", "Native library 'codec' is declared import, but the link would load code from it for 'codec_version'.")]
    [InlineData("k14", "codec", "Native library 'codec' is declared import, but its input is a linked image (a DLL or shared object), not a library.")]
    [InlineData("22", "alpha", "Native library 'zlib' is declared static, but its input is a linked image (a DLL or shared object), not a library.\nAmbiguous native symbol 'codec_add': defined by the libraries alpha and codec.")]
    [InlineData("k3x", "codec", "Native library 'codec' is declared static, but the link would load import members from it for 'GetTickCount'.")]
    [InlineData("k11lc", "lcfg", "Native library 'lcfg' is declared import, but the link would load code from it for '_load_config_used'.")]
    [InlineData("k11lcb", "lcfg", "Native library 'lcfg' holds LLVM bitcode that the link would load, which is not supported.\nNative library 'lcfg' is declared import, but the link would load code from it for '_load_config_used'.")]
    public async Task AnInputInconsistentWithItsKindFailsBeforeTheLink(string row, string name, string lines)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        await this.AssertFailsBeforeTheLink(await this.Dependency(row, name, "O2"), lines);
    }

    // LinkRoots (IMPL 20.4: an unsupported native dependency is an error; LinkRoots design §1, Revision 1a, its labels and base cells,
    // R-E, §2.4, §2.8 and T's ruling 67a; fail-before: artifacts\tatara\linkroots\fail-before.txt): a library whose loaded unit carries
    // /entry: (E: lld-link keeps or replaces the program's entry by the pass that loads the unit) or is LLVM bitcode (B: LTO's code
    // generation adds references no symbol table lists) fails the build before the link with its line, a pick's or a whole input's (34w,
    // 34we, 35w, 35wp) alike. The unit still joins, so its own references are resolved (34x, 35x), and rule P judges its library beside
    // the line (34p, 35p, 34w, 35wp); the /entry: name itself is not resolved (R-D: no ambiguity line for ent2, which alpha and codec
    // define in 34). Every line of the row, AssertFailsBeforeTheLink counting each (E and B begin with "Native library '"). On the base
    // 34 and 34we built and ran "ok", 34b built and started at ent2 (exit 11), 35 and 35w built (LTO's floor call taking the first
    // library's), and the other rows gave only their second line.
    [Theory]
    [InlineData("34", "carrier", "Native library 'carrier' sets the entry point to 'ent2' in a linker directive of an object that the link would load, which is not supported.")]
    [InlineData("34b", "lcfge", "Native library 'lcfge' sets the entry point to 'ent2' in a linker directive of an object that the link would load, which is not supported.")]
    [InlineData("34x", "carrier", "Native library 'carrier' sets the entry point to 'ent2' in a linker directive of an object that the link would load, which is not supported.\nAmbiguous native symbol 'zz': defined by the libraries alpha and codec.")]
    [InlineData("34p", "carrier", "Native library 'carrier' sets the entry point to 'ent2' in a linker directive of an object that the link would load, which is not supported.\nNative function 'carrier_g' is imported from carrier, which does not define 'carrier_g'; zeta defines 'carrier_g'.")]
    [InlineData("34w", "carrier", "Native library 'carrier' sets the entry point to 'ent2' in a linker directive of an object that the link would load, which is not supported.\nNative function 'carrier_g' is imported from carrier, which does not define 'carrier_g'; zeta defines 'carrier_g'.")]
    [InlineData("34we", "carrier", "Native library 'carrier' sets the entry point to 'ent2' in a linker directive of an object that the link would load, which is not supported.")]
    [InlineData("35", "alpha", "Native library 'alpha' holds LLVM bitcode that the link would load, which is not supported.")]
    [InlineData("35w", "alpha", "Native library 'alpha' holds LLVM bitcode that the link would load, which is not supported.")]
    [InlineData("35x", "alpha", "Native library 'alpha' holds LLVM bitcode that the link would load, which is not supported.\nAmbiguous native symbol 'zz': defined by the libraries codec and zeta.")]
    [InlineData("35p", "alpha", "Native library 'alpha' holds LLVM bitcode that the link would load, which is not supported.\nNative function 'alpha_g' is imported from alpha, which does not define 'alpha_g'; zeta defines 'alpha_g'.")]
    [InlineData("35wp", "alpha", "Native library 'alpha' holds LLVM bitcode that the link would load, which is not supported.\nNative function 'alpha_g' is imported from alpha, which does not define 'alpha_g'; zeta defines 'alpha_g'.")]
    public async Task AnUnsupportedLibraryFailsBeforeTheLink(string row, string name, string lines)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        await this.AssertFailsBeforeTheLink(await this.Dependency(row, name, "O2"), lines);
    }

    // Kind's valid rows (kind-design.md §1): an archive's import member no loaded unit references (k4), a bare object as a static input
    // (k6), an import library (k7), a mixed library whose loaded members are its imports (k10), a long-format import library, objects
    // whose definitions lie in .idata sections (k12; P-K3: built from clang objects), and k5w's object as an import input (k5wi): each
    // builds; k4 and k6 run (the others import from a codec.dll no test has).
    [Theory]
    [InlineData("k4", true)]
    [InlineData("k5wi", false)]
    [InlineData("k6", true)]
    [InlineData("k7", false)]
    [InlineData("k10", false)]
    [InlineData("k12", false)]
    public async Task AnInputConsistentWithItsKindBuilds(string row, bool runs)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var project = await this.Dependency(row, "codec", "O2");
        Assert.True(await project.Build(TestContext.Current.CancellationToken), this.console.Text);
        Assert.True(File.Exists(ArtifactPaths.Create(project).Executable));
        if (runs)
        {
            await RunsOk(ArtifactPaths.Create(project).Executable);
        }
    }

    // A bare short import file (row 2's import library's codec_add member alone, its bytes as llvm-lib /def writes them; C-544 PB3) is
    // no library, whatever its Kind (T's rulings 53 and 54): LLD refuses it with its fatal(), after which Tatara refuses every later
    // call of its process, so the step reports it before the link, as it reports an image input (k14). On a24ce9194 k2m (static) failed
    // before the link with the whole-input Import line and k7m (import) reached LLD's fatal, as both did on 330801e63; the rows have
    // their own theory so that a fail-before run on such a base runs them last.
    [Theory]
    [InlineData("k2m", "Native library 'codec' is declared static, but its input is a bare import member, not a library.")]
    [InlineData("k7m", "Native library 'codec' is declared import, but its input is a bare import member, not a library.")]
    public async Task AShortImportFileFailsBeforeTheLink(string row, string line)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        await this.AssertFailsBeforeTheLink(await this.Dependency(row, "codec", "O2"), line);
    }

    // NamedProvider (SPEC 22.6, IMPL 21.2: an import is resolved in the library it names; provider-design.md Revision 2 §1, its labels
    // and base cells, with T's rulings 61, 62 and 65; fail-before: artifacts\tatara\provider\fail-before.txt): a declared import whose
    // named library neither has an input the link loads whole defining its referenced form (__imp_X for an import library, X for a
    // static one) nor picks it fails the build before the link with one line, naming the libraries that define the form and, for an
    // import form, those that define the symbol, at every level alike (P reads the declarations, never the object: p14's only call O2
    // removes). Row p<n> is design row n (p2i: row 2 with codec an import library; p24b: row 24's member that codec's index does not
    // name, appended after the index); k8, k8o2, k13 and k13p are Kind's rows (design rows 5 and 4), whose K2 line P replaces. On the
    // base p2, p3, p2i and p14 at O2 built, p6, p14 at O0 and p24b failed in LLD (an undefined codec_add), and the k rows had K2's line.
    [Theory]
    [InlineData("p2", "codec", "O2", "Native function 'codec_add' is imported from codec, which does not define 'codec_add'; zlib defines 'codec_add'.")]
    [InlineData("p3", "codec", "O2", "Native function 'codec_add' is imported from codec, which does not define 'codec_add'; zlib defines 'codec_add'.")]
    [InlineData("p2i", "codec", "O2", "Native function 'codec_add' is imported from codec, which does not define '__imp_codec_add'; zlib defines '__imp_codec_add', and zlib defines 'codec_add'.")]
    [InlineData("k13", "codec", "O2", "Native function 'codec_add' is imported from codec, which does not define '__imp_codec_add'; zlib defines 'codec_add'.")]
    [InlineData("k13p", "codec", "O2", "Native function 'codec_add' is imported from codec, which does not define '__imp_codec_add'; alpha and zlib define 'codec_add'.")]
    [InlineData("k8", "codec", "O2", "Native function 'codec_add' is imported from codec, which does not define '__imp_codec_add'; codec defines 'codec_add'.")]
    [InlineData("k8o2", "codec", "O2", "Native function 'codec_add' is imported from codec, which does not define '__imp_codec_add'; codec defines 'codec_add'.")]
    [InlineData("p6", "codec", "O2", "Native function 'codec_add' is imported from codec, which does not define 'codec_add'.")]
    [InlineData("p14", "codec", "O0", "Native function 'codec_add' is imported from codec, which does not define 'codec_add'.")]
    [InlineData("p14", "codec", "O2", "Native function 'codec_add' is imported from codec, which does not define 'codec_add'.")]
    [InlineData("p24b", "codec", "O2", "Native function 'codec_add' is imported from codec, which does not define 'codec_add'.")]
    public async Task AnImportItsLibraryDoesNotDefineFailsBeforeTheLink(string row, string name, string level, string lines)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        await this.AssertFailsBeforeTheLink(await this.Dependency(row, name, level), lines);
    }

    // LinkAmbiguity's valid rows (design §1): an unreferenced definition (10), one library's two definers (11, its pick the member its
    // index names, 25a), a weak reference (16), COMDAT copies across libraries (20, 21) and an /alternatename: whose source a library
    // defines (28's twin) are no ambiguous dependency: each builds and runs. NamedProvider's p24a (provider-design.md §1 row 24's first
    // half): codec's m2 and m3 define codec_add and its pick, the member its index names (m3), defines it, so codec defines its import.
    // LinkRoots' valid rows (LinkRoots design §1): 31b's carrier alone, whose loaded member defines the foo it exports (31t), one
    // library's load configuration (33t), and an /entry: member and a bitcode member that no reference loads (34t, 35t: KR1). G1378:
    // no build leaves an import library beside its executable (lld-link writes <stem>.<guid>.lib beside an image that exports a
    // symbol, 31t's, unless /noimplib; the staged copies are in <stem>.native\).
    [Theory]
    [InlineData("10")]
    [InlineData("11")]
    [InlineData("16")]
    [InlineData("20")]
    [InlineData("21")]
    [InlineData("25a")]
    [InlineData("28t")]
    [InlineData("p24a")]
    [InlineData("31t")]
    [InlineData("33t")]
    [InlineData("34t")]
    [InlineData("35t")]
    public async Task AnUnambiguousDependencyBuilds(string row)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var project = await this.Dependency(row, "alpha", "O2");
        Assert.True(await project.Build(TestContext.Current.CancellationToken), this.console.Text);
        var paths = ArtifactPaths.Create(project);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(paths.Executable)!, Path.GetFileName(paths.Stem) + ".*.lib"));
        await RunsOk(paths.Executable);
    }

    // T's ruling 79 (c), design §3.11: the ELF line on both start paths, from the catalog's places: the libc-free path's -e and the
    // row's entry before its one input, the object; the C path's files around the object, crt1.o crti.o <obj> <program libraries> libc.a
    // crtn.o, and no -e (crt1.o defines _start).
    [Fact]
    public void TheElfLinePlacesItsInputsAroundTheObject()
    {
        string[] head = ["ld.lld", "-m", "elf_x86_64", "-static", "--no-dependent-libraries", "--gc-sections", "--build-id=none", "-o", "Hello.elf"];
        Assert.Equal([.. head, "-e", TargetProfile.Linux.EntrySymbol, "Hello.obj"], NativeToolchain.ElfLine(TargetProfile.Linux.EntrySymbol, "Hello.obj", [], "Hello.elf"));
        var libraries = new List<NativeToolchain.StagedLibrary>();
        try
        {
            foreach (var file in ToolchainIdentity.Of(TargetProfile.Linux.Supply("c"))!.Files)
            {
                libraries.Add(new("c", "static", file.Sha256, File.Create(Path.Combine(this.directory, file.Name)), file));
            }

            libraries.Add(new("codec", "static", "0", File.Create(Path.Combine(this.directory, "codec.a")), null));
            string[] inputs = ["crt1.o", "crti.o", "codec.a", "libc.a", "crtn.o"];
            var staged = inputs.Select(x => Path.Combine(this.directory, x)).ToArray();
            Assert.Equal([.. head, staged[0], staged[1], "Hello.obj", .. staged[2..]], NativeToolchain.ElfLine("main", "Hello.obj", libraries, "Hello.elf"));
        }
        finally
        {
            foreach (var library in libraries)
            {
                library.Held.Dispose();
            }
        }
    }

    // The Tatara plan's B7b7 (design §3.11, §5 row 21): a Linux pair builds a static ELF executable through build --Manifest at either
    // level. Hello, without a library, takes the libc-free path and starts at the row's entry; Ffi, whose codec.a (ELF relocatable
    // objects, Kind's row 21) calls musl's getpid, takes the C path: c's start files, the object, codec.a, libc.a and c's end file, the
    // process starting at crt1.o's _start. The manifest lists c before codec (Ordinal), Hello's no library; the record names one row per
    // staged input (T's ruling 49). Fail-before: B7b7a's head had no ELF link writer.
    [Theory]
    [InlineData("Hello", "O0")]
    [InlineData("Hello", "O2")]
    [InlineData("Ffi", "O0")]
    [InlineData("Ffi", "O2")]
    public async Task ALinuxPairBuildsAStaticElf(string program, string level)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var paths = await this.LinuxProgram(program, level);
        await LinuxPair.Build(paths);
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(paths.Manifest));
        string[] names = [.. LinkManifest.Items(manifest.RootElement, "libraries").Select(static x => LinkManifest.Text(x, "libraries.name"))];
        Assert.Equal(program == "Ffi" ? ["c", "codec"] : [], names);
        ElfFile.Read(paths.Executable).AssertStatic(program == "Ffi" ? "_start" : TargetProfile.Linux.EntrySymbol);
        using var record = JsonDocument.Parse(File.ReadAllText(paths.Record));
        Assert.Equal("linked", record.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            program == "Ffi" ? ["c .o", "c .o", "c .a", "c .o", "codec .a"] : [],
            record.RootElement.GetProperty("libraries").EnumerateArray().Select(static x => $"{x.GetProperty("name").GetString()} {Path.GetExtension(x.GetProperty("path").GetString())}"));
    }

    // The Tatara plan's B7b7 (design §3.11, §6.1): Linux pairs whose head, entry or object the row does not take fail before any link:
    // another relocation model (the fixed-field text), Ffi's entry edited to main with c removed and Hello's __kimi_start with c listed
    // (the profile text, per row; O8), an object calling bcmp, which no declaration names (the IR edited, its hash rewritten), and Ffi's c
    // removed with its entry edited to the row's __kimi_start, which its object does not define (Inspect's entry check, T's ruling 79;
    // ld.lld only warns of an undefined -e entry: on 0e7a59618 it built, artifacts\tatara\b7b7\b7b7c\probe-twin-0e7a59618.txt). Two
    // builds of each pair in two folders give equal executables.
    [Theory]
    [InlineData("relocation", "'codegen.relocationModel' is not this compiler's")]
    [InlineData("main", "Manifest does not describe the supported x86_64-unknown-linux-musl Application profile.")]
    [InlineData("start", "Manifest does not describe the supported x86_64-unknown-linux-musl Application profile.")]
    [InlineData("bcmp", "Unsupported actual object dependency: bcmp U 0 0")]
    [InlineData("entry", "The object does not define the manifest's entry '__kimi_start' as an external function.")]
    public async Task ALinuxPairTheRowDoesNotTakeFails(string edit, string failure)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var paths = await this.LinuxProgram(edit == "start" ? "Hello" : "Ffi", "O2");
        var manifest = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(paths.Manifest))!.AsObject();
        var libraries = manifest["libraries"]!.AsArray();
        switch (edit)
        {
            case "relocation":
                manifest["codegen"]!["relocationModel"] = "pic";
                break;
            case "main" or "entry":
                libraries.Remove(libraries.Single(static x => x!["name"]!.GetValue<string>() == "c"));
                if (edit == "entry")
                {
                    manifest["entry"] = TargetProfile.Linux.EntrySymbol;
                }

                break;
            case "start":
                libraries.Add(System.Text.Json.Nodes.JsonNode.Parse("{\"name\": \"c\", \"kind\": \"static\", \"resolution\": \"toolchain\"}"));
                break;
            default:
                var ir = File.ReadAllText(paths.Ir);
                Assert.Contains("ret i32 0\n}\n", ir, StringComparison.Ordinal);
                File.WriteAllText(paths.Ir, ir.Replace("ret i32 0\n}\n", "%bcmp = call i32 @bcmp(ptr null, ptr null, i64 0)\n  ret i32 %bcmp\n}\n", StringComparison.Ordinal) + "declare i32 @bcmp(ptr, ptr, i64)\n");
                manifest["irSha256"] = ArtifactFiles.Hash(paths.Ir);
                break;
        }

        File.WriteAllText(paths.Manifest, manifest.ToJsonString());
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => LinuxPair.Build(paths));
        Assert.Contains(failure, error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(paths.Executable));
    }

    // §6.1: two builds of Hello's and Ffi's Linux pairs, each emitted and built in its own folder, give equal ELF bytes.
    [Theory]
    [InlineData("Hello")]
    [InlineData("Ffi")]
    public async Task LinuxBuildsInTwoFoldersAreEqual(string program)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var first = await this.LinuxProgram(program, "O2");
        await LinuxPair.Build(first);
        var other = new NativeToolchainTest();
        try
        {
            var second = await other.LinuxProgram(program, "O2");
            await LinuxPair.Build(second);
            Assert.Equal(File.ReadAllBytes(first.Executable), File.ReadAllBytes(second.Executable));
        }
        finally
        {
            other.Dispose();
        }
    }

    // LinkAmbiguity's and Kind's L rows (LA §1 rows 1, 2 and 22's L half; KD §1 row 22; T's ruling 48's row r48), each a Linux pair at O2
    // built through build --Manifest: the dependency step fails it before the link with exactly these lines, the record incomplete and no
    // executable. Rows 1 and 2: codec (zlib) defines write, which c's libc.a defines (on the base: a silent link with codec's write, P6).
    // Row 22: a shared object as static zlib.a beside row 7's ambiguity (on the base: "attempted static link of dynamic object"). KD 22:
    // a shared object as static codec.a. C-544 C2 (T's ruling 52): static codec.a archiving a shared object that the module's call picks
    // (ruling 24's line). r48: codec.a archived without a symbol index (llvm-ar rcS), which ld.lld would still load (a silent
    // pass). Fail-before: B7b7a's head had no ELF link writer, and the base no index line. NamedProvider's p12 (provider-design.md §1 row
    // 12): codec imports strlen, which codec.a lacks and c's libc.a defines (on the base: a silent link with libc's strlen, P-P3).
    // LinkRoots' L35 (LinkRoots design §1, §2.4): codec.a's one member is LLVM bitcode (on the base: a link through ld.lld's LTO).
    [Theory]
    [InlineData("1", "codec", "Ambiguous native symbol 'write': defined by the libraries c and codec.")]
    [InlineData("1", "zlib", "Ambiguous native symbol 'write': defined by the libraries c and zlib.")]
    [InlineData("22", "alpha", "Native library 'zlib' is declared static, but its input is a linked image (a DLL or shared object), not a library.\nAmbiguous native symbol 'codec_add': defined by the libraries alpha and codec.")]
    [InlineData("k22", "codec", "Native library 'codec' is declared static, but its input is a linked image (a DLL or shared object), not a library.")]
    [InlineData("c2", "codec", "Native library 'codec' holds a linked image (a DLL or shared object) as an archive member.")]
    [InlineData("r48", "codec", "Native library 'codec' is an archive that has no symbol index, which is not supported.")]
    [InlineData("p12", "codec", "Native function 'strlen' is imported from codec, which does not define 'strlen'; c defines 'strlen'.")]
    [InlineData("L35", "codec", "Native library 'codec' holds LLVM bitcode that the link would load, which is not supported.")]
    public async Task ALinuxDependencyFailsBeforeTheLink(string row, string name, string lines)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var paths = await this.LinuxDependency(row, name);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => LinuxPair.Build(paths));
        Assert.Equal(lines, error.Message);
        using var record = JsonDocument.Parse(File.ReadAllText(paths.Record));
        Assert.Equal("incomplete", record.RootElement.GetProperty("status").GetString());
        Assert.False(File.Exists(paths.Executable));
    }

    // LinkAmbiguity's valid L rows (LA §1 row 18; T's rulings 6 and 51: rows 11, 20 and 21's L halves): a program library's _start in a
    // member nothing pulls beside crt1.o's (18: c's start and end files load whole); one library's two definers (11: the index's first
    // entry, member order, the member ld.lld loads); selectany copies in two libraries (20, and 21, whose third member references g, so
    // both picks are GRP_COMDAT copies counting as one): each builds a static ELF at crt1.o's _start. NamedProvider's p10
    // (provider-design.md §1 row 10): c's write, which c defines through one of its four inputs (libc.a; T's ruling 62). LinkRoots'
    // L32t (LinkRoots design §1): alpha.a and codec.a each define _load_config_used, which an ELF link never roots.
    [Theory]
    [InlineData("18")]
    [InlineData("11")]
    [InlineData("20")]
    [InlineData("21")]
    [InlineData("p10")]
    [InlineData("L32t")]
    public async Task AnUnambiguousLinuxDependencyBuilds(string row)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var paths = await this.LinuxDependency(row, "alpha");
        await LinuxPair.Build(paths);
        ElfFile.Read(paths.Executable).AssertStatic("_start");
    }

    // Design §5 row 37 on the Linux row: a library's strong memcpy in the member pulled for codec_add is LLD's ELF duplicate on the C
    // path (the module defines memcpy; the dependency step never counts the module's definitions), and the never-pulled memset member adds
    // nothing.
    [Fact]
    public async Task ALinuxLibrarysStrongMemcpyIsADuplicate()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        const string Copy = "void *memcpy(void *d, const void *s, unsigned long long n) { char *x = d; const char *y = s; while (n--) *x++ = *y++; return d; }\n";
        await LinuxPair.Archive(this.directory, "codec", "rcs", ("needed", "int codec_add(int a, int b) { return a + b; }\n" + Copy), ("other", "void *memset(void *d, int c, unsigned long long n) { char *x = d; while (n--) *x++ = (char)c; return d; }\n"));
        var paths = LinuxPair.Publish(this.directory, LinuxSource([("codec", "codec_add")]), "O2", ("codec", "static", "codec.a"));
        var error = await Assert.ThrowsAsync<IOException>(() => LinuxPair.Build(paths));
        Assert.StartsWith("Tatara failed: link\n", error.Message, StringComparison.Ordinal);
        Assert.Contains("duplicate symbol: memcpy", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("memset", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(paths.Executable));
    }

    // Change W2 (design §4, R3, Reading 6; §5 row 18): a thin archive, whose members are files outside the staged copy, fails at staging
    // with its own text before the compile, on every row: a thin codec.a (Linux, llvm-ar T) and a thin codec.lib (Windows, llvm-lib
    // /llvmlibthin), exit 1, no executable. Fail-before: Tatara's inspect refused both ("thin archive"; LinkAmbiguity) after the compile.
    [Theory]
    [InlineData("linux")]
    [InlineData("windows")]
    public async Task AThinArchiveFailsAtStaging(string row)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        const string Text = "Native library 'codec' is a thin archive; its members are files outside the staged copy. Supply an archive that contains its members.";
        if (row == "linux")
        {
            await LinuxPair.Archive(this.directory, "codec", "rcsT", ("m1", "int codec_add(void) { return 3; }\n"));
            var paths = LinuxPair.Publish(this.directory, LinuxSource([("codec", "codec_add")]), "O2", ("codec", "static", "codec.a"));
            Assert.Equal(Text, (await Assert.ThrowsAsync<InvalidDataException>(() => LinuxPair.Build(paths))).Message);
            Assert.False(File.Exists(paths.Stem + ".obj"));
            Assert.False(File.Exists(paths.Executable));
            return;
        }

        var native = Directory.CreateDirectory(Path.Combine(this.directory, "native")).FullName;
        File.WriteAllText(Path.Combine(native, "m1.c"), "int codec_add(void) { return 3; }\n");
        Assert.Equal(string.Empty, await TataraExecutorTest.Tool("clang", native, "--target=" + TargetProfile.Windows.Target, "-O2", "-fno-autolink", "-c", "m1.c", "-o", "m1.obj"));
        Assert.Equal(string.Empty, await TataraExecutorTest.Tool("llvm-lib", native, "/llvmlibthin", "/out:codec.lib", "m1.obj"));
        var project = this.Main("group Native\n    #LibraryImport(\"codec\", \"codec_add\")\n    public unsafe func add() -> i32\npublic func main()\n    var v: i32 = 0\n    unsafe => v = Native.add()\n");
        project.ProjectFile.NativeLibraries[TargetProfile.Windows.Target] = new() { ["codec"] = new() { Kind = "static", Input = "native/codec.lib" } };
        Assert.False(await project.Build(TestContext.Current.CancellationToken), this.console.Text);
        Assert.Contains(Text, this.console.Text, StringComparison.Ordinal);
        Assert.False(File.Exists(ArtifactPaths.Create(project).Stem + ".obj"));
        Assert.False(File.Exists(ArtifactPaths.Create(project).Executable));
    }

    // LinkAmbiguity's store (design §4, N5): a staged input's facts are read once per process and kept by its staged SHA-256, so a second
    // build of the same library content, from another folder, reads the entry the first build kept; a compiler supply file's stored facts
    // take the same path, under its catalog hash (SupplyFacts, design §7 T3). The store is process-wide and tests run in parallel:
    // identity only, never absence or a count (T's ruling 4).
    [Fact]
    public async Task ASecondBuildReusesItsInputsFacts()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var project = await this.Dependency("10", "alpha", "O2");
        Assert.True(await project.Build(TestContext.Current.CancellationToken), this.console.Text);
        var hash = ArtifactFiles.Hash(Path.Combine(this.directory, "native", "codec.lib"));
        var first = NativeToolchain.LinkInputs[hash];
        var (kernel32, backend) = (NativeToolchain.LinkInputs[ToolchainIdentity.Kernel32Sha256], NativeToolchain.LinkInputs[ToolchainIdentity.BackendSha256]);
        var copy = Path.Combine(this.directory, "copy");
        Directory.CreateDirectory(Path.Combine(copy, "native"));
        foreach (var file in new[] { "Hello.kimiproj", "main.kimi", Path.Combine("native", "codec.lib") })
        {
            File.Copy(Path.Combine(this.directory, file), Path.Combine(copy, file));
        }

        Assert.True(Project.TryCreate(new Kimigayo(this.console), null, Path.Combine(copy, "Hello.kimiproj"), out var second));
        Assert.True(await second.Build(TestContext.Current.CancellationToken), this.console.Text);
        Assert.Same(first, NativeToolchain.LinkInputs[hash]);
        Assert.Same(kernel32, NativeToolchain.LinkInputs[ToolchainIdentity.Kernel32Sha256]);
        Assert.Same(backend, NativeToolchain.LinkInputs[ToolchainIdentity.BackendSha256]);
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
    // - CheckFaulted_Kd, the project's own result (Project.OwnResult): "project checks", which read a null target list, a caller
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
            project.ProjectFile.NativeLibraries[TargetProfile.Windows.Target] = new() { ["codec"] = new() { Kind = "static", Input = "native/codec.lib", Sha256 = new string('0', 64) } };
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

    // Denies the current user creating files in the folder, by a rule its files do not inherit, and returns the action that removes the
    // rule again (LC3 S1's row 3 and C-508 M1).
    [SupportedOSPlatform("windows")]
    private static Action DenyCreatingFiles(DirectoryInfo folder)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var rule = new FileSystemAccessRule(identity.User!, FileSystemRights.CreateFiles, AccessControlType.Deny);
        var security = folder.GetAccessControl();
        security.AddAccessRule(rule);
        folder.SetAccessControl(security);
        return () =>
        {
            security.RemoveAccessRule(rule);
            folder.SetAccessControl(security);
        };
    }

    // The pair's IR asks LLVM to warn of every stack frame ("warn-stack-size"="0" in the functions' attribute group) and its manifest
    // takes the edited IR's hash, so Tatara's emit reports a Warning on the build's report after the staging and before the link, when
    // emit has returned and before the build writes the object (StagedHold's and Intermediates' hook).
    private static void WarnAtEmit(ArtifactPaths paths)
    {
        var ir = File.ReadAllText(paths.Ir);
        Assert.Contains("attributes #0 = { ", ir, StringComparison.Ordinal);
        File.WriteAllText(paths.Ir, ir.Replace("attributes #0 = { ", "attributes #0 = { \"warn-stack-size\"=\"0\" ", StringComparison.Ordinal));
        var manifest = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(paths.Manifest))!.AsObject();
        manifest["irSha256"] = ArtifactFiles.Hash(paths.Ir);
        File.WriteAllText(paths.Manifest, manifest.ToJsonString());
    }

    // The executable runs, writes "ok" and exits with 0.
    private static async Task RunsOk(string executable)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        using var process = Process.Start(start)!;
        Assert.Equal("ok\n", await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken));
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, process.ExitCode);
    }

    // windows-x64's compiler supplies (kernel32, kimi_backend), one file each.
    private static IEnumerable<CompilerSupply> Windows64Supplies() => ToolchainIdentity.Supplies.Where(static x => x.Row == TargetProfile.Windows);

    // A Linux program with an import of each (library, symbol) as int name(void), which writes "ok" (LinuxPair's source form).
    private static string LinuxSource((string Library, string Symbol)[] imports)
        => "group Native\n" + string.Concat(imports.Select(static (x, i) => $"    #LibraryImport(\"{x.Library}\", \"{x.Symbol}\")\n    public unsafe func i{i}() -> i32\n")) +
            "public func main()\n    var v: i32 = 0\n" + string.Concat(imports.Select(static (_, i) => $"    unsafe => v = Native.i{i}()\n")) + "    Console.writeLine(\"ok\")\n";

    // The dependency step fails the build before the link with exactly these lines in this order (LinkAmbiguity, Kind, NamedProvider):
    // the record incomplete, no executable.
    private async Task AssertFailsBeforeTheLink(Project project, string lines)
    {
        Assert.False(await project.Build(TestContext.Current.CancellationToken), this.console.Text);
        var text = this.console.Text.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains(lines + "\n", text, StringComparison.Ordinal);
        Assert.Equal(lines.Split('\n').Length, Regex.Count(text, "Ambiguous native symbol |Native library '|Native function '"));
        Assert.DoesNotContain("lld-link", text, StringComparison.Ordinal);
        using var record = JsonDocument.Parse(File.ReadAllText(ArtifactPaths.Create(project).Record));
        Assert.Equal("incomplete", record.RootElement.GetProperty("status").GetString());
        Assert.False(File.Exists(ArtifactPaths.Create(project).Executable));
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

    // Hello.kimiproj whose main.kimi calls codec_add from native/<name>.lib, a program's static supply built here: a member defining
    // codec_add and memcpy (weak or not), and one defining only memset. The program also grows an Array and writes "ffi ok".
    private async Task<Project> Codec(string name, bool weak)
    {
        var native = Path.Combine(this.directory, "native");
        Directory.CreateDirectory(native);
        const string Copy = "void *memcpy(void *d, const void *s, unsigned long long n) { char *x = d; const char *y = s; while (n--) *x++ = *y++; return d; }\n";
        File.WriteAllText(Path.Combine(native, "needed.c"), "int codec_add(int a, int b) { return a + b; }\n" + (weak ? "__attribute__((weak)) " : string.Empty) + Copy);
        File.WriteAllText(Path.Combine(native, "other.c"), "void *memset(void *d, int c, unsigned long long n) { char *x = d; while (n--) *x++ = (char)c; return d; }\n");
        foreach (var member in new[] { "needed", "other" })
        {
            string[] compile = ["--target=" + TargetProfile.Windows.Target, "-O2", "-fno-builtin", "-fno-autolink", "-fno-stack-protector", "-c", member + ".c", "-o", member + ".obj"];
            Assert.Equal(string.Empty, await TataraExecutorTest.Tool("clang", native, compile));
        }

        Assert.Equal(string.Empty, await TataraExecutorTest.Tool("llvm-lib", native, $"/out:{name}.lib", "needed.obj", "other.obj"));
        var project = this.Hello("unused");
        var source = $"group Codec\n    #LibraryImport(\"{name}\", \"codec_add\")\n    public unsafe func add(a: i32, b: i32) -> i32\npublic func main()\n" +
            "    var sum: i32 = 0\n    unsafe => sum = Codec.add(40, -2)\n    require sum == 38 else => $abort(\"add\")\n    var values: Array<i32> = []\n" +
            "    for i in 0..100\n        values.append(i)\n    require values.length == 100 else => $abort(\"append\")\n    Console.writeLine(\"ffi ok\")\n";
        File.WriteAllText(Path.Combine(this.directory, "main.kimi"), source);
        project.ProjectFile.NativeLibraries[TargetProfile.Windows.Target] = new() { [name] = new() { Kind = "static", Input = $"native/{name}.lib" } };
        return project;
    }

    // LinkAmbiguity's rows (design §1): Hello.kimiproj at the level, whose main.kimi calls each import (int name(void)) and writes "ok",
    // and its program libraries, static unless named, each native/<library>.lib built by Archive. name: the row's variant, the name of
    // the library that holds codec's or alpha's part (before or after kernel32 and kimi_backend in name order). Rows 29 and 30 are
    // P9's programs (an import called only under a condition O2 folds; codec_add(int, int)). Rows k<n> are Kind's (kind-design.md §1
    // row n; k8o2 row 8's O2 twin; k5w, k5wi, k2p, k13p, k2m and k7m as their theories state; k2m and k7m write the short import
    // header: Sig1 0, Sig2 0xFFFF, version 0, x64, no time stamp, 20 bytes of names, hint 0, a code import by name): a library named
    // "codec:import" is import, "codec=mixed" reads native/mixed.lib; Long is a long-format import member from a clang object
    // (__imp_codec_add in .idata$5 and its thunk; P-K3). Rows p<n> are NamedProvider's (provider-design.md §1 row n, as its theories
    // state; p14 is k8's program at the level, its codec lacking codec_add), each codec of them but p24a's lacking codec_add (Other).
    // Rows 31-35 with their letters, k3x, k11lc and k11lcb are LinkRoots' (LinkRoots design §1, Revision 1a, as their theories state):
    // a member named *.bc is LLVM bitcode (Archive compiles it with -flto), and Lone makes one member's object a library's whole input
    // (k6's form); LC is the probes' 0x140-byte load configuration, EntryLine a member's /entry:ent2 directive.
    private async Task<Project> Dependency(string row, string name, string level)
    {
        const string Add = "int codec_add(void) { return 3; }\n";
        const string Other = "int codec_other(void) { return 1; }\n";
        const string X = "int alpha_x(void) { return 1; }\n";
        const string Z = "int zeta_x(void) { return 1; }\n";
        const string LC = "const unsigned long long _load_config_used[40] = { 320 };\n";
        const string Carrier = "int carrier_f(void) { return 3; }\n";
        const string Ent2 = "int ent2(void) { return 11; }\n";
        const string EntryLine = "#pragma comment(linker, \"/entry:ent2\")\n";
        const string Floor = "volatile double alpha_g = 2.5;\nint alpha_fl(void) { return (int)__builtin_floor(alpha_g); }\n";
        const string Helper = "int helper(void);\nint codec_add(void) { return helper(); }\n";
        const string Directive = "#pragma comment(linker, \"/alternatename:a=b\")\nint a(void);\nint codec_add(void) { return a(); }\n";
        const string Long = "int (*__imp_codec_add)(void) __attribute__((section(\".idata$5\"))) = 0;\nint codec_add(void) { return __imp_codec_add(); }\n";
        (string, string) m1 = ("m1", Add), alpha = ("m1", X);
        (string Library, string Symbol)[] imports = [("codec", "codec_add")];
        string[] libraries = ["codec"];
        string? source = null;
        switch (row)
        {
            case "3":
                await Archive(name, ("m1", "unsigned GetLastError(void);\nint codec_add(void) { return (int)GetLastError(); }\n"), ("m2", "unsigned GetLastError(void) { return 0; }\n"));
                (imports, libraries) = ([(name, "codec_add")], [name]);
                break;
            case "4":
                await Archive(name, ("m1", "void *__imp_WriteFile = 0;\n" + Add));
                (imports, libraries) = ([(name, "codec_add")], [name]);
                break;
            case "6":
                await Archive(name, ("m1", "int codec_add(void) { volatile char buffer[8192]; buffer[0] = 3; return buffer[0]; }\n"), ("m2", "void __chkstk(void) { }\n"));
                (imports, libraries) = ([(name, "codec_add")], [name]);
                break;
            case "7" or "22":
                await Archive(name, alpha, ("m2", Add));
                await Archive("codec", m1);
                (imports, libraries) = ([(name, "alpha_x"), ("codec", "codec_add")], [name, "codec"]);
                if (row == "22")
                {
                    await this.Image("zlib", "int zlib_f(void) { return 4; }\n" + Add);
                    (imports, libraries) = ([.. imports, ("zlib", "zlib_f")], [.. libraries, "zlib"]);
                }

                break;
            case "8" or "15":
                await Archive("alpha", ("m1", (row == "15" ? "__attribute__((weak)) " : string.Empty) + "int f(void) { return 1; }\n" + X));
                await Archive("codec", ("m1", "int f(void) { return 2; }\n" + Add), ("m2", "int f(void);\nint codec_use(void) { return f(); }\n"));
                (imports, libraries) = ([("alpha", "alpha_x"), ("codec", "codec_add"), ("codec", "codec_use")], ["alpha", "codec"]);
                break;
            case "17":
                await Archive("codec", ("m1", Add + "int codec_sub(void) { return 1; }\n"));
                (imports, libraries) = ([("codec", "codec_add"), ("codec2", "codec_sub")], ["codec", "codec2=codec"]);
                break;
            case "23":
                await this.ImportLibrary("codec", "VirtualProtect");
                (imports, libraries) = ([("codec", "VirtualProtect")], ["codec:import"]);
                break;
            case "24":
                await Archive(name, ("m1", "int VirtualProtect(void) { return 1; }\n"));
                (imports, libraries) = ([(name, "VirtualProtect")], [name]);
                break;
            case "25a" or "25b":
                (string, string) m2 = ("m2", "int helper(void) { return 2; }\n"), m3 = ("m3", "int VirtualProtect(void);\nint helper(void) { return VirtualProtect(); }\n");
                await Archive("codec", [("m1", Helper), .. row == "25a" ? [m3, m2] : new[] { m2, m3 }]);
                await Archive("zlib", ("z1", "int zlib_crc(void) { return 4; }\n"), ("z2", "int VirtualProtect(void) { return 1; }\n"));
                (imports, libraries) = ([("codec", "codec_add"), ("zlib", "zlib_crc")], ["codec", "zlib"]);
                break;
            case "26":
                await Archive("alpha", ("m1", "__declspec(selectany) int g = 1;\nint alpha_x(void) { return g; }\n"));
                await Archive("codec", ("m1", "extern int g;\nint codec_add(void) { return g; }\n"), ("m2", "int g = 2;\n"));
                (imports, libraries) = ([("alpha", "alpha_x"), ("codec", "codec_add")], ["alpha", "codec"]);
                break;
            case "27":
                await Archive("alpha", alpha, ("m2", "int f(void) { return 1; }\n"));
                await Archive("codec", ("m1", "#pragma comment(linker, \"/include:f\")\n" + Add), ("m2", "int f(void) { return 2; }\n"));
                (imports, libraries) = ([("alpha", "alpha_x"), ("codec", "codec_add")], ["alpha", "codec"]);
                break;
            case "28" or "28t":
                await Archive("alpha", [alpha, ("m2", "int b(void) { return 1; }\n"), .. row == "28t" ? [("m3", "int a(void) { return 5; }\n")] : Array.Empty<(string, string)>()]);
                await Archive("codec", ("m1", Directive), ("m2", "int b(void) { return 2; }\n"));
                (imports, libraries) = ([("alpha", "alpha_x"), ("codec", "codec_add")], ["alpha", "codec"]);
                break;
            case "29":
                await Archive("alpha", alpha, ("m2", "int codec_add(int a, int b) { return 0; }\n"));
                await Archive("codec", ("m1", "int codec_add(int a, int b) { return a + b; }\n"));
                libraries = ["alpha", "codec"];
                source = "group Native\n    #LibraryImport(\"alpha\", \"alpha_x\")\n    public unsafe func alphaX() -> i32\n    #LibraryImport(\"codec\", \"codec_add\")\n" +
                    "    public unsafe func add(a: i32, b: i32) -> i32\nfunc pick(flag: bool) -> i32\n    var s: i32 = 0\n    if flag\n        unsafe => s = Native.add(40, -2)\n" +
                    "    return s\npublic func main()\n    var x: i32 = 0\n    unsafe => x = Native.alphaX()\n    let y = pick(false)\n" +
                    "    require x + y == 1 else => $abort(\"alpha\")\n    Console.writeLine(\"ok\")\n";
                break;
            case "30":
                await Archive("codec", ("m1", "void __chkstk(void) { }\nint codec_add(int a, int b) { return a + b; }\n"));
                source = "group Native\n    #LibraryImport(\"codec\", \"codec_add\")\n    public unsafe func add(a: i32, b: i32) -> i32\npublic func main()\n" +
                    "    var sum: i32 = 0\n    unsafe => sum = Native.add(40, -2)\n    require sum == 38 else => $abort(\"add\")\n    Console.writeLine(\"ok\")\n";
                break;
            case "10":
                await Archive("codec", ("m1", Add + "int VirtualProtect(void) { return 1; }\n"));
                break;
            case "k2":
                await this.ImportLibrary("codec", "codec_add");
                break;
            case "k2p":
                await this.ImportLibrary("codec", "codec_add", "codec_sub");
                await this.ImportLibrary("zlib", "codec_add", "codec_sub");
                (imports, libraries) = ([("codec", "codec_add"), ("zlib", "codec_sub")], ["codec", "zlib"]);
                break;
            case "k3" or "k4" or "k3x":
                await (row == "k3x"
                    ? Archive("codec", ("m1", "#pragma comment(linker, \"/export:GetTickCount\")\n" + Add))
                    : Archive("codec", m1, ("m2", "unsigned GetTickCount(void);\nint codec_tick(void) { return (int)GetTickCount(); }\n")));
                await this.ImportLibrary("kernel32", "GetTickCount");
                Assert.Equal(string.Empty, await TataraExecutorTest.Tool("llvm-lib", Path.Combine(this.directory, "native"), "/out:mixed.lib", "codec.lib", "kernel32.lib"));
                (imports, libraries) = ([("codec", "codec_add"), .. row == "k3" ? [("codec", "codec_tick")] : Array.Empty<(string, string)>()], ["codec=mixed"]);
                break;
            case "k5":
                await Archive("codec", ("m1", Long));
                break;
            case "k2m" or "k7m":
                byte[] importFile = [0, 0, 0xFF, 0xFF, 0, 0, 0x64, 0x86, 0, 0, 0, 0, 20, 0, 0, 0, 0, 0, 4, 0, .. "codec_add\0codec.dll\0"u8];
                File.WriteAllBytes(Path.Combine(Directory.CreateDirectory(Path.Combine(this.directory, "native")).FullName, "codec.lib"), importFile);
                libraries = [row == "k2m" ? "codec" : "codec:import"];
                break;
            case "k6" or "k9" or "k5w" or "k5wi":
                await Lone("codec", "m1", row.StartsWith("k5", StringComparison.Ordinal) ? Long : Add);
                libraries = [row is "k6" or "k5w" ? "codec" : "codec:import"];
                break;
            case "k7" or "k14":
                await (row == "k7" ? this.ImportLibrary("codec", "codec_add") : this.Image("codec", Add));
                libraries = ["codec:import"];
                break;
            case "k8" or "k8o2" or "p14":
                await Archive("codec", ("m1", row == "p14" ? Other : "int codec_add(int a, int b) { return a + b; }\n"));
                libraries = [row == "p14" ? "codec" : "codec:import"];
                source = "group Native\n    #LibraryImport(\"codec\", \"codec_add\")\n    public unsafe func add(a: i32, b: i32) -> i32\nfunc pick(flag: bool) -> i32\n    var s: i32 = 0\n" +
                    $"    if flag\n        unsafe => s = Native.add(40, -2)\n    return s\npublic func main()\n    let y = pick({(row == "k8" ? "true" : "false")})\n    require y >= 0 else => $abort(\"pick\")\n    Console.writeLine(\"ok\")\n";
                break;
            case "k10" or "k11":
                await this.ImportLibrary("codec", "codec_add");
                await Archive("stub", ("m1", "int codec_version(void) { return 1; }\n"));
                Assert.Equal(string.Empty, await TataraExecutorTest.Tool("llvm-lib", Path.Combine(this.directory, "native"), "/out:mixed.lib", "codec.lib", "stub.lib"));
                await Archive("zlib", ("z1", "int codec_version(void);\nint zlib_f(void) { return codec_version(); }\n"));
                imports = row == "k11" ? [("codec", "codec_add"), ("zlib", "zlib_f")] : imports;
                libraries = row == "k11" ? ["codec=mixed:import", "zlib"] : ["codec=mixed:import"];
                break;
            case "k11lc" or "k11lcb":
                await this.ImportLibrary("lcfg", "lcfg_x");
                await Archive("stub", (row == "k11lcb" ? "m1.bc" : "m1", LC));
                Assert.Equal(string.Empty, await TataraExecutorTest.Tool("llvm-lib", Path.Combine(this.directory, "native"), "/out:mixed.lib", "lcfg.lib", "stub.lib"));
                (imports, libraries) = ([("lcfg", "lcfg_x")], ["lcfg=mixed:import"]);
                break;
            case "k12":
                await Archive(
                    "codec",
                    ("m1", Long + "extern char __head_codec[];\nvoid *__codec_head __attribute__((section(\".idata$7\"))) = __head_codec;\n"),
                    ("head", "extern char __codec_iname[];\nchar __head_codec[20] __attribute__((section(\".idata$2\"))) = {0};\nvoid *__codec_name __attribute__((section(\".idata$2\"))) = __codec_iname;\n"),
                    ("tail", "char __codec_iname[] __attribute__((section(\".idata$7\"))) = \"codec.dll\";\nvoid *__codec_end __attribute__((section(\".idata$5\"))) = 0;\n"));
                libraries = ["codec:import"];
                break;
            case "k13" or "k13p":
                await this.ImportLibrary("codec", "codec_other");
                await Archive("zlib", ("z1", "int zlib_f(void) { return 4; }\n" + Add));
                (imports, libraries) = ([("codec", "codec_add"), ("zlib", "zlib_f")], ["codec:import", "zlib"]);
                if (row == "k13p")
                {
                    await Archive("alpha", alpha, ("m2", Add));
                    (imports, libraries) = ([.. imports, ("alpha", "alpha_x")], [.. libraries, "alpha"]);
                }

                break;
            case "p2" or "p3" or "p2i":
                await (row == "p2i" ? this.ImportLibrary("codec", "codec_other") : Archive("codec", ("m1", Other)));
                await (row == "p3" ? Archive("zlib", ("z1", "int zlib_f(void) { return 4; }\n" + Add)) : this.ImportLibrary("zlib", "codec_add", "zlib_f"));
                (imports, libraries) = ([("codec", "codec_add"), ("zlib", "zlib_f")], [row == "p2i" ? "codec:import" : "codec", row == "p3" ? "zlib" : "zlib:import"]);
                break;
            case "p6":
                await Archive("codec", ("m1", Other));
                break;
            case "p24a":
                await Archive("codec", ("m1", Other), ("m2", Add), ("m3", Add));
                break;
            case "p24b":
                // codec.lib indexes m1; m2, defining codec_add, is appended after it as an ar member no index entry names (P-P4): its
                // 60-byte header (name, date, owner, group, mode and size, then `\n), its bytes, and a \n to the next even offset.
                await Archive("codec", ("m1", Other));
                await Archive("loose", ("m2", Add));
                var appended = File.ReadAllBytes(Path.Combine(this.directory, "native", "loose.members", "m2.obj"));
                using (var archive = new FileStream(Path.Combine(this.directory, "native", "codec.lib"), FileMode.Append))
                {
                    archive.Write(Encoding.ASCII.GetBytes($"{"m2.obj/",-16}{0,-12}{0,-6}{0,-6}{644,-8}{appended.Length,-10}`\n"));
                    archive.Write(appended);
                    if (appended.Length % 2 == 1)
                    {
                        archive.WriteByte((byte)'\n');
                    }
                }

                break;
            case "11":
                await Archive("codec", ("m1", Helper), ("m2", "int helper(void) { return 2; }\n"), ("m3", "int helper(void) { return 3; }\n"));
                break;
            case "16":
                await Archive("alpha", alpha, ("m2", "int f(void) { return 1; }\n"));
                await Archive("codec", ("m1", "extern int f(void) __attribute__((weak));\nint codec_add(void) { return f ? f() : 3; }\n"), ("m2", "int f(void) { return 2; }\n"));
                (imports, libraries) = ([("alpha", "alpha_x"), ("codec", "codec_add")], ["alpha", "codec"]);
                break;
            case "20" or "21":
                await Archive("alpha", ("m1", "__declspec(selectany) int g = 1;\nint alpha_x(void) { return g; }\n"));
                await Archive("codec", [("m1", "__declspec(selectany) int g = 1;\nint codec_add(void) { return g + 2; }\n"), .. row == "21" ? [("m2", "extern int g;\nint codec_use(void) { return g; }\n")] : Array.Empty<(string, string)>()]);
                (imports, libraries) = ([("alpha", "alpha_x"), ("codec", "codec_add"), .. row == "21" ? [("codec", "codec_use")] : Array.Empty<(string, string)>()], ["alpha", "codec"]);
                break;
            case "31" or "31b" or "31t":
                await Archive("carrier", ("m1", row == "31" ? "#pragma comment(linker, \"/export:foo\")\n" + Carrier : "__declspec(dllexport) int foo(void) { return 7; }\nint carrier_f(void) { return foo(); }\n"));
                (imports, libraries) = ([("carrier", "carrier_f")], ["carrier"]);
                if (row != "31t")
                {
                    await Archive("alpha", alpha, ("m2", "int foo(void) { return 1; }\n"));
                    (imports, libraries) = ([.. imports, ("alpha", "alpha_x")], [.. libraries, "alpha"]);
                }

                if (row == "31")
                {
                    await Archive("codec", m1, ("m2", "int foo(void) { return 2; }\n"));
                    (imports, libraries) = ([.. imports, ("codec", "codec_add")], [.. libraries, "codec"]);
                }

                break;
            case "32":
                await Archive("alpha", alpha, ("m2", LC));
                await Archive("codec", m1, ("m2", LC));
                (imports, libraries) = ([("alpha", "alpha_x"), ("codec", "codec_add")], ["alpha", "codec"]);
                break;
            case "33" or "33t":
                await Archive("lcfg", ("m1", "int lcfg_x(void) { return 1; }\n"), ("m2", LC + (row == "33" ? "int lc_helper(void);\nint (*const lc_use)(void) = lc_helper;\n" : string.Empty)));
                (imports, libraries) = ([("lcfg", "lcfg_x")], ["lcfg"]);
                if (row == "33")
                {
                    await Archive("alpha", alpha, ("m2", "int lc_helper(void) { return 1; }\n"));
                    await Archive("codec", m1, ("m2", "int lc_helper(void) { return 1; }\n"));
                    (imports, libraries) = ([.. imports, ("alpha", "alpha_x"), ("codec", "codec_add")], [.. libraries, "alpha", "codec"]);
                }

                break;
            case "34" or "34x":
                (string, string)[] zz = row == "34x" ? [("m3", "int zz(void) { return 5; }\n")] : [];
                await Archive("carrier", ("m1", EntryLine + (row == "34x" ? "int zz(void);\nint carrier_f(void) { return zz(); }\n" : Carrier)));
                await Archive("alpha", [alpha, ("m2", Ent2), .. zz]);
                await Archive("codec", [m1, ("m2", Ent2), .. zz]);
                (imports, libraries) = ([("carrier", "carrier_f"), ("alpha", "alpha_x"), ("codec", "codec_add")], ["alpha", "carrier", "codec"]);
                break;
            case "34b":
                await Archive("lcfge", ("m1", "int lcfge_x(void) { return 1; }\n"), ("m2", EntryLine + LC));
                await Archive("alpha", alpha, ("m2", Ent2));
                (imports, libraries) = ([("lcfge", "lcfge_x"), ("alpha", "alpha_x")], ["alpha", "lcfge"]);
                break;
            case "34t":
                await Archive("carrier", ("m1", Carrier), ("m2", EntryLine + "int carrier_u(void) { return 0; }\n"));
                (imports, libraries) = ([("carrier", "carrier_f")], ["carrier"]);
                break;
            case "34p" or "34w":
                await (row == "34w" ? Lone("carrier", "m1", EntryLine + Carrier) : Archive("carrier", ("m1", EntryLine + Carrier)));
                await Archive("zeta", ("m1", Z + "int carrier_g(void) { return 2; }\n"));
                (imports, libraries) = ([("carrier", "carrier_f"), ("carrier", "carrier_g"), ("zeta", "zeta_x")], ["carrier", "zeta"]);
                break;
            case "34we":
                await Lone("carrier", "m1", EntryLine + Carrier);
                await Archive("alpha", alpha, ("m2", Ent2));
                (imports, libraries) = ([("carrier", "carrier_f"), ("alpha", "alpha_x")], ["alpha", "carrier"]);
                break;
            case "35" or "35w":
                await (row == "35w" ? Lone("alpha", "m1.bc", Floor) : Archive("alpha", ("m1.bc", Floor)));
                await Archive("codec", m1, ("m2", "double floor(double d) { return d + 100; }\n"));
                await Archive("zeta", ("m1", Z), ("m2", "double floor(double d) { return d + 200; }\n"));
                (imports, libraries) = ([("alpha", "alpha_fl"), ("codec", "codec_add"), ("zeta", "zeta_x")], ["alpha", "codec", "zeta"]);
                break;
            case "35t":
                await Archive("alpha", alpha, ("m2.bc", "int alpha_u(void) { return 0; }\n"));
                (imports, libraries) = ([("alpha", "alpha_x")], ["alpha"]);
                break;
            case "35x":
                await Archive("alpha", ("m1.bc", "int zz(void);\nint alpha_z(void) { return zz(); }\n"));
                await Archive("codec", m1, ("m2", "int zz(void) { return 5; }\n"));
                await Archive("zeta", ("m1", Z), ("m2", "int zz(void) { return 5; }\n"));
                (imports, libraries) = ([("alpha", "alpha_z"), ("codec", "codec_add"), ("zeta", "zeta_x")], ["alpha", "codec", "zeta"]);
                break;
            case "35p" or "35wp":
                const string AlphaB = "int alpha_b(void) { return 1; }\n";
                await (row == "35wp" ? Lone("alpha", "m1.bc", AlphaB) : Archive("alpha", ("m1.bc", AlphaB)));
                await Archive("zeta", ("m1", Z + "int alpha_g(void) { return 2; }\n"));
                (imports, libraries) = ([("alpha", "alpha_b"), ("alpha", "alpha_g"), ("zeta", "zeta_x")], ["alpha", "zeta"]);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(row));
        }

        source ??= "group Native\n" + string.Concat(imports.Select(static (x, i) => $"    #LibraryImport(\"{x.Library}\", \"{x.Symbol}\")\n    public unsafe func i{i}() -> i32\n")) +
            "public func main()\n    var v: i32 = 0\n" + string.Concat(imports.Select(static (_, i) => $"    unsafe => v = Native.i{i}()\n")) + "    Console.writeLine(\"ok\")\n";
        File.WriteAllText(Path.Combine(this.directory, "main.kimi"), source);
        var supplies = string.Concat(libraries.Select(static x => x.Split('=', ':') switch
        {
            [var library, var input] when x.Contains('=') => $"    {library}={{ Kind=\"static\" Input=\"native/{input}.lib\" }}\n",
            [var library, var kind] => $"    {library}={{ Kind=\"{kind}\" Input=\"native/{library}.lib\" }}\n",
            [var library, var input, var kind] => $"    {library}={{ Kind=\"{kind}\" Input=\"native/{input}.lib\" }}\n",
            [var library] => $"    {library}={{ Kind=\"static\" Input=\"native/{library}.lib\" }}\n",
            _ => throw new ArgumentOutOfRangeException(nameof(row)),
        }));
        var file = Path.Combine(this.directory, "Hello.kimiproj");
        File.WriteAllText(file, $"Targets={{\"{TargetProfile.Windows.Target}\"}} OutputPath=\"out/Hello.ll\" Optimization=\"{level}\"\nNativeLibraries=\n  \"{TargetProfile.Windows.Target}\"=\n{supplies}");
        Assert.True(Project.TryCreate(new Kimigayo(this.console), null, file, out var project));
        return project;

        // native/<library>.lib: each member's C source compiled by the pinned clang (rows 27 and 28 keep their linker directives:
        // -fno-autolink drops them; probes.md P8; a member named *.bc with -flto, LLVM bitcode) and archived by llvm-lib in this
        // command-line order (llvm-lib writes its members in reverse, so its index names the command line's last definer of a symbol; P5).
        async Task Archive(string library, params (string Member, string Source)[] members)
        {
            var folder = Directory.CreateDirectory(Path.Combine(this.directory, "native", library + ".members")).FullName;
            foreach (var (member, text) in members)
            {
                File.WriteAllText(Path.Combine(folder, member + ".c"), text);
                string[] autolink = text.Contains("#pragma", StringComparison.Ordinal) ? [] : ["-fno-autolink"];
                string[] lto = member.EndsWith(".bc", StringComparison.Ordinal) ? ["-flto"] : [];
                string[] compile = ["--target=" + TargetProfile.Windows.Target, "-O2", "-fno-builtin", .. autolink, "-fno-stack-protector", "-fms-extensions", .. lto, "-c", member + ".c", "-o", member + ".obj"];
                Assert.Equal(string.Empty, await TataraExecutorTest.Tool("clang", folder, compile));
            }

            Assert.Equal(string.Empty, await TataraExecutorTest.Tool("llvm-lib", folder, ["/out:" + Path.Combine("..", library + ".lib"), .. members.Select(static x => x.Member + ".obj")]));
        }

        // native/<library>.lib, one member's object itself (compiled as Archive compiles it): an input of no archive, which the link loads
        // whole (k6's form).
        async Task Lone(string library, string member, string source)
        {
            await Archive("object", (member, source));
            File.Copy(Path.Combine(this.directory, "native", "object.members", member + ".obj"), Path.Combine(this.directory, "native", library + ".lib"));
        }
    }

    // Design §5 row 21's Linux programs as pairs at the level: Hello (libc-free) and Ffi, whose static codec.a's one member defines
    // codec_add(int, short) and codec_pid, which calls musl's getpid (the C path; Kind's row 21).
    private async Task<ArtifactPaths> LinuxProgram(string program, string level)
    {
        if (program == "Hello")
        {
            return LinuxPair.Publish(this.directory, "::Kimi.Console.writeLine(\"Hello, world!\")", level);
        }

        await LinuxPair.Archive(this.directory, "codec", "rcs", ("m1", "int codec_add(int a, short b) { return a + b; }\nint getpid(void);\nint codec_pid(void) { return getpid(); }\n"));
        const string Ffi = "group Codec\n    #LibraryImport(\"codec\", \"codec_add\")\n    public func add(a: i32, b: i16) -> i32\npublic func main()\n" +
            "    require Codec.add(40, -2) == 38 else => $abort(\"add\")\n    Console.writeLine(\"ffi ok\")\n";
        return LinuxPair.Publish(this.directory, Ffi, level, ("codec", "static", "codec.a"));
    }

    // LinkAmbiguity's and Kind's L rows (Dependency's Linux counterpart; labels copied from the designs) as pairs at O2: name is the row's
    // variant (the library holding codec's or alpha's part); every library static, each native/<library>.a by LinuxPair.Archive (rcs; r48:
    // rcS), a shared object (LinuxPair.SharedObject) or an archive of one (c2: LinuxPair.ImageArchive). NamedProvider's rows p10 and p12
    // (provider-design.md §1 rows 10 and 12): c's write with no program library, and codec's strlen, which only c's libc.a defines.
    // LinkRoots' L35 and L32t (LinkRoots design §1): a bitcode member (m1.bc) and Dependency's load configuration (LC) in two libraries.
    private async Task<ArtifactPaths> LinuxDependency(string row, string name)
    {
        const string Add = "int codec_add(void) { return 3; }\n";
        const string X = "int alpha_x(void) { return 1; }\n";
        const string LC = "const unsigned long long _load_config_used[40] = { 320 };\n";
        (string Library, string Symbol)[] imports = [("codec", "codec_add")];
        string[] libraries = ["codec"];
        switch (row)
        {
            case "1":
                await LinuxPair.Archive(this.directory, name, "rcs", ("m1", "long write(int fd, const void *buffer, unsigned long n) { return 0; }\n"));
                (imports, libraries) = ([(name, "write")], [name]);
                break;
            case "22":
                await LinuxPair.Archive(this.directory, name, "rcs", ("m1", X), ("m2", Add));
                await LinuxPair.Archive(this.directory, "codec", "rcs", ("m1", Add));
                await LinuxPair.SharedObject(this.directory, "zlib", "int zlib_f(void) { return 4; }\n" + Add);
                (imports, libraries) = ([(name, "alpha_x"), ("codec", "codec_add"), ("zlib", "zlib_f")], [name, "codec", "zlib"]);
                break;
            case "k22":
                await LinuxPair.SharedObject(this.directory, "codec", Add);
                break;
            case "c2":
                await LinuxPair.ImageArchive(this.directory, "codec", Add);
                break;
            case "r48":
                await LinuxPair.Archive(this.directory, "codec", "rcS", ("m1", Add));
                break;
            case "18":
                await LinuxPair.Archive(this.directory, "codec", "rcs", ("m1", Add), ("m2", "void _start(void) { }\n"));
                break;
            case "11":
                await LinuxPair.Archive(this.directory, "codec", "rcs", ("m1", "int helper(void);\nint codec_add(void) { return helper(); }\n"), ("m2", "int helper(void) { return 2; }\n"), ("m3", "int helper(void) { return 3; }\n"));
                break;
            case "20" or "21":
                await LinuxPair.Archive(this.directory, "alpha", "rcs", ("m1", "__declspec(selectany) int g = 1;\n" + X));
                await LinuxPair.Archive(this.directory, "codec", "rcs", [("m1", "__declspec(selectany) int g = 1;\nint codec_add(void) { return g + 2; }\n"), .. row == "21" ? [("m2", "extern int g;\nint codec_use(void) { return g; }\n")] : Array.Empty<(string, string)>()]);
                (imports, libraries) = ([("alpha", "alpha_x"), ("codec", "codec_add"), .. row == "21" ? [("codec", "codec_use")] : Array.Empty<(string, string)>()], ["alpha", "codec"]);
                break;
            case "p10":
                (imports, libraries) = ([("c", "write")], []);
                break;
            case "p12":
                await LinuxPair.Archive(this.directory, "codec", "rcs", ("m1", Add));
                imports = [("codec", "codec_add"), ("codec", "strlen")];
                break;
            case "L35":
                await LinuxPair.Archive(this.directory, "codec", "rcs", ("m1.bc", Add));
                break;
            case "L32t":
                await LinuxPair.Archive(this.directory, "alpha", "rcs", ("m1", X), ("m2", LC));
                await LinuxPair.Archive(this.directory, "codec", "rcs", ("m1", Add), ("m2", LC));
                (imports, libraries) = ([("alpha", "alpha_x"), ("codec", "codec_add")], ["alpha", "codec"]);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(row));
        }

        return LinuxPair.Publish(this.directory, LinuxSource(imports), "O2", [.. libraries.Select(static x => (x, "static", x + ".a"))]);
    }

    // native/<library>.lib, a linked image (design §1 row 22; Kind's row 14): a DLL linked by lld-link from a clang object, its COFF
    // symbol table kept (/debug:symtab), so a reader of whole files would see its definitions.
    private async Task Image(string library, string source)
    {
        var native = Directory.CreateDirectory(Path.Combine(this.directory, "native")).FullName;
        File.WriteAllText(Path.Combine(native, library + ".c"), source);
        Assert.Equal(string.Empty, await TataraExecutorTest.Tool("clang", native, "--target=" + TargetProfile.Windows.Target, "-O2", "-fno-autolink", "-c", library + ".c", "-o", library + ".obj"));
        Assert.Equal(string.Empty, await TataraExecutorTest.Tool("lld-link", native, "/dll", "/noentry", "/nodefaultlib", "/debug:symtab", "/noimplib", "/out:" + library + ".dll", library + ".obj"));
        File.Copy(Path.Combine(native, library + ".dll"), Path.Combine(native, library + ".lib"));
    }

    // native/<library>.lib, an import library exporting the symbols (llvm-lib /def; design §1 row 23).
    private async Task ImportLibrary(string library, params string[] symbols)
    {
        var native = Directory.CreateDirectory(Path.Combine(this.directory, "native")).FullName;
        File.WriteAllText(Path.Combine(native, library + ".def"), $"LIBRARY {library}\nEXPORTS\n" + string.Concat(symbols.Select(static x => $"    {x}\n")));
        Assert.Equal(string.Empty, await TataraExecutorTest.Tool("llvm-lib", native, "/machine:x64", "/def:" + library + ".def", "/out:" + library + ".lib"));
    }

    // Hello.kimiproj at O2 into out/Hello.ll, its main.kimi writing the text, on the test's console.
    private Project Hello(string text) => this.Main($"::Kimi.Console.writeLine(\"{text}\")");

    // Hello.kimiproj at O2 into out/Hello.ll with this main.kimi, on the test's console.
    private Project Main(string source)
    {
        var file = Path.Combine(this.directory, "Hello.kimiproj");
        File.WriteAllText(file, $"Targets={{\"{TargetProfile.Windows.Target}\"}} OutputPath=\"out/Hello.ll\"");
        File.WriteAllText(Path.Combine(this.directory, "main.kimi"), source);
        Assert.True(Project.TryCreate(new Kimigayo(this.console), null, file, out var project));
        return project;
    }

    // A selected input in the test's directory, as resolution gives it: a .kimi file's implicit project, or a project file's project.
    private Project Load(string input)
    {
        var path = Path.Combine(this.directory, input);
        if (input.EndsWith(".kimi", StringComparison.OrdinalIgnoreCase))
        {
            return Project.CreateFromSource(new Kimigayo(this.console), path, new KimiOptions());
        }

        Assert.True(Project.TryCreate(new Kimigayo(this.console), null, path, out var project));
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
