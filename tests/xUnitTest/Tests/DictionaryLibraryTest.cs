// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class DictionaryLibraryTest
{
    [Fact]
    public void RemovalResultCompilesFromOrdinarySource()
    {
        var c = MinimalEmissionTest.Analyze("var entries = [1: 42]\nmatch entries.remove(1)\n    .Some((let key, let value)) => require key == 1 and value == 42 else => $abort(\"removed\")\n    .None => $abort(\"missing\")\nrequire entries.length == 0 else => $abort(\"length\")");
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var function = c.Library.GetSymbol(KimiDeclarationId.DictionaryRemove);
        Assert.NotNull(function);
        Assert.Equal(CompilerFunctionKind.None, function.CompilerFunction);
        Assert.Contains(c.Ownership.Bodies, body => ReferenceEquals(body.Function, function.Declaration));
        var ir = CompilationTestHelper.WriteIr(c);
        Assert.DoesNotContain("__kimi_dictionary_remove", ir, StringComparison.Ordinal);
        ScalarEmissionTest.WriteFixture("DictionaryLibrarySourceRemove", ir, string.Empty);
    }

    [Fact]
    public void ClearingCompilesFromOrdinarySource()
    {
        var c = MinimalEmissionTest.Analyze("var entries = [1: 42]\nentries.clear()\nrequire entries.length == 0 and entries.capacity >= 1 else => $abort(\"clear\")");
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var function = c.Library.GetSymbol(KimiDeclarationId.DictionaryClear);
        Assert.NotNull(function);
        Assert.Equal(CompilerFunctionKind.None, function.CompilerFunction);
        Assert.Contains(c.Ownership.Bodies, body => ReferenceEquals(body.Function, function.Declaration));
        ScalarEmissionTest.WriteFixture("DictionaryLibrarySourceClear", CompilationTestHelper.WriteIr(c), string.Empty);
    }

    [Fact]
    public void LookupCompilesFromOrdinarySource()
    {
        var c = MinimalEmissionTest.Analyze("var entries = [1: 42]\nmatch entries.tryGet(1)\n    .Some(let value) => require value == 42 else => $abort(\"value\")\n    .None => $abort(\"missing\")");
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var function = c.Library.GetSymbol(KimiDeclarationId.DictionaryTryGet);
        Assert.NotNull(function);
        Assert.Equal(CompilerFunctionKind.None, function.CompilerFunction);
        Assert.Contains(c.Ownership.Bodies, body => ReferenceEquals(body.Function, function.Declaration));
        var ir = CompilationTestHelper.WriteIr(c);
        Assert.DoesNotContain("__kimi_dictionary_tryget", ir, StringComparison.Ordinal);
        ScalarEmissionTest.WriteFixture("DictionaryLibrarySourceLookup", ir, string.Empty);
    }

    [Fact]
    public void CapacityDecisionsCompileFromOrdinarySource()
    {
        var c = MinimalEmissionTest.Analyze("var entries: Dictionary<i32, i32> = [:]\nentries.reserve(8)\n_ = entries.tryInsert(1, 2)");
        Assert.True(c.Library.IsValid, string.Join('\n', TestDiagnostics.Of(c.Library.Kotonoha)));
        var ir = CompilationTestHelper.WriteIr(c);
        Assert.Contains(c.Ownership.Bodies, static body => body.Function.Name == "grow" && body.Function.CodeContext.SourceDocument?.Path.EndsWith("DictionaryStorage.kimi", StringComparison.Ordinal) == true);
        var wrapper = System.Text.RegularExpressions.Regex.Match(ir, @"(?ms)^define[^\n]*@__kimi_dictionary_reserve\([^\n]*\n.*?^\}").Value;
        Assert.NotEmpty(wrapper);
        Assert.DoesNotContain("__kimi_array_grow", wrapper, StringComparison.Ordinal);
    }

    [Fact]
    public void UserHelperNamesDoNotReplaceTheLibraryAlgorithms()
    {
        const string Source = """
            group DictionaryStorage
                public func initialize() => $abort("user initialize")
                public func find() => $abort("user find")
                public func unlink() => $abort("user unlink")
                public func compact() => $abort("user compact")
            var entries: Dictionary<i32, i32> = [:]
            entries.reserve(8)
            _ = entries.tryInsert(1, 10)
            _ = entries.tryInsert(2, 20)
            _ = entries.remove(1)
            entries.shrinkToFit()
            require entries[2] == 20 else => $abort("stored value")
            entries.clear()
            require entries.length == 0 else => $abort("clear")
            """;
        ScalarEmissionTest.EmitFixture("DictionaryLibraryIdentity", Source, string.Empty);
    }

    [Fact]
    public void ProgramsWithoutDictionariesDoNotCompileStorageAlgorithms()
    {
        var c = MinimalEmissionTest.Analyze("Console.writeLine(\"plain\")");
        var outputIr = CompilationTestHelper.WriteIr(c);
        Assert.DoesNotContain(c.Ownership.Bodies, body => body.Function.CodeContext.SourceDocument?.Path.EndsWith("DictionaryStorage.kimi", StringComparison.Ordinal) == true);
        Assert.DoesNotContain("__kimi_dictionary_", outputIr);
    }

    [Fact]
    public void PrivateHandleUsesTheExpectedPlatformLayout()
    {
        var c = MinimalEmissionTest.Analyze("var entries: Dictionary<i32, i32> = [:]");
        var pointer = c.Library.DictionaryUnlink.Parameters[0].Type.BoundType!;
        var layout = Assert.IsType<AggregateLayout>(new AggregateLayoutPool().Get(pointer.Components[0]));
        Assert.True(layout.CLayout);
        Assert.Equal(56, layout.Value.Layout.Size);
        Assert.Equal(8, layout.Value.Layout.Alignment);
        Assert.Equal(new[] { 0, 8, 16, 24, 32, 40, 48 }, layout.Value.Layout.FieldOffsets.ToArray());
    }

    [Fact]
    public void PointerReturningCallbacksUseOrdinaryCallAbi()
    {
        const string Source = """
            func invoke(f: (isize) -> unsafe/u8) -> unsafe/u8 => f(0)
            unsafe
                let address: usize = 4096
                let expected = address@unsafe/u8
                let concrete = func [address] (offset: isize) -> unsafe/u8
                    unsafe => return address@unsafe/u8
                let erased: (isize) -> unsafe/u8 = concrete
                require concrete(0) == expected and erased(0) == expected else => $abort("pointer result")
                require invoke(erased@move) == expected else => $abort("forwarded result")
            """;
        ScalarEmissionTest.EmitFixture("DictionaryPointerCallbacks", Source, string.Empty);
    }

    [Fact]
    public void RemovalCompilesTheOrdinaryLibrarySource()
    {
        var c = MinimalEmissionTest.Analyze("var entries: Dictionary<i32, i32> = [:]\n_ = entries.tryInsert(1, 2)\n_ = entries.remove(1)");
        var outputIr = CompilationTestHelper.WriteIr(c);
        Assert.Contains(c.Ownership.Bodies, body => ReferenceEquals(body.Function, c.Library.DictionaryUnlink));
        Assert.Contains(c.Ownership.Bodies, body => ReferenceEquals(body.Function, c.Library.DictionaryAppendSlot));
        Assert.Contains(c.Ownership.Bodies, body => ReferenceEquals(body.Function, c.Library.DictionaryInitialize));
        Assert.Contains(c.Ownership.Bodies, body => ReferenceEquals(body.Function, c.Library.DictionaryClearLinks));
        Assert.Contains(c.Ownership.Bodies, body => ReferenceEquals(body.Function, c.Library.DictionaryFind));
        Assert.Contains(c.Ownership.Bodies, body => ReferenceEquals(body.Function, c.Library.DictionaryClear));
        Assert.Contains(c.Ownership.Bodies, body => ReferenceEquals(body.Function, c.Library.DictionaryCompact));
        Assert.Contains(c.Ownership.Bodies, body => ReferenceEquals(body.Function, c.Library.DictionaryShrink));
        Assert.Equal(CompilerFunctionKind.None, c.Library.DictionaryUnlink.BoundSymbol!.CompilerFunction);
        Assert.Contains("DictionaryStorage.kimi", c.Library.DictionaryUnlink.CodeContext.SourceDocument!.Path);
        Assert.DoesNotContain("%left_index = sub", outputIr);
        ScalarEmissionTest.WriteFixture("DictionaryLibraryRemoval", outputIr, string.Empty);
    }

    [Theory]
    [InlineData("unsafe => Kimi.DictionaryStorage.unlink(null, 24, 1)")]
    [InlineData("unsafe => Kimi.DictionaryStorage.initialize(null)")]
    [InlineData("unsafe => Kimi.DictionaryStorage.clearLinks(null)")]
    public void PrivateStorageFunctionsAreNotAPublicUnsafeAPI(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.InaccessibleBinding_Kd);
    }
}
