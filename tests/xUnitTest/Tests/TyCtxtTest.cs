// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>The Type context (<c>Compilation.Types</c>, docs/dev/COMPILER_ARCHITECTURE.md, R2b): a substitution is interned, so a warm
/// lookup returns the same object and allocates nothing, and a call's, closure's or function item's substitution applied to a declared
/// Type is the instantiation Binding makes for it, over the Kimi library and every milestone program.</summary>
public sealed class TyCtxtTest
{
    // A generic function whose closure an instance closes, called with two lengths.
    private const string ClosureSource = "func identity<length N>(value: [N of i32]) -> [N of i32]\n    let action = func [] (inner: [N of i32]) -> [N of i32] => inner\n" +
        "    return action(value)\n_ = identity([1, 2])\n_ = identity([5])\n";

    // SPEC 8.3, 8.6: the callee's declared parameter and result Types under a call's substitution are the call's instantiation, a closure
    // in such a Type has its instance's signature, and a function item's substitution applies as its context does.
    [Theory]
    [MemberData(nameof(HirColumnsTest.Corpus), MemberType = typeof(HirColumnsTest))]
    public void ApplyIsTheInstantiation(string name)
    {
        var (c, module) = SemanticSnapshotTest.Bind(name);
        var problems = new List<string>();
        void Check(Koto node, BoundType? declared, Subst subst, CallPlan context)
        {
            var applied = declared is null ? null : c.Types.Apply(declared, subst);
            if (!ReferenceEquals(applied, declared is null ? null : c.Binding.InstantiateStorageType(declared, context)) ||
                (applied is { Kind: BoundTypeKind.Closure, Symbol.Declaration: FunctionKoto function } && function.ClosureOf() is { } closure &&
                    !ReferenceEquals(c.Binding.ClosureSignature(applied), c.Types.Apply(closure.Signature, c.Types.SubstOfClosure(applied)!))))
            {
                problems.Add($"{node.CodeContext.SourceDocument?.Path}:{node.Span.Start} {node.Akind} {declared}: {applied}");
            }
        }

        var calls = 0;
        foreach (var node in KotoTree.Walk(module.RootKoto))
        {
            for (var i = 0; CallPlan.Of(node, i) is { } call; i++)
            {
                if (c.Types.SubstOf(call) is { } subst && call.Target.Declaration is FunctionKoto callee)
                {
                    calls++;
                    foreach (var parameter in callee.Parameters)
                    {
                        Check(node, parameter.Type.TypeOf(), subst, call);
                    }

                    Check(node, call.Target.Type, subst, call);
                }
            }

            if (node.TypeOf() is { Kind: BoundTypeKind.FunctionItem, Symbol.Declaration: FunctionKoto function } item && c.Binding.FunctionItemContext(item, false) is { } context)
            {
                foreach (var parameter in function.Parameters)
                {
                    Check(node, parameter.Type.TypeOf(), c.Types.SubstOfItem(item)!, context);
                }
            }
        }

        Assert.True(calls > 0 && problems.Count == 0, $"{calls} calls; {problems.Count} differ:\n{string.Join('\n', problems.Take(40))}");
    }

    // SPEC 8.3: a closure an instance closed is in that instance's substitution and has its signature; a closure no instance closed is
    // in the identity of the function enclosing it, which keeps every Type.
    [Fact]
    public void AClosureIsInItsInstancesSubstitution()
    {
        var c = MinimalEmissionTest.Analyze(ClosureSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var function = nodes.OfType<FunctionKoto>().Single(static x => x.Name == "identity");
        var closure = nodes.OfType<FunctionKoto>().Single(static x => x.IsAnonymous).ClosureOf()!;
        var calls = nodes.OfType<InvocationKoto>().Where(static x => x.CallOf()?.Target.Name == "identity").Select(static x => x.CallOf()!).ToArray();
        var closed = calls.Select(x => c.Binding.InstantiateStorageType(closure.EnvironmentType!, x)!).ToArray();
        Assert.All(closed, static x => Assert.NotNull(x.ClosureContext));
        Assert.Equal(calls.Select(c.Types.SubstOf), closed.Select(c.Types.SubstOfClosure));
        Assert.NotSame(c.Types.SubstOf(calls[0]), c.Types.SubstOf(calls[1]));
        Assert.All(closed, x => Assert.Same(c.Binding.ClosureSignature(x), c.Types.Apply(closure.Signature, c.Types.SubstOfClosure(x)!)));
        var identity = c.Types.SubstOfClosure(closure.EnvironmentType!)!;
        Assert.Same(c.Types.Identity(function), identity);
        Assert.Same(closure.Signature, c.Types.Apply(closure.Signature, identity));
    }

    // SPEC 3.4.1, 13.5.5.1: selection follows a reference or object handle as a Deref and a pair layer as a PairLayer, never a raw pointer
    // or an owned value.
    [Fact]
    public void LayersAreReferencesHandlesAndPairLayers()
    {
        var c = MinimalEmissionTest.Analyze("func f<s/T>(value: s/T) -> s/T\n    s is owner or uniq\n    T is Copy\n    return value\n");
        var types = c.Types;
        var pair = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<JumpKoto>().Single().Expression!.TypeOf()!;
        Assert.Equal(HirPlaceKind.Deref, TyCtxt.Layer(types.Reference(SemanticsKind.Uniq, BoundType.I32), out var below));
        Assert.Same(BoundType.I32, below);
        Assert.Equal(HirPlaceKind.Deref, TyCtxt.Layer(types.Reference(SemanticsKind.ObjRef, BoundType.I32), out _));
        Assert.Equal(HirPlaceKind.Value, TyCtxt.Layer(types.Pointer(BoundType.I32), out below));
        Assert.Equal(types.Pointer(BoundType.I32), below);
        Assert.Equal(HirPlaceKind.Value, TyCtxt.Layer(types.Tuple([BoundType.I32, BoundType.Boolean]), out _));
        Assert.Equal(HirPlaceKind.PairLayer, TyCtxt.Layer(pair, out below));
        Assert.Equal(HirPlaceKind.Value, TyCtxt.Layer(below, out _));
    }

    // A warm rebind keeps every call's substitution the same object and looking it up allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmSubstitutionsAreTheSameAndAllocateNothing()
    {
        var c = SemanticSnapshotTest.Bind("milestone/Milestone26").Compilation;
        var calls = KotoTree.Walk(c.Kotonoha.RootKoto).Concat(KotoTree.Walk(c.Library.Kotonoha.RootKoto))
            .SelectMany(static x => Enumerable.Range(0, 8).Select(i => (Node: x, Ordinal: i)).TakeWhile(static y => CallPlan.Of(y.Node, y.Ordinal) is not null))
            .Where(x => c.Types.SubstOf(CallPlan.Of(x.Node, x.Ordinal)!) is not null).ToArray();
        var substs = calls.Select(x => c.Types.SubstOf(CallPlan.Of(x.Node, x.Ordinal)!)).ToArray();
        var changed = 0;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            for (var i = 0; i < calls.Length; i++)
            {
                changed += ReferenceEquals(c.Types.SubstOf(CallPlan.Of(calls[i].Node, calls[i].Ordinal)!), substs[i]) ? 0 : 1;
            }
        }));
        Assert.Equal(0, changed);
        Assert.Contains(substs, static x => x is { IsIdentity: false, Types.Length: > 0 });
    }
}
