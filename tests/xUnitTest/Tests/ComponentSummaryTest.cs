// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>The per-declaration component summaries (SPEC 15.2.3, 15.3.5; docs/dev/DECISIONS.md BU-22): Owned of a nominal instance read
/// through its declaration's summary, never by instantiating storage; a projection family's or a limit's located Unsupported_Kd at its
/// stored Type. Rows follow the component-summary prototype's cases (draft/Sketches/A5 Step 2/ComponentSummaryPrototype.md), Owned
/// column; the Transferable column waits for unit 2b-4.</summary>
public class ComponentSummaryTest
{
    private const string Prelude =
        "struct Item\n    let x: i32\nopen struct Base\n    let x: i32\nstruct Box<T>\n    let value: T\nstruct Holder\n    let item: rc/Item\n" +
        "struct Phantom<T>\nenum Msg\n    A(i32)\n    B(rc/Item)\nstruct Hook\n    let next: Option<obj/Hook>\n    let call: () -> ()\n" +
        "struct Node<T> {a}\n    let value: ref/T during a\n    let next: Option<rc/Node<T>{n}>\n        origin n.a == a\n" +
        "struct Linked<T>\n    let value: T\n    let next: Option<rc/Linked<T>>\nstruct Nest<T>\n    let value: T\n    let more: Array<Nest<(T, T)>>\n" +
        "struct Nest15<T>\n    let value: T\n    let next: Option<rc/Nest15<(T, T)>>\nstruct Chain<T>\n    let next: Option<obj/Chain<(T, T)>>\n" +
        "struct NodeFix<T>\n    let next: Option<obj/NodeFix<(i32, i32)>>\n" +
        "struct Ping<T>\n    let p: Option<obj/Pong<(T, i32)>>\nstruct Pong<T>\n    let q: Option<obj/Ping<Option<T>>>\n    let v: T\n" +
        "struct D1<T>\n    let d: D2<(T, T)>\nstruct D2<T>\n    let d: D3<(T, T)>\nstruct D3<T>\n    let d: D4<(T, T)>\nstruct D4<T>\n    let d: D5<(T, T)>\n" +
        "struct D5<T>\n    let d: D6<(T, T)>\nstruct D6<T>\n    let v: T\n" +
        "open struct Opn<T>\n    let v: T\nstruct Grow<T>\n    T is ObjectPayload\n    let o: obj/T\n    let g: Option<obj/Grow<Opn<T>>>\n" +
        "contract Has\n    associate Out\ncontract OHas: Has, Owned\n    Self.(Has).Out is Owned\n" +
        "struct Cfg\n    Self is Has\n    associate Has.Out is i32\n    let x: i32\n" +
        "struct Pair2<X>\n    let a: X\n    let b: X\n    Self is Has when X is Has\n        associate Has.Out is X.(Has).Out\n" +
        "struct PHold<T>\n    T is Has\n    let v: T.(Has).Out\nstruct Fold<T>\n    T is Has\n    let v: T.(Has).Out\n    let f: Option<obj/Fold<Pair2<T>>>\n" +
        "contract Tree\n    associate Child is Tree\ncontract OTree: Tree, Owned\n    Self.(Tree).Child is OTree\n" +
        "struct TreeNode<T>\n    T is Tree\n    let v: T\n    let kids: Array<TreeNode<T.(Tree).Child>>\n" +
        "contract Marker\n    associate M\n" +
        "struct TreeM<T>\n    T is Tree\n    Self is Marker\n    associate Marker.M is i32\n    let v: T\n    let kids: Array<TreeM<T.(Tree).Child>>\n" +
        "contract RHas\n    associate Out is RHas\n" +
        "struct P2<X>\n    let a: X\n    Self is RHas when X is RHas\n        associate RHas.Out is X.(RHas).Out.(RHas).Out\n" +
        "struct FoldR<T>\n    T is RHas\n    let v: T.(RHas).Out\n    let f: Option<obj/FoldR<P2<T>>>\n" +
        "contract Seq\n    associate Next is Seq\n    associate Elem\n" +
        "struct Walk<T>\n    T is Seq\n    let e: T.(Seq).Elem\n    let n: Option<obj/Walk<T.(Seq).Next>>\n" +
        "struct Owner<s/T>\n    s is owner\n    T is Has\n    let v: T.(Has).Out\nstruct Whole<s/T>\n    s is owner\n    T is Has\n    let e: Owner<s/T>\n";

    private const string Need = "func need<T>(x: T)\n    T is Owned\n    ()\n";

    private const string Site = "Unsupported_Kd Array<TreeNode<T.(Tree).Child>>";

