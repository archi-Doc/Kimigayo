// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class DictionaryLibraryTest
{
    [Theory]
    [InlineData("i32")]
    [InlineData("string")]
    public void BorrowedLookupDoesNotRegisterDestructionHelpers(string valueType)
    {
        var c = MinimalEmissionTest.Analyze("func lookup(values: ref/Dictionary<i32, " + valueType + ">) -> ref/" + valueType + " during values => values[1]@ref\nlet boot = 0");
        Assert.True(c.Emission.TryPrepare(out var module, out var failure), MinimalEmissionTest.Describe(c, failure));
        Assert.Empty(module.DictionaryHelpers);
        Assert.False(module.NeedsDictionaryRuntime);
    }

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

    [Theory]
    [InlineData(KimiDeclarationId.DictionaryTryInsert, "TryInsert", "i32", "match entries.tryInsert(1, 2)\n    .Ok(_) => require entries[1] == 2 else => $abort(\"stored\")\n    .Err(_) => $abort(\"rejected\")\nmatch entries.tryInsert(1, 3)\n    .Ok(_) => $abort(\"duplicate\")\n    .Err((let key, let value)) => require key == 1 and value == 3 and entries[1] == 2 and entries.length == 1 else => $abort(\"returned\")")]
    [InlineData(KimiDeclarationId.DictionaryInsertOrReplace, "InsertOrReplace", "i32", "match entries.insertOrReplace(1, 2)\n    .Some(_) => $abort(\"replaced\")\n    .None => require entries[1] == 2 else => $abort(\"stored\")\nmatch entries.insertOrReplace(1, 3)\n    .Some(let old) => require old == 2 and entries[1] == 3 and entries.length == 1 else => $abort(\"old\")\n    .None => $abort(\"appended\")")]
    [InlineData(KimiDeclarationId.DictionaryInsertOrReplace, "InsertOrReplaceString", "string", "match entries.insertOrReplace(1, \"a\")\n    .Some(_) => $abort(\"replaced\")\n    .None => require entries.length == 1 else => $abort(\"stored\")\nmatch entries.insertOrReplace(1, \"b\")\n    .Some(let old) => require old == \"a\" and entries[1] == \"b\" and entries.length == 1 else => $abort(\"old\")\n    .None => $abort(\"appended\")")]
    public void InsertionCompilesFromOrdinarySource(KimiDeclarationId id, string name, string valueType, string source)
    {
        var c = MinimalEmissionTest.Analyze("var entries: Dictionary<i32, " + valueType + "> = [:]\n" + source);
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var function = c.Library.GetSymbol(id);
        Assert.NotNull(function);
        Assert.Equal(CompilerFunctionKind.None, function.CompilerFunction);
        Assert.Contains(c.Ownership.Bodies, body => ReferenceEquals(body.Function, function.Declaration));
        var ir = CompilationTestHelper.WriteIr(c);
        Assert.DoesNotContain("__kimi_dictionary_tryinsert", ir, StringComparison.Ordinal);
        Assert.DoesNotContain("__kimi_dictionary_insertorreplace", ir, StringComparison.Ordinal);
        Assert.Contains("__kimi_dictionary_place", ir, StringComparison.Ordinal);
        ScalarEmissionTest.WriteFixture("DictionaryLibrarySource" + name, ir, string.Empty);
    }

    [Theory]
    [InlineData(KimiDeclarationId.DictionaryReserve, "Reserve", "entries.reserve(8)\nrequire entries.capacity >= 9 and entries.length == 1 and entries[1] == 2 else => $abort(\"reserve\")")]
    [InlineData(KimiDeclarationId.DictionaryShrinkToFit, "Shrink", "entries.reserve(8)\nentries.shrinkToFit()\nrequire entries.capacity == 1 and entries.length == 1 and entries[1] == 2 else => $abort(\"shrink\")")]
    public void CapacityOperationsCompileFromOrdinarySource(KimiDeclarationId id, string name, string source)
    {
        var c = MinimalEmissionTest.Analyze("var entries = [1: 2]\n" + source);
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var function = c.Library.GetSymbol(id);
        Assert.NotNull(function);
        Assert.Equal(CompilerFunctionKind.None, function.CompilerFunction);
        Assert.Contains(c.Ownership.Bodies, body => ReferenceEquals(body.Function, function.Declaration));
        var ir = CompilationTestHelper.WriteIr(c);
        Assert.Contains(name == "Reserve" ? "@__kimi_dictionary_reserve(" : "@__kimi_dictionary_shrink(", ir, StringComparison.Ordinal);
        ScalarEmissionTest.WriteFixture("DictionaryLibrarySource" + name, ir, string.Empty);
    }

    // The source bodies forward the standard operation's caller location, so capacity failures name the user's call,
    // never Dictionary.kimi or DictionaryStorage.kimi, including through a generic helper instance.
    [Theory]
    [InlineData("NegativeReserve", "var entries = [1: 2]\nlet n: isize = -1\nentries.reserve(n)\nConsole.writeLine(\"after\")", "Hello.kimi:3:1: abort KIMI_E_ARGUMENT: Invalid argument value\n")]
    [InlineData("OverflowReserve", "var entries = [1: 2]\nlet n: isize = 9223372036854775807\nentries.reserve(n)\nConsole.writeLine(\"after\")", "Hello.kimi:3:1: abort KIMI_E_INT_OVERFLOW: Integer overflow\n")]
    [InlineData("SizeReserve", "var entries = [1: 2]\nlet n: isize = 4611686018427387904\nentries.reserve(n)\nConsole.writeLine(\"after\")", "Hello.kimi:3:1: abort KIMI_E_ALLOC_SIZE: Allocation size exceeds limit\n")]
    [InlineData("GenericReserve", "func grow<K, V>(target: uniq/Dictionary<K, V>, amount: isize)\n    K is Equatable\n    target.reserve(amount)\nvar entries = [1: 2]\ngrow(entries@uniq, -1)\nConsole.writeLine(\"after\")", "Hello.kimi:3:5: abort KIMI_E_ARGUMENT: Invalid argument value\n")]
    public void CapacityFailuresReportTheCallerLocation(string name, string source, string stderr)
        => ScalarEmissionTest.EmitFixture("DictionaryLibraryCapacity" + name, source, string.Empty, 1, stderr);

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
            func invoke(f: (isize) -> raw/u8) -> raw/u8 => f(0)
            unsafe
                let address: usize = 4096
                let expected = address@raw/u8
                let concrete = func [address] (offset: isize) -> raw/u8
                    unsafe => return address@raw/u8
                let erased: (isize) -> raw/u8 = concrete
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
    [InlineData("unsafe => Kimi.Storage.placeEntry<i32, i32>(null, 1, 2)")]
    [InlineData("var entries: Dictionary<i32, i32> = [:]\nKimi.Storage.reserveEntries(entries@uniq, 4)")]
    [InlineData("var entries: Dictionary<i32, i32> = [:]\nKimi.Storage.shrinkEntries(entries@uniq)")]
    public void PrivateStorageFunctionsAreNotAPublicUnsafeAPI(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.InaccessibleBinding_Kd);
    }
}
