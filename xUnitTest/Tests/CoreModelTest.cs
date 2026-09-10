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
    [InlineData("x86_64-pc-win32-msvc", OsType.Win32, "windows", 64)]
    [InlineData("x86_64-unknown-linux-gnu", OsType.Linux, "linux", 64)]
    [InlineData("aarch64-apple-macosx", OsType.MacOSX, "macos", 64)]
    public void PrepareCreatesCanonicalTargetVariables(
        string target,
        OsType expectedOs,
        string expectedOsVariable,
        long expectedPointerWidth)
    {
        var compilation = Compilation.CreateForTest();

        Assert.True(compilation.Prepare(target));
        Assert.Equal(expectedOs, compilation.TargetTriple.Os);
        Assert.Equal(expectedPointerWidth, compilation.PointerWidth);
        Assert.True(compilation.Variables.TryGetValue(expectedOsVariable, out var osValue));
        Assert.True(osValue.Bool);
        Assert.True(compilation.Variables.TryGetValue("pointerWidth", out var pointerWidth));
        Assert.Equal(expectedPointerWidth, pointerWidth.I64);
    }

    [Fact]
    public void PrepareRejectsAnUnsupportedTargetAndClearsTargetVariables()
    {
        var compilation = Compilation.CreateForTest();
        Assert.True(compilation.Prepare("x86_64-pc-windows-msvc"));

        Assert.False(compilation.Prepare("unknown-unknown-unknown"));
        Assert.Same(TargetTriple.Invalid, compilation.TargetTriple);
        Assert.Same(IrTarget.Invalid, compilation.IrTarget);
        Assert.False(compilation.Variables.TryGetValue("pointerWidth", out _));
    }
}
