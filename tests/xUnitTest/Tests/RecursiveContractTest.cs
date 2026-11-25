// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>The recursive-base view (SPEC 8.3.3, 15.6.4; docs/dev/DECISIONS.md R4-M87): <c>TyCtxt.RecursiveBase</c>, whether one of a
/// projection's bases is proven under a definition's premises to conform to a recursive Contract, observed on the Type of a probe
/// function's parameter under the probe's premises.</summary>
public sealed partial class RecursiveContractTest
{
    private const string Tree = "contract Tree\n    associate Child is Tree\n";

    private const string Mutual = "contract A\n    associate X is B\ncontract B\n    associate Y is A\n";

    private const string Refinement = "contract Node\n    associate Child\ncontract Rec: Node\n    Self.(Node).Child is Rec\n";

    private const string TwoStep = "contract B\n    associate Y\ncontract A\n    associate X is B\n    Self.(A).X.(B).Y is A\n";

    private const string Forest = Tree + "contract Forest\n    associate Root is Tree\n";

    private const string Copies = "contract Tree\n    associate Child is Tree\ncontract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\n";

    private const string Outers = "contract Holder\n    associate Item\ncontract CopyHolder: Holder\n    Self.(Holder).Item is Copy\ncontract Outer\n    associate Inner is Holder\ncontract CopyOuter: Outer\n    Self.(Outer).Inner is CopyHolder\n";

    private const string Walks = "contract Sink\n    func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        effect confined\ncontract Tree\n    associate Child is Tree\n    func child(self: ref/Self) -> Option<Self.Child>\n        effect confined\ncontract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\ngroup Trees\n    public func two<T>(node: T)\n        T is Tree\n        match node.child()\n            .Some(let next) => Trees.one(next@move)\n            .None => return\n    public func one<T>(node: T)\n        T is Tree\n        match node.child()\n            .Some(let next) => return\n            .None => return\nstruct Holder<T>\n    T is CopyTree\n    Self is Sink\n    var node: T\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n";

    private const string Conditions = "contract D\ncontract C\n    associate X\nstruct Box<A>\n    Self is C when A is D\n    associate X is i32\ncontract K\n    associate Y\n";

    private const string Clauses = "    Self.(K).Y is M\n    Box<Self.(K).Y>.(C).X is Copy\nfunc needs<V>(v: ref/V)\n    V is D\n    ()\n";

    private const string IntTrees = "contract Tree\n    associate Child is Tree\n    associate Value\ncontract IntTree: Tree\n    Self.(Tree).Child is IntTree\n    Self.(Tree).Value is PrimitiveInteger\n";

    private const string IntTwice = "group H\n    public func twice<T>(value: T.(Tree).Child.(Tree).Value) -> (T.(Tree).Child.(Tree).Value, T.(Tree).Child.(Tree).Value)\n        T is IntTree\n        return (value, value)\n";

    private const string Marks = "contract Mark\nfunc needMark<V>(v: ref/V)\n    V is Mark\n    ()\n";

    // R7-C2c2 c2-5a (G837, the Middle's C-304 R2, rebuilt from its text): MT's clause names MT's own parameter.
    private const string MarkArgs = MarkTree + "func needMark<V, W>(v: ref/V)\n    V is Mark<W>\n    ()\nfunc needMT<V, W>(v: ref/V)\n    V is MT<W>\n    ()\n";

    private const string MarkTree = "contract Tree\n    associate Child is Tree\ncontract Mark<A>\ncontract MT<A>: Tree, Mark<A>\n    Self.(Tree).Child is MT<A>\n";

    // MT's requirement whose clause names MT's own reference, and a generic struct conforming to MT<i32> that implements it.
    private const string Put = "    func put<U>(self: ref/Self, u: ref/U) -> i32\n        U is MT<A>\n";

    private const string PutS = "struct S<T>\n    T is MT<i32>\n    Self is MT<i32>\n    associate Tree.Child is T\n    public var tag: i32 = 0\n    public func put<U>(self: ref/Self, u: ref/U) -> i32\n";

    private const string Second = "contract MT2<A, B>: Tree, Mark<B>\n    Self.(Tree).Child is MT2<A, B>\n";

    private const string SelfArg = "contract D<U>\ncontract C<A>\n    A is D<Self>\n";

    private const string Either = "contract E\ncontract D<U>\ncontract C<A>\n    A is E or D<Self>\n";

    private const string Both = "contract E\ncontract D<U>\ncontract C<A>\n    A is E and D<Self>\n";

    // c2-5a's B3 rows: Src.(HasItem).Item is a completed projection identical with i32 (SPEC 8.2.3).
    private const string SrcItem = "contract HasItem\n    associate Item\nstruct Src\n    Self is HasItem\n    associate HasItem.Item is i32\n    public var tag: i32 = 0\n";

    private const string KArgs = "contract K<A>\n    associate X\n";

    private const string KTwice = "group H\n    public func twice<T>(x: T.(K<i32>).X) -> (T.(K<i32>).X, T.(K<i32>).X)\n";

    // b1-r5's form (SPEC 6.1.1: a Container body does not see document-local functions, so the struct calls a group's function).
    private const string MarkHolder = "group N\n    public func needMark<V, W>(v: ref/V)\n        V is Mark<W>\n        ()\n" +
        "struct Holder<T>\n    T is MT<i32>\n    public var tag: i32 = 0\n    public func probe(self: ref/Self, c: ref/T.(Tree).Child)\n";

    private const string Swap = "contract P2<A, B>: Tree, Mark<A>\n    Self.(Tree).Child is P2<B, A>\n";

    private const string Fixed = "contract MF<A>: Tree, Mark<A>\n    Self.(Tree).Child is MF<i32>\n";

    private const string GTwice = "contract GTree<A>\n    associate Child\ngroup H\n    public func twice<T>(x: T.(GTree<i32>).Child) -> (T.(GTree<i32>).Child, T.(GTree<i32>).Child)\n";

    private const string NeedE = "contract E\nfunc needE<V>(v: ref/V)\n    V is E\n    ()\n";

    // R7-C2c2 c2-5b2's programs (artifacts/units/r7-c25b2/programs.py's): a recursive Contract whose one clause states V, with V's
    // accessors at depth 1, 2 and 3: a negated user Contract atom (NM), its positive twin (PM), none (NB), `not Copy` (NC), Contract
    // arguments (NA, also with a projection argument K), a negated identity atom (KS) and its positive twin (KP); and the callees.
    private const string NegatedMarks = "contract Marker\ncontract NM\n    associate Next is NM\n    associate V\n    Self.(NM).V is not Marker\n    func near(self: ref/Self) -> ref/Self.(NM).V\n    func deep(self: ref/Self) -> ref/Self.(NM).Next.(NM).V\n    func deep3(self: ref/Self) -> ref/Self.(NM).Next.(NM).Next.(NM).V\n";

    private const string PositiveMarks = "contract Marker\ncontract PM\n    associate Next is PM\n    associate V\n    Self.(PM).V is Marker\n    func near(self: ref/Self) -> ref/Self.(PM).V\n    func deep(self: ref/Self) -> ref/Self.(PM).Next.(PM).V\n    func deep3(self: ref/Self) -> ref/Self.(PM).Next.(PM).Next.(PM).V\n";

    private const string BareMarks = "contract Marker\ncontract NB\n    associate Next is NB\n    associate V\n    func near(self: ref/Self) -> ref/Self.(NB).V\n    func deep(self: ref/Self) -> ref/Self.(NB).Next.(NB).V\n    func deep3(self: ref/Self) -> ref/Self.(NB).Next.(NB).Next.(NB).V\n";

    private const string NegatedCopies = "contract NC\n    associate Next is NC\n    associate V\n    Self.(NC).V is not Copy\n    func near(self: ref/Self) -> ref/Self.(NC).V\n    func deep(self: ref/Self) -> ref/Self.(NC).Next.(NC).V\n    func deep3(self: ref/Self) -> ref/Self.(NC).Next.(NC).Next.(NC).V\n";

    private const string NegatedDs = "contract D<U>\ncontract NA\n    associate Next is NA\n    associate V\n    Self.(NA).V is not D<i32>\n    func near(self: ref/Self) -> ref/Self.(NA).V\n    func deep(self: ref/Self) -> ref/Self.(NA).Next.(NA).V\n    func deep3(self: ref/Self) -> ref/Self.(NA).Next.(NA).Next.(NA).V\n";

    private const string NegatedDks = "contract D<U>\ncontract NA\n    associate Next is NA\n    associate V\n    associate K\n    Self.(NA).V is not D<Self.(NA).K>\n    func near(self: ref/Self) -> ref/Self.(NA).V\n    func deep(self: ref/Self) -> ref/Self.(NA).Next.(NA).V\n    func deep3(self: ref/Self) -> ref/Self.(NA).Next.(NA).Next.(NA).V\n    func deepk(self: ref/Self) -> ref/Self.(NA).Next.(NA).K\n";

    private const string NegatedTexts = "contract KS\n    associate Next is KS\n    associate V\n    Self.(KS).V is not string\n    func near(self: ref/Self) -> ref/Self.(KS).V\n    func deep(self: ref/Self) -> ref/Self.(KS).Next.(KS).V\n    func deep3(self: ref/Self) -> ref/Self.(KS).Next.(KS).Next.(KS).V\n";

    private const string PositiveTexts = "contract KP\n    associate Next is KP\n    associate V\n    Self.(KP).V is string\n    func near(self: ref/Self) -> ref/Self.(KP).V\n    func deep(self: ref/Self) -> ref/Self.(KP).Next.(KP).V\n    func deep3(self: ref/Self) -> ref/Self.(KP).Next.(KP).Next.(KP).V\n";

    private const string NoMark = "func noMark<V>(v: ref/V) -> i32\n    V is not Marker\n    return 0\n";

    private const string NoTag = "func noTag<V>(v: ref/V) -> i32\n    V is not Tag\n    return 0\n";

    private const string NoCopy = "func noCopy<V>(v: ref/V) -> i32\n    V is not Copy\n    return 0\n";

    private const string NotText = "func notText<V>(v: ref/V) -> i32\n    V is not string\n    return 0\n";

    private const string NotI32 = "func notI32<V>(v: ref/V) -> i32\n    V is not i32\n    return 0\n";

    private const string NoD = "func noD<V>(v: ref/V) -> i32\n    V is not D<i32>\n    return 0\n";

    private const string NoD64 = "func noD64<V>(v: ref/V) -> i32\n    V is not D<i64>\n    return 0\n";

    private const string NoDw = "func noDW<V, W>(v: ref/V, w: ref/W) -> i32\n    V is not D<W>\n    return 0\n";

    // SPEC 10.1's `keep` pair: the first overload requires the Constraint and is strictly best (fewer defaults).
    private const string PickMark = "func pickm<F>(f: ref/F) -> i32\n    F is Marker\n    return 1\nfunc pickm<F>(f: ref/F, tag: i32 = 0) -> i32\n    return 2\n";

    private const string PickText = "func pickt<F>(f: ref/F) -> i32\n    F is string\n    return 1\nfunc pickt<F>(f: ref/F, tag: i32 = 0) -> i32\n    return 2\n";

    private const string Runner = "contract Runner\n    func run(self: ref/Self) -> i32\n";

    // PLAN G1407's programs (artifacts/units/g1406/programs.py, with r7-c25b2's helpers): a recursive Contract with `next` whose own
    // `Self` clause states every link, `not ObjectPayload` (NP) or Copy (CP), Copy as a parent (CPA) or nothing (NB0); V's conjunction
    // clauses, negated (NA2), positive (PA2) and nested (PA3), a disjunction (NOR2) and a negated conjunction (NNA2); and the callees.
    private const string NotPayloads = "contract NP\n    associate Next is NP\n    Self is not ObjectPayload\n    func next(self: ref/Self) -> ref/Self.(NP).Next\n";

    private const string SelfCopies = "contract CP\n    associate Next is CP\n    Self is Copy\n    func next(self: ref/Self) -> ref/Self.(CP).Next\n";

    private const string ParentCopies = "contract CPA: Copy\n    associate Next is CPA\n    func next(self: ref/Self) -> ref/Self.(CPA).Next\n";

    private const string BareNexts = "contract NB0\n    associate Next is NB0\n    func next(self: ref/Self) -> ref/Self.(NB0).Next\n";

    private const string NegatedConjuncts = "contract Marker\ncontract Tag\ncontract NA2\n    associate Next is NA2\n    associate V\n    Self.(NA2).V is (not Marker) and (not Tag)\n    func near(self: ref/Self) -> ref/Self.(NA2).V\n    func deep(self: ref/Self) -> ref/Self.(NA2).Next.(NA2).V\n    func deep3(self: ref/Self) -> ref/Self.(NA2).Next.(NA2).Next.(NA2).V\n";

    private const string PositiveConjuncts = "contract Marker\ncontract Tag\ncontract PA2\n    associate Next is PA2\n    associate V\n    Self.(PA2).V is Marker and Tag\n    func near(self: ref/Self) -> ref/Self.(PA2).V\n    func deep(self: ref/Self) -> ref/Self.(PA2).Next.(PA2).V\n    func deep3(self: ref/Self) -> ref/Self.(PA2).Next.(PA2).Next.(PA2).V\n";

    private const string NestedConjuncts = "contract Marker\ncontract Tag\ncontract Other\ncontract PA3\n    associate Next is PA3\n    associate V\n    Self.(PA3).V is Marker and (Tag and Other)\n    func near(self: ref/Self) -> ref/Self.(PA3).V\n    func deep(self: ref/Self) -> ref/Self.(PA3).Next.(PA3).V\n    func deep3(self: ref/Self) -> ref/Self.(PA3).Next.(PA3).Next.(PA3).V\n";

    private const string NegatedDisjuncts = "contract Marker\ncontract Tag\ncontract NOR2\n    associate Next is NOR2\n    associate V\n    Self.(NOR2).V is (not Marker) or (not Tag)\n    func near(self: ref/Self) -> ref/Self.(NOR2).V\n    func deep(self: ref/Self) -> ref/Self.(NOR2).Next.(NOR2).V\n    func deep3(self: ref/Self) -> ref/Self.(NOR2).Next.(NOR2).Next.(NOR2).V\n";

    private const string NegatedConjunction = "contract Marker\ncontract Tag\ncontract NNA2\n    associate Next is NNA2\n    associate V\n    Self.(NNA2).V is not (Marker and Tag)\n    func near(self: ref/Self) -> ref/Self.(NNA2).V\n    func deep(self: ref/Self) -> ref/Self.(NNA2).Next.(NNA2).V\n    func deep3(self: ref/Self) -> ref/Self.(NNA2).Next.(NNA2).Next.(NNA2).V\n";

    // The review's twins (artifacts/units/g1406/review-twins.py, F's G1406-3 C1): an identity clause stating its own link (KI), and an
    // identity atom inside a conjunction (KC).
    private const string LinkIdentities = "contract Marker\ncontract KI\n    associate Next is KI\n    associate X\n    Self.(KI).X is Self\n    Self.(KI).X is Marker\n    func next(self: ref/Self) -> ref/Self.(KI).Next\n";

    private const string ConjunctIdentities = "contract Marker\ncontract KC\n    associate Next is KC\n    associate V\n    associate W\n    Self.(KC).V is Self.(KC).W and Copy\n    Self.(KC).W is Marker\n    func near(self: ref/Self) -> ref/Self.(KC).V\n    func deep(self: ref/Self) -> ref/Self.(KC).Next.(KC).V\n    func deep3(self: ref/Self) -> ref/Self.(KC).Next.(KC).Next.(KC).V\n";

    private const string NeedNotPayload = "func needNotOP<V>(v: ref/V) -> i32\n    V is not ObjectPayload\n    return 0\n";

    private const string NeedPayload = "func needOP<V>(v: ref/V) -> i32\n    V is ObjectPayload\n    return 0\n";

    private const string NeedCopy = "func needCopy<V>(v: ref/V) -> i32\n    V is Copy\n    return 0\n";

    private const string NeedMarker = "func needMark<V>(v: ref/V) -> i32\n    V is Marker\n    return 0\n";

    private const string NeedOther = "func needOther<V>(v: ref/V) -> i32\n    V is Other\n    return 0\n";

    // PLAN G1406's programs (artifacts/units/g1406/programs-u2.py, with r7-c25b2's helpers and its review's textOnly): KP's twin with
    // another associate W that no clause states (KP2), and the callees requiring string or i32.
    private const string OtherAssociates = "contract KP2\n    associate Next is KP2\n    associate V\n    associate W\n    Self.(KP2).V is string\n    func deep(self: ref/Self) -> ref/Self.(KP2).Next.(KP2).V\n    func deepw(self: ref/Self) -> ref/Self.(KP2).Next.(KP2).W\n";

    private const string TextOnly = "func textOnly<V>(v: ref/V) -> i32\n    V is string\n    return 0\n";

    private const string NeedI32 = "func needI32<V>(v: ref/V) -> i32\n    V is i32\n    return 0\n";

    // The Middle's C-313 B1 views (c2-2): AT2 supplies `T.(Tree).Child is AT3`, whose identity clause makes `T.(Tree).Child.(Tree).Alias`
    // identical with `T.(Tree).Child.(Tree).Child`, an AT3 (SPEC 8.2.3), so it is Copy; the view has no identity premise, so it keeps rows.
    private const string Orders = OrdersHead + "    Self.(Tree).Alias is Self.(Tree).Child\n" + OrdersTail;

    private const string OrdersHead = "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\n    associate Other is Tree\n    associate Free\n" +
        "contract AT3: Tree, Copy\n    Self.(Tree).Child is AT3\n";

    private const string OrdersTail = "contract AT2: Tree, Copy\n" +
        "    Self.(Tree).Child is AT3\nfunc needsTree<V>(v: ref/V)\n    V is Tree\n    ()\nfunc needsCopy<V>(v: ref/V)\n    V is Copy\n    ()\n";

