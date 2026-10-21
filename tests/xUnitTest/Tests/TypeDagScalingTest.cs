// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>PLAN G140, DECISIONS BU-18: an interned Type is a DAG, so the parts of a Type may be shared, and every walk over a Type's parts
/// decides each distinct part once per query. Checking a program whose Types share parts is then linear in the distinct parts: each
/// scaling row sizes its shape so that a walk that re-visits shared parts meets 2^26 or more paths (hours) while the DAG walk takes
/// milliseconds; a regression shows as a hang, which the runner's process timeout reports, as <see cref="InferenceSlotScalingTest"/>'s
/// does.</summary>
public class TypeDagScalingTest
{
    // Binding's walks: the instance Types of a Tuple chain (TyCtxt.Apply, from the checked set), the capability proofs of HIR publication
    // over inferred shared Tuples, closed and generic, through the MIR, its analyses and the checked set.
    [Theory]
    [InlineData("chain", 28)]
    [InlineData("chainValues", 28)]
    [InlineData("lets", 28)]
    [InlineData("genericLets", 26)]
    public void BindingWalksAreLinearInDistinctParts(string shape, int size)
    {
        var c = MirBuildTest.Compile(Source(shape, size));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.Empty(program.Records);
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Empty(set.Records);
        if (shape.StartsWith("chain", StringComparison.Ordinal))
        {
            Assert.Equal(size + 1, Instances(set, "f"));
        }
    }

    // SPEC 8.2: two closures of one generic function in a shared Tuple close in each instance's substitution; the Tuple's shared parts
    // are closed once and stay shared, and every part equals the part instantiated alone.
    [Fact]
    public void ClosuresOfASharedTupleCloseInEachInstance()
    {
        const string Source = "func make<T>(x: T) -> ()\n    T is Copy\n    let f = func [x] () => x\n    let g = func [x] () => x\n    let p = ((f, g), (f, g))\n" +
            "    let q = (p, p)\n    let r = q.0.0.0()\n    let s = q.1.1.1()\n    return\npublic func main() -> ()\n    make(1)\n    make(true)\n    return\n";
        var c = MirBuildTest.Compile(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var environments = nodes.OfType<FunctionKoto>().Where(static x => x.IsAnonymous).Select(static x => x.ClosureOf()!.EnvironmentType!).ToArray();
        var calls = nodes.OfType<InvocationKoto>().Where(static x => x.CallOf()?.Target.Name == "make").Select(x => c.Types.SubstOf(x.CallOf()!)!).ToArray();
        Assert.Equal(2, environments.Length);
        Assert.Equal(2, calls.Length);
        var pair = c.Types.Intern(BoundTypeKind.Tuple, null, SemanticsKind.Owner, environments);
        var half = c.Types.Intern(BoundTypeKind.Tuple, null, SemanticsKind.Owner, [pair, pair]);
        var whole = c.Types.Intern(BoundTypeKind.Tuple, null, SemanticsKind.Owner, [half, half]);
        var closed = calls.Select(x => c.Types.Apply(whole, x)!).ToArray();
        for (var i = 0; i < calls.Length; i++)
        {
            Assert.Same(closed[i].Components[0], closed[i].Components[1]);
            var parts = closed[i].Components[0].Components[0].Components;
            Assert.Equal(environments.Select(x => c.Types.Apply(x, calls[i])!), parts);
            Assert.All(parts, x => Assert.Same(calls[i], x.ClosureContext));
        }

        Assert.NotSame(closed[0], closed[1]);
        var set = new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1);
        Assert.Empty(set.Records);
    }

