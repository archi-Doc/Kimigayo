// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Verification;
using Xunit;

namespace XunitTest;

public partial class ClosureResultOriginTest
{
    private const string Inspect = "func inspect(value: ref/i32) => ()\n";

    private const string Apply = "func apply(f: () -> i32) -> i32 => f()\n";

    private const string Generic = "func applyG<F>(f: ref/F) -> i32\n    F is Callable<() -> i32>\n    return 0\n";

    private const string Use = "func use()\n";

    private const string ApplyA = "    let a = outer(3)\n    let r = apply(a)\npublic func main() => ()\n";

    private const string GenericA = "    let a = outer(3)\n    let r = applyG(a@ref)\npublic func main() => ()\n";

    private const string RawUse = "func use(p: raw/i32)\n";

    private const string RawMaker = "    let outer = func (q: raw/i32)\n        unsafe\n            let r = (*q)@ref\n            return func [r] () -> i32 => r@follow\n";

    private const string FailMaker = "    let outer = func (x: i32) => func [x@ref] () -> i32 => x@follow\n";

    private const string FailLiteral = "func [x@ref] () -> i32 => x@follow";

    private const string Confined = "struct Item\n    public var value: i32 = 7\n    drop => Console.writeLine(\"drop\")\nfunc apply<F>(f: F) -> i32\n" +
        "    F is Callable<owner, () -> i32>\n        effect confined\n    return f@move()\npublic func main() => ()\nfunc entry() -> i32\n";

    private const string ConfinedF = "    let f = outer(3, Item.init())\n    return apply(f@move)\n";

    private const string NestedUniq = "func use() -> i32\n    var n = 1\n    let c = func [var n] ()\n";

    private const string NestedCall = "    c()\n    return 0\npublic func main() => ()\n";

    private const string Captured = "let n = 7\nlet values: [1 of ref/i32] = [n@ref]\nlet f = func [values] () => values[0]\n";

    [Theory]
    [InlineData("Array", Captured + "let first = f()\nlet second = f()\nrequire first == 7 and second == 7 else => $abort(\"result\")")]
    [InlineData("BorrowedClosure", Captured + "let borrowed = f@ref\nlet result = borrowed()\nrequire result == 7 else => $abort(\"result\")")]
    [InlineData("Direct", "let n = 7\nlet view = n@ref\nlet f = func [view] () => view\nlet result = f()\nrequire result == 7 else => $abort(\"result\")")]
    [InlineData("Slot", "let n = 7\nlet f = func [n@ref] () => n\nlet result = f()\nrequire result == 7 else => $abort(\"result\")")]
    [InlineData("Tuple", "let n = 7\nlet view = n@ref\nlet f = func [view] () => (view, 9)\nlet result = f()\nrequire result.0 == 7 and result.1 == 9 else => $abort(\"result\")")]
    [InlineData("Option", "let n = 7\nlet view = n@ref\nlet f = func [view] () => Option.Some(view)\nlet result = f()\nmatch result\n    .Some(let item)\n        require item == 7 else => $abort(\"result\")\n    .None => $abort(\"none\")")]
    [InlineData("Input", "func read(n: ref/i32) -> i32\n    let values: [1 of ref/i32] = [n]\n    let f = func [values] () => values[0]\n    let result = f()\n    return result\nlet n = 7\nrequire read(n@ref) == 7 else => $abort(\"result\")")]
    [InlineData("FreshArgument", "var n = 7\nlet view = n@ref\nlet f = func [view] (other: ref/i32) => view\nvar other = 9\nlet result = f(other@ref)\nother = 11\nrequire result == 7 and other == 11 else => $abort(\"result\")")]
    public void ConcreteResultsKeepFixedExternalOrigins(string name, string source)
        => ScalarEmissionTest.EmitFixture("ClosureResultOrigin" + name, source, string.Empty);

