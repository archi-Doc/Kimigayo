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
        "contract Seq\n    associate Next is Seq\n    associate Elem\n" +
        "struct Walk<T>\n    T is Seq\n    let e: T.(Seq).Elem\n    let n: Option<obj/Walk<T.(Seq).Next>>\n" +
        "struct Owner<s/T>\n    s is owner\n    T is Has\n    let v: T.(Has).Out\nstruct Whole<s/T>\n    s is owner\n    T is Has\n    let e: Owner<s/T>\n";

    private const string Need = "func need<T>(x: T)\n    T is Owned\n    ()\n";

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
    [InlineData("p7", "Fold<Cfg>", "", ConstraintProof.Proven)]
    [InlineData("f1", "TreeNode<U>", "U is OTree", ConstraintProof.Error)]
    [InlineData("f2", "TreeNode<U>", "U is Tree and Owned", ConstraintProof.Error)]
    [InlineData("f5", "Walk<U>", "U is Seq and Owned", ConstraintProof.Error)]
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

    // P < U < L < R < E (DECISIONS BU-22): an exact refutation beside a family wins at the use; the family's site, which the judgment read,
    // is still reported.
    [Fact]
    public void ARefutationBesideAFamilyIsItsOwnRecord()
    {
        var c = CompilationTestHelper.ParseSuccess(Prelude + Need +
            "func inspect<U>(x: (TreeNode<U>, Text.FixedBuffer during a))\n    U is OTree\n    need(x@move)");
        Assert.False(c.Bind().IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.Equal(
            [("NoApplicableOverload_Kd", "need(x@move)"), ("Unsupported_Kd", "Array<TreeNode<T.(Tree).Child>>")],
            TestDiagnostics.Of(c).Select(static x => (x.Code, x.Text!)).Order());
    }

    // The closure of F5 (a) (DECISIONS BU-22): a summary holding more than 64 projections is a site, which only yields the located
    // Unsupported_Kd at the stored Type past the bound, never a verdict.
    [Theory]
    [InlineData(64, "Proven", "")]
    [InlineData(65, "Error", "Unsupported_Kd T.(Many).A65")]
    public void ProjectionsPastTheBoundAreASite(int fields, string owned, string records)
    {
        var source = new StringBuilder("contract Many\n");
        for (var i = 1; i <= fields; i++)
        {
            source.Append("    associate A").Append(i).Append(" is Owned\n");
        }

        source.Append("struct Wide<T>\n    T is Many\n");
        for (var i = 1; i <= fields; i++)
        {
            source.Append("    let field").Append(i).Append(": T.(Many).A").Append(i).Append('\n');
        }

        var c = CompilationTestHelper.ParseSuccess(source + Need + "func inspect<U>(x: Wide<U>)\n    U is Many and Owned\n    need(x@move)");
        c.Bind();
        c.Binding.ReportDiagnostics();
        var f = Function(c, "inspect");
        Assert.Equal(owned, c.Binding.ProveOwned(f.Parameters[0].Type.TypeOf()!, f).ToString());
        Assert.Equal(records, string.Join(" | ", TestDiagnostics.Of(c).Select(static x => $"{x.Code} {x.Text}")));
    }

    // OQ-F5-3 (a), D5 (DECISIONS BU-22): Binding now accepts a value of a non-regular recursive Type where Owned is required, and the old
    // Emission meets its existing generation depth limit with it, as with any value of that Type; the acceptance change adds no runtime
    // form to the old path, so no old-path guard is needed.
    [Fact]
    public void ANewlyOwnedNonRegularValueMeetsTheOldGenerationLimit()
    {
        var old = MinimalEmissionTest.Analyze(
            "struct Chain<T>\n    public let next: Option<obj/Chain<(T, T)>>\n    public init()\n        self.next = .None\n" + Need +
            "let c = Chain<i32>.init()\nneed(c@move)\nConsole.writeLine(\"chain\")",
            codegen: CompilerCodegen.Legacy);
        Assert.True(old.Binding.Result.IsComplete, Describe(old));
        Assert.False(old.WriteIr(TextWriter.Null, out var failure));
        Assert.Contains("depth limit of 64 levels", failure, StringComparison.Ordinal);
    }

    // Immutable after Bind and pooled: a warm rebind that solves the summaries again allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRebindSolvesTheSummariesWithoutAllocating()
    {
        var c = CompilationTestHelper.ParseSuccess(Prelude + Need +
            "func f(a: Chain<i32>, b: Ping<i32>, c: Nest<i32>)\n    need(a@move)\n    need(b@move)\n    need(c@move)\n" +
            "func g<U>(a: Fold<U>)\n    U is OHas\n    need(a@move)");
        Assert.True(c.Binding.Bind(BindingMode.Final).IsComplete, Describe(c));
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }

    private static FunctionKoto Function(Compilation c, string name) => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(f => f.Name == name);

    private static string Describe(Compilation c) => string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}"));
}
