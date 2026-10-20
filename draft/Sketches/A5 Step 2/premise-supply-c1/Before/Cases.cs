// Frozen synthetic model inputs. Expected outcomes live separately in
// expectations.json. None of these owner snapshots is claimed source-backed.
namespace PremiseSupply;

internal static class Cases
{
    private static string A(string name) => "is(T," + name + ")";
    private static Rule R(string id, string from, string to, string source = "ok") => new(id, from, to, source);
    private static Rule C(string id, string from, string to, string source = "ok")
        => R(id, "is($S," + from + ")", "is($S," + to + ")", source);

    private static Input Case(string id, string[] roots, Rule[] rules, params string[] queries) => new(
        id, "scope:" + id, Oracle.ClosedForward, Validation.Valid,
        [new("ok", SourceState.Available, "synthetic"), new("off", SourceState.Unavailable, "synthetic"),
         new("wait", SourceState.Pending, "synthetic"), new("limit", SourceState.Resource, "synthetic"),
         new("unsupported", SourceState.Unsupported, "synthetic")],
        roots.Select((x, i) => new Root("r" + i, x, "ok")).ToArray(), rules, [], [],
        queries.Select((x, i) => new Query("q" + i, x)).ToArray(),
        "synthetic stable snapshot; actual producer/owner correspondence unproved");