    // DECISIONS BU-18 (R5's condition for the closing table): an edit leaves no closing answer behind, so a closure whose signature an
    // edit changed is instantiated again with the new signature in the next pass. The edited tree is rebound, not built (its replaced
    // declaration keeps the donor's module).
    [Fact]
    public void AnEditedClosureSignatureIsInstantiatedAgain()
    {
        const string Before = "func make<T>(x: T) -> ()\n    T is Copy\n    let f = func [x] () -> T => x\n    let r = f()\n    return\n";
        const string After = "func make<T>(x: T) -> ()\n    T is Copy\n    let f = func [x] () -> (T, T) => (x, x)\n    let r = f()\n    return\n";
        const string Main = "public func main() -> ()\n    make(1)\n    return\n";
        var c = MirBuildTest.Compile(Before + Main);
        Assert.Empty(new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1).Records);
        Assert.Equal("i32", Binding.DiagnosticTypeName(Result(c)));
        var original = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "make");
        var replacement = KotoTree.Walk(ParseTestHelper.ParseSuccess(After + Main).RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "make");
        Assert.True(KotoHelper.Replace(original.Parent!, original, replacement));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal("(i32, i32)", Binding.DiagnosticTypeName(Result(c)));

        // The closure's result Type in the instance make<i32>.
        static BoundType Result(Compilation c)
        {
            var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
            var environment = nodes.OfType<FunctionKoto>().Single(static x => x.IsAnonymous).ClosureOf()!.EnvironmentType!;
            var call = nodes.OfType<InvocationKoto>().Single(static x => x.CallOf()?.Target.Name == "make").CallOf()!;
            return c.Binding.ClosureSignature(c.Types.Apply(environment, c.Types.SubstOf(call)!)!)!.Components[1];
        }
    }

    // SPEC 8.13: a part shared below a length-generic Tuple is substituted with the length argument; the call checks and the checked
    // set holds the instance without a record.
    [Fact]
    public void ASharedLengthGenericPartIsSubstitutedWithItsLength()
    {
        const string Source = "func f<length N>(a: [N of i32]) -> i32\n    let t = (a, a)\n    let u = (t, t)\n    return u.0.0[0]\npublic func main() -> ()\n" +
            "    let a: [3 of i32] = [1, 2, 3]\n    require f(a) == 1 else => $abort(\"x\")\n    return\n";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c, null));
        var set = new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1);
        Assert.Empty(set.Records);
        Assert.Equal(1, Instances(set, "f"));
    }

    // SPEC 8.3: a constraint on a shared Tuple whose part names a failed declaration is an Error of that constraint, as for a tree.
    [Fact]
    public void AFailedPartOfASharedTupleFailsItsConstraint()
    {
        const string Source = "struct Bad\n    public var x: Missing\nfunc need<T>(x: ref/T) -> ()\n    T is Copy\n    return\nfunc f(v: ref/((Bad, Bad), (Bad, Bad))) -> ()\n" +
            "    need(v)\n    return\npublic func main() -> ()\n    return\n";
        Assert.Equal(["UnresolvedBinding_Kd", "UnprovenConstraint_Kd"], DiagnosticCorpus.Check(Source).Diagnostics.Select(static x => x.Code));
    }

    // A warm rebind of a program whose Types share parts allocates nothing: the answer tables keep their capacity.
    [Fact]
    [Trait("Purpose", "Allocation")]
    public void WarmBindOfSharedTypesAllocatesNothing()
    {
        var c = MirBuildTest.Compile(Source("chainValues", 16));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var complete = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => complete &= c.Bind().IsComplete));
        Assert.True(complete, MinimalEmissionTest.Describe(c, null));
    }

    // The source of a shape: a chain of generic functions, each calling the next with the Tuple of two of its argument (PLAN G153 (2)),
    // passing a value or nothing; a body whose locals pair the previous local, over i32 or a Copy parameter.
    internal static string Source(string shape, int size)
    {
        var text = new System.Text.StringBuilder();
        switch (shape)
        {
            case "chain":
                for (var i = 0; i < size; i++)
                {
                    text.Append($"func f{i}<T>(x: Option<raw/T>) -> ()\n    f{i + 1}<(T, T)>(.None)\n    return\n");
                }

                return text.Append($"func f{size}<T>(x: Option<raw/T>) -> ()\n    return\npublic func main() -> ()\n    f0<i32>(.None)\n    return\n").ToString();
            case "chainValues":
                for (var i = 0; i < size; i++)
                {
                    text.Append($"func f{i}<T>(x: T) -> ()\n    T is Copy\n    f{i + 1}<(T, T)>((x, x))\n    return\n");
                }

                return text.Append($"func f{size}<T>(x: T) -> ()\n    return\npublic func main() -> ()\n    f0<i32>(1)\n    return\n").ToString();
            default:
                text.Append(shape == "genericLets" ? "func f<T>(t: T) -> ()\n    T is Copy\n" : "func f(t: i32) -> ()\n").Append("    let a0 = (t, t)\n");
                for (var i = 1; i < size; i++)
                {
                    text.Append($"    let a{i} = (a{i - 1}, a{i - 1})\n");
                }

                return text.Append("    return\npublic func main() -> ()\n    f(1)\n    return\n").ToString();
        }
    }

    // The body instances of the functions whose name starts with a prefix.
    private static int Instances(CheckedSet set, string prefix)
    {
        var count = 0;
        foreach (var item in set.Items)
        {
            count += item.Key is { Kind: MonoKind.Body, Body.Def.Decl.Name: { } name } && name.StartsWith(prefix, StringComparison.Ordinal) ? 1 : 0;
        }

        return count;
    }
}
