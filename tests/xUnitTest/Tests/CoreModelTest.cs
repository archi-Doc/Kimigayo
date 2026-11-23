// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Target;
using Xunit;

namespace XunitTest;

public class CoreModelTest
{
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

    // The target table (IMPL 21.4): one row today, windows-x64, whose triple is the literal the [InlineData] rows name. Each row's reserved
    // supplies (IMPL 20.4): distinct names in Ordinal order (publication interleaves a program's supplies by name), pairwise disjoint
    // symbol sets (IMPL 21.2: an external symbol has one providing library). The Windows row: kernel32 providing every runtime
    // declaration (IMPL 21.4.7), and IMPL 21.2's reserved external names.
    [Fact]
    public void TheReservedSuppliesAreOneTable()
    {
        Assert.Same(TargetProfile.Windows, Assert.Single(TargetProfile.Rows));
        Assert.Equal("x86_64-pc-windows-msvc", TargetProfile.Windows.Target);
        foreach (var row in TargetProfile.Rows)
        {
            var supplies = row.Supplies;
            for (var i = 1; i < supplies.Length; i++)
            {
                Assert.True(string.CompareOrdinal(supplies[i - 1].Name, supplies[i].Name) < 0);
            }

            for (var i = 0; i < supplies.Length; i++)
            {
                Assert.Same(supplies[i], row.Supply(supplies[i].Name));
                for (var j = i + 1; j < supplies.Length; j++)
                {
                    Assert.Empty(supplies[i].Symbols.Intersect(supplies[j].Symbols, StringComparer.Ordinal));
                }
            }

            Assert.Null(row.Supply("codec"));
            Assert.Null(row.Supply(null));
        }

        var windows = TargetProfile.Windows;
        Assert.Equal(["kernel32", "kimi_backend"], windows.Supplies.Select(static x => x.Name));
        Assert.Equal(["import", "static"], windows.Supplies.Select(static x => x.Kind));
        var kernel32 = windows.Supply("kernel32")!.Symbols.ToHashSet(StringComparer.Ordinal);
        Assert.Subset(kernel32, windows.RuntimeDeclarations.Select(static x => x.Symbol).ToHashSet(StringComparer.Ordinal));
        Assert.Equal(["_fltused", "__chkstk", "memcmp", "memcpy", "memmove", "memset"], windows.ReservedSymbols);
    }

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
}
