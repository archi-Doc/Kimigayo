// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using System.Runtime.InteropServices;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Target;
using Xunit;

namespace XunitTest;

public class CoreModelTest
{
    // The none row's kimi_backend symbols: compiler-rt 22.1.8's soft-float routines in src/backend/none-x64/profile.json's order (the
    // Tatara plan's B9e; the catalog's providedSymbols, the archive's defined set).
    private static readonly string[] NoneBackendSymbols =
    [
        "__adddf3", "__addsf3", "__cmpdf2", "__cmpsf2", "__divdf3", "__divsf3", "__eqdf2", "__eqsf2", "__extendsfdf2", "__fe_getround",
        "__fe_raise_inexact", "__fixdfdi", "__fixdfsi", "__fixsfdi", "__fixsfsi", "__fixunsdfdi", "__fixunsdfsi", "__fixunssfdi",
        "__fixunssfsi", "__floatdidf", "__floatdisf", "__floatsidf", "__floatsisf", "__floatundidf", "__floatundisf", "__floatunsidf",
        "__floatunsisf", "__gedf2", "__gesf2", "__gtdf2", "__gtsf2", "__ledf2", "__lesf2", "__ltdf2", "__ltsf2", "__muldf3", "__mulsf3",
        "__nedf2", "__nesf2", "__subdf3", "__subsf3", "__truncdfsf2", "__unorddf2", "__unordsf2",
    ];

    [Fact]
    public void CompilationRegistersItsPrimaryKotonoha()
    {
        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        Assert.True(compilation.TryGetKotonoha(kotonoha.Id, out var resolvedKotonoha));
        Assert.Same(kotonoha, resolvedKotonoha);
    }

    [Fact]
    public void CodeContextRejectsADeclarationContainerOwnedByAnotherKotonoha()
    {
        var compilation = Compilation.CreateForTest();
        var context = compilation.Kotonoha.CreateCodeContext();
        var external = new Kotonoha(compilation, "External", "external.kimi");

        var exception = Assert.Throws<ArgumentException>(
            () => context.Parse(external.RootKoto, "var invalid = 1"));

        Assert.Equal("parentKoto", exception.ParamName);
        Assert.Empty(external.RootKoto.Members);
    }

    [Theory]
    [InlineData("x86_64-pc-win32-msvc", "windows", 64)]
    [InlineData("x86_64-unknown-linux-gnu", "linux", 64)]
    [InlineData("aarch64-apple-macosx", "macos", 64)]
    public void PrepareCreatesCanonicalTargetVariables(string target, string expectedOsVariable, long expectedPointerWidth)
    {
        var compilation = Compilation.CreateForTest();

        Assert.True(compilation.Prepare(target));
        Assert.Equal(expectedOsVariable, compilation.TargetTriple.OsFamily);
        Assert.Equal(expectedPointerWidth, compilation.IrTarget.PointerWidth);
        Assert.True(compilation.Variables.TryGetValue(expectedOsVariable, out var osValue));
        Assert.True(osValue.Bool);
        Assert.True(compilation.Variables.TryGetValue("pointerWidth", out var pointerWidth));
        Assert.Equal(expectedPointerWidth, pointerWidth.I64);
    }

    // SPEC 22 (O-79 (a)): the IR target and the pointerWidth Condition are the target's; isize and usize keep the profile's width.
    [Theory]
    [InlineData("x86_64-pc-windows-msvc", 64)]
    [InlineData("i686-pc-windows-msvc", 32)]
    [InlineData("avr-unknown-unknown", 16)]
    public void PointerSizedIntegersKeepTheProfileWidth(string target, long width)
    {
        var compilation = Compilation.CreateForTest();
        Assert.Equal(64, BoundType.ISize.Width);
        Assert.True(compilation.Prepare(target));
        Assert.Equal(width, compilation.IrTarget.PointerWidth);
        Assert.Equal(width, compilation.Variables["pointerWidth"].I64);
        Assert.Equal(64, BoundType.USize.Width);
    }