    [Theory]
    [InlineData("var n = 7\nlet values: [1 of ref/i32] = [n@ref]\nlet f = func [values] () => values[0]\nlet result = f()\nn = 9\nrequire result == 7 else => $abort(\"result\")")]
    [InlineData("var n = 7\nlet f = func [n@ref] () => n\nlet result = f()\nn = 9\nrequire result == 7 else => $abort(\"result\")")]
    public void TheReturnedViewProtectsTheOwnerAfterTheLastClosureUse(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Contains(c.Problems, x => x.Code.IsLoanConflict());
        Assert.DoesNotContain(c.Problems, x => x.Code == DiagnosticCode.Unsupported_Kd);
        Assert.False(c.Generate(TextWriter.Null));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ReturningAnExternalReferenceDoesNotRetainTheEnvironment()
        => NativeAllocationAudit.WriteFixture("ClosureResultOriginCleanup", "struct Item\n    public let id: i32 = 7\n    drop => Console.writeLine(\"drop\")\nlet owner = Kimi.Intrinsics.makeObj(Item.init())\nlet n = 7\nlet values: [1 of ref/i32] = [n@ref]\nlet f = func [values, owner@move] () => values[0]\nlet result = f@move()\nrequire result == 7 else => $abort(\"result\")", 1, 1, 20, "drop\n");

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void StoredObjectResultsKeepHeaderIdentityAndTheOwner()
        => NativeAllocationAudit.WriteFixture("ClosureResultOriginObject", "struct Item\n    public let id: i32 = 7\n    drop => Console.writeLine(\"drop\")\nlet owner = Kimi.Intrinsics.makeObj(Item.init())\nlet values: [1 of objref/Item] = [owner@objref]\nlet f = func [values] () => values[0]\nlet result = f()\nrequire result.id == 7 else => $abort(\"result\")", 1, 1, 20, "drop\n");

    // SPEC 15.8.2, 15.6.4, 7.7.3: a call's result over the closure's environment depends on the call receiver. Binding binds it with the
    // receiver's Origin (DECISIONS R4-M152), and the MIR judges it (MirBorrowsTest), so since the MIR decides acceptance (H5, R5-M182) each
    // program is accepted with no record and generated (1a, the owner's list of 2026-11-03; the old analysis's located Unsupported_Kd at the
    // call went with its gate): a borrow of an environment binding, through a reference receiver, a Reborrow through a captured exclusive
    // reference, a result beside a per-call input, a temporary closure, a Consuming call that moves a captured reference out, a returned
    // closure that borrows the environment, a call inside a closure argument (bound under a consultation: F4) and a nested Closure's call
    // in its maker's body, each call's result bound or used.
    [Theory]
    [InlineData("let n = 7\nlet f = func [n] () => n@ref\nlet r = f()")]
    [InlineData("let n = 7\nlet f = func [n] () => n@ref\nlet g = f@ref\nlet r = g()")]
    [InlineData("var n = 7\nlet view = n@uniq\nlet f = func [view] () => view@follow@ref\nlet r = f()")]
    [InlineData("func pick(a: ref/i32, b: ref/i32) -> ref/i32 => a\nlet n = 7\nlet f = func [n] (x: ref/i32) => pick(n@ref, x)\nlet m = 8\nlet r = f(m@ref)")]
    [InlineData("let n = 7\nlet r = (func [n] () => n@ref)()")]
    [InlineData("var n = 7\nlet view = n@uniq\nvar f = func [view] () => view\nlet r = f()")]
    [InlineData("func id<V>(x: V) -> V => x@move\nvar n = 7\nlet view = n@uniq\nvar f = func [view] () => id(view)\nlet r = f()")]
    [InlineData("func id<V>(x: V) -> V => x@move\nvar n = 7\nlet view = n@uniq\nvar f = func [view@move] () => id(view)\nlet r = f()")]
    [InlineData("var n = 7\nlet view = n@uniq\nlet take = func [view@move] () => view@move\nlet r = take@move()")]
    [InlineData("let n = 7\nlet outer = func [n] () => func [n@ref] () => n\nlet inner = outer()")]
    [InlineData("func apply(f: () -> i32) -> i32 => f()\nlet n: i32 = 4\nlet get = func [n] () => n@ref\nlet r = apply(func [get] () => get()@follow)")]
    [InlineData("var x: i32 = 1\nlet u = x@uniq\nvar outer = func [u] () -> i32\n    let i = func [u] () => u@move\n    let p = i@move()\n    p@follow = 2\n    return 0\n_ = outer()")]
    public void AResultOverTheEnvironmentIsAccepted(string source)
    {
        Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
        MinimalEmissionTest.GeneratedBodies(source, OutputKind.Application, accepted: true);
    }

    // Kept apart, not counted for G273 (PLAN G280; R4's G273 problem check K3). SPEC 15.8.2, 23.1.2 (DECISIONS R4-M152 P8, R5-M63): a
    // returned Closure literal that P8 fails, a maker's expression body at any nesting, keeps its definition: its record is at the literal,
    // as the block form's `return` places it, and the returned value stays callable, with no derived NotCallable_Kd or UnresolvedBinding_Kd
    // (review 2's B1: v2, a08, f03 = G273's p8_param_bad, f04, f07; each with its block-form twin). A reader reaching a closure through its
    // Type reads its definition (BoundClosure.Of), whatever its literal's creation outcome. The record is A5's one, at the literal in either
    // form (R7-O1 H; its twin at the literal or the block form's `return`, H5, R4-M108, went with Binding's). P8's relation is A5's
    // record, so Binding is complete, and P9 images the Closure's call result atom, a per-call Origin (SPEC 15.8.2), as its call's own
    // call result region in the calling body (B1; P9's Unbound image went with R7-O1 H): a value read in place, called after its `let` or
    // called directly (`outer(3)()`, whose inner call's result names that region, not the maker's atom: F1 is not reached) gives no
    // record beside the literal's (23.1.2; the direct call's located limit went with B1).
    [Theory]
    [InlineData("let outer = func (x: i32) => func [x@ref] () => x\nlet a = outer(3)\nlet r = a()", "func [x@ref] () => x")]
    [InlineData("let outer = func (x: i32)\n    return func [x@ref] () => x\nlet a = outer(3)\nlet r = a()", "func [x@ref] () => x")]
    [InlineData(Inspect + "let outer = func (x: i32) => func [x@ref] () => x\nlet a = outer(3)\ninspect(a())", "func [x@ref] () => x")]
    [InlineData(Inspect + "let outer = func (x: i32)\n    return func [x@ref] () => x\nlet a = outer(3)\ninspect(a())", "func [x@ref] () => x")]
    [InlineData("let outer = func (x: i32) => func (y: i32) => func [y@ref] () => y\nlet m = outer(3)\nlet a = m(4)\nlet r = a()", "func [y@ref] () => y")]
    [InlineData(
        "let outer = func (x: i32)\n    return func (y: i32) => func [y@ref] () => y\nlet m = outer(3)\nlet a = m(4)\nlet r = a()",
        "func [y@ref] () => y")]
    [InlineData(
        "let outer = func (x: i32)\n    return func (y: i32)\n        return func [y@ref] () => y\nlet m = outer(3)\nlet a = m(4)\nlet r = a()",
        "func [y@ref] () => y")]
    [InlineData("let outer = func (x: i32) => func [x@ref] () => x\nlet r = outer(3)()", "func [x@ref] () => x")]
    [InlineData("let outer = func (x: i32)\n    return func [x@ref] () => x\nlet r = outer(3)()", "func [x@ref] () => x")]
    public void AFailedReturnedLiteralKeepsItsDefinitionPendingG280(string source, string literal, string? use = null)
        => Assert.Equal($"{Unsatisfied} {literal}" + (use is null ? string.Empty : $"; {use}"), Records(source));

    // DECISIONS R4-M152 (round 3, BU's re-check 2): a returned value P8 failed whose result is `ref/i32`, passed to a generic
    // `F is Callable<() -> i32>`, keeps beside the record at the literal an independent NoApplicableOverload_Kd, since a valid Closure of
    // that signature gets it too (dev had a derived NotCallable_Kd); the literal's record is A5's one (R7-O1 H; its twin at the `return`,
    // H5, R4-M108, went with Binding's).
    [Fact]
    public void AFailedReturnedLiteralsCallableUseKeepsItsIndependentRecord()
        => Assert.Equal(
            "UnsatisfiedOriginRelation_Kd func [x@ref] () => x; NoApplicableOverload_Kd apply(a@ref)",
            Records("func apply<F>(f: ref/F) -> i32\n    F is Callable<() -> i32>\n    return 0\nlet outer = func (x: i32)\n    return func [x@ref] () => x\n" +
                "let a = outer(3)\nlet r = apply(a@ref)"));

    // SPEC 7.7.4, 8.11, 23.1.2 (DECISIONS R4-M152, R5-M63 (1)): a use that reaches a returned literal P8 failed through its Type reads the
    // closure's definition, so the bare expression body gives its parenthesized and block twins' records: no TransferRequired_Kd with an
    // inapplicable Repair at a `() -> i32` parameter (review 3's h1, BU's d3), no InvalidConstraint_Kd from the failed-context walk, where
    // the literal keeps its signature (h4, d4, and eff1, whose MIR keeps its independent UnsatisfiedEffectBound_Kd). The literal's record
    // is A5's one (R7-O1 H; its twin at the literal or the block form's `return`, H0b §5.4, R4-M108, went with Binding's), and eff1's
    // UnsatisfiedEffectBound_Kd is published at `f@move` (R5-M184). B1 (C-488; SPEC 15.8.2, 15.6.4): P9 images the Closure's call result
    // atom in the environment's Borrow as the call's own call result region, which has no source: `apply(a)`'s Owned proof over it is
    // Unknown, never Refuted (8.3.1), so the candidate stays applicable (10.1) and the erasure's Constraint failure is recorded after
    // selection at the argument, `UnprovenConstraint_Kd a` (BU-107; R1-a reads the complete region at once; F's R1-a ruling 1 (e), the
    // Middle's agreement on C-488: dev's NoApplicableOverload_Kd at the call came from the maker's atom refuted at step 5, another body's
    // root there); `applyG(a@ref)`'s premise bound relates a region of the calling body, so it gives no record beside the literal's
    // (23.1.2; dev's located limit at `a@ref`, the view's row 9, went with B1).
    [Theory]
    [InlineData(Apply + Use + "    let outer = func (x: i32) => func [x@ref] () -> i32 => x@follow\n" + ApplyA, "func [x@ref] () -> i32 => x@follow", "UnprovenConstraint_Kd a", null)]
    [InlineData(Apply + Use + "    let outer = func (x: i32) => (func [x@ref] () -> i32 => x@follow)\n" + ApplyA, "(func [x@ref] () -> i32 => x@follow)", "UnprovenConstraint_Kd a", null)]
    [InlineData(Apply + Use + "    let outer = func (x: i32)\n        return func [x@ref] () -> i32 => x@follow\n" + ApplyA, "func [x@ref] () -> i32 => x@follow", "UnprovenConstraint_Kd a", null)]
    [InlineData(Apply + Use + "    let outer = func (x: i32) => func [x@ref] () => x@follow\n" + ApplyA, "func [x@ref] () => x@follow", "UnprovenConstraint_Kd a", null)]
    [InlineData(Apply + Use + "    let outer = func (x: i32)\n        return func [x@ref] () => x@follow\n" + ApplyA, "func [x@ref] () => x@follow", "UnprovenConstraint_Kd a", null)]
    [InlineData(Generic + Use + "    let outer = func (x: i32) => func [x@ref] () -> i32 => x@follow\n" + GenericA, "func [x@ref] () -> i32 => x@follow", null, null)]
    [InlineData(Generic + Use + "    let outer = func (x: i32) => (func [x@ref] () -> i32 => x@follow)\n" + GenericA, "(func [x@ref] () -> i32 => x@follow)", null, null)]
    [InlineData(Generic + Use + "    let outer = func (x: i32)\n        return func [x@ref] () -> i32 => x@follow\n" + GenericA, "func [x@ref] () -> i32 => x@follow", null, null)]
    [InlineData(Generic + Use + "    let outer = func (x: i32) => func [x@ref] () => x@follow\n" + GenericA, "func [x@ref] () => x@follow", null, null)]
    [InlineData(Generic + Use + "    let outer = func (x: i32)\n        return func [x@ref] () => x@follow\n" + GenericA, "func [x@ref] () => x@follow", null, null)]
    [InlineData(
        Confined + "    let outer = func (x: i32, item: Item) => func [item@move, x@ref] () -> i32 => item.value\n" + ConfinedF,
        "func [item@move, x@ref] () -> i32 => item.value",
        null,
        "f@move")]
    [InlineData(
        Confined + "    let outer = func (x: i32, item: Item)\n        return func [item@move, x@ref] () -> i32 => item.value\n" + ConfinedF,
        "func [item@move, x@ref] () -> i32 => item.value",
        null,
        "f@move")]
    public void AUseOfAFailedReturnedLiteralReadsItsDefinition(string source, string literal, string? use, string? effect)
    {
        var expected = $"{Unsatisfied} {literal}" +
            (use is null ? string.Empty : $"; {use}") +
            (effect is null ? string.Empty : $"; {nameof(DiagnosticCode.UnsatisfiedEffectBound_Kd)} {effect}");
        Assert.Equal(expected, Records(source));

        var c = VerifiedSpecExampleTest.Bind(source);
        c.Analyze();
        var effects = MirTargetSide.MirSide(c).Where(static x => x.Requirement?.Partition != DiagnosticPartition.Binding && x.Code == DiagnosticCode.UnsatisfiedEffectBound_Kd);
        Assert.Equal(effect is null ? [] : [effect], effects.Select(x => source.Substring(x.Node.Span.Start, x.Node.Span.Length)));
    }

    // SPEC 5.2.2, 15.8.2 (R7-O1 H; the Front's review C1; R7-M66 Open r01, closed by B1): a raw Place's Borrow `(*q)@ref` captured by a
    // returned literal is valid (its Origin is fitted to its destination and may be static); P8's BodyLocalIn leaves its Anchor out, so the
    // published result names the Closure's call result atom, which each call images as its own call result region in the calling body
    // (B1), and the use `applyG(a@ref)` relates that region there: accepted (dev's located limit at `a@ref`, the view's row 9, went).
    [Fact]
    public void ARawPlaceBorrowInAReturnedEnvironmentFitsItsUse()
        => Assert.Equal(
            string.Empty,
            Records(Generic + "func use(p: raw/i32)\n    let outer = func (q: raw/i32)\n        unsafe\n            let r = (*q)@ref\n" +
                "            return func [r] () -> i32 => r@follow\n    let a = outer(p)\n    let r = applyG(a@ref)\npublic func main() => ()\n"));

    // B1 (C-488's coverage rows B6, B7; SPEC 15.8.2, 15.6.4, 15.2.3, 10.1, 23.1.2; the owner's O-523 (a)): each call of a maker images its
    // per-call result Origin as its own region, so a holder fitted twice is a fit that holds (f_raw_two, valid; f_g_var and f_g_twice give
    // the literal's record alone). The region has no source: through a `let` holder or as a direct argument, `apply`'s erasure is Unknown,
    // the candidate applicable and the Constraint failure recorded at the argument (d_raw_let, d_direct, d_raw_direct; R1-a); with two
    // candidates no one is strictly best (x_pair_let: AmbiguousBinding_Kd; dev accepted it, the maker's atom refuting the erasure). A `var`
    // holder's region may still be refuted by a later assignment, so applicability waits for the body: PLAN G1030's located limit at the
    // argument (d_var, d_raw_var; dev's NoApplicableOverload_Kd at the call, the owner's O-523 (a)).
    [Theory]
    [InlineData("f_raw_two", Generic + RawUse + RawMaker + "    var a = outer(p)\n    a = outer(p)\n    let s = applyG(a@ref)\n" + MainUnit, "")]
    [InlineData("f_g_var", Generic + Use + FailMaker + "    var a = outer(3)\n    a = outer(4)\n    let r = applyG(a@ref)\n" + MainUnit, Unsatisfied + " " + FailLiteral)]
    [InlineData("f_g_twice", Generic + Use + FailMaker + "    let a = outer(3)\n    let b = outer(4)\n    let r = applyG(a@ref)\n    let t = applyG(b@ref)\n" + MainUnit, Unsatisfied + " " + FailLiteral)]
    [InlineData("d_raw_let", Apply + RawUse + RawMaker + "    let a = outer(p)\n    let r = apply(a)\n" + MainUnit, "UnprovenConstraint_Kd a")]
    [InlineData("d_direct", Apply + Use + FailMaker + "    let r = apply(outer(3))\n" + MainUnit, Unsatisfied + " " + FailLiteral + "; UnprovenConstraint_Kd outer(3)")]
    [InlineData("d_raw_direct", Apply + RawUse + RawMaker + "    let r = apply(outer(p))\n" + MainUnit, "UnprovenConstraint_Kd outer(p)")]
    [InlineData("x_pair_let", "func run(f: () -> i32) -> i32 => 0\nfunc run<F>(f: F) -> i32 => 1\n" + RawUse + RawMaker + "    let a = outer(p)\n    let n = run(a)\n" + MainUnit, "AmbiguousBinding_Kd run(a)")]
    [InlineData("d_var", Apply + Use + FailMaker + "    var a = outer(3)\n    let r = apply(a)\n" + MainUnit, Unsatisfied + " " + FailLiteral + "; Unsupported_Kd a")]
    [InlineData("d_raw_var", Apply + RawUse + RawMaker + "    var a = outer(p)\n    let r = apply(a)\n" + MainUnit, "Unsupported_Kd a")]
    public void AMakersCallResultIsItsCallsOwnRegion(string form, string source, string records)
        => Assert.True(records == Records(source), $"{form}: expected [{records}], found [{Records(source)}]");

    // SPEC 7.7.3, 23.1.2 (DECISIONS R4-M152): the receiver classification reads a nested Closure's definition, so a nested literal whose
    // erasure fails (its creation failed) still makes its maker Exclusive through its `n@uniq` capture: `c()` through a `let` is
    // InvalidAssignment_Kd at `c`, the independent record its created twin has (BU's zz3_n2 and zz3_n2ok).
    [Theory]
    [InlineData(
        "func run(f: () -> ()) => f()\n" + NestedUniq + "        run(func [n@uniq] () => ())\n" + NestedCall,
        "UnprovenConstraint_Kd func [n@uniq] () => (); InvalidAssignment_Kd c")]
    [InlineData(NestedUniq + "        let k = func [n@uniq] () => ()\n        k()\n" + NestedCall, "InvalidAssignment_Kd c")]
    public void ANestedClosureWhoseCreationFailedStillClassifiesItsMaker(string source, string records)
    {
        Assert.Equal(records, Records(source));
    }

    // SPEC 15.8.2, 13.5.6 (DECISIONS R4-M152): a Closure literal's own temporary is its ClosureValueSlot atom, the receiver Origin ρ of a
    // call on the literal, so a Borrow of it kept past its statement is the Loan conflict at its storage end (its slot 0 read as the
    // literal's parameter); used within its statement it is valid. Since the MIR decides (H5, R5-M182), the one record is A5's, at the
    // temporary whose storage ends (the old analysis placed it at the statement).
    [Theory]
    [InlineData(Inspect + "let n: i32 = 1\nlet r = (func [n] () => n)@ref\ninspect(r()@ref)", "ComparisonLoanConflict_Kd (func [n] () => n)")]
    [InlineData(Inspect + "let n: i32 = 1\ninspect((func [n] () => n)@ref()@ref)", "")]
    public void AClosureLiteralsTemporaryIsItsReceiverAtom(string source, string records)
    {
        var found = DiagnosticCorpus.Check(source).Diagnostics.Select(x => $"{x.Code} {source.Substring(x.Span!.Value.Start, x.Span.Value.Length)}");
        Assert.Equal(records, string.Join("; ", found));
    }

    [Fact]
    public void CommonFunctionErasureCannotHideAnExternalOrigin()
    {
        var c = MinimalEmissionTest.Analyze(Captured + "let erased: () -> ref/i32 = f\nerased()");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Generate(TextWriter.Null));
    }

