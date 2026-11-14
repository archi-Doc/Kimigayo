// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>The recursive-base view (SPEC 8.3.3, 15.6.4; docs/dev/DECISIONS.md R4-M87): <c>TyCtxt.RecursiveBase</c>, whether one of a
/// projection's bases is proven under a definition's premises to conform to a recursive Contract, observed on the Type of a probe
/// function's parameter under the probe's premises.</summary>
public sealed class RecursiveContractTest
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
    [InlineData("contract Seq<E>\n    associate Next is Seq<Option<E>>\n", "T is Seq<i32>", "T.(Seq<i32>).Next", true)]
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
        var type = TyCtxt.Signature(probe, null, 0, out _)!;
        Assert.Equal(BoundTypeKind.AssociatedProjection, type.Kind);
        Assert.Equal(expected, c.Types.RecursiveBase(probe, type));
    }

    // A Type parameter is not a projection: its own conformance to a recursive Contract gives it no recursive base.
    [Fact]
    public void ATypeParameterHasNoRecursiveBase()
    {
        var c = Bind(Tree + "func probe<T>(value: T)\n    T is Tree\n    ()\n");
        var probe = Probe(c);
        Assert.False(c.Types.RecursiveBase(probe, TyCtxt.Signature(probe, null, 0, out _)!));
    }

    // Immutable after Bind and pooled: a warm rebind, which revokes the pass's recursive Contracts, and the queries that find them again
    // allocate nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRebindAndItsQueriesAllocateNothing()
    {
        var c = Bind(Forest + Mutual + "contract Seq<E>\n    associate Next is Seq<Option<E>>\n" +
            "func probe<T, U, V>(value: T.(Forest).Root.(Tree).Child, other: U.(A).X, next: V.(Seq<i32>).Next)\n    T is Forest\n    U is A\n" +
            "    V is Seq<i32>\n    ()\n");
        var probe = Probe(c);
        var types = Enumerable.Range(0, 3).Select(i => TyCtxt.Signature(probe, null, i, out _)!).ToArray();
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
    [InlineData("C2c2", "d09y-leafproj", Marks + "contract Holder\n    associate Item\nstruct Leaf\n    public var v: i32\nfunc f<T>(x: ref/Leaf)\n    T is Holder\n    T.(Holder).Item is Leaf\n    T.(Holder).Item is Mark\n    needMark<Leaf>(x)\n", "InvalidConstraint_Kd: T.(Holder).Item is Leaf | InvalidConstraint_Kd: T.(Holder).Item is Mark | InvalidConstraint_Kd: func f<T>(x: ref/Leaf)")]
    [InlineData("C2c2", "d09y-noquery", Marks + "contract Holder\n    associate Item\nstruct Leaf\n    public var v: i32\nfunc f<T>(x: ref/Leaf)\n    T is Holder\n    T.(Holder).Item is Leaf\n    T.(Holder).Item is Mark\n    ()\n", "InvalidConstraint_Kd: T.(Holder).Item is Leaf | InvalidConstraint_Kd: T.(Holder).Item is Mark | InvalidConstraint_Kd: func f<T>(x: ref/Leaf)")]
    [InlineData("C2c2", "d09-option", Marks + "func f<T, U>(x: ref/Option<U>)\n    T is Option<U>\n    T is Mark\n    needMark<Option<U>>(x)\n", "UnprovenConstraint_Kd: needMark<Option<U>>(x)")]
    [InlineData("C2c2", "d09d-optioninf", Marks + "func f<T, U>(y: ref/T)\n    T is Option<U>\n    T is Mark\n    needMark(y)\n", "UnprovenConstraint_Kd: needMark(y)")]
    // SPEC 8.2.3: `T is Copy` is evidence for the identical `Option<U>`, whose own Copy path is Unknown (not Refuted): valid. The
    // implementation's limit (Binding.TypeIdentity.Keys' ClassKey): a premise is not yet read as evidence for a constructed member whose
    // conformance is Unknown, so it is rejected, as on base, until R7-C2c2 c2-4 (the Middle's R2-C1, its N7 with r2-c2 and r2-c7).
    [InlineData("c2-4", "d09c-optioncopy", "func f<T, U>(x: Option<U>) -> (Option<U>, Option<U>)\n    T is Option<U>\n    T is Copy\n    return (x, x)\n", "", Explicit = true)]
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
    [InlineData("C2b", "b1-notcopy-d2", IntTrees + "func probe<T>(value: T.(Tree).Child.(Tree).Value) -> i32\n    T is IntTree\n    T.(Tree).Child.(Tree).Value is not Copy\n    return 0\n", "InvalidConstraint_Kd: func probe<T>(value: T.(Tree).Child.(Tree).Value) -> i32 | InvalidConstraint_Kd: T.(Tree).Child.(Tree).Value is not Copy")]
    [InlineData("guard", "b1-r22-formation", "struct Pair<X>\n    X is Copy\n    public var first: X\nfunc g<T, U>(u: ref/U)\n    T is PrimitiveInteger\n    U is Pair<T>\n    ()\n", "")]
    [InlineData("guard", "b1n1-marker", "contract Tree\n    associate Child is Tree\n    associate Value\ncontract Marker\ncontract IntTree: Tree\n    Self.(Tree).Child is IntTree\n    Self.(Tree).Value is Marker\n" + IntTwice, "TransferRequired_Kd: return (value, value)")]
    [InlineData("guard", "b1n2-sealed", IntTrees + "func needsSealed<V>(v: ref/V)\n    V is Sealed\n    ()\nfunc probe<T>(value: ref/T.(Tree).Child.(Tree).Value)\n    T is IntTree\n    needsSealed<T.(Tree).Child.(Tree).Value>(value)\n", "UnprovenConstraint_Kd: needsSealed<T.(Tree).Child.(Tree).Value>(value)")]
    [InlineData("guard", "b1n3-notcopy", "group H\n    public func twice<T>(value: T) -> (T, T)\n        T is PrimitiveInteger\n        T is not Copy\n        return (value, value)\n", "InvalidConstraint_Kd: public func twice<T>(value: T) -> (T, T)")]
    [InlineData("guard", "b1n4-noparent", "contract Tree\n    associate Child is Tree\ncontract PTree: Tree\n    Self.(Tree).Child is PTree\ngroup H\n    public func twice<T>(value: T.(Tree).Child.(Tree).Child) -> (T.(Tree).Child.(Tree).Child, T.(Tree).Child.(Tree).Child)\n        T is PTree\n        return (value, value)\n", "TransferRequired_Kd: return (value, value)")]
    [InlineData("guard", "b1-r2-failed", IntTrees + "    bool is PrimitiveInteger\n" + IntTwice, "UnsatisfiedConstraint_Kd: contract IntTree: Tree | UnprovenConstraint_Kd: Self.(Tree).Child is IntTree | UnprovenConstraint_Kd: Self.(Tree).Value is PrimitiveInteger | UnsatisfiedConstraint_Kd: bool is PrimitiveInteger | UnprovenConstraint_Kd: public func twice<T>(value: T.(Tree).Child.(Tree).Value) -> (T.(Tree).Child.(Tree).Value, T.(Tree).Child.(Tree).Value)")]
    [InlineData("guard", "b1-r12-userchild", "contract IntP: PrimitiveInteger\nstruct S\n    Self is IntP\n    public var tag: i32 = 0\n", "UnsatisfiedConstraint_Kd: Self is IntP")]
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
