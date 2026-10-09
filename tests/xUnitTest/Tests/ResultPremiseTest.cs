// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 15.3.7, 15.6.1, 15.6.5: a signature's result-Type well-formedness is a premise of its definition and an obligation at each use.
// A call solves its fresh Origins under the premise, so a result over body-local borrows is bounded by every Origin it holds; where an
// argument pins an Origin so that no solution satisfies it, the substituted result's relation is a wellFormed record at the call.
public class ResultPremiseTest
{
    private const string Main = "public func main() => ()\n";

    private const string Holder = "struct Holder {a}\n    public let item: ref/i32 during a\n\n    public init(item: ref/i32 during a) => self.item = item\n\n";

    // The SPEC 15.6.5 example: q outlives p is the result's well-formedness, so only the clause for the returned value is written.
    private const string PickRelated = "func pickRelated(p: ref/Holder, q: ref/i32) -> ref/(ref/i32 during q) during p\n    origin p.a outlives q\n    return p.item@ref\n\n";

    private const string Leak = "func leak(p: ref/Holder, q: ref/i32) -> (ref/(ref/i32 during q) during p, ref/i32 during p)\n    origin p.a outlives q\n    return (p.item@ref, q)\n\n";

    private const string PickInto = "func pickInto(p: ref/Holder, q: ref/i32, keep: uniq/(ref/i32 during p)) -> ref/(ref/i32 during q) during p\n    origin p.a outlives q\n    return p.item@ref\n\n";

    private const string Pinned = "func caller(h: ref/Holder during s, x: ref/i32, keep: uniq/(ref/i32 during s)) -> i32\n    origin h.a outlives x\n    let r = pickInto(p: h, q: x, keep: keep)\n    return r@follow@follow\n";

    // The body returns q at `during p` under the premise q outlives p; every use of the Function Item must prove it.
    private const string LeakItem = "func leak(p: ref/i32, q: ref/i32) -> (Option<ref/(ref/i32 during q) during p>, ref/i32 during p)\n    return (.None, q)\n\n";

    private const string LeakEscape = "func escape(x: ref/i32) -> ref/i32 during x\n    let local: i32 = 99\n    let fr = leak\n    let pair = fr(x, local@ref)\n    return pair.1\n";

    private const string Pin = "func pin(p: ref/i32) -> ref/i32 during static\n    origin p outlives static\n    return p\n\n";

    // Every Item use path that is valid under the premise or the clause: value calls through the Item and a reference to it, both
    // conversions to a common Function Type, and a Callable argument. The top-level result part keeps the Loan on `other`.
    private const string ItemUses = LeakItem +
        "func leakWritten(p: ref/i32, q: ref/i32) -> (Option<ref/(ref/i32 during q) during p>, ref/i32 during p)\n    origin q outlives p\n    return (.None, q)\n\n" +
        "func apply<F>(f: ref/F, x: ref/i32, y: ref/i32) -> i32\n    F is Callable<(ref/i32, ref/i32) -> (Option<ref/(ref/i32)>, ref/i32)>\n    return f(x, y).1@follow\n\n" +
        "func run(x: ref/i32) -> i32\n    let local: i32 = 5\n    let fr = leak\n    let g = fr@ref\n    let written = leakWritten\n" +
        "    let erased: (ref/i32, ref/i32) -> (Option<ref/(ref/i32)>, ref/i32) = fr\n    let referenced: (ref/i32, ref/i32) -> (Option<ref/(ref/i32)>, ref/i32) = leak\n" +
        "    return fr(x, local@ref).1@follow + g(x, local@ref).1@follow + written(x, local@ref).1@follow + erased(x, local@ref).1@follow + referenced(x, local@ref).1@follow + apply(leak@ref, x, local@ref)\n\n" +
        "let a: i32 = 7\nvar other: i32 = 3\nlet top = leak\nlet pair = top(a@ref, other@ref)\nlet second = pair.1\n" +
        "require second@follow == 3 else => $abort(\"second\")\nConsole.writeLine(\"\\(run(a@ref)) \\(second@follow)\")\n";

    // A result whose borrow layer stores a Function Type: the per-call input of `(ref/i32) -> ref/i32` is bound by that Type.
    private const string ActionBox = "func ident(x: ref/i32) -> ref/i32\n    return x\n\nstruct Box\n    public let action: (ref/i32) -> ref/i32\n\n" +
        "    public init(action: (ref/i32) -> ref/i32) => self.action = action@move\n\nfunc pick(b: ref/Box) -> ref/((ref/i32) -> ref/i32) during b\n    return b.action@ref\n\n";

    // Generic Function Items called as values, whose entries return an Option or a struct over a borrow: without a condition, under the
    // result premise, under the written clause, and erased to a common Function Type.
    private const string GenericItems = "struct Pair<T>\n    public let first: T\n    public let second: Option<T>\n\n" +
        "    public init(first: T, second: Option<T>)\n        self.first = first@move\n        self.second = second@move\n\n" +
        "func pairOf<T>(p: ref/T, q: ref/T) -> Pair<ref/T during p>\n    return Pair<ref/T during p>.init(first: p, second: .Some(p))\n\n" +
        "func idg<T>(p: ref/T, q: ref/T) -> (Option<ref/T during p>, ref/T during p)\n    return (.None, p)\n\n" +
        "func leakg<T>(p: ref/T, q: ref/T) -> (Option<ref/(ref/T during q) during p>, ref/T during p)\n    return (.None, q)\n\n" +
        "func leakw<T>(p: ref/T, q: ref/T) -> (Option<ref/(ref/T during q) during p>, ref/T during p)\n    origin q outlives p\n    return (.None, q)\n\n" +
        "func run(x: ref/i32) -> i32\n    let local: i32 = 5\n    let plain = idg<i32>\n    let premise = leakg<i32>\n    let written = leakw<i32>\n" +
        "    let erased: (ref/i32, ref/i32) -> (Option<ref/(ref/i32)>, ref/i32) = premise\n    let make = pairOf<i32>\n    let made = make(x, local@ref)\n" +
        "    let extra = match made.second\n        .Some(let r) => r@follow\n        .None => 0\n" +
        "    return plain(x, local@ref).1@follow + premise(x, local@ref).1@follow + written(x, local@ref).1@follow + erased(x, local@ref).1@follow + made.first@follow + extra\n\n" +
        "let a: i32 = 7\nConsole.writeLine(\"\\(run(a@ref))\")\n";

