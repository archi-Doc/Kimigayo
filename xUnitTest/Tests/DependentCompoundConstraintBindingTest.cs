// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class DependentCompoundConstraintBindingTest
{
    [Theory]
    [InlineData("[2 of T] is Copy", "i32", true)]
    [InlineData("[2 of T] is Copy", "string", false)]
    [InlineData("(T, i32) is Copy", "i32", true)]
    [InlineData("(T, i32) is Copy", "string", false)]
    [InlineData("[2 of T] is not Copy", "string", true)]
    [InlineData("[2 of T] is not Copy", "i32", false)]
    [InlineData("[2 of T] is Copy and Owned", "i32", true)]
    [InlineData("[2 of T] is Copy and Owned", "string", false)]
    [InlineData("[2 of T] is Copy or [2 of string]", "string", true)]
    [InlineData("[2 of T] is [2 of i32]", "i32", true)]
    [InlineData("[2 of T] is [2 of i32]", "u32", false)]
    [InlineData("((T)) is Copy", "i32", true)]
    [InlineData("((T)) is Copy", "string", false)]
    public void CompoundInputIsCheckedAfterSubstitution(string clause, string argument, bool valid)
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
    public void ConstructedSubjectIdentityIsSubstituted(string argument, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("public struct Box<T>\npublic struct Target<T>\n    Box<T> is Box<i32>\npublic func use(value?: Target<" + argument + ">)\n    return");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid, Reload(c).Bind().IsComplete);
    }

    [Theory]
    [InlineData("struct", "i32", true)]
    [InlineData("struct", "string", false)]
    [InlineData("enum", "i32", true)]
    [InlineData("enum", "string", false)]
    public void GroupedSelfRetainsCopyDerivation(string kind, string argument, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("public " + kind + " Target<T>\n    T is Copy\n    ((Self)) is Copy\n" + (kind == "enum" ? "    A(T)\n" : "    var value: T\n") + "public func use(value?: Target<" + argument + ">)\n    return");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid, Reload(c).Bind().IsComplete);
    }

    [Fact]
    public void GroupedSelfDoesNotAssumeUnimplementedRequirements()
    {
        var c = MinimalEmissionTest.Analyze("public contract C\n    func read(self: ref/Self) -> i32\npublic struct Target<T>\n    (Self) is C");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        var target = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Target");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
        Assert.False(c.Binding.GetConformanceDefinition(target.BoundType!, contract.BoundSymbol!)?.IsVerified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompoundAssumptionsSupportGenericForwarding(bool valid)
    {
        var c = MinimalEmissionTest.Analyze("public struct Target<T>\n    [2 of T] is Copy\npublic struct Forward<U>\n    " + (valid ? "[2 of U] is Copy" : "U is Owned") + "\n    var value: Target<U>");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
    }

    [Theory]
    [InlineData("(T.Origin.Item, i32) is Copy", "i32", true)]
    [InlineData("(T.Origin.Item, i32) is Copy", "string", false)]
    [InlineData("[2 of T.Origin.Item] is Copy", "i32", true)]
    [InlineData("[2 of T.Origin.Item] is Copy", "string", false)]
    public void NestedProjectionInputsAreBoundAfterTheirRootPremises(string clause, string item, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("public contract Origin\n    associate Item\npublic struct Source\n    Self is Origin\n    associate Origin.Item is " + item + "\npublic struct Target<T>\n    " + clause + "\n    T is Origin\npublic func use(value?: Target<Source>)\n    return");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(BindingState.Resolved, c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Target").BindingState);
        Assert.Equal(valid, Reload(c).Bind().IsComplete);
    }

    [Fact]
    public void ContradictoryCompoundAssumptionsInvalidateTheDeclaration()
    {
        var c = MinimalEmissionTest.Analyze("public struct Target<T>\n    [2 of T] is Copy\n    [2 of T] is not Copy");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(BindingState.Invalid, c.Kotonoha.RootKoto.NestedContainers.Single().BindingState);
    }

    [Fact]
    public void CompoundCopyPremiseSupportsStoredFieldDerivation()
    {
        var c = MinimalEmissionTest.Analyze("public struct Target<T>\n    [2 of T] is Copy\n    Self is Copy\n    var values: [2 of T]");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void ReplacingACompoundInputRechecksExistingUses()
    {
        const string source = "public struct Target<T>\n    [2 of T] is Copy\npublic func use(value?: Target<i32>)\n    return";
        var c = MinimalEmissionTest.Analyze(source);
        var clause = c.Kotonoha.RootKoto.NestedContainers.Single().ConstraintNodes[0];
        var original = clause.Right;
        var donor = MinimalEmissionTest.Analyze(source.Replace("is Copy", "is not Copy", StringComparison.Ordinal));
        var replacement = donor.Kotonoha.RootKoto.NestedContainers.Single().ConstraintNodes[0].Right;
        Assert.True(KotoHelper.Replace(clause, original, replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.True(KotoHelper.Replace(clause, replacement, original));
        Assert.True(c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("public", true)]
    [InlineData("internal", false)]
    public void CompoundInputSubjectHasTheDeclarationApiDomain(string access, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(access + " struct Box<T>\npublic struct Target<T>\n    Box<T> is Owned");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Access);
        }
    }

    [Fact]
    public void WarmCompoundInputsAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("public struct Target<T>\n    [2 of T] is Copy\npublic func use(value?: Target<i32>)\n    return");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Compound constraint failed.");
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
