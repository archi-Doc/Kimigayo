// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>A4's widening (S5; SPEC 15.6.4, 8.3.3; docs/dev/DECISIONS.md R4-M76, R4-M87): a summary keeps no parameter-dependent effect
/// on a projection with a recursive base; it has unknown environment effects instead.</summary>
public sealed partial class MirEffectsTest
{
    private const string Visited = "IncompatibleContractImplementation_Kd Self is Visit 1";

    private const string RecursiveTree = "contract Tree\n    associate Child is Tree\n    func child(self: ref/Self) -> Option<Self.Child>\n        effect confined\n";

    private const string CopyTree = RecursiveTree + "contract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\n";

    private const string Walk = RecursiveTree + "group Trees\n    public func walk<T>(node: T, depth: i32)\n        T is Tree\n        if depth == 0 => return\n" +
        "        match node.child()\n            .Some(let next) => Trees.walk(next@move, depth - 1)\n            .None => return\n";

    // Premises under which every Type of a growing chain over Tree is U, which is Copy: the chain's only unclassified effect was the limit.
    private const string TreeLoop = "        U is Tree\n        U is Copy\n        U.(Tree).Child is U\n";

    private const string Mutual = "contract A\n    associate X is B\n    func toB(self: ref/Self) -> Option<Self.X>\n        effect confined\n" +
        "contract B\n    associate Y is A\n    func toA(self: ref/Self) -> Option<Self.Y>\n        effect confined\n" + MutualF + MutualG + MutualTail;

    private const string TwoStep = "contract B\n    associate Y\n    func toA(self: ref/Self) -> Option<Self.Y>\n        effect confined\n" +
        "contract A\n    associate X is B\n    Self.(A).X.(B).Y is A\n    func toB(self: ref/Self) -> Option<Self.X>\n        effect confined\n" +
        MutualF + MutualG + "        U.(B).Y is A\n" + MutualTail;

    // f<T: A> destroys T and calls g with T.X; g<U: B> destroys U and calls f with U.Y.
    private const string MutualF = "group M\n    public func f<T>(value: T, budget: i32)\n        T is A\n        if budget == 0 => return\n" +
        "        match value.toB()\n            .Some(let next) => M.g(next@move, budget - 1)\n            .None => return\n";

    private const string MutualG = "    public func g<U>(value: U, budget: i32)\n        U is B\n";

    private const string MutualTail = "        if budget == 0 => return\n        match value.toA()\n            .Some(let next) => M.f(next@move, budget - 1)\n" +
        "            .None => return\n";

    private const string MutualLoop = "        U is A\n        U is Copy\n        U.(A).X is Copy\n        U.(A).X.(B).Y is U\n";

    private const string Refinement = "contract Node\n    associate Child\n    func child(self: ref/Self) -> Option<Self.Child>\n        effect confined\n" +
        "contract Rec: Node\n    Self.(Node).Child is Rec\ngroup G\n    public func f<T>(value: T, budget: i32)\n        T is Rec\n" +
        "        if budget == 0 => return\n        match value.child()\n            .Some(let next) => G.f(next@move, budget - 1)\n            .None => return\n";

    private const string DeepBase = "contract Box\n    associate Inner\n    func inner(self: ref/Self) -> Self.Inner\n        effect confined\n" +
        "contract Tree3\n    associate Child is Tree3\n    associate Item is Box\n    func item(self: ref/Self) -> Self.Item\n        effect confined\n" +
        "group G\n    public func peek<T>(node: ref/T)\n        T is Tree3\n        T.(Tree3).Item is Copy\n        let inner = node.item().inner()\n";

    private const string Cleanup = RecursiveTree + "struct Holder<T>\n    T is Tree\n    public var node: T\n    public init(node: T) => self.node = node@move\n" +
        "    drop\n        Trees.look(self.node@ref)\ngroup Trees\n    public func look<T>(n: ref/T)\n        T is Tree\n        match n.child()\n" +
        "            .Some(let next) => Trees.make(next@move)\n            .None => return\n    public func make<U>(n: U)\n        U is Tree\n" +
        "        let h = Holder.init(n@move)\n";

    // k<U: Tree> destroys U; h<T> calls k with T.Child under its premise, which follows.
    private const string Destroyer = CopyTree + "group H\n    public func k<U>(u: U)\n        U is Tree\n        ()\n    public func h<T>(t: ref/T)\n";

    private const string DestroyerBody = "        match t.child()\n            .Some(let next) => H.k(next@move)\n            .None => return\n";

    private const string SelfRecursion = CopyTree + "group H\n    public func f<T>(t: ref/T, budget: i32)\n        T is Tree\n        if budget == 0 => return\n" +
        "        match t.child()\n            .Some(let next) => ()\n            .None => return\n        H.f<T>(t, budget - 1)\n";

    private const string Swap = "contract Source\n    associate Item\n    func make(self: ref/Self) -> Self.Item\n        effect confined\n" +
        "group Ping\n    public func ping<L, R>(left: ref/L, right: ref/R, budget: i32)\n        L is Source\n        R is Source\n" +
        "        if budget == 0 => return\n        let item = left.make()\n        Ping.ping(right, left, budget - 1)\n" +
        "struct Name\n    Self is Source\n    public var n: i32 = 0\n    public func make(self: ref/Self) -> string => \"name\"\n";

