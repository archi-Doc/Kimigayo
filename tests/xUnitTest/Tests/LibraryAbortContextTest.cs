// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class LibraryAbortContextTest
{
    [Fact]
    public void SourceContextDeclarationsAreRecognized()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete, string.Join("; ", c.Binding.Issues));
        Assert.True(KimiLibraryCatalog.RequiresCallerLocation(LibrarySource.Symbol(c.Library, "RangeIterator.starting")));
    }

    // SPEC 22.5.1 (O-405): every destructor of the Kimi Kotonoha takes the caller location, the location its drop glue receives; a user
    // destructor reports its own locations (the second bullet).
    [Fact]
    public void KimiDestructorsTakeTheCallerLocation()
    {
        var c = MinimalEmissionTest.Analyze("struct Guard\n    public init() => ()\n    drop => ()\nlet guard = Guard.init()");
        var library = KotoTree.Walk(c.Library.Kotonoha.RootKoto).OfType<FunctionKoto>().Where(static x => x.IsDestructor).ToArray();
        Assert.Equal(4, library.Length); // HeapBuffer, ArrayOwningIterator, DictionaryOwnedRemainder, FixedOwnedRemainder.
        Assert.All(library, static x => Assert.True(KimiLibraryCatalog.RequiresCallerLocation(x.SymbolOf())));
        var user = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.IsDestructor);
        Assert.False(KimiLibraryCatalog.RequiresCallerLocation(user.SymbolOf()));
    }

    [Theory]
    [InlineData("HalfOpenShared", "let r = 3..1\nlet it = r.iterate()", "Hello.kimi:2:10")]
    [InlineData("HalfOpenExclusive", "var r = 3..1\nlet it = r.iterateUniq()", "Hello.kimi:2:10")]
    [InlineData("HalfOpenOwned", "let r = 3..1\nlet it = r.intoIterator()", "Hello.kimi:2:10")]
    [InlineData("ClosedShared", "let r = 3..=1\nlet it = r.iterate()", "Hello.kimi:2:10")]
    [InlineData("ClosedExclusive", "var r = 3..=1\nlet it = r.iterateUniq()", "Hello.kimi:2:10")]
    [InlineData("ClosedOwned", "let r = 3..=1\nlet it = r.intoIterator()", "Hello.kimi:2:10")]
    [InlineData("Truncate", "var values = [1, 2]\nvalues.truncate(-1)", "Hello.kimi:2:1")]
    [InlineData("Repeating", "let n: isize = -1\nlet values = Array<i32>.init(repeating: 1, count: n)", "Hello.kimi:2:14")]
    [InlineData("Generic", "func start<T>(range: Range<T, T>)\n    T is PrimitiveInteger\n    let it = range.iterate()\nstart(3..1)", "Hello.kimi:3:14")]
    public void StandardPreconditionsReportTheSourceOperation(string name, string source, string location)
        => ScalarEmissionTest.EmitFixture("LibraryAbortContext" + name, source, string.Empty, 1, location + ": abort KIMI_E_ARG_RANGE: Argument out of range\n");

    // SPEC 4.6.3: every `resolve` performs its Type's resolution and Aborts with KIMI_E_INDEX_BOUNDS on None (SPEC 22.5.2), at the
    // invoking expression (SPEC 22.5.1); a negative length fails every position (SPEC 4.6.1).
    [Theory]
    [InlineData("Position", "let p = ^4\nlet value = p.resolve(3)", "Hello.kimi:2:13")]
    [InlineData("Start", "let r = ..\nlet value = r.start.resolve(-1)", "Hello.kimi:2:13")]
    [InlineData("End", "let r = ..\nlet value = r.end.resolve(-1)", "Hello.kimi:2:13")]
    [InlineData("Range", "let r = 3..1\nlet value = r.resolve(4)", "Hello.kimi:2:13")]
    [InlineData("RangeEnd", "let r = 0..6\nlet value = r.resolve(5)", "Hello.kimi:2:13")]
    [InlineData("ClosedRange", "let r = 3..=1\nlet value = r.resolve(4)", "Hello.kimi:2:13")]
    [InlineData("ClosedEnd", "let r = 0..=5\nlet value = r.resolve(5)", "Hello.kimi:2:13")]
    [InlineData("ResolvedRange", "let r = (0..1).resolve(1)\nlet value = r.resolve(0)", "Hello.kimi:2:13")]
    [InlineData("ArraySplitAt", "let values: Array<i32> = [1]\nlet parts = values.splitAt(2)", "Hello.kimi:2:13")] // PLAN G71: no library line.
    [InlineData("SliceSplitAt", "let values: Array<i32> = [1]\nlet parts = values.slice().splitAt(^2)", "Hello.kimi:2:13")]
    [InlineData("FixedSplitAt", "let values: [2 of i32] = [1, 2]\nlet parts = values.splitAt(3)", "Hello.kimi:2:13")]
    [InlineData("GenericIndex", "func first<S>(items: ref/S) -> S.(Indexable<isize>).Element\n    S is Indexable<isize>\n    S.(Indexable<isize>).Element is Copy\n    return items[5]\nlet values: Array<i32> = [1]\nlet x = first(values)", "Hello.kimi:4:12")]
    [InlineData("GenericIndexUniq", "func touch<S>(items: uniq/S)\n    S is UniqIndexable<isize>\n    let slot = items[5]@uniq\nvar values: Array<i32> = [1]\ntouch(values@uniq)", "Hello.kimi:3:16")]
    public void FailedResolutionReportsTheSourceOperation(string name, string source, string location)
        => ScalarEmissionTest.EmitFixture("LibraryAbortContext" + name, source, string.Empty, 1, location + ": abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");

    // SPEC 22.5.1, 21.4.3 (PLAN G71): a release failure while a Kimi operation destroys elements, Array.removeAll's truncation and
    // Dictionary.clear's entries, reports the user's call.
    [Theory]
    [InlineData("ArrayRemoveAll", "var values: Array<Array<i32>> = [[1], [2]]\nvalues.removeAll(matching: func (v) => v@follow.length > 0)", "Hello.kimi:2:1")]
    [InlineData("DictionaryClear", "var entries: Dictionary<i32, Array<i32>> = [:]\n_ = entries.tryInsert(1, [1, 2])\nentries.clear()", "Hello.kimi:3:1")]
    public void ReleaseFailureReportsTheSourceOperation(string name, string source, string location)
        => ReleaseFails("LibraryAbortContextRelease" + name, source, location);

    // SPEC 22.5.1 (O-405 (a)): a release failure inside a Kimi destructor run implicitly (SPEC 16.2, 16.3.2, 16.4, 22.2.2) reports the
    // destruction point in user code, the location the Drop carries, whatever reaches the destructor: a local at a return, a block's
    // end or a reassignment, a temporary, an unconsumed remainder, a Field, an element, a generic body, a Closure's environment or an
    // object's payload. The first release fails, so each program's first release is the destructor's.
    [Theory]
    [InlineData("HeapReturn", "func build() -> isize\n    let buffer = Text.heap(16)\n    return buffer.capacity\nlet n = build()", "Hello.kimi:2:5")]
    [InlineData("HeapBlockEnd", "func build()\n    let buffer = Text.heap(16)\n    require buffer.capacity == 16 else => $abort(\"capacity\")\nbuild()", "Hello.kimi:2:5")]
    [InlineData("HeapReassignment", "var buffer = Text.heap(16)\nbuffer = Text.heap(8)", "Hello.kimi:2:1")]
    [InlineData("HeapTemporary", "let n = Text.heap(16).capacity", "Hello.kimi:1:9")]
    [InlineData("HeapField", "struct Holder\n    var buffer: Text.HeapBuffer\n    public init(buffer: Text.HeapBuffer) => self.buffer = buffer@move\nfunc run() -> i32\n    let holder = Holder.init(buffer: Text.heap(16))\n    return 0\nlet n = run()", "Hello.kimi:5:5")]
    [InlineData("HeapFieldAfterDrop", "struct Holder\n    var buffer: Text.HeapBuffer\n    public init(buffer: Text.HeapBuffer) => self.buffer = buffer@move\n    drop => ()\nfunc run() -> i32\n    let holder = Holder.init(buffer: Text.heap(16))\n    return 0\nlet n = run()", "Hello.kimi:4:5")]
    [InlineData("HeapTupleElement", "func run() -> i32\n    let pair = (Text.heap(16), 1)\n    return 0\nlet n = run()", "Hello.kimi:2:5")]
    [InlineData("HeapArrayElement", "func run() -> i32\n    let buffers: Array<Text.HeapBuffer> = [Text.heap(16)]\n    return 0\nlet n = run()", "Hello.kimi:2:5")]
    [InlineData("HeapFixedElement", "func run() -> i32\n    let buffers: [1 of Text.HeapBuffer] = [Text.heap(16)]\n    return 0\nlet n = run()", "Hello.kimi:2:5")]
    [InlineData("HeapGeneric", "func hold<T>(value: T) -> i32\n    return 0\nlet n = hold(Text.heap(16))", "Hello.kimi:1:21")]
    [InlineData("HeapClosure", "func run() -> i32\n    let buffer = Text.heap(16)\n    let job = func [buffer@move] () => ()\n    return 0\nlet n = run()", "Hello.kimi:3:5")]
    [InlineData("HeapErasedClosure", "func run() -> i32\n    let buffer = Text.heap(16)\n    let job: () -> () = func [buffer@move] () => ()\n    return 0\nlet n = run()", "Hello.kimi:3:5")]
    [InlineData("HeapObjectPayload", "func run() -> i32\n    let handle = Kimi.Intrinsics.makeRc(Text.heap(16))\n    return 0\nlet n = run()", "Hello.kimi:2:5")]
    [InlineData("ArrayIteratorRemainder", "func run() -> i32\n    let values: Array<i32> = [1, 2]\n    let it = values@move.intoIterator()\n    return 0\nlet n = run()", "Hello.kimi:3:5")]
    [InlineData("ArrayIteratorFor", "let values: Array<i32> = [1, 2]\nfor value in values@move\n    exit", "Hello.kimi:2:1")]
    [InlineData("DictionaryRemainder", "func run() -> i32\n    var entries: Dictionary<i32, i32> = [:]\n    _ = entries.tryInsert(1, 2)\n    let it = entries@move.intoIterator()\n    return 0\nlet n = run()", "Hello.kimi:4:5")]
    [InlineData("FixedRemainder", "func keep<C>(items: C) -> i32\n    C is IntoIterable\n    let it = items@move.intoIterator()\n    return 0\nlet values: [2 of Array<i32>] = [[1], [2]]\nlet n = keep(values@move)", "Hello.kimi:3:5")]
    [InlineData("HeapRemoveAll", "var buffers: Array<Text.HeapBuffer> = [Text.heap(16)]\nbuffers.removeAll(matching: func (b) => b@follow.capacity > 0)", "Hello.kimi:2:1")]
    [InlineData("HeapDictionaryClear", "var entries: Dictionary<i32, Text.HeapBuffer> = [:]\n_ = entries.tryInsert(1, Text.heap(16))\nentries.clear()", "Hello.kimi:3:1")]
    [InlineData("HeapReplace", "var buffer = Text.heap(16)\nKimi.Intrinsics.replace(buffer@uniq, with: Text.heap(8))", "Hello.kimi:2:1")]
    [InlineData("ArrayControl", "func run() -> i32\n    let values: Array<i32> = [1, 2]\n    return 0\nlet n = run()", "Hello.kimi:2:5")]
    public void ImplicitDestructionReportsTheDestructionPoint(string name, string source, string location)
        => ReleaseFails("LibraryAbortContextDestruction" + name, source, location);

    [Fact]
    public void UserAbortAndSameSpelledHelpersKeepTheirOwnMeaning()
        => ScalarEmissionTest.EmitFixture("LibraryAbortContextUser", "func argumentOutOfRange() -> i32 => 42\nrequire argumentOutOfRange() == 42 else => $abort(\"helper\")\n$abort(\"user\")", string.Empty, 1, "Hello.kimi:3:1: abort KIMI_E_ABORT: user\n");

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmSourceContextPreparationAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("let r = 0..3\nvar total = 0\nfor value in r => total += value\nrequire total == 3 else => $abort(\"sum\")");
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid);
    }

    // A program whose every release fails (the runtime's HeapFree returns 0, GetLastError 5): its first release Aborts with KIMI_E_FREE.
    private static void ReleaseFails(string name, string source, string location)
    {
        var ir = CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze(source))
            .Replace("call i32 @HeapFree(", "call i32 @fail_release(", StringComparison.Ordinal)
            .Replace("call i32 @GetLastError()", "call i32 @fail_release_error()", StringComparison.Ordinal) +
            "\ndefine internal i32 @fail_release(ptr %heap, i32 %flags, ptr %memory) {\nentry:\n  ret i32 0\n}\n" +
            "define internal i32 @fail_release_error() {\nentry:\n  ret i32 5\n}\n";
        ScalarEmissionTest.WriteFixture(name, ir, string.Empty, 1, location + ": abort KIMI_E_FREE: Failed to free memory (win32=5)\n");
    }
}
