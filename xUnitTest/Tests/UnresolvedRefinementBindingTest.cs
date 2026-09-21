// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class UnresolvedRefinementBindingTest
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MissingParentIsPendingUntilSourceIsAppended(bool qualified, bool indirect)
    {
        var name = indirect ? "Leaf" : "Child";
        var c = Parse((qualified ? "public group Api\n" : string.Empty) + "public contract Child: " + (qualified ? "Api.Future" : "Future") + "\n" + (indirect ? "public contract Leaf: Child\n" : string.Empty) + "public struct Target\n    Self is " + name + "\ngroup G\n    func inspect<T>(value: T)\n        T is " + name + "\n        ()");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        var contract = Container(c, name);
        Assert.Equal(BindingState.Unresolved, contract.BindingState);
        var function = Container(c, "G").Members.OfType<FunctionKoto>().Single();
        Assert.Equal(ConstraintProof.Unknown, c.Binding.Prove(Assert.IsType<IsKoto>(function.TypeConstraints[0]).BoundConstraint!, function));
        Assert.False(c.Binding.GetConformanceDefinition(Container(c, "Target").BoundType!, contract.BoundSymbol!)!.IsVerified);
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", qualified ? "public group Api\n    public contract Future" : "public contract Future"));
        Assert.True(c.Bind().IsComplete, string.Join("; ", c.Binding.Issues.Select(x => x.ToString())));
        Assert.True(c.Binding.GetConformanceDefinition(Container(c, "Target").BoundType!, contract.BoundSymbol!)!.IsVerified);
        Assert.True(Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("i32")]
    [InlineData("Api")]
    [InlineData("Target")]
    [InlineData("Future<i32>")]
    public void AvailableNonContractOrGenericParentsRemainInvalid(string parent)
    {
        var c = Parse("public group Api\npublic struct Target\npublic contract Future\npublic contract Child: " + parent);
        Assert.True(c.Binding.Bind(BindingMode.Provisional).InvalidCount > 0);
        Assert.Equal(BindingState.Invalid, Container(c, "Child").BindingState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingParentsDoNotHideKnownRefinementCycles(bool reverse)
    {
        var c = Parse("public contract Child: " + (reverse ? "Other, Future" : "Future, Other") + "\npublic contract Other: Child");
        Assert.True(c.Binding.Bind(BindingMode.Provisional).InvalidCount > 0);
        Assert.Equal(BindingState.Invalid, Container(c, "Child").BindingState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncompleteRefinementCannotSupplyAKnownAncestor(bool independent)
    {
        var c = Parse("public contract Child: Copy, Future\ngroup G\n    func inspect<T>(value: T)\n        T is Child" + (independent ? "\n        T is Copy" : string.Empty) + "\n        ()");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        var function = Container(c, "G").Members.OfType<FunctionKoto>().Single();
        Assert.Equal(independent ? ConstraintProof.Proven : ConstraintProof.Unknown, c.Binding.ProveCopy(function.Parameters[0].Type.BoundType!, function));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AppendedParentsContributeTheirActualRequirements(bool implemented)
    {
        var c = Parse("public contract Child: Future\npublic struct Target\n    Self is Child" + (implemented ? "\n    public func read(self: ref/Self) -> i32 => 1" : string.Empty));
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", "public contract Future\n    func read(self: ref/Self) -> i32"));
        Assert.Equal(implemented, c.Bind().IsComplete);
        Assert.Equal(implemented, c.Binding.GetConformanceDefinition(Container(c, "Target").BoundType!, Container(c, "Child").BoundSymbol!)!.IsVerified);
    }

    [Theory]
    [InlineData("public struct Future")]
    [InlineData("public contract Future: Child")]
    public void SuppliedInvalidParentsAreRechecked(string declaration)
    {
        var c = Parse("public contract Child: Future");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", declaration));
        Assert.True(c.Binding.Bind(BindingMode.Provisional).InvalidCount > 0);
        Assert.False(c.Bind().IsComplete);
    }

    [Fact]
    public void FinalAbsenceAndReloadPreserveTheDeadline()
    {
        var c = Parse("public contract Child: Future\npublic contract Leaf: Child");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(BindingState.Invalid, Container(c, "Child").BindingState);
        var restored = Reload(c);
        Assert.Equal(0, restored.Binding.Bind(BindingMode.Provisional).InvalidCount);
        Assert.Equal(BindingState.Unresolved, Container(restored, "Leaf").BindingState);
    }

    [Fact]
    public void WarmMissingParentBindingAllocatesNothing()
    {
        var c = Parse("public contract Child: Copy, Future\npublic contract Leaf: Child");
        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (c.Binding.Bind(BindingMode.Provisional).InvalidCount != 0)
            {
                throw new InvalidOperationException("Absent parent became invalid.");
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

    private static DeclarationContainerKoto Container(Compilation c, string name)
        => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == name);
}
