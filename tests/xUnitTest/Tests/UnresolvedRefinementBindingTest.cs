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
        Assert.Equal(BindingState.Unresolved, contract.StateOf());
        var function = Container(c, "G").Members.OfType<FunctionKoto>().Single();
        Assert.Equal(ConstraintProof.Unknown, c.Binding.Prove(Assert.IsType<IsKoto>(function.TypeConstraints[0]).ConstraintOf()!, function));
        Assert.False(c.Binding.GetConformanceDefinition(Container(c, "Target").TypeOf()!, contract.SymbolOf()!)!.IsVerified);
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", qualified ? "public group Api\n    public contract Future" : "public contract Future"));
        Assert.True(c.Bind().IsComplete, string.Join("; ", c.Binding.Issues.Select(x => x.ToString())));
        Assert.True(c.Binding.GetConformanceDefinition(Container(c, "Target").TypeOf()!, contract.SymbolOf()!)!.IsVerified);
        Assert.True(CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("i32")]
    [InlineData("Future<i32>")]
    public void AvailableNonContractOrGenericParentsRemainInvalid(string parent)
    {
        var c = Parse("public group Api\npublic struct Target\npublic contract Future\npublic contract Child: " + parent);
        Assert.True(c.Binding.Bind(BindingMode.Provisional).InvalidCount > 0);
        Assert.Equal(BindingState.Invalid, Container(c, "Child").StateOf());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingParentsDoNotHideKnownRefinementCycles(bool reverse)
    {
        var c = Parse("public contract Child: " + (reverse ? "Other, Future" : "Future, Other") + "\npublic contract Other: Child");
        Assert.True(c.Binding.Bind(BindingMode.Provisional).InvalidCount > 0);
        Assert.Equal(BindingState.Invalid, Container(c, "Child").StateOf());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncompleteRefinementCannotSupplyAKnownAncestor(bool independent)
    {
        var c = Parse("public contract Child: Copy, Future\ngroup G\n    func inspect<T>(value: T)\n        T is Child" + (independent ? "\n        T is Copy" : string.Empty) + "\n        ()");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        var function = Container(c, "G").Members.OfType<FunctionKoto>().Single();
        Assert.Equal(independent ? ConstraintProof.Proven : ConstraintProof.Unknown, c.Binding.ProveCopy(function.Parameters[0].Type.TypeOf()!, function));
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
        Assert.Equal(implemented, c.Binding.GetConformanceDefinition(Container(c, "Target").TypeOf()!, Container(c, "Child").SymbolOf()!)!.IsVerified);
    }

    [Theory]
    [InlineData("public contract Future: Child")]
    public void SuppliedInvalidParentsAreRechecked(string declaration)
    {
        var c = Parse("public contract Child: Future");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", declaration));
        Assert.True(c.Binding.Bind(BindingMode.Provisional).InvalidCount > 0);
        Assert.False(c.Bind().IsComplete);
    }

    // A role filters candidates by declaration kind, and lookup stops only at a stage with an eligible declaration (SPEC 9.2, 9.5): a
    // declaration of the name that the role excludes, of any kind, decides nothing. The name is missing: pending while Provisional,
    // since a later source can still supply it, and a missing name's records at the final Bind.
    [Theory]
    // A parent (Contract Reference): a struct or a group of its name beside it, a struct supplied later, and a nearer group passed
    // for the Contract a later source supplies (v07).
    [InlineData("public group Api\npublic struct Target\npublic contract Future\npublic contract Child: Target", null, "InvalidConstraint_Kd Target|InvalidConstraint_Kd contract Child")]
    [InlineData("public group Api\npublic struct Target\npublic contract Future\npublic contract Child: Api", null, "InvalidConstraint_Kd Api|InvalidConstraint_Kd contract Child")]
    [InlineData("public contract Child: Future", "public struct Future", "InvalidConstraint_Kd Future|InvalidConstraint_Kd contract Child")]
    [InlineData("public group G\n    group Future\n        func z() -> i32 => 0\n    public contract Child: Future", "public contract Future", "")]
    // A requirement's Type argument (Core): a Contract of its name (v11).
    [InlineData("public contract C\n    func m(self: ref/Self) -> i32\npublic struct Box<T>\ngroup G\n    func f<T>(x: T)\n        T is Box<C>\n        ()", null, "InvalidConstraint_Kd Box<C>|InvalidConstraint_Kd func f<T>(x: T)\n    T is Box<C>\n    ()|UnresolvedBinding_Kd C")]
    public void ADeclarationTheRoleExcludesLeavesTheNameMissing(string source, string? supplied, string records)
    {
        var c = Parse(source);
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        if (supplied is not null)
        {
            c.Kotonoha.AddSource(new SourceDocument("Generated.kimi", supplied));
            Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        }

        Assert.Equal(records.Length == 0, c.Bind().IsComplete);
        Assert.Equal(records, string.Join('|', c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}").Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void FinalAbsenceAndReloadPreserveTheDeadline()
    {
        var c = Parse("public contract Child: Future\npublic contract Leaf: Child");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(BindingState.Invalid, Container(c, "Child").StateOf());
        var restored = CompilationTestHelper.Reload(c);
        Assert.Equal(0, restored.Binding.Bind(BindingMode.Provisional).InvalidCount);
        Assert.Equal(BindingState.Unresolved, Container(restored, "Leaf").StateOf());
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmMissingParentBindingAllocatesNothing()
    {
        var c = Parse("public contract Child: Copy, Future\npublic contract Leaf: Child");
        Assert.Equal(0, c.Binding.Bind(BindingMode.Provisional).InvalidCount);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (c.Binding.Bind(BindingMode.Provisional).InvalidCount != 0)
            {
                throw new InvalidOperationException("Absent parent became invalid.");
            }
        }));
    }

    private static Compilation Parse(string source)
    {
        var c = CompilationTestHelper.Parse(source, "Hello.kimi");
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        return c;
    }

    private static DeclarationContainerKoto Container(Compilation c, string name)
        => c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == name);
}
