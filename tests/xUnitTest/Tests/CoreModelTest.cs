// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
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
    [InlineData(WindowsProfile.Target, 64)]
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
}
