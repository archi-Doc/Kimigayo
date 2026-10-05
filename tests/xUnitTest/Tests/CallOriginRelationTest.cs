// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 15.6.1, 10.1, 10.3, 10.4: candidate applicability, expected-result filtering and ranking use only the structural part of each
// fit, so the Origin relations of a call never change its selection. The selected candidate's relations are judged after selection at
// their own sources: each argument's fit at the argument, a substituted clause at the first input that names its longer Origin, and
// the result at its destination. A chain between body Origins stays the located UnsupportedOwnership_Kd limit at the argument.
public class CallOriginRelationTest(ITestOutputHelper output)
{
    private const string Main = "public func main() => ()\n";

    private const string NeedsStatic = "func needsStatic(x: ref/i32 during static) -> i32 => x@follow\n";

    private const string Refuted = "Return or store an owned value, or a borrow of an input, instead of a borrow of storage that ends with the body";

    // q35: the clause is related with the role `relation`, and its source is `declared`.
    private const string Clause = "func needs(a: ref/i32) -> i32\n    origin a outlives static\n    return 1\n\nfunc caller(p: ref/i32) -> i32 => needs(p)\n";

    // q19: the fresh Origin `o` is fixed to x by the invariant first argument, so the second one does not fit.
    private const string Fresh = "func pair(a: uniq/(ref/i32 during o), b: uniq/(ref/i32 during o)) -> () => ()\n\nfunc caller(x: ref/i32, y: ref/i32, p: uniq/(ref/i32 during x), q: uniq/(ref/i32 during y)) -> ()\n    pair(p, q)\n";

