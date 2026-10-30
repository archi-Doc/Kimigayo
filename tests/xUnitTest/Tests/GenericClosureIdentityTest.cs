// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class GenericClosureIdentityTest
{
    private const string Identity = "func identity<length N>(value: [N of i32]) -> [N of i32]\n    let action = func [] (inner: [N of i32]) -> [N of i32] => inner\n    return action(value)\n";

    [Fact]
    public void EmptyEnvironmentsKeepLengthOnlyAbiSubstitutions()
    {
        const string Source = Identity + "let empty: [0 of i32] = []\nlet first = identity(empty)\nlet second = identity([2, 4])\nrequire first.length == 0 and second[1] == 4 else => $abort(\"length\")";
        ScalarEmissionTest.EmitFixture("GenericClosureIdentityLength", Source, string.Empty);
    }

    [Fact]
    public void EmptyEnvironmentsKeepContainerOnlySubstitutions()
    {
        const string Source = "struct Factory<T>\n    public func identity(value: T) -> T\n        let action = func [] (inner: T) -> T => inner@move\n        return action(value@move)\nrequire Factory<i32>.identity(7) == 7 else => $abort(\"container\")\nConsole.writeLine(Factory<string>.identity(\"owned\"))";
        ScalarEmissionTest.EmitFixture("GenericClosureIdentityContainer", Source, "owned\n");
    }

    [Fact]
    public void LengthContextIdentityIsCanonicalAndDistinct()
    {
        var c = MinimalEmissionTest.Analyze(Identity + "_ = identity([1, 2])\n_ = identity([3, 4])\n_ = identity([5])");
        var closure = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsAnonymous).ClosureOf()!.EnvironmentType!;
        var calls = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Where(x => x.CallOf()?.Target.Name == "identity").Select(x => x.CallOf()!).ToArray();
        var first = c.Binding.InstantiateStorageType(closure, calls[0])!;
        var same = c.Binding.InstantiateStorageType(closure, calls[1])!;
        var different = c.Binding.InstantiateStorageType(closure, calls[2])!;
        Assert.Same(first, same);
        Assert.NotSame(first, different);
        Assert.False(Binding.FitsType(first, different));
        Assert.False(Binding.FitsType(different, first));
        Assert.Empty(first.Components);
    }

    // DECISIONS R5-M36, S2-S4 X10: two closures of equal result Type in one generic instance share the instance's context and keep their
    // own closed Types, which key their instances, so each instance runs each closure's own body.
    [Fact]
    public void TwoClosuresOfOneInstanceKeepTheirOwnBodies()
    {
        const string Source = "func pair<T>(value: T) -> i32\n    T is Copy\n    let first = func [] (x: T) -> i32 => 1\n    let second = func [] (x: T) -> i32 => 2\n" +
            "    return first(value) * 10 + second(value)\nrequire pair(3) == 12 and pair(true) == 12 else => $abort(\"two closures\")";
        var c = MinimalEmissionTest.Analyze(Source);
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var call = nodes.OfType<InvocationKoto>().First(static x => x.CallOf()?.Target.Name == "pair").CallOf()!;
        var closures = nodes.OfType<FunctionKoto>().Where(static x => x.IsAnonymous).Select(x => c.Binding.InstantiateStorageType(x.ClosureOf()!.EnvironmentType!, call)!).ToArray();
        Assert.Equal(2, closures.Length);
        Assert.Same(closures[0].ClosureContext, closures[1].ClosureContext);
        Assert.Same(c.Types.SubstOf(call), closures[0].ClosureContext);
        Assert.NotSame(closures[0], closures[1]);
        ScalarEmissionTest.EmitFixture("GenericClosureIdentityTwo", Source, string.Empty);
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "zero\n")]
    public void ZeroSizedEnvironmentsAllocateNothing(bool destructor, string output)
    {
        var source = "struct Zero\n" + (destructor ? "    drop => Console.writeLine(\"zero\")\n" : string.Empty) +
            "func keep<T>(value: T) -> () -> i32\n    T is Owned\n    let action = func [value@move] () -> i32 => 7\n    return action@move\nlet result = keep(Zero.init())\nrequire result() == 7 else => $abort(\"zero\")";

        // A zero-sized environment is stored in the Function value's word, also one that needs destruction (PD19, IMPL 21.1.3; DECISIONS
        // R5-M104).
        NativeAllocationAudit.WriteFixture("GenericClosureIdentityZero" + destructor, source, 0, 0, 0, output);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmLengthOnlyContextsAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Identity + "let empty: [0 of i32] = []\n_ = identity(empty)\n_ = identity([1, 2])");
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}
