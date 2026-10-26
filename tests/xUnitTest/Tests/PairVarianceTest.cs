// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 15.3.5: a pair layer whose binder admits uniq, obj, objuniq or raw is invariant in its target, as the concrete exclusive
// layer is, so a relation between its targets must hold as an equality in the generic body; a binder admitting only shared or
// owning Semantics keeps the covariant target.
public sealed class PairVarianceTest
{
    private const string Generic = "func smuggle<s/T>(t: s/T, longer: s/(ref/i32 during a)) -> s/(ref/i32 during b) during b\n    {0}\n    origin a outlives b\n    return longer@move\n" +
        "public func main() -> ()\n    let outer: i32 = 1\n    var holder = outer@ref\n    var n: i32 = 0\n    let shorter = smuggle(n@uniq, holder@uniq)\n    Console.writeLine(\"\\(shorter@follow@follow)\")\n";

    private const string Concrete = "func smuggle(t: uniq/i32, longer: uniq/(ref/i32 during a)) -> uniq/(ref/i32 during b) during b\n    origin a outlives b\n    return longer@move\n" +
        "public func main() -> ()\n    let outer: i32 = 1\n    var holder = outer@ref\n    var n: i32 = 0\n    let shorter = smuggle(n@uniq, holder@uniq)\n    Console.writeLine(\"\\(shorter@follow@follow)\")\n";

    // A covariant shortening of V's, Outer's and Outer<i32>'s one slot, and V's second Field over a `uniq` target beside a `ref` one.
    private const string ShortenV = "func shorten(x: V<i32> during a, y: ref/i32 during b) -> V<i32> during b\n    origin a outlives b\n    return x@move";

    private const string ShortenOuter = "func shorten(x: Outer during a, y: ref/i32 during b) -> Outer during b\n    origin a outlives b\n    return x@move";

    private const string ShortenGeneric = "func shorten(x: Outer<i32> during a, y: ref/i32 during b) -> Outer<i32> during b\n    origin a outlives b\n    return x@move";

    private const string UniqTargetV = "    origin a outlives c\n    public let x: uniq/(s/T during a) during c\n    public let k: ref/i32 during a\n" +
        "func shorten(x: V<i32>{w}, y: ref/i32 during e) -> V<i32>{z}\n    origin w.a outlives e\n    origin w.c outlives e\n    origin z.a == e\n    origin z.c == e\n    return x@move";

    // The target of an exclusive-admitting layer is invariant: the result fit requires a == b, as the concrete uniq form does.
    [Theory]
    [InlineData("s is owner or uniq")]
    [InlineData("s is uniq")]
    [InlineData("s is owner or objuniq")]
    public void AnExclusiveAdmittingLayerRequiresEqualTargets(string premise)
    {
        var generic = Relations(DiagnosticCorpus.Check(Generic.Replace("{0}", premise, StringComparison.Ordinal)));
        Assert.Contains(("==", "a", "b"), generic);
        Assert.Contains(("outlives", "longer", "b"), generic);
        Assert.Equal(Relations(DiagnosticCorpus.Check(Concrete)), generic);
    }

    // A layer admitting only shared or owning Semantics keeps the covariant target: `a outlives b` suffices, so no equality is required.
    [Theory]
    [InlineData("s is owner or ref")]
    [InlineData("s is ref")]
    [InlineData("s is owner")]
    public void ASharedOrOwningLayerKeepsTheCovariantTarget(string premise)
    {
        var relations = Relations(DiagnosticCorpus.Check(Generic.Replace("{0}", premise, StringComparison.Ordinal)));
        Assert.DoesNotContain(relations, static x => x.Relation == "==");
    }

    // SPEC 5.7, 15.3.5: a raw layer's Origin is a covariant position, so a slot a Loan<ref/...> Field and a raw layer state stays
    // covariant.
    [Fact]
    public void ARawOriginIsACovariantPosition()
    {
        var c = CompilationTestHelper.BindSuccess("struct W {s}\n    let keep: Loan<ref/i32 during s>\n    let p: raw/i32 during s\n");
        var schema = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<Kimi.Compiler.Parsing.StructKoto>().Single().SymbolOf()!.Schema!;
        Assert.Equal((OriginVariance.Covariant, new SlotRequirement(LoanRequirement.Ref, 0)), (schema.Origins[0].Variance, schema.Origins[0].Requirement));
    }

