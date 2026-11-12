// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class ScalarTemporaryBorrowTest
{
    private const string Read = "func read(n: ref/i32) -> bool\n    Console.writeLine(\"called\")\n    return true\nfunc one() -> i32 => 41\n";

    private const string Both = "func both(p: (ref/i32 during a, ref/i32 during a)) -> bool\n    Console.writeLine(\"called\")\n    return p.0@follow + p.1@follow == 3\nlet x: i32 = 2\n";

    private const string Invariant = "func fe(p: (ref/i32 during a, i32), q: uniq/(ref/i32 during a)) -> i32 => 0\nfunc fi(p: (ref/(ref/i32 during a, i32) during a, i32), q: uniq/(ref/i32 during a)) -> i32 => 0\nfunc t(c: bool, n: ref/i32 during b, w: uniq/(ref/i32 during b), v: i32) -> i32\n";

    private const string Literals = "func kf(x: ref/[2 of i64]) -> i32 => 0\nfunc kg(x: ref/[2 of i32]) -> i32 => 0\nfunc km(x: ref/Range<i64, i32>) -> i32 => 0\nfunc kn(x: ref/Range<i64, i64>) -> i32 => 0\nfunc t(c: bool, s: string, b: u8, v: i32, w: i64) -> i32\n";

    private const string Composites = "func kfe(x: ref/Range<i32, FromEnd<i32>>) -> i32 => 0\nfunc kcf(x: ref/ClosedRange<i32, FromEnd<i32>>) -> i32 => 0\nfunc kfs(x: ref/Range<FromEnd<i32>, i32>) -> i32 => 0\nfunc kft(x: ref/[2 of (i64, i64)]) -> i32 => 0\nfunc kfu(x: ref/[2 of (i32, i32)]) -> i32 => 0\nfunc kff(x: ref/[2 of [2 of i64]]) -> i32 => 0\n";

    private const string Values = "func kv(p: ref/[2 of i32] during a, q: ref/i32 during a) -> ref/i32 during a => q\nfunc kr(p: ref/Range<i32, i32> during a, q: ref/i32 during a) -> ref/i32 during a => q\nfunc kt(p: ref/(i32, i32) during a, q: ref/i32 during a) -> ref/i32 during a => q\nfunc ke(p: (ref/[2 of i32] during a, i32), q: ref/i32 during a) -> ref/i32 during a => q\nfunc t(n: ref/i32, v: i32) -> i32\n";

    private const string Inspect = "func inspect<T>(value: ref/T) -> () => ()\nfunc makeValue() -> i64 => 1\n";

    // SPEC 10.2: an owner scalar temporary is materialized once and shared-borrowed for a ref/T argument.
    [Theory]
    [InlineData("Call", Read + "require read(one()) else => $abort(\"value\")", "called\n")]
    [InlineData("Twice", Read + "require read(one()) and read(one()) else => $abort(\"value\")", "called\ncalled\n")]
    [InlineData("Binary", Read + "require read(one() + 1) else => $abort(\"value\")", "called\n")]
    [InlineData("Bool", "func check(b: ref/bool) -> bool\n    Console.writeLine(\"checked\")\n    return true\nfunc yes() -> bool => true\nrequire check(yes()) else => $abort(\"value\")", "checked\n")]
    [InlineData("Literal", Read + "require read(1) else => $abort(\"value\")", "called\n")]
    [InlineData("Float", "func check(x: ref/f64) -> bool\n    Console.writeLine(\"checked\")\n    return true\nrequire check(1.5) else => $abort(\"value\")", "checked\n")]
    [InlineData("Rank", "func pick(value: i64) -> i32 => 1\nfunc pick(value: ref/i32) -> i32 => 2\nrequire pick(1) == 1 else => $abort(\"rank\")", "")]
    [InlineData("Returned", Read + "func keep(n: ref/i32) -> ref/i32 during n => n\nrequire read(keep(1)) and read(keep(one())) else => $abort(\"value\")", "called\ncalled\n")]
    [InlineData("Generic", Inspect + "inspect(1)\ninspect(1.5)\ninspect(makeValue())\nConsole.writeLine(\"done\")", "done\n")]

    // The result of a selection, `do`, loop exit or short circuit is stored in its result slot at the borrow, as an argument,
    // an explicit borrow or an interpolated value.
    [InlineData("Join", Read + "let c = true\nrequire read(if c => 1 else => 2) and read(do => 3) and read(loop => exit 4) else => $abort(\"join\")", "called\ncalled\ncalled\n")]
    [InlineData("ShortCircuit", "func check(b: ref/bool) -> bool => b@follow\nlet x: i32 = 4\nrequire check((x > 1 and x < 9)@ref) and not check(x > 9 or x < 0) else => $abort(\"bool\")", "")]
    [InlineData("Interpolated", "let c = true\nlet x: i32 = 4\nConsole.writeLine(\"\\(if c => 5 else => 6) \\(do => 7) \\(x > 1 and x < 9)\")", "5 7 true\n")]

    // PLAN G585: a literal at a ref/ element of a literal argument is the same temporary, its Place Origin an input of the call's
    // Origins: a scalar, a Tuple and an array element, and through a function value.
    [InlineData("Element", Both + "require both((x@ref, 1)) else => $abort(\"element\")", "called\n")]
    [InlineData("ValueElement", Both + "let f = both\nrequire f((x@ref, 1)) else => $abort(\"value\")", "called\n")]
    [InlineData("NestedElement", "func nested(p: (ref/(i32, i32) during a, i32)) -> bool\n    let inner: (i32, i32) = p.0@follow\n    return inner.0 + inner.1 + p.1 == 6\nrequire nested(((1, 2), 3)) else => $abort(\"nested\")", "")]
    [InlineData("ArrayElement", "func sum(p: (ref/[2 of i32] during a, i32)) -> bool\n    let items: [2 of i32] = p.0@follow\n    return items[0] + items[1] + p.1 == 6\nrequire sum(([1, 2], 3)) else => $abort(\"array\")", "")]
    [InlineData("StringElement", "func show(p: (ref/(string, i32) during a, i32)) -> bool\n    Console.writeLine(p.0.0)\n    return p.1 == 2\nlet s = \"text\"\nrequire show(((s@move, 1), 2)) else => $abort(\"string\")", "text\n")]
    [InlineData("LetJoin", "let c = true\nlet v: i32 = 2\nlet x: ref/i32 = if c => v@ref else => 1\nlet y: ref/i32 = if c => 1 else => 2\nConsole.writeLine(\"done\")", "done\n")]
    public void ExecutesScalarTemporaryBorrows(string name, string source, string output)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        ScalarEmissionTest.EmitFixture("ScalarTemporaryBorrow" + name, source, output);
    }

    [Theory]
    [InlineData("func keep(n: ref/i32) -> ref/i32 during n => n\nfunc one() -> i32 => 1\nlet r = keep(one())\nlet s = r")]
    [InlineData("func keep(n: ref/i32) -> ref/i32 during n => n\nlet r = keep(1)\nlet s = r")]
    [InlineData("func keep(p: (ref/i32 during a, ref/i32 during a)) -> ref/i32 during a => p.0\nlet x: i32 = 2\nlet r = keep((x@ref, 1))\nlet s = r")]
    [InlineData("func keep(p: (ref/(ref/i32 during a, i32) during a, i32)) -> ref/i32 during a => p.0.0\nlet x: i32 = 2\nlet r = keep(((x@ref, 1), 2))\nlet s = r")]
    [InlineData("func keep(p: (ref/i32 during a, ref/i32 during a)) -> ref/i32 during a => p.0\nlet x: i32 = 2\nlet f = keep\nlet r = f((x@ref, 1))\nlet s = r")]
    [InlineData("let a: ref/i32 = 1\nlet s = a")]
    [InlineData("let v: i32 = 2\nvar x: ref/i32 = v@ref\nx = 1\nlet s = x")]
    [InlineData("let c = true\nlet v: i32 = 2\nlet x: ref/i32 = if c => v@ref else => 1\nlet s = x")]
    [InlineData("let c = true\nlet x: ref/i32 = if c => 1 else => 2\nlet s = x")]
    [InlineData("let a: ref/(raw/i32) = null\nlet s = a")]
    public void RejectsReturnedBorrowOfTemporaryAfterStatement(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.False(c.Analysis.IsVerified);
        Assert.False(c.Generate(TextWriter.Null));
    }

    // SPEC 10.2 row 2 at an annotated initializer, its elements and an assignment source (PLAN G585): a literal at ref/U is its owner
    // temporary at U, borrowed for the statement; U supplies the literal's Type (the owner's O-201 (1) (a): i64) and an aggregate's
    // positions; a join holding a literal source is one temporary, its value (O-201 (2) (a)).
    [Theory]
    [InlineData("let a: ref/i32 = 1")]
    [InlineData("let x: (ref/i32, i32) = (1, 2)")]
    [InlineData("let x: [2 of ref/i32] = [2 of 1]")]
    [InlineData("let a: ref/i64 = 1")]
    [InlineData("let x: (ref/i64, i32) = (1, 2)")]
    [InlineData("let x: (ref/(i64, i64), i32) = ((1, 2), 3)")]
    [InlineData("let x: (ref/[2 of i32], i32) = ([1, 2], 3)")]
    [InlineData("let v: i32 = 2\nvar x: ref/i32 = v@ref\nx = 1")]
    [InlineData("let c = true\nlet x: (ref/i32, i32) = (if c => 1 else => 2, 3)")]
    [InlineData("let a: ref/(raw/i32) = null")]
    [InlineData("let c = true\nlet p: raw/i32 = null\nlet a: ref/(raw/i32) = if c => p else => null")]
    public void AcceptsTemporaryBorrowsAtAnnotatedInitializers(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        Assert.True(c.Generate(TextWriter.Null));
    }

    // SPEC 15.3.5, 15.3.6 (PLAN G585): a call's Origin fixed by invariance to a parameter's Origin `b` is judged against each borrowed
    // temporary's Place Origin, nested or through a function value: the temporary cannot outlive `b`. Through a function value the Loan
    // check also reports the stored temporary, as dev does for a nested typed element (`g(((n, 1), 2), w)`).
    [Theory]
    [InlineData("fe", Invariant + "    return fe((1, 2), w)\n", "UnsatisfiedOriginRelation_Kd: return fe((1, 2), w)")]
    [InlineData("fi", Invariant + "    return fi(((n, 1), 2), w)\n", "UnsatisfiedOriginRelation_Kd: return fi(((n, 1), 2), w)")]
    [InlineData("fe-value", Invariant + "    let g = fe\n    return g((1, 2), w)\n", "UnsatisfiedOriginRelation_Kd: return g((1, 2), w) | ComparisonLoanConflict_Kd: return g((1, 2), w)")]
    [InlineData("fe-n", Invariant + "    return fe((n, 2), w)\n", "")]
    public void JudgesATemporaryAgainstAnInvariantOrigin(string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines(form, program + "()", records);

    // SPEC 12.3.1, 14.9.2 step 3 (PLAN G585, K's K1-K3): at ref/U a literal source, or a join holding one, is fitted to U, so each
    // misfit is its own record at U (one at `s`, one at `1`; one at `300`, also beside a resting join); a fill and a mixed range take
    // nothing from U (4.3.3, 4.6.2 rule 2), at a let and at a call alike, while their own Type fits (the i32 controls): each Literal
    // part of a fill keeps its default at any depth (a Tuple, a join, a nested fill), and a literal-only boundary takes its typed
    // boundary's Type, `T` for `FromEnd<T>` (the FromEnd rows are valid).
    [Theory]
    [InlineData("string", Literals + "    let x: ref/string = if c => s else => 1\n", "TransferRequired_Kd: let x: ref/string = if c => s else => 1 | TypeMismatch_Kd: let x: ref/string = if c => s else => 1")]
    [InlineData("u8", Literals + "    let x: ref/u8 = if c => b else => 300\n", "InvalidNumericLiteral_Kd: let x: ref/u8 = if c => b else => 300")]
    [InlineData("fill", Literals + "    let x: ref/[2 of i64] = [2 of 1]\n", "TypeMismatch_Kd: let x: ref/[2 of i64] = [2 of 1]")]
    [InlineData("fill-call", Literals + "    let r = kf([2 of 1])\n", "NoApplicableOverload_Kd: let r = kf([2 of 1])")]
    [InlineData("range", Literals + "    let x: ref/Range<i64, i32> = 0..v\n", "TypeMismatch_Kd: let x: ref/Range<i64, i32> = 0..v")]
    [InlineData("range-call", Literals + "    let r = km(0..v)\n", "NoApplicableOverload_Kd: let r = km(0..v)")]
    [InlineData("controls", Literals + "    let x: ref/[2 of i32] = [2 of 1]\n    let r = kg([2 of 1]) + kn(0..w)\n", "")]
    [InlineData("fill-tuple-call", Composites + Literals + "    let r = kft([2 of (1, 2)])\n", "NoApplicableOverload_Kd: let r = kft([2 of (1, 2)])")]
    [InlineData("fill-join-call", Composites + Literals + "    let r = kf([2 of if c => 1 else => 2])\n", "NoApplicableOverload_Kd: let r = kf([2 of if c => 1 else => 2])")]
    [InlineData("fill-nested-call", Composites + Literals + "    let r = kff([2 of [2 of 1]])\n", "NoApplicableOverload_Kd: let r = kff([2 of [2 of 1]])")]
    [InlineData("fill-controls", Composites + Literals + "    let r = kfu([2 of (1, 2)]) + kf([2 of w]) + kg([2 of if c => 1 else => 2])\n", "")]
    [InlineData("fromend-call", Composites + Literals + "    let x: ref/Range<i32, FromEnd<i32>> = v..^1\n    let r = kfe(v..^1) + kcf(v..=^1) + kfs(^1..v)\n", "")]
    public void FitsALiteralSourceAtTheReferent(string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines(form, program + "    return 0\n()", records);

    // SPEC 10.2 row 2, 15.3.6 (PLAN G585): a function value's call takes the Place Origin of every borrowed temporary that waits for its
    // solve as an input of its Origins, as a direct call does: a fill, a mixed range or a Tuple literal at the root, a fill at an
    // element. Kept past its statement, the temporary is the Loan record.
    [Theory]
    [InlineData("fill", Values + "    let g = kv\n    let r = g([2 of 1], n)\n", "")]
    [InlineData("range", Values + "    let g = kr\n    let r = g(0..v, n)\n", "")]
    [InlineData("tuple", Values + "    let g = kt\n    let r = g((1, 2), n)\n", "")]
    [InlineData("element", Values + "    let g = ke\n    let r = g(([2 of 1], 3), n)\n", "")]
    [InlineData("fill-use", Values + "    let g = kv\n    let r = g([2 of 1], n)\n    let b: i32 = r\n", "ComparisonLoanConflict_Kd: let r = g([2 of 1], n)")]
    [InlineData("range-use", Values + "    let g = kr\n    let r = g(0..v, n)\n    let b: i32 = r\n", "ComparisonLoanConflict_Kd: let r = g(0..v, n)")]
    public void AFunctionValueCallBorrowsItsWaitingTemporaries(string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines(form, program + "    return 0\n()", records);

    [Theory]
    [InlineData("func bump(n: uniq/i32)\n    ()\nbump(1)")]
    [InlineData("func chooseBorrow(value: ref/i32) -> () => ()\nfunc chooseBorrow(value: ref/i64) -> () => ()\nchooseBorrow(1)")]
    [InlineData("func read(n: ref/u8) -> i32 => 0\nlet r = read(-1)")]
    [InlineData("func modify<T>(value: uniq/T) -> () => ()\nmodify(1)")]
    public void RejectsInvalidLiteralBorrows(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Generate(TextWriter.Null));
    }
}
