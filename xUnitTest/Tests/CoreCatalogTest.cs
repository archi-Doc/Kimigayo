// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class CoreCatalogTest
{
    [Fact]
    public void CatalogDistinguishesAvailableDeclarationsFromMissingLibraryFeatures()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        Assert.False(c.Library.IsCompleteLibrary);
        Assert.Equal(10, c.Library.ValidatedDeclarationCount);
        Assert.Equal(22, c.Library.Declarations.Length);
        for (var i = 0; i < c.Library.Declarations.Length; i++)
        {
            var entry = c.Library.Declarations[i];
            Assert.Equal(i, (int)entry.Id);
            if ((int)entry.Id < 6 || entry.Id >= KimiDeclarationId.Sealed)
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
}