    // An unknown architecture, and one without an LLVM backend (LLVM's pointer width 32 or 64), has no IR target (DECISIONS R5-M174).
    [Theory]
    [InlineData("unknown-unknown-unknown")]
    [InlineData("amdil-unknown-unknown")]
    [InlineData("hsail64-unknown-unknown")]
    public void PrepareRejectsAnUnsupportedTargetAndClearsTargetVariables(string target)
    {
        var compilation = Compilation.CreateForTest();
        Assert.True(compilation.Prepare("x86_64-pc-windows-msvc"));

        Assert.False(compilation.Prepare(target));
        Assert.Contains(TestDiagnostics.Of(compilation), x => x.Code == nameof(DiagnosticCode.UnsupportedTarget_Kd));
        Assert.Same(TargetTriple.Invalid, compilation.TargetTriple);
        Assert.Same(IrTarget.Invalid, compilation.IrTarget);
        Assert.False(compilation.Variables.TryGetValue("pointerWidth", out _));
    }

    // The target table (IMPL 21.4, 21.5, 21.6): three selectable rows, windows-x64, whose triple is the literal the [InlineData] rows
    // name, linux-x64 (the Tatara plan's B7b2a) and none-x64 (B9g: no staged row and no array of every row remain, so no member names
    // them; design §0.2's planned removal). Fail-before (B9g): Rows was two rows, and Staged held none-x64.
    // Each row's reserved supplies (IMPL 20.4): distinct names in Ordinal order (publication interleaves a program's
    // supplies by name), pairwise disjoint symbol sets (IMPL 21.2: an external symbol has one providing library), each one's kind among the
    // row's Kinds and the table's supply of its name (B7b5), and a C runtime, if any, that names one of them; each runtime declaration is
    // the table's for its symbol, provided by one of the row's supplies (G1 (i)). The Windows row: kernel32 providing every runtime
    // declaration (IMPL 21.4.7), both kinds, no C runtime and no argument extension; the Linux row (B7b2b): c, a static library providing
    // the C functions the layer stands for, its C runtime, the static kind (Q3a (a)), and System V's argument extension (IMPL 21.2). The
    // table's reserved external names (IMPL 21.2, every target; B7b5) are built once from pairwise disjoint parts: each row's float marker
    // and kimi_backend symbols (Windows': __chkstk alone, B7r5; none-x64's: compiler-rt's soft-float routines, the Tatara plan's B9e,
    // change N1), MemorySymbols and MainSymbol. Fail-before (B9e): the none row had no supply, and the literal no compiler-rt name.
    [Fact]
    public void TheReservedSuppliesAreOneTable()
    {
        Assert.Equal([TargetProfile.Windows, TargetProfile.Linux, TargetProfile.None], TargetProfile.Rows);
        Assert.DoesNotContain(typeof(TargetProfile).GetMembers(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic), static x => x.Name is "Staged" or "AllRows");
        Assert.Equal("x86_64-pc-windows-msvc", TargetProfile.Windows.Target);
        Assert.Equal("x86_64-unknown-linux-musl", TargetProfile.Linux.Target);
        Assert.Equal("x86_64-unknown-none", TargetProfile.None.Target);
        List<string> reserved = [];
        foreach (var row in TargetProfile.Rows)
        {
            reserved.AddRange(row.FloatMarker is { } marker ? [marker] : []);
            reserved.AddRange(row.Supply("kimi_backend")?.Symbols ?? []);
            var supplies = row.Supplies;
            for (var i = 1; i < supplies.Length; i++)
            {
                Assert.True(string.CompareOrdinal(supplies[i - 1].Name, supplies[i].Name) < 0);
            }

            for (var i = 0; i < supplies.Length; i++)
            {
                Assert.Same(supplies[i], row.Supply(supplies[i].Name));
                Assert.Contains(supplies[i].Kind, row.Kinds);
                Assert.Equal(supplies[i].Kind, TargetProfile.ReservedSupply(supplies[i].Name)!.Kind);
                for (var j = i + 1; j < supplies.Length; j++)
                {
                    Assert.Empty(supplies[i].Symbols.Intersect(supplies[j].Symbols, StringComparer.Ordinal));
                }
            }

            Assert.Null(row.Supply("codec"));
            Assert.Null(row.Supply(null));
            Assert.True(row.CRuntime is null || row.Supply(row.CRuntime) is not null);
            foreach (var (symbol, signature) in row.RuntimeDeclarations)
            {
                Assert.True(TargetProfile.TryGetRuntimeDeclaration(symbol, out var declared, out var provider));
                Assert.Equal((signature, Assert.Single(supplies, x => x.Symbols.Contains(symbol)).Name), (declared, provider));
            }
        }

        var windows = TargetProfile.Windows;
        Assert.Equal(["kernel32", "kimi_backend"], windows.Supplies.Select(static x => x.Name));
        Assert.Equal(["import", "static"], windows.Supplies.Select(static x => x.Kind));
        Assert.Equal(["import", "static"], windows.Kinds);
        var kernel32 = windows.Supply("kernel32")!.Symbols.ToHashSet(StringComparer.Ordinal);
        Assert.Subset(kernel32, windows.RuntimeDeclarations.Select(static x => x.Symbol).ToHashSet(StringComparer.Ordinal));
        Assert.Equal(["__chkstk"], windows.Supply("kimi_backend")!.Symbols);
        reserved.AddRange([.. TargetProfile.MemorySymbols, TargetProfile.MainSymbol]);
        Assert.Equal(reserved.Distinct(StringComparer.Ordinal), reserved);
        Assert.Equal(["_fltused", "__chkstk", .. NoneBackendSymbols, "memcmp", "memcpy", "memmove", "memset", "main"], TargetProfile.ReservedSymbols);
        Assert.Same(TargetProfile.ReservedSymbols, TargetProfile.ReservedSymbols);
        Assert.Null(windows.CRuntime);
        Assert.Null(windows.ArgumentExtension);
        var linux = TargetProfile.Linux;
        var c = Assert.Single(linux.Supplies);
        Assert.Equal(("c", "static"), (c.Name, c.Kind));
        Assert.Equal(["write", "_exit", "exit", "mmap", "munmap", "clock_gettime"], c.Symbols);
        Assert.Equal(["static"], linux.Kinds);
        Assert.Equal("c", linux.CRuntime);
        Assert.Equal("SystemV", linux.ArgumentExtension);

        // IMPL 21.6.1: none-x64's kernel code generation, no SSE or x87 state (soft float, whose System V ABI passes floating values in
        // integer registers), no red zone and no stack probe (SPEC 17.4.3's freestanding exception); one supply, kimi_backend, static,
        // compiler-rt 22.1.8's soft-float routines in its catalog's order (IMPL 21.6.7; the Tatara plan's B9e); no runtime declaration or
        // C runtime; the static kind.
        var none = TargetProfile.None;
        Assert.Equal("-mmx,-sse,-sse2,-sse3,-ssse3,-sse4.1,-sse4.2,-avx,-avx2,+soft-float", none.Features);
        Assert.Equal("kernel", none.CodeModel);
        Assert.Equal("static", none.RelocationModel);
        Assert.Equal(["noredzone"], none.FunctionAttributes);
        Assert.Equal(("NonePlatform.ll.in", "SystemV"), (none.Layer, none.ArgumentExtension));
        var backend = Assert.Single(none.Supplies);
        Assert.Equal(("kimi_backend", "static"), (backend.Name, backend.Kind));
        Assert.Equal(NoneBackendSymbols, backend.Symbols);
        Assert.Empty(none.RuntimeDeclarations);
        Assert.Null(none.CRuntime);
        Assert.Equal(["static"], none.Kinds);

        // SPEC 22.4.5: the program supplies the platform layer's routines on the freestanding row alone.
        Assert.Equal([false, false, true], TargetProfile.Rows.Select(static x => x.ProgramPlatform));

        // The Tatara plan's decision 31 (B9f): the compiler's own linker script on the freestanding row alone; the hosted rows link in the
        // linker's layout.
        Assert.Equal([null, null, "kernel.ld"], TargetProfile.Rows.Select(static x => x.LinkerScript));

        // The Tatara plan's B7b7: each row's input extension and executable format (IMPL 20.3.2, 20.4); a subsystem, a float marker and
        // a load configuration symbol exactly on the COFF rows (LinkRoots: windows-x64's _load_config_used).
        Assert.Equal([(".lib", "COFF"), (".a", "ELF"), (".a", "ELF")], TargetProfile.Rows.Select(static x => (x.InputExtension, x.Format)));
        Assert.All(TargetProfile.Rows, static x => Assert.Equal((x.Format == "COFF", x.Format == "COFF", x.Format == "COFF"), (x.Subsystem is not null, x.FloatMarker is not null, x.LoadConfigSymbol is not null)));
        Assert.Equal("_load_config_used", TargetProfile.Windows.LoadConfigSymbol);

        // The Tatara plan's B7b8: the host that runs each row's executables, as HostName spells one; none for linux-x64 until B8, and none
        // for none-x64.
        Assert.Equal(["windows x64", null, null], TargetProfile.Rows.Select(static x => x.ExecutionHost));
    }

