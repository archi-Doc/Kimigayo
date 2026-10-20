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

    // SPEC 8.3.3: each recursive Contract of the S5 proposal's 2.1, through the premise supply of `T is C` (SPEC 8.3.2): Tree; CopyTree,
    // which refines Tree and constrains its inherited Child; A and B, which supply each other's conformance; Rec, whose refinement
    // constrains Node's unconstrained Child (Node is not recursive); Seq, whose Next conforms to another bound reference of its declaration;
    // A with a Constraint on a two-step projection of Self (B is not recursive). A disjunction supplies nothing, so Bush is not recursive.
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
        var c = Bind(Forest + Mutual + "func probe<T, U>(value: T.(Forest).Root.(Tree).Child, other: U.(A).X)\n    T is Forest\n    U is A\n    ()\n");
        var probe = Probe(c);
        var (first, second) = (TyCtxt.Signature(probe, null, 0, out _)!, TyCtxt.Signature(probe, null, 1, out _)!);
        Assert.True(c.Types.RecursiveBase(probe, first) && c.Types.RecursiveBase(probe, second));
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            _ = c.Types.RecursiveBase(probe, first);
            _ = c.Types.RecursiveBase(probe, second);
        }));
    }

    private static Compilation Bind(string source)
    {
        var c = MirBuildTest.Compile(source + "public func main() => ()\n");
        Assert.True(c.Binding.Result.IsComplete, string.Join(", ", c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}")));
        return c;
    }

    private static BindingSymbol Probe(Compilation c) => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe").SymbolOf()!;
}
