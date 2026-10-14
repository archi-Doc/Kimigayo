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

    // Defaults that call a generic function, a length-generic one, one with an Origin, a requirement and, in a generic struct's method, a
    // function over the struct's Type parameter.
    private const string ComposeSource = "func copy<T>(x: T) -> T\n    T is Copy\n    return x\nfunc f<T>(x: T, y: T = copy(x)) -> T\n    T is Copy\n    return y\n" +
        "func count<length N>(values: ref/[N of i32]) -> isize => values.length\nfunc size<length N>(values: ref/[N of i32], k: isize = count(values)) -> isize => k\n" +
        "func first<T>(values: ref/[2 of T] during a) -> ref/T during a => values[0]@ref/T\n" +
        "func pick<T>(values: ref/[2 of T] during s, r: ref/T during s = first(values)) -> ref/T during s => r\n" +
        "func same<T>(x: T, equal: bool = x == x) -> bool\n    T is Equatable\n    return equal\n" +
        "struct Box<T>\n    T is Copy\n    public var value: T\n    public init(value: T) => self.value = value\n    public func get(self: ref/Self) -> T => self.value\n" +
        "    public func orElse(self: ref/Self, other: T = self.get()) -> T => other\n" +
        "let fixed: [3 of i32] = [1, 2, 3]\nlet two: [2 of i32] = [4, 5]\nlet box = Box<i32>.init(6)\nlet p = pick(two@ref)\n" +
        "require f(7) == 7 and f(true) and size(fixed@ref) == 3 and p@follow == 4 and same(8) and same(true) and box.orElse() == 6 else => $abort(\"compose\")\n";

    // SPEC 8.2, 8.10: the callee's declared parameter and result Types under a call's substitution are the call's instantiation (SPEC 8.4.3:
    // a requirement's under its conforming Self and the bound Contract reference it dispatches through, which SubstOf records as its
    // declaring Type), a closure in such a Type has its instance's signature, and a function item's substitution applies as its context does.
    [Theory]
    [MemberData(nameof(HirColumnsTest.Corpus), MemberType = typeof(HirColumnsTest))]
    public void ApplyIsTheInstantiation(string name)
    {
        var (c, module) = SemanticSnapshotTest.Bind(name);
        var problems = new List<string>();
        void Check(Koto node, BoundType? declared, Subst subst, CallPlan context)
        {
            var applied = declared is null ? null : c.Types.Apply(declared, subst);
            var instance = declared is null ? null : context.Kind != CalleeKind.Requirement ? c.Binding.InstantiateStorageType(declared, context)
                : c.Binding.InstantiateStorageType(declared, context.Target, subst.Declaring, context.TypeArguments, context.LengthArguments, context.Origins, context.InputOrigins, context.ConformingType);
            if (!ReferenceEquals(applied, instance) ||
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

    // SPEC 8.2: a closure an instance closed is in that instance's substitution and has its signature; a closure no instance closed is
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

    // SPEC 8.13.3: a substitution holds one length entry per slot whichever producer forms it, so a direct call, a function item and a
    // synthesized call of one instance are one substitution.
    [Fact]
    public void ASubstitutionHasOneLengthEntryPerSlot()
    {
        var c = MinimalEmissionTest.Analyze("func make<T>() -> i32 => 1\nlet f = make<i32>\nrequire make<i32>() == f() else => $abort(\"make\")\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var call = nodes.OfType<InvocationKoto>().Select(static x => x.CallOf()).Single(static x => x is { Kind: CalleeKind.Function, Target.Name: "make" })!;
        var item = nodes.Select(static x => x.TypeOf()).First(static x => x is { Kind: BoundTypeKind.FunctionItem })!;
        var subst = c.Types.SubstOf(call)!;
        Assert.Equal(1, subst.Lengths.Length);
        Assert.Same(subst, c.Types.SubstOfItem(item));
        Assert.Same(subst, c.Types.Synthesized(call.Target, null, call.TypeArguments));
    }

    // SPEC 3.4.3, 13.5.5: selection follows a reference or object handle as a Deref and a pair layer, the original s/T or an application
    // s/U, as a PairLayer of the binder in scope, never a raw pointer or an owned value. SPEC 23.1: a Type is named as diagnostics spell it.
    [Fact]
    public void LayersAreReferencesHandlesAndPairLayers()
    {
        var c = MinimalEmissionTest.Analyze("func f<s/T>(value: s/T, count: s/i32) -> s/T\n    s is owner or uniq\n    T is Copy\n    _ = count\n    return value\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var types = c.Types;
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "f");
        var pair = function.Parameters[0].Type.TypeOf()!;
        var application = function.Parameters[1].Type.TypeOf()!;
        var binders = new List<PairBinder>();
        PairBinder.Collect(function, binders);
        Assert.Equal(HirPlaceKind.Deref, TyCtxt.Layer(types.Reference(SemanticsKind.Uniq, BoundType.I32), out var below, out var binder));
        Assert.Same(BoundType.I32, below);
        Assert.Null(binder);
        Assert.Equal(HirPlaceKind.Deref, TyCtxt.Layer(types.Reference(SemanticsKind.ObjRef, BoundType.I32), out _, out _));
        Assert.Equal(HirPlaceKind.Value, TyCtxt.Layer(types.Pointer(BoundType.I32), out below, out binder));
        Assert.Equal((types.Pointer(BoundType.I32), null), (below, binder));
        Assert.Equal(HirPlaceKind.Value, TyCtxt.Layer(types.Tuple([BoundType.I32, BoundType.Boolean]), out _, out _));
        Assert.Equal(HirPlaceKind.PairLayer, TyCtxt.Layer(pair, out below, out binder));
        Assert.Same(Assert.Single(binders).Target, binder);
        Assert.Equal(HirPlaceKind.Value, TyCtxt.Layer(below, out _, out binder));
        Assert.Null(binder);
        Assert.Equal((BoundTypeKind.SemanticsApplication, HirPlaceKind.PairLayer), (application.Kind, TyCtxt.Layer(application, out below, out binder)));
        Assert.Equal((BoundType.I32, binders[0].Target), (below, binder));
        Assert.Equal(["uniq/i32", "s/T", "s/i32"], [TyCtxt.Name(types.Reference(SemanticsKind.Uniq, BoundType.I32)), TyCtxt.Name(pair), TyCtxt.Name(application)]);
    }

    // Plan 5.6.2: a call in an omitted default composed with the substitution of the call omitting it is the call Binding forwards into that
    // instance (InstantiateForwardedCall), over the corpus and over generic, length, Origin, member and requirement calls in defaults.
    [Fact]
    public void ComposeIsTheForwardedInstantiation()
    {
        var problems = new List<string>();
        var corpus = 0;
        foreach (var name in HirColumnsTest.Corpus.Select(static x => x.Data))
        {
            var (c, module) = SemanticSnapshotTest.Bind(name);
            corpus += ComposeProblems(c, module, problems);
        }

        var direct = MinimalEmissionTest.Analyze(ComposeSource);
        Assert.True(direct.Binding.Result.IsComplete, MinimalEmissionTest.Describe(direct, null));
        var calls = ComposeProblems(direct, direct.Kotonoha, problems);
        Assert.True(corpus == 6 && calls == 7 && problems.Count == 0, $"{corpus} corpus and {calls} direct nested calls; {problems.Count} differ:\n{string.Join('\n', problems.Take(40))}");

        // copy, count, first, same (a requirement, so the forwarding resolves its witness) and get bind a Type, a length, an Origin, Self and
        // a declaring Type of the instance.
        var composed = NestedCalls(direct.Kotonoha).Select(x => (x.Inner.Target.Name, Subst: direct.Types.Compose(direct.Types.SubstOf(x.Inner)!, direct.Types.SubstOf(x.Outer)!)!)).ToArray();
        Assert.Equal(["i32", "bool"], composed.Where(static x => x.Name == "copy").Select(static x => x.Subst.Types[0]!.Name));
        Assert.Equal(3, composed.Single(static x => x.Name == "count").Subst.Lengths[0]!.Value);
        Assert.Contains(composed.Single(static x => x.Name == "first").Subst.Origins.ToArray().Concat(composed.Single(static x => x.Name == "first").Subst.Inputs.ToArray()), static x => x is not null);
        Assert.Equal(["i32", "bool"], composed.Where(static x => x.Subst.Target.Declaration is FunctionKoto { IsRequirement: true }).Select(static x => x.Subst.Self!.Name));
        Assert.Equal("Box", composed.Single(static x => x.Name == "get").Subst.Declaring!.Name);
        Assert.Same(BoundType.I32, composed.Single(static x => x.Name == "get").Subst.Declaring!.Components[0]);
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

    // A warm rebind composes each call in a default to the same substitution and allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmComposeIsTheSameAndAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(ComposeSource);
        var pairs = NestedCalls(c.Kotonoha).ToArray();
        var composed = pairs.Select(x => c.Types.Compose(c.Types.SubstOf(x.Inner)!, c.Types.SubstOf(x.Outer)!)).ToArray();
        var changed = 0;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            for (var i = 0; i < pairs.Length; i++)
            {
                changed += ReferenceEquals(c.Types.Compose(c.Types.SubstOf(pairs[i].Inner)!, c.Types.SubstOf(pairs[i].Outer)!), composed[i]) ? 0 : 1;
            }
        }));
        Assert.Equal(0, changed);
        Assert.Contains(composed, static x => x is { IsIdentity: false, Types.Length: > 0 });
    }

    // Each call in a default a call omits, with that call.
    private static IEnumerable<(CallPlan Inner, CallPlan Outer)> NestedCalls(Kotonoha module)
    {
        foreach (var node in KotoTree.Walk(module.RootKoto))
        {
            for (var i = 0; CallPlan.Of(node, i) is { } outer; i++)
            {
                foreach (var omitted in outer.DefaultArguments.ToArray())
                {
                    foreach (var nested in KotoTree.Walk(DefaultDecl.Of(omitted).Expression))
                    {
                        for (var k = 0; CallPlan.Of(nested, k) is { } inner; k++)
                        {
                            if (inner.Kind != CalleeKind.Value)
                            {
                                yield return (inner, outer);
                            }
                        }
                    }
                }
            }
        }
    }

    // The forwarded call has the composed substitution and instantiates each declared Type of the callee as Apply does; a requirement call,
    // which the forwarding resolves to its witness, composes as applying the two substitutions in turn.
    private static int ComposeProblems(Compilation c, Kotonoha module, List<string> problems)
    {
        var count = 0;
        foreach (var (inner, outer) in NestedCalls(module))
        {
            count++;
            var composed = c.Types.Compose(c.Types.SubstOf(inner)!, c.Types.SubstOf(outer)!);
            var forwarded = inner.Kind == CalleeKind.Requirement ? null : c.Binding.InstantiateForwardedCall(inner, outer);
            var where = $"{module.Compilation.Kotonoha.Name} {outer.Target.Name} > {inner.Target.Name}";
            if (inner.Kind != CalleeKind.Requirement && (forwarded is null || !ReferenceEquals(c.Types.SubstOf(forwarded), composed)))
            {
                problems.Add($"{where}: {composed is null} {forwarded is null}");
                continue;
            }

            foreach (var declared in ((FunctionKoto)inner.Target.Declaration).Parameters.Select(static x => x.Type.TypeOf()).Append(inner.Target.Type))
            {
                var expected = declared is null ? null : forwarded is not null ? c.Binding.InstantiateStorageType(declared, forwarded)
                    : c.Types.Apply(declared, c.Types.SubstOf(inner)!) is { } once ? c.Types.Apply(once, c.Types.SubstOf(outer)!) : null;
                if (composed is null || !ReferenceEquals(declared is null ? null : c.Types.Apply(declared, composed), expected))
                {
                    problems.Add($"{where} {declared}: {expected}");
                }
            }
        }

        return count;
    }
}