    // SPEC 7.7.4, 15.6.1, 23.1: a common Function value cannot return a borrow of its hidden environment receiver, so the erasure of
    // a closure whose result borrows its environment is one UnprovenOriginContract_Kd at the converted value, whose longer end is the
    // closure display "call receiver", related at the anonymous function's header. It was displayed as a borrow of the closure's text.
    [Theory]
    [InlineData("    let n = 7\n    let f = func [n] () => n@ref\n    let e: () -> ref/i32 = f\n", "f")]
    [InlineData("    let text = \"abc\"\n    let f = func [text@move] () => text@ref\n    let e: () -> ref/string = f@move\n", "f@move")]
    public void AnErasedResultCannotBorrowTheCallReceiver(string body, string text)
    {
        var source = "public func main() -> ()\n" + body;
        var output = DiagnosticCorpus.Check(source);
        var error = Assert.Single(output.Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenOriginContract_Kd), text), (error.Code, error.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty));
        var header = Assert.Single(error.Related!, static x => x.Role == "origin").Span!.Value;
        Assert.StartsWith("func [", source.Substring(header.Start, header.Length), StringComparison.Ordinal);
        Assert.EndsWith("] ()", source.Substring(header.Start, header.Length), StringComparison.Ordinal);
        Assert.Equal("A common Function value cannot return a borrow of its hidden environment receiver (SPEC 7.7.4)", error.Note);
        var result = new DiagnosticResult(output.Diagnostics, output.Sources);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("the result requires the closure's call receiver outlives static, which is not proven", console.Text, StringComparison.Ordinal);
        var json = JsonSerializer.Serialize(result, DiagnosticJsonContext.Default.DiagnosticResult);
        Assert.Contains("{\"name\":\"longer\",\"kind\":\"Origin\",\"value\":\"call receiver\",\"elided\":false,\"origin\":\"closure\"}", json, StringComparison.Ordinal);
    }

    [Fact]
    public void AReturnedViewCannotOutliveItsExternalOwner()
    {
        var c = MinimalEmissionTest.Analyze("func bad() -> ref/i32 during static\n    let n = 7\n    let values: [1 of ref/i32] = [n@ref]\n    let f = func [values] () => values[0]\n    return f()\nbad()");
        // Binding judges no body relation, so it is complete (R7-O1 H, F-H1-5); A5's record rejects the program and nothing is generated.
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Problems, static x => x.Code == DiagnosticCode.UnsatisfiedOriginRelation_Kd);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Failure == BindingFailure.Unsupported);
        Assert.False(c.Generate(TextWriter.Null));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmBindingAnalysisAndEmissionReuseFixedContracts()
    {
        var c = MinimalEmissionTest.Analyze(Captured + "let result = f()\nrequire result == 7 else => $abort(\"result\")");
        var expected = CompilationTestHelper.WriteIr(c);
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid);
        Assert.Equal(expected, CompilationTestHelper.WriteIr(c));
    }

    // A program's published records in source order, each as its code and its span's text.
    private static string Records(string source)
        => string.Join("; ", ContextualCaptureTest.PublishedRecords(source).OrderBy(static x => x.Span.Start)
            .Select(x => $"{x.Code} {source.Substring(x.Span.Start, x.Span.Length)}"));
}
