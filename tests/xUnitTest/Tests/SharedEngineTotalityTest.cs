// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 13.5.5.1, 15.6.3, 15.8.2: the concrete engine carries a root's Loan into every holder a value flows into, whatever the
// operation: a captured exclusive reference with a declared Origin roots its closure body (E1), a store through an exclusive
// reference retains the stored value's Loans in the referent (E2), and a closure value holds its entries' Loans (E3).
public class SharedEngineTotalityTest
{
    private const string Copy = "    X is Copy\n";

    private const string Store = "func f<X>(value: uniq/X during a, other: uniq/X during a, x: X) -> X\n" + Copy +
        "    let local = value\n    var holder = other\n";

    private const string Put = "func put<X>(slot: uniq/(uniq/X during b), v: uniq/X during b) -> ()\n    slot@follow = v\n";

    private const string Closure = "    var v = value\n    let hh = func [v] () => v@follow\n";

    // E1: the environment binding of `view` is its own external root inside the closure body, with a declared or an elided Origin.
    [Theory]
    [InlineData("func g<X>(view: uniq/X during a, x: X) -> ()\n" + Copy + "    var reader = func [view, x] () -> ()\n        let c = view\n        view@follow = x\n        _ = c@follow\n    reader()\nvar n: i32 = 1\ng(n@uniq, 9)", "view", "let c = view")]
    [InlineData("func g<X>(view: uniq/X, x: X) -> ()\n" + Copy + "    var reader = func [view, x] () -> ()\n        let c = view\n        view@follow = x\n        _ = c@follow\n    reader()\nvar n: i32 = 1\ng(n@uniq, 9)", "view", "let c = view")]
    // E2: the store through `sref` installs `local` in `holder`, which retains the Loan on `value`; so does the call rule.
    [InlineData(Store + "    let sref = holder@uniq\n    sref@follow = local@move\n    value@follow = x\n    return holder@follow\nvar n: i32 = 1\nvar m: i32 = 2\n_ = f(n@uniq, m@uniq, 9)", "value", "var holder = other")]
    [InlineData(Put + Store + "    put(holder@uniq, local)\n    value@follow = x\n    return holder@follow\nvar n: i32 = 1\nvar m: i32 = 2\n_ = f(n@uniq, m@uniq, 9)", "value", "var holder = other")]
    // E3: the closure value holds the entry's Loan on `value` in both Origin forms, on a local slot, and after a Move into a Tuple.
    [InlineData("func h(value: uniq/i32, x: i32) -> i32\n" + Closure + "    v@follow = x\n    return hh()\nvar n: i32 = 1\n_ = h(n@uniq, 9)", "v", "let hh = func [v] () => v@follow")]
    [InlineData("func h(value: uniq/i32 during a, x: i32) -> i32\n" + Closure + "    v@follow = x\n    return hh()\nvar n: i32 = 1\n_ = h(n@uniq, 9)", "v", "let hh = func [v] () => v@follow")]
    [InlineData("var n: i32 = 1\nvar v = n@uniq\nlet hh = func [v] () => v@follow\nv@follow = 9\n_ = hh()", "v", "let hh = func [v] () => v@follow")]
    [InlineData("func h(value: uniq/i32 during a, x: i32) -> i32\n" + Closure + "    let t = (hh@move, 1)\n    v@follow = x\n    let g = t.0@move\n    return g() + t.1\nvar n: i32 = 1\n_ = h(n@uniq, 9)", "v", "let t = (hh@move, 1)")]
    public void RejectsAccessesWhileTheRetainedLoanLives(string source, string text, string loan)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure is OwnershipFailure.Unsupported or OwnershipFailure.Internal);

        // DIAGNOSTICS.md §10: the record is located at the conflicting access and relates the holder retaining the Loan.
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.ComparisonLoanConflict_Kd), text), (error.Code, Text(source, error.Span)));
        var related = Assert.Single(error.Related!);
        Assert.Equal(("loan", loan), (related.Role, Text(source, related.Span)));
    }

    // The Loans end at the holders' last uses, so the orders that write after them run.
    [Theory]
    [InlineData("EnvironmentRoot", "func g<X>(view: uniq/X during a, x: X) -> ()\n" + Copy + "    var reader = func [view, x] () -> ()\n        let c = view\n        _ = c@follow\n        view@follow = x\n    reader()\nvar n: i32 = 1\ng(n@uniq, 9)\nrequire n == 9 else => $abort(\"environment\")")]
    [InlineData("StoreReturn", Store + "    let sref = holder@uniq\n    sref@follow = local@move\n    return holder@follow\nvar n: i32 = 1\nvar m: i32 = 2\nlet r = f(n@uniq, m@uniq, 9)\nrequire r == 1 and n == 1 and m == 2 else => $abort(\"return\")")]
    [InlineData("StoreThenWrite", Store + "    let sref = holder@uniq\n    sref@follow = local@move\n    let r = holder@follow\n    value@follow = x\n    return r\nvar n: i32 = 1\nvar m: i32 = 2\nlet r = f(n@uniq, m@uniq, 9)\nrequire r == 1 and n == 9 and m == 2 else => $abort(\"store\")")]
    [InlineData("ReplaceThenWrite", Store + "    holder = local@move\n    let r = holder@follow\n    value@follow = x\n    return r\nvar n: i32 = 1\nvar m: i32 = 2\nlet r = f(n@uniq, m@uniq, 9)\nrequire r == 1 and n == 9 and m == 2 else => $abort(\"replace\")")]
    [InlineData("ClosureThenWrite", "func h(value: uniq/i32 during a, x: i32) -> i32\n" + Closure + "    let r = hh()\n    v@follow = x\n    return r + v@follow\nvar n: i32 = 1\nlet r = h(n@uniq, 9)\nrequire r == 10 else => $abort(\"closure\")")]
    [InlineData("ClosureInTuple", "func h(value: uniq/i32 during a, x: i32) -> i32\n" + Closure + "    let t = (hh@move, 1)\n    let g = t.0@move\n    let r = g() + t.1\n    v@follow = x\n    return r + v@follow\nvar n: i32 = 1\nlet r = h(n@uniq, 9)\nrequire r == 11 else => $abort(\"tuple\")")]
    public void RunsTheOrdersThatWriteAfterTheLastUse(string name, string source)
        => ScalarEmissionTest.EmitFixture("SharedEngine" + name, source, string.Empty);

    private static string Text(string source, Kimi.Diagnostics.SourceSpan? span)
        => span is { } at ? source.Substring(at.Start, at.Length) : string.Empty;
}