    // Each row's data layout is the one the pinned clang writes for its triple (`clang --target=<triple> -S -emit-llvm` of an empty C
    // file, the Tatara plan's B7b2a probe; design §5 row 42), and each row's module emitted by Tatara for its triple checks that the pair
    // agrees (TataraExecutorTest).
    [Theory]
    [InlineData("x86_64-pc-windows-msvc", "e-m:w-p270:32:32-p271:32:32-p272:64:64-i64:64-i128:128-f80:128-n8:16:32:64-S128")]
    [InlineData("x86_64-unknown-linux-musl", "e-m:e-p270:32:32-p271:32:32-p272:64:64-i64:64-i128:128-f80:128-n8:16:32:64-S128")]
    [InlineData("x86_64-unknown-none", "e-m:e-p270:32:32-p271:32:32-p272:64:64-i64:64-i128:128-f80:128-n8:16:32:64-S128")]
    public void TheProfileLayoutIsItsTriplesLayout(string triple, string layout)
        => Assert.Equal(layout, Assert.Single(TargetProfile.Rows, row => row.Target == triple).DataLayout);

    // The Tatara plan's B7b2a (N2): each row's Layer names exactly one of the Backend's RuntimeLayer records, which Core cannot name, so a
    // row's frame always has its layer.
    [Fact]
    public void EveryRowNamesOneRuntimeLayer()
        => Assert.All(TargetProfile.Rows, static row => Assert.Single(RuntimeAbi.Layers, x => x.Template == row.Layer));

    // The Tatara plan's B7b2a: Find gives each row for its triple, and none for a triple without a row.
    [Fact]
    public void FindGivesTheRowOfATriple()
    {
        Assert.All(TargetProfile.Rows, static row => Assert.Same(row, TargetProfile.Find(row.Target)));
        Assert.Null(TargetProfile.Find(string.Empty));
        Assert.Null(TargetProfile.Find("x86_64-unknown-linux-gnu"));
    }

    // The Tatara plan's B7b8 (C-575 N6): the host's row is the one row whose ExecutionHost is this process's HostName (SingleOrDefault
    // throws on two), or null when there is none; on a Windows x64 process it is windows-x64's.
    [Fact]
    public void HostIsTheRowThatRunsOnThisHost()
    {
        Assert.Same(TargetProfile.Rows.SingleOrDefault(static x => x.ExecutionHost == TargetProfile.HostName), TargetProfile.Host);
        if (OperatingSystem.IsWindows() && RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.X64)
        {
            Assert.Equal("windows x64", TargetProfile.HostName);
            Assert.Same(TargetProfile.Windows, TargetProfile.Host);
        }
    }
}
