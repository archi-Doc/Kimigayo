// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kimi;
using Kimi.Build;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Xunit;

namespace XunitTest;

// Tatara, the native build's LLVM and LLD (Tatara designs A2 §4.5, A4 §2.6 and B6b1 §2), on the compiler's Tatara (the tatara.dll the
// build copies beside the test output's Kimi.dll) and the checkout's development toolchain's tools. A row that loads tatara.dll or runs a tool
// skips off Windows; on Windows a missing tatara.dll fails it. KIMI_TATARA_CORPUS names a corpus directory whose fixtures/*.ll (bare
// IR) replace the fixed rows of the tool control comparison, at O0 and O2; each row's decision and outcome is appended to
// <corpus>/results.tsv (Run; tests/scripts/run-tatara-corpus.ps1 prepares the corpus, runs this class and lists the rows Run failed).
// A fatal row leaves Tatara refusing every later call for the rest of its process, so each runs in a child test process of its own
// (Fatal).
public sealed class TataraExecutorTest : IDisposable
{
    private const string Windows = "Tatara and the LLVM tools run on Windows.";

    // The probe's metadata naming a dependent library, so its ELF object has a .deplibs section.
    private const string DependentLibrary = "!llvm.dependent-libraries = !{!0}\n!0 = !{!\"c\"}";

    private static readonly string Triple = TargetProfile.Windows.Target;

    private static readonly string[] Levels = ["O0", "O2"];

    private static readonly string[] ToolNames = ["opt", "llc", "lld-link", "llvm-nm", "llvm-readobj"];

    // What NativeBuild gives Tatara's calls, without a report or a cancellation.
    private static readonly TataraContext Context = new(
        (_, _) => { }, CancellationToken.None, Triple, TargetProfile.Windows.Cpu, TargetProfile.Windows.Features, TargetProfile.Windows.RelocationModel, TargetProfile.Windows.CodeModel);

    // The Linux row's target (Tatara B7b2a): a static x86-64 ELF.
    private static readonly TataraContext LinuxContext = new(
        (_, _) => { }, CancellationToken.None, TargetProfile.Linux.Target, TargetProfile.Linux.Cpu, TargetProfile.Linux.Features, TargetProfile.Linux.RelocationModel, TargetProfile.Linux.CodeModel);

    // The tool control's two formats. The archive's ld.lld.exe and llvm-readelf.exe are the toolchain's lld-link.exe and llvm-readobj.exe
    // (equal SHA-256); -flavor gnu and --elf-output-style=GNU select what those names select.
    private static readonly ToolTarget Coff = new(Context, ["--coff-directives"], CoffLibrary, "lld-link", [], LinkArguments);

    private static readonly ToolTarget Elf = new(LinuxContext, ["--elf-output-style=GNU", "--sections"], ElfLibrary, "ld.lld", ["-flavor", "gnu"], ElfLinkArguments);

    // The archive rows' C members (LinkAmbiguity design §5, probes P5): m1 calls helper, which m2 defines, and m3 too, calling other.
    private static readonly (string Name, string Source) M1 = ("m1.c", "int helper(void);\nint codec_add(int a, int b) { return a + b + helper(); }\n");

    private static readonly (string Name, string Source) M2 = ("m2.c", "int helper(void) { return 2; }\n");

    private static readonly (string Name, string Source) M3 = ("m3.c", "int other(void);\nint helper(void) { return other(); }\n");

    // The archive rows' compile options but the directive row's: -fno-autolink drops #pragma comment(linker) (LinkAmbiguity's P8).
    private static readonly string[] NoAutolink = ["-fno-autolink"];

    private readonly string directory = Path.Combine(Path.GetTempPath(), "kimi-tatara-" + Guid.NewGuid().ToString("N"));

    public TataraExecutorTest() => Directory.CreateDirectory(this.directory);

    private delegate IntPtr SetFilter(IntPtr filter);

    private delegate int ModuleFromAddress(uint flags, IntPtr address, out IntPtr module);

    private static string Root => Path.Combine(Repository.Root, "toolchain");

    private static string? Corpus => Environment.GetEnvironmentVariable("KIMI_TATARA_CORPUS");

    private static string Hello => Path.Combine(Repository.Root, "docs", "examples", "Hello", "Hello.kimi");

    // The libraries the compiler carries, committed beside the catalog (Tatara B6b6).
    private static string Kernel32 => Path.Combine(Repository.Root, "src", "backend", "windows-x64", "kernel32.lib");

    private static string Backend => Path.Combine(Repository.Root, "src", "backend", "windows-x64", "kimi_backend_windows_x64_v1.lib");

    // Both, in ToolchainIdentity.Supplies' order: a link manifest's libraries when the program names none of its own.
    private static string[] CompilerLibraries => [Kernel32, Backend];

    public void Dispose() => Directory.Delete(this.directory, true);

