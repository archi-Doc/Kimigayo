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
    {
        var ir = CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze(source))
            .Replace("call i32 @HeapFree(", "call i32 @fail_release(", StringComparison.Ordinal)
            .Replace("call i32 @GetLastError()", "call i32 @fail_release_error()", StringComparison.Ordinal) +
            "\ndefine internal i32 @fail_release(ptr %heap, i32 %flags, ptr %memory) {\nentry:\n  ret i32 0\n}\n" +
            "define internal i32 @fail_release_error() {\nentry:\n  ret i32 5\n}\n";
        ScalarEmissionTest.WriteFixture("LibraryAbortContextRelease" + name, ir, string.Empty, 1, location + ": abort KIMI_E_FREE: Failed to free memory (win32=5)\n");
    }

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
}