    // SPEC 8.3.3: each recursive Contract of the S5 proposal's 2.1, through the premise supply of `T is C` (SPEC 8.3.2): Tree; CopyTree,
    // which refines Tree and constrains its inherited Child; A and B, which supply each other's conformance; Rec, whose refinement
    // constrains Node's unconstrained Child (Node is not recursive); Seq, whose Next conforms to another bound reference of its declaration;
    // A with a Constraint on a two-step projection of Self (B is not recursive). A disjunction supplies nothing, so Bush is not recursive;
    // a conjunction supplies each side (Hedge); a conformance to a proper refinement counts (Shrub's Child is a Bonsai, which refines Shrub).
    // SPEC 15.6.4: any base counts: Forest is not recursive, but Root is a Tree, so T.Root.Child has a recursive base.
    [Theory]
    [InlineData(Tree, "T is Tree", "T.(Tree).Child", true)]
    [InlineData(Tree + "contract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\n", "T is CopyTree", "T.(Tree).Child", true)]
    [InlineData(Mutual, "T is A", "T.(A).X", true)]
    [InlineData(Mutual, "T is B", "T.(B).Y", true)]
    [InlineData(Refinement, "T is Rec", "T.(Node).Child", true)]
    [InlineData(Refinement, "T is Node", "T.(Node).Child", false)]
    [InlineData("contract Seq<E>\n    associate Next is Seq<i32>\n", "T is Seq<i32>", "T.(Seq<i32>).Next", true)]
    [InlineData(TwoStep, "T is A", "T.(A).X", true)]
    [InlineData(TwoStep, "T is B", "T.(B).Y", false)]
    [InlineData("contract Leafy\ncontract Bush\n    associate Child is Bush or Leafy\n", "T is Bush", "T.(Bush).Child", false)]
    [InlineData("contract Leafy\ncontract Hedge\n    associate Child is Hedge and Leafy\n", "T is Hedge", "T.(Hedge).Child", true)]
    [InlineData("contract Shrub\n    associate Child is Bonsai\ncontract Bonsai: Shrub\n", "T is Shrub", "T.(Shrub).Child", true)]
    [InlineData(Forest, "T is Forest", "T.(Forest).Root", false)]
    [InlineData(Forest, "T is Forest", "T.(Forest).Root.(Tree).Child", true)]
    public void AProjectionHasARecursiveBase(string declarations, string premise, string projection, bool expected)
    {
        var c = Bind(declarations + $"func probe<T>(value: {projection})\n    {premise}\n    ()\n");
        var probe = Probe(c);
        var type = c.Types.Signature(probe, null, 0, out _)!;
        Assert.Equal(BoundTypeKind.AssociatedProjection, type.Kind);
        Assert.Equal(expected, c.Types.RecursiveBase(probe, type));
    }

    // A Type parameter is not a projection: its own conformance to a recursive Contract gives it no recursive base.
    [Fact]
    public void ATypeParameterHasNoRecursiveBase()
    {
        var c = Bind(Tree + "func probe<T>(value: T)\n    T is Tree\n    ()\n");
        var probe = Probe(c);
        Assert.False(c.Types.RecursiveBase(probe, c.Types.Signature(probe, null, 0, out _)!));
    }

