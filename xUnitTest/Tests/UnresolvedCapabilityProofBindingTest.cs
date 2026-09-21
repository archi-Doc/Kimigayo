// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class UnresolvedCapabilityProofBindingTest
{
    [Theory]
    [InlineData("Target", true)]
    [InlineData("[2 of Target]", true)]
    [InlineData("ref{static}/Target", true)]
    [InlineData("() -> Target", false)]
    public void PendingTypeConditionsCannotSupplyCapabilityProofs(string type, bool copy)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", "public contract Origin\npublic struct Source {}\npublic struct Target {}\n    Source is Origin\n    Self is Copy\ngroup G\n    func inspect(value?: " + type + ") => ()"));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "G").Members.OfType<FunctionKoto>().Single();
        var bound = function.Parameters[0].Type.BoundType!;
        Assert.Equal(ConstraintProof.Unknown, c.Binding.ProveCopy(bound, function));
        Assert.Equal(ConstraintProof.Unknown, c.Binding.ProveOwned(bound, function));
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", "public struct Source {}\n    Self is Origin"));
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(copy ? ConstraintProof.Proven : ConstraintProof.Refuted, c.Binding.ProveCopy(bound, function));
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveOwned(bound, function));
        Assert.True(Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("Target")]
    [InlineData("[2 of Target]")]
    [InlineData("ref{static}/Target")]
    [InlineData("() -> Target")]
    public void FinalUnmetConditionsProduceErrorInsteadOfCapabilityEvidence(string type)
    {
        var c = Parse(type);
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        var function = Function(c);
        var bound = function.Parameters[0].Type.BoundType!;
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(ConstraintProof.Error, c.Binding.ProveCopy(bound, function));
        Assert.Equal(ConstraintProof.Error, c.Binding.ProveOwned(bound, function));
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        Assert.Equal(ConstraintProof.Unknown, c.Binding.ProveCopy(bound, function));
    }

    [Fact]
    public void SymbolicIdentityCannotBypassPendingConcretePrerequisites()
    {
        var c = Parse("T", "<T>", "\n        T is Target\n        ()");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        var function = Function(c);
        var bound = function.Parameters[0].Type.BoundType!;
        Assert.Equal(ConstraintProof.Unknown, c.Binding.ProveCopy(bound, function));
        Assert.Equal(ConstraintProof.Unknown, c.Binding.ProveOwned(bound, function));
        Assert.Equal(ConstraintProof.Unknown, c.Binding.Prove(((IsKoto)function.TypeConstraints[0]).BoundConstraint!, function));
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", "public struct Source {}\n    Self is Origin"));
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(bound, function));
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveOwned(bound, function));
    }

    [Theory]
    [InlineData("(Target, Bad)")]
    [InlineData("(Bad, Target)")]
    [InlineData("(Target) -> Bad")]
    public void ErrorStillAbsorbsUnknownOperands(string type)
    {
        var c = Parse(type);
        c.Kotonoha.AddSource(new SourceDocument("Bad.kimi", "public struct Bad {}\n    string is Copy"));
        Assert.True(c.Binding.Bind(BindingMode.Provisional).InvalidCount > 0);
        var function = Function(c);
        var bound = function.Parameters[0].Type.BoundType!;
        Assert.Equal(ConstraintProof.Error, c.Binding.ProveCopy(bound, function));
        Assert.Equal(ConstraintProof.Error, c.Binding.ProveOwned(bound, function));
    }

    [Theory]
    [InlineData("i32", ConstraintProof.Proven)]
    [InlineData("ref{static}/i32", ConstraintProof.Proven)]
    [InlineData("() -> i32", ConstraintProof.Refuted)]
    public void UnrelatedResolvedOperandsRetainTheirProofs(string type, ConstraintProof copy)
    {
        var c = Parse(type);
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        var function = Function(c);
        var bound = function.Parameters[0].Type.BoundType!;
        Assert.Equal(copy, c.Binding.ProveCopy(bound, function));
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveOwned(bound, function));
    }

    [Fact]
    public void WarmPendingCapabilityQueriesAllocateNothing()
    {
        var c = Parse("[2 of Target]");
        var provisional = c.Binding.Bind(BindingMode.Provisional);
        Assert.Equal(0, provisional.InvalidCount);
        var function = Function(c);
        var bound = function.Parameters[0].Type.BoundType!;
        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(ConstraintProof.Unknown, c.Binding.ProveCopy(bound, function));
            Assert.Equal(ConstraintProof.Unknown, c.Binding.ProveOwned(bound, function));
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (c.Binding.ProveCopy(bound, function) != ConstraintProof.Unknown || c.Binding.ProveOwned(bound, function) != ConstraintProof.Unknown)
            {
                throw new InvalidOperationException("Pending capability proof became definite.");
            }
        }));
    }

    private static Compilation Parse(string type, string generics = "", string body = " => ()")
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", "public contract Origin\npublic struct Source {}\npublic struct Target {}\n    Source is Origin\n    Self is Copy\ngroup G\n    func inspect" + generics + "(value: " + type + ")" + body));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        return c;
    }

    private static FunctionKoto Function(Compilation c)
        => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "G").Members.OfType<FunctionKoto>().Single();

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
