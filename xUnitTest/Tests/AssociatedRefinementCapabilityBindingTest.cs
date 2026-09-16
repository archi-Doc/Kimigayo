// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class AssociatedRefinementCapabilityBindingTest
{
    [Theory]
    [InlineData("Copy", false)]
    [InlineData("Copy", true)]
    [InlineData("Owned", false)]
    [InlineData("Owned", true)]
    public void AssociatedContractPremisesEntailIntrinsicAncestors(string capability, bool indirect)
    {
        var c = MinimalEmissionTest.Analyze("public contract Base: " + capability + "\n" + (indirect ? "public contract Trait: Base\n" : string.Empty) + "public contract Elements\n    associate Item is " + (indirect ? "Trait" : "Base") + "\ngroup G\n    func inspect<T>(value: T.Item)\n        T is Elements\n        ()");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.True(c.Binding.Result.IsComplete);
        var function = Function(c);
        var type = function.Parameters[0].Type.BoundType!;
        Assert.Equal(ConstraintProof.Proven, capability == "Copy" ? c.Binding.ProveCopy(type, function) : c.Binding.ProveOwned(type, function));
        Assert.True(c.Bind().IsComplete);
        Assert.True(Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothOuterAndRefinedContractPrerequisitesRemainRequired(bool outer)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        var condition = "\n    Source is Origin";
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", "public contract Origin\npublic struct Source\npublic contract Trait: Copy" + (outer ? string.Empty : condition) + "\npublic contract Elements" + (outer ? condition : string.Empty) + "\n    associate Item is Trait\ngroup G\n    func inspect<T>(value: T.Item)\n        T is Elements\n        ()"));
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        var function = Function(c);
        Assert.Equal(ConstraintProof.Unknown, c.Binding.ProveCopy(function.Parameters[0].Type.BoundType!, function));
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", "public struct Source\n    Self is Origin"));
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(function.Parameters[0].Type.BoundType!, function));
    }

    [Theory]
    [InlineData("and", ConstraintProof.Proven)]
    [InlineData("or", ConstraintProof.Unknown)]
    public void RefinementDoesNotEliminateDisjunctions(string operation, ConstraintProof expected)
    {
        var c = MinimalEmissionTest.Analyze("public contract Trait: Copy\npublic contract Other\npublic contract Elements\n    associate Item is Trait " + operation + " Other\ngroup G\n    func inspect<T>(value: T.Item)\n        T is Elements\n        ()");
        Assert.True(c.Binding.Result.IsComplete);
        var function = Function(c);
        Assert.Equal(expected, c.Binding.ProveCopy(function.Parameters[0].Type.BoundType!, function));
    }

    [Theory]
    [InlineData("Copy")]
    [InlineData("Owned")]
    public void GenericCallsConsumeAssociatedRefinementEvidence(string capability)
    {
        var c = MinimalEmissionTest.Analyze("public contract Trait: " + capability + "\npublic contract Elements\n    associate Item is Trait\ngroup G\n    func take<U>()\n        U is " + capability + "\n        ()\n    func inspect<T>(value: T.Item)\n        T is Elements\n        take<T.Item>()");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.True(c.Binding.Result.IsComplete);
        var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "G").Members.OfType<FunctionKoto>().Single(x => x.Name == "inspect");
        Assert.NotNull(Assert.IsType<InvocationKoto>(function.Body!.Items.Single()).BoundCall);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void RecursiveAssociatedRequirementsAreExpandedOnlyAsReferenced(int depth)
    {
        var type = "T.Item" + string.Concat(Enumerable.Repeat(".Next", depth));
        var c = MinimalEmissionTest.Analyze("public contract Trait: Copy\n    associate Next is Trait\npublic contract Elements\n    associate Item is Trait\ngroup G\n    func inspect<T>(value: " + type + ")\n        T is Elements\n        ()");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.True(c.Binding.Result.IsComplete);
        var function = Function(c);
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(function.Parameters[0].Type.BoundType!, function));
    }

    [Fact]
    public void WarmAssociatedRefinementProofsAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("public contract Trait: Copy\npublic contract Elements\n    associate Item is Trait\ngroup G\n    func inspect<T>(value: T.Item)\n        T is Elements\n        ()");
        var function = Function(c);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
            Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(function.Parameters[0].Type.BoundType!, function));
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete || c.Binding.ProveCopy(function.Parameters[0].Type.BoundType!, function) != ConstraintProof.Proven)
            {
                throw new InvalidOperationException("Associated refinement proof failed.");
            }
        }));
    }

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

    private static FunctionKoto Function(Compilation c)
        => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "G").Members.OfType<FunctionKoto>().Single();
}
