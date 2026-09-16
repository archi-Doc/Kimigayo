// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ConstraintContextBindingTest
{
    [Theory]
    [InlineData("#Unknown", "Invalid.S")]
    [InlineData("#Layout(\"C\")", "Invalid.S")]
    [InlineData("#Unknown", "Box<Invalid.S>")]
    [InlineData("#Unknown", "(i32, Invalid.S)")]
    [InlineData("#Unknown", "unsafe/Invalid.S")]
    public void InvalidTypeContextCannotProveIdentity(string attribute, string argument)
    {
        var c = MinimalEmissionTest.Analyze(attribute + "\ngroup Invalid\n    public struct S\nstruct Box<T>\ngroup Consumer\n    func query<T>()\n        T is " + argument + "\n        ()");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single();
        var proposition = ((IsKoto)function.TypeConstraints[0]).BoundConstraint!;
        Assert.Equal(ConstraintProof.Error, c.Binding.Prove(proposition, function, [proposition.RequiredType], c.Kotonoha.RootKoto));
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(ConstraintProof.Error, c.Binding.Prove(proposition, function));
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        var restoredFunction = Query(restored);
        Assert.Equal(ConstraintProof.Error, restored.Binding.Prove(((IsKoto)restoredFunction.TypeConstraints[0]).BoundConstraint!, restoredFunction));
    }

    [Theory]
    [InlineData("Invalid.Marker")]
    [InlineData("not Invalid.Marker")]
    [InlineData("i32 or Invalid.Marker")]
    public void InvalidContractContextCannotSupplyAnAssumption(string requirement)
    {
        var c = MinimalEmissionTest.Analyze("#Unknown\ngroup Invalid\n    public contract Marker\ngroup Consumer\n    func query<T>()\n        T is " + requirement + "\n        ()");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        var function = Query(c);
        Assert.Equal(ConstraintProof.Error, c.Binding.Prove(((IsKoto)function.TypeConstraints[0]).BoundConstraint!, function));
    }

    [Fact]
    public void RemovingInvalidMarkerRestoresProofs()
    {
        var c = MinimalEmissionTest.Analyze("#Unknown\ngroup Invalid\n    public struct S\ngroup Consumer\n    func query<T>()\n        T is Invalid.S\n        ()");
        var group = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Invalid");
        var function = Query(c);
        var proposition = ((IsKoto)function.TypeConstraints[0]).BoundConstraint!;
        Assert.Equal(ConstraintProof.Error, c.Binding.Prove(proposition, function));
        Assert.True(group.RemoveAttribute(Assert.IsType<AttributeKoto>(group.AttributeChain)));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Same(proposition, ((IsKoto)function.TypeConstraints[0]).BoundConstraint);
        Assert.Equal(ConstraintProof.Proven, c.Binding.Prove(proposition, function));
        Assert.Equal(ConstraintProof.Proven, c.Binding.Prove(proposition, function, [proposition.RequiredType], c.Kotonoha.RootKoto));
    }

    [Fact]
    public void UnrelatedInvalidGroupDoesNotPoisonValidProofs()
    {
        var c = MinimalEmissionTest.Analyze("#Unknown\ngroup Invalid\ngroup Valid\n    public struct S\ngroup Consumer\n    func query<T>()\n        T is Valid.S\n        ()");
        Assert.False(c.Binding.Result.IsComplete);
        var function = Query(c);
        var proposition = ((IsKoto)function.TypeConstraints[0]).BoundConstraint!;
        Assert.Equal(ConstraintProof.Proven, c.Binding.Prove(proposition, function));
        Assert.Equal(ConstraintProof.Proven, c.Binding.Prove(proposition, function, [proposition.RequiredType], c.Kotonoha.RootKoto));
    }

    [Fact]
    public void WarmContextProofsAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("group Valid\n    public struct S\ngroup Consumer\n    func query<T>()\n        T is Valid.S\n        ()");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Constraint context Binding failed.");
            }
        }));
    }

    private static FunctionKoto Query(Compilation c)
        => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Consumer").Members.OfType<FunctionKoto>().Single();

    private static Compilation Reload(Compilation c)
    {
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha), ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        return restored;
    }
}