    // Immutable after Bind and pooled: a warm rebind, which revokes the pass's recursive Contracts, and the queries that find them again
    // allocate nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRebindAndItsQueriesAllocateNothing()
    {
        var c = Bind(Forest + Mutual + "contract Seq<E>\n    associate Next is Seq<i32>\n" +
            "func probe<T, U, V>(value: T.(Forest).Root.(Tree).Child, other: U.(A).X, next: V.(Seq<i32>).Next)\n    T is Forest\n    U is A\n" +
            "    V is Seq<i32>\n    ()\n");
        var probe = Probe(c);
        var types = Enumerable.Range(0, 3).Select(i => c.Types.Signature(probe, null, i, out _)!).ToArray();
        Assert.All(types, x => Assert.True(c.Types.RecursiveBase(probe, x)));
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            foreach (var type in types)
            {
                _ = c.Types.RecursiveBase(probe, type);
            }
        }));
    }

    // A row is (unit, form, program, records). Its verdict comes from SPEC, never from a build: records is empty for a valid program, which
    // also generates, and otherwise lists each expected Error as `Code: source line` (see CompilationTestHelper.AssertRecordsAtLines). unit
    // guard: the row runs today and must keep passing. Any other unit is the plan's unit that makes the row pass: the row is Explicit
    // until that unit lands and removes the marker. `dotnet xUnitTest.dll -class <class> -explicit on` runs all of them.
    [Theory]
    // SPEC 8.3.2, 8.5.3: `T is CopyTree` supplies Copy to every projection a query needs, however deep and whatever its spelling, in a non-recursive
    // Contract too; `T is Tree` supplies none, and a read that leaves its source initialized needs Copy (SPEC 8.15.1).
    [InlineData("guard", "value1", Copies + "group H\n    public func twice<T>(value: T.(Tree).Child) -> (T.(Tree).Child, T.(Tree).Child)\n        T is CopyTree\n        return (value, value)\n", "")]
    [InlineData("C2", "value2", Copies + "group H\n    public func twice<T>(value: T.(Tree).Child.(Tree).Child) -> (T.(Tree).Child.(Tree).Child, T.(Tree).Child.(Tree).Child)\n        T is CopyTree\n        return (value, value)\n", "")]
    [InlineData("C2", "value4", Copies + "group H\n    public func twice<T>(value: T.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child) -> (T.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child, T.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child)\n        T is CopyTree\n        return (value, value)\n", "")]
    [InlineData("C2", "value8", Copies + "group H\n    public func twice<T>(value: T.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child) -> (T.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child, T.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child)\n        T is CopyTree\n        return (value, value)\n", "")]
    [InlineData("guard", "value2n", Copies + "group H\n    public func twice<T>(value: T.(Tree).Child.(Tree).Child) -> (T.(Tree).Child.(Tree).Child, T.(Tree).Child.(Tree).Child)\n        T is Tree\n        return (value, value)\n", "TransferRequired_Kd: return (value, value)")]
    [InlineData("guard", "value2hint", Copies + "group H\n    public func twice<T>(hint: ref/T.(Tree).Child.(CopyTree).Child, value: T.(Tree).Child.(Tree).Child) -> (T.(Tree).Child.(Tree).Child, T.(Tree).Child.(Tree).Child)\n        T is CopyTree\n        return (value, value)\n", "")]
    [InlineData("C2", "nonrec", Outers + "func inspect<T>(value: T.(Outer).Inner.(Holder).Item) -> i32\n    T is CopyOuter\n    let a = value\n    let b = value\n    return 0\n", "")]
    [InlineData("guard", "nonrecn", Outers + "func inspect<T>(value: T.(Outer).Inner.(Holder).Item) -> i32\n    T is Outer\n    let a = value\n    let b = value\n    return 0\n", "TransferRequired_Kd: let a = value | TransferRequired_Kd: let b = value")]

    // SPEC 15.6.4: a widened effect conflicts as an unknown call does, even where the caller's premises would classify every effect it replaces.
    [InlineData("guard", "s5two", Walks + "        Trees.two(self.node)\n        return .Ok(())\n", "IncompatibleContractImplementation_Kd: Self is Sink")]
    [InlineData("guard", "s5one", Walks + "        Trees.one(self.node)\n        return .Ok(())\n", "IncompatibleContractImplementation_Kd: Self is Sink")]

    // SPEC 8.11.3, 8.3.2: the callee's own premises prove every destroyed Type Copy, whatever the projection's spelling.
    [InlineData("C2", "a4h", Copies + "group H\n    public func h<T>(t: T.(Tree).Child.(Tree).Child)\n        T is CopyTree\n        ()\ncontract Visit\n    func visit(self: ref/Self)\n        effect confined\nstruct W<U>\n    U is CopyTree\n    U is Copy\n    U.(Tree).Child is U\n    Self is Visit\n    let node: U\n    public func visit(self: ref/Self)\n        H.h<U>(self.node)\n", "")]
    public void CopyReachesAProjectionWhateverItsSpelling(string unit, string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines($"{form} [{unit}]", program + "()", records);

    // DECISIONS R7-C2e2-1 at c2-2's merge (N3 of its merge review): o1's view has no identity premise, so its Tree request keeps the
    // view's entry; the Copy request extends, filing the facts again under its extended keys, so it reopens the entry as its own and
    // writes nothing into the Kept entry; `T.(Tree).Other`, asked last, forms the entry again. The pairs indexed over Orders' after each
    // request, exact: the Tree request forms the entry (24); the Copy request extends as its class's closure (R7-C2c2 c2-4b, DECISIONS
    // R4.13), from its root and again from its re-rooted root under the declaration's table, each walking round reopening the entry
    // before it files (74); the last request forms the entry again (84). Each publication's start forgets the Kept entries (R7-C2e2
    // e2-3a, ForgetKeptTables), so a Kept entry of the view is formed again at its next read, 10 pairs each: the capability phase's
    // first publication adds 10 to each program, the after-bodies one 10 where the last request left the entry Kept (the Tree requests;
    // the Copy request's own entry went when it returned, and Supply's rows reader forms it once more for a later `V is Copy`
    // proposition, R7-C2c2 c2-5b2, F's CC-25): 44, 94 and 104 (24, 74 and 84 without the forget). N3's observable: an
    // extension writing into the Kept entry leaves it Kept for the last request, which then indexes nothing (104 -> 84; reopening by the
    // table's finality did: a round with no rows builds a Final table).
    [Fact]
    public void AnExtensionReopensTheViewsKeptEntryAsItsOwn()
    {
        var probe = "func probe<T>(r: ref/T.(Tree).Child.(Tree).Alias, o: ref/T.(Tree).Other)\n    T is AT2\n    needsTree<T.(Tree).Child.(Tree).Alias>(r)\n";
        var orders = Work(Orders);
        var tree = Work(Orders + probe) - orders;
        probe += "    needsCopy<T.(Tree).Child.(Tree).Alias>(r)\n";
        var copy = Work(Orders + probe) - orders;
        probe += "    needsTree<T.(Tree).Other>(o)\n";
        Assert.Equal((44, 94, 104), (tree, copy, Work(Orders + probe) - orders));

        static int Work(string source) => Bind(source).Binding.ClosureIndexWork;
    }

    [Theory]
    // SPEC 8.3.2 (the refinement row), 8.3.3 (Finality): a Contract premise supplies each conformance its refinement clauses state, at any
    // depth, in any order of declarations and premises, through Contracts that supply each other, inside a Tuple or an Option, through an
    // identity the view's facts state and whatever the qualifiers. Unit C2a1's rows are the programs Enumerate newly accepts (and p30n,
    // whose records were the premise's and are now the read's); each has its invalid counterpart, which the same premises never prove.
    [InlineData("C2a1", "p01-declorder", "group H\n    public func twice<T>(value: T.(Tree).Child.(Tree).Child) -> (T.(Tree).Child.(Tree).Child, T.(Tree).Child.(Tree).Child)\n        T is CopyTree\n        return (value, value)\ncontract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\ncontract Tree\n    associate Child is Tree\n", "")]
    [InlineData("C2a1", "p02-premorder", Copies + "group H\n    public func twice<T>(value: T.(Tree).Child.(Tree).Child.(Tree).Child) -> (T.(Tree).Child.(Tree).Child.(Tree).Child, T.(Tree).Child.(Tree).Child.(Tree).Child)\n        T is Tree\n        T is CopyTree\n        return (value, value)\n", "")]
    [InlineData("C2a1", "p03-mutual", "contract A\n    associate N is B\ncontract B\n    associate M is A\ncontract CA: A, Copy\n    Self.(A).N is CB\ncontract CB: B, Copy\n    Self.(B).M is CA\ngroup H\n    public func twice<T>(value: T.(A).N.(B).M.(A).N) -> (T.(A).N.(B).M.(A).N, T.(A).N.(B).M.(A).N)\n        T is CA\n        return (value, value)\n", "")]
    [InlineData("C2a1", "p06-shallowprem", Copies + "group H\n    public func twice<T>(value: T.(Tree).Child.(Tree).Child.(Tree).Child) -> (T.(Tree).Child.(Tree).Child.(Tree).Child, T.(Tree).Child.(Tree).Child.(Tree).Child)\n        T is Tree\n        T.(Tree).Child is CopyTree\n        return (value, value)\n", "")]
    [InlineData("C2a1", "p08-identity", Copies + "group H\n    public func twice<T, U>(value: U.(Tree).Child.(Tree).Child) -> (U.(Tree).Child.(Tree).Child, U.(Tree).Child.(Tree).Child)\n        T is CopyTree\n        U is Tree\n        U.(Tree).Child is T.(Tree).Child\n        return (value, value)\n", "")]
    [InlineData("C2a1", "p11-depthident", "contract Tree\n    associate Child is Tree\n    associate Alias\n    Self.(Tree).Alias is Self.(Tree).Child\ncontract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\ngroup H\n    public func twice<T>(value: T.(Tree).Child.(Tree).Alias.(Tree).Child) -> (T.(Tree).Child.(Tree).Alias.(Tree).Child, T.(Tree).Child.(Tree).Alias.(Tree).Child)\n        T is CopyTree\n        return (value, value)\n", "")]
    [InlineData("C2a1", "p23-tuplecopy", Copies + "group H\n    public func twice<T>(value: (T.(Tree).Child.(Tree).Child, i32)) -> ((T.(Tree).Child.(Tree).Child, i32), (T.(Tree).Child.(Tree).Child, i32))\n        T is CopyTree\n        return (value, value)\n", "")]
    [InlineData("C2a1", "p24-optioncopy", Copies + "group H\n    public func twice<T>(value: Option<T.(Tree).Child.(Tree).Child>) -> (Option<T.(Tree).Child.(Tree).Child>, Option<T.(Tree).Child.(Tree).Child>)\n        T is CopyTree\n        return (value, value)\n", "")]
    [InlineData("C2a1", "p25dr-deepident", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\ncontract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\ngroup H\n    public func twice<T>(value: T.(Tree).Child) -> (T.(Tree).Child, T.(Tree).Child)\n        T is CopyTree\n        T.(Tree).Alias.(Tree).Alias.(Tree).Alias is T.(Tree).Child\n        return (value, value)\n", "")]
    [InlineData("C2a1", "p25er-deeperdemand", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\ncontract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\ngroup H\n    public func twice<T>(value: T.(Tree).Child.(Tree).Child) -> (T.(Tree).Child.(Tree).Child, T.(Tree).Child.(Tree).Child)\n        T is CopyTree\n        T.(Tree).Alias.(Tree).Alias is T.(Tree).Child\n        return (value, value)\n", "")]
    [InlineData("C2a1", "p27v-arithrec", "contract K\n    associate Y is M\ncontract M: K, Addable<Self>\n    Self.(K).Y.(Addable<Self.(K).Y>).Output is Copy\ngroup H\n    public func twice<T>(value: T.(K).Y.(Addable<T.(K).Y>).Output) -> (T.(K).Y.(Addable<T.(K).Y>).Output, T.(K).Y.(Addable<T.(K).Y>).Output)\n        T is M\n        return (value, value)\n", "")]
    [InlineData("C2a1", "p27r-arithrec", "contract K\n    associate Y\ncontract M: K, Addable<Self>\n    Self.(K).Y is M\n    Self.(K).Y.(Addable<Self.(K).Y>).Output is Copy\ngroup H\n    public func twice<T>(value: T.(K).Y.(Addable<T.(K).Y>).Output) -> (T.(K).Y.(Addable<T.(K).Y>).Output, T.(K).Y.(Addable<T.(K).Y>).Output)\n        T is M\n        return (value, value)\n", "")]
    [InlineData("C2a1", "p29a-unqualified", Copies + "group H\n    public func twice<T>(value: T.Child.Child) -> (T.Child.Child, T.Child.Child)\n        T is CopyTree\n        return (value, value)\n", "")]
    [InlineData("C2a1", "p30-paramident", Copies + "group H\n    public func twice<T, U>(value: U.(Tree).Child.(Tree).Child) -> (U.(Tree).Child.(Tree).Child, U.(Tree).Child.(Tree).Child)\n        T is CopyTree\n        T is U\n        return (value, value)\n", "")]
    [InlineData("guard", "p03n-mutual", "contract A\n    associate N is B\ncontract B\n    associate M is A\ncontract CA: A, Copy\n    Self.(A).N is CB\ncontract CB: B\n    Self.(B).M is CA\ngroup H\n    public func twice<T>(value: T.(A).N.(B).M.(A).N) -> (T.(A).N.(B).M.(A).N, T.(A).N.(B).M.(A).N)\n        T is CA\n        return (value, value)\n", "TransferRequired_Kd: return (value, value)")]
    [InlineData("guard", "p06n-deepprem", Copies + "group H\n    public func twice<T>(value: T.(Tree).Child.(Tree).Child) -> (T.(Tree).Child.(Tree).Child, T.(Tree).Child.(Tree).Child)\n        T is Tree\n        T.(Tree).Child.(Tree).Child.(Tree).Child is CopyTree\n        return (value, value)\n", "TransferRequired_Kd: return (value, value)")]
    [InlineData("guard", "p08n-identity", Copies + "group H\n    public func twice<T, U>(value: U.(Tree).Child.(Tree).Child) -> (U.(Tree).Child.(Tree).Child, U.(Tree).Child.(Tree).Child)\n        T is CopyTree\n        U is Tree\n        return (value, value)\n", "TransferRequired_Kd: return (value, value)")]
    [InlineData("guard", "p11n-depthident", "contract Tree\n    associate Child is Tree\n    associate Alias\n    Self.(Tree).Alias is Self.(Tree).Child\ncontract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\ngroup H\n    public func twice<T>(value: T.(Tree).Child.(Tree).Alias.(Tree).Child) -> (T.(Tree).Child.(Tree).Alias.(Tree).Child, T.(Tree).Child.(Tree).Alias.(Tree).Child)\n        T is Tree\n        return (value, value)\n", "TransferRequired_Kd: return (value, value)")]
    [InlineData("guard", "p23n-tuplecopy", Copies + "group H\n    public func twice<T>(value: (T.(Tree).Child.(Tree).Child, i32)) -> ((T.(Tree).Child.(Tree).Child, i32), (T.(Tree).Child.(Tree).Child, i32))\n        T is Tree\n        return (value, value)\n", "TransferRequired_Kd: return (value, value)")]
    [InlineData("guard", "p24n-optioncopy", Copies + "group H\n    public func twice<T>(value: Option<T.(Tree).Child.(Tree).Child>) -> (Option<T.(Tree).Child.(Tree).Child>, Option<T.(Tree).Child.(Tree).Child>)\n        T is Tree\n        return (value, value)\n", "TransferRequired_Kd: return (value, value)")]
    [InlineData("guard", "p27rn-arithrec", "contract K\n    associate Y\ncontract M: K, Addable<Self>\n    Self.(K).Y is M\n    Self.(K).Y.(Addable<Self.(K).Y>).Output is Copy\ngroup H\n    public func twice<T>(value: T.(K).Y) -> (T.(K).Y, T.(K).Y)\n        T is M\n        return (value, value)\n", "TransferRequired_Kd: return (value, value)")]
    [InlineData("guard", "p29n-unqualified", Copies + "group H\n    public func twice<T>(value: T.Child.Child) -> (T.Child.Child, T.Child.Child)\n        T is Tree\n        return (value, value)\n", "TransferRequired_Kd: return (value, value)")]
    [InlineData("C2a1", "p30n-paramident", Copies + "group H\n    public func twice<T, U>(value: U.(Tree).Child.(Tree).Child) -> (U.(Tree).Child.(Tree).Child, U.(Tree).Child.(Tree).Child)\n        T is Tree\n        T is U\n        return (value, value)\n", "TransferRequired_Kd: return (value, value)")]

    // R7-C2a (design review 4, N2): a request drops the rows its preparation's demands left before its walk reads them. Normalizing the
    // Output premise under `T is U` asks whether U.(Tree).Child is a PrimitiveInteger (the built-in Addable's Output), and that demand
    // enumerates U and U.(Tree).Child from the written facts alone, which state T; kept, those empty rows would hide `U is CopyTree`
    // from the walk of U.(Tree).Child.(Tree).Child, whose rows come only through its prefixes' clauses.
    [InlineData("C2a1", "p33-prepmemo", Copies + "func needs<V>(v: ref/V)\n    V is CopyTree\n    ()\nfunc probe<T, U>(value: ref/U.(Tree).Child.(Tree).Child)\n    T is CopyTree\n    T is U\n    T.(Tree).Child is Addable<T.(Tree).Child>\n    T.(Tree).Child.(Addable<T.(Tree).Child>).Output is Copy\n    needs<U.(Tree).Child.(Tree).Child>(value)\n", "")]
    [InlineData("C2a1", "p33n-prepmemo", Copies + "func needs<V>(v: ref/V)\n    V is CopyTree\n    ()\nfunc probe<T, U>(value: ref/U.(Tree).Child.(Tree).Child)\n    T is Tree\n    T is U\n    T.(Tree).Child is Addable<T.(Tree).Child>\n    T.(Tree).Child.(Addable<T.(Tree).Child>).Output is Copy\n    needs<U.(Tree).Child.(Tree).Child>(value)\n", "UnprovenConstraint_Kd: needs<U.(Tree).Child.(Tree).Child>(value)")]

    // Accepted before Enumerate and still: an identity written from the supplied subject, a clause whose normalization asks a capability
    // of the link being enumerated (the walk demands nothing, so it is never re-entered), a parameter identity over a refining Contract.
    [InlineData("guard", "p25r-headbound", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\ncontract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\ngroup H\n    public func twice<T>(value: T.(Tree).Child) -> (T.(Tree).Child, T.(Tree).Child)\n        T is CopyTree\n        T.(Tree).Alias.(Tree).Alias is T.(Tree).Child\n        return (value, value)\n", "")]
    [InlineData("guard", "p26-reentry", "contract K\n    associate Y is PrimitiveInteger\ncontract M: K\n    Self.(K).Y.(Addable<Self.(K).Y>).Output is Copy\ngroup H\n    public func twice<T>(value: T.(K).Y) -> (T.(K).Y, T.(K).Y)\n        T is M\n        return (value, value)\n", "")]
    [InlineData("guard", "p32b-leafcache", "contract D: Copy\ncontract C\n    associate X\nstruct Box<A>\n    Self is C when A is Copy\n    associate X is i32\ngroup H\n    public func twice<T, U>(value: U) -> (U, U)\n        T is D\n        T is U\n        return (value, value)\n", "")]

    // SPEC 8.2.3: identity is symmetric and replaces inside a subject, so a premise's supply (8.3.2) reaches every Type of its identity
    // class: a supplied subject (a fact's or a clause instance's) reached through an identity premise (unit C2c2 c2-1's rule).
    [InlineData("C2c2", "p08z-paramident", Copies + "group H\n    public func twice<T, U>(value: U.(Tree).Child.(Tree).Child) -> (U.(Tree).Child.(Tree).Child, U.(Tree).Child.(Tree).Child)\n        U is T\n        T is CopyTree\n        return (value, value)\n", "")]
    [InlineData("C2c2", "p25-headbound", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\ncontract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\ngroup H\n    public func twice<T>(value: T.(Tree).Child) -> (T.(Tree).Child, T.(Tree).Child)\n        T is CopyTree\n        T.(Tree).Child is T.(Tree).Alias.(Tree).Alias\n        return (value, value)\n", "")]
    [InlineData("C2c2", "p25a-headbound", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\ncontract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\ngroup H\n    public func twice<T>(value: T.(Tree).Alias.(Tree).Alias) -> (T.(Tree).Alias.(Tree).Alias, T.(Tree).Alias.(Tree).Alias)\n        T is CopyTree\n        T.(Tree).Child is T.(Tree).Alias.(Tree).Alias\n        return (value, value)\n", "")]
    [InlineData("C2c2", "p25d-deepident", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\ncontract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\ngroup H\n    public func twice<T>(value: T.(Tree).Child) -> (T.(Tree).Child, T.(Tree).Child)\n        T is CopyTree\n        T.(Tree).Child is T.(Tree).Alias.(Tree).Alias.(Tree).Alias\n        return (value, value)\n", "")]
    [InlineData("C2c2", "p25e-deeperdemand", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\ncontract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\ngroup H\n    public func twice<T>(value: T.(Tree).Child.(Tree).Child) -> (T.(Tree).Child.(Tree).Child, T.(Tree).Child.(Tree).Child)\n        T is CopyTree\n        T.(Tree).Child is T.(Tree).Alias.(Tree).Alias\n        return (value, value)\n", "")]
    [InlineData("C2c2", "p28-unnormdemand", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\n    associate Other\ncontract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\ngroup H\n    public func twice<T>(value: T.(Tree).Other.(Tree).Child) -> (T.(Tree).Other.(Tree).Child, T.(Tree).Other.(Tree).Child)\n        T is CopyTree\n        T.(Tree).Child is T.(Tree).Alias.(Tree).Alias\n        T.(Tree).Other is T.(Tree).Child\n        return (value, value)\n", "")]
    // R7-C2c2 (review 1, B1): a view holding a derived identity fact (T is K, K stating Self.(K).X is i32) has no Final identity table
    // (W6); the key is formed from the view's classes whatever their finality, so these stay accepted.
    [InlineData("C2c2", "nf1-nonfinal", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\n    associate Other\ncontract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\ncontract K\n    associate X\n    Self.(K).X is i32\nfunc twice<T, U>(value: U.(Tree).Child.(Tree).Child) -> (U.(Tree).Child.(Tree).Child, U.(Tree).Child.(Tree).Child)\n    U is T\n    T is CopyTree\n    T is K\n    return (value, value)\n", "")]
    [InlineData("guard", "nf2-nonfinal", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\n    associate Other\ncontract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\ncontract K\n    associate X\n    Self.(K).X is i32\nfunc twice<T, U>(value: U.(Tree).Child.(Tree).Child) -> (U.(Tree).Child.(Tree).Child, U.(Tree).Child.(Tree).Child)\n    T is U\n    T is CopyTree\n    T is K\n    return (value, value)\n", "")]
    [InlineData("C2c2", "nf3-nonfinal", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\n    associate Other\ncontract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\ncontract K\n    associate X\n    Self.(K).X is i32\nfunc twice<T>(value: T.(Tree).Alias.(Tree).Alias) -> (T.(Tree).Alias.(Tree).Alias, T.(Tree).Alias.(Tree).Alias)\n    T is CopyTree\n    T is K\n    T.(Tree).Child is T.(Tree).Alias.(Tree).Alias\n    return (value, value)\n", "")]
    // R7-C2c2 c2-2 (SPEC 8.2.3 grounds: the identity Constraints `T is MT` supplies apply at every depth): `T.(Tree).Child is MT`
    // supplies `T.(Tree).Child.(Tree).Alias is T.(Tree).Child.(Tree).Child`; the walk's identity rows join the request's classes.
    [InlineData("C2c22", "idd1-depth2", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\ncontract MT: Tree, Copy\n    Self.(Tree).Child is MT\n    Self.(Tree).Alias is Self.(Tree).Child\ngroup H\n    public func twice<T>(value: T.(Tree).Child.(Tree).Alias) -> (T.(Tree).Child.(Tree).Alias, T.(Tree).Child.(Tree).Alias)\n        T is MT\n        return (value, value)\n", "")]
    [InlineData("C2c22", "idd2-depth3", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\ncontract MT: Tree, Copy\n    Self.(Tree).Child is MT\n    Self.(Tree).Alias is Self.(Tree).Child\ngroup H\n    public func twice<T>(value: T.(Tree).Child.(Tree).Child.(Tree).Alias) -> (T.(Tree).Child.(Tree).Child.(Tree).Alias, T.(Tree).Child.(Tree).Child.(Tree).Alias)\n        T is MT\n        return (value, value)\n", "")]
    [InlineData("guard", "idd1n-depth2", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\ncontract MT: Tree, Copy\n    Self.(Tree).Child is MT\ngroup H\n    public func twice<T>(value: T.(Tree).Child.(Tree).Alias) -> (T.(Tree).Child.(Tree).Alias, T.(Tree).Child.(Tree).Alias)\n        T is MT\n        return (value, value)\n", "TransferRequired_Kd: return (value, value)")]
    [InlineData("guard", "idd2n-depth3", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\ncontract MT: Tree, Copy\n    Self.(Tree).Child is MT\ngroup H\n    public func twice<T>(value: T.(Tree).Child.(Tree).Child.(Tree).Alias) -> (T.(Tree).Child.(Tree).Child.(Tree).Alias, T.(Tree).Child.(Tree).Child.(Tree).Alias)\n        T is MT\n        return (value, value)\n", "TransferRequired_Kd: return (value, value)")]
    // SPEC 8.3.3: one answer whichever demand asks first (ord1, ord2), and the same record in both orders without the clause.
    [InlineData("C2c22", "ord1-aliasfirst", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\ncontract MTI: Tree, Copy\n    Self.(Tree).Child is MTI\n    Self.(Tree).Alias is Self.(Tree).Child\nfunc needsCopy<V>(v: ref/V)\n    V is Copy\n    ()\nfunc probe<T>(a: ref/T.(Tree).Child.(Tree).Alias, c: ref/T.(Tree).Child.(Tree).Child)\n    T is MTI\n    needsCopy<T.(Tree).Child.(Tree).Alias>(a)\n    needsCopy<T.(Tree).Child.(Tree).Child>(c)\n", "")]
    [InlineData("C2c22", "ord2-childfirst", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\ncontract MTI: Tree, Copy\n    Self.(Tree).Child is MTI\n    Self.(Tree).Alias is Self.(Tree).Child\nfunc needsCopy<V>(v: ref/V)\n    V is Copy\n    ()\nfunc probe<T>(a: ref/T.(Tree).Child.(Tree).Alias, c: ref/T.(Tree).Child.(Tree).Child)\n    T is MTI\n    needsCopy<T.(Tree).Child.(Tree).Child>(c)\n    needsCopy<T.(Tree).Child.(Tree).Alias>(a)\n", "")]
    [InlineData("guard", "ord1n-aliasfirst", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\ncontract MTI: Tree, Copy\n    Self.(Tree).Child is MTI\nfunc needsCopy<V>(v: ref/V)\n    V is Copy\n    ()\nfunc probe<T>(a: ref/T.(Tree).Child.(Tree).Alias, c: ref/T.(Tree).Child.(Tree).Child)\n    T is MTI\n    needsCopy<T.(Tree).Child.(Tree).Alias>(a)\n    needsCopy<T.(Tree).Child.(Tree).Child>(c)\n", "UnprovenConstraint_Kd: needsCopy<T.(Tree).Child.(Tree).Alias>(a)")]
    [InlineData("guard", "ord2n-childfirst", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\ncontract MTI: Tree, Copy\n    Self.(Tree).Child is MTI\nfunc needsCopy<V>(v: ref/V)\n    V is Copy\n    ()\nfunc probe<T>(a: ref/T.(Tree).Child.(Tree).Alias, c: ref/T.(Tree).Child.(Tree).Child)\n    T is MTI\n    needsCopy<T.(Tree).Child.(Tree).Child>(c)\n    needsCopy<T.(Tree).Child.(Tree).Alias>(a)\n", "UnprovenConstraint_Kd: needsCopy<T.(Tree).Child.(Tree).Alias>(a)")]
    // R7-C2c2 c2-2 (the Middle's C-313 B1; SPEC 8.3.3: the order of checks never changes the result): a kept row answers only where it
    // decides, so a request the rows do not refine and whose walk recorded an identity row extends as if nothing were kept. o1, o3, o4,
    // o8 and o10 ask the subject (or its link) first for a Contract its walk proves without extending; o2, o5 and o7 are the other order.
    [InlineData("C2c22", "o1-tree-copy", Orders + "func probe<T>(r: ref/T.(Tree).Child.(Tree).Alias, value: T.(Tree).Child.(Tree).Alias)\n    T is AT2\n    needsTree<T.(Tree).Child.(Tree).Alias>(r)\n    needsCopy<T.(Tree).Child.(Tree).Alias>(r)\n", "")]
    [InlineData("C2c22", "o2-copy-tree", Orders + "func probe<T>(r: ref/T.(Tree).Child.(Tree).Alias, value: T.(Tree).Child.(Tree).Alias)\n    T is AT2\n    needsCopy<T.(Tree).Child.(Tree).Alias>(r)\n    needsTree<T.(Tree).Child.(Tree).Alias>(r)\n", "")]
    [InlineData("C2c22", "o3-tree-dup", Orders + "func probe<T>(r: ref/T.(Tree).Child.(Tree).Alias, value: T.(Tree).Child.(Tree).Alias)\n    T is AT2\n    needsTree<T.(Tree).Child.(Tree).Alias>(r)\n    let d = (value, value)\n", "")]
    [InlineData("C2c22", "o4-dup-tree", Orders + "func probe<T>(r: ref/T.(Tree).Child.(Tree).Alias, value: T.(Tree).Child.(Tree).Alias)\n    T is AT2\n    let d = (value, value)\n    needsTree<T.(Tree).Child.(Tree).Alias>(r)\n", "")]
    [InlineData("C2c22", "o5-copy", Orders + "func probe<T>(r: ref/T.(Tree).Child.(Tree).Alias, value: T.(Tree).Child.(Tree).Alias)\n    T is AT2\n    needsCopy<T.(Tree).Child.(Tree).Alias>(r)\n", "")]
    [InlineData("C2c22", "o7-sig", Orders + "func probe<T>(r: ref/T.(Tree).Child.(Tree).Alias, s: ref/T.(Tree).Child.(Tree).Alias.(Tree).Child)\n    T is AT2\n    needsCopy<T.(Tree).Child.(Tree).Alias>(r)\n", "")]
    [InlineData("C2c22", "o8-deeper-first", Orders + "func probe<T>(r: ref/T.(Tree).Child.(Tree).Alias, s: ref/T.(Tree).Child.(Tree).Alias.(Tree).Child)\n    T is AT2\n    needsTree<T.(Tree).Child.(Tree).Alias.(Tree).Child>(s)\n    needsCopy<T.(Tree).Child.(Tree).Alias>(r)\n", "")]
    [InlineData("C2c22", "o10-final", Orders + "func probe<T, U>(r: ref/T.(Tree).Child.(Tree).Alias, value: T.(Tree).Child.(Tree).Alias)\n    T is AT2\n    U is T\n    needsTree<T.(Tree).Child.(Tree).Alias>(r)\n    needsCopy<T.(Tree).Child.(Tree).Alias>(r)\n", "")]
    // Without AT3's identity clause the subject is only Tree (SPEC 8.3.1: a required Constraint succeeds only when Proven): the kept
    // Tree row never answers Copy (o1's invalid twin).
    [InlineData("C2c22", "o1-tree-copyn", OrdersHead + OrdersTail + "func probe<T>(r: ref/T.(Tree).Child.(Tree).Alias, value: T.(Tree).Child.(Tree).Alias)\n    T is AT2\n    needsTree<T.(Tree).Child.(Tree).Alias>(r)\n    needsCopy<T.(Tree).Child.(Tree).Alias>(r)\n", "UnprovenConstraint_Kd: needsCopy<T.(Tree).Child.(Tree).Alias>(r)")]
    // R7-C2c2 c2-3a: an identity row the walk records that does not reach the subject (AT3 states Other, not Alias) marks the kept
    // rows as extensible, and the extension proves nothing: the same record in both orders (the Extends entry's invalid twins).
    [InlineData("C2c23a", "o1n2-tree-copy", OrdersHead + "    Self.(Tree).Other is Self.(Tree).Child\n" + OrdersTail + "func probe<T>(r: ref/T.(Tree).Child.(Tree).Alias, value: T.(Tree).Child.(Tree).Alias)\n    T is AT2\n    needsTree<T.(Tree).Child.(Tree).Alias>(r)\n    needsCopy<T.(Tree).Child.(Tree).Alias>(r)\n", "UnprovenConstraint_Kd: needsCopy<T.(Tree).Child.(Tree).Alias>(r)")]
    [InlineData("C2c23a", "o2n2-copy-tree", OrdersHead + "    Self.(Tree).Other is Self.(Tree).Child\n" + OrdersTail + "func probe<T>(r: ref/T.(Tree).Child.(Tree).Alias, value: T.(Tree).Child.(Tree).Alias)\n    T is AT2\n    needsCopy<T.(Tree).Child.(Tree).Alias>(r)\n    needsTree<T.(Tree).Child.(Tree).Alias>(r)\n", "UnprovenConstraint_Kd: needsCopy<T.(Tree).Child.(Tree).Alias>(r)")]
    // SPEC 8.2.3, 8.6.4: the identity clause at depth makes the subject an MT, so Named's `name` is its member: valid. The
    // implementation's limit (ContractSolver's Extension): name discovery reads the first walk's rows and demands no Contract, so it
    // never extends and the member is not found, as on base, until R7-C2e2's Closure(scope); without the clause it is no member.
    [InlineData("C2e2", "dm1-discovery-depth", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\ncontract Named\n    func name(self: ref/Self) -> i32\ncontract MT: Tree, Named\n    Self.(Tree).Child is MT\n    Self.(Tree).Alias is Self.(Tree).Child\nfunc probe<T>(v: ref/T.(Tree).Child.(Tree).Alias) -> i32\n    T is MT\n    return v.name()\n", "", Explicit = true)]
    [InlineData("guard", "dm1n-discovery-depth", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\ncontract Named\n    func name(self: ref/Self) -> i32\ncontract MT: Tree, Named\n    Self.(Tree).Child is MT\nfunc probe<T>(v: ref/T.(Tree).Child.(Tree).Alias) -> i32\n    T is MT\n    return v.name()\n", "UnresolvedBinding_Kd: return v.name()")]
    // The group review's twins (q1, q2 are idd1, idd2): premise order under `U is T`, mutual Contracts, demand order, a clause
    // joining a deeper Type to a shallower one (q6, eager at depth 1).
    [InlineData("C2c22", "q3a-premorder", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\ncontract MTI: Tree, Copy\n    Self.(Tree).Child is MTI\n    Self.(Tree).Alias is Self.(Tree).Child\nfunc twice<T, U>(value: U.(Tree).Child.(Tree).Alias) -> (U.(Tree).Child.(Tree).Alias, U.(Tree).Child.(Tree).Alias)\n    U is T\n    T is MTI\n    return (value, value)\n", "")]
    [InlineData("C2c22", "q3b-premorder", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\ncontract MTI: Tree, Copy\n    Self.(Tree).Child is MTI\n    Self.(Tree).Alias is Self.(Tree).Child\nfunc twice<T, U>(value: U.(Tree).Child.(Tree).Alias) -> (U.(Tree).Child.(Tree).Alias, U.(Tree).Child.(Tree).Alias)\n    T is MTI\n    U is T\n    return (value, value)\n", "")]
    [InlineData("guard", "q3an-premorder", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\ncontract MTI: Tree, Copy\n    Self.(Tree).Child is MTI\nfunc twice<T, U>(value: U.(Tree).Child.(Tree).Alias) -> (U.(Tree).Child.(Tree).Alias, U.(Tree).Child.(Tree).Alias)\n    U is T\n    T is MTI\n    return (value, value)\n", "TransferRequired_Kd: return (value, value)")]
    [InlineData("guard", "q3bn-premorder", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\ncontract MTI: Tree, Copy\n    Self.(Tree).Child is MTI\nfunc twice<T, U>(value: U.(Tree).Child.(Tree).Alias) -> (U.(Tree).Child.(Tree).Alias, U.(Tree).Child.(Tree).Alias)\n    T is MTI\n    U is T\n    return (value, value)\n", "TransferRequired_Kd: return (value, value)")]
    [InlineData("C2c22", "q4-mutual", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\ncontract A: Tree, Copy\n    Self.(Tree).Child is B\ncontract B: Tree, Copy\n    Self.(Tree).Child is A\n    Self.(Tree).Alias is Self.(Tree).Child\nfunc twice<T>(value: T.(Tree).Child.(Tree).Alias) -> (T.(Tree).Child.(Tree).Alias, T.(Tree).Child.(Tree).Alias)\n    T is A\n    return (value, value)\n", "")]
    [InlineData("guard", "q4n-mutual", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\ncontract A: Tree, Copy\n    Self.(Tree).Child is B\ncontract B: Tree, Copy\n    Self.(Tree).Child is A\nfunc twice<T>(value: T.(Tree).Child.(Tree).Alias) -> (T.(Tree).Child.(Tree).Alias, T.(Tree).Child.(Tree).Alias)\n    T is A\n    return (value, value)\n", "TransferRequired_Kd: return (value, value)")]
    [InlineData("C2c22", "q5a-demands", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\ncontract MTI: Tree, Copy\n    Self.(Tree).Child is MTI\n    Self.(Tree).Alias is Self.(Tree).Child\nfunc both<T>(a: T.(Tree).Child.(Tree).Alias, b: T.(Tree).Child) -> (T.(Tree).Child.(Tree).Alias, T.(Tree).Child.(Tree).Alias, T.(Tree).Child, T.(Tree).Child)\n    T is MTI\n    return (a, a, b, b)\n", "")]
    [InlineData("C2c22", "q5b-demands", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\ncontract MTI: Tree, Copy\n    Self.(Tree).Child is MTI\n    Self.(Tree).Alias is Self.(Tree).Child\nfunc both<T>(a: T.(Tree).Child, b: T.(Tree).Child.(Tree).Alias) -> (T.(Tree).Child, T.(Tree).Child, T.(Tree).Child.(Tree).Alias, T.(Tree).Child.(Tree).Alias)\n    T is MTI\n    return (a, a, b, b)\n", "")]
    [InlineData("guard", "q6-shorter", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\ncontract M3: Tree, Copy\n    Self.(Tree).Child is M3\n    Self.(Tree).Child.(Tree).Alias is Self.(Tree).Child\nfunc twice<T>(value: T.(Tree).Child.(Tree).Alias.(Tree).Child) -> (T.(Tree).Child.(Tree).Alias.(Tree).Child, T.(Tree).Child.(Tree).Alias.(Tree).Child)\n    T is M3\n    return (value, value)\n", "")]
    [InlineData("guard", "q6n-shorter", "contract Tree\n    associate Child is Tree\n    associate Alias is Tree\ncontract M3: Tree, Copy\n    Self.(Tree).Child is M3\nfunc twice<T>(value: T.(Tree).Child.(Tree).Alias.(Tree).Child) -> (T.(Tree).Child.(Tree).Alias.(Tree).Child, T.(Tree).Child.(Tree).Alias.(Tree).Child)\n    T is M3\n    return (value, value)\n", "TransferRequired_Kd: return (value, value)")]
    // The library Iterator states `LendingIterator.LentItem(step) is Item`: a view with an Iterator premise records identity rows.
    [InlineData("guard", "it1-iterator", "func twice<I>(source: uniq/I) -> (Option<I.Item>, Option<I.Item>)\n    I is Iterator\n    I.Item is Copy\n    let first = source.next()\n    return (first, first)\n", "")]
    [InlineData("guard", "it1n-iterator", "func twice<I>(source: uniq/I) -> (Option<I.Item>, Option<I.Item>)\n    I is Iterator\n    let first = source.next()\n    return (first, first)\n", "TransferRequired_Kd: return (first, first)")]

    // R7-C2c2 (review 1, the rule's wider instances): a parameter identical to a supplied projection, a conformance call, a transitive
    // identity and a Callable's projection; each invalid twin keeps its independent record, and the dependent ones went with their cause.
    [InlineData("C2c2", "id2-projident", Copies + "func twice<T, U>(value: U.(Tree).Child) -> (U.(Tree).Child, U.(Tree).Child)\n    T is CopyTree\n    U is T.(Tree).Child\n    return (value, value)\n", "")]
    [InlineData("C2c2", "id2n-projident", Copies + "func twice<T, U>(value: U.(Tree).Child) -> (U.(Tree).Child, U.(Tree).Child)\n    T is Tree\n    U is T.(Tree).Child\n    return (value, value)\n", "TransferRequired_Kd: return (value, value)")]
    [InlineData("C2c2", "id3-call", Copies + "func needsCopyTree<V>(v: ref/V)\n    V is CopyTree\n    ()\nfunc probe<T, U>(x: ref/U.(Tree).Child)\n    U is T\n    T is CopyTree\n    needsCopyTree<U.(Tree).Child>(x)\n", "")]
    [InlineData("C2c2", "id3n-call", Copies + "func needsCopyTree<V>(v: ref/V)\n    V is CopyTree\n    ()\nfunc probe<T, U>(x: ref/U.(Tree).Child)\n    U is T\n    T is Tree\n    needsCopyTree<U.(Tree).Child>(x)\n", "UnprovenConstraint_Kd: needsCopyTree<U.(Tree).Child>(x)")]
    [InlineData("C2c2", "id4-transitive", Copies + "func twice<T, U, W>(value: W.(Tree).Child) -> (W.(Tree).Child, W.(Tree).Child)\n    U is T\n    W is U\n    T is CopyTree\n    return (value, value)\n", "")]
    [InlineData("guard", "id4n-transitive", Copies + "func twice<T, U, W>(value: W.(Tree).Child) -> (W.(Tree).Child, W.(Tree).Child)\n    U is T\n    W is Tree\n    T is CopyTree\n    return (value, value)\n", "TransferRequired_Kd: return (value, value)")]
    [InlineData("C2c2", "kc1-callable", "contract H\n    associate F\n    Self.(H).F is Callable<(i32) -> i32>\nfunc probe<T, U>(f: ref/U.(H).F) -> i32\n    U is T\n    T is H\n    return f(1)\n", "")]
    [InlineData("C2c2", "kc1n-callable", "contract H\n    associate F\nfunc probe<T, U>(f: ref/U.(H).F) -> i32\n    U is T\n    T is H\n    return f(1)\n", "NotCallable_Kd: return f(1)")]
    // R7-C2c2 (RP17 review B1, ruling 15): a constructed member of a class (`i32`, a struct, `Option<U>`) is no key's: its conformance is
    // ResolveConformance's, so `T is Mark` never reaches `i32` or `Leaf`, and a premise that contradicts a Refuted one (SPEC 8.3.2's
    // closed world) stays its declaration's error; the consistent twins (the conformance holds) stay accepted.
    [InlineData("C2c2", "d09p-prim", Marks + "func f<T>(x: ref/i32)\n    T is i32\n    T is Mark\n    needMark<i32>(x)\n", "InvalidConstraint_Kd: func f<T>(x: ref/i32)")]
    [InlineData("C2c2", "d09q-priminf", Marks + "func f<T>(x: ref/i32)\n    T is i32\n    T is Mark\n    needMark(x)\n", "InvalidConstraint_Kd: func f<T>(x: ref/i32)")]
    // N9 (SPEC 8.2.3, 8.3.2): a premise against the closed-world refutation of a complete constructed Type its subject is identical
    // with is contradictory evidence, the premise's error at the declaration (as d09y's projection case on base), not the query's.
    [InlineData("C2c2", "d09t-paraminf", Marks + "func f<T>(y: ref/T)\n    T is i32\n    T is Mark\n    needMark(y)\n", "InvalidConstraint_Kd: func f<T>(y: ref/T)")]
    [InlineData("C2c2", "d09z-prim2", Marks + "func f<T, U>(x: ref/i32)\n    U is T\n    T is i32\n    U is Mark\n    needMark<i32>(x)\n", "InvalidConstraint_Kd: func f<T, U>(x: ref/i32)")]
    [InlineData("C2c2", "d09r-struct", Marks + "struct Leaf\n    public var v: i32\nfunc f<T>(x: ref/Leaf)\n    T is Leaf\n    T is Mark\n    needMark<Leaf>(x)\n", "InvalidConstraint_Kd: func f<T>(x: ref/Leaf)")]
    [InlineData("C2c2", "d09y-leafproj", Marks + "contract Holder\n    associate Item\nstruct Leaf\n    public var v: i32\nfunc f<T>(x: ref/Leaf)\n    T is Holder\n    T.(Holder).Item is Leaf\n    T.(Holder).Item is Mark\n    needMark<Leaf>(x)\n", "InvalidConstraint_Kd: func f<T>(x: ref/Leaf) <- T.(Holder).Item is Mark")] // R7-C3 (C2 rulings 245 (i), 273, 278; DECISIONS Q13): SPEC 8.2.3's "once"; the record's related premises are its Failed premises (`Item is Leaf` stays valid; the failed `Item is Mark` speaks to `Leaf is Mark`)
    [InlineData("C2c2", "d09y-noquery", Marks + "contract Holder\n    associate Item\nstruct Leaf\n    public var v: i32\nfunc f<T>(x: ref/Leaf)\n    T is Holder\n    T.(Holder).Item is Leaf\n    T.(Holder).Item is Mark\n    ()\n", "InvalidConstraint_Kd: func f<T>(x: ref/Leaf) <- T.(Holder).Item is Mark")] // R7-C3 (C2 rulings 245 (i), 273, 278; DECISIONS Q13): SPEC 8.2.3's "once"; the record's related premises are its Failed premises (`Item is Leaf` stays valid; the failed `Item is Mark` speaks to `Leaf is Mark`)
    [InlineData("C2c2", "d09-option", Marks + "func f<T, U>(x: ref/Option<U>)\n    T is Option<U>\n    T is Mark\n    needMark<Option<U>>(x)\n", "InvalidConstraint_Kd: func f<T, U>(x: ref/Option<U>)")] // O-344 (a): the premise's error
    [InlineData("C2c2", "d09d-optioninf", Marks + "func f<T, U>(y: ref/T)\n    T is Option<U>\n    T is Mark\n    needMark(y)\n", "InvalidConstraint_Kd: func f<T, U>(y: ref/T)")] // O-344 (a)
    // SPEC 8.2.3: `T is Copy` is evidence for the identical `Option<U>`, whose own Copy path is Unknown (not Refuted): valid; c2-4a
    // keys a constructed member to its class, so the premise reaches it (the Middle's R2-C1, its N7 with r2-c2 and r2-c7).
    [InlineData("c2-4", "d09c-optioncopy", "func f<T, U>(x: Option<U>) -> (Option<U>, Option<U>)\n    T is Option<U>\n    T is Copy\n    return (x, x)\n", "")]
    [InlineData("guard", "d09s-primcopy", "func f<T>(x: i32) -> (i32, i32)\n    T is i32\n    T is Copy\n    return (x, x)\n", "")]
    [InlineData("guard", "d09r-ok", Marks + "struct Leaf2\n    Self is Mark\n    public var v: i32\nfunc f<T>(x: ref/Leaf2)\n    T is Leaf2\n    T is Mark\n    needMark<Leaf2>(x)\n", "")]
    [InlineData("guard", "d09t-ok", Marks + "struct Leaf2\n    Self is Mark\n    public var v: i32\nfunc f<T>(y: ref/T)\n    T is Leaf2\n    T is Mark\n    needMark(y)\n", "")]
    [InlineData("guard", "d09y-ok", Marks + "contract Holder\n    associate Item\nstruct Leaf2\n    Self is Mark\n    public var v: i32\nfunc f<T>(x: ref/Leaf2)\n    T is Holder\n    T.(Holder).Item is Leaf2\n    T.(Holder).Item is Mark\n    needMark<Leaf2>(x)\n", "")]

    // SPEC 8.8, 8.3.2: `Self.(K).Y is M` supplies D to Box's condition whatever M's own state; its check ends today (ACheckOverAConditionalClauseEnds).
    [InlineData("C2e", "p27c-condrec", Conditions + "contract M: K, D\n" + Clauses + "func probe<T>(value: ref/T.(K).Y)\n    T is M\n    needs<T.(K).Y>(value)\n", "", Explicit = true)]
    [InlineData("C2e", "p31-prepdemand", Conditions + "contract M: K, D\n" + Clauses + "func probe<T, W>(value: ref/T.(K).Y)\n    T is M\n    W is T\n    needs<T.(K).Y>(value)\n", "", Explicit = true)]
    // SPEC 8.4.3, 8.7.1 (DECISIONS BU-152): a judged intrinsic parent is a requirement; IntTree's clause supplies PrimitiveInteger to a
    // projection two links up under `T is U` (the structural supply at depth and the boundary's reading); without the parent, unproven.
    [InlineData("C2a1", "tP1-intparent", "contract Tree\n    associate Child is Tree\ncontract IntTree: Tree, PrimitiveInteger\n    Self.(Tree).Child is IntTree\ncontract Tag\nfunc needs<V>(v: ref/V)\n    V is Tag\n    ()\nfunc needsPI<V>(v: ref/V)\n    V is PrimitiveInteger\n    ()\nfunc probe<T, U>(value: ref/U.(Tree).Child.(Tree).Child)\n    T is IntTree\n    T is U\n    needsPI<U.(Tree).Child.(Tree).Child>(value)\n", "")]
    [InlineData("C2a1", "tP1n-intparent", "contract Tree\n    associate Child is Tree\ncontract IntTree: Tree\n    Self.(Tree).Child is IntTree\ncontract Tag\nfunc needs<V>(v: ref/V)\n    V is Tag\n    ()\nfunc needsPI<V>(v: ref/V)\n    V is PrimitiveInteger\n    ()\nfunc probe<T, U>(value: ref/U.(Tree).Child.(Tree).Child)\n    T is IntTree\n    T is U\n    needsPI<U.(Tree).Child.(Tree).Child>(value)\n", "UnprovenConstraint_Kd: needsPI<U.(Tree).Child.(Tree).Child>(value)")]

    // SPEC 8.3.2, 8.7.4 (R7-C2b b1): Copy and Owned are PrimitiveInteger's refinement parents, so a PrimitiveInteger that refinement
    // supplies at any projection supplies both there: IntTree's Value one, two and three links up, under an enclosing struct's premise
    // (b1-r5), while IntTree waits for its closed clause's round (b1-r1), and PrimitiveInteger as a parent two links up (b1v6). The
    // invalid twins keep their records: a Value of a parentless Contract, Sealed (never supplied, 8.7.2), both polarities of Copy,
    // PTree without the parent, IntTree failed by a refuted closed clause (b1-r2), a struct conforming to a child of PrimitiveInteger
    // (b1-r12). A formation under a PrimitiveInteger premise reads its Copy (b1-r22). The supply also gives a value read at depth its
    // copy (b1-valueread, InternalInvariant_Kd before) and makes `not Copy` there contradictory evidence (b1-notcopy, accepted before).
    [InlineData("guard", "b1v0-depth1", IntTrees + "group H\n    public func twice<T>(value: T.(Tree).Value) -> (T.(Tree).Value, T.(Tree).Value)\n        T is IntTree\n        return (value, value)\n", "")]
    [InlineData("C2b", "b1v1-depth2", IntTrees + IntTwice, "")]
    [InlineData("C2b", "b1v2-depth3", IntTrees + "group H\n    public func twice<T>(value: T.(Tree).Child.(Tree).Child.(Tree).Value) -> (T.(Tree).Child.(Tree).Child.(Tree).Value, T.(Tree).Child.(Tree).Child.(Tree).Value)\n        T is IntTree\n        return (value, value)\n", "")]
    [InlineData("C2b", "b1v3-owned", IntTrees + "func needsOwned<V>(v: ref/V)\n    V is Owned\n    ()\nfunc probe<T>(value: ref/T.(Tree).Child.(Tree).Value)\n    T is IntTree\n    needsOwned<T.(Tree).Child.(Tree).Value>(value)\n", "")]
    [InlineData("guard", "b1v4-direct", "group H\n    public func twice<T>(value: T) -> (T, T)\n        T is PrimitiveInteger\n        return (value, value)\n", "")]
    [InlineData("guard", "b1v5-identity", "group H\n    public func twice<T, U>(value: U) -> (U, U)\n        T is PrimitiveInteger\n        U is T\n        return (value, value)\n", "")]
    [InlineData("C2b", "b1v6-intparent", "contract Tree\n    associate Child is Tree\ncontract PTree: Tree, PrimitiveInteger\n    Self.(Tree).Child is PTree\ngroup H\n    public func twice<T>(value: T.(Tree).Child.(Tree).Child) -> (T.(Tree).Child.(Tree).Child, T.(Tree).Child.(Tree).Child)\n        T is PTree\n        return (value, value)\n", "")]
    [InlineData("C2b", "b1-r1-pending", IntTrees + "    i32 is PrimitiveInteger\n" + IntTwice, "")]
    [InlineData("C2b", "b1-r5-enclosing", IntTrees + "struct Holder<T>\n    T is IntTree\n    public var tag: i32 = 0\n    public func twice(self: ref/Self, value: T.(Tree).Child.(Tree).Value) -> (T.(Tree).Child.(Tree).Value, T.(Tree).Child.(Tree).Value)\n        return (value, value)\n", "")]
    [InlineData("C2b", "b1-valueread-d2", IntTrees + "func probe<T>(r: ref/T.(Tree).Child.(Tree).Value) -> T.(Tree).Child.(Tree).Value\n    T is IntTree\n    return r\n", "")]
    [InlineData("C2b", "b1-notcopy-d2", IntTrees + "func probe<T>(value: T.(Tree).Child.(Tree).Value) -> i32\n    T is IntTree\n    T.(Tree).Child.(Tree).Value is not Copy\n    return 0\n", "InvalidConstraint_Kd: func probe<T>(value: T.(Tree).Child.(Tree).Value) -> i32 <- T.(Tree).Child.(Tree).Value is not Copy")] // R7-C3 (C2 ruling 245 (i), Q13): SPEC 8.2.3's "once"; the premise clauses are the record's related premises
    [InlineData("guard", "b1-r22-formation", "struct Pair<X>\n    X is Copy\n    public var first: X\nfunc g<T, U>(u: ref/U)\n    T is PrimitiveInteger\n    U is Pair<T>\n    ()\n", "")]
    [InlineData("guard", "b1n1-marker", "contract Tree\n    associate Child is Tree\n    associate Value\ncontract Marker\ncontract IntTree: Tree\n    Self.(Tree).Child is IntTree\n    Self.(Tree).Value is Marker\n" + IntTwice, "TransferRequired_Kd: return (value, value)")]
    [InlineData("guard", "b1n2-sealed", IntTrees + "func needsSealed<V>(v: ref/V)\n    V is Sealed\n    ()\nfunc probe<T>(value: ref/T.(Tree).Child.(Tree).Value)\n    T is IntTree\n    needsSealed<T.(Tree).Child.(Tree).Value>(value)\n", "UnprovenConstraint_Kd: needsSealed<T.(Tree).Child.(Tree).Value>(value)")]
    [InlineData("guard", "b1n3-notcopy", "group H\n    public func twice<T>(value: T) -> (T, T)\n        T is PrimitiveInteger\n        T is not Copy\n        return (value, value)\n", "InvalidConstraint_Kd: public func twice<T>(value: T) -> (T, T)")]
    // R7-C2c2 c2-5b2 (SPEC 8.3.2 Exact assumption and Negation; labelled from SPEC first, artifacts/units/r7-c25b2/labels.md): a negated
    // atom of any kind, a Contract one (nc2, nm2, nca2) or an identity (nts2), that a clause supplies two or more links up proves the
    // negated requirement, through `U is T` too, refutes an overload's Constraint (SPEC 10.1) and holds in a conformance's scope; a
    // written premise against it is contradictory evidence (nc2c to ncs2c, class 1). Its twins keep their verdicts: depth 1, another
    // Contract or reference, no clause, the positive clause, the use on T itself, a negated disjunction (8.3.1: no De Morgan), the
    // positive clause against a written negation, a Contract's own premises meeting, a Semantics atom (the admitted set decides, 8.3.4).
    // nts2c-rev is invalid and accepted until PLAN's open issue against c2-5c's constructed roots: its identity at depth is no row.
    // ncs2g's noMark is a group's (SPEC 6.1.1: a Container body never finds a root function; ncs2, a guard row, keeps that record).
    // twcall keeps b1-r2-failed's records with a name discovery before the copy (W6: a walk that consulted a failure keeps no rows).
    [InlineData("c2-5b2", "nc2", "contract NC\n    associate Next is NC\n    associate V\n    Self.(NC).V is not Copy\n    func near(self: ref/Self) -> ref/Self.(NC).V\n    func deep(self: ref/Self) -> ref/Self.(NC).Next.(NC).V\nfunc noCopy<V>(v: ref/V) -> i32\n    V is not Copy\n    return 0\nfunc probe<T>(t: ref/T) -> i32\n    T is NC\n    let v = t.deep()\n    return noCopy(v)\n", "")]
    [InlineData("c2-5b2", "nc3", NegatedCopies + NoCopy + "func probe<T>(t: ref/T) -> i32\n    T is NC\n    let v = t.deep3()\n    return noCopy(v)\n", "")]
    [InlineData("c2-5b2", "nm2", NegatedMarks + NoMark + "func probe<T>(t: ref/T) -> i32\n    T is NM\n    let v = t.deep()\n    return noMark(v)\n", "")]
    [InlineData("c2-5b2", "nm3", NegatedMarks + NoMark + "func probe<T>(t: ref/T) -> i32\n    T is NM\n    let v = t.deep3()\n    return noMark(v)\n", "")]
    [InlineData("c2-5b2", "nm2i", NegatedMarks + NoMark + "func probe<T, U>(t: ref/U) -> i32\n    U is T\n    T is NM\n    let v = t.deep()\n    return noMark(v)\n", "")]
    [InlineData("c2-5b2", "nca2", NegatedDs + NoD + "func probe<T>(t: ref/T) -> i32\n    T is NA\n    let v = t.deep()\n    return noD(v)\n", "")]
    [InlineData("c2-5b2", "nca2p", NegatedDks + NoDw + "func probe<T>(t: ref/T) -> i32\n    T is NA\n    let v = t.deep()\n    let k = t.deepk()\n    return noDW(v, k)\n", "")]
    [InlineData("c2-5b2", "nts2", "contract KS\n    associate Next is KS\n    associate Item\n    Self.(KS).Item is not string\n    func deep(self: ref/Self) -> ref/Self.(KS).Next.(KS).Item\nfunc notText<V>(v: ref/V) -> i32\n    V is not string\n    return 0\nfunc probe<T>(t: ref/T) -> i32\n    T is KS\n    let v = t.deep()\n    return notText(v)\n", "")]
    [InlineData("c2-5b2", "nts3", NegatedTexts + NotText + "func probe<T>(t: ref/T) -> i32\n    T is KS\n    let v = t.deep3()\n    return notText(v)\n", "")]
    [InlineData("c2-5b2", "nts2i", NegatedTexts + NotText + "func probe<T, U>(t: ref/U) -> i32\n    U is T\n    T is KS\n    let v = t.deep()\n    return notText(v)\n", "")]
    [InlineData("c2-5b2", "nmr2", NegatedMarks + PickMark + "func probe<T>(t: ref/T) -> i32\n    T is NM\n    let v = t.deep()\n    return pickm(v)\n", "")]
    [InlineData("c2-5b2", "ntr2", NegatedTexts + PickText + "func probe<T>(t: ref/T) -> i32\n    T is KS\n    let v = t.deep()\n    return pickt(v)\n", "")]
    [InlineData("c2-5b2", "ncs2g", NegatedMarks + "group G\n    public func noMark<V>(v: ref/V) -> i32\n        V is not Marker\n        return 0\n" + Runner + "struct Box<T>\n    T is NM\n    Self is Runner\n    var value: T\n    public func run(self: ref/Self) -> i32\n        return 0\n    public func probe(self: ref/Self, t: ref/T) -> i32\n        let v = t.deep()\n        return G.noMark(v)\n", "")]
    [InlineData("c2-5b2", "nc2c", "contract Marker\ncontract NC\n    associate Next is NC\n    associate V\n    Self.(NC).V is not Marker\n    func deep(self: ref/Self) -> ref/Self.(NC).Next.(NC).V\nfunc probe<T>(t: ref/T) -> i32\n    T is NC\n    T.(NC).Next.(NC).V is Marker\n    return 0\n", "InvalidConstraint_Kd: func probe<T>(t: ref/T) -> i32")]
    [InlineData("c2-5b2", "nm3c", NegatedMarks + "func probe<T>(t: ref/T) -> i32\n    T is NM\n    T.(NM).Next.(NM).Next.(NM).V is Marker\n    return 0\n", "InvalidConstraint_Kd: func probe<T>(t: ref/T) -> i32")]
    [InlineData("c2-5b2", "ncp2c", NegatedCopies + "func probe<T>(t: ref/T) -> i32\n    T is NC\n    T.(NC).Next.(NC).V is Copy\n    return 0\n", "InvalidConstraint_Kd: func probe<T>(t: ref/T) -> i32")]
    [InlineData("c2-5b2", "nm2ci", NegatedMarks + "func probe<T, U>(t: ref/T) -> i32\n    T is NM\n    U is T\n    U.(NM).Next.(NM).V is Marker\n    return 0\n", "InvalidConstraint_Kd: func probe<T, U>(t: ref/T) -> i32")]
    [InlineData("c2-5b2", "nca2c", NegatedDs + "func probe<T>(t: ref/T) -> i32\n    T is NA\n    T.(NA).Next.(NA).V is D<i32>\n    return 0\n", "InvalidConstraint_Kd: func probe<T>(t: ref/T) -> i32")]
    [InlineData("c2-5b2", "ncs2c", NegatedMarks + Runner + "struct BoxC<T>\n    T is NM\n    T.(NM).Next.(NM).V is Marker\n    Self is Runner\n    var value: T\n    public func run(self: ref/Self) -> i32\n        return 0\n", "InvalidConstraint_Kd: struct BoxC<T> | ...")]
    [InlineData("c2-5b2", "nts2c", NegatedTexts + "func probe<T>(t: ref/T) -> i32\n    T is KS\n    T.(KS).Next.(KS).V is string\n    return 0\n", "InvalidConstraint_Kd: func probe<T>(t: ref/T) -> i32")]
    [InlineData("guard", "nc1", "contract NC\n    associate Next is NC\n    associate V\n    Self.(NC).V is not Copy\n    func near(self: ref/Self) -> ref/Self.(NC).V\n    func deep(self: ref/Self) -> ref/Self.(NC).Next.(NC).V\nfunc noCopy<V>(v: ref/V) -> i32\n    V is not Copy\n    return 0\nfunc probe<T>(t: ref/T) -> i32\n    T is NC\n    let v = t.near()\n    return noCopy(v)\n", "")]
    [InlineData("guard", "nm1", NegatedMarks + NoMark + "func probe<T>(t: ref/T) -> i32\n    T is NM\n    let v = t.near()\n    return noMark(v)\n", "")]
    [InlineData("guard", "nts1", NegatedTexts + NotText + "func probe<T>(t: ref/T) -> i32\n    T is KS\n    let v = t.near()\n    return notText(v)\n", "")]
    [InlineData("guard", "nm2n-other", NegatedMarks + "contract Tag\n" + NoTag + "func probe<T>(t: ref/T) -> i32\n    T is NM\n    let v = t.deep()\n    return noTag(v)\n", "UnprovenConstraint_Kd: return noTag(v)")]
    [InlineData("guard", "nm2n-bare", BareMarks + NoMark + "func probe<T>(t: ref/T) -> i32\n    T is NB\n    let v = t.deep()\n    return noMark(v)\n", "UnprovenConstraint_Kd: return noMark(v)")]
    [InlineData("guard", "nm2n-pos", PositiveMarks + NoMark + "func probe<T>(t: ref/T) -> i32\n    T is PM\n    let v = t.deep()\n    return noMark(v)\n", "NoApplicableOverload_Kd: return noMark(v)")]
    [InlineData("guard", "nm0n-self", NegatedMarks + NoMark + "func probe<T>(t: ref/T) -> i32\n    T is NM\n    return noMark(t)\n", "UnprovenConstraint_Kd: return noMark(t)")]
    [InlineData("guard", "ncs2", NegatedMarks + NoMark + Runner + "struct Box<T>\n    T is NM\n    Self is Runner\n    var value: T\n    public func run(self: ref/Self) -> i32\n        return 0\n    public func probe(self: ref/Self, t: ref/T) -> i32\n        let v = t.deep()\n        return noMark(v)\n", "UnresolvedBinding_Kd: return noMark(v)")]
    [InlineData("guard", "nm2n-or", "contract Marker\ncontract Tag\ncontract NO\n    associate Next is NO\n    associate V\n    Self.(NO).V is not Marker or Tag\n    func near(self: ref/Self) -> ref/Self.(NO).V\n    func deep(self: ref/Self) -> ref/Self.(NO).Next.(NO).V\n    func deep3(self: ref/Self) -> ref/Self.(NO).Next.(NO).Next.(NO).V\n" + NoMark + "func probe<T>(t: ref/T) -> i32\n    T is NO\n    let v = t.deep()\n    return noMark(v)\n", "UnprovenConstraint_Kd: return noMark(v)")]
    [InlineData("guard", "nca2n", NegatedDs + NoD64 + "func probe<T>(t: ref/T) -> i32\n    T is NA\n    let v = t.deep()\n    return noD64(v)\n", "UnprovenConstraint_Kd: return noD64(v)")]
    [InlineData("guard", "nts2n-other", NegatedTexts + NotI32 + "func probe<T>(t: ref/T) -> i32\n    T is KS\n    let v = t.deep()\n    return notI32(v)\n", "UnprovenConstraint_Kd: return notI32(v)")]
    [InlineData("guard", "nts2n-pos", PositiveTexts + NotText + "func probe<T>(t: ref/T) -> i32\n    T is KP\n    let v = t.deep()\n    return notText(v)\n", "UnprovenConstraint_Kd: return notText(v)")]
    [InlineData("guard", "nm2c-rev", PositiveMarks + "func probe<T>(t: ref/T) -> i32\n    T is PM\n    T.(PM).Next.(PM).V is not Marker\n    return 0\n", "InvalidConstraint_Kd: func probe<T>(t: ref/T) -> i32")]
    [InlineData("c2-5c", "nts2c-rev", PositiveTexts + "func probe<T>(t: ref/T) -> i32\n    T is KP\n    T.(KP).Next.(KP).V is not string\n    return 0\n", "InvalidConstraint_Kd: func probe<T>(t: ref/T) -> i32", Explicit = true)]
    [InlineData("guard", "nh2c", "contract Marker\ncontract H2\n    associate Next is H2\n    associate V\n    Self.(H2).V is Marker\n    Self.(H2).Next.(H2).V is not Marker\n    func near(self: ref/Self) -> ref/Self.(H2).V\n    func deep(self: ref/Self) -> ref/Self.(H2).Next.(H2).V\n    func deep3(self: ref/Self) -> ref/Self.(H2).Next.(H2).Next.(H2).V\n", "InvalidConstraint_Kd: contract H2 | ...")]
    [InlineData("guard", "nmr2-pos", PositiveMarks + PickMark + "func probe<T>(t: ref/T) -> i32\n    T is PM\n    let v = t.deep()\n    return pickm(v)\n", "")]
    [InlineData("guard", "nmr2n-bare", BareMarks + PickMark + "func probe<T>(t: ref/T) -> i32\n    T is NB\n    let v = t.deep()\n    return pickm(v)\n", "UnprovenConstraint_Kd: return pickm(v)")]
    [InlineData("guard", "nmr2n-self", NegatedMarks + PickMark + "func probe<T>(t: ref/T) -> i32\n    T is NM\n    return pickm(t)\n", "UnprovenConstraint_Kd: return pickm(t)")]
    [InlineData("guard", "ntr2n-bare", BareMarks + PickText + "func probe<T>(t: ref/T) -> i32\n    T is NB\n    let v = t.deep()\n    return pickt(v)\n", "UnprovenConstraint_Kd: return pickt(v)")]
    [InlineData("guard", "ns0", "func probe<s/T>(value: s/T) -> i32\n    s is not object\n    return 0\n", "")]
    [InlineData("guard", "nsem-c", "contract NS\n    associate Next is NS\n    associate V\n    Self.(NS).V is not object\n    func near(self: ref/Self) -> ref/Self.(NS).V\n    func deep(self: ref/Self) -> ref/Self.(NS).Next.(NS).V\n    func deep3(self: ref/Self) -> ref/Self.(NS).Next.(NS).Next.(NS).V\n", "UnresolvedBinding_Kd: Self.(NS).V is not object | ...")]
    [InlineData("guard", "b1n4-noparent", "contract Tree\n    associate Child is Tree\ncontract PTree: Tree\n    Self.(Tree).Child is PTree\ngroup H\n    public func twice<T>(value: T.(Tree).Child.(Tree).Child) -> (T.(Tree).Child.(Tree).Child, T.(Tree).Child.(Tree).Child)\n        T is PTree\n        return (value, value)\n", "TransferRequired_Kd: return (value, value)")]
    [InlineData("guard", "b1-r2-failed", IntTrees + "    bool is PrimitiveInteger\n" + IntTwice, "UnsatisfiedConstraint_Kd: contract IntTree: Tree | UnprovenConstraint_Kd: Self.(Tree).Child is IntTree | UnprovenConstraint_Kd: Self.(Tree).Value is PrimitiveInteger | UnsatisfiedConstraint_Kd: bool is PrimitiveInteger | InvalidConstraint_Kd: public func twice<T>(value: T.(Tree).Child.(Tree).Value) -> (T.(Tree).Child.(Tree).Value, T.(Tree).Child.(Tree).Value)")] // R7-C3 C3-1b (F's C2 rulings 288 (2), 290; DECISIONS Q13, code): `twice`'s premise `T is IntTree` names an invalid declaration (IntTree's closed `bool is PrimitiveInteger` is Refuted; SPEC 8.3.1, 8.2.3), as `caller`'s `T is Marker` does: one record, same site
    [InlineData("c2-5b2", "twcall", "contract Tree\n    associate Child is Tree\n    associate Value\ncontract Num\n    func n(self: ref/Self) -> i32\ncontract IntTree: Tree\n    Self.(Tree).Child is IntTree\n    Self.(Tree).Value is PrimitiveInteger\n    Self.(Tree).Value is Num\n    bool is PrimitiveInteger\ngroup H\n    public func twice<T>(value: T.(Tree).Child.(Tree).Value) -> (T.(Tree).Child.(Tree).Value, T.(Tree).Child.(Tree).Value)\n        T is IntTree\n        let k = value.n()\n        return (value, value)\n", "UnsatisfiedConstraint_Kd: contract IntTree: Tree | UnprovenConstraint_Kd: Self.(Tree).Child is IntTree | UnprovenConstraint_Kd: Self.(Tree).Value is PrimitiveInteger | UnprovenConstraint_Kd: Self.(Tree).Value is Num | UnsatisfiedConstraint_Kd: bool is PrimitiveInteger | InvalidConstraint_Kd: public func twice<T>(value: T.(Tree).Child.(Tree).Value) -> (T.(Tree).Child.(Tree).Value, T.(Tree).Child.(Tree).Value)")]
    // Guard rows (F's CC-29 (4), the Middle's C-570; CC-30 N7, N8, the review's Phase B): rd-other and rd-neg, a failed premise the
    // Copy walk does not read (another subject; a negation supplies only its atom's refutation), keep `TransferRequired_Kd` (rule W's
    // limit, SPEC 23.1.2); rv-nc2-need, a positive use the negated clause at depth refutes, is `NoApplicableOverload_Kd` (SPEC 8.3.2,
    // 10.1; dev `UnprovenConstraint_Kd`); rv-sp-field, a field's Type spelling, is accepted (dev rejects it).
    [InlineData("guard", "rd-other", "func dup<T, U>(value: ref/T, other: ref/U) -> T\n    U is Missing\n    return value@follow\n", "InvalidConstraint_Kd: func dup<T, U>(value: ref/T, other: ref/U) -> T | TransferRequired_Kd: return value@follow | UnresolvedBinding_Kd: U is Missing")]
    [InlineData("guard", "rd-neg", "func dup<T>(value: ref/T) -> T\n    T is not Missing\n    return value@follow\n", "InvalidConstraint_Kd: func dup<T>(value: ref/T) -> T | TransferRequired_Kd: return value@follow | UnresolvedBinding_Kd: T is not Missing")]
    [InlineData("guard", "rv-nc2-need", NegatedCopies + "func needCopy<V>(v: ref/V) -> i32\n    V is Copy\n    return 0\nfunc probe<T>(t: ref/T) -> i32\n    T is NC\n    let v = t.deep()\n    return needCopy(v)\n", "NoApplicableOverload_Kd: return needCopy(v)")]
    [InlineData("guard", "rv-sp-field", NegatedMarks + NoMark + "struct Box<T>\n    T is NM\n    public var f: T.(NM).Next.(NM).V\nfunc probe<T>(b: ref/Box<T>) -> i32\n    T is NM\n    let v = b.f@ref\n    return noMark(v)\n", "")]
    [InlineData("guard", "b1-r12-userchild", "contract IntP: PrimitiveInteger\nstruct S\n    Self is IntP\n    public var tag: i32 = 0\n", "UnsatisfiedConstraint_Kd: Self is IntP")]

    // PLAN G1407 (U1; artifacts/units/g1406/plan.md section 3, labelled from SPEC first): a Contract row supplies at its link what a
    // premise supplies at its subject (SPEC 8.3.2, 8.2.3). Form A: a Contract's own `Self` clause (8.7.1: `contract C: Copy` means
    // `Self is Copy`) holds at every link of its recursion, one and two links up; a written premise against it is contradictory evidence
    // (8.7.3, class 1); a positive use the negation refutes has no applicable overload (10.1); a bare Contract supplies nothing. Form B:
    // a conjunction clause at depth supplies each conjunct, nested ones too (8.3.1, 8.3.2 Conjunction); a disjunction supplies neither
    // side, and a negated conjunction no negated conjunct (no De Morgan). cp1a, nop0 and pand1 are guards (an ancestor, the eager supply).
    [InlineData("g1407", "nop1", NotPayloads + NeedNotPayload + "func probe<T>(t: ref/T) -> i32\n    T is NP\n    let n = t.next()\n    return needNotOP(n)\n", "")]
    [InlineData("g1407", "cp1", SelfCopies + NeedCopy + "func probe<T>(t: ref/T) -> i32\n    T is CP\n    let n = t.next()\n    return needCopy(n)\n", "")]
    [InlineData("g1407", "nop2", NotPayloads + NeedNotPayload + "func probe<T>(t: ref/T) -> i32\n    T is NP\n    let n = t.next()\n    let m = n.next()\n    return needNotOP(m)\n", "")]
    [InlineData("guard", "cp1a", ParentCopies + NeedCopy + "func probe<T>(t: ref/T) -> i32\n    T is CPA\n    let n = t.next()\n    return needCopy(n)\n", "")]
    [InlineData("guard", "nop0", NotPayloads + NeedNotPayload + "func probe<T>(t: ref/T) -> i32\n    T is NP\n    return needNotOP(t)\n", "")]
    [InlineData("g1407", "nop1c", NotPayloads + "func probe<T>(t: ref/T) -> i32\n    T is NP\n    T.(NP).Next is ObjectPayload\n    return 0\n", "InvalidConstraint_Kd: func probe<T>(t: ref/T) -> i32")]
    [InlineData("g1407", "cp1c", SelfCopies + "func probe<T>(t: ref/T) -> i32\n    T is CP\n    T.(CP).Next is not Copy\n    return 0\n", "InvalidConstraint_Kd: func probe<T>(t: ref/T) -> i32")]
    [InlineData("g1407", "nop2c", NotPayloads + "func probe<T>(t: ref/T) -> i32\n    T is NP\n    T.(NP).Next.(NP).Next is ObjectPayload\n    return 0\n", "InvalidConstraint_Kd: func probe<T>(t: ref/T) -> i32")]
    [InlineData("g1407", "nop1-pos", NotPayloads + NeedPayload + "func probe<T>(t: ref/T) -> i32\n    T is NP\n    let n = t.next()\n    return needOP(n)\n", "NoApplicableOverload_Kd: return needOP(n)")]
    [InlineData("guard", "nb1", BareNexts + NeedNotPayload + "func probe<T>(t: ref/T) -> i32\n    T is NB0\n    let n = t.next()\n    return needNotOP(n)\n", "UnprovenConstraint_Kd: return needNotOP(n)")]
    [InlineData("g1407", "nand2", NegatedConjuncts + NoMark + "func probe<T>(t: ref/T) -> i32\n    T is NA2\n    let v = t.deep()\n    return noMark(v)\n", "")]
    [InlineData("g1407", "pand2", PositiveConjuncts + NeedMarker + "func probe<T>(t: ref/T) -> i32\n    T is PA2\n    let v = t.deep()\n    return needMark(v)\n", "")]
    [InlineData("g1407", "nand2t", NegatedConjuncts + NoTag + "func probe<T>(t: ref/T) -> i32\n    T is NA2\n    let v = t.deep()\n    return noTag(v)\n", "")]
    [InlineData("g1407", "pand3n", NestedConjuncts + NeedOther + "func probe<T>(t: ref/T) -> i32\n    T is PA3\n    let v = t.deep()\n    return needOther(v)\n", "")]
    [InlineData("guard", "pand1", PositiveConjuncts + NeedMarker + "func probe<T>(t: ref/T) -> i32\n    T is PA2\n    let v = t.near()\n    return needMark(v)\n", "")]
    [InlineData("g1407", "nand2c", NegatedConjuncts + "func probe<T>(t: ref/T) -> i32\n    T is NA2\n    T.(NA2).Next.(NA2).V is Marker\n    return 0\n", "InvalidConstraint_Kd: func probe<T>(t: ref/T) -> i32")]
    [InlineData("g1407", "pand2c", PositiveConjuncts + "func probe<T>(t: ref/T) -> i32\n    T is PA2\n    T.(PA2).Next.(PA2).V is not Tag\n    return 0\n", "InvalidConstraint_Kd: func probe<T>(t: ref/T) -> i32")]
    [InlineData("g1407", "pand2-neg", PositiveConjuncts + NoMark + "func probe<T>(t: ref/T) -> i32\n    T is PA2\n    let v = t.deep()\n    return noMark(v)\n", "NoApplicableOverload_Kd: return noMark(v)")]
    [InlineData("guard", "nor2", NegatedDisjuncts + NoMark + "func probe<T>(t: ref/T) -> i32\n    T is NOR2\n    let v = t.deep()\n    return noMark(v)\n", "UnprovenConstraint_Kd: return noMark(v)")]
    [InlineData("guard", "nnand2", NegatedConjunction + NoMark + "func probe<T>(t: ref/T) -> i32\n    T is NNA2\n    let v = t.deep()\n    return noMark(v)\n", "UnprovenConstraint_Kd: return noMark(v)")]
    // The review's twins (F's G1406-3 C1; fail-before: the review's base run on 3cbde9881). The link's own identity item is recorded in
    // the same intake: `Self.(KI).X is Self` with `Self.(KI).X is Marker` makes `T.(KI).Next` a Marker (f-idl1, 8.2.3 grounds), and a
    // written `T.(KI).Next is not Marker` is contradictory evidence (f-idl1c, 8.3.2); an identity atom inside a conjunction is recorded
    // per conjunct (f-idand2); a class mate's row read at the link supplies there as the link's own (h-nop1i, `U is T`; row (5)).
    [InlineData("g1407", "f-idl1", LinkIdentities + NeedMarker + "func probe<T>(t: ref/T) -> i32\n    T is KI\n    let n = t.next()\n    return needMark(n)\n", "")]
    [InlineData("g1407", "f-idl1c", LinkIdentities + "func probe<T>(t: ref/T) -> i32\n    T is KI\n    T.(KI).Next is not Marker\n    return 0\n", "InvalidConstraint_Kd: func probe<T>(t: ref/T) -> i32")]
    [InlineData("g1407", "f-idand2", ConjunctIdentities + NeedMarker + "func probe<T>(t: ref/T) -> i32\n    T is KC\n    let v = t.deep()\n    return needMark(v)\n", "")]
    [InlineData("g1407", "h-nop1i", NotPayloads + NeedNotPayload + "func probe<T, U>(t: ref/U) -> i32\n    U is T\n    T is NP\n    let n = t.next()\n    return needNotOP(n)\n", "")]

    // PLAN G1406 (U2; artifacts/units/g1406/plan.md section 3's form C, labelled from SPEC first): the identity judge reads a positive
    // identity clause at depth (SPEC 8.2.3: the grounds apply to every projection a query needs however deep; 8.3.2 Type identity).
    // `Self.(KP).V is string` makes V a string two and three links up and through `U is T` (rv-g-tpos2, tpos3, tpos2i), so the overload
    // requiring it is strictly best (rv-g-ntr2p, 10.1); a written negation of it is contradictory evidence (nts3c-rev, nts2ci-rev, class 1,
    // as nts2c-rev above), and a use requiring i32 is refuted (ti32-2, 10.1). rv-g-tpos1 (one link) and tw2 (an associate no clause
    // states: no evidence) are guards.
    [InlineData("g1406", "rv-g-tpos2", PositiveTexts + TextOnly + "func probe<T>(t: ref/T) -> i32\n    T is KP\n    let v = t.deep()\n    return textOnly(v)\n", "", Explicit = true)]
    [InlineData("g1406", "rv-g-ntr2p", PositiveTexts + PickText + "func probe<T>(t: ref/T) -> i32\n    T is KP\n    let v = t.deep()\n    return pickt(v)\n", "", Explicit = true)]
    [InlineData("g1406", "tpos3", PositiveTexts + TextOnly + "func probe<T>(t: ref/T) -> i32\n    T is KP\n    let v = t.deep3()\n    return textOnly(v)\n", "", Explicit = true)]
    [InlineData("g1406", "tpos2i", PositiveTexts + TextOnly + "func probe<T, U>(t: ref/U) -> i32\n    U is T\n    T is KP\n    let v = t.deep()\n    return textOnly(v)\n", "", Explicit = true)]
    [InlineData("guard", "rv-g-tpos1", PositiveTexts + TextOnly + "func probe<T>(t: ref/T) -> i32\n    T is KP\n    let v = t.near()\n    return textOnly(v)\n", "")]
    [InlineData("g1406", "nts3c-rev", PositiveTexts + "func probe<T>(t: ref/T) -> i32\n    T is KP\n    T.(KP).Next.(KP).Next.(KP).V is not string\n    return 0\n", "InvalidConstraint_Kd: func probe<T>(t: ref/T) -> i32", Explicit = true)]
    [InlineData("g1406", "nts2ci-rev", PositiveTexts + "func probe<T, U>(t: ref/T) -> i32\n    T is KP\n    U is T\n    U.(KP).Next.(KP).V is not string\n    return 0\n", "InvalidConstraint_Kd: func probe<T, U>(t: ref/T) -> i32", Explicit = true)]
    [InlineData("g1406", "ti32-2", PositiveTexts + NeedI32 + "func probe<T>(t: ref/T) -> i32\n    T is KP\n    let v = t.deep()\n    return needI32(v)\n", "NoApplicableOverload_Kd: return needI32(v)", Explicit = true)]
    [InlineData("guard", "tw2", OtherAssociates + TextOnly + "func probe<T>(t: ref/T) -> i32\n    T is KP2\n    let w = t.deepw()\n    return textOnly(w)\n", "UnprovenConstraint_Kd: return textOnly(w)")]

    // SPEC 8.3.2 Contract refinement, 8.4.1, 8.4.3 (R7-C2c2 c2-5a; PLAN G402, G837, G849): `T is C<i32>` supplies C's own and
    // inherited Constraints with T for Self and i32 for C's parameter, at every projection a query needs: a clause naming the Contract's
    // parameter (x-args, MT), swapping two (x-swap), over an ancestor reference's projection (b-cargs, k-nonrec, contractargs, p05) or
    // over the parameter alone (x-carried). The invalid twins keep their records: an abstract projection's other Contract argument is
    // Unknown (8.3.2 Closed world). Programs rebuilt from the Middle's C-304 R2 and C-310 F3 texts (artifacts\units\r7-c25\labels.md).
    [InlineData("guard", "x-args0-v", MarkArgs + "func probe<T>(t: ref/T)\n    T is MT<i32>\n    needMark<T, i32>(t)\n", "")]
    [InlineData("guard", "x-args1-v", MarkArgs + "func probe<T>(c: ref/T.(Tree).Child)\n    T is MT<i32>\n    needMark<T.(Tree).Child, i32>(c)\n", "")]
    [InlineData("guard", "x-args2-v", MarkArgs + "func probe<T>(c: ref/T.(Tree).Child.(Tree).Child)\n    T is MT<i32>\n    needMark<T.(Tree).Child.(Tree).Child, i32>(c)\n", "")]
    [InlineData("guard", "x-args3-v", MarkArgs + "func probe<T>(c: ref/T.(Tree).Child.(Tree).Child.(Tree).Child)\n    T is MT<i32>\n    needMark<T.(Tree).Child.(Tree).Child.(Tree).Child, i32>(c)\n", "")]
    [InlineData("guard", "x-argsrow2-v", MarkArgs + "func probe<T>(c: ref/T.(Tree).Child.(Tree).Child)\n    T is MT<i32>\n    needMT<T.(Tree).Child.(Tree).Child, i32>(c)\n", "")]
    [InlineData("guard", "b-args-v", MarkArgs + MarkHolder + "        N.needMark<T.(Tree).Child, i32>(c)\n", "")]
    [InlineData("guard", "x-args1-n", MarkArgs + "func probe<T>(c: ref/T.(Tree).Child)\n    T is MT<i32>\n    needMark<T.(Tree).Child, i64>(c)\n", "UnprovenConstraint_Kd: needMark<T.(Tree).Child, i64>(c)")]
    [InlineData("guard", "x-args2-n", MarkArgs + "func probe<T>(c: ref/T.(Tree).Child.(Tree).Child)\n    T is MT<i32>\n    needMark<T.(Tree).Child.(Tree).Child, i64>(c)\n", "UnprovenConstraint_Kd: needMark<T.(Tree).Child.(Tree).Child, i64>(c)")]
    [InlineData("guard", "x-args3-n", MarkArgs + "func probe<T>(c: ref/T.(Tree).Child.(Tree).Child.(Tree).Child)\n    T is MT<i32>\n    needMark<T.(Tree).Child.(Tree).Child.(Tree).Child, i64>(c)\n", "UnprovenConstraint_Kd: needMark<T.(Tree).Child.(Tree).Child.(Tree).Child, i64>(c)")]
    [InlineData("guard", "x-argsrow2-n", MarkArgs + "func probe<T>(c: ref/T.(Tree).Child.(Tree).Child)\n    T is MT<i32>\n    needMT<T.(Tree).Child.(Tree).Child, i64>(c)\n", "UnprovenConstraint_Kd: needMT<T.(Tree).Child.(Tree).Child, i64>(c)")]
    [InlineData("guard", "x-args1-nmt", MarkArgs + "func probe<T>(c: ref/T.(Tree).Child)\n    T is MT<i32>\n    needMT<T.(Tree).Child, i64>(c)\n", "UnprovenConstraint_Kd: needMT<T.(Tree).Child, i64>(c)")]
    [InlineData("guard", "x-args-noclause-n", "contract Tree\n    associate Child is Tree\ncontract Mark<A>\ncontract MT<A>: Tree, Mark<A>\nfunc needMark<V, W>(v: ref/V)\n    V is Mark<W>\n    ()\nfunc probe<T>(c: ref/T.(Tree).Child)\n    T is MT<i32>\n    needMark<T.(Tree).Child, i32>(c)\n", "UnprovenConstraint_Kd: needMark<T.(Tree).Child, i32>(c)")]
    [InlineData("guard", "b-args-n", MarkArgs + MarkHolder + "        N.needMark<T.(Tree).Child, i64>(c)\n", "UnprovenConstraint_Kd: N.needMark<T.(Tree).Child, i64>(c)")]
    [InlineData("guard", "b-args-mixn", MarkArgs + MarkHolder + "        N.needMark<T.(Tree).Child, i32>(c)\n        N.needMark<T.(Tree).Child, i64>(c)\n", "UnprovenConstraint_Kd: N.needMark<T.(Tree).Child, i64>(c)")]
    [InlineData("guard", "x-swap0-v", MarkArgs + Swap + "func probe<T>(t: ref/T)\n    T is P2<i32, i64>\n    needMark<T, i32>(t)\n", "")]
    [InlineData("guard", "x-swap1-v", MarkArgs + Swap + "func probe<T>(c: ref/T.(Tree).Child)\n    T is P2<i32, i64>\n    needMark<T.(Tree).Child, i64>(c)\n", "")]
    [InlineData("guard", "x-swap2-v", MarkArgs + Swap + "func probe<T>(c: ref/T.(Tree).Child.(Tree).Child)\n    T is P2<i32, i64>\n    needMark<T.(Tree).Child.(Tree).Child, i32>(c)\n", "")]
    [InlineData("guard", "x-swap0-n", MarkArgs + Swap + "func probe<T>(t: ref/T)\n    T is P2<i32, i64>\n    needMark<T, i64>(t)\n", "UnprovenConstraint_Kd: needMark<T, i64>(t)")]
    [InlineData("guard", "x-swap1-n", MarkArgs + Swap + "func probe<T>(c: ref/T.(Tree).Child)\n    T is P2<i32, i64>\n    needMark<T.(Tree).Child, i32>(c)\n", "UnprovenConstraint_Kd: needMark<T.(Tree).Child, i32>(c)")]
    [InlineData("guard", "x-swap2-n", MarkArgs + Swap + "func probe<T>(c: ref/T.(Tree).Child.(Tree).Child)\n    T is P2<i32, i64>\n    needMark<T.(Tree).Child.(Tree).Child, i64>(c)\n", "UnprovenConstraint_Kd: needMark<T.(Tree).Child.(Tree).Child, i64>(c)")]
    [InlineData("guard", "x-fixed1-i32", MarkArgs + Fixed + "func probe<T>(c: ref/T.(Tree).Child)\n    T is MF<i32>\n    needMark<T.(Tree).Child, i32>(c)\n", "")]
    [InlineData("guard", "x-fixed2-i32", MarkArgs + Fixed + "func probe<T>(c: ref/T.(Tree).Child.(Tree).Child)\n    T is MF<i32>\n    needMark<T.(Tree).Child.(Tree).Child, i32>(c)\n", "")]
    [InlineData("guard", "x-fixed1-i64", MarkArgs + Fixed + "func probe<T>(c: ref/T.(Tree).Child)\n    T is MF<i32>\n    needMark<T.(Tree).Child, i64>(c)\n", "UnprovenConstraint_Kd: needMark<T.(Tree).Child, i64>(c)")]
    [InlineData("guard", "x-fixed2-i64", MarkArgs + Fixed + "func probe<T>(c: ref/T.(Tree).Child.(Tree).Child)\n    T is MF<i32>\n    needMark<T.(Tree).Child.(Tree).Child, i64>(c)\n", "UnprovenConstraint_Kd: needMark<T.(Tree).Child.(Tree).Child, i64>(c)")]
    [InlineData("guard", "x-argsplain2-v", "contract Tree\n    associate Child is Tree\ncontract Plain\ncontract MP<A>: Tree, Plain\n    Self.(Tree).Child is MP<A>\nfunc needPlain<V>(v: ref/V)\n    V is Plain\n    ()\nfunc probe<T>(c: ref/T.(Tree).Child.(Tree).Child)\n    T is MP<i32>\n    needPlain<T.(Tree).Child.(Tree).Child>(c)\n", "")]
    [InlineData("guard", "b-cargs-v", KArgs + "contract CK<A>: K<A>\n    Self.(K<A>).X is Copy\n", "")]
    [InlineData("guard", "b-cargs2-v", KArgs + "contract CK<A>: K<A>\n    Self.(K<A>).X is Copy\n" + KTwice + "        T is CK<i32>\n        return (x, x)\n", "")]
    [InlineData("guard", "k-nonrec", KArgs + "contract CK<A>: K<A>\n    Self.(K<A>).X is PrimitiveInteger\n" + KTwice + "        T is CK<i32>\n        return (x, x)\n", "")]
    [InlineData("guard", "k-nonrec-marker", "contract Marker\n" + KArgs + "contract CK<A>: K<A>\n    Self.(K<A>).X is Marker\nfunc needMarker<V>(v: ref/V)\n    V is Marker\n    ()\nfunc probe<T>(x: ref/T.(K<i32>).X)\n    T is CK<i32>\n    needMarker<T.(K<i32>).X>(x)\n", "")]
    [InlineData("guard", "contractargs-i32", "contract K<A>\n    associate X\n    associate Child is K<A>\ncontract CK<A>: K<A>\n    Self.(K<A>).Child is CK<A>\n    Self.(K<A>).X is PrimitiveInteger\ngroup H\n    public func twice<T>(x: T.(K<i32>).Child.(K<i32>).X) -> (T.(K<i32>).Child.(K<i32>).X, T.(K<i32>).Child.(K<i32>).X)\n        T is CK<i32>\n        return (x, x)\n", "")]
    [InlineData("guard", "contractargs-single", "contract K<A>\n    associate X\n    associate Child is K<A>\ncontract CK<A>: K<A>\n    Self.(K<A>).Child is CK<A>\n    Self.(K<A>).X is PrimitiveInteger\nfunc probe<T>(t: ref/T)\n    T is CK<i32>\n    ()\n", "")]
    [InlineData("guard", "b-cargs-n1", KArgs + "contract CK<A>: K<A>\n" + KTwice + "        T is CK<i32>\n        return (x, x)\n", "TransferRequired_Kd: return (x, x)")]
    [InlineData("guard", "b-cargs-n2", "contract Marker\n" + KArgs + "contract CK<A>: K<A>\n    Self.(K<A>).X is Marker\n" + KTwice + "        T is CK<i32>\n        return (x, x)\n", "TransferRequired_Kd: return (x, x)")]
    [InlineData("guard", "b-cargs-n3", KArgs + "contract CK<A>: K<A>\n    Self.(K<A>).X is Copy\n" + KTwice + "        T is K<i32>\n        return (x, x)\n", "TransferRequired_Kd: return (x, x)")]
    // SPEC 6.1.4 (R7-C2c2 c2-5a): forming `CK<S>` judges CK's reference input condition `A is E` and never its implementation
    // requirement `Self.(K<A>).X is Copy`; the twins' input condition fails at the clause. cc-n1's supplied `i32 is E` is refuted: 8.3.2's
    // Error at the premise, which the function's record relates (an-n1 likewise).
    [InlineData("guard", "cc-v1", "contract E\nstruct S\n    Self is E\n    public var n: i32 = 0\n" + KArgs + "contract CK<A>: K<A>\n    A is E\n    Self.(K<A>).X is Copy\nfunc probe<T>(t: ref/T)\n    T is CK<S>\n    ()\n", "")]
    [InlineData("guard", "cc-n1", "contract E\n" + KArgs + "contract CK<A>: K<A>\n    A is E\n    Self.(K<A>).X is Copy\nfunc probe<T>(t: ref/T)\n    T is CK<i32>\n    ()\n", "InvalidConstraint_Kd: func probe<T>(t: ref/T) <- T is CK<i32> | InvalidConstraint_Kd: T is CK<i32>")]
    [InlineData("guard", "cc-n2", "contract E\ncontract D<U>\n    U is E\nfunc probe<T>(t: ref/T)\n    T is D<i32>\n    ()\n", "*: T is D<i32> | ...")]
    // SPEC 6.1.4, 8.2.3 (PLAN G1260's e1t and e1): A's clauses all name its Self, so forming `A<U, i32>` judges none of them; the identity
    // they derive between P and Q is a supplied ground, here U is Option<i32>. In e1 that supply makes U identical with Option<U>: one
    // premise error at its clause and no second record at `A<U, U>`.
    [InlineData("guard", "e1t", "contract A<P, Q>\n    associate X\n    associate Y\n    Self.X is P\n    Self.Y is Option<Q>\n    Self.X is Self.Y\nfunc f<T, U>(value: T) -> i32\n    T is A<U, i32>\n    return 0\n", "")]
    [InlineData("guard", "e1", "contract A<P, Q>\n    associate X\n    associate Y\n    Self.X is P\n    Self.Y is Option<Q>\n    Self.X is Self.Y\nfunc f<T, U>(value: T) -> i32\n    T is A<U, U>\n    return 0\n", "InvalidConstraint_Kd: T is A<U, U>")]
    [InlineData("guard", "p05", "contract CT<A>: GTree<A>, Copy\n    Self.(GTree<A>).Child is CT<A>\n" + GTwice + "        T is CT<i32>\n        return (x, x)\n", "")]
    [InlineData("guard", "p05-n1", "contract CT<A>: GTree<A>\n    Self.(GTree<A>).Child is CT<A>\n" + GTwice + "        T is CT<i32>\n        return (x, x)\n", "TransferRequired_Kd: return (x, x)")]
    [InlineData("guard", "p05-n2", "contract CT<A>: GTree<A>, Copy\n    Self.(GTree<A>).Child is CT<A>\n" + GTwice + "        T is GTree<i32>\n        return (x, x)\n", "TransferRequired_Kd: return (x, x)")]
    [InlineData("guard", "p05-n3", "contract CT<A>: GTree<A>, Copy\n    Self.(GTree<A>).Child is GTree<A>\n" + GTwice + "        T is CT<i32>\n        return (x, x)\n", "TransferRequired_Kd: return (x, x)")]
    [InlineData("guard", "x-carried-v", NeedE + "contract D<U>\n    U is E\nfunc probe<T, A>(t: ref/T, a: ref/A)\n    T is D<A>\n    needE<A>(a)\n", "")]
    [InlineData("guard", "x-carried-n1", NeedE + "contract D<U>\nfunc probe<T, A>(t: ref/T, a: ref/A)\n    T is D<A>\n    needE<A>(a)\n", "UnprovenConstraint_Kd: needE<A>(a)")]
    [InlineData("guard", "x-carried-n2", NeedE + "contract D<U>\n    U is E\nfunc probe<T, A>(t: ref/T, a: ref/A)\n    T is D<A>\n    needE<T>(t)\n", "UnprovenConstraint_Kd: needE<T>(t)")]
    [InlineData("guard", "x-carried-n3", NeedE + "contract D<U>\n    U is E\nfunc probe<T, A, B>(t: ref/T, a: ref/A, b: ref/B)\n    T is D<B>\n    needE<A>(a)\n", "UnprovenConstraint_Kd: needE<A>(a)")]
    // c2-5a's design review twins (artifacts\units\r7-c25\rv\twins.txt, labelled from SPEC; F's rulings CC-6, CC-10). The own reference
    // is instantiated wherever C's clauses are read through `C<X>`: a generic struct's conformance (gs), a requirement's premise (rq)
    // and call (rc), two parameters (m2), the formation's query (rf-n1: 8.3.2's Error for the supplied, refuted `i32 is R<i32>`).
    // Formation judges each condition by its role (6.1.4; 8.3.1's requirement precedence splits `and`, while `or` and `not` are one
    // condition): Self in a Contract argument (sa), a projection's (pa), a Tuple or Function Type (tf), a disjunction (or), a conjunction's
    // Self part only (an; an-n1 keeps its input condition's record). A premise's own supply proves its formation's input condition (rf-v1,
    // rf-n2, rf-n3, tf-n1: F's F-1 (a), x-carried-v's reading); a premise whose supply is refuted on a concrete Type is contradictory
    // evidence at the function (sa-v1, or-v1, an-v1: F-2 (a)); er-n1 loses only the record resting on its unresolved clause.
    [InlineData("guard", "gs-n1", MarkTree + "struct S<T>\n    T is MT<i64>\n    Self is MT<i32>\n    associate Tree.Child is T\n    public var tag: i32 = 0\n", "*: Self is MT<i32> | ...")]
    [InlineData("guard", "gs-n2", MarkTree + "struct S<T>\n    T is Tree\n    Self is MT<i32>\n    associate Tree.Child is T\n    public var tag: i32 = 0\n", "*: Self is MT<i32> | ...")]
    [InlineData("guard", "gs-n3", MarkTree + "contract MT3<B>: MT<B>\nstruct S<T>\n    T is MT<i64>\n    Self is MT3<i32>\n    associate Tree.Child is T\n    public var tag: i32 = 0\n", "*: Self is MT3<i32> | ...")]
    [InlineData("guard", "rq-v1", MarkTree + Put + PutS + "        U is MT<i32>\n        return 0\n", "")]
    [InlineData("guard", "rq-n1", MarkTree + Put + PutS + "        U is MT<i64>\n        return 0\n", "*: Self is MT<i32> | ...")]
    [InlineData("guard", "rq-n2", MarkTree + Put + PutS + "        U is Mark<i64>\n        return 0\n", "*: Self is MT<i32> | ...")]
    [InlineData("guard", "rc-n1", MarkTree + Put + "func probe<T, U>(t: ref/T, u: ref/U) -> i32\n    T is MT<i32>\n    U is MT<i64>\n    return t.put(u)\n", "UnprovenConstraint_Kd: return t.put(u)")]
    [InlineData("guard", "m2-v1", MarkArgs + Second + "func probe<T>(c: ref/T.(Tree).Child)\n    T is MT2<i32, i64>\n    needMark<T.(Tree).Child, i64>(c)\n", "")]
    [InlineData("guard", "m2-v2", MarkArgs + Second + "func probe<T>(c: ref/T.(Tree).Child.(Tree).Child)\n    T is MT2<i32, i64>\n    needMark<T.(Tree).Child.(Tree).Child, i64>(c)\n", "")]
    [InlineData("guard", "m2-n1", MarkArgs + Second + "func probe<T>(c: ref/T.(Tree).Child)\n    T is MT2<i32, i64>\n    needMark<T.(Tree).Child, i32>(c)\n", "UnprovenConstraint_Kd: needMark<T.(Tree).Child, i32>(c)")]
    [InlineData("guard", "m2-n2", MarkArgs + Second + "func probe<T>(c: ref/T.(Tree).Child.(Tree).Child)\n    T is MT2<i32, i64>\n    needMark<T.(Tree).Child.(Tree).Child, i32>(c)\n", "UnprovenConstraint_Kd: needMark<T.(Tree).Child.(Tree).Child, i32>(c)")]
    [InlineData("guard", "rf-n1", "contract R<A>\n    A is R<A>\nfunc probe<T>(t: ref/T)\n    T is R<i32>\n    ()\n", "InvalidConstraint_Kd: T is R<i32> | ...")]
    [InlineData("guard", "rf-v1", "contract R<A>\n    A is R<A>\nfunc probe<T, U>(t: ref/T)\n    U is R<U>\n    T is R<U>\n    ()\n", "")]
    [InlineData("guard", "rf-n2", "contract R<A>\n    A is R<A>\nfunc probe<T, U>(t: ref/T)\n    T is R<U>\n    ()\n", "")]
    [InlineData("guard", "rf-n3", "contract R2<A, B>\n    B is R2<A, B>\nfunc probe<T, Y>(t: ref/T)\n    Y is R2<i64, Y>\n    T is R2<i32, Y>\n    ()\n", "")]
    [InlineData("guard", "sa-v1", SelfArg + "struct S\n    public var tag: i32 = 0\nfunc probe<T>(t: ref/T)\n    T is C<S>\n    ()\n", "InvalidConstraint_Kd: func probe<T>(t: ref/T)")]
    [InlineData("guard", "sa-v2", SelfArg + "struct S\n    Self is D<W>\n    public var tag: i32 = 0\nstruct W\n    Self is C<S>\n    public var tag: i32 = 0\n", "")]
    [InlineData("guard", "sa-n1", SelfArg + "struct S\n    public var tag: i32 = 0\nstruct W\n    Self is C<S>\n    public var tag: i32 = 0\n", "UnsatisfiedConstraint_Kd: Self is C<S>")]
    [InlineData("guard", "pa-v1", KArgs + "contract C<A>\n    A is K<Self>\n    A.(K<Self>).X is Copy\ngroup H\n    public func twice<T, S>(x: S.(K<T>).X) -> (S.(K<T>).X, S.(K<T>).X)\n        T is C<S>\n        S is K<T>\n        return (x, x)\n", "")]
    [InlineData("guard", "pa-n1", KArgs + "contract C<A>\n    A is K<Self>\ngroup H\n    public func twice<T, S>(x: S.(K<T>).X) -> (S.(K<T>).X, S.(K<T>).X)\n        T is C<S>\n        S is K<T>\n        return (x, x)\n", "TransferRequired_Kd: return (x, x)")]
    [InlineData("guard", "pa-n2", KArgs + "contract C<A>\n    A is K<Self>\n    A.(K<Self>).X is Copy\ngroup H\n    public func twice<T, S>(x: S.(K<i32>).X) -> (S.(K<i32>).X, S.(K<i32>).X)\n        T is C<S>\n        S is K<i32>\n        return (x, x)\n", "TransferRequired_Kd: return (x, x)")]
    [InlineData("guard", "tf-v1", "contract Holder<P>\ncontract C<A>\n    A is Holder<(Self, i32)>\nfunc probe<T, S>(t: ref/T)\n    T is C<S>\n    ()\n", "")]
    [InlineData("guard", "tf-v2", "contract Holder<P>\ncontract C<A>\n    A is Holder<(Self) -> i32>\nfunc probe<T, S>(t: ref/T)\n    T is C<S>\n    ()\n", "")]
    [InlineData("guard", "tf-n1", "contract Holder<P>\ncontract C<A>\n    A is Holder<(A, i32)>\nfunc probe<T, S>(t: ref/T)\n    T is C<S>\n    ()\n", "")]
    [InlineData("guard", "or-v1", Either + "func probe<T>(t: ref/T)\n    T is C<i32>\n    ()\n", "InvalidConstraint_Kd: func probe<T>(t: ref/T)")]
    [InlineData("guard", "or-v2", Either + "func probe<T, S>(t: ref/T)\n    T is C<S>\n    ()\n", "")]
    [InlineData("guard", "an-v1", Both + "struct S\n    Self is E\n    public var tag: i32 = 0\nfunc probe<T>(t: ref/T)\n    T is C<S>\n    ()\n", "InvalidConstraint_Kd: func probe<T>(t: ref/T)")]
    [InlineData("guard", "an-v2", Both + "func probe<T, S>(t: ref/T)\n    S is E\n    T is C<S>\n    ()\n", "")]
    [InlineData("guard", "an-n1", Both + "func probe<T>(t: ref/T)\n    T is C<i32>\n    ()\n", "InvalidConstraint_Kd: func probe<T>(t: ref/T) <- T is C<i32> | InvalidConstraint_Kd: T is C<i32>")]
    [InlineData("guard", "sc-n1", "contract D<U>\ncontract C\n    i32 is D<i64>\nfunc probe<T>(t: ref/T)\n    T is C\n    ()\n", "*: i32 is D<i64> | ...")]
    [InlineData("guard", "er-n1", KArgs + "contract C<A>: K<A>\n    Self.(K<A>).X is Missing\nfunc probe<T>(t: ref/T)\n    T is C<i32>\n    ()\n", "InvalidConstraint_Kd: contract C<A>: K<A> | UnresolvedBinding_Kd: Self.(K<A>).X is Missing")]
    // B3: a Contract argument written as a completed projection is interpreted at the proof's intake, so the premise and its supply
    // meet the instantiated own reference; dev's collapsed reference read any `R<X>` and hid the gap.
    [InlineData("guard", "sr-alias-v", "contract R<A>\n    A is R<A>\ncontract HasItem\n    associate Item\nstruct Src<V>\n    Self is HasItem\n    associate HasItem.Item is V\n    public var tag: i32 = 0\nfunc probe<T, U>(t: ref/T)\n    T is R<Src<U>.(HasItem).Item>\n    ()\n", "")]
    [InlineData("guard", "rc2-alias-v", MarkTree + Put + SrcItem + "func probe<T, U>(t: ref/T, u: ref/U) -> i32\n    T is MT<i32>\n    U is MT<Src.(HasItem).Item>\n    return t.put(u)\n", "")]
    [InlineData("guard", "sr-alias-n", "contract R<A>\n    A is R<A>\n" + SrcItem + "func probe<T>(t: ref/T)\n    T is R<Src.(HasItem).Item>\n    ()\n", "InvalidConstraint_Kd: T is R<Src.(HasItem).Item> | ...")]
    [InlineData("guard", "gs2-alias-v", MarkTree + SrcItem + "struct S<T>\n    T is MT<Src.(HasItem).Item>\n    Self is MT<i32>\n    associate Tree.Child is T\n    public var tag: i32 = 0\n", "")]
    [InlineData("guard", "gs2-alias-n", MarkTree + "contract HasItem\n    associate Item\nstruct Src\n    Self is HasItem\n    associate HasItem.Item is i64\n    public var tag: i32 = 0\nstruct S<T>\n    T is MT<Src.(HasItem).Item>\n    Self is MT<i32>\n    associate Tree.Child is T\n    public var tag: i32 = 0\n", "*: Self is MT<i32> | ...")]
    // Closedness does not read a clause's Contract arguments (pre-existing; c2-5a's PLAN row for unit C2, review N3): `Q is F<U>` is a
    // reference input condition judged at the declaration, and `i32 is D<Self>` (an implementation requirement) a closed obligation.
    [InlineData("C2", "cl-v1", "contract F<W>\nstruct S\n    public var tag: i32 = 0\nstruct Q\n    Self is F<S>\n    public var tag: i32 = 0\ncontract D<U>\n    Q is F<U>\nfunc probe<T>(t: ref/T)\n    T is D<S>\n    ()\n", "", Explicit = true)]
    [InlineData("C2", "cl-n1", "contract F<W>\nstruct S\n    public var tag: i32 = 0\nstruct Q\n    Self is F<S>\n    public var tag: i32 = 0\ncontract D<U>\n    Q is F<U>\nfunc probe<T>(t: ref/T)\n    T is D<i32>\n    ()\n", "*: T is D<i32> | ...", Explicit = true)]
    [InlineData("C2", "sc-v2", "contract D<U>\ncontract C\n    i32 is D<Self>\n", "", Explicit = true)]
    public void APremiseSuppliesItsRefinementsByDemand(string unit, string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines($"{form} [{unit}]", program + "()", records);

    // R7-C2a a1's kept rows (SPEC 8.3.2, 8.3.3 Finality, 23.1.2): probe's two calls ask the rows of T in one view, so the second reads
    // what the first kept, and K's declaration state changes inside the capability epoch that kept them (the validation loop fails K
    // on its closed clause, on its ancestor or on a dependent clause's unresolved part). The loop clears the epoch and runs again, so
    // the records are those of rows walked afresh in either order of the calls: the failed Contract's own, and each function whose
    // premise names it resting on it. A guard row, not a fail-before row: the records are the same without kept rows, and no
    // program with a kept row that differs from a fresh walk is known (ContractSolver's Kept rows gives the argument).
    [Theory]
    [InlineData("closed-k2k", "contract K\n    Missing is Copy\n", "    needsK2<T>(t)\n    needsK<T>(t)\n", "InvalidConstraint_Kd: func needsK<V>(v: ref/V) | InvalidConstraint_Kd: func probe<T>(t: ref/T) | UnresolvedBinding_Kd: Missing is Copy")]
    [InlineData("closed-kk2", "contract K\n    Missing is Copy\n", "    needsK<T>(t)\n    needsK2<T>(t)\n", "InvalidConstraint_Kd: func needsK<V>(v: ref/V) | InvalidConstraint_Kd: func probe<T>(t: ref/T) | UnresolvedBinding_Kd: Missing is Copy")]
    [InlineData("ancestor", "contract Bad\n    Missing is Copy\ncontract K: Bad\n", "    needsK2<T>(t)\n    needsK<T>(t)\n", "InvalidConstraint_Kd: contract K: Bad | InvalidConstraint_Kd: func needsK<V>(v: ref/V) | InvalidConstraint_Kd: func probe<T>(t: ref/T) | UnresolvedBinding_Kd: Missing is Copy")]
    [InlineData("dependent", "contract K\n    Self.Missing is Copy\n", "    needsK2<T>(t)\n    needsK<T>(t)\n", "InvalidConstraint_Kd: func needsK<V>(v: ref/V) | InvalidConstraint_Kd: func probe<T>(t: ref/T) | UnresolvedBinding_Kd: Self.Missing is Copy")]
    [InlineData("valid-k2k", "contract K\n", "    needsK2<T>(t)\n    needsK<T>(t)\n", "")]
    [InlineData("valid-kk2", "contract K\n", "    needsK<T>(t)\n    needsK2<T>(t)\n", "")]
    public void ADeclarationStateChangedInsideAnEpochGivesTheRecordsOfEitherCallOrder(string form, string contract, string calls, string records)
        => CompilationTestHelper.AssertRecordsAtLines(form, contract + "    func k(self) -> i32\ncontract K2\n    func k2(self) -> i32\nfunc needsK<V>(v: ref/V)\n    V is K\n    ()\nfunc needsK2<V>(v: ref/V)\n    V is K2\n    ()\nfunc probe<T>(t: ref/T)\n    T is K\n    T is K2\n" + calls + "()", records);

    // R7-E2 E2-5a (probe c1, labelled from SPEC 8.3 and 23.1.2 first): refinement is acyclic, so the cycle is one failed requirement per
    // member the check fails; a Contract invalid only over its own cycle or a failed refinement rests on them and adds no record of its
    // own (the slot's first failure until R7-E5, D-924). A regression row for the record E2-5's first prototype published at `contract A`;
    // the twin without the cycle is valid.
    [Theory]
    [InlineData("c1", "contract A: B\ncontract B: A", "CyclicBinding_Kd: contract A: B | InvalidConstraint_Kd: contract B: A")]
    [InlineData("c1t", "contract A: B\ncontract B", "")]
    public void AContractOverItsOwnCycleAddsNoRecord(string form, string program, string records)
        => CompilationTestHelper.AssertBindingRecordsAtLines(form, program + "\npublic func main() => ()", records);

    // R7-C2a: Enumerate's walk demands nothing, so a clause whose normalization proves a conformance condition over a taller projection
    // (Box<Self.(K).Y>.(C).X under `Self.(K).Y is M`) cannot continue it, and under a parameter identity the view's preparation
    // normalizes each fact once. Each check ends without an internal failure; an invalid twin is rejected.
    [Theory(Timeout = 60000)]
    [InlineData("p27c-condrec", "contract M: K, D\n", "func probe<T>(value: ref/T.(K).Y)\n    T is M\n", false)]
    [InlineData("p27cn-condrec", "contract M: K\n", "func probe<T>(value: ref/T.(K).Y)\n    T is M\n", true)]
    [InlineData("p31-prepdemand", "contract M: K, D\n", "func probe<T, W>(value: ref/T.(K).Y)\n    T is M\n    W is T\n", false)]
    [InlineData("p31n-prepdemand", "contract M: K\n", "func probe<T, W>(value: ref/T.(K).Y)\n    T is M\n    W is T\n", true)]
    public async Task ACheckOverAConditionalClauseEnds(string form, string contract, string probe, bool invalid)
    {
        var records = await Task.Run(() => CompilationTestHelper.RecordsAtLines(Conditions + contract + Clauses + probe + "    needs<T.(K).Y>(value)\n()"), TestContext.Current.CancellationToken);
        Assert.True(!records.Any(static x => x.StartsWith("InternalInvariant_Kd", StringComparison.Ordinal)) && (!invalid || records.Length != 0), $"{form}: [{string.Join(" | ", records)}]");
    }

    // SPEC 8.3.3 Finality: neither the order of the premises nor the order of the declarations changes a result. Each row is one program
    // in two orders whose view is prepared under a parameter identity; normalizing a fact there asks a capability (o-capability: the
    // built-in Addable's Output asks whether its receiver is a PrimitiveInteger) or a conformance condition (o-condition, o-binding:
    // Box's `when`), each answered from the written facts alone. o-binding's clause stands in a struct region, since a body parses
    // a clause whose subject begins with a declared Type as an expression (PLAN G507); its condition and its consumer are each
    // answered through a refinement (E: D, Sub: Tag). The memo's clear after the preparation is p33-prepmemo's.
    [Theory]
    [InlineData(
        "o-capability",
        "contract K\n    associate Y is PrimitiveInteger\ncontract M: K\n    Self.(K).Y.(Addable<Self.(K).Y>).Output is Copy\ngroup H\n    public func twice<T, U>(value: U.(K).Y.(Addable<U.(K).Y>).Output) -> (U.(K).Y.(Addable<U.(K).Y>).Output, U.(K).Y.(Addable<U.(K).Y>).Output)\n        T is M\n        T is U\n        return (value, value)\n",
        "group H\n    public func twice<T, U>(value: U.(K).Y.(Addable<U.(K).Y>).Output) -> (U.(K).Y.(Addable<U.(K).Y>).Output, U.(K).Y.(Addable<U.(K).Y>).Output)\n        T is U\n        T is M\n        return (value, value)\ncontract M: K\n    Self.(K).Y.(Addable<Self.(K).Y>).Output is Copy\ncontract K\n    associate Y is PrimitiveInteger\n")]
    [InlineData(
        "o-condition",
        Conditions + "contract M: K, D\n" + Clauses + "func probe<T, W>(value: ref/T.(K).Y)\n    T is M\n    W is T\n    needs<T.(K).Y>(value)\n",
        "func probe<T, W>(value: ref/T.(K).Y)\n    W is T\n    T is M\n    needs<T.(K).Y>(value)\ncontract M: K, D\n" + Clauses + Conditions)]
    [InlineData(
        "o-binding",
        "contract Tag\ncontract Sub: Tag\ncontract D\ncontract E: D\ncontract C\n    associate X\nstruct Box<A>\n    Self is C when A is D\n    associate X is A\nstruct Need<V>\n    V is Tag\n    public var item: i32\nstruct W<T, U>\n    U is E\n    T is U\n    Box<T>.(C).X is Sub\n    public var need: Need<U>\n",
        "struct W<T, U>\n    Box<T>.(C).X is Sub\n    T is U\n    U is E\n    public var need: Need<U>\nstruct Need<V>\n    V is Tag\n    public var item: i32\nstruct Box<A>\n    Self is C when A is D\n    associate X is A\ncontract C\n    associate X\ncontract E: D\ncontract D\ncontract Sub: Tag\ncontract Tag\n")]
    public void TheOrderOfPremisesNeverChangesAResult(string form, string first, string second)
        => Assert.True(CompilationTestHelper.RecordsAtLines(first + "()").SequenceEqual(CompilationTestHelper.RecordsAtLines(second + "()")), $"{form}: [{string.Join(" | ", CompilationTestHelper.RecordsAtLines(first + "()"))}] and [{string.Join(" | ", CompilationTestHelper.RecordsAtLines(second + "()"))}]");

    // Enumerate's tables are owned by a request and pooled: a warm rebind whose calls prove a conformance of a projection three links
    // up the premise's chain, and of one under a parameter identity (the view's preparation), allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRebindThatEnumeratesAllocatesNothing()
    {
        var c = Bind(Copies + "func needs<V>(v: ref/V)\n    V is CopyTree\n    ()\n" +
            "func probe<T>(value: ref/T.(Tree).Child.(Tree).Child.(Tree).Child)\n    T is CopyTree\n    needs<T.(Tree).Child.(Tree).Child.(Tree).Child>(value)\n" +
            "func both<T, U>(value: ref/U.(Tree).Child.(Tree).Child)\n    T is CopyTree\n    T is U\n    needs<U.(Tree).Child.(Tree).Child>(value)\n");
        Assert.Empty(CompilationTestHelper.BindingErrors(c));
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }

    // PLAN G1407 (F's G1406-3 N3): the walk splits a conjunction clause at depth into its conjuncts over the pooled `conjuncts` run, so
    // a warm rebind whose calls prove a conjunct two and three links up the premise's chain allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRebindThatSplitsAConjunctionAtDepthAllocatesNothing()
    {
        var c = Bind(PositiveConjuncts + NeedMarker + "func probe<T>(t: ref/T) -> i32\n    T is PA2\n    let v = t.deep()\n    return needMark(v)\n" +
            "func probe3<T>(t: ref/T) -> i32\n    T is PA2\n    let v = t.deep3()\n    return needMark(v)\n");
        Assert.Empty(CompilationTestHelper.BindingErrors(c));
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }

    // R7-C2b b1: PrimitiveInteger's parents are rows of the walk, not tables: a warm rebind that copies IntTree's Value three links up
    // (b1v2) and proves it Owned there allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRebindOfASuppliedIntegerAllocatesNothing()
    {
        var c = Bind(IntTrees + "group H\n    public func twice<T>(value: T.(Tree).Child.(Tree).Child.(Tree).Value) -> (T.(Tree).Child.(Tree).Child.(Tree).Value, T.(Tree).Child.(Tree).Child.(Tree).Value)\n        T is IntTree\n        return (value, value)\n" +
            "func needsOwned<V>(v: ref/V)\n    V is Owned\n    ()\nfunc probe<T>(value: ref/T.(Tree).Child.(Tree).Child.(Tree).Value)\n    T is IntTree\n    needsOwned<T.(Tree).Child.(Tree).Child.(Tree).Value>(value)\n");
        Assert.Empty(CompilationTestHelper.BindingErrors(c));
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }

    // PremiseList's shape: functions of many premises under a parameter identity, whose requests read every fact of their view through
    // Reading; a warm rebind allocates nothing once the tables have their size.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRebindOfManyPremisesUnderAnIdentityAllocatesNothing()
    {
        var source = new System.Text.StringBuilder(Copies);
        for (var f = 0; f < 4; f++)
        {
            source.Append($"func list{f}<T0, T1, T2, T3, T4, T5, U>(v: T0.(Tree).Child, u: U) -> (T0.(Tree).Child, T0.(Tree).Child)\n");
            for (var i = 0; i < 6; i++)
            {
                source.Append($"    T{i} is CopyTree\n");
            }

            source.Append("    T0 is U\n    return (v, v)\n");
        }

        var c = Bind(source.ToString());
        Assert.Empty(CompilationTestHelper.BindingErrors(c));
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }

    private static Compilation Bind(string source)
    {
        var c = MirBuildTest.Compile(source + "public func main() => ()\n");
        Assert.True(c.Binding.Result.IsComplete, string.Join(", ", c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}")));
        return c;
    }

    private static BindingSymbol Probe(Compilation c) => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe").SymbolOf()!;
}
