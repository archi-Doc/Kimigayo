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
    [InlineData("C2", "value2", Copies + "group H\n    public func twice<T>(value: T.(Tree).Child.(Tree).Child) -> (T.(Tree).Child.(Tree).Child, T.(Tree).Child.(Tree).Child)\n        T is CopyTree\n        return (value, value)\n", "", Explicit = true)]
    [InlineData("C2", "value4", Copies + "group H\n    public func twice<T>(value: T.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child) -> (T.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child, T.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child)\n        T is CopyTree\n        return (value, value)\n", "", Explicit = true)]
    [InlineData("C2", "value8", Copies + "group H\n    public func twice<T>(value: T.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child) -> (T.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child, T.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child.(Tree).Child)\n        T is CopyTree\n        return (value, value)\n", "", Explicit = true)]
    [InlineData("guard", "value2n", Copies + "group H\n    public func twice<T>(value: T.(Tree).Child.(Tree).Child) -> (T.(Tree).Child.(Tree).Child, T.(Tree).Child.(Tree).Child)\n        T is Tree\n        return (value, value)\n", "TransferRequired_Kd: return (value, value)")]
    [InlineData("guard", "value2hint", Copies + "group H\n    public func twice<T>(hint: ref/T.(Tree).Child.(CopyTree).Child, value: T.(Tree).Child.(Tree).Child) -> (T.(Tree).Child.(Tree).Child, T.(Tree).Child.(Tree).Child)\n        T is CopyTree\n        return (value, value)\n", "")]
    [InlineData("C2", "nonrec", Outers + "func inspect<T>(value: T.(Outer).Inner.(Holder).Item) -> i32\n    T is CopyOuter\n    let a = value\n    let b = value\n    return 0\n", "", Explicit = true)]
    [InlineData("guard", "nonrecn", Outers + "func inspect<T>(value: T.(Outer).Inner.(Holder).Item) -> i32\n    T is Outer\n    let a = value\n    let b = value\n    return 0\n", "TransferRequired_Kd: let a = value | TransferRequired_Kd: let b = value")]

    // SPEC 15.6.4: a widened effect conflicts as an unknown call does, even where the caller's premises would classify every effect it replaces.
    [InlineData("guard", "s5two", Walks + "        Trees.two(self.node)\n        return .Ok(())\n", "IncompatibleContractImplementation_Kd: Self is Sink")]
    [InlineData("guard", "s5one", Walks + "        Trees.one(self.node)\n        return .Ok(())\n", "IncompatibleContractImplementation_Kd: Self is Sink")]

    // SPEC 8.11.3, 8.3.2: the callee's own premises prove every destroyed Type Copy, whatever the projection's spelling.
    [InlineData("C2", "a4h", Copies + "group H\n    public func h<T>(t: T.(Tree).Child.(Tree).Child)\n        T is CopyTree\n        ()\ncontract Visit\n    func visit(self: ref/Self)\n        effect confined\nstruct W<U>\n    U is CopyTree\n    U is Copy\n    U.(Tree).Child is U\n    Self is Visit\n    let node: U\n    public func visit(self: ref/Self)\n        H.h<U>(self.node)\n", "", Explicit = true)]
    public void CopyReachesAProjectionWhateverItsSpelling(string unit, string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines($"{form} [{unit}]", program + "()", records);

    private static Compilation Bind(string source)
    {
        var c = MirBuildTest.Compile(source + "public func main() => ()\n");
        Assert.True(c.Binding.Result.IsComplete, string.Join(", ", c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}")));
        return c;
    }

    private static BindingSymbol Probe(Compilation c) => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe").SymbolOf()!;
}
