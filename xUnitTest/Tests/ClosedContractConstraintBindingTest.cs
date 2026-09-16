// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ClosedContractConstraintBindingTest
{
    [Theory]
    [InlineData("i32 is Copy", true)]
    [InlineData("string is Copy", false)]
    [InlineData("Source.Origin.Item is i32", true)]
    [InlineData("Source.Origin.Item is string", false)]
    [InlineData("[2 of i32] is Copy", true)]
    [InlineData("(i32, string) is Copy", false)]
    [InlineData("i32 is Copy and Owned", true)]
    [InlineData("i32 is string or Copy", true)]
    [InlineData("string is not Copy", true)]
    [InlineData("Source is Origin", true)]
    public void ClosedContractConditionsAreProvedAtTheDeclaration(string clause, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("public contract Origin\n    associate Item\npublic struct Source\n    Self is Origin\n    associate Origin.Item is i32\npublic contract R\n    " + clause + "\npublic struct Impl\n    Self is R");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid ? BindingState.Resolved : BindingState.Invalid, c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "R").BindingState);
        var implementation = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Impl");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "R");
        Assert.Equal(valid, c.Binding.GetConformanceDefinition(implementation.BoundType!, contract.BoundSymbol!)!.IsVerified);
        Assert.Equal(valid, c.Bind().IsComplete);
        Assert.Equal(valid, Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosedConditionsCannotSupplyTheirOwnMissingConformance(bool cycle)
    {
        var c = MinimalEmissionTest.Analyze("public contract R\n    Source is R\npublic struct Source" + (cycle ? "\n    Self is R" : string.Empty));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(BindingState.Invalid, c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "R").BindingState);
        Assert.False(Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LateFailureInvalidatesContractRefinementsInEitherSourceOrder(bool reverse)
    {
        const string contract = "public contract R\n    Source.Origin.Item is i32\npublic contract Child: R\npublic struct Impl\n    Self is Child\n";
        const string source = "public struct Source\n    string is Copy\n    Self is Origin\n    associate Origin.Item is i32\n";
        var c = MinimalEmissionTest.Analyze("public contract Origin\n    associate Item\n" + (reverse ? contract + source : source + contract));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(BindingState.Invalid, c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "R").BindingState);
        var child = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Child");
        Assert.Equal(BindingState.Invalid, child.BindingState);
        var implementation = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Impl");
        Assert.False(c.Binding.GetConformanceDefinition(implementation.BoundType!, child.BoundSymbol!)!.IsVerified);
    }

    [Theory]
    [InlineData("(Origin.Item, i32) is Copy", "i32", true)]
    [InlineData("(Origin.Item, i32) is Copy", "string", false)]
    [InlineData("i32 is Origin.Item", "i32", true)]
    [InlineData("i32 is Origin.Item", "string", false)]
    public void SelfDependentConditionsRemainImplementationObligations(string clause, string item, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("public contract Origin\n    associate Item\npublic contract R: Origin\n    " + clause + "\npublic struct Impl\n    Self is R\n    associate Origin.Item is " + item);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(BindingState.Resolved, c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "R").BindingState);
        Assert.Equal(valid, Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("Hidden is Copy", "public", true)]
    [InlineData("Hidden is Copy", "internal", false)]
    [InlineData("[2 of Hidden] is Copy", "internal", false)]
    public void ClosedSubjectsMustCoverTheContractApi(string clause, string access, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(access + " struct Hidden\n    Self is Copy\npublic contract R\n    " + clause);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Access);
        }
    }

    [Theory]
    [InlineData("Self")]
    [InlineData("((Self))")]
    public void SelfClausesDoNotDeclareContractRefinement(string subject)
    {
        var c = MinimalEmissionTest.Analyze("public contract Origin\npublic contract R\n    " + subject + " is Origin");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.InvalidConstraint);
    }

    [Fact]
    public void ClosedContractReplacementRevokesAndRestoresConformance()
    {
        const string source = "public contract R\n    i32 is Copy\npublic struct Impl\n    Self is R";
        var c = MinimalEmissionTest.Analyze(source);
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "R");
        var clause = contract.ConstraintNodes[0];
        var original = clause.Left;
        var donor = MinimalEmissionTest.Analyze(source.Replace("i32", "string", StringComparison.Ordinal));
        var replacement = donor.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "R").ConstraintNodes[0].Left;
        var implementation = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Impl");
        Assert.True(KotoHelper.Replace(clause, original, replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.False(c.Binding.GetConformanceDefinition(implementation.BoundType!, contract.BoundSymbol!)!.IsVerified);
        Assert.True(KotoHelper.Replace(clause, replacement, original));
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Binding.GetConformanceDefinition(implementation.BoundType!, contract.BoundSymbol!)!.IsVerified);
    }

    [Fact]
    public void WarmClosedContractChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("public contract R\n    [2 of i32] is Copy\npublic struct Impl\n    Self is R");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Closed Contract proof failed.");
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
}