    internal static Input[] All()
    {
        var negative = R("negative", "is($S,C)", "not(is($S,C))");
        var downstream = C("downstream", "C", "D");
        var rows = new List<Input>
        {
            Case("v2-1", [A("C")], [negative], A("C")),
            Case("v2-2", [A("C")], [negative, downstream], A("D")),
            Case("v2-3", [A("C"), A("D")], [negative, downstream], A("D")),
            Case("v2-4", ["not(" + A("C") + ")", A("D")], [downstream], A("D")),
            Case("v2-5", [A("A"), A("D")], [C("optional", "A", "D", "off")], A("D")),
            Case("v2-6", [A("D")], [], A("D")) with { Operands = [new(A("D"), Validation.Invalid)] },
            Case("v2-7", [], [C("cd", "C", "D"), C("dc", "D", "C")], A("C"), A("D")),
            Case("v2-8-pending", [A("A"), A("D")], [R("late", "is($S,A)", "not(is($S,D))", "wait")], A("D")),
            Case("v2-8-final", [A("A"), A("D")], [R("late", "is($S,A)", "not(is($S,D))")], A("D")),
            Case("and", [A("A"), A("B")], [], "and(" + A("A") + "," + A("B") + ")"),
            Case("and-eliminate", ["and(" + A("A") + "," + A("B") + ")"], [], A("A"), A("B")),
            Case("or-introduce", [A("A")], [], "or(" + A("A") + "," + A("B") + ")"),
            Case("or-no-eliminate", ["or(" + A("A") + "," + A("B") + ")", "not(" + A("A") + ")"], [], A("B")),
            Case("negation", [A("A")], [], "not(" + A("A") + ")"),
            Case("double-negation", ["not(not(" + A("A") + "))"], [], A("A")),
            Case("exact-operand-error", ["or(" + A("A") + "," + A("B") + ")"], [], "or(" + A("A") + "," + A("B") + ")")
                with { Operands = [new(A("A"), Validation.Invalid)] },
            Case("and-refuted", ["not(" + A("A") + ")"], [], "and(" + A("A") + "," + A("B") + ")"),
            Case("or-refuted", ["not(" + A("A") + ")", "not(" + A("B") + ")"], [], "or(" + A("A") + "," + A("B") + ")"),
            Case("deep-nonprefix", [A("C")], [R("deep", "is($S,C)", "is(Y(X($S)),Copy)")], "is(Y(X(T)),Copy)"),
            Case("inherited", [A("Derived")], [C("ancestor", "Derived", "Base"), R("associated", "is($S,Base)", "is(Item($S,Base),Copy)")], "is(Item(T,Base),Copy)"),
            Case("hidden-binder", ["is(T,C(A,B))", "is(T,C(A,D))"],
                [R("item", "is($S,C($U,$V))", "is(Item($S,$U),Copy)")], "is(Item(T,A),Copy)"),
            Case("hidden-self", ["is(T,C(A))"], [R("argument", "is($S,C($U))", "is($U,Copy)")], "is(A,Copy)", "is(B,Copy)"),
            Case("full-metadata", ["is(W(ref(U,originA),lengthN,contextOne),C)"],
                [R("metadata", "is($S,C)", "is(Item($S),Copy)")],
                "is(Item(W(ref(U,originA),lengthN,contextOne)),Copy)",
                "is(Item(W(ref(U,originB),lengthN,contextOne)),Copy)",
                "is(Item(W(ref(U,originA),lengthM,contextOne)),Copy)",
                "is(Item(W(ref(U,originA),lengthN,contextTwo)),Copy)"),
            Case("identity-alias", ["eq(T,U)", A("C")], [C("copy", "C", "Copy")], "is(U,Copy)"),
            Case("identity-congruence", ["eq(T,U)", "is(Box(T),C)"], [], "is(Box(U),C)"),
            Case("identity-no-inverse", ["eq(Box(T),Box(U))"], [], "eq(T,U)"),
            Case("identity-late", ["is(A,C)", "is(T,Copy)"], [R("identity", "is($S,C)", "eq(T,U)")], "is(U,Copy)"),
            Case("identity-concrete-conflict", ["eq(i32,bool)"], [], "eq(i32,bool)") with { Environment = Validation.Invalid },
            Case("identity-proper-part", ["eq(T,Child(T))"], [], "eq(T,Child(T))") with { Environment = Validation.Invalid },
            Case("cycle-independent", [A("C")], [C("cd", "C", "D"), C("dc", "D", "C")], A("D")),
            Case("candidate-pending", [], [], A("C")) with
            {
                Roots = [new("candidate", A("C"), "wait")],
                Queries = [new("discovery", "is(T,$contract)", QueryKind.Candidates), new("use", A("C"))],
            },
            Case("candidate-complete", [A("Indexable(A,B)"), A("Arithmetic(A,B)")], []) with
            {
                Queries = [new("discovery", "is(T,$contract)", QueryKind.Candidates)],
            },
            Case("callable-original-clauses", ["callable(T,Signature,Ref)", "callable(T,Signature,Ref)"], []) with
            {
                Roots = [new("r0", "callable(T,Signature,Ref)", "ok", new("clause0", "bound0", CallableBound.Confined)),
                         new("r1", "callable(T,Signature,Ref)", "ok", new("clause1", "bound1", CallableBound.PreservesResults)),
                         new("r2", A("C"), "ok")],
                Rules = [R("derived-callable", "is($S,C)", "callable($S,Signature,Ref)")],
                Queries = [new("discovery", "callable(T,$signature,$receiver)", QueryKind.Candidates)],
                Correspondence = "synthetic original-clause/bound transport; equal derived Callable has no written bound; receiver/effect selection remains owner-unmapped",
            },
            Case("callable-receiver", ["callable(T,Signature,Ref)"], [], "callable(T,Signature,Uniq)"),
            Case("scope-conditional", [A("Copy")], [], A("Copy")) with { Scope = "conditional" },
            Case("scope-outer", [], [], A("Copy")) with { Scope = "outer" },
            Case("scope-property-witness", [A("Copy")], [], A("Copy")) with { Scope = "property-path" },
            Case("scope-override", [A("Copy")], [], A("Copy")) with { Scope = "shared-implementation-environment" },
            Case("purpose", [], [], A("Copy")) with
            {
                Leaves = [new("ordinary", A("Copy"), Purpose.Ordinary, Proof.Proven, Validation.Valid, "synthetic"),
                          new("nominal", A("Copy"), Purpose.NominalCopy, Proof.Unknown, Validation.Valid, "synthetic")],
                Queries = [new("ordinary", A("Copy")), new("nominal", A("Copy"), Purpose: Purpose.NominalCopy)],
            },
            Case("owned-owner", [], [], A("Owned")) with
            {
                Leaves = [new("owned-fixed-point", A("Owned"), Purpose.StructuralOwned, Proof.Proven, Validation.Valid, "synthetic")],
                Queries = [new("owned", A("Owned"), Purpose: Purpose.StructuralOwned), new("ordinary", A("Owned"))],
                Correspondence = "synthetic existing-owner judgment; structural GFP algorithm and production correspondence unimplemented",
            },
            Case("recursive-concrete-owner", [], [], "is(Node,CopyTree)") with
            {
                Leaves = [new("recursive-classifier", "is(Node,CopyTree)", Purpose.Ordinary, Proof.Error, Validation.Valid, "synthetic")],
                Correspondence = "synthetic completed recursive-Contract owner rejection; no new classifier or native-safety evidence",
            },
            Case("required-unsupported", [A("D"), A("C")], [C("blocked", "C", "D", "unsupported")], A("D")),
            Case("opposite-resource", [A("D"), A("C")], [R("negative", "is($S,C)", "not(is($S,D))", "limit")], A("D")),
            Case("budget-chain", [A("C")], [C("cd", "C", "D")], A("D")),
            Case("growing-demand", [A("C")], [R("growing", "is(Box($S),C)", "is($S,C)")], "is(U,C)") with { Oracle = Oracle.GrowingResource },
        };
        foreach (var depth in new[] { 1, 2, 4, 8 }) rows.Add(G170("g170-d" + depth, depth));
        // Stronger spelling and unused before/after are separate actual PS0
        // captures mapped to the same d2 query theory, not invented extra roots.
        return rows.ToArray();
    }

    private static Input G170(string id, int depth)
    {
        var type = "T";
        for (var i = 0; i < depth; i++) type = "Child(" + type + ")";
        return Case(id, [A("CopyTree")],
            [C("copy-ancestor", "CopyTree", "Copy"), C("tree-ancestor", "CopyTree", "Tree"),
             R("copy-child", "is($S,CopyTree)", "is(Child($S),CopyTree)"),
             R("tree-child", "is($S,Tree)", "is(Child($S),Tree)")], "is(" + type + ",Copy)")
            with { Oracle = Oracle.FiniteDerivation, Correspondence = "synthetic theory in frozen g170-oracle-argument.md; seven PS0 input variants have separate real capture; source completion not inferred" };
    }
}
