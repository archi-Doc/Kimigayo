// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 10.8, 15.3.6, 15.6.1: Type-argument inference solves a slot's structure and Semantics; Origin evidence for a slot never makes a
// candidate inapplicable. An explicit Type argument is kept, an invariant binding of the slot is kept over covariant ones (the first
// invariant one over later ones, in either argument order), and covariant bindings meet; each argument's Origin relation to the solution
// is then judged after selection at that argument. An independently known expected result fills the still-unbound slots structurally.
public class TypeArgumentOriginEvidenceTest
{
    private const string Main = "public func main() => ()\n";

    private const string Put = "func put<T>(slot: uniq/T, v: T) -> ()\n    slot@follow = v@move\n";

    private const string PutReversed = "func put<T>(v: T, slot: uniq/T) -> ()\n    slot@follow = v@move\n";

    // An explicit Type argument, a covariant meet of two inputs and an expected result that fills the slot.
    private const string Valid = "func id<T>(x: T) -> T => x@move\nfunc first<T>(a: T, b: T) -> T => a@move\nfunc make<T>(n: i32) -> Option<ref/T during static> => .None\n" +
        "func pick(x: ref/i32, y: ref/i32) -> i32\n    let s = id<ref/i32 during x>(x)\n    let r = first(x, y)\n    let o: Option<ref/i32 during y> = make(1)\n    match o\n        .Some(let v) => return 0\n        .None => ()\n    return s@follow + r@follow\n";

    // q11: the explicit Type argument is kept; it was NoApplicableOverload_Kd.
    [Fact]
    public void AnExplicitTypeArgumentIsKept()
    {
        var source = "func id<T>(x: T) -> T => x@move\nfunc caller(p: ref/i32) -> i32 => id<ref/i32 during static>(p)@follow\n" + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "p", "requires p outlives static, which is not proven"), (error.Code, Text(source, error.Span), error.Label));
        Assert.Equal(("fit", "ref/i32 during static"), (error.Reason![3].Value, error.Reason[4].Value));
    }

    // q12: the first invariant binding is kept, so only the second argument fails, as one `==` record that relates the first; it was a
    // meet `(x and y)` reported at both arguments.
    [Fact]
    public void TheFirstInvariantBindingIsKept()
    {
        var source = "func same<T>(a: uniq/T, b: uniq/T) -> () => ()\nfunc caller(p: uniq/(ref/i32 during x), q: uniq/(ref/i32 during y), x: ref/i32, y: ref/i32) -> ()\n    same(p, q)\n" + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "q", "requires y == x, which is not proven"), (error.Code, Text(source, error.Span), error.Label));
        Assert.Equal("==", error.Reason![0].Value);
        var related = Assert.Single(error.Related!);
        Assert.Equal(("relation", "p"), (related.Role, Text(source, related.Span)));
    }

    // g1/g2: an invariant binding wins over a covariant one in either argument order, so the borrow of the local is the value that does
    // not fit; it was a meet `x == (x and local)` reported at the borrow.
    [Theory]
    [InlineData(Put, "put(slot@uniq, local@ref)")]
    [InlineData(PutReversed, "put(local@ref, slot@uniq)")]
    public void AnInvariantBindingWinsOverACovariantOne(string declaration, string call)
    {
        var source = declaration + "func use(x: ref/i32) -> i32\n    var slot: ref/i32 during x = x\n    if x@follow > 0\n        let local: i32 = 5\n        " + call + "\n    return slot@follow\n" + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "local@ref", "requires the borrow local@ref outlives x, which is false"), (error.Code, Text(source, error.Span), error.Label));
    }

    // q20: either argument order stores the new Borrow into the same local region.
    [Theory]
    [InlineData(Put, "put(slot@uniq, b@ref)")]
    [InlineData(PutReversed, "put(b@ref, slot@uniq)")]
    public void AStoreIntoABodyOriginExecutes(string declaration, string call)
    {
        var source = declaration + "public func main() -> ()\n    let a = 1\n    let b = 2\n    var slot = a@ref\n    " + call + "\n    Console.writeLine(\"\\(slot@follow)\")\n";
        ScalarEmissionTest.EmitFixture(declaration == Put ? "LocalRegionPut" : "LocalRegionPutReversed", source, "2\n");
    }

    // q33: an independently known expected result fills a still-unbound slot structurally; `make(1)` was the fact-less
    // UnprovenConstraint_Kd.
    [Fact]
    public void AnExpectedResultFillsTheSlotStructurally()
    {
        var source = "func get<T>(n: ref/T during static) -> ref/T during static => n\nfunc caller(p: ref/i32, q: ref/i32 during static) -> ref/i32 during p => get(q)\n" +
            "func make<T>(n: i32) -> Option<ref/T during static> => .None\nfunc caller2(p: ref/i32) -> Option<ref/i32 during p> => make(1)\n" + Main;
        Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
        ScalarEmissionTest.EmitFixture("TypeArgumentOriginExpected", "func make<T>(n: i32) -> Option<ref/T during static> => .None\nfunc caller2(p: ref/i32) -> Option<ref/i32 during p> => make(1)\nlet a: i32 = 3\nmatch caller2(a@ref)\n    .Some(let v) => $abort(\"some\")\n    .None => ()", string.Empty);
    }

    // SPEC 10.8, 15.6.1: Constraints still take part in applicability with the solution: the first equality (static) satisfies Owned,
    // and the second argument's `==` is the judged relation.
    [Fact]
    public void ConstraintsUseTheFirstInvariantBinding()
    {
        var source = "func pin<T>(a: uniq/T, b: uniq/T) -> i32\n    T is Owned\n    return 1\nfunc caller(s: uniq/(ref/i32 during static), p: uniq/(ref/i32 during x), x: ref/i32) -> i32 => pin(s, p)\n" + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "p", "requires x == static, which is not proven"), (error.Code, Text(source, error.Span), error.Label));
        Assert.Equal(("relation", "s"), (Assert.Single(error.Related!).Role, Text(source, error.Related![0].Span)));
    }

    [Fact]
    public void ValidSolutionsRun()
        => ScalarEmissionTest.EmitFixture("TypeArgumentOriginValid", Valid + "let a: i32 = 7\nlet b: i32 = 8\nrequire pick(a@ref, b@ref) == 14 else => $abort(\"valid\")", string.Empty);

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmTypeArgumentOriginEvidenceAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(Valid + "let a: i32 = 7\nlet b: i32 = 8\nrequire pick(a@ref, b@ref) == 14 else => $abort(\"warm\")");
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }

    private static string Text(string source, SourceSpan? span) => span is { } value ? source.Substring(value.Start, value.Length) : string.Empty;
}