    // A result Tuple whose Function Type part the premise skips, and a result that is a well-formed Function Type.
    private const string FunctionPart = "func twice(x: ref/i32) -> i32\n    return x@follow * 2\n\n" +
        "func both(p: ref/i32, q: ref/i32, f: (ref/i32) -> i32) -> (Option<ref/(ref/i32 during q) during p>, ref/i32 during p, (ref/i32) -> i32)\n    return (.None, q, f@move)\n\n" +
        "func g0(q: ref/i32) -> Option<ref/(ref/i32 during static) during static>\n    return .None\n\n" +
        "func get() -> (ref/i32) -> Option<ref/(ref/i32 during static) during static>\n    return g0\n\n" +
        "func run(x: ref/i32) -> i32\n    let local: i32 = 5\n    let triple = both(x, local@ref, twice)\n    let g = get()\n" +
        "    let none = match g(local@ref)\n        .Some(let r) => r@follow@follow\n        .None => 0\n    return triple.1@follow + triple.2(x) + none\n\n" +
        "let a: i32 = 7\nvar other: i32 = 3\nlet top = both(a@ref, other@ref, twice)\nlet second = top.1\n" +
        "require second@follow == 3 else => $abort(\"second\")\nConsole.writeLine(\"\\(run(a@ref)) \\(second@follow)\")\n";

    // A premise over the result-only signature Origin `s` bounds it at the call, as the written clause does.
    private const string ResultOnly = "func f(q: ref/i32) -> (Option<ref/(ref/i32 during q) during s>, ref/i32 during s)\n    return (.None, q)\n\n";

    // Meets at the longer end of relations that ownership analysis judges: a join, a Type occurrence, a part of a call result under
    // written clauses or under the result premise, and two premises or a meet argument at a call that pins the outer Origin.
    private const string JoinMeet = "func g(c: bool, a: ref/i32, b: ref/i32, x: ref/i32) -> i32\n    let t = if c => a else => b\n    let k: ref/i32 during x = t\n    return k@follow\n";

    private const string TypeMeet = "func g(a: ref/i32, b: ref/i32, x: ref/i32) -> i32\n    let k: Option<ref/(ref/i32 during (a and b)) during x> = .None\n    return 0\n";

    private const string ClauseCallee = "func f(p: ref/i32, q: ref/i32, r: ref/i32) -> (ref/i32 during q, i32)\n    origin p outlives q\n    origin r outlives q\n    return (q, 0)\n\n";

    private const string ClauseMeet = ClauseCallee + "func g(a: ref/i32, x: ref/i32, b: ref/i32) -> i32\n    let t = f(a, x, b)\n    let k: ref/i32 during x = t.0\n    return k@follow\n";

    private const string PremiseMeet = Holder +
        "func f(p: ref/Holder, q: ref/i32, r: ref/Holder) -> (Option<ref/(ref/i32 during p.a) during q>, Option<ref/(ref/i32 during r.a) during q>, ref/i32 during q)\n    return (.None, .None, p.item)\n\n" +
        "func g(x: ref/i32, h: ref/Holder, j: ref/Holder) -> i32\n    let t = f(h, x, j)\n    let k: ref/i32 during x = t.2\n    return k@follow\n";

    private const string TwoPremises = "func pk(p: ref/i32, q: ref/i32, r: ref/i32, keep: uniq/(ref/i32 during p)) -> (Option<ref/(ref/i32 during q) during p>, Option<ref/(ref/i32 during r) during p>)\n    return (.None, .None)\n\n" +
        "func caller(x: ref/i32, y: ref/i32, h: ref/i32 during s, keep: uniq/(ref/i32 during s)) -> i32\n    let r = pk(h, x, y, keep)\n    return 0\n";

    private const string MeetArgument = ClauseCallee + "func pk(p: ref/i32, q: ref/i32, keep: uniq/(ref/i32 during p)) -> Option<ref/(ref/i32 during q) during p>\n    return .None\n\n" +
        "func caller(a: ref/i32, x: ref/i32, b: ref/i32, h: ref/i32 during s, keep: uniq/(ref/i32 during s)) -> i32\n    let t = f(a, x, b)\n    let r = pk(h, t.0, keep)\n    return 0\n";

    // SPEC 15.6.5: pickRelated is valid and runs; the call's result keeps the Loans of both borrows, so writing either referent while
    // it lives is a Loan conflict.
    [Fact]
    public void TheSpecExampleRunsAndKeepsBothLoans()
    {
        var source = Holder + PickRelated + "var value: i32 = 7\nlet h = Holder.init(item: value@ref)\nvar other: i32 = 3\nlet r = pickRelated(p: h@ref, q: other@ref)\n" +
            "require r@follow@follow == 7 else => $abort(\"r\")\nConsole.writeLine(\"\\(r@follow@follow)\")\n";
        ScalarEmissionTest.EmitFixture("ResultPremisePickRelated", source, "7\n");
        foreach (var write in new[] { "other = 4\n", "value = 8\n" })
        {
            var invalid = MinimalEmissionTest.Analyze(source.Replace("require r@follow@follow", write + "require r@follow@follow", StringComparison.Ordinal));
            Assert.True(invalid.Binding.Result.IsComplete, MinimalEmissionTest.Describe(invalid, null));
            Assert.Contains(invalid.Ownership.Issues, static issue => issue.Failure == OwnershipFailure.ComparisonLoanConflict);
        }
    }

