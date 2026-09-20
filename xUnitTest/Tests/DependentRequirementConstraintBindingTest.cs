// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class DependentRequirementConstraintBindingTest
{
    [Theory]
    [InlineData("i32 is T", "i32", true)]
    [InlineData("i32 is T", "string", false)]
    [InlineData("[2 of i32] is [2 of T]", "i32", true)]
    [InlineData("[2 of i32] is [2 of T]", "string", false)]
    [InlineData("i32 is not T", "i32", false)]
    [InlineData("i32 is not T", "string", true)]
    [InlineData("i32 is T and Copy", "i32", true)]
    [InlineData("i32 is T and Copy", "string", false)]
    [InlineData("i32 is T or Copy", "string", true)]
    [InlineData("[2 of i32] is [2 of T] or [2 of u32]", "u32", false)]
    [InlineData("(i32, bool) is (T, bool)", "i32", true)]
    [InlineData("(i32, bool) is (T, bool)", "string", false)]
    public void BothPropositionOperandsDetermineInputDependence(string clause, string argument, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("public struct Target<T>\n    " + clause + "\npublic func use(value?: Target<" + argument + ">)\n    return");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(BindingState.Resolved, c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Target").BindingState);
        Assert.Equal(valid, c.Bind().IsComplete);
        Assert.Equal(valid, Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("i32", true)]
    [InlineData("string", false)]
    public void EnumConditionsAreCheckedAtUses(string argument, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("public enum Target<T>\n    i32 is T\n    A\npublic func use(value?: Target<" + argument + ">)\n    return");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid, Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("i32", true)]
    [InlineData("string", false)]
    public void AssociatedRequirementsAreNormalizedAfterSubstitution(string item, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("public contract Origin\n    associate Item\npublic struct Source\n    Self is Origin\n    associate Origin.Item is " + item + "\npublic struct Target<T>\n    i32 is T.Origin.Item\n    T is Origin\npublic func use(value?: Target<Source>)\n    return");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(BindingState.Resolved, c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Target").BindingState);
        Assert.Equal(valid, Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForwardingRequiresTheExactProposition(bool valid)
    {
        var c = MinimalEmissionTest.Analyze("public struct Target<T>\n    i32 is T\npublic struct Forward<U>\n    " + (valid ? "i32 is U" : "U is Copy") + "\n    var value: Target<U>");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
    }

    [Theory]
    [InlineData("i32 is T and string")]
    [InlineData("i32 is T\n    i32 is not T")]
    [InlineData("i32 is Box<string> or T")]
    public void DependenceDoesNotHideContradictionsOrInvalidFormation(string clause)
    {
        var c = MinimalEmissionTest.Analyze("public struct Box<T>\n    T is Copy\npublic struct Target<T>\n    " + clause);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(Reload(c).Bind().IsComplete);
    }

    [Fact]
    public void ReplacingTheRequiredTypeRevokesAndRestoresUses()
    {
        const string source = "public struct Target<T>\n    i32 is T\npublic func use(value?: Target<i32>)\n    return";
        var c = MinimalEmissionTest.Analyze(source);
        var clause = c.Kotonoha.RootKoto.NestedContainers.Single().ConstraintNodes[0];
        var original = clause.Right;
        var donor = MinimalEmissionTest.Analyze(source.Replace("i32 is T", "i32 is not T", StringComparison.Ordinal));
        var replacement = donor.Kotonoha.RootKoto.NestedContainers.Single().ConstraintNodes[0].Right;
        Assert.True(KotoHelper.Replace(clause, original, replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.True(KotoHelper.Replace(clause, replacement, original));
        Assert.True(c.Bind().IsComplete);
    }

    [Fact]
    public void WarmDependentRequirementProofsAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("public struct Target<T>\n    i32 is T\npublic func use(value?: Target<i32>)\n    return");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Dependent requirement failed.");
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