    // The start check's comparison (no DLL): the expected ABI is the SHA-256 of src/Tatara/tatara.h, and a DLL of another ABI or LLVM
    // version is refused with both values and the remedy, which names no root (Tatara B6b1).
    [Fact]
    public void StartCheckComparesTheAbiAndTheLlvmVersion()
    {
        var header = Path.Combine(Repository.Root, "src", "Tatara", "tatara.h");
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(header))), Tatara.ExpectedAbi);
        Tatara.CheckStart(Tatara.ExpectedAbi, ToolchainIdentity.LlvmVersion);
        var abi = Assert.Throws<InvalidDataException>(() => Tatara.CheckStart("0123", ToolchainIdentity.LlvmVersion));
        Assert.Contains("0123", abi.Message, StringComparison.Ordinal);
        Assert.Contains(Tatara.ExpectedAbi, abi.Message, StringComparison.Ordinal);
        Assert.Contains("build Kimi again with the toolchain of its revision (a toolchain directory of this checkout's own, or a merge to the toolchain's revision)", abi.Message, StringComparison.Ordinal);
        var version = Assert.Throws<InvalidDataException>(() => Tatara.CheckStart(Tatara.ExpectedAbi, "1.0.0"));
        Assert.Contains("1.0.0", version.Message, StringComparison.Ordinal);
        Assert.Contains(ToolchainIdentity.LlvmVersion, version.Message, StringComparison.Ordinal);
        Assert.True(NativeToolchain.IsToolchainFailure(abi));
    }

    // llvm-nm's posix lines name a symbol exactly, also one with a space or a tab (SPEC 22.6: any nonempty symbol without NUL), as
    // `declare i32 @"a b"()` lists; a line without the kind, value and size is refused (the tool control comparison's reading).
    [Fact]
    public void PosixSymbolsReadTheNameBeforeTheLastThreeFields()
    {
        Assert.Equal(
            [new ObjectSymbol("a b", 'U'), new ObjectSymbol("t\tab", 'U'), new ObjectSymbol("?value@@YAHXZ", 'T')],
            PosixSymbols("a b U 0 0\nt\tab U 0 0\r\n?value@@YAHXZ T 0 0\n"));
        Assert.Throws<InvalidDataException>(() => PosixSymbols("a b\n"));
    }

    // The tool control comparison (permanent): the archive's opt, llc and lld-link with P13's arguments against Tatara's operations,
    // on the IR of the fixed rows or every corpus fixture, at O0 and O2. Every row is a program that links, so each must give equal
    // products down to the executable (Run): a generated row is an Application whose links take its manifest's libraries in the build's
    // order, the debug IR has the entry and takes the compiler's libraries, and a corpus fixture is a native fixture, which the fixture
    // runner links with the compiler's libraries and runs at both levels.
    [Fact]
    public async Task TataraEqualsTheTools()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var rows = Corpus is { } corpus
            ? Directory.GetFiles(Path.Combine(corpus, "fixtures"), "*.ll").SelectMany(ir => Levels.Select(level => new Row(ir, level, CompilerLibraries))).ToArray()
            : (await this.FixedRows()).Select(row => Generated(Manifest(row))).Concat(Levels.Select(level => new Row(this.DebugIr(), level, CompilerLibraries))).ToArray();
        await Run(rows, row => this.CompareWithTools(row, Coff));
    }

    // The tool control comparison on ELF (Tatara B7b1): the probe's Linux IR and the same IR naming a dependent library, at O0 and O2,
    // through opt, llc, llvm-nm, llvm-readelf and ld.lld -static, with no library.
    [Fact]
    public async Task TataraEqualsTheToolsOnElf()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        string[] probes = [this.LinuxProbe("probe", string.Empty), this.LinuxProbe("deplibs", DependentLibrary)];
        await Run([.. probes.SelectMany(ir => Levels.Select(level => new Row(ir, level, [])))], row => this.CompareWithTools(row, Elf));
    }

    // Tatara's ELF side (Tatara B7b1): the probe's Linux object at O0 and O2 is one member, a relocatable object (form o, no archive),
    // with _start defined, nothing undefined and no dependent library (the variant naming one has it); ld.lld -static links a static
    // executable (§6.1's checks), which llvm-readobj reads alike. Another format stays an inspection failure.
    [Fact]
    public async Task TataraInspectsAndLinksAnElf()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var tatara = Tatara.Instance;
        var probe = this.LinuxProbe("probe", string.Empty);
        foreach (var level in Levels)
        {
            var (obj, exe) = (Path.Combine(this.directory, "probe." + level + ".o"), Path.Combine(this.directory, "probe." + level));
            File.WriteAllBytes(obj, tatara.Emit(LinuxContext, probe, obj, level[1] - '0'));
            var facts = tatara.Inspect(LinuxContext, obj);
            var member = Assert.Single(facts.Members);
            Assert.Empty(member.Undefined);
            Assert.Equal([new ObjectSymbol("_start", 'T')], member.DefinedExternal);
            Assert.True(member.Form == 'o' && !facts.IsArchive && !facts.HasDefaultLibrary, level);
            tatara.Link(LinuxContext, ["ld.lld", .. ElfLinkArguments(obj, exe, [])]);
            var elf = ElfFile.Read(exe);
            elf.AssertStatic("_start");
            var (failure, listing) = await Launch("llvm-readobj", this.directory, "--file-headers", "--program-headers", exe);
            Assert.Equal(string.Empty, failure);
            var headers = listing[listing.IndexOf("ProgramHeaders [", StringComparison.Ordinal)..];
            Assert.Equal(elf.Entry, Convert.ToUInt64(Regex.Match(listing, @"(?m)^\s*Entry: 0x([0-9A-F]+)\r?$").Groups[1].Value, 16));
            Assert.Equal(elf.Segments, Regex.Matches(headers, @"(?m)^\s*Type: \S+ \(0x([0-9A-F]+)\)\r?$").Zip(
                Regex.Matches(headers, @"(?m)^\s*Flags \[ \(0x([0-9A-F]+)\)\r?$"), (t, f) => (Convert.ToUInt32(t.Groups[1].Value, 16), Convert.ToUInt32(f.Groups[1].Value, 16))));
        }

        var deplibs = Path.Combine(this.directory, "deplibs.o");
        File.WriteAllBytes(deplibs, tatara.Emit(LinuxContext, this.LinuxProbe("deplibs", DependentLibrary), deplibs, 0));
        Assert.True(tatara.Inspect(LinuxContext, deplibs).HasDefaultLibrary);
        var macho = Path.Combine(this.directory, "probe.macho.o");
        File.WriteAllBytes(macho, tatara.Emit(Context with { Triple = "x86_64-apple-macosx" }, probe, macho, 0));
        Assert.Contains("not a COFF or ELF object", Assert.Throws<IOException>(() => tatara.Inspect(Context, macho)).Message, StringComparison.Ordinal);
    }

    // Tatara's inspect reads archives (LinkAmbiguity phase A, design §2 "Tatara's read"): per member in archive order its symbols, its
    // form (o, i, s), its definitions in sections that admit copies and its .drectve's references, and the archive's index naming each
    // entry's member (Describe); an object or a DLL is one member of no archive; a bitcode member is read as lld-link links it (P4), its
    // copies and linker options as LLD's LTO input reads them (its IR symbol table; T's ruling 32); a directive's option takes each of
    // LLD's four prefixes, "/", "-", "/?" and "-?" (ruling 32). llvm-lib writes its members in reverse command-line order (as lib.exe does; its /list reverses them back), so its index, naming
    // the command line's last definer (P5), names the first in member order. A malformed archive fails with LLVM's text and Tatara runs
    // again; a thin archive fails. Libraries: the compiler's committed ones, or the pinned clang's objects archived by llvm-lib.
    [Theory]
    [InlineData("kernel32")]
    [InlineData("backend")]
    [InlineData("codec2")]
    [InlineData("object")]
    [InlineData("dll")]
    [InlineData("comdat")]
    [InlineData("directives")]
    [InlineData("bitcode")]
    [InlineData("bitcodefacts")]
    [InlineData("prefixes")]
    [InlineData("index")]
    [InlineData("malformed")]
    [InlineData("thin")]
    [InlineData("resource")]
    [InlineData("linebreak")]
    [InlineData("truncated")]
    [InlineData("unterminated")]
    [InlineData("emptyname")]
    public async Task TataraInspectsAnArchivesMembers(string row)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        const string Directives = """
            #pragma comment(linker, "/include:f")
            #pragma comment(linker, "/alternatename:a=b")
            #pragma comment(linker, "-INCLUDE:g /ALTERNATENAME:\"c=d\"")
            #pragma comment(linker, "/alternatename:bad")
            int directives(void) { return 0; }
            """;
        const string BitcodeFacts = """
            #pragma comment(linker, "/include:f")
            #pragma comment(linker, "/alternatename:a=b")
            extern "C" __declspec(selectany) int g = 1;
            extern "C" int plain = 2;
            inline int twice(int x) { return x * 2; }
            extern "C" int (*pointer)(int) = twice;
            """;
        await (row switch
        {
            "kernel32" => Expect(Kernel32Facts(), Task.FromResult(Kernel32)),
            "backend" => Expect("archive\no T:__chkstk\nindex 0:__chkstk", Task.FromResult(Backend)),
            "codec2" => Expect("archive\no T:helper\no U:helper T:codec_add\nindex 1:codec_add 0:helper", this.Library("codec.lib", NoAutolink, M1, M2)),
            "object" => this.InspectsAnObject(),
            "dll" => this.InspectsADll(),
            "comdat" => Expect(
                "archive\no T:?twice@@YAHH@Z D:pointer comdat:?twice@@YAHH@Z\no D:g D:plain comdat:g\nindex 0:?twice@@YAHH@Z 1:g 1:plain 0:pointer",
                this.Library("comdat.lib", ["-fno-autolink", "-fms-extensions", "-fdata-sections"], ("selectany.c", "__declspec(selectany) int g = 1;\nint plain = 2;\n"), ("inline.cpp", "inline int twice(int x) { return x * 2; }\nextern \"C\" int (*pointer)(int) = twice;\n"))),
            "directives" => Expect(
                "archive\no T:directives include:f alternatename:a=b include:g alternatename:c=d\nindex 0:directives",
                this.Library("directives.lib", ["-fms-extensions"], ("directives.c", Directives))),
            "bitcode" => Expect("archive\no T:bc_add\nindex 0:bc_add", this.Library("bitcode.lib", ["-fno-autolink", "-flto"], ("bitcode.c", "int bc_add(int a, int b) { return a + b; }\n"))),
            "bitcodefacts" => Expect(
                "archive\no W:?twice@@YAHH@Z W:g D:plain D:pointer comdat:?twice@@YAHH@Z comdat:g include:f alternatename:a=b\nindex 0:?twice@@YAHH@Z 0:g 0:plain 0:pointer",
                this.Library("bitcodefacts.lib", ["-fms-extensions", "-flto"], ("bitcodefacts.cpp", BitcodeFacts))),
            "prefixes" => Expect(
                "archive\no T:prefixes include:f alternatename:a=b\nindex 0:prefixes",
                this.Library("prefixes.lib", ["-fms-extensions"], ("prefixes.c", "#pragma comment(linker, \"/?include:f -?ALTERNATENAME:a=b /??include:x\")\nint prefixes(void) { return 0; }\n"))),
            "index" => this.InspectsTheIndex(),
            "malformed" => this.FailsOnAMalformedArchive(),
            "thin" => this.FailsOnAThinArchive(),

            // Crafted members (C-533): a COFF resource; short import members whose name holds a line break, is empty (an empty line
            // ends a block), ends inside their header (the magic alone), or has no NUL (LLD's "broken import library").
            "resource" => this.FailsOnAMember("not a COFF or ELF object", [0, 0, 0, 0, 0x20, 0, 0, 0, 0xFF, 0xFF, 0, 0, 0xFF, 0xFF, 0, 0, .. new byte[16]]),
            "linebreak" => this.FailsOnAMember("a name holds a line break", ShortImport("a\nb\0KERNEL32.dll\0"u8)),
            "emptyname" => this.FailsOnAMember("a name is empty", ShortImport("\0KERNEL32.dll\0"u8)),
            "truncated" => this.FailsOnAMember("broken import library", [0, 0, 0xFF, 0xFF]),
            "unterminated" => this.FailsOnAMember("broken import library", ShortImport("abc"u8)),
            _ => throw new ArgumentOutOfRangeException(nameof(row)),
        });
    }

    // The Tatara plan's B7b2a: the Linux row's libc-free modules (Hello, Hello ending in an Abort, design §5 row 14's allocator program and
    // B7b6's Stopwatch over the layer's clock), each generated for the row, verified and emitted by Tatara at O0 and O2 with no undefined
    // symbol, and linked by ld.lld at the row's entry into a static executable. Linux runs them under WSL (B7b2a's before its Ready, the
    // Stopwatch at B7b7), not here (N7).
    [Theory]
    [InlineData("Hello", "::Kimi.Console.writeLine(\"Hello, world!\")")]
    [InlineData("Abort", "::Kimi.Console.writeLine(\"Hello, world!\")\n$abort(\"x\")")]
    [InlineData("Heap", HeapRuntimeTest.AllocatorProgram)]
    [InlineData("Stopwatch", TimeLibraryTest.Measured)]
    public void TheLinuxRowsModulesLinkStatically(string name, string source)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var ir = Path.Combine(this.directory, name + ".ll");
        File.WriteAllText(ir, CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze(TargetProfile.Linux, source, "Hello.kimi", OutputKind.Application)));
        foreach (var level in Levels)
        {
            var (obj, exe) = (Path.Combine(this.directory, name + "." + level + ".o"), Path.Combine(this.directory, name + "." + level));
            File.WriteAllBytes(obj, Tatara.Instance.Emit(LinuxContext, ir, obj, level[1] - '0'));
            Assert.Empty(Assert.Single(Tatara.Instance.Inspect(LinuxContext, obj).Members).Undefined);
            Tatara.Instance.Link(LinuxContext, ["ld.lld", "-e", TargetProfile.Linux.EntrySymbol, .. ElfLinkArguments(obj, exe, [])]);
            ElfFile.Read(exe).AssertStatic(TargetProfile.Linux.EntrySymbol);
        }
    }

    // Two emits and links of the probe in two folders give equal objects and executables (§6.1).
    [Fact]
    public void ElfBuildsInTwoFoldersAreEqual()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var probe = this.LinuxProbe("probe", string.Empty);
        var products = new string[2];
        for (var i = 0; i < products.Length; i++)
        {
            var folder = Directory.CreateDirectory(Path.Combine(this.directory, "elf" + i)).FullName;
            var (obj, exe) = (Path.Combine(folder, "probe.o"), Path.Combine(folder, "probe"));
            File.WriteAllBytes(obj, Tatara.Instance.Emit(LinuxContext, probe, obj, 2));
            Tatara.Instance.Link(LinuxContext, ["ld.lld", .. ElfLinkArguments(obj, exe, [])]);
            products[i] = Hash(obj) + Hash(exe);
        }

        Assert.Equal(products[0], products[1]);
    }

    // C1's deep input: a global of 20,000 nested array types, deeper than dotnet's 1.5 MiB reserve allows and within the tools' 10 MB,
    // and the entry, so it links: Tatara's optimize, emit and link give the tools' products, at O0 and O2 (Run).
    [Fact]
    public async Task DeepInputEqualsTheTools()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var deep = Path.Combine(this.directory, "deep.ll");
        File.WriteAllText(deep, "target triple = \"x86_64-pc-windows-msvc\"\n\n@g = global " + string.Concat(Enumerable.Repeat("[1 x ", 20000)) + "i32" + new string(']', 20000) + " zeroinitializer\n\ndefine void @" + TargetProfile.Windows.EntrySymbol + "() {\nentry:\n  ret void\n}\n");
        await Run([.. Levels.Select(level => new Row(deep, level, CompilerLibraries))], row => this.CompareWithTools(row, Coff));
    }

    // Twenty native builds in one process alternating two programs give equal bytes each time.
    [Fact]
    public async Task RepeatedBuildsAreEqual()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        string[] rows = [await this.Generate("hello", Hello, "O2"), await this.Generate("m1", Milestone(1), "O2")];
        var first = new string?[2];
        for (var i = 0; i < 20; i++)
        {
            var manifest = Manifest(rows[i % 2]);
            await NativeToolchain.BuildManifest(manifest, (_, _) => { }, TestContext.Current.CancellationToken);
            var built = ArtifactPaths.Create(ArtifactPaths.IrOf(manifest), Level(manifest));
            var products = ArtifactFiles.Hash(built.Stem + ".obj") + ArtifactFiles.Hash(built.Executable); // Both exist: a missing one throws.
            first[i % 2] ??= products;
            Assert.Equal(first[i % 2], products);
        }
    }

    // A request that fails with return 1 is the toolchain's failure, and the next request succeeds: an invalid IR; an inline-asm
    // constraint error, which llc reports after opening the object and which leaves no object, as llc's (Tatara's emit writes no
    // file); then a link without kernel32's import library, whose imports are undefined symbols.
    [Fact]
    public async Task FailedRequestsLeaveTataraUsable()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var row = await this.Generate("hello", Hello, "O0");
        var ir = ArtifactPaths.IrOf(Manifest(row));
        var tatara = Tatara.Instance;
        var invalid = Path.Combine(this.directory, "invalid.ll");
        File.WriteAllText(invalid, "target triple = \"x86_64-pc-windows-msvc\"\n\ndefine i32 @f( {\n");
        var error = Assert.Throws<IOException>(() => tatara.Emit(Context, invalid, Path.Combine(this.directory, "invalid.obj"), 0));
        Assert.StartsWith("Tatara failed: emit\n", error.Message, StringComparison.Ordinal);
        Assert.True(NativeToolchain.IsToolchainFailure(error));
        var asm = Path.Combine(this.directory, "asmerror.ll");
        File.WriteAllText(asm, "target triple = \"x86_64-pc-windows-msvc\"\n\ndefine i32 @f() {\nentry:\n  %r = call i32 asm sideeffect \"mov $0, 1\", \"={notaregister}\"()\n  ret i32 %r\n}\n");
        var asmError = Assert.Throws<IOException>(() => tatara.Emit(Context, asm, Path.Combine(this.directory, "asmerror.obj"), 0));
        Assert.StartsWith("Tatara failed: emit\n", asmError.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(this.directory, "asmerror.obj")));
        var obj = Path.Combine(this.directory, "hello.obj");
        File.WriteAllBytes(obj, tatara.Emit(Context, ir, obj, 0));
        var link = Assert.Throws<IOException>(() => tatara.Link(Context, ["lld-link", obj, "/entry:__kimi_start", "/subsystem:console", "/nodefaultlib", "/out:" + Path.Combine(this.directory, "a.exe")]));
        Assert.StartsWith("Tatara failed: link\n", link.Message, StringComparison.Ordinal);
        Assert.Contains("undefined symbol", link.Message, StringComparison.Ordinal);
        var flavor = Assert.Throws<IOException>(() => tatara.Link(Context, ["lld", .. LinkArguments(obj, Path.Combine(this.directory, "c.exe"), CompilerLibraries)]));
        Assert.Contains("arguments[0] names the flavor, lld-link or ld.lld, not 'lld'", flavor.Message, StringComparison.Ordinal);
        tatara.Link(Context, ["lld-link", .. LinkArguments(obj, Path.Combine(this.directory, "b.exe"), CompilerLibraries)]);
        Assert.True(File.Exists(Path.Combine(this.directory, "b.exe")));
    }

    // ADOPTION.md's memory-function suite (tests/memory.c) on the runtime's memory functions (IMPL 21.4.7; the Tatara plan's decision
    // 24, B7r4, B7r5): the memory part alone, between the profile's target lines and the footer, optimized and emitted by Tatara at O0
    // and O2, defines exactly TargetProfile.MemorySymbols and calls nothing, itself included (no call instruction); linked with the suite
    // (the pinned clang's object) and kernel32 alone, no backend archive among the inputs, it exits 0.
    [Fact]
    public async Task TheMemoryPartPassesTheHelperSuite()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var tests = Path.Combine(Repository.Root, "src", "backend", "windows-x64", "tests");
        var work = this.directory;
        string[] oracle = ["--target=" + Triple, "-O0", "-ffreestanding", "-fno-builtin", "-fno-stack-protector", "-fno-autolink", "-mno-avx", "-c"];
        Assert.Equal(string.Empty, await Tool("clang", work, [.. oracle, Path.Combine(tests, "memory.c"), "-o", "suite.obj"]));
        var memory = Path.Combine(work, "memory.ll");
        File.WriteAllText(memory, $"target triple = \"{Triple}\"\ntarget datalayout = \"{TargetProfile.Windows.DataLayout}\"\n{LlvmModuleText.MemoryRuntime}{LlvmModuleText.Frame(TargetProfile.Windows).Footer}");
        foreach (var level in Levels)
        {
            var ir = level == "O2" ? Path.Combine(work, "memory.O2.ll") : memory;
            if (level == "O2")
            {
                File.WriteAllText(ir, Tatara.Instance.Optimize(Context, memory));
            }

            var obj = Path.Combine(work, $"memory.{level}.obj");
            File.WriteAllBytes(obj, Tatara.Instance.Emit(Context, ir, obj, level[1] - '0'));
            var member = Assert.Single(Tatara.Instance.Inspect(Context, obj).Members);
            Assert.Empty(member.Undefined);
            Assert.Equal(TargetProfile.MemorySymbols, member.DefinedExternal.Select(static x => x.Name).Order(StringComparer.Ordinal));
            var (failure, disassembly) = await Launch("llvm-objdump", work, "-d", obj);
            Assert.Equal(string.Empty, failure);
            Assert.DoesNotMatch(@"\scall", disassembly);
            var exe = Path.Combine(work, $"suite.{level}.exe");
            Assert.Equal(string.Empty, await Tool("lld-link", work, obj, "suite.obj", Kernel32, "/entry:test_entry", "/subsystem:console", "/nodefaultlib", "/Brepro", "/out:" + exe));
            using var process = Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = work, UseShellExecute = false, CreateNoWindow = true })!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }

            Assert.Equal(0, process.ExitCode);
        }
    }

    // Tatara takes no signal or exception-filter state (C-424 R1): after an optimize, an emit, a failed emit, an inspect and a link,
    // the process's unhandled-exception filter is still in the module that owned it before (the host's, never tatara.dll). LLVM's
    // Windows signal registration sets this filter and its console handler together, so an unchanged filter also means no console
    // handler. The filter is read by setting it and restoring it at once.
    [Fact]
    public async Task UnhandledExceptionFilterStaysTheHosts()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var row = await this.Generate("hello", Hello, "O0");
        var ir = ArtifactPaths.IrOf(Manifest(row));
        var tatara = Tatara.Instance;
        var before = FilterOwner();
        tatara.Optimize(Context, ir);
        var obj = Path.Combine(this.directory, "hello.obj");
        File.WriteAllBytes(obj, tatara.Emit(Context, ir, obj, 0));
        var asm = Path.Combine(this.directory, "asmerror.ll");
        File.WriteAllText(asm, "target triple = \"x86_64-pc-windows-msvc\"\n\ndefine i32 @f() {\nentry:\n  %r = call i32 asm sideeffect \"mov $0, 1\", \"={notaregister}\"()\n  ret i32 %r\n}\n");
        Assert.Throws<IOException>(() => tatara.Emit(Context, asm, Path.Combine(this.directory, "asmerror.obj"), 0));
        tatara.Inspect(Context, obj);
        tatara.Link(Context, ["lld-link", .. LinkArguments(obj, Path.Combine(this.directory, "hello.exe"), CompilerLibraries)]);
        var module = NativeLibrary.Load("tatara", typeof(Tatara).Assembly, DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.System32);
        NativeLibrary.Free(module);
        Assert.Equal(before, FilterOwner());
        Assert.NotEqual(module, before);
    }

    // Each fatal row in a child test process of its own, its folder this row's (Fatal); the child loads the tatara.dll beside its
    // Kimi.dll, as this process does.
    [Theory]
    [InlineData(nameof(Fatal.LinkerFatalRefusesEveryLaterCall))]
    [InlineData(nameof(Fatal.FatalErrorRefusesEveryLaterCall))]
    public async Task AFatalErrorRefusesEveryLaterCallOfItsProcess(string row)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        await FreshProcess.RunExplicit(typeof(Fatal), row, (Fatal.Folder, this.directory));
    }

    // A tool of the development toolchain run in the directory: its failure, empty on success (Launch); NativeToolchainTest's libraries too.
    internal static async Task<string> Tool(string name, string directory, params string[] arguments) => (await Launch(name, directory, arguments)).Failure;

    // The module that owns the process's unhandled-exception filter (IntPtr.Zero: none), through kernel32's exports.
    private static IntPtr FilterOwner()
    {
        var kernel32 = NativeLibrary.Load("kernel32.dll");
        var set = Marshal.GetDelegateForFunctionPointer<SetFilter>(NativeLibrary.GetExport(kernel32, "SetUnhandledExceptionFilter"));
        var owner = Marshal.GetDelegateForFunctionPointer<ModuleFromAddress>(NativeLibrary.GetExport(kernel32, "GetModuleHandleExW"));
        var filter = set(IntPtr.Zero);
        set(filter);
        NativeLibrary.Free(kernel32);

        // GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT
        return filter == IntPtr.Zero || owner(6, filter, out var module) == 0 ? filter : module;
    }

    private static string Milestone(int number) => Path.Combine(Repository.Root, "tests", "milestones", $"Milestone{number}.kimi");

    private static string Manifest(string row) => Directory.EnumerateFiles(row, "*.link.json", SearchOption.AllDirectories).Single();

    // The pair's optimization, as its file names carry it (Name.link.json beside Name.ll; the level from the manifest's text).
    private static string Level(string manifest) => File.ReadAllText(manifest).Contains("\"optimization\": \"O0\"", StringComparison.Ordinal) ? "O0" : "O2";

    // A generated row: its manifest's IR and optimization, and its libraries in the manifest's order, which the native build links in
    // (NativeBuild's ResolveLibraries): a reserved name is the compiler's supply, any other a program's, its input resolved against
    // the manifest's folder.
    private static Row Generated(string manifest)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(manifest));
        var folder = Path.GetDirectoryName(manifest)!;
        string[] libraries = [.. LinkManifest.Items(document.RootElement, "libraries").Select(x => Array.FindIndex(ToolchainIdentity.Supplies, s => s.Supply.Name == LinkManifest.Text(x, "libraries.name")) is var supply and >= 0
            ? CompilerLibraries[supply] : ArtifactFiles.ResolvePath(LinkManifest.Text(x, "libraries.input"), folder))];
        return new(ArtifactPaths.IrOf(manifest), Level(manifest), libraries);
    }

    private static string Hash(string path) => File.Exists(path) ? ArtifactFiles.Hash(path) : "(none)";

    // lld-link's arguments after its flavor, as NativeBuild.Link's list: the object, the libraries in the build's order, then P13's
    // options and LC3 D1's (no environment, no toolchain detection; the system root the executable's own .native folder, as /out: is
    // each side's own).
    private static string[] LinkArguments(string obj, string exe, string[] libraries)
        => [obj, .. libraries, "/entry:" + TargetProfile.Windows.EntrySymbol, "/subsystem:" + TargetProfile.Windows.Subsystem, "/nodefaultlib", "/Brepro", "/lldignoreenv", "/winsysroot:" + Path.ChangeExtension(exe, ".native"), "/out:" + exe];

    // ld.lld's arguments after its flavor: a static executable of the object and the libraries, no library a .deplibs section names.
    private static string[] ElfLinkArguments(string obj, string exe, string[] libraries) => ["-m", "elf_x86_64", "-static", "--no-dependent-libraries", obj, .. libraries, "-o", exe];

    // llvm-readobj --coff-directives' default-library fact: DEFAULTLIB in any case.
    private static bool CoffLibrary(string listing) => listing.Contains("DEFAULTLIB", StringComparison.OrdinalIgnoreCase);

    // llvm-readelf --sections' default-library fact: an SHT_LLVM_DEPENDENT_LIBRARIES section (a line's name and type; the null section's
    // line has no name and matches nothing).
    private static bool ElfLibrary(string listing)
        => Regex.Matches(listing, @"(?m)^\s*\[\s*\d+\]\s+(\S+)\s+(\S+)\s+[0-9a-f]{16}\s").Any(s => s.Groups[2].Value == "LLVM_DEPENDENT_LIBRARIES");

    // Runs every row (in parallel for a corpus: the tools run concurrently, Tatara's calls are serialized by its lock) and decides it: a
    // row passes only when its verdict is "equal" (a cleanup's note beside it aside), so an equal failure fails too and a run that
    // compares no executable fails (PLAN G1255). Appends each row's decision and outcome to the corpus's results.tsv
    // ("<row>\tpass\t<outcome>" or "<row>\tfail\t<outcome>", which run-tatara-corpus.ps1 reads) and fails with every row it failed.
    private static async Task Run(Row[] rows, Func<Row, Task<string>> compare)
    {
        var failed = new ConcurrentBag<string>();
        var results = Corpus is { } corpus ? Path.Combine(corpus, "results.tsv") : null;
        var gate = new object();
        await Parallel.ForEachAsync(rows, new ParallelOptions { MaxDegreeOfParallelism = results is null ? 1 : 6, CancellationToken = TestContext.Current.CancellationToken }, async (row, _) =>
        {
            string outcome;
            try
            {
                outcome = await compare(row);
            }
            catch (Exception ex)
            {
                outcome = "error\t" + ex.GetType().Name + ": " + ex.Message.ReplaceLineEndings(" ");
            }

            var decision = outcome.Split('\t')[0] == "equal" ? "pass" : "fail";
            if (decision == "fail")
            {
                failed.Add(row + "\t" + outcome);
            }

            lock (gate)
            {
                if (results is not null)
                {
                    File.AppendAllText(results, row + "\t" + decision + "\t" + outcome.ReplaceLineEndings(" ") + "\n");
                }
            }
        });
        Assert.True(failed.IsEmpty, string.Join("\n", failed));
    }

    // A row's verdict is decided before its work folder is deleted; a folder that cannot be deleted (an executable still held, for
    // example) is reported as the cleanup's, beside the verdict.
    private static string Clean(string work, string verdict)
    {
        try
        {
            Directory.Delete(work, true);
            return verdict;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return verdict + "\tcleanup: " + ex.GetType().Name + ": " + ex.Message;
        }
    }

    // A failed call's text after its header line ("Tool failed: <path>; exit=<code>" or "Tatara failed: <operation>"), with the tools'
    // paths replaced by their names and the given folders removed: the tool's text and Tatara's compare equal when they say the same.
    private static string FailureText(string message, params string[] folders)
    {
        var text = message[(message.IndexOf('\n') + 1)..];
        foreach (var name in ToolNames)
        {
            text = text.Replace(Path.Combine(Root, name + ".exe"), name, StringComparison.OrdinalIgnoreCase).Replace(name + ".exe", name, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var folder in folders)
        {
            text = text.Replace(folder + Path.DirectorySeparatorChar, string.Empty, StringComparison.OrdinalIgnoreCase).Replace(folder, "<dir>", StringComparison.OrdinalIgnoreCase);
        }

        return text.ReplaceLineEndings(" ").Trim();
    }

    // Every file of the two folders: equal names and bytes.
    private static List<string> CompareFiles(string left, string right)
    {
        var differences = new List<string>();
        var names = Directory.EnumerateFiles(left, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(left, f))
            .Union(Directory.EnumerateFiles(right, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(right, f)));
        foreach (var name in names.Order(StringComparer.Ordinal))
        {
            var (a, b) = (Path.Combine(left, name), Path.Combine(right, name));
            if (!File.Exists(a) || !File.Exists(b))
            {
                differences.Add(name + " only in " + (File.Exists(a) ? "tools" : "tatara"));
            }
            else if (Hash(a) != Hash(b))
            {
                differences.Add(name);
            }
        }

        return differences;
    }

    // A tool of the root run in the directory: its failure ("<name> exit=<code>", a line end, then its output; empty on success) and
    // its standard output.
    private static async Task<(string Failure, string Output)> Launch(string name, string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Root, name + ".exe")) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode == 0 ? string.Empty : $"{name} exit={process.ExitCode}\n{await output}{errors}", await output);
    }

    // The tools' facts of an object, as the external build read them before Tatara: the two llvm-nm listings and the target's
    // llvm-readobj listing with its default-library predicate.
    private static async Task<(string Failure, string Facts)> ToolFacts(string directory, string obj, ToolTarget target)
    {
        var undefined = await Launch("llvm-nm", directory, "--undefined-only", "--format=posix", obj);
        var defined = await Launch("llvm-nm", directory, "--defined-only", "--extern-only", "--format=posix", obj);
        var inspection = await Launch("llvm-readobj", directory, [.. target.Inspection, obj]);
        var failure = undefined.Failure + defined.Failure + inspection.Failure;
        var member = new ObjectMember('o', PosixSymbols(undefined.Output), PosixSymbols(defined.Output), [], []);
        return failure.Length != 0 ? (failure, string.Empty) : (string.Empty, Facts(new ObjectFacts([member], [], IsArchive: false, target.DefaultLibrary(inspection.Output))));
    }

    // llvm-nm's --format=posix lines: the name, then the kind's letter, the value and the size, each after one space. The name is
    // everything before the last three fields, so a symbol with a space or a tab (SPEC 22.6) is read exactly.
    private static List<ObjectSymbol> PosixSymbols(string listing)
    {
        var symbols = new List<ObjectSymbol>();
        foreach (var line in listing.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var text = line.AsSpan().TrimEnd('\r');
            var size = text.LastIndexOf(' ');
            var value = size > 0 ? text[..size].LastIndexOf(' ') : -1;
            var kind = value > 0 ? text[..value].LastIndexOf(' ') : -1;
            if (kind < 1 || value != kind + 2)
            {
                throw new InvalidDataException("Unexpected llvm-nm line: " + line);
            }

            symbols.Add(new ObjectSymbol(text[..kind].ToString(), text[kind + 1]));
        }

        return symbols;
    }

    // An object's facts as the comparison prints them: its one member's symbols and its default-library flag.
    private static string Facts(ObjectFacts facts)
    {
        var member = Assert.Single(facts.Members);
        return string.Join(' ', member.Undefined) + " | " + string.Join(' ', member.DefinedExternal) + " | default library " + facts.HasDefaultLibrary;
    }

    // An inspection as the archive rows compare it: "archive" or "object", then ", default library" when a member names one; a line per
    // member: its form, "<letter>:<name>" per undefined and per defined symbol, "comdat:<name>" per definition that admits copies and
    // "include:<name>" or "alternatename:<from>=<to>" per directive reference; then "index" and its "<member>:<name>" entries.
    private static string Describe(ObjectFacts facts)
    {
        var lines = new List<string> { (facts.IsArchive ? "archive" : "object") + (facts.HasDefaultLibrary ? ", default library" : string.Empty) };
        foreach (var member in facts.Members)
        {
            var parts = member.Undefined.Concat(member.DefinedExternal).Select(static s => s.Kind + ":" + s.Name)
                .Concat(member.Comdats.Select(static name => "comdat:" + name))
                .Concat(member.Directives.Select(static d => d.Kind == 'I' ? "include:" + d.Name : "alternatename:" + d.Name + "=" + d.Target));
            lines.Add(member.Form + string.Concat(parts.Select(static part => " " + part)));
        }

        lines.Add("index" + string.Concat(facts.Index.Select(static entry => " " + entry.Member + ":" + entry.Name)));
        return string.Join('\n', lines);
    }

    // A COFF short import member (IMAGE_FILE_IMPORT_HEADER: Sig2 0xFFFF, machine x64, the data's size, code imported by name), then the
    // data: the symbol's name and the DLL's, each ended by a NUL.
    private static byte[] ShortImport(ReadOnlySpan<byte> data)
    {
        var header = new byte[20];
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2), 0xFFFF);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), 0x8664);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12), data.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(18), 4);
        return [.. header, .. data];
    }

    // The row's input inspected and described (Describe).
    private static async Task Expect(string expected, Task<string> input) => Assert.Equal(expected, Describe(Tatara.Instance.Inspect(Context, await input)));

    // kernel32.lib as llvm-dlltool writes it: the import descriptor (referencing the other two), the null import descriptor and the null
    // thunk data, objects whose symbols read I, then a short import member per export of kernel32.def in its order (code: T X and T
    // __imp_X); its index names each definition's member, in name order.
    private static string Kernel32Facts()
    {
        string[] exports = [.. File.ReadAllLines(Path.ChangeExtension(Kernel32, ".def")).Skip(2).Select(static x => x.Trim()).Where(static x => x.Length != 0)];
        string[] members = ["o U:__NULL_IMPORT_DESCRIPTOR U:\u007fKERNEL32_NULL_THUNK_DATA I:__IMPORT_DESCRIPTOR_KERNEL32", "o I:__NULL_IMPORT_DESCRIPTOR", "o I:\u007fKERNEL32_NULL_THUNK_DATA", .. exports.Select(static x => $"i T:{x} T:__imp_{x}")];
        var index = members.SelectMany(static (member, i) => member.Split(' ').Skip(1).Where(static part => part[0] != 'U').Select(part => (Member: i, Name: part[2..])));
        return "archive\n" + string.Join('\n', members) + "\nindex" + string.Concat(index.OrderBy(static e => e.Name, StringComparer.Ordinal).Select(static e => $" {e.Member}:{e.Name}"));
    }

    // The steps on the target, each run by the tools and by Tatara; a later step's input is that side's own product. The inspect step
    // writes each side's facts to inspected (tools, then Tatara). The tools' LLD is lld-link.exe with the target's flavor arguments; both
    // links take the row's libraries.
    private static IEnumerable<(string Step, Func<Task<string>> Tools, Action Tatara)> Steps(Row row, string tools, string tatara, ToolTarget target, string[] inspected)
    {
        var (ir, level, libraries) = row;
        var (library, context) = (Tatara.Instance, target.Context);
        var (toolIr, tataraIr) = (ir, ir);
        if (level == "O2")
        {
            (toolIr, tataraIr) = (Path.Combine(tools, "input.O2.ll"), Path.Combine(tatara, "input.O2.ll"));
            yield return ("optimize", () => Tool("opt", tools, "-S", "-passes=default<O2>", "-mtriple=" + context.Triple, ir, "-o", toolIr), () => File.WriteAllText(tataraIr, library.Optimize(context, ir)));
        }

        yield return ("emit", () => Tool("llc", tools, "-" + level, "-filetype=obj", "-mtriple=" + context.Triple, "-mcpu=" + context.Cpu, "-mattr=" + context.Features, "-relocation-model=" + context.RelocationModel, "-code-model=" + context.CodeModel, toolIr, "-o", "input.obj"), () => File.WriteAllBytes(Path.Combine(tatara, "input.obj"), library.Emit(context, tataraIr, Path.Combine(tatara, "input.obj"), level[1] - '0')));
        yield return ("inspect", async () =>
        {
            var (failure, facts) = await ToolFacts(tools, Path.Combine(tools, "input.obj"), target);
            inspected[0] = facts;
            return failure;
        }, () => inspected[1] = Facts(library.Inspect(context, Path.Combine(tatara, "input.obj"))));
        yield return ("link", () => Tool("lld-link", tools, [.. target.ToolFlavor, .. target.Link(Path.Combine(tools, "input.obj"), Path.Combine(tools, "input.exe"), libraries)]), () => library.Link(context, [target.Flavor, .. target.Link(Path.Combine(tatara, "input.obj"), Path.Combine(tatara, "input.exe"), libraries)]));
    }

    // One IR at one level on the target: the tools (opt -S default<O2>, llc, llvm-nm and llvm-readobj, LLD with the target's arguments)
    // in one folder, Tatara's optimize, emit, inspect and link in another, the same input file read by both; equal O2 text, object,
    // object facts and executable, or the same failing step with the same text (FailureText).
    private async Task<string> CompareWithTools(Row row, ToolTarget target)
    {
        var work = Path.Combine(this.directory, Guid.NewGuid().ToString("N"));
        var (tools, tatara) = (Path.Combine(work, "tools"), Path.Combine(work, "tatara"));
        Directory.CreateDirectory(tools);
        Directory.CreateDirectory(tatara);
        var inspected = new string[2];
        (string Step, string Text) toolFailure = (string.Empty, string.Empty), tataraFailure = (string.Empty, string.Empty);
        foreach (var (step, run, call) in Steps(row, tools, tatara, target, inspected))
        {
            toolFailure = toolFailure.Step.Length == 0 && await run() is { Length: > 0 } failed ? (step, FailureText(failed, tools)) : toolFailure;
            try
            {
                if (tataraFailure.Step.Length == 0)
                {
                    call();
                }
            }
            catch (IOException ex)
            {
                tataraFailure = (step, FailureText(ex.Message, tatara));
            }
        }

        var differences = CompareFiles(tools, tatara);
        if (inspected[0] != inspected[1])
        {
            differences.Add("inspect: tools " + inspected[0] + " || tatara " + inspected[1]);
        }

        var verdict = toolFailure != tataraFailure
            ? "differ\tfailure: tools " + (toolFailure.Step.Length == 0 ? "none" : toolFailure) + " | tatara " + (tataraFailure.Step.Length == 0 ? "none" : tataraFailure)
            : differences.Count != 0 ? "differ\t" + string.Join("; ", differences) : toolFailure.Step.Length != 0 ? "equal failure\t" + toolFailure.Step : "equal";
        return Clean(work, verdict);
    }

    // C-424 N6: an IR with a CodeView compile unit (the debug information no fixed row's IR carries), and the entry, so it links.
    private string DebugIr()
    {
        var path = Path.Combine(this.directory, "debug.ll");
        var ir = $$"""
            target triple = "x86_64-pc-windows-msvc"

            @g = global i32 7, align 4

            define void @{{TargetProfile.Windows.EntrySymbol}}() {
            entry:
              ret void
            }

            define i32 @f(i32 %x) !dbg !8 {
            entry:
              %p = alloca i32, align 4
              store i32 %x, ptr %p, align 4, !dbg !12
              %v = load i32, ptr %p, align 4, !dbg !12
              %w = load i32, ptr @g, align 4, !dbg !13
              %y = add i32 %v, %w, !dbg !13
              ret i32 %y, !dbg !13
            }

            !llvm.dbg.cu = !{!0}
            !llvm.module.flags = !{!3, !4}

            !0 = distinct !DICompileUnit(language: DW_LANG_C99, file: !1, producer: "debug row", isOptimized: false, runtimeVersion: 0, emissionKind: FullDebug)
            !1 = !DIFile(filename: "debug.c", directory: "C:\\debug")
            !3 = !{i32 2, !"CodeView", i32 1}
            !4 = !{i32 2, !"Debug Info Version", i32 3}
            !8 = distinct !DISubprogram(name: "f", scope: !1, file: !1, line: 1, type: !9, scopeLine: 1, spFlags: DISPFlagDefinition, unit: !0)
            !9 = !DISubroutineType(types: !10)
            !10 = !{null}
            !12 = !DILocation(line: 2, column: 3, scope: !8)
            !13 = !DILocation(line: 3, column: 5, scope: !8)

            """;
        File.WriteAllText(path, ir);
        return path;
    }

    // The B7 probe (artifacts\tatara\b7\probe\nolibc.c) as hand-written Linux IR: _start writes "Hello, world!" with the write system
    // call and ends with exit_group, without a C library; then the given metadata.
    private string LinuxProbe(string name, string metadata)
    {
        var path = Path.Combine(this.directory, name + ".ll");
        var ir = $$"""
            source_filename = "probe"
            target triple = "x86_64-unknown-linux-musl"

            @text = private unnamed_addr constant [14 x i8] c"Hello, world!\0A"

            define void @_start() #0 {
            entry:
              %written = call i64 asm sideeffect "syscall", "={rax},{rax},{rdi},{rsi},{rdx},~{rcx},~{r11},~{memory}"(i64 1, i64 1, ptr @text, i64 14)
              %exited = call i64 asm sideeffect "syscall", "={rax},{rax},{rdi},~{rcx},~{r11},~{memory}"(i64 231, i64 0)
              unreachable
            }

            attributes #0 = { noreturn nounwind uwtable }
            {{metadata}}

            """;
        File.WriteAllText(path, ir);
        return path;
    }

    // The fixed rows, each a generated project: Hello and Milestones 1, 17 and 30 at O0 and O2, the FFI program with a user static
    // library (test-cli.ps1's foreign supply) at O2, and Hello's Debug build.
    private async Task<string[]> FixedRows()
    {
        var rows = new List<string>();
        foreach (var level in Levels)
        {
            rows.Add(await this.Generate("hello-" + level, Hello, level));
            foreach (var number in new[] { 1, 17, 30 })
            {
                rows.Add(await this.Generate($"m{number}-{level}", Milestone(number), level));
            }
        }

        rows.Add(await this.Generate("debug-O2", Hello, "O2", debug: true));
        rows.Add(await this.GenerateForeign());
        return [.. rows];
    }

    private async Task<string> Generate(string name, string source, string level, bool debug = false, string supply = "")
    {
        var row = Path.Combine(this.directory, name);
        Directory.CreateDirectory(row);
        var stem = Path.GetFileNameWithoutExtension(source);
        File.Copy(source, Path.Combine(row, stem + ".kimi"), true);
        var project = Path.Combine(row, stem + ".kimiproj");
        File.WriteAllText(project, $"Targets=\n  \"{Triple}\"\nOutputKind=\"Application\"\nOptimization=\"{level}\"\n{supply}\n");
        var console = new DiagnosticContractTest.DiagnosticConsole();
        Assert.True(Project.TryCreate(new Kimigayo(console), null, project, out var loaded), console.Text);
        loaded.KimiOptions.Debug = debug;
        Assert.True(await loaded.Generate(TestContext.Current.CancellationToken), console.Text);
        return row;
    }

    private async Task<string> GenerateForeign()
    {
        var source = Path.Combine(this.directory, "Foreign.kimi");
        var program = """
            group Native
                #LibraryImport("codec", "mix")
                public unsafe func mix(a: i8, b: u16, c: i32, d: i64, e: f32, f: f64) -> f64
                #LibraryImport("codec", "notify")
                public unsafe func notify(value: u32) -> ()
                #LibraryImport("codec", "read_total")
                public unsafe func readTotal() -> i32
                #LibraryImport("codec", "?value@@YAHXZ")
                public unsafe func mangled() -> i32
            public func main()
                var total: f64 = 0.0
                unsafe => total = Native.mix(-1, 65535, 3, -4, 0.5, 2.25)
                require total == 65535.75 else => $abort("mix")
                unsafe => Native.notify(7)
                unsafe => Native.notify(5)
                var sum: i32 = 0
                unsafe => sum = Native.readTotal()
                require sum == 12 else => $abort("notify")
                var answer: i32 = 0
                unsafe => answer = Native.mangled()
                require answer == 42 else => $abort("mangled")
                Console.writeLine("foreign ok")
            """;
        File.WriteAllText(source, program);
        var native = Path.Combine(this.directory, "ffi-O2", "native");
        Directory.CreateDirectory(native);
        var codec = """
            static int total;
            double mix(signed char a, unsigned short b, int c, long long d, float e, double f) { return a + b + c + d + e + f; }
            void notify(unsigned int value) { total += (int)value; }
            int read_total(void) { return total; }
            int mangled(void) __asm__("?value@@YAHXZ");
            int mangled(void) { return 42; }
            """;
        File.WriteAllText(Path.Combine(native, "codec.c"), codec);
        Assert.Equal(string.Empty, await Tool("clang", native, "--target=" + Triple, "-O2", "-fno-autolink", "-fno-stack-protector", "-c", "codec.c", "-o", "codec.obj"));
        Assert.Equal(string.Empty, await Tool("llvm-lib", native, "/out:codec.lib", "codec.obj"));
        var supply = $"NativeLibraries=\n  \"{Triple}\"=\n    codec={{ Kind=\"static\" Input=\"native/codec.lib\" }}";
        return await this.Generate("ffi-O2", source, "O2", supply: supply);
    }

    // Each source written into the folder and compiled by the pinned clang (a .cpp name as C++) with the archive rows' line and the
    // options, as "<stem>.obj".
    private async Task Compile(string[] options, params (string Name, string Source)[] sources)
    {
        foreach (var (name, source) in sources)
        {
            File.WriteAllText(Path.Combine(this.directory, name), source);
            Assert.Equal(string.Empty, await Tool("clang", this.directory, ["--target=" + Triple, "-O2", "-fno-builtin", "-fno-stack-protector", .. options, "-c", name, "-o", Path.ChangeExtension(name, ".obj")]));
        }
    }

    // A static library of the compiled members, archived by llvm-lib in their order: its path.
    private async Task<string> Library(string library, string[] options, params (string Name, string Source)[] members)
    {
        await this.Compile(options, members);
        Assert.Equal(string.Empty, await Tool("llvm-lib", this.directory, ["/out:" + library, .. members.Select(static m => Path.ChangeExtension(m.Name, ".obj"))]));
        return Path.Combine(this.directory, library);
    }

    // The Hello module's object is one member of no archive, a relocatable object with no copy, no directive reference and no index
    // (its symbols are TataraEqualsTheTools').
    private async Task InspectsAnObject()
    {
        var ir = ArtifactPaths.IrOf(Manifest(await this.Generate("hello", Hello, "O0")));
        var obj = Path.Combine(this.directory, "hello.obj");
        File.WriteAllBytes(obj, Tatara.Instance.Emit(Context, ir, obj, 0));
        var facts = Tatara.Instance.Inspect(Context, obj);
        var member = Assert.Single(facts.Members);
        Assert.Equal(('o', false), (member.Form, facts.IsArchive));
        Assert.Empty(member.Comdats);
        Assert.Empty(member.Directives);
        Assert.Empty(facts.Index);
    }

    // A DLL (lld-link /dll /noentry of a clang object) is one member of no archive, a linked image, whose symbols it does not list.
    private async Task InspectsADll()
    {
        await this.Compile(NoAutolink, M2);
        Assert.Equal(string.Empty, await Tool("lld-link", this.directory, "/dll", "/noentry", "/nodefaultlib", "/out:codec.dll", "m2.obj"));
        await Expect("object\ns\nindex", Task.FromResult(Path.Combine(this.directory, "codec.dll")));
    }

    // The index names each entry's member by its position (P5's archives, llvm-lib's members in reverse command-line order): helper's is
    // m3 for llvm-lib m1 m2 m3 and m2 for m1 m3 m2, each the first member, the command line's last definer.
    private async Task InspectsTheIndex()
    {
        await Expect("archive\no U:other T:helper\no T:helper\no U:helper T:codec_add\nindex 2:codec_add 0:helper", this.Library("index1.lib", NoAutolink, M1, M2, M3));
        await Expect("archive\no T:helper\no U:other T:helper\no U:helper T:codec_add\nindex 2:codec_add 0:helper", this.Library("index2.lib", NoAutolink, M1, M3, M2));
    }

    // A malformed archive fails with LLVM's text (return 1, LinkAmbiguity's P-F2), and Tatara runs again: an object inspects after it.
    private async Task FailsOnAMalformedArchive()
    {
        var malformed = Path.Combine(this.directory, "malformed.lib");
        File.WriteAllText(malformed, "!<arch>\nnot a library member\n");
        var error = Assert.Throws<IOException>(() => Tatara.Instance.Inspect(Context, malformed));
        Assert.StartsWith("Tatara failed: inspect\n", error.Message, StringComparison.Ordinal);
        Assert.Contains("truncated or malformed archive", error.Message, StringComparison.Ordinal);
        await this.Compile(NoAutolink, M2);
        await Expect("object\no T:helper\nindex", Task.FromResult(Path.Combine(this.directory, "m2.obj")));
    }

    // An archive whose one member is the given bytes (written here: llvm-lib would read a crafted member itself; a GNU archive with no
    // index) fails (1) with the text naming the member, and Tatara runs again (no call returned 2).
    private Task FailsOnAMember(string text, byte[] member)
    {
        var library = Path.Combine(this.directory, "crafted.lib");
        var header = Encoding.ASCII.GetBytes($"{"m.obj/",-16}{0,-12}{0,-6}{0,-6}{644,-8}{member.Length,-10}`\n");
        File.WriteAllBytes(library, [.. "!<arch>\n"u8, .. header, .. member, .. member.Length % 2 == 0 ? [] : "\n"u8]);
        var error = Assert.Throws<IOException>(() => Tatara.Instance.Inspect(Context, library));
        Assert.Contains("crafted.lib(m.obj): " + text, error.Message, StringComparison.Ordinal);
        Assert.Null(Tatara.FirstRefusal());
        return Task.CompletedTask;
    }

    // A thin archive (llvm-lib /llvmlibthin) fails: its members are outside the file.
    private async Task FailsOnAThinArchive()
    {
        await this.Compile(NoAutolink, M2);
        Assert.Equal(string.Empty, await Tool("llvm-lib", this.directory, "/llvmlibthin", "/out:thin.lib", "m2.obj"));
        Assert.Contains("thin archive", Assert.Throws<IOException>(() => Tatara.Instance.Inspect(Context, Path.Combine(this.directory, "thin.lib"))).Message, StringComparison.Ordinal);
    }

    // The fatal rows, each run by AFatalErrorRefusesEveryLaterCallOfItsProcess in a child test process of its own: a return of 2 makes
    // the failing request and every later one the toolchain's failure naming the first cause, for the rest of the process. Each row's
    // folder is its parent's (named by Folder, removed by the parent).
    public sealed class Fatal
    {
        internal const string Folder = "KIMI_TATARA_FATAL_FOLDER";

        // LLD's fatal(): a malformed static library; LLD's canRunAgain is false.
        [Fact(Explicit = true)]
        public void LinkerFatalRefusesEveryLaterCall()
        {
            var directory = Environment.GetEnvironmentVariable(Folder)!;
            File.WriteAllText(Path.Combine(directory, "f.ll"), "target triple = \"x86_64-pc-windows-msvc\"\n\ndefine void @f() {\n  ret void\n}\n");
            File.WriteAllText(Path.Combine(directory, "malformed.lib"), "!<arch>\nnot a library member\n");
            var tatara = Tatara.Instance;
            var obj = Path.Combine(directory, "f.obj");
            File.WriteAllBytes(obj, tatara.Emit(Context, Path.Combine(directory, "f.ll"), obj, 0));
            var error = Assert.Throws<IOException>(() => tatara.Link(Context, ["lld-link", obj, Path.Combine(directory, "malformed.lib"), "/entry:f", "/subsystem:console", "/nodefaultlib", "/out:" + Path.Combine(directory, "f.exe")]));
            Assert.StartsWith("Tatara failed: link\n", error.Message, StringComparison.Ordinal);
            Assert.Contains("lld-link: error:", error.Message, StringComparison.Ordinal);
            Assert.Contains("truncated or malformed archive", error.Message, StringComparison.Ordinal);
            var later = Assert.Throws<IOException>(() => tatara.Inspect(Context, obj));
            Assert.StartsWith("Tatara failed: inspect\n", later.Message, StringComparison.Ordinal);
            Assert.Contains("truncated or malformed archive", later.Message, StringComparison.Ordinal);
            Assert.True(NativeToolchain.IsToolchainFailure(later));
        }

        // An LLVM fatal error during emit; no object is written, as llc leaves none (C-424 R2).
        [Fact(Explicit = true)]
        public void FatalErrorRefusesEveryLaterCall()
        {
            var directory = Environment.GetEnvironmentVariable(Folder)!;
            var fatal = Path.Combine(directory, "fatal.ll");
            var ir = """
                target triple = "x86_64-pc-windows-msvc"

                declare <4 x i32> @llvm.aarch64.neon.abs.v4i32(<4 x i32>)

                define <4 x i32> @f(<4 x i32> %x) {
                  %y = call <4 x i32> @llvm.aarch64.neon.abs.v4i32(<4 x i32> %x)
                  ret <4 x i32> %y
                }

                """;
            File.WriteAllText(fatal, ir);
            var tatara = Tatara.Instance;
            var error = Assert.Throws<IOException>(() => tatara.Emit(Context, fatal, Path.Combine(directory, "fatal.obj"), 2));
            Assert.StartsWith("Tatara failed: emit\n", error.Message, StringComparison.Ordinal);
            Assert.Contains("Cannot select", error.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(directory, "fatal.obj")));
            var later = Assert.Throws<IOException>(() => tatara.Inspect(Context, Path.Combine(directory, "fatal.obj")));
            Assert.StartsWith("Tatara failed: inspect\n", later.Message, StringComparison.Ordinal);
            Assert.Contains("Cannot select", later.Message, StringComparison.Ordinal);
            Assert.True(NativeToolchain.IsToolchainFailure(later));
        }
    }

    // A format of the tool control comparison: Tatara's context, llvm-readobj's arguments for the object's default-library fact and its
    // reading, LLD's flavor, the tool's arguments selecting it, and the link's arguments after the flavor for an object, an executable and
    // the row's libraries.
    private sealed record ToolTarget(
        TataraContext Context, string[] Inspection, Func<string, bool> DefaultLibrary, string Flavor, string[] ToolFlavor, Func<string, string, string[], string[]> Link);

    // A row of the tool control comparison: an IR, its level and the libraries both links take after the object, in the build's order (a
    // generated row's manifest's, the compiler's for another COFF row, none on ELF); named "<ir>|<level>" in a failure and in results.tsv.
    private sealed record Row(string Ir, string Level, string[] Libraries)
    {
        public override string ToString() => this.Ir + "|" + this.Level;
    }

    // §6.1's managed reading of a static ELF executable (Tatara B7b1): ELF64 little-endian; the header's type, machine and entry, each
    // program header's type and flags, and .symtab's symbols with their values.
    private sealed record ElfFile(int Type, int Machine, ulong Entry, (uint Type, uint Flags)[] Segments, Dictionary<string, ulong> Symbols)
    {
        internal static ElfFile Read(string path)
        {
            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.AsSpan(0, 6).SequenceEqual("\u007fELF\u0002\u0001"u8), path + " is not an ELF64 little-endian file.");
            int U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at));
            uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
            ulong U64(int at) => BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(at));
            var (programs, programSize, sections, sectionSize) = ((int)U64(32), U16(54), (int)U64(40), U16(58));
            var segments = Enumerable.Range(0, U16(56)).Select(i => (U32(programs + (i * programSize)), U32(programs + (i * programSize) + 4))).ToArray();
            var symbols = new Dictionary<string, ulong>();
            for (var header = sections; header < sections + (U16(60) * sectionSize); header += sectionSize)
            {
                // SHT_SYMTAB: its entries after the null symbol; sh_link names its string table's header.
                if (U32(header + 4) == 2)
                {
                    var (start, end, size) = ((int)U64(header + 24), (int)(U64(header + 24) + U64(header + 32)), (int)U64(header + 56));
                    var names = (int)U64(sections + ((int)U32(header + 40) * sectionSize) + 24);
                    for (var symbol = start + size; symbol < end; symbol += size)
                    {
                        var name = names + (int)U32(symbol);
                        symbols.TryAdd(Encoding.UTF8.GetString(bytes, name, Array.IndexOf(bytes, (byte)0, name) - name), U64(symbol + 8));
                    }
                }
            }

            return new ElfFile(U16(16), U16(18), U64(24), segments, symbols);
        }

        // ET_EXEC for EM_X86_64; no PT_INTERP and no PT_DYNAMIC; one executable PT_LOAD; one PT_GNU_STACK, not executable; the entry at
        // the entry symbol.
        internal void AssertStatic(string entry)
        {
            Assert.Equal((2, 62), (this.Type, this.Machine));
            Assert.DoesNotContain(this.Segments, s => s.Type is 2 or 3);
            Assert.Single(this.Segments, s => s.Type == 1 && (s.Flags & 1) != 0);
            Assert.Equal(0u, Assert.Single(this.Segments, s => s.Type == 0x6474e551).Flags & 1);
            Assert.Equal(this.Symbols[entry], this.Entry);
        }
    }
}
