// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class CoreCatalogTest
{
    [Fact]
    public void ConcurrentCompilationsKeepSeparateLibraryState()
    {
        var libraries = new KimiLibrary[16];
        Parallel.For(0, libraries.Length, i =>
        {
            var c = Compilation.CreateForTest();
            libraries[i] = c.Library;
            Assert.True(c.Bind().IsComplete);
        });
        for (var i = 1; i < libraries.Length; i++)
        {
            Assert.NotSame(libraries[0].Option, libraries[i].Option);
            Assert.NotSame(libraries[0].Option.Declaration, libraries[i].Option.Declaration);
        }
    }

    [Fact]
    public void EmbeddedSourcesKeepFileLocationsAndShareOnlyText()
    {
        var first = Compilation.CreateForTest();
        var second = Compilation.CreateForTest();
        foreach (var id in new[] { KimiDeclarationId.Copy, KimiDeclarationId.Option, KimiDeclarationId.Iterator, KimiDeclarationId.Slice, KimiDeclarationId.MakeObj, KimiDeclarationId.WriteLine })
        {
            var a = first.Library.GetSymbol(id)!;
            var b = second.Library.GetSymbol(id)!;
            var source = a.Declaration.CodeContext.SourceDocument!;
            Assert.StartsWith("compiler://Kimi/" + Compilation.CurrentLanguageVersion + "/", source.Path);
            Assert.EndsWith(".kimi", source.Path);
            Assert.NotSame(a, b);
            Assert.NotSame(a.Declaration, b.Declaration);
            Assert.NotSame(source, b.Declaration.CodeContext.SourceDocument);
            Assert.Same(source.SourceText, b.Declaration.CodeContext.SourceDocument!.SourceText);
        }

        Assert.True(first.Bind().IsComplete);
        Assert.True(second.Bind().IsComplete);
    }

    [Fact]
    public void DeclarationOrderDoesNotChangeRecognizedIdentities()
    {
        var c = Compilation.CreateForTest();
        var makeObj = c.Library.MakeObj;
        Assert.IsType<List<DeclarationContainerKoto>>(c.Library.Kotonoha.RootKoto.NestedContainers).Reverse();
        Assert.IsType<List<Koto>>(c.Library.Intrinsics.Members).Reverse();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "let value = Kimi.Intrinsics.makeObj(42)");
        Assert.True(c.Bind().IsComplete, string.Join('\n', c.Binding.Issues));
        Assert.Same(makeObj, c.Library.MakeObj);
        Assert.Equal(KimiDeclarationState.Validated, c.Library.GetDeclarationState(KimiDeclarationId.MakeObj));
    }

    [Fact]
    public void OrdinaryLibraryHelpersUseTheNormalBindingPipeline()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare("x86_64-pc-windows-msvc"));
        c.Library.Kotonoha.CreateCodeContext().Parse(
            c.Library.Kotonoha.RootKoto,
            "public struct Extra\n    public func read(self: ref/Self) -> i32 => 42\npublic group Helpers\n    public func helper() -> i32 => Extra.init().read()");
        c.Library.Kotonoha.CreateCodeContext().Parse(
            (StructKoto)c.Library.Slice.Declaration,
            "public func extra(self: Self) -> isize => self.length");
        c.Library.Kotonoha.CreateCodeContext().Parse(
            (EnumKoto)c.Library.Option.Declaration,
            "public func extra(self: ref/Self) -> i32 => 42");
        c.Kotonoha.CreateCodeContext().Parse(
            c.Kotonoha.RootKoto,
            "let value = Kimi.Helpers.helper()\nlet a: [1 of i32] = [42]\nlet count = a[..].extra()");
        Assert.True(c.Bind().IsComplete, string.Join('\n', c.Binding.Issues));
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(c.Ownership.Analyze().IsVerified, string.Join('\n', c.Ownership.Issues));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
    }

    [Fact]
    public void OwnershipAvailabilityIsTrackedPerDeclaration()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        Assert.False(c.Library.IsCompleteOwnershipFamily);
        Assert.Equal(KimiDeclarationState.Validated, c.Library.GetDeclarationState(KimiDeclarationId.MakeObj));
        Assert.Equal(KimiDeclarationState.Missing, c.Library.GetDeclarationState(KimiDeclarationId.MakeRc));
        Assert.Equal(KimiDeclarationState.Missing, c.Library.GetDeclarationState(KimiDeclarationId.Weak));
        Assert.Null(c.Library.GetSymbol(KimiDeclarationId.ObjectOwnership));
        Assert.DoesNotContain(c.Library.Declarations.ToArray(), x => x.Id == KimiDeclarationId.ObjectOwnership);
    }

    [Fact]
    public void InvalidSignatureDiagnosticPointsToItsLibrarySource()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        var factory = Assert.IsType<FunctionKoto>(c.Library.MakeObj.Declaration);
        factory.Parameters[0].Type = ((FunctionKoto)c.Library.Replace.Declaration).Parameters[0].Type;
        Assert.False(c.Bind().IsComplete);
        var issue = Assert.Single(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidKimiLibrary_Kd);
        Assert.Same(factory, issue.Node);
        Assert.EndsWith("/Intrinsics.kimi", issue.Node.CodeContext.SourceDocument!.Path);
    }

    [Fact]
    public void SliceHelpersCannotChangeCompilerManagedStorage()
    {
        var c = Compilation.CreateForTest();
        c.Library.Kotonoha.CreateCodeContext().Parse((StructKoto)c.Library.Slice.Declaration, "let extra: i32 = 0");
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(KimiDeclarationState.Invalid, c.Library.GetDeclarationState(KimiDeclarationId.Slice));
    }

    [Fact]
    public void DuplicateIntrinsicAndRemovedDeclarationAreRejected()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        c.Library.Intrinsics.AddLast(c.Library.MakeObj.Declaration);
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(KimiDeclarationState.Invalid, c.Library.GetDeclarationState(KimiDeclarationId.MakeObj));

        c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        Assert.IsType<List<DeclarationContainerKoto>>(c.Library.Kotonoha.RootKoto.NestedContainers).Remove((DeclarationContainerKoto)c.Library.Option.Declaration);
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(KimiDeclarationState.Invalid, c.Library.GetDeclarationState(KimiDeclarationId.Option));
    }

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
        Assert.Equal(73, c.Library.ValidatedDeclarationCount);
        Assert.Equal(83, c.Library.Declarations.Length);
        for (var i = 0; i < c.Library.Declarations.Length; i++)
        {
            var entry = c.Library.Declarations[i];
            if ((int)entry.Id < 6 || entry.Id >= KimiDeclarationId.Utf8Format || entry.Id is KimiDeclarationId.Equatable or KimiDeclarationId.Comparable or KimiDeclarationId.Index or KimiDeclarationId.Sealed or KimiDeclarationId.Replace or KimiDeclarationId.Exchange or KimiDeclarationId.Swap or KimiDeclarationId.MakeObj || entry.Id is KimiDeclarationId.Iterator or KimiDeclarationId.Iterable or KimiDeclarationId.Slice or KimiDeclarationId.Array or KimiDeclarationId.Dictionary or KimiDeclarationId.TestTempDirectory or (>= KimiDeclarationId.ArrayReserve and <= KimiDeclarationId.ArrayRemoveIndex))
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
    [InlineData(KimiDeclarationId.ObjectPayload)]
    public void RebindRejectsChangedIntrinsicContracts(KimiDeclarationId id)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        var symbol = c.Library.GetSymbol(id)!;
        c.Library.Kotonoha.CreateCodeContext().Parse((ContractKoto)symbol.Declaration, "func extra() -> i32");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidKimiLibrary_Kd);
        Assert.Equal(KimiDeclarationState.Invalid, c.Library.GetDeclarationState(id));
        Assert.Same(symbol, c.Library.GetSymbol(id));
        Assert.False(c.Library.IsCompleteLibrary);
    }

    [Fact]
    public void MissingCatalogEntriesDoNotCreateLookupCandidates()
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "func f(x: ::Kimi.Weak<i32>) => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Null(c.Library.GetSymbol(KimiDeclarationId.Weak));
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
        var source = prefix + "var x: i32 = 1\nvar y: i32 = 2\n" + qualifier + "replace(x@uniq, with: 3)\n" +
            qualifier + "exchange(x@uniq, with: 4)\n" + qualifier + "swap(x@uniq, y@uniq)\n" + qualifier + "makeObj(5)";
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
