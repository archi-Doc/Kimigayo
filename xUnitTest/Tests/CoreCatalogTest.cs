// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class CoreCatalogTest
{
    [Fact]
    public void CompilerImplementedSignaturesHaveNoSourceBodyErrors()
    {
        var c = Compilation.CreateForTest();
        Assert.False(c.Library.Kotonoha.DiagnosticCollection.HasErrors);
        Assert.True(c.Bind().IsComplete);
        foreach (var symbol in new[] { c.Library.Replace, c.Library.Exchange, c.Library.Swap })
        {
            var declaration = Assert.IsType<FunctionKoto>(symbol.Declaration);
            Assert.Null(declaration.Body);
            Assert.Null(declaration.ExpressionBody);
        }

        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "func missing() -> ()");
        Assert.Contains(c.Kotonoha.DiagnosticCollection.GetArray(), x => x.Entry.Name == nameof(DiagnosticCode.EmptyExecutableBlock_Kd));
    }

    [Fact]
    public void CatalogDistinguishesAvailableDeclarationsFromMissingLibraryFeatures()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        Assert.False(c.Library.IsCompleteLibrary);
        Assert.Equal(12, c.Library.ValidatedDeclarationCount);
        Assert.Equal(22, c.Library.Declarations.Length);
        for (var i = 0; i < c.Library.Declarations.Length; i++)
        {
            var entry = c.Library.Declarations[i];
            Assert.Equal(i, (int)entry.Id);
            if ((int)entry.Id < 6 || entry.Id >= KimiDeclarationId.Sealed || entry.Id is KimiDeclarationId.Iterator or KimiDeclarationId.Slice)
            {
                Assert.Equal(KimiDeclarationState.Validated, entry.State);
                Assert.Same(entry.Symbol, c.Library.GetSymbol(entry.Id));
            }
            else
            {
                Assert.Equal(KimiDeclarationState.Missing, entry.State);
                Assert.Null(entry.Symbol);
            }
        }
    }

    [Theory]
    [InlineData(KimiDeclarationId.Copy)]
    [InlineData(KimiDeclarationId.Owned)]
    [InlineData(KimiDeclarationId.Callable)]
    [InlineData(KimiDeclarationId.Sealed)]
    public void RebindRejectsChangedIntrinsicContracts(KimiDeclarationId id)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        var symbol = c.Library.GetSymbol(id)!;
        c.Library.Kotonoha.CreateCodeContext().Parse((ContractKoto)symbol.Declaration, "func extra() -> i32");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidKimiLibrary_Kd);
        Assert.Equal(KimiDeclarationState.Invalid, c.Library.Declarations[(int)id].State);
        Assert.Same(symbol, c.Library.GetSymbol(id));
        Assert.False(c.Library.IsCompleteLibrary);
    }

    [Fact]
    public void MissingCatalogEntriesDoNotCreateLookupCandidates()
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "func f(x: ::Kimi.Array<i32>) => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Null(c.Library.GetSymbol(KimiDeclarationId.Array));
    }

    [Fact]
    public void WarmCatalogValidationAndBindingReuseStorage()
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "Console.writeLine(\"x\")");
        for (var i = 0; i < 8; i++)
        {
            Assert.True(c.Bind().IsComplete);
            Assert.False(c.Library.IsCompleteLibrary);
        }

        var copy = c.Library.Copy;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Bind();
            _ = c.Library.IsCompleteLibrary;
        }));
        Assert.Same(copy, c.Library.Copy);
    }

    [Theory]
    [InlineData("", "Kimi.Intrinsics.")]
    [InlineData("", "::Kimi.Intrinsics.")]
    [InlineData("", "Intrinsics.")]
    [InlineData("alias Memory => Kimi.Intrinsics\n", "Memory.")]
    [InlineData("alias Kimi.Intrinsics\n", "")]
    [InlineData("let Intrinsics = 0\n", "::Kimi.Intrinsics.")]
    public void OwnershipOperationsKeepIdentityThroughQualifiedAndAliasLookup(string prefix, string qualifier)
    {
        var c = Compilation.CreateForTest();
        var source = prefix + "var x: i32 = 1\nvar y: i32 = 2\n" + qualifier + "replace(x, with: 3)\n" +
            qualifier + "exchange(x, with: 4)\n" + qualifier + "swap(x, y)\n" + qualifier + "makeObj(5)";
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        var expected = new[] { c.Library.Replace, c.Library.Exchange, c.Library.Swap, c.Library.MakeObj };
        for (var iteration = 0; iteration < 3; iteration++)
        {
            Assert.True(c.Bind().IsComplete, string.Join('\n', c.Binding.Issues));
            var calls = c.Kotonoha.GeneratedFunction!.Body!.Items.OfType<InvocationKoto>().ToArray();
            Assert.Equal(expected.Length, calls.Length);
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.Same(expected[i], calls[i].BoundCall!.Target);
                Assert.Equal("Intrinsics", Assert.IsType<GroupKoto>(expected[i].Declaration.Parent).Name);
            }
        }
    }

    [Theory]
    [InlineData("Kimi.replace(x, with: 2)")]
    [InlineData("Kimi.exchange(x, with: 2)")]
    [InlineData("Kimi.swap(x, y)")]
    [InlineData("Kimi.makeObj(1)")]
    [InlineData("replace(x, with: 2)")]
    [InlineData("exchange(x, with: 2)")]
    [InlineData("swap(x, y)")]
    [InlineData("makeObj(1)")]
    public void DefaultAliasDoesNotExposeOwnershipFunctionsAtTheRoot(string expression)
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "var x: i32 = 1\nvar y: i32 = 2\n" + expression);
        Assert.False(c.Bind().IsComplete);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidKimiLibrary_Kd);
    }

    [Fact]
    public void RebindRejectsChangedIntrinsicsContainer()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        var group = Assert.IsType<GroupKoto>(c.Library.Replace.Declaration.Parent);
        c.Library.Kotonoha.CreateCodeContext().Parse(group, "public func extra() => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidKimiLibrary_Kd);
    }
}