    // SPEC 15.6.4: the body may return q at `during p` under the premise, so the call's outer Origin is solved as the meet with the
    // inner one; the Tuple part typed by it keeps the inner borrow's Loan after the Tuple is taken apart.
    [Fact]
    public void AResultPartOverTheOuterOriginKeepsTheInnerLoan()
    {
        var source = Holder + Leak + "let value: i32 = 7\nlet h = Holder.init(item: value@ref)\nvar other: i32 = 3\nlet pair = leak(p: h@ref, q: other@ref)\nlet second = pair.1\n" +
            "require second@follow == 3 else => $abort(\"second\")\nConsole.writeLine(\"\\(second@follow)\")\n";
        ScalarEmissionTest.EmitFixture("ResultPremiseLeakPart", source, "3\n");
        var invalid = MinimalEmissionTest.Analyze(source.Replace("require second@follow", "other = 5\nrequire second@follow", StringComparison.Ordinal));
        Assert.True(invalid.Binding.Result.IsComplete, MinimalEmissionTest.Describe(invalid, null));
        Assert.Contains(invalid.Ownership.Issues, static issue => issue.Failure == OwnershipFailure.ComparisonLoanConflict);

        // A fixed caller cannot pass that part off as bounded by the outer input alone: its return is the fit failure.
        var escape = Holder + Leak + "func escape(h: ref/Holder, x: ref/i32) -> ref/i32 during h\n    origin h.a outlives x\n    return leak(p: h, q: x).1\n" + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(escape).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "leak(p: h, q: x).1", "fit"), (error.Code, Text(escape, error.Span), error.Reason![3].Value));
        Assert.Equal("requires x outlives h, which is not proven", error.Label); // The failing operand of the meet (h and x and h.a).
    }

    // SPEC 15.3.7: a method's premise may name its container's slot; the call relates the receiver Type's argument instead, so the
    // result of viaSlot over `other` keeps that Loan.
    [Fact]
    public void AMethodPremiseUsesTheReceiverTypesSlots()
    {
        const string methods = "struct Holder {a}\n    public let item: ref/i32 during a\n\n    public init(item: ref/i32 during a) => self.item = item\n\n" +
            "    public func pickWith(self: ref/Self, q: ref/i32) -> ref/(ref/i32 during q) during self\n        origin a outlives q\n        return self.item@ref\n\n" +
            "    public func viaSlot(self: ref/Self, q: ref/i32) -> ref/(ref/i32 during a) during q\n        $abort(\"unused\")\n\n";
        var source = methods + "func run(flag: bool) -> ()\n    let value: i32 = 7\n    let h = Holder.init(item: value@ref)\n    var other: i32 = 3\n    let r = h.pickWith(other@ref)\n" +
            "    require r@follow@follow == 7 else => $abort(\"r\")\n    Console.writeLine(\"\\(r@follow@follow)\")\n    if flag\n        let z = h.viaSlot(other@ref)\n        Console.writeLine(\"\\(z@follow@follow)\")\nrun(false)\n";
        ScalarEmissionTest.EmitFixture("ResultPremiseMethod", source, "7\n");
        var invalid = MinimalEmissionTest.Analyze(source.Replace("        Console.writeLine(\"\\(z@follow@follow)\")", "        other = 4\n        Console.writeLine(\"\\(z@follow@follow)\")", StringComparison.Ordinal));
        Assert.True(invalid.Binding.Result.IsComplete, MinimalEmissionTest.Describe(invalid, null));
        Assert.Contains(invalid.Ownership.Issues, static issue => issue.Failure == OwnershipFailure.ComparisonLoanConflict);
    }

    // SPEC 15.3.7, 15.6.1: a fixed caller whose arguments leave the outer Origin free is valid, since the call's Origins satisfy the
    // premise; one whose argument pins it at an invariant position must prove the substituted relation, and an Unknown one is a
    // wellFormed record at the call. The premise stated by the caller proves it.
    [Fact]
    public void ACallProvesItsCalleesResultPremise()
    {
        Assert.True(DiagnosticCorpus.Check(Holder + PickRelated + "func caller(h: ref/Holder, x: ref/i32) -> i32\n    origin h.a outlives x\n    let r = pickRelated(p: h, q: x)\n    return r@follow@follow\n" + Main).Accepted);

        var source = Holder + PickInto + Pinned + Main;
        var output = DiagnosticCorpus.Check(source);
        var error = Assert.Single(output.Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenOriginRelation_Kd), DiagnosticCategory.Proof), (error.Code, error.Category));
        Assert.Equal("pickInto(p: h, q: x, keep: keep)", Text(source, error.Span));
        Assert.Equal("requires x outlives s, which is not proven", error.Label);
        Assert.Equal(["outlives", "x", "s", "wellFormed"], error.Reason!.Select(static x => x.Value));
        Assert.Null(error.Related);

        // The console and the language server publish the same record at the call.
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(new DiagnosticResult(output.Diagnostics, output.Sources), string.Empty);
        Assert.Contains("requires x outlives s, which is not proven", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(output.Sources[error.Source].Path);
        var published = Assert.Single(WorkspaceCheck.Place(output, [identity], identity, true)[identity]);
        Assert.Equal((error.Code, error.Display!.Range), (published.Code, published.Range));

        // An independent Loan conflict in the same body stays visible beside it.
        var independent = DiagnosticCorpus.Check(source.Replace("    let r = pickInto", "    var n: i32 = 1\n    let m = n@ref\n    n = 2\n    Console.writeLine(\"\\(m@follow)\")\n    let r = pickInto", StringComparison.Ordinal)).Diagnostics;
        Assert.Equal([nameof(DiagnosticCode.ComparisonLoanConflict_Kd), nameof(DiagnosticCode.UnprovenOriginRelation_Kd)], independent.Select(static x => x.Code));

        Assert.True(DiagnosticCorpus.Check(source.Replace("    origin h.a outlives x\n", "    origin h.a outlives x\n    origin x outlives s\n", StringComparison.Ordinal)).Accepted);
    }

    // SPEC 15.6.4 step 3, 15.3.7: a call through a Function Item, or through a reference to one, solves the named callee's fresh Origins
    // under its result premises and declared relations, as a direct call does. The valid uses run; a result part that would outlive the
    // local borrow is the fit record at the return, related at that borrow (these calls were accepted and read a dead stack slot).
    [Fact]
    public void AValueCallThroughAFunctionItemProvesTheCalleesConditions()
    {
        ScalarEmissionTest.EmitFixture("ResultPremiseItemUses", ItemUses, "30 3\n");
        var conflict = MinimalEmissionTest.Analyze(ItemUses.Replace("require second@follow", "other = 5\nrequire second@follow", StringComparison.Ordinal));
        Assert.True(conflict.Binding.Result.IsComplete, MinimalEmissionTest.Describe(conflict, null));
        Assert.Contains(conflict.Ownership.Issues, static issue => issue.Failure == OwnershipFailure.ComparisonLoanConflict);

        var direct = LeakItem + LeakEscape + Main;
        var throughReference = direct.Replace("    let pair = fr(x, local@ref)", "    let g = fr@ref\n    let pair = g(x, local@ref)", StringComparison.Ordinal);
        var written = direct.Replace("    return (.None, q)", "    origin q outlives p\n    return (.None, q)", StringComparison.Ordinal);
        foreach (var source in new[] { direct, throughReference, written })
        {
            var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
            Assert.Equal((nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "pair.1"), (error.Code, Text(source, error.Span)));
            Assert.Equal("requires the borrow local@ref outlives x, which is false", error.Label);
            Assert.Equal(["outlives", "local@ref", "x", "fit", "ref/i32 during x"], error.Reason!.Select(static x => x.Value));
            var related = Assert.Single(error.Related!);
            Assert.Equal(("origin", "local@ref"), (related.Role, Text(source, related.Span)));
        }

        // A premise over static is refuted at the call that pins it with a local borrow, as at a direct call.
        var pinned = "func f(q: ref/i32) -> (Option<ref/(ref/i32 during q) during static>, ref/i32 during static)\n    return (.None, q)\n\n" +
            "func g() -> ref/i32 during static\n    let local: i32 = 5\n    let fr = f\n    let pair = fr(local@ref)\n    return pair.1\n" + Main;
        var call = Assert.Single(DiagnosticCorpus.Check(pinned).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "fr(local@ref)"), (call.Code, Text(pinned, call.Span)));
        Assert.Equal("requires the borrow local@ref outlives static, which is false", call.Label);
        Assert.Equal("wellFormed", call.Reason![3].Value);

        // Origin conditions belong to the selected Item, at the argument supplying the failed relation.
        var clause = Pin + "func escape() -> ref/i32 during static\n    let local: i32 = 5\n    let fr = pin\n    return fr(local@ref)\n" + Main;
        var failure = Assert.Single(DiagnosticCorpus.Check(clause).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "local@ref"), (failure.Code, Text(clause, failure.Span)));
        Assert.Equal("requires the borrow local@ref outlives static, which is false", failure.Label);
        Assert.Equal("declared", failure.Reason!.Single(x => x.Name == "source").Value);
        Assert.Contains(failure.Related!, x => x.Role == "relation" && Text(clause, x.Span) == "origin p outlives static");
        Assert.True(DiagnosticCorpus.Check(written.Replace("func escape(x: ref/i32) -> ref/i32 during x", "func escape(x: ref/i32) -> i32", StringComparison.Ordinal)
            .Replace("    return pair.1\n", "    return pair.1@follow\n", StringComparison.Ordinal)).Accepted);
    }

    // SPEC 15.3.7, 10.7: a conversion of a Function Item or a reference, and a Callable proof, solve the implementation's call Origins under
    // its conditions and prove them from the required contract; a failure names the condition, never a parameter that fits.
    [Fact]
    public void AConversionProvesTheImplementationsConditions()
    {
        // The premise shapes the instantiation, as the written clause does (the premise-only form blamed the 1st parameter).
        var erased = LeakItem + "func escape(x: ref/i32) -> i32\n    let local: i32 = 99\n    let fr: (ref/i32, ref/i32) -> (Option<ref/(ref/i32)>, ref/i32) = leak\n    let pair = fr(x, local@ref)\n    return pair.1@follow\n" + Main;
        Assert.True(DiagnosticCorpus.Check(erased).Accepted);

        var item = Pin + "func forward(x: ref/i32 during static) -> ref/i32 during static\n    let fr = pin\n    let g: (ref/i32) -> ref/i32 during static = fr\n    return g(x)\n" + Main;
        var reference = item.Replace("    let fr = pin\n    let g: (ref/i32) -> ref/i32 during static = fr\n", "    let g: (ref/i32) -> ref/i32 during static = pin\n", StringComparison.Ordinal);
        foreach (var (source, at) in new[] { (item, "fr"), (reference, "pin") })
        {
            var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
            Assert.Equal((nameof(DiagnosticCode.UnprovenOriginContract_Kd), DiagnosticCategory.Proof), (error.Code, error.Category));
            Assert.Equal(at, Text(source, error.Span));
            Assert.Equal("the clause 'origin p outlives static' requires the omitted Origin of ref/i32 outlives static, which is not proven", error.Label);
            Assert.Equal(["conversion", "the clause 'origin p outlives static'", "outlives", "ref/i32", "static"], error.Reason!.Select(static x => x.Value));
            var related = Assert.Single(error.Related!);
            Assert.Equal(("origin", "ref/i32"), (related.Role, Text(source, related.Span)));
            Assert.True(DiagnosticCorpus.Check(source.Replace("let g: (ref/i32) ->", "let g: (ref/i32 during static) ->", StringComparison.Ordinal)).Accepted);
        }

        var premise = "func f(q: ref/i32) -> (Option<ref/(ref/i32 during q) during static>, ref/i32 during static)\n    return (.None, q)\n\n" +
            "func use() -> i32\n    let g: (ref/i32) -> (Option<ref/(ref/i32) during static>, ref/i32 during static) = f\n    return 0\n" + Main;
        var result = Assert.Single(DiagnosticCorpus.Check(premise).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenOriginContract_Kd), "f"), (result.Code, Text(premise, result.Span)));
        Assert.Equal("the result's well-formedness requires the omitted Origin of ref/i32 outlives static, which is not proven", result.Label);
        Assert.True(DiagnosticCorpus.Check(premise.Replace("let g: (ref/i32) ->", "let g: (ref/i32 during static) ->", StringComparison.Ordinal)).Accepted);

        // A Callable argument proves the clause too: an Item whose clause the requirement cannot prove fails the Callable proof in its
        // Origin part, which is Unknown (SPEC 8.7, 15.6.1), so the call is the Constraint record at the argument naming the clause (it was
        // NoApplicableOverload_Kd at the call); an implementation without that clause is accepted.
        var callable = Pin + "func apply<F>(f: ref/F, x: ref/i32) -> i32\n    F is Callable<(ref/i32) -> ref/i32>\n    return f(x)@follow\n\n" +
            "func run() -> i32\n    let local: i32 = 5\n    return apply(pin@ref, local@ref)\n" + Main;
        var argument = Assert.Single(DiagnosticCorpus.Check(callable).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenConstraint_Kd), DiagnosticCategory.Proof, "pin@ref"), (argument.Code, argument.Category, Text(callable, argument.Span)));
        Assert.Equal(["F is Callable<(ref/i32) -> ref/i32>", "the clause 'origin p outlives static'", "outlives", "ref/i32", "static"], argument.Reason!.Skip(1).Select(static x => x.Value));
 // SPEC 8.6: a Callable signature writes no `during`.
        Assert.Equal([("constraint", "F is Callable<(ref/i32) -> ref/i32>"), ("origin", "ref/i32")], argument.Related!.Select(x => (x.Role, Text(callable, x.Span))));
        Assert.True(DiagnosticCorpus.Check(callable.Replace(Pin, "func pin(p: ref/i32) -> ref/i32 during p => p\n\n", StringComparison.Ordinal)).Accepted);
    }

    // SPEC 15.3.7: a conformance solves the implementation's call Origins under its result premise, so a requirement whose result is
    // bounded by both inputs admits it; one whose result part is bounded by `p` alone cannot prove `q outlives p`. Contract comparisons
    // do not report UnprovenOriginContract_Kd yet (G56 U4), so that failure is the comparison record at the conformance.
    [Fact]
    public void AConformanceProvesTheImplementationsResultPremise()
    {
        const string implementation = "struct S\n    Self is Leaker\n\n    public func leak(self: ref/Self, p: ref/i32, q: ref/i32) -> (Option<ref/(ref/i32 during q) during p>, ref/i32 during p)\n        return (.None, q)\n\n";
        var admitted = "contract Leaker\n    func leak(self: ref/Self, p: ref/i32, q: ref/i32) -> (Option<ref/(ref/i32 during q) during (p and q)>, ref/i32 during (p and q))\n\n" + implementation + Main;
        Assert.True(DiagnosticCorpus.Check(admitted).Accepted);

        var refused = "contract Leaker\n    func leak(self: ref/Self, p: ref/i32, q: ref/i32) -> (Option<ref/(ref/i32 during (p and q)) during (p and q)>, ref/i32 during p)\n\n" + implementation + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(refused).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.IncompatibleContractImplementation_Kd), "Self is Leaker"), (error.Code, Text(refused, error.Span)));
    }

    // SPEC 15.3.6, 15.6.1: a meet at the longer end of a fit is decomposed; the record names its failing operand, and either a premise
    // for that operand or the whole meet as the result bound makes the program valid.
    [Fact]
    public void AMeetAtTheLongerEndNamesItsFailingOperand()
    {
        var source = Holder + "func f(p: ref/Holder, q: ref/i32) -> (Option<ref/(ref/i32 during p.a) during q>, ref/i32 during q)\n    return (.None, p.item)\n\n" +
            "func g(x: ref/i32, h: ref/Holder) -> ref/i32 during x\n    let pair = f(h, x)\n    return pair.1\n" + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "pair.1"), (error.Code, Text(source, error.Span)));
        Assert.Equal("requires h.a outlives x, which is not proven", error.Label);
        Assert.Equal(["outlives", "h.a", "x", "fit", "ref/i32 during x"], error.Reason!.Select(static x => x.Value));
        Assert.True(DiagnosticCorpus.Check(source.Replace("    let pair = f(h, x)", "    origin h.a outlives x\n    let pair = f(h, x)", StringComparison.Ordinal)).Accepted);
        Assert.True(DiagnosticCorpus.Check(source.Replace("-> ref/i32 during x\n    let pair", "-> ref/i32 during (x and h.a)\n    let pair", StringComparison.Ordinal)).Accepted);

        // A local annotation's fit, which ownership analysis judges, decomposes the meet too, under the premise or the written clause;
        // it is no result value, so only the premise applies, and it makes the program valid.
        var local = source.Replace("-> ref/i32 during x\n    let pair = f(h, x)\n    return pair.1\n", "-> i32\n    let pair = f(h, x)\n    let k: ref/i32 during x = pair.1\n    return k@follow\n", StringComparison.Ordinal);
        var written = local.Replace("    return (.None, p.item)", "    origin p.a outlives q\n    return (.None, p.item)", StringComparison.Ordinal);
        foreach (var program in new[] { local, written })
        {
            var record = Assert.Single(DiagnosticCorpus.Check(program).Diagnostics);
            Assert.Equal((nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "pair.1"), (record.Code, Text(program, record.Span)));
            Assert.Equal("requires h.a outlives x, which is not proven", record.Label);
            Assert.Equal(["outlives", "h.a", "x", "fit", "ref/i32 during x"], record.Reason!.Select(static x => x.Value));
            Assert.True(DiagnosticCorpus.Check(program.Replace("    let pair = f(h, x)", "    origin h.a outlives x\n    let pair = f(h, x)", StringComparison.Ordinal)).Accepted);
        }
    }

    // SPEC 15.6.1 Identity: a problem is its location, its relation's source and its longer end, and every failed chain is reported, so
    // the failing operands of a meet that ownership analysis judges, and the failing relations substituted at one call, are separate
    // records at one location, each with its own repair; a proven operand adds none. These faulted check with CheckFaulted_Kd ("one
    // problem was reported with different facts"), also for the join, Type-occurrence and clause forms that dev reported as one meet.
    [Theory]
    [InlineData(JoinMeet, "= t", "fit", "a outlives x", "b outlives x")]
    [InlineData(TypeMeet, "ref/(ref/i32 during (a and b)) during x", "wellFormed", "a outlives x", "b outlives x")]
    [InlineData(ClauseMeet, "t.0", "fit", "a outlives x", "b outlives x")]
    [InlineData(PremiseMeet, "t.2", "fit", "h.a outlives x", "j.a outlives x")]
    [InlineData(TwoPremises, "pk(h, x, y, keep)", "wellFormed", "x outlives s", "y outlives s")]
    [InlineData(MeetArgument, "pk(h, t.0, keep)", "wellFormed", "a outlives s", "x outlives s", "b outlives s")]
    public void EachFailedChainAtOneLocationIsItsOwnRecord(string program, string at, string source, params string[] relations)
    {
        var text = program + Main;
        var output = DiagnosticCorpus.Check(text);
        var start = text.LastIndexOf(at, StringComparison.Ordinal) + (at.StartsWith("= ", StringComparison.Ordinal) ? 2 : 0);
        var shown = at.StartsWith("= ", StringComparison.Ordinal) ? at[2..] : at;
        Assert.Equal(relations.Select(static x => $"requires {x}, which is not proven"), output.Diagnostics.Select(static x => x.Label));
        foreach (var error in output.Diagnostics)
        {
            Assert.Equal((nameof(DiagnosticCode.UnprovenOriginRelation_Kd), shown, start, source), (error.Code, Text(text, error.Span), error.Span!.Value.Start, error.Reason![3].Value));
            var label = error.Label!;
            var relation = label["requires ".Length..label.IndexOf(',', StringComparison.Ordinal)];
        }

        // Both outputs publish every record at that location, and adding each failing relation as a premise makes the program valid.
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(new DiagnosticResult(output.Diagnostics, output.Sources), string.Empty);
        Assert.All(relations, x => Assert.Contains($"requires {x}, which is not proven", console.Text, StringComparison.Ordinal));
        var identity = SourceIdentity.FromPath(output.Sources[output.Diagnostics[0].Source].Path);
        var published = WorkspaceCheck.Place(output, [identity], identity, true)[identity];
        Assert.Equal(relations.Length, published.Count(x => x.Code == nameof(DiagnosticCode.UnprovenOriginRelation_Kd) && x.Range == output.Diagnostics[0].Display!.Range));
        var header = text.IndexOf('\n', text.LastIndexOf("\nfunc ", StringComparison.Ordinal) + 1);
        var repaired = text.Insert(header + 1, string.Concat(relations.Select(static x => $"    origin {x}\n")));
        Assert.True(DiagnosticCorpus.Check(repaired).Accepted, string.Join("\n", DiagnosticCorpus.Check(repaired).Diagnostics.Select(static x => $"{x.Code}: {x.Label}")));
    }

    // A Refuted chain and an Unknown one at one location are two records, each with its own code and label, the Borrow related.
    [Fact]
    public void ARefutedAndAnUnknownOperandAtOneLocationAreTwoRecords()
    {
        var source = ClauseCallee + "func g(x: ref/i32, b: ref/i32) -> i32\n    let local: i32 = 3\n    let t = f(local@ref, x, b)\n    let k: ref/i32 during x = t.0\n    return k@follow\n" + Main;
        var output = DiagnosticCorpus.Check(source).Diagnostics;
        var at = source.IndexOf("t.0", StringComparison.Ordinal);
        Assert.Equal(
            [(nameof(DiagnosticCode.UnprovenOriginRelation_Kd), at, "requires b outlives x, which is not proven"), (nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), at, "requires the borrow local@ref outlives x, which is false")],
            output.Select(static x => (x.Code, x.Span!.Value.Start, x.Label)));
        var related = Assert.Single(output[1].Related!);
        Assert.Equal(("origin", "local@ref"), (related.Role, Text(source, related.Span)));
    }

    // SPEC 15.6.5: a Binding fit's own record names the failing operand that decides the judgment, and each other failing operand is a
    // record of its own: a Refuted Borrow before an Unknown
    // parameter, whatever their order, under a written clause or the result premise, and also after the Unknown one is proven. It named
    // the first failing operand, an Unknown `b`. With no body-local operand, a premise for `b` or the meet as the result bound makes the
    // program valid.
    [Theory]
    [InlineData("    let local: i32 = 3\n    let t = f(b, x, local@ref)\n    return t.0\n", "t.0")]
    [InlineData("    let local: i32 = 3\n    let t = f(local@ref, x, b)\n    return t.0\n", "t.0")]
    [InlineData("    origin b outlives x\n    let local: i32 = 3\n    let t = f(b, x, local@ref)\n    return t.0\n", "t.0")]
    [InlineData("    let local: i32 = 3\n    let t = w(b, x, local@ref)\n    return t.2\n", "t.2")]
    public void TheNamedOperandFollowsTheJudgment(string body, string at)
    {
        const string callees = ClauseCallee + "func w(p: ref/i32, q: ref/i32, r: ref/i32) -> (Option<ref/(ref/i32 during p) during q>, Option<ref/(ref/i32 during r) during q>, ref/i32 during q)\n    return (.None, .None, q)\n\n";
        var source = callees + "func h(x: ref/i32, b: ref/i32) -> ref/i32 during x\n" + body + Main;
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        var error = Assert.Single(errors, static x => x.Code == nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd));
        Assert.Equal((nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), DiagnosticCategory.Language), (error.Code, error.Category));
        Assert.Equal((at, source.IndexOf("return " + at, StringComparison.Ordinal) + 7), (Text(source, error.Span), error.Span!.Value.Start));
        Assert.Equal("requires the borrow local@ref outlives x, which is false", error.Label);
        Assert.Equal(["outlives", "local@ref", "x", "fit", "ref/i32 during x"], error.Reason!.Select(static x => x.Value));
        var related = Assert.Single(error.Related!);
        Assert.Equal(("origin", source.IndexOf("local@ref", StringComparison.Ordinal)), (related.Role, related.Span!.Value.Start));

        // SPEC 15.6.1: every failed chain is reported, so an Unknown `b` that no premise proves is its own record at that value (U3-B).
        if (body.Contains("origin b", StringComparison.Ordinal))
        {
            Assert.Single(errors);
        }
        else
        {
            var other = Assert.Single(errors, static x => x.Code != nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd));
            Assert.Equal((nameof(DiagnosticCode.UnprovenOriginRelation_Kd), error.Span, "requires b outlives x, which is not proven"), (other.Code, other.Span, other.Label));
        }

        var valid = source.Replace("local@ref", "x", StringComparison.Ordinal);
        if (!body.Contains("origin b", StringComparison.Ordinal))
        {
            var unknown = Assert.Single(DiagnosticCorpus.Check(valid).Diagnostics);
            Assert.Equal((nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "requires b outlives x, which is not proven"), (unknown.Code, unknown.Label));
            Assert.True(DiagnosticCorpus.Check(valid.Replace("-> ref/i32 during x\n", "-> ref/i32 during (x and b)\n", StringComparison.Ordinal)).Accepted);
            valid = valid.Replace("    let local: i32 = 3\n", "    origin b outlives x\n    let local: i32 = 3\n", StringComparison.Ordinal);
        }

        Assert.True(DiagnosticCorpus.Check(valid).Accepted);
    }

    // SPEC 15.3.4, 15.3.7: a nested Function Type's per-call Origins cannot leave their binder, so the result premise stops at it. The
    // well-formedness inside it stays the definition's obligation at its Type occurrence, as before the premise existed, never a call
    // obligation no caller can prove or a bound that leaks the per-call Origin into the caller's Types.
    [Fact]
    public void ANestedFunctionTypeKeepsItsWellFormednessAtTheDefinition()
    {
        var getter = "func g0(q: ref/i32) -> Option<ref/(ref/i32 during static) during static>\n    return .None\n\n" +
            "func get() -> (ref/i32) -> Option<ref/(ref/i32) during static>\n    return g0\n\n" +
            "func make() -> i32\n    let local: i32 = 41\n    let g = get()\n    match g(local@ref)\n        .Some(let r) => return r@follow@follow\n        .None => return 0\n" + Main;
        var definition = Assert.Single(DiagnosticCorpus.Check(getter).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "ref/(ref/i32) during static"), (definition.Code, Text(getter, definition.Span)));
        Assert.Equal(getter.IndexOf("Option<ref/(ref/i32) during static>", StringComparison.Ordinal) + 7, definition.Span!.Value.Start); // In get's result, never at the call.
        Assert.Equal("requires the omitted Origin of ref/i32 outlives static, which is not proven", definition.Label);
        Assert.Equal(["outlives", "ref/i32", "static", "wellFormed"], definition.Reason!.Select(static x => x.Value));
        var input = Assert.Single(definition.Related!);
        Assert.Equal("origin", input.Role);
        Assert.Equal(getter.IndexOf("-> (ref/i32) ->", StringComparison.Ordinal) + 4, input.Span!.Value.Start);

        // Two signatures with the same Function Type part each keep their own record; the forwarding call adds none.
        var forward = ActionBox + "func forward(bx: ref/Box) -> ref/((ref/i32) -> ref/i32) during bx\n    return pick(bx)\n\n" +
            "func run() -> i32\n    let box = Box.init(action: ident)\n    let f = forward(box@ref)\n    return 0\n" + Main;
        Assert.Equal(
            [("ref/((ref/i32) -> ref/i32) during b", "requires the omitted Origin of ref/i32 outlives b, which is not proven"), ("ref/((ref/i32) -> ref/i32) during bx", "requires the omitted Origin of ref/i32 outlives bx, which is not proven")],
            DiagnosticCorpus.Check(forward).Diagnostics.Select(x => (Text(forward, x.Span), x.Label)));

        // A caller's annotation of the call result is judged by its own Type occurrence; no meet with the per-call Origin reaches it.
        var annotated = ActionBox + "func use(bx: ref/Box) -> i32\n    let f = pick(bx)\n    let g: ref/((ref/i32) -> ref/i32) during bx = f\n    return 0\n" + Main;
        Assert.Equal(
            [("ref/((ref/i32) -> ref/i32) during b", "wellFormed"), ("ref/((ref/i32) -> ref/i32) during bx", "wellFormed")],
            DiagnosticCorpus.Check(annotated).Diagnostics.Select(x => (Text(annotated, x.Span), x.Reason![3].Value)));

        // A Function Item converted to a nested Function Type proves its premise from that Type's inputs alone: a per-call input
        // cannot outlive `b`, and writing it over `b` converts.
        var converted = LeakItem + "func mk(b: ref/i32) -> (ref/i32 during b, ref/i32) -> (Option<ref/(ref/i32) during b>, ref/i32 during b)\n    return leak\n" + Main;
        var conversion = Assert.Single(DiagnosticCorpus.Check(converted).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenOriginContract_Kd), "leak"), (conversion.Code, Text(converted, conversion.Span)));
        Assert.Equal("the result's well-formedness requires the omitted Origin of ref/i32 outlives b, which is not proven", conversion.Label);
        Assert.Equal(["conversion", "the result's well-formedness", "outlives", "ref/i32", "b"], conversion.Reason!.Select(static x => x.Value));
        Assert.Equal(converted.IndexOf("during b, ref/i32)", StringComparison.Ordinal) + 10, Assert.Single(conversion.Related!).Span!.Value.Start);
        Assert.True(DiagnosticCorpus.Check(converted.Replace("(ref/i32 during b, ref/i32) ->", "(ref/i32 during b, ref/i32 during b) ->", StringComparison.Ordinal)).Accepted);
    }

    // A result that holds a Function Type beside a premise keeps the premise outside it: the valid uses run, and the top-level result
    // part keeps the Loan on `other`.
    [Fact]
    public void APremiseBesideAFunctionTypePartStillBoundsTheCall()
    {
        ScalarEmissionTest.EmitFixture("ResultPremiseFunctionPart", FunctionPart, "19 3\n");
        var conflict = MinimalEmissionTest.Analyze(FunctionPart.Replace("require second@follow", "other = 4\nrequire second@follow", StringComparison.Ordinal));
        Assert.True(conflict.Binding.Result.IsComplete, MinimalEmissionTest.Describe(conflict, null));
        Assert.Contains(conflict.Ownership.Issues, static issue => issue.Failure == OwnershipFailure.ComparisonLoanConflict);
    }

    // SPEC 7.6.4, 15.3.7: a value call through a generic Function Item enters the instance of its bound arguments, whose result keeps the
    // Item's per-call Origins; an Option or struct over a borrow there has a concrete representation (generation failed with "Generic
    // entry result has no concrete representation", also without any premise). The premise form still refutes an escaping part.
    [Fact]
    public void AGenericFunctionItemValueCallRuns()
    {
        ScalarEmissionTest.EmitFixture("ResultPremiseGenericItem", GenericItems, "36\n");
        var escape = "func leakg<T>(p: ref/T, q: ref/T) -> (Option<ref/(ref/T during q) during p>, ref/T during p)\n    return (.None, q)\n\n" +
            "func escape(x: ref/i32) -> ref/i32 during x\n    let local: i32 = 99\n    let fr = leakg<i32>\n    let pair = fr(x, local@ref)\n    return pair.1\n" + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(escape).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "pair.1"), (error.Code, Text(escape, error.Span)));
        Assert.Equal("requires the borrow local@ref outlives x, which is false", error.Label);
    }

    // SPEC 15.3.6, 15.6.4: a premise over a result-only signature Origin bounds it at the call, as the written clause does, so the call
    // runs, keeps the argument's Loan and is refuted where the part would outlive a local borrow (it was UnprovenConstraint_Kd).
    [Fact]
    public void APremiseBoundsAResultOnlyOrigin()
    {
        var valid = ResultOnly + "func run(x: ref/i32) -> i32\n    let second = f(x).1\n    return second@follow\n\n" +
            "func forward(x: ref/i32) -> ref/i32 during x\n    return f(x).1\n\nlet a: i32 = 7\nConsole.writeLine(\"\\(run(a@ref)) \\(forward(a@ref)@follow)\")\n";
        ScalarEmissionTest.EmitFixture("ResultPremiseResultOnly", valid, "7 7\n");

        var write = ResultOnly + "func run() -> i32\n    var local: i32 = 99\n    let second = f(local@ref).1\n    local = 5\n    return second@follow\n" + Main;
        var conflict = Assert.Single(DiagnosticCorpus.Check(write).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.ComparisonLoanConflict_Kd), "local = 5"), (conflict.Code, Text(write, conflict.Span)));

        var escape = ResultOnly + "func run(x: ref/i32) -> ref/i32 during x\n    let local: i32 = 99\n    let pair = f(local@ref)\n    return pair.1\n" + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(escape).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "pair.1"), (error.Code, Text(escape, error.Span)));
        Assert.Equal("requires the borrow local@ref outlives x, which is false", error.Label);
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void WarmBindingOfResultPremiseCallsAllocatesNothing(int form)
    {
        var c = MinimalEmissionTest.Analyze(form switch
        {
            1 => ItemUses,
            2 => ResultOnly + "func forward(x: ref/i32) -> ref/i32 during x\n    return f(x).1\n\n" + FunctionPart,
            _ => Holder + PickRelated + PickInto + Pinned.Replace("    origin h.a outlives x\n", "    origin h.a outlives x\n    origin x outlives s\n", StringComparison.Ordinal) +
                "let value: i32 = 7\nlet h = Holder.init(item: value@ref)\nlet other: i32 = 3\nlet r = pickRelated(p: h@ref, q: other@ref)\nrequire r@follow@follow == 7 else => $abort(\"r\")\n",
        });
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Result premise binding failed.");
            }
        }));
    }

    private static string Text(string source, SourceSpan? span) => span is { } value ? source.Substring(value.Start, value.Length) : string.Empty;
}
