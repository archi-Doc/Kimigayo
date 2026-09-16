// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class AssociatedTypeFormationBindingTest
{
    [Theory]
    [InlineData("Box<string>")]
    [InlineData("(Box<string>, i32)")]
    [InlineData("[2 of Box<string>]")]
    [InlineData("(unsafe/Box<string>, i32)")]
    public void InvalidAssociatedDefinitionCannotCertifyConformance(string type)
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\ncontract C\n    associate Item\nstruct S\n    Self is C\n    associate C.Item is " + type);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(Definition(c).IsVerified);
        Assert.False(c.Bind().IsComplete);
        Assert.False(Definition(c).IsVerified);
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.False(Definition(restored).IsVerified);
    }

    [Theory]
    [InlineData("Box<i32>")]
    [InlineData("(Box<i32>, i32)")]
    [InlineData("[2 of Box<i32>]")]
    public void ValidAssociatedDefinitionsStillCertify(string type)
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\ncontract C\n    associate Item\nstruct S\n    Self is C\n    associate C.Item is " + type);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(Definition(c).IsVerified);
        var restored = Reload(c);
        Assert.True(restored.Bind().IsComplete);
        Assert.True(Definition(restored).IsVerified);
    }

    [Theory]
    [InlineData("contract C\n    associate Item is Box<string>\nstruct S\n    Self is C")]
    [InlineData("contract Parent\n    associate Item is Box<string>\ncontract C: Parent\nstruct S\n    Self is C")]
    [InlineData("contract C\n    associate Item\nstruct S<U>\n    Self is C when U is Copy\n        associate C.Item is Box<string>")]
    public void FixedInheritedAndConditionalDefinitionsValidateFormation(string declaration)
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\n" + declaration);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.All(Definition(c).Paths, path => Assert.False(path.IsVerified));
    }

    [Theory]
    [InlineData("struct S<U>\n    U is i32\n    Self is C\n    associate C.Item is Box<U>")]
    [InlineData("struct S<U>\n    Self is C when U is i32\n        associate C.Item is Box<U>")]
    public void DependentDefinitionsUseTheirOwnConformanceEvidence(string declaration)
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\ncontract C\n    associate Item\n" + declaration);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.All(Definition(c).Paths, path => Assert.True(path.IsVerified));
    }

    [Fact]
    public void InvalidUnrelatedDefinitionDoesNotPoisonValidConformance()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\ncontract C\n    associate Item\nstruct Invalid\n    Self is C\n    associate C.Item is Box<string>\nstruct S\n    Self is C\n    associate C.Item is Box<i32>");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.True(Definition(c).IsVerified);
    }

    [Fact]
    public void ReplacingDefinitionRestoresSameCertificate()
    {
        const string source = "struct Box<T>\n    T is i32\ncontract C\n    associate Item\nstruct S\n    Self is C\n    associate C.Item is Box<i32>";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        var definition = Definition(c);
        var clause = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S").Members.OfType<IsKoto>().Single(x => x.IsAssociatedConstraint);
        var original = clause.Right;
        var donor = MinimalEmissionTest.Analyze(source.Replace("Box<i32>", "Box<string>", StringComparison.Ordinal));
        var replacement = donor.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S").Members.OfType<IsKoto>().Single(x => x.IsAssociatedConstraint).Right;
        Assert.True(KotoHelper.Replace(clause, original, replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.False(definition.IsVerified);
        Assert.True(KotoHelper.Replace(clause, replacement, original));
        Assert.True(c.Bind().IsComplete);
        Assert.Same(definition, Definition(c));
        Assert.True(definition.IsVerified);
    }

    [Fact]
    public void WarmAssociatedFormationChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\ncontract C\n    associate Item\nstruct S\n    Self is C\n    associate C.Item is Box<i32>");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Associated Type formation failed.");
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

    private static BoundConformance Definition(Compilation c)
    {
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
        return Assert.IsType<BoundConformance>(c.Binding.GetConformanceDefinition(type.BoundType!, contract.BoundSymbol!));
    }
}
