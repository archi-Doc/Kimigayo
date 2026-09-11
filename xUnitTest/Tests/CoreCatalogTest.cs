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
        Assert.False(c.Core.IsCompleteLibrary);
        Assert.Equal(4, c.Core.ValidatedDeclarationCount);
        Assert.Equal(18, c.Core.Declarations.Length);
        for (var i = 0; i < c.Core.Declarations.Length; i++)
        {
            var entry = c.Core.Declarations[i];
            Assert.Equal(i, (int)entry.Id);
            if ((int)entry.Id < 4)
            {
                Assert.Equal(CoreDeclarationState.Validated, entry.State);
                Assert.Same(entry.Symbol, c.Core.GetSymbol(entry.Id));
            }
            else
            {
                Assert.Equal(CoreDeclarationState.Missing, entry.State);
                Assert.Null(entry.Symbol);
            }
        }
    }

    [Theory]
    [InlineData(CoreDeclarationId.Copy)]
    [InlineData(CoreDeclarationId.Owned)]
    [InlineData(CoreDeclarationId.Callable)]
    public void RebindRejectsChangedIntrinsicContracts(CoreDeclarationId id)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        var symbol = c.Core.GetSymbol(id)!;
        c.Core.Kotonoha.CreateCodeContext().Parse((ContractKoto)symbol.Declaration, "func extra() -> i32");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidCoreIntrinsics_Kd);
        Assert.Equal(CoreDeclarationState.Invalid, c.Core.Declarations[(int)id].State);
        Assert.Same(symbol, c.Core.GetSymbol(id));
        Assert.False(c.Core.IsCompleteLibrary);
    }

    [Fact]
    public void MissingCatalogEntriesDoNotCreateLookupCandidates()
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "func f(x: ::Core.Option<i32>) => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Null(c.Core.GetSymbol(CoreDeclarationId.Option));
    }

    [Fact]
    public void WarmCatalogValidationAndBindingReuseStorage()
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "writeLine(\"x\")");
        for (var i = 0; i < 8; i++)
        {
            Assert.True(c.Bind().IsComplete);
            Assert.False(c.Core.IsCompleteLibrary);
        }

        var copy = c.Core.Copy;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 8; i++)
        {
            c.Bind();
            _ = c.Core.IsCompleteLibrary;
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Same(copy, c.Core.Copy);
    }
}
