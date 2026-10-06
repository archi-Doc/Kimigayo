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
        var closure = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsAnonymous).BoundClosure!.EnvironmentType!;
        var calls = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Where(x => x.BoundCall?.Target.Name == "identity").Select(x => x.BoundCall!).ToArray();
        var first = c.Binding.InstantiateStorageType(closure, calls[0])!;
        var same = c.Binding.InstantiateStorageType(closure, calls[1])!;
        var different = c.Binding.InstantiateStorageType(closure, calls[2])!;
        Assert.Same(first, same);
        Assert.NotSame(first, different);
        Assert.False(Binding.FitsType(first, different));
        Assert.False(Binding.FitsType(different, first));
        Assert.Empty(first.Components);
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false, 0, 0, "")]
    [InlineData(true, 1, 1, "zero\n")]
    public void ZeroSizedEnvironmentsAllocateOnlyForRequiredDestruction(bool destructor, int count, int bytes, string output)
    {
        var source = "struct Zero\n" + (destructor ? "    drop => Console.writeLine(\"zero\")\n" : string.Empty) +
            "func keep<T>(value: T) -> () -> i32\n    T is Owned\n    let action = func [value@move] () -> i32 => 7\n    return action@move\nlet result = keep(Zero.init())\nrequire result() == 7 else => $abort(\"zero\")";
        NativeAllocationAudit.WriteFixture("GenericClosureIdentityZero" + destructor, source, count, count, bytes, output);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmLengthOnlyContextsAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Identity + "let empty: [0 of i32] = []\n_ = identity(empty)\n_ = identity([1, 2])");
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}
