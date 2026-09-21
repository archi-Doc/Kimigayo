// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ProvisionalContractPremiseBindingTest
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PendingRefinementDoesNotSupplyEvidence(bool indirect, bool independent)
    {
        var c = Parse("public contract Origin\npublic struct Source {}\npublic contract Marker: Copy\n    Source is Origin\n" + (indirect ? "public contract Child: Marker\n" : string.Empty) + "group G\n    func inspect<T>(value?: T)\n        T is " + (indirect ? "Child" : "Marker") + (independent ? "\n        T is Copy" : string.Empty) + "\n        ()");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        var function = Function(c);
        Assert.Equal(independent ? ConstraintProof.Proven : ConstraintProof.Unknown, c.Binding.ProveCopy(function.Parameters[0].Type.BoundType!, function));
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", "public struct Source {}\n    Self is Origin"));
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(function.Parameters[0].Type.BoundType!, function));
        Assert.True(Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASecondValidRefinementKeepsItsEvidence(bool reverse)
    {
        var constraints = reverse ? "T is Ready\n        T is Pending" : "T is Pending\n        T is Ready";
        var c = Parse("public contract Origin\npublic struct Source {}\npublic contract Pending: Copy\n    Source is Origin\npublic contract Ready: Copy\ngroup G\n    func inspect<T>(value?: T)\n        " + constraints + "\n        ()");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(Function(c).Parameters[0].Type.BoundType!, Function(c)));
    }

    [Fact]
    public void PendingPositiveEvidenceDoesNotContradictANegativeInput()
    {
        var c = Parse("public contract Origin\npublic struct Source {}\npublic contract Marker: Copy\n    Source is Origin\ngroup G\n    func inspect<T>(value?: T)\n        T is Marker\n        T is not Copy\n        ()");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        Assert.Equal(ConstraintProof.Refuted, c.Binding.ProveCopy(Function(c).Parameters[0].Type.BoundType!, Function(c)));
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", "public struct Source {}\n    Self is Origin"));
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(ConstraintProof.Error, c.Binding.ProveCopy(Function(c).Parameters[0].Type.BoundType!, Function(c)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CallsRequireCurrentlyAvailableDerivedEvidence(bool independent)
    {
        var c = Parse("public contract Origin\npublic struct Source {}\npublic contract Marker: Copy\n    Source is Origin\ngroup G\n    func take<T>()\n        T is Copy\n        ()\n    func inspect<T>(value?: T)\n        T is Marker" + (independent ? "\n        T is Copy" : string.Empty) + "\n        take<T>()");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        var function = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "G").Members.OfType<FunctionKoto>().Single(x => x.Name == "inspect");
        var invocation = Assert.IsType<InvocationKoto>(function.Body!.Items.Single());
        Assert.Equal(independent, invocation.BoundCall is not null);
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", "public struct Source {}\n    Self is Origin"));
        Assert.True(c.Bind().IsComplete);
        Assert.NotNull(invocation.BoundCall);
    }

    [Theory]
    [InlineData("Copy")]
    [InlineData("not Copy")]
    [InlineData("Copy and Owned")]
    public void AssociatedRequirementsRetainTheirDefiningContract(string requirement)
    {
        var c = Parse("public contract Origin\npublic struct Source {}\npublic contract Marker\n    Source is Origin\n    associate Element is " + requirement + "\ngroup G\n    func inspect<T>(value?: T.Element)\n        T is Marker\n        ()");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        var function = Function(c);
        Assert.Equal(ConstraintProof.Unknown, c.Binding.ProveCopy(function.Parameters[0].Type.BoundType!, function));
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", "public struct Source {}\n    Self is Origin"));
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(requirement == "not Copy" ? ConstraintProof.Refuted : ConstraintProof.Proven, c.Binding.ProveCopy(function.Parameters[0].Type.BoundType!, function));
    }

    [Fact]
    public void WarmProvenanceRevalidationAllocatesNothing()
    {
        var c = Parse("public contract Origin\npublic struct Source {}\npublic contract Marker: Copy\n    Source is Origin\ngroup G\n    func inspect<T>(value?: T)\n        T is Marker\n        ()");
        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (c.Binding.Bind(BindingMode.Provisional).InvalidCount != 0)
            {
                throw new InvalidOperationException("Pending premise became invalid.");
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

    private static Compilation Parse(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", source));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        return c;
    }

    private static FunctionKoto Function(Compilation c)
        => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "G").Members.OfType<FunctionKoto>().Single();
}