    // The prototype's id, the queried Type, its premises (U a Type parameter when they name it) and SPEC's Owned, with a projection
    // family's located U as Error (F5 (a)). The Origin rows name an input Origin `a` or prove `x.a == static`.
    [Theory]
    [InlineData("c1", "i32", "", ConstraintProof.Proven)]
    [InlineData("c2", "string", "", ConstraintProof.Proven)]
    [InlineData("c3", "rc/Item", "", ConstraintProof.Proven)]
    [InlineData("c5", "ref/i32 during static", "", ConstraintProof.Proven)]
    [InlineData("c6", "ref/i32 during a", "", ConstraintProof.Unknown)]
    [InlineData("c7", "arc/Holder", "", ConstraintProof.Proven)]
    [InlineData("c12", "raw/i32", "", ConstraintProof.Proven)]
    [InlineData("c18", "obj/Base", "", ConstraintProof.Proven)]
    [InlineData("c21", "obj/Holder", "", ConstraintProof.Proven)]
    [InlineData("c23", "Array<rc/Item>", "", ConstraintProof.Proven)]
    [InlineData("c24", "Dictionary<i32, rc/Item>", "", ConstraintProof.Proven)]
    [InlineData("c29", "[0 of rc/Item]", "", ConstraintProof.Proven)]
    [InlineData("c30", "Phantom<rc/Item>", "", ConstraintProof.Proven)]
    [InlineData("c31", "Phantom<ref/i32 during a>", "", ConstraintProof.Unknown)]
    [InlineData("c33", "Msg", "", ConstraintProof.Proven)]
    [InlineData("c34", "Hook", "", ConstraintProof.Proven)]
    [InlineData("c39", "Text.FixedBuffer during a", "", ConstraintProof.Refuted)]
    [InlineData("c40", "Box<Text.FixedBuffer during a>", "", ConstraintProof.Refuted)]
    [InlineData("c41", "Node<i32>", "origin x.a == static", ConstraintProof.Proven)]
    [InlineData("c42", "Node<i32>", "", ConstraintProof.Unknown)]
    [InlineData("r1", "Nest<i32>", "", ConstraintProof.Proven)]
    [InlineData("r1b", "Nest<Text.FixedBuffer during a>", "", ConstraintProof.Refuted)]
    [InlineData("r3", "Nest15<i32>", "", ConstraintProof.Proven)]
    [InlineData("r4", "Linked<i32>", "", ConstraintProof.Proven)]
    [InlineData("r5", "Chain<i32>", "", ConstraintProof.Proven)]
    [InlineData("r5b", "Chain<ref/i32 during a>", "", ConstraintProof.Unknown)]
    [InlineData("r6", "Chain<rc/Item>", "", ConstraintProof.Proven)]
    [InlineData("r7", "NodeFix<i32>", "", ConstraintProof.Proven)]
    [InlineData("r8", "Box<Box<i32>>", "", ConstraintProof.Proven)]
    [InlineData("r9", "Ping<i32>", "", ConstraintProof.Proven)]
    [InlineData("r10", "Ping<Text.FixedBuffer during a>", "", ConstraintProof.Refuted)]
    [InlineData("r11", "D1<Text.FixedBuffer during a>", "", ConstraintProof.Refuted)]
    [InlineData("r12", "Grow<Item>", "", ConstraintProof.Proven)]
    [InlineData("p1", "PHold<U>", "U is Has and Owned", ConstraintProof.Unknown)]
    [InlineData("p2", "PHold<U>", "U is OHas", ConstraintProof.Proven)]
    [InlineData("p5", "PHold<Cfg>", "", ConstraintProof.Proven)]
    [InlineData("p6", "Fold<U>", "U is OHas", ConstraintProof.Proven)]
    [InlineData("p6b", "Fold<U>", "U is Has and Owned", ConstraintProof.Unknown)] // Fold's summary keeps T.(Has).Out.
    [InlineData("p7", "Fold<Cfg>", "", ConstraintProof.Proven)]
    [InlineData("f1", "TreeNode<U>", "U is OTree", ConstraintProof.Error)]
    [InlineData("f2", "TreeNode<U>", "U is Tree and Owned", ConstraintProof.Error)]
    [InlineData("f5", "Walk<U>", "U is Seq and Owned", ConstraintProof.Error)]
    [InlineData("fa", "FoldR<U>", "U is RHas and Owned", ConstraintProof.Error)] // F-a: a conformance binding lengthens the projection.
    [InlineData("w1", "Whole<Cfg>", "", ConstraintProof.Proven)]
    public void OwnedFollowsTheDeclarationSummaries(string id, string type, string premises, ConstraintProof expected)
    {
        var generic = premises.Contains('U', StringComparison.Ordinal) ? "<U>" : string.Empty;
        var c = CompilationTestHelper.ParseSuccess(Prelude + $"func inspect{generic}(x: {type})\n    {(premises.Length == 0 ? "()" : premises + "\n    ()")}");
        Assert.True(c.Bind().IsComplete, id + ": " + Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(expected, c.Binding.ProveOwned(f.Parameters[0].Type.TypeOf()!, f));
    }

    // F5 (a): a requirement resting on a projection family reports one located Unsupported_Kd at the stored Type naming the family's
    // projection link, however many uses read it; each use's own record is derived from it (SPEC 23.1.2), never a Language error.
    [Fact]
    public void AFamilyRequirementRestsOnOneLocatedUnsupported()
    {
        var c = CompilationTestHelper.ParseSuccess(Prelude + Need +
            "func inspect<U>(a: TreeNode<U>)\n    U is OTree\n    need(a@move)\nfunc again<U>(a: TreeNode<U>)\n    U is Tree and Owned\n    need(a@move)");
        Assert.False(c.Bind().IsComplete);
        c.Binding.ReportDiagnostics();
        var record = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal(("Unsupported_Kd", "Array<TreeNode<T.(Tree).Child>>"), (record.Code, record.Text));
        var calls = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Where(static x => x.ToString() == "need(a@move)").ToArray();
        Assert.Equal(2, calls.Length);
        Assert.All(calls, x => Assert.Equal(BindingState.Unresolved, c.Semantics.StateOf(x))); // Each rests on the site (CompleteDependent): a derived record.
    }

    // The ruling on the limit's answer (DECISIONS BU-22): a candidate whose Constraint reads a family answers Error resting on the site,
    // so the call rests on it and selects no other candidate.
    [Fact]
    public void ACallSelectionReadingAFamilyRestsOnItsSite()
    {
        var c = CompilationTestHelper.ParseSuccess(Prelude +
            "func pick<T>(x: T, y: i32) -> i32\n    T is Owned\n    return 1\nfunc pick<T>(x: T, y: i64) -> i32\n    return 2\n" +
            "func inspect<U>(a: TreeNode<U>) -> i32\n    U is OTree\n    return pick(a@move, 1)");
        Assert.False(c.Bind().IsComplete);
        c.Binding.ReportDiagnostics();
        var record = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal(("Unsupported_Kd", "Array<TreeNode<T.(Tree).Child>>"), (record.Code, record.Text));
        var call = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Single(static x => x.ToString().StartsWith("pick(", StringComparison.Ordinal));
        Assert.Equal(BindingState.Unresolved, c.Semantics.StateOf(call)); // It rests on the site and selects no candidate.
        Assert.Null(c.Semantics.CallOf(call));
    }

    // P < U < L < R < E (DECISIONS BU-22): a limit is a value of the proofs that combine it, the connectives, a clause list and a
    // candidate's applicability; only where the verdict is no longer combined and is L does it answer Error resting on the site, whose
    // one record a failing check takes (its own derived); an exact refutation or proof elsewhere decides. Readers that record their own
    // failure, such as a common Function conversion, rest on it too (SPEC 23.1.2). An unread family raises nothing.
    [Theory]
    [InlineData("a1", "func reject<T>(x: T)\n    T is not Owned\n    ()\nfunc inspect<U>(x: (TreeNode<U>, Text.FixedBuffer during a))\n    U is OTree\n    reject(x@move)", "")]
    [InlineData("a2", "func reject<T>(x: T)\n    T is not Owned\n    ()\nfunc inspect<U>(x: TreeNode<U>)\n    U is OTree\n    reject(x@move)", Site)]
    [InlineData("a3", Need + "func inspect<U>(x: (TreeNode<U>, Text.FixedBuffer during a))\n    U is OTree\n    need(x@move)", "NoApplicableOverload_Kd need(x@move)")]
    [InlineData("b1", "func pick<T>(x: T, y: i32) -> i32\n    T is Owned\n    return 1\nfunc pick<T>(x: T, y: i64) -> i32\n    return 2\nfunc inspect<U>(x: (TreeNode<U>, Text.FixedBuffer during a)) -> i32\n    U is OTree\n    return pick(x@move, 1)", "")]
    [InlineData("c1", "func need2<T>(x: T)\n    T is Owned and Copy\n    ()\nfunc inspect<U>(x: TreeNode<U>)\n    U is OTree\n    need2(x@move)", "NoApplicableOverload_Kd need2(x@move)")]
    [InlineData("c2", "func need3<T>(x: T)\n    T is Owned\n    T is Copy\n    ()\nfunc inspect<U>(x: TreeNode<U>)\n    U is OTree\n    need3(x@move)", "NoApplicableOverload_Kd need3(x@move)")]
    [InlineData("d1", "func needOr<T>(x: T)\n    T is Marker or Owned\n    ()\nfunc inspect<U>(x: TreeM<U>)\n    U is OTree\n    needOr(x@move)", "")]
    [InlineData("d2", "func needOr<T>(x: T)\n    T is Owned or Marker\n    ()\nfunc inspect<U>(x: TreeM<U>)\n    U is OTree\n    needOr(x@move)", "")]
    [InlineData("d4", "func needOr<T>(x: T)\n    T is Copy or Owned\n    ()\nfunc inspect<U>(x: TreeNode<U>)\n    U is OTree\n    needOr(x@move)", Site)]
    [InlineData("k1", Need + "func reject<T>(x: T)\n    T is not Owned\n    ()\nfunc inspect<U>(x: TreeNode<U>, y: (TreeNode<U>, Text.FixedBuffer during a))\n    U is OTree\n    reject(y@move)\n    need(x@move)", Site)]
    [InlineData("f1", "func identity<T>(value: T) -> T => value@move\nfunc inspect<U>(x: TreeNode<U>)\n    U is OTree\n    let f: (TreeNode<U>) -> TreeNode<U> = identity\n    ()", Site)]
    [InlineData("f2", "func inspect<U>(x: TreeNode<U>)\n    U is OTree\n    let f: () -> () = func [x@move] () => ()\n    ()", Site)]
    [InlineData("g1", "func inspect<U>(x: TreeNode<U>)\n    U is OTree\n    let y = x@move\n    ()", "")]
    [InlineData("h1", Need + "func inspect<U>(x: FoldR<U>)\n    U is RHas and Owned\n    need(x@move)", "Unsupported_Kd Option<obj/FoldR<P2<T>>>")]
    [InlineData("two", Need + "func inspect<U>(a: TreeNode<U>)\n    U is OTree\n    need(a@move)\nfunc again<U>(a: TreeNode<U>)\n    U is Tree and Owned\n    need(a@move)", Site)]
    public void ALimitIsSettledWhereItsVerdictIsNoLongerCombined(string id, string source, string records)
    {
        var c = CompilationTestHelper.ParseSuccess(Prelude + source);
        var complete = c.Bind().IsComplete;
        c.Binding.ReportDiagnostics();
        Assert.Equal(records, string.Join(" | ", TestDiagnostics.Of(c).Select(static x => $"{x.Code} {x.Text}")));
        Assert.True(complete == (records.Length == 0), id);
    }

    // SPEC 8.3.3 (DECISIONS BU-22): a conformance binding that uses itself at a deeper instance grows a proof without end, at the use
    // (the growth guard) and in a summary's walk (Error resting on the stored Type); the conformance has its own record, and Binding ends.
    // A deeper instance of a declaration reached through another conformance is no such growth and stays exact.
    [Theory]
    [InlineData("n1", "func inspect(a: PH<W<i32>>)\n    need(a@move)", "PH<W<(X, X)>>", "Unknown")]
    [InlineData("n2", "struct S\n    let p: PH<W<i32>>\nfunc inspect(a: S)\n    need(a@move)", "PH<W<(X, X)>>", "Error")]
    [InlineData("x1", "struct S\n    let p: PH<W<i32>>\nfunc inspect(a: S)\n    need(a@move)", "PH<V<Option<X>>>", "Proven")]
    [InlineData("x2", "func inspect(a: PH<W<i32>>)\n    need(a@move)", "PH<V<Option<X>>>", "Proven")]
    public void AConformanceUsingItselfIsItsOwnRecord(string id, string use, string binding, string owned)
    {
        var c = CompilationTestHelper.ParseSuccess(Need + "contract Has2\n    associate Out\nstruct V<Y>\n    Self is Has2\n    associate Has2.Out is i32\n" +
            "struct W<X>\n    Self is Has2\n    associate Has2.Out is " + binding + "\nstruct PH<T>\n    T is Has2\n    let v: T.(Has2).Out\n" + use);
        var complete = c.Bind().IsComplete;
        c.Binding.ReportDiagnostics();
        var records = TestDiagnostics.Of(c).Select(static x => $"{x.Code} {x.Text}").ToArray();
        var f = Function(c, "inspect");
        Assert.Equal(owned, c.Binding.ProveOwned(f.Parameters[0].Type.TypeOf()!, f).ToString());
        Assert.True(complete == (owned == "Proven"), id);
        Assert.Equal(!complete, records.Contains("UnprovenConstraint_Kd Self is Has2"));
        Assert.DoesNotContain(records, static x => x.StartsWith("Unsupported_Kd", StringComparison.Ordinal));
    }

    // The closure of F5 (a) (DECISIONS BU-22): a projection of more than 64 steps (a conformance binding can lengthen a projection with
    // no projection link) makes its summary a site, which only yields the located Unsupported_Kd at its stored Type, never a verdict; a
    // shorter one is judged (a stored projection of several steps stays Unknown, as before), and many short projections stay exact.
    [Theory]
    [InlineData(64, 1, "Unknown", "UnprovenConstraint_Kd")]
    [InlineData(65, 1, "Error", "Unsupported_Kd")]
    [InlineData(1, 65, "Proven", "")]
    public void ProjectionsPastTheBoundAreASite(int steps, int fields, string owned, string record)
    {
        var source = new StringBuilder("contract Deep: Owned\n    associate Next is Deep\ncontract Many\n");
        for (var i = 1; i <= fields; i++)
        {
            source.Append("    associate A").Append(i).Append(" is Deep\n");
        }

        source.Append("struct Wide<T>\n    T is Many\n");
        for (var i = 1; i <= fields; i++)
        {
            source.Append("    let field").Append(i).Append(": T.(Many).A").Append(i).Append(string.Concat(Enumerable.Repeat(".(Deep).Next", steps - 1))).Append('\n');
        }

        var c = CompilationTestHelper.ParseSuccess(source + Need + "func inspect<U>(x: Wide<U>)\n    U is Many and Owned\n    need(x@move)");
        Assert.Equal(record.Length == 0, c.Bind().IsComplete);
        c.Binding.ReportDiagnostics();
        var f = Function(c, "inspect");
        Assert.Equal(owned, c.Binding.ProveOwned(f.Parameters[0].Type.TypeOf()!, f).ToString());
        Assert.Equal(record, string.Join(" | ", TestDiagnostics.Of(c).Select(static x => x.Code)));
    }

    // OQ-F5-3 (a), D5 (DECISIONS BU-22): Binding now accepts a value of a non-regular recursive Type where Owned is required, and check
    // meets the existing generation limits with it, as with any value of that Type, on every pipeline (DECISIONS R5-M156): each call
    // requesting an instance over Chain<i32>, whose storage walk passes Binding's bound, is the capability table's storage-nesting gap
    // (PLAN G128, PD30's (e) row), and the value's drop glue grows through its object handle, Chain<(T, T)> at each step, so the collector
    // stops it at the destroyed value after 16 generations (DECISIONS R5-M170); the acceptance change adds no runtime form.
    [Fact]
    public void ANewlyOwnedNonRegularValueMeetsTheGenerationLimits()
    {
        var c = MinimalEmissionTest.Analyze(
            "struct Chain<T>\n    public let next: Option<obj/Chain<(T, T)>>\n    public init()\n        self.next = .None\n" + Need +
            "let c = Chain<i32>.init()\nneed(c@move)\nConsole.writeLine(\"chain\")");
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        Assert.Equal(
            "Unsupported_Kd Chain<i32>.init() | Unsupported_Kd need(c@move) | GenerationResourceLimit_Kd c@move", MinimalEmissionTest.Problems(c));
    }

    // Immutable after Bind and pooled: a warm rebind that solves the summaries again allocates nothing, a family's settled limit included.
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("func f(a: Chain<i32>, b: Ping<i32>, c: Nest<i32>)\n    need(a@move)\n    need(b@move)\n    need(c@move)\nfunc g<U>(a: Fold<U>)\n    U is OHas\n    need(a@move)", true)]
    [InlineData("func f<U>(a: TreeNode<U>, b: Walk<U>)\n    U is OTree and Seq\n    need(a@move)\n    need(b@move)", false)]
    public void AWarmRebindSolvesTheSummariesWithoutAllocating(string source, bool complete)
    {
        var c = CompilationTestHelper.ParseSuccess(Prelude + Need + source);
        Assert.Equal(complete, c.Binding.Bind(BindingMode.Final).IsComplete);
        c.Binding.Bind(BindingMode.Final);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }

    private static FunctionKoto Function(Compilation c, string name) => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(f => f.Name == name);

    private static string Describe(Compilation c) => string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}"));
}