    [Theory]
    [InlineData(NeedsStatic + "func caller(p: ref/i32) -> i32 => needsStatic(p)\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "p", "requires p outlives static, which is not proven", null, "Bind p to static where it is introduced, as in 'p: ref/i32 during static'")]
    [InlineData(NeedsStatic + "func caller() -> i32\n    let local = 3\n    return needsStatic(local)\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "local", "requires the borrow local outlives static, which is false", "local", Refuted)]
    [InlineData(NeedsStatic + "func caller() -> i32\n    let local = 3\n    return needsStatic(local@ref)\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "local@ref", "requires the borrow local@ref outlives static, which is false", "local@ref", Refuted)]
    [InlineData(NeedsStatic + "func caller() -> i32 => needsStatic(5)\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "5", "requires the borrow 5 outlives static, which is false", "5", Refuted)]
    [InlineData(NeedsStatic + "func make() -> i32 => 4\nfunc caller() -> i32 => needsStatic(make())\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "make()", "requires the borrow make() outlives static, which is false", "make()", Refuted)]
    [InlineData("func takePair(x: (ref/i32 during static, i32)) -> i32 => 1\nfunc caller(p: ref/i32) -> i32 => takePair((p, 1))\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "(p, 1)", "requires p outlives static, which is not proven", null, "Bind p to static where it is introduced")]
    [InlineData("func store(x: ref/i32, target: uniq/Array<ref/i32 during x>) -> ()\n    let local: i32 = 1\n    target@follow.append(local@ref)\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "local@ref", "requires the borrow local@ref outlives x, which is false", "local@ref", Refuted)]
    public void AnArgumentRelationIsJudgedAfterSelection(string body, string code, string text, string label, string? origin, string advice)
    {
        var source = body + Main;
        var check = DiagnosticCorpus.Check(source);
        var error = Assert.Single(check.Diagnostics);
        Assert.Equal((code, text, label), (error.Code, Text(source, error.Span), error.Label));
        Assert.Equal(code == nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd) ? DiagnosticCategory.Language : DiagnosticCategory.Proof, error.Category);
        Assert.Contains(advice, error.Advice, StringComparison.Ordinal);
        var reason = error.Reason!;
        Assert.Equal(["relation", "longer", "shorter", "source", "destination"], reason.Select(static x => x.Name));
        Assert.Equal(("outlives", "fit"), (reason[0].Value, reason[3].Value));
        if (origin is null)
        {
            Assert.Null(error.Related);
        }
        else
        {
            var related = Assert.Single(error.Related!);
            Assert.Equal(("origin", origin), (related.Role, Text(source, related.Span)));
        }
    }

    // SPEC 15.6.1: every failed chain is reported, each at the argument that does not fit.
    [Fact]
    public void EveryFailingArgumentIsItsOwnRecord()
    {
        var source = "func both(x: ref/i32 during static, y: ref/i32 during static) -> i32 => 1\nfunc caller(p: ref/i32, q: ref/i32) -> i32 => both(p, q)\n" + Main;
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Equal([("p", "requires p outlives static, which is not proven"), ("q", "requires q outlives static, which is not proven")], errors.Select(x => (Text(source, x.Span), x.Label)));
    }

    // SPEC 15.6.1: a contextual payload that fits its expected payload Type only structurally is an Origin relation at the payload,
    // whether the expected enum comes from a selected parameter or an annotation, never a Type mismatch.
    [Theory]
    [InlineData("func needs(o: Option<ref/i32 during static>) -> i32 => 1\nfunc caller(p: ref/i32) -> i32\n    let local: i32 = 3\n    return needs(.Some(local@ref)) + needs(.Some(p))\n")]
    [InlineData("func caller(p: ref/i32) -> i32\n    let local: i32 = 3\n    let o: Option<ref/i32 during static> = .Some(local@ref)\n    let q: Option<ref/i32 during static> = .Some(p)\n    return 1\n")]
    public void AContextualPayloadRelationIsJudgedAtThePayload(string body)
    {
        var source = body + Main;
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Equal(
            [(nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "local@ref", "requires the borrow local@ref outlives static, which is false"), (nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "p", "requires p outlives static, which is not proven")],
            errors.Select(x => (x.Code, Text(source, x.Span), x.Label)));
        Assert.All(errors, static x => Assert.Equal(("fit", "ref/i32 during static"), (x.Reason![3].Value, x.Reason[4].Value)));
    }

    // SPEC 23.3.6.5: at an inner position of a Borrow argument, the failing Origin is the stored Borrow, not the argument's own one: the
    // record names that Borrow and relates it, and the destination shows only the Origin at the failed position. A shorter end that an
    // equality fixed to another argument's stored Borrow is shown and related the same way, and never written as `during` a borrow.
    [Theory]
    [InlineData("func needs(x: uniq/(ref/i32 during static)) -> () => ()\nfunc g() -> i32\n    let local: i32 = 1\n    var y = local@ref\n    needs(y@uniq)\n    return 1\n", "y@uniq", "requires the borrow local@ref == static, which is false", "uniq/(ref/i32 during static)")]
    [InlineData("func needs(x: ref/Option<ref/i32 during static>) -> i32 => 1\nfunc g() -> i32\n    let local: i32 = 1\n    let o: Option<ref/i32> = .Some(local@ref)\n    return needs(o@ref)\n", "o@ref", "requires the borrow local@ref outlives static, which is false", "ref/Option<ref/i32 during static>")]
    public void AnInnerBorrowIsNamedAndRelated(string body, string text, string label, string destination)
    {
        var source = body + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), text, label), (error.Code, Text(source, error.Span), error.Label));
        Assert.Equal(("fit", destination), (error.Reason![3].Value, error.Reason[4].Value));
        Assert.Contains(error.Related!, x => x.Role == "origin" && Text(source, x.Span) == "local@ref");
    }

    // A valid counterpart: the stored value's Origin fits the inner position, so the call is accepted. (The exclusive forms with a
    // fitting stored reference meet the existing CallActivationConflict_Kd limit of ownership, as on the base.)
    [Fact]
    public void AnInnerBorrowThatFitsIsAccepted()
    {
        var valid = DiagnosticCorpus.Check("func needs(x: ref/Option<ref/i32 during p>, p: ref/i32) -> i32 => 1\nfunc g(p: ref/i32) -> i32\n    let o: Option<ref/i32 during p> = .Some(p)\n    return needs(o@ref, p)\n" + Main);
        Assert.True(valid.Accepted, string.Join("\n", valid.Diagnostics.Select(static x => $"{x.Code}: {x.Label}")));
    }

    // SPEC 10.4 step 2, 15.6.1: Function-typed parameters are compared by their structural part too, so a generic candidate whose
    // substituted result Origin is static is not better than the nongeneric one, and step 3 prefers the nongeneric candidate (it
    // selected the generic one, printing 1).
    [Fact]
    public void FunctionTypedParametersRankByStructure()
        => ScalarEmissionTest.EmitFixture("CallOriginRelationFunctionRank", "func make<T>(action: (ref/i32) -> T) -> i32 => 1\nfunc make(action: (ref/i32) -> ref/i32) -> i32 => 2\nfunc pickStatic(n: ref/i32) -> ref/i32 during static => $abort(\"never\")\nrequire make(pickStatic) == 2 else => $abort(\"ranked\")", string.Empty);

    [Fact]
    public void ADeclaredRelationRelatesItsClause()
    {
        var source = Clause + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "p", "requires p outlives static, which is not proven"), (error.Code, Text(source, error.Span), error.Label));
        Assert.Equal("declared", error.Reason![3].Value);

        // SPEC 23.3.6.5: `destination` is a fact of `fit` only; a declared relation relates its clause instead.
        Assert.Equal(["relation", "longer", "shorter", "source"], error.Reason.Select(static x => x.Name));
        var related = Assert.Single(error.Related!);
        Assert.Equal(("relation", "origin a outlives static"), (related.Role, Text(source, related.Span)));
    }

    [Fact]
    public void AFreshEqualityRelatesTheInputThatFixedIt()
    {
        var source = Fresh + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "q", "requires y == x, which is not proven"), (error.Code, Text(source, error.Span), error.Label));
        Assert.Equal("==", error.Reason![0].Value);
        var related = Assert.Single(error.Related!);
        Assert.Equal(("relation", "p"), (related.Role, Text(source, related.Span)));
    }

    // SPEC 10.3, 15.6.1: an expected result never filters by Origins; the result's relation is one record at its destination, and a
    // meet at its longer end names the failing operand (SPEC 15.3.6). A constructor call whose local's clause fixes its slot is that
    // record at the call (it was NoApplicableOverload_Kd at the call, and a fact-less UnprovenConstraint_Kd before local slots were
    // judged by variance).
    [Theory]
    [InlineData("struct H {s}\n    public let item: ref/i32 during s\n    public init(item: ref/i32 during s) => self.item = item\n\nfunc f(a: ref/i32, b: ref/i32) -> i32\n    let h: H{x} = H.init(a)\n        origin x.s == b\n    return 0\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "H.init(a)", "requires a outlives b, which is not proven", null)]
    [InlineData("func take(x: ref/i32) -> ref/i32 during x => x\nfunc f(x: ref/i32) -> ref/i32 during x\n    let local: i32 = 5\n    return take(local)\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "take(local)", "requires the borrow local outlives x, which is false", "local")]
    [InlineData("func two(a: ref/i32, b: ref/i32) -> ref/i32 during b\n    origin a outlives b\n    return a\nfunc caller(p: ref/i32, q: ref/i32) -> ref/i32 during q => two(p, q)\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "two(p, q)", "requires p outlives q, which is not proven", null)]
    [InlineData("func first<T>(a: T, b: T) -> T => a@move\nfunc caller(p: ref/i32, q: ref/i32) -> ref/i32 during p => first(p, q)\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "first(p, q)", "requires q outlives p, which is not proven", null)]
    [InlineData("func id(a: ref/i32) -> ref/i32 during a => a\nfunc caller(p: ref/i32, q: ref/i32) -> ref/i32 during p\n    let r: ref/i32 during p = id(q)\n    return r\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "id(q)", "requires q outlives p, which is not proven", null)]
    [InlineData("func id(a: ref/i32) -> ref/i32 during a => a\nfunc caller(p: ref/i32, q: ref/i32, c: bool) -> ref/i32 during p\n    return if c => p else => id(q)\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "if c => p else => id(q)", "requires q outlives p, which is not proven", null)]
    [InlineData("func id(a: ref/i32) -> ref/i32 during a => a\nfunc keep(x: ref/i32 during static) -> i32 => 1\nfunc caller(q: ref/i32) -> i32 => keep(id(q))\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "id(q)", "requires q outlives static, which is not proven", null)]
    public void TheResultRelationIsJudgedAtItsDestination(string body, string code, string text, string label, string? origin)
    {
        var source = body + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((code, text, label), (error.Code, Text(source, error.Span), error.Label));
        if (origin is not null)
        {
            Assert.Equal(("origin", origin), (Assert.Single(error.Related!).Role, Text(source, error.Related![0].Span)));
        }
    }

    // SPEC 10.4 step 2, 15.6.1: Origin bindings never rank candidates, so a candidate whose Origin relation fails is still selected.
    [Fact]
    public void SelectionIgnoresOriginParts()
    {
        var literal = "func h(x: ref/i32 during static, y: i32) -> i32 => 1\nfunc h(x: ref/i32, y: i64) -> i32 => 2\nfunc caller(p: ref/i32) -> i32 => h(p, 1)\nfunc staticCaller(p: ref/i32 during static) -> i32 => h(p, 1)\n" + Main;
        Assert.Equal([nameof(DiagnosticCode.AmbiguousBinding_Kd), nameof(DiagnosticCode.AmbiguousBinding_Kd)], DiagnosticCorpus.Check(literal).Diagnostics.Select(static x => x.Code));

        // The nongeneric candidate wins by step 3; its relation is then Unproven (it was the generic one, printing 2).
        var generic = "func k(x: ref/i32 during static, y: i32) -> i32 => 1\nfunc k<T>(x: ref/i32, y: T) -> i32 => 2\nfunc caller(p: ref/i32) -> i32 => k(p, 1)\n" + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(generic).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "p"), (error.Code, Text(generic, error.Span)));
    }

    [Theory]
    [InlineData("Ranked", "func m<T>(x: ref/i32 during static, y: T) -> i32 => 1\nfunc m(x: ref/i32, y: i32) -> i32 => 2\nfunc caller(p: ref/i32) -> i32 => m(p, 1)\nlet v: i32 = 5\nrequire caller(v@ref) == 2 else => $abort(\"ranked\")")]
    [InlineData("Explicit", "func k(x: ref/i32 during static, y: i32) -> i32 => 1\nfunc k<T>(x: ref/i32, y: T) -> i32 => 2\nfunc localCaller(p: ref/i32) -> i32 => k<i32>(p, 1)\nlet v: i32 = 5\nrequire localCaller(v@ref) == 2 else => $abort(\"explicit\")")]
    public void AStructuralSelectionRuns(string name, string source)
        => ScalarEmissionTest.EmitFixture("CallOriginRelation" + name, source, string.Empty);

    // SPEC 15.6.5, PLAN G56: a call whose argument would store a borrow into a body Origin needs region-inference Loan edges, so it
    // stays the located limit at the argument; it is never accepted, also where the stored borrow would dangle. A generic `put<T>`
    // takes the meet of both Origins for T (Type-argument inference keeps a covariant meet), so its exclusive target does not fit.
    [Theory]
    [InlineData("func put(anchor: ref/i32, target: uniq/(ref/i32 during anchor)) -> () => ()\nfunc f() -> ()\n    let a = 1\n    let c = 3\n    var slot: ref/i32 = c@ref\n    put(a@ref, slot@uniq)\n    Console.writeLine(\"\\(slot@follow)\")\n", "a@ref")]
    [InlineData("func put<T>(target: uniq/T, value: T) -> () => ()\nfunc f() -> ()\n    let a = 1\n    let b = 2\n    var slot = a@ref\n    put(slot@uniq, b@ref)\n    Console.writeLine(\"\\(slot@follow)\")\n", "slot@uniq")]
    [InlineData("func put<T>(target: uniq/T, value: T) -> () => ()\nfunc f() -> ()\n    let a = 1\n    var slot = a@ref\n    if true\n        let b = 2\n        put(slot@uniq, b@ref)\n    Console.writeLine(\"\\(slot@follow)\")\n", "slot@uniq")]
    [InlineData("func put(target: uniq/(ref/i32 during o), value: ref/i32 during o) -> () => ()\nfunc g(x: ref/i32) -> i32\n    let local = 1\n    var slot = local@ref\n    put(slot@uniq, x)\n    return slot@follow\n", "x")]
    public void CallsThatStoreIntoBodyOriginsAreALocatedLimit(string body, string text)
    {
        var source = body + Main;
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        output.WriteLine(string.Join("\n", errors.Select(x => $"{x.Code} {Text(source, x.Span)}")));
        Assert.NotEmpty(errors);
        Assert.All(errors, static x => Assert.Equal(nameof(DiagnosticCode.UnsupportedOwnership_Kd), x.Code));
        Assert.Contains(errors, x => Text(source, x.Span) == text);
    }

    [Theory]
    [InlineData("func two(a: ref/i32, b: ref/i32) -> ref/i32 during b\n    origin a outlives b\n    return a\nfunc caller(p: ref/i32, q: ref/i32) -> i32 => two(p, q)@follow\n")]
    [InlineData("func needs(a: ref/i32) -> i32\n    origin a outlives static\n    return 1\nfunc staticCaller(p: ref/i32 during static) -> i32 => needs(p)\n")]
    [InlineData("struct S\n    public var n: i32\n    public init(n: i32) => self.n = n\n    public func takes(self: ref/Self, x: ref/i32 during self) -> i32 => 1\n    public func view(self: ref/Self) -> ref/i32 during self => self.n@ref\nfunc viaReceiver(s: ref/S, x: ref/i32) -> i32 => s.takes(x)\nfunc viaReceiverOk(s: ref/S, x: ref/i32) -> i32\n    origin x outlives s\n    return s.takes(x)\nfunc viaReceiverSame(s: ref/S, x: ref/i32 during s) -> i32 => s.takes(x)\nfunc viaResult(s: ref/S) -> ref/i32 during s => s.view()\n")]
    public void AProvenCallRelationIsAccepted(string body)
    {
        var check = DiagnosticCorpus.Check(body + Main);
        Assert.True(check.Accepted, string.Join("\n", check.Diagnostics.Select(static x => $"{x.Code}: {x.Label}")));
    }

    // SPEC 15.6.1: Constraints whose proof involves Origins, such as Owned, still take part in applicability.
    [Fact]
    public void ConstraintsKeepTakingPartInApplicability()
    {
        var source = "func hold<T>(x: T, n: i32) -> i32\n    T is Owned\n    return 1\n\nfunc hold<T>(x: T, n: i64) -> i32 => 2\n\nfunc caller(p: ref/i32) -> i32 => hold(p, 1)\n\nfunc staticCaller(p: ref/i32 during static) -> i32 => hold(p, 1)\n" + Main;
        Assert.Equal([nameof(DiagnosticCode.UnprovenConstraint_Kd), nameof(DiagnosticCode.AmbiguousBinding_Kd)], DiagnosticCorpus.Check(source).Diagnostics.Select(static x => x.Code));
    }

    // SPEC 23.3.6.8: the console, the language server and JSON carry the call relation with its related location.
    [Theory]
    [InlineData(Clause, "relation", "origin a outlives static")]
    [InlineData(Fresh, "relation", "p")]
    public void EveryOutputCarriesTheCallRelation(string body, string role, string related)
    {
        var check = DiagnosticCorpus.Check(body + Main);
        var record = Assert.Single(check.Diagnostics);
        var result = new DiagnosticResult(check.Diagnostics, check.Sources);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        Assert.Contains("UnprovenOriginRelation_Kd", console.Text, StringComparison.Ordinal);
        Assert.Contains($" = {role}: ", console.Text, StringComparison.Ordinal);
        Assert.Equal(related, Text(body + Main, record.Related![0].Span));

        var identity = SourceIdentity.FromPath(check.Sources[record.Source].Path);
        var plain = Assert.Single(WorkspaceCheck.Place(check, [identity], identity, false)[identity]);
        Assert.Equal((record.Code, record.Display!.Range), (plain.Code, plain.Range));
        var withRelated = Assert.Single(WorkspaceCheck.Place(check, [identity], identity, true)[identity]);
        Assert.Equal(record.Related![0].Range, Assert.Single(withRelated.RelatedInformation!).Location.Range);

        var json = JsonSerializer.Serialize(result, DiagnosticJsonContext.Default.DiagnosticResult);
        Assert.Equal(result, JsonSerializer.Deserialize(json, DiagnosticJsonContext.Default.DiagnosticResult));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmRelationFreeOverloadSelectionAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("func pick(x: ref/i32, y: i32) -> i32 => 1\nfunc pick(x: ref/i32, y: i64) -> i32 => 2\nfunc view(a: ref/i32, b: ref/i32) -> ref/i32 during a => a\nlet v: i32 = 3\nlet w: i32 = 4\nrequire pick(v@ref, 1@i32) + pick(w@ref, 2@i64) + view(v@ref, w@ref)@follow == 6 else => $abort(\"warm\")");
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }

    private static string Text(string source, SourceSpan? span) => span is { } value ? source.Substring(value.Start, value.Length) : string.Empty;
}