    // SPEC 15.3.5 (DECISIONS BU-44): a slot only a conditional pair slot uses publishes no unconditional requirement and its binder's bit,
    // and its declaration's cases range from the binder's weakest to its strongest class; a binder admitting no borrow adds no bit.
    [Theory]
    [InlineData("uniq", 1UL, LoanRequirement.Uniq, LoanRequirement.Uniq)]
    [InlineData("owner or uniq", 1UL, LoanRequirement.None, LoanRequirement.Uniq)]
    [InlineData("ref or uniq", 1UL, LoanRequirement.Ref, LoanRequirement.Uniq)]
    [InlineData("owner", 0UL, LoanRequirement.None, LoanRequirement.None)]
    public void AConditionalSlotPublishesItsBinder(string semantics, ulong binders, LoanRequirement weakest, LoanRequirement strongest)
    {
        var c = CompilationTestHelper.BindSuccess($"struct H<s/T> {{a}}\n    s is {semantics}\n    let item: s/T during a\n");
        var slot = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<Kimi.Compiler.Parsing.StructKoto>().Single().SymbolOf()!.Schema!.Origins[0];
        Assert.Equal((new SlotRequirement(LoanRequirement.None, binders), (weakest, strongest)), (slot.Requirement, c.Binding.RequirementAt(slot, null)));
    }

    // SPEC 15.3.5 (DECISIONS BU-44): variance stays one per declaration, so every leaf and nested position counts toward it whatever
    // case an instance selects, and a Type's slot that is Phantom in every admitted case is invariant as Phantom: its covariant
    // shortening is unproven for a binder admitting no borrow (`s is owner`), a nested slot Phantom in its only case (`Inner<i32>`) or
    // through an outer binder admitting no borrow, as for the concrete twin; a slot used in some case stays covariant, and an invariant
    // position (a raw or `uniq` target) stays invariant beside another use of the slot, a leaf's whose binder admits no borrow included.
    [Theory]
    [InlineData("struct V<s/T> {a}\n    s is owner\n    public let item: s/T during a\n" + ShortenV, true)]
    [InlineData("struct V<s/T> {a}\n    s is owner or uniq\n    public let item: s/T during a\n" + ShortenV, false)]
    [InlineData(PairAnnotationBindingTest.InnerOwnerUniq + "struct Outer {a}\n    public let j: Inner<i32> during a\n" + ShortenOuter, true)]
    [InlineData("struct Inner {b}\n    public let x: i32\nstruct Outer {a}\n    public let j: Inner during a\n" + ShortenOuter, true)]
    [InlineData(PairAnnotationBindingTest.InnerOwnerUniq + "struct Outer<q/U> {a}\n    q is owner or uniq\n    public let j: Inner<q/U> during a\n" + ShortenGeneric, false)]
    [InlineData(PairAnnotationBindingTest.InnerOwnerUniq + "struct Outer<q/U> {a}\n    q is owner\n    public let j: Inner<q/U> during a\n" + ShortenGeneric, true)]
    [InlineData(
        "struct Inner<r/U> {b}\n    r is owner or uniq\n    public let x: raw/(r/U during b)\nstruct Outer {a}\n    public let j: Inner<i32> during a\n    public let k: ref/i32 during a\n" + ShortenOuter,
        true)]
    [InlineData(
        "struct Inner<r/U> {b, c}\n    r is owner or uniq\n    origin b outlives c\n    public let x: uniq/(r/U during b) during c\nstruct Outer {a, d}\n    origin a outlives d\n" +
        "    public let j: Inner<i32>{v}\n        origin v.b == a\n        origin v.c == d\n    public let k: ref/i32 during a\nfunc shorten(x: Outer{w}, y: ref/i32 during e) -> Outer{z}\n" +
        "    origin w.a outlives e\n    origin w.d outlives e\n    origin z.a == e\n    origin z.d == e\n    return x@move",
        true)]
    [InlineData("struct V<s/T> {a, c}\n    s is owner\n" + UniqTargetV, true)]
    [InlineData("struct V<s/T> {a, c}\n    s is owner or uniq\n" + UniqTargetV, true)]
    public void AVariancePositionCountsInEveryCase(string declarations, bool unproven)
        => Assert.Equal(
            unproven ? [nameof(DiagnosticCode.UnprovenOriginRelation_Kd)] : [],
            DiagnosticCorpus.Check(declarations + "\npublic func main() => ()\n").Diagnostics.Select(static x => x.Code));

    private static List<(string Relation, string Longer, string Shorter)> Relations(CheckOutput output)
    {
        var relations = new List<(string, string, string)>();
        foreach (var record in output.Diagnostics)
        {
            if (record.Code != nameof(DiagnosticCode.UnprovenOriginRelation_Kd) || record.Reason is not { } facts)
            {
                continue;
            }

            relations.Add((Fact(facts, "relation"), Fact(facts, "longer"), Fact(facts, "shorter")));
        }

        relations.Sort();
        return relations;
    }

    private static string Fact(DiagnosticValue[] facts, string name)
    {
        foreach (var fact in facts)
        {
            if (fact.Name == name)
            {
                return fact.Value;
            }
        }

        return string.Empty;
    }
}