    private const string Chain = "contract C1\n    associate A1 is C2\n    func a(self: ref/Self) -> Self.A1\n        effect confined\n" +
        "contract C2\n    associate A2 is C3\n    func a(self: ref/Self) -> Self.A2\n        effect confined\n" +
        "contract C3\n    associate A3\n    func a(self: ref/Self) -> Self.A3\n        effect confined\n" +
        "group G\n    public func f<T>(t: ref/T, budget: i32)\n        T is C1\n        if budget == 0 => return\n        let x = t.a().a().a()\n" +
        "        G.f<T>(t, budget - 1)\nstruct L2\n    Self is C3\n    associate C3.A3 is i32\n    public var n: i32 = 0\n" +
        "    public func a(self: ref/Self) -> i32 => 1\nstruct L1\n    Self is C2\n    associate C2.A2 is L2\n    public var n: i32 = 0\n" +
        "    public func a(self: ref/Self) -> L2 => L2.init()\nstruct L0\n    Self is C1\n    associate C1.A1 is L1\n    public var n: i32 = 0\n" +
        "    public func a(self: ref/Self) -> L1 => L1.init()\n";

    // SPEC 15.6.4 (S5): growth through a recursive base ends at once, also under a confined caller whose premises classify every effect it
    // replaces (each was the limit's Unsupported_Kd before): the walk on T.Child (model case 1), mutual recursion through A.X is B and B.Y
    // is A (3), a refinement's Self.(Node).Child is Rec (10), a cleanup cycle through a drop body with no recursive call (15) and a
    // Constraint on a two-step projection of Self (18). An effect that does not grow widens too (each valid before): T.Item.Inner, whose
    // immediate base T.Item is a Box, under a Tree3 (13: any base), and stronger callers under CopyTree of a destroyer of T.Child (16) and
    // of a recursion on T itself (17). Kept: a destruction proven Copy under the summary's premises (7), and effects on projections of
    // Contracts that are not recursive, swapped between calls (4) or along a finite chain with no limit (12).
    [Theory]
    [InlineData(Walk, TreeLoop, "Trees.walk(node, 2)", Visited)]
    [InlineData(Mutual, MutualLoop, "M.f(node, 2)", Visited)]
    [InlineData(Refinement, "        U is Rec\n        U is Copy\n        U.(Node).Child is U\n", "G.f(node, 2)", Visited)]
    [InlineData(Cleanup, TreeLoop, "Trees.make(node)", Visited)]
    [InlineData(TwoStep, MutualLoop, "M.f(node, 2)", Visited)]
    [InlineData(DeepBase, "        U is Tree3\n        U is Copy\n        U.(Tree3).Item is Copy\n        U.(Tree3).Item.(Box).Inner is Copy\n", "G.peek(node@ref)", Visited)]
    [InlineData(Destroyer + "        T is Tree\n" + DestroyerBody, "        U is CopyTree\n", "H.h(node@ref)", Visited)]
    [InlineData(SelfRecursion, "        U is CopyTree\n", "H.f(node@ref, 2)", Visited)]
    [InlineData(Destroyer + "        T is CopyTree\n" + DestroyerBody, "        U is CopyTree\n", "H.h(node@ref)", "")]
    [InlineData(Swap, "        U is Copy\n", "let a = Name.init()\n        let b = Name.init()\n        Ping.ping(a@ref, b@ref, 2)", "")]
    [InlineData(Chain, "        U is Copy\n", "let l = L0.init()\n        G.f(l@ref, 2)", "")]
    public void AnEffectOnARecursiveBaseWidens(string declarations, string premises, string call, string expected)
        => Assert.Equal(
            Expected(expected),
            Problems(declarations + "contract Visit\n    func visit<U>(self: ref/Self, node: U)\n" + premises + "        effect confined\nstruct V\n" +
                "    Self is Visit\n    public func visit<U>(self: ref/Self, node: U)\n" + premises + $"        {call}\n"));

    // SPEC 15.6.4, 8.11.4: a widened effect fails a confined Callable bound at its binding, which was the limit's Unsupported_Kd before.
    [Fact]
    public void AWidenedEffectFailsACallableBound()
        => Assert.Equal(
            ["UnsatisfiedEffectBound_Kd func [u]() => Trees.walk(u, 2) 18446744073709551615"],
            Problems(Walk + "func apply<F>(f: ref/F)\n    F is Callable<() -> ()>\n        effect confined\n    f()\nfunc user<U>(u: U)\n    U is Tree\n" +
                "    U is Copy\n    U.(Tree).Child is U\n    apply(func [u] () => Trees.walk(u, 2))\n"));

    // SPEC 15.6.4 (S5): the fixed point needs no bound: a walk whose recursion grows T to T.Child takes no more evaluations than one that
    // recurses on T itself (before, it took one round per projection step up to G128's bound of 64).
    [Fact]
    public void AGrowingSummaryNeedsNoBound()
    {
        var evaluations = new int[2];
        string[] recursions = ["Trees.walk(next@ref, depth - 1)", "Trees.walk(node, depth - 1)"];
        for (var i = 0; i < recursions.Length; i++)
        {
            var c = MirBuildTest.Compile(RecursiveTree + "group Trees\n    public func walk<T>(node: ref/T, depth: i32)\n        T is Tree\n" +
                $"        if depth == 0 => return\n        match node.child()\n            .Some(let next) => {recursions[i]}\n            .None => return\n" +
                "public func main() => ()\n");
            var program = c.BuildMir();
            Assert.Empty(Problems(c, program));
            var (cases, flow) = (new MirCases(c.Types), new MirDataflow(new MirReach()));
            var effects = new MirEffects(c.Types, cases, flow);
            Analyze(effects, flow, cases, program, new MirReport());
            evaluations[i] = effects.Evaluations;
        }

        Assert.True(evaluations[0] <= evaluations[1] + 2, $"evaluations {evaluations[0]} (growing), {evaluations[1]} (on T)");
    }
}
