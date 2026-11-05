// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class DictionaryLibraryTest
{
    [Fact]
    public void LiteralChecksAndImplicitCleanupReuseTypedSourceCalls()
    {
        const string Source = "var entries = [1: \"first\", 2: \"second\"]\nentries.clear()\n_ = entries.tryInsert(3, \"last\")\nConsole.writeLine(entries[3])";
        var c = MinimalEmissionTest.Analyze(Source);
        var (sel, generated) = FunctionCodegenTest.GeneratedDefinitions(Source);
        Assert.Contains(sel.Library.DictionaryStorage(DictionaryCallee.FindEntry).Declaration, generated);
        Assert.Contains(sel.Library.DictionaryStorage(DictionaryCallee.ClearEntries).Declaration, generated);
        var ir = CompilationTestHelper.WriteIr(c);
        ScalarEmissionTest.WriteFixture("DictionaryLibraryTypedCleanup", ir, "last\n");
    }

    // SPEC 4.5, 16.3.2: a Dictionary Field's drop helper requested only by its owner's drop glue brings the runtimes its row
    // declares, so the module defines every Dictionary helper it calls (PLAN G133).
    [Theory]
    [InlineData("Scalar", "i32")]
    [InlineData("String", "string")]
    public void FieldDropHelperRequestedByDropGlueIsDefined(string name, string valueType)
    {
        var c = MinimalEmissionTest.Analyze("struct Box\n    public let entries: Dictionary<i32, " + valueType + ">\nlet value: Option<Box> = .None");
        var ir = CompilationTestHelper.WriteIr(c);
        var calls = System.Text.RegularExpressions.Regex.Matches(ir, @"call void @(__kimi_dictionary_\w+)\(");
        Assert.NotEmpty(calls);
        foreach (System.Text.RegularExpressions.Match call in calls)
        {
            Assert.Contains("define internal void @" + call.Groups[1].Value + "(", ir, StringComparison.Ordinal);
        }

        // X4 (PLAN G134): the Drop helper grows no buffer, so the module holds no capacity routine.
        Assert.DoesNotContain("@__kimi_array_grow(", ir, StringComparison.Ordinal);
        ScalarEmissionTest.WriteFixture("DictionaryLibraryFieldDrop" + name, ir, string.Empty);
    }

    // SPEC 4.9.6: a Dictionary operation is called with its published summary, not as an unbounded generic call, so a generic body may
    // keep its result across another call; `K` here may be a borrow, whose Loans the ordinary dependencies track.
    [Theory]
    [InlineData("DictionaryPublishedTwice", "func put<K>(k: K) -> isize\n    K is Copy\n    K is Equatable\n    var d: Dictionary<K, i32> = [:]\n    let r = d.tryInsert(k, 1)\n    var e: Dictionary<K, i32> = [:]\n    let s = e.tryInsert(k, 2)\n    match r\n        .Ok(()) => return 1\n        .Err(_) => return 0\nlet n = 7\nConsole.writeLine(\"\\(put(n@ref))\")", "1\n")]
    [InlineData("DictionaryPublishedThenGeneric", "func touch<V>(v: V) -> isize\n    V is Copy\n    return 1\nfunc put<K>(k: K) -> isize\n    K is Copy\n    K is Equatable\n    var d: Dictionary<K, i32> = [:]\n    let r = d.tryInsert(k, 1)\n    let n = touch(k)\n    match r\n        .Ok(()) => return n\n        .Err(_) => return 0\nlet n = 7\nConsole.writeLine(\"\\(put(n@ref))\")", "1\n")]
    [InlineData("DictionaryPublishedArrayControl", "func f<U>(x: U) -> isize\n    U is Copy\n    var a = Array<U>.init(capacity: 2)\n    a.append(x)\n    var b = Array<U>.init(capacity: 2)\n    b.append(x)\n    return a.length + b.length\nlet n = 3\nConsole.writeLine(\"\\(f(n@ref))\")", "2\n")]
    public void GenericBodiesUsePublishedSummaries(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture(name, source, stdout);

    [Fact]
    public void RemovalResultCompilesFromOrdinarySource()
    {
        const string Source = "var entries = [1: 42]\nmatch entries.remove(1)\n" +
            "    .Some((let key, let value)) => require key == 1 and value == 42 else => $abort(\"removed\")\n" +
            "    .None => $abort(\"missing\")\nrequire entries.length == 0 else => $abort(\"length\")";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        var function = c.Library.GetSymbol(KimiDeclarationId.DictionaryRemove);
        Assert.NotNull(function);
        Assert.Equal(CompilerFunctionKind.None, function.CompilerFunction);
        var (sel, generated) = FunctionCodegenTest.GeneratedDefinitions(Source);
        Assert.Contains(sel.Library.GetSymbol(KimiDeclarationId.DictionaryRemove)!.Declaration, generated);
        var ir = CompilationTestHelper.WriteIr(c);
        ScalarEmissionTest.WriteFixture("DictionaryLibrarySourceRemove", ir, string.Empty);
    }

    [Fact]
    public void ClearingCompilesFromOrdinarySource()
    {
        const string Source = "var entries = [1: 42]\nentries.clear()\nrequire entries.length == 0 and entries.capacity >= 1 else => $abort(\"clear\")";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        var function = c.Library.GetSymbol(KimiDeclarationId.DictionaryClear);
        Assert.NotNull(function);
        Assert.Equal(CompilerFunctionKind.None, function.CompilerFunction);
        var (sel, generated) = FunctionCodegenTest.GeneratedDefinitions(Source);
        Assert.Contains(sel.Library.GetSymbol(KimiDeclarationId.DictionaryClear)!.Declaration, generated);
        ScalarEmissionTest.WriteFixture("DictionaryLibrarySourceClear", CompilationTestHelper.WriteIr(c), string.Empty);
    }

    [Fact]
    public void LookupCompilesFromOrdinarySource()
    {
        const string Source = "var entries = [1: 42]\nmatch entries.tryGet(1)\n    .Some(let value) => require value == 42 else => $abort(\"value\")\n" +
            "    .None => $abort(\"missing\")";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        var function = c.Library.GetSymbol(KimiDeclarationId.DictionaryTryGet);
        Assert.NotNull(function);
        Assert.Equal(CompilerFunctionKind.None, function.CompilerFunction);
        var (sel, generated) = FunctionCodegenTest.GeneratedDefinitions(Source);
        Assert.Contains(sel.Library.GetSymbol(KimiDeclarationId.DictionaryTryGet)!.Declaration, generated);
        var ir = CompilationTestHelper.WriteIr(c);
        ScalarEmissionTest.WriteFixture("DictionaryLibrarySourceLookup", ir, string.Empty);
    }

    [Theory]
    [InlineData(KimiDeclarationId.DictionaryTryInsert, "TryInsert", "i32", "match entries.tryInsert(1, 2)\n    .Ok(_) => require entries[1] == 2 else => $abort(\"stored\")\n    .Err(_) => $abort(\"rejected\")\nmatch entries.tryInsert(1, 3)\n    .Ok(_) => $abort(\"duplicate\")\n    .Err((let key, let value)) => require key == 1 and value == 3 and entries[1] == 2 and entries.length == 1 else => $abort(\"returned\")")]
    [InlineData(KimiDeclarationId.DictionaryInsertOrReplace, "InsertOrReplace", "i32", "match entries.insertOrReplace(1, 2)\n    .Some(_) => $abort(\"replaced\")\n    .None => require entries[1] == 2 else => $abort(\"stored\")\nmatch entries.insertOrReplace(1, 3)\n    .Some(let old) => require old == 2 and entries[1] == 3 and entries.length == 1 else => $abort(\"old\")\n    .None => $abort(\"appended\")")]
    [InlineData(KimiDeclarationId.DictionaryInsertOrReplace, "InsertOrReplaceString", "string", "match entries.insertOrReplace(1, \"a\")\n    .Some(_) => $abort(\"replaced\")\n    .None => require entries.length == 1 else => $abort(\"stored\")\nmatch entries.insertOrReplace(1, \"b\")\n    .Some(let old) => require old == \"a\" and entries[1] == \"b\" and entries.length == 1 else => $abort(\"old\")\n    .None => $abort(\"appended\")")]
    public void InsertionCompilesFromOrdinarySource(KimiDeclarationId id, string name, string valueType, string source)
    {
        var program = "var entries: Dictionary<i32, " + valueType + "> = [:]\n" + source;
        var c = MinimalEmissionTest.Analyze(program);
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        var function = c.Library.GetSymbol(id);
        Assert.NotNull(function);
        Assert.Equal(CompilerFunctionKind.None, function.CompilerFunction);
        var (sel, generated) = FunctionCodegenTest.GeneratedDefinitions(program);
        Assert.Contains(sel.Library.GetSymbol(id)!.Declaration, generated);
        var ir = CompilationTestHelper.WriteIr(c);
        Assert.Contains("__kimi_dictionary_place", ir, StringComparison.Ordinal);
        ScalarEmissionTest.WriteFixture("DictionaryLibrarySource" + name, ir, string.Empty);
    }

    [Theory]
    [InlineData(KimiDeclarationId.DictionaryReserve, "Reserve", "entries.reserve(8)\nrequire entries.capacity >= 9 and entries.length == 1 and entries[1] == 2 else => $abort(\"reserve\")")]
    [InlineData(KimiDeclarationId.DictionaryShrinkToFit, "Shrink", "entries.reserve(8)\nentries.shrinkToFit()\nrequire entries.capacity == 1 and entries.length == 1 and entries[1] == 2 else => $abort(\"shrink\")")]
    public void CapacityOperationsCompileFromOrdinarySource(KimiDeclarationId id, string name, string source)
    {
        var program = "var entries = [1: 2]\n" + source;
        var c = MinimalEmissionTest.Analyze(program);
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        var function = c.Library.GetSymbol(id);
        Assert.NotNull(function);
        Assert.Equal(CompilerFunctionKind.None, function.CompilerFunction);
        var (sel, generated) = FunctionCodegenTest.GeneratedDefinitions(program);
        Assert.Contains(sel.Library.GetSymbol(id)!.Declaration, generated);
        // SPEC 22.2, 22.5.1: the capacity decisions are DictionaryStorage source over located primitives; the compiler builds
        // no bridges, platform callbacks or callback tables.
        var storage = name == "Reserve" ? KimiDeclarationId.DictionaryStorageReserve : KimiDeclarationId.DictionaryStorageShrinkToFit;
        Assert.Contains(sel.Library.GetSymbol(storage)!.Declaration, generated);
        var ir = CompilationTestHelper.WriteIr(c);
        Assert.Contains(name == "Reserve" ? "call ptr @__kimi_raw_allocate(" : "call ptr @__kimi_try_allocate(", ir, StringComparison.Ordinal);
        ScalarEmissionTest.WriteFixture("DictionaryLibrarySource" + name, ir, string.Empty);
    }

    // The source bodies forward the standard operation's caller location, so capacity failures name the user's call,
    // never Dictionary.kimi or DictionaryStorage.kimi, including through a generic helper instance.
    [Theory]
    [InlineData("NegativeReserve", "var entries = [1: 2]\nlet n: isize = -1\nentries.reserve(n)\nConsole.writeLine(\"after\")", "Hello.kimi:3:1: abort KIMI_E_ARG_RANGE: Argument out of range\n")]
    [InlineData("OverflowReserve", "var entries = [1: 2]\nlet n: isize = 9223372036854775807\nentries.reserve(n)\nConsole.writeLine(\"after\")", "Hello.kimi:3:1: abort KIMI_E_INT_OVERFLOW: Integer overflow\n")]
    [InlineData("SizeReserve", "var entries = [1: 2]\nlet n: isize = 4611686018427387904\nentries.reserve(n)\nConsole.writeLine(\"after\")", "Hello.kimi:3:1: abort KIMI_E_ALLOC_SIZE: Allocation size exceeds limit\n")]
    [InlineData("GenericReserve", "func grow<K, V>(target: uniq/Dictionary<K, V>, amount: isize)\n    K is Equatable\n    target.reserve(amount)\nvar entries = [1: 2]\ngrow(entries@uniq, -1)\nConsole.writeLine(\"after\")", "Hello.kimi:3:5: abort KIMI_E_ARG_RANGE: Argument out of range\n")]
    public void CapacityFailuresReportTheCallerLocation(string name, string source, string stderr)
        => ScalarEmissionTest.EmitFixture("DictionaryLibraryCapacity" + name, source, string.Empty, 1, stderr);

    // K12 (DECISIONS R4-M13a, R5-M145): the capacity decisions, reserve and grow, are generated library bodies of ordinary source; their
    // caller location is the native capacity fixtures' (CapacityFailuresReportTheCallerLocation).
    [Fact]
    public void CapacityDecisionsCompileFromOrdinarySource()
    {
        const string Source = "var entries: Dictionary<i32, i32> = [:]\nentries.reserve(8)\n_ = entries.tryInsert(1, 2)";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, string.Join('\n', TestDiagnostics.Of(c.Library.Kotonoha)));
        var definitions = FunctionCodegenTest.GeneratedDefinitions(Source).Definitions;
        Assert.Contains(definitions, static x => x is FunctionKoto { Name: "grow" } && IsStorage(x));
        Assert.Contains(definitions, static x => x is FunctionKoto { Name: "reserve" } && IsStorage(x));
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
                public func reserve() => $abort("user reserve")
                public func grow() => $abort("user grow")
                public func append() => $abort("user append")
                public func shrinkToFit() => $abort("user shrinkToFit")
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

    // SPEC 22.5.1, 21.4.3: an Alloc failure inside the DictionaryStorage capacity bodies reports the standard operation the user
    // wrote, through tryInsert, insertOrReplace, a literal's key and a generic helper's call, never a library position.
    [Theory]
    [InlineData("Reserve", "var entries: Dictionary<i32, i32> = [:]\nentries.reserve(4)", "Hello.kimi:2:1")]
    [InlineData("TryInsert", "var entries: Dictionary<i32, i32> = [:]\n_ = entries.tryInsert(1, 2)", "Hello.kimi:2:5")]
    [InlineData("InsertOrReplace", "var entries: Dictionary<i32, i32> = [:]\n_ = entries.insertOrReplace(1, 2)", "Hello.kimi:2:5")]
    [InlineData("Literal", "let n = 1\nvar entries = [n: 10, 2: 20]", "Hello.kimi:2:16")]
    [InlineData("Generic", "func put<K, V>(target: uniq/Dictionary<K, V>, key: K, value: V)\n    K is Equatable\n    _ = target.tryInsert(key@move, value@move)\nvar entries: Dictionary<i32, i32> = [:]\nput(entries@uniq, 1, 2)", "Hello.kimi:3:9")]
    public void CapacityAllocationFailuresReportTheCallerLocation(string name, string source, string location)
    {
        var ir = CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze(source)).Replace("call ptr @HeapAlloc(", "call ptr @fail_dictionary_alloc(", StringComparison.Ordinal) +
            "\ndefine internal ptr @fail_dictionary_alloc(ptr %heap, i32 %flags, i64 %bytes) {\nentry:\n  ret ptr null\n}\n";
        ScalarEmissionTest.WriteFixture("DictionaryLibraryAllocation" + name, ir, string.Empty, 1, location + ": abort KIMI_E_ALLOC: Failed to allocate memory\n");
    }

    // SPEC 22.5.1, 21.4.3: the release of the old storage after shrinking reports the user's shrinkToFit call.
    [Fact]
    public void ShrinkReleaseFailureReportsTheCallerLocation()
    {
        var ir = CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze("var entries: Dictionary<i32, i32> = [:]\nentries.reserve(8)\n_ = entries.tryInsert(1, 2)\nentries.shrinkToFit()"))
            .Replace("call i32 @HeapFree(", "call i32 @fail_dictionary_free(", StringComparison.Ordinal)
            .Replace("call i32 @GetLastError()", "call i32 @fail_dictionary_error()", StringComparison.Ordinal) +
            "\ndefine internal i32 @fail_dictionary_free(ptr %heap, i32 %flags, ptr %memory) {\nentry:\n  ret i32 0\n}\n" +
            "define internal i32 @fail_dictionary_error() {\nentry:\n  ret i32 5\n}\n";
        ScalarEmissionTest.WriteFixture("DictionaryLibraryReleaseFailure", ir, string.Empty, 1, "Hello.kimi:4:1: abort KIMI_E_FREE: Failed to free memory (win32=5)\n");
    }

    // SPEC 4.9.6, 8.11.2: the capacity bodies call located primitives directly rather than callbacks, so a confined
    // implementation may reserve, insert, replace and shrink.
    [Fact]
    public void ConfinedImplementationsUseCapacityOperations()
    {
        const string Source = """
            contract Sink
                func put(self: uniq/Self, value: i32) -> i32
                    effect confined
            struct CountingSink
                Self is Sink
                var seen: Dictionary<i32, i32>
                public init() => self.seen = [:]
                public func put(self: uniq/Self, value: i32) -> i32
                    self.seen.reserve(1)
                    _ = self.seen.tryInsert(value, value)
                    _ = self.seen.insertOrReplace(value, value + 1)
                    self.seen.shrinkToFit()
                    return self.seen[value]
            var sink = CountingSink.init()
            require sink.put(3) == 4 else => $abort("put")
            """;
        ScalarEmissionTest.EmitFixture("DictionaryLibraryConfined", Source, string.Empty);
    }

    [Fact]
    public void ProgramsWithoutDictionariesDoNotCompileStorageAlgorithms()
    {
        const string Source = "Console.writeLine(\"plain\")";
        Assert.DoesNotContain(FunctionCodegenTest.GeneratedDefinitions(Source).Definitions, IsStorage);
    }

    [Fact]
    public void PrivateHandleUsesTheExpectedPlatformLayout()
    {
        var c = MinimalEmissionTest.Analyze("var entries: Dictionary<i32, i32> = [:]");
        var pointer = c.Library.DictionaryUnlink.Parameters[0].Type.TypeOf()!;
        var handle = pointer.Components[0];
        Assert.True(handle.Symbol!.Adt!.IsCLayout);
        var layout = c.Layout.Of(handle).Value!.Layout;
        Assert.Equal(56, layout.Size);
        Assert.Equal(8, layout.Alignment);
        Assert.Equal(new[] { 0, 8, 16, 24, 32, 40, 48 }, layout.FieldOffsets.ToArray());
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
        const string Source = "var entries: Dictionary<i32, i32> = [:]\n_ = entries.tryInsert(1, 2)\n_ = entries.remove(1)";
        var c = MinimalEmissionTest.Analyze(Source);
        var outputIr = CompilationTestHelper.WriteIr(c);
        var (sel, generated) = FunctionCodegenTest.GeneratedDefinitions(Source);
        Assert.Contains(sel.Library.DictionaryUnlink, generated);

        // The library reach of the hidden DictionaryStorage instances in the checked set (DECISIONS R4-M13a, R5-M27, R5-M124), where the old
        // analysis collected the Dictionary runtime's callees for every Dictionary Type: placeEntry's LegacyRow item targets append, which
        // reaches appendSlot; the Dictionary from none targets initialize, which reaches clearLinks; tryInsert and remove reach findEntry.
        // The glue targets clearEntries only when an entry may need a drop (CallsEntries): never for i32 entries, and for a string value.
        // Shrinking is reached only through shrinkToFit calls; no bridge compiles it eagerly.
        Assert.Contains(sel.Library.DictionaryAppendSlot, generated);
        Assert.Contains(sel.Library.DictionaryStorage(DictionaryCallee.Initialize).Declaration, generated);
        Assert.Contains(sel.Library.DictionaryClearLinks, generated);
        Assert.Contains(sel.Library.DictionaryStorage(DictionaryCallee.FindEntry).Declaration, generated);
        Assert.Contains(sel.Library.DictionaryStorage(DictionaryCallee.Append).Declaration, generated);
        Assert.DoesNotContain(sel.Library.DictionaryStorage(DictionaryCallee.ClearEntries).Declaration, generated);
        var (texts, dropped) = FunctionCodegenTest.GeneratedDefinitions("var entries: Dictionary<i32, string> = [:]\n_ = entries.tryInsert(1, \"a\")");
        Assert.Contains(texts.Library.DictionaryStorage(DictionaryCallee.ClearEntries).Declaration, dropped);
        Assert.DoesNotContain(sel.Library.DictionaryCompact, generated);
        Assert.DoesNotContain(sel.Library.GetSymbol(KimiDeclarationId.DictionaryStorageShrinkToFit)!.Declaration, generated);
        Assert.Equal(CompilerFunctionKind.None, c.Library.DictionaryUnlink.SymbolOf()!.CompilerFunction);
        Assert.Contains("DictionaryStorage.kimi", c.Library.DictionaryUnlink.CodeContext.SourceDocument!.Path);
        ScalarEmissionTest.WriteFixture("DictionaryLibraryRemoval", outputIr, string.Empty);
    }

    [Theory]
    [InlineData("unsafe => Kimi.DictionaryStorage.unlink(null, 24, 1)")]
    [InlineData("unsafe => Kimi.DictionaryStorage.initialize(null)")]
    [InlineData("unsafe => Kimi.DictionaryStorage.clearLinks(null)")]
    [InlineData("unsafe => Kimi.Storage.placeEntry<i32, i32>(null, 1, 2)")]
    [InlineData("var entries: Dictionary<i32, i32> = [:]\nKimi.DictionaryStorage.reserveEntries(entries@uniq, 4)")]
    [InlineData("var entries: Dictionary<i32, i32> = [:]\nKimi.DictionaryStorage.shrinkEntries(entries@uniq)")]
    [InlineData("unsafe => _ = Kimi.DictionaryStorage.reserve(null, 24, 4)")]
    [InlineData("unsafe => Kimi.DictionaryStorage.grow(null, 24, 4)")]
    [InlineData("unsafe => Kimi.Storage.transferBytes(null, null, 0)")]
    [InlineData("let memory = Kimi.Storage.tryAllocateBytes(8)")]
    [InlineData("Kimi.Storage.countOverflow()")]
    [InlineData("Kimi.Storage.allocationSizeExceeded()")]
    public void PrivateStorageFunctionsAreNotAPublicUnsafeAPI(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.InaccessibleBinding_Kd);
    }

    // A declaration of DictionaryStorage.kimi.
    private static bool IsStorage(Koto definition)
        => definition.CodeContext.SourceDocument?.Path.EndsWith("DictionaryStorage.kimi", StringComparison.Ordinal) == true;
}
