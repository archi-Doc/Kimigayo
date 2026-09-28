// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class AssociatedSpecificationAccessBindingTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssociatedSpecificationsCannotHideRestrictedProjectionContracts(bool conditional)
    {
        var declaration = conditional
            ? "public struct Api<T>\n    Self is Export when T is Copy\n        associate Export.Item is Source.Hidden.Element"
            : "public struct Api\n    Self is Export\n    associate Export.Item is Source.Hidden.Element";
        var c = MinimalEmissionTest.Analyze("contract Hidden\n    associate Element\npublic struct Source\n    Self is Hidden\n    associate Hidden.Element is i32\npublic contract Export\n    associate Item\n" + declaration);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Binding.Issues, x => x.Node is IsKoto && x.Node.BindingFailure == BindingFailure.Access);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("public", "public", "internal", "public", false)]
    [InlineData("public", "public", "public", "internal", false)]
    [InlineData("public", "public", "public", "public", true)]
    [InlineData("public", "internal", "internal", "public", true)]
    [InlineData("internal", "public", "internal", "public", true)]
    [InlineData("public", "private", "public", "internal", true)]
    public void OrdinarySpecificationUsesTheIntersection(string typeAccess, string exportAccess, string contractAccess, string sourceAccess, bool valid)
    {
        Check($"{contractAccess} contract Hidden\n    associate Element\n{sourceAccess} struct Source\n    Self is Hidden\n    associate Hidden.Element is i32\n{exportAccess} contract Export\n    associate Item\n{typeAccess} struct Api\n    Self is Export\n    associate Export.Item is Source.Hidden.Element", valid);
    }

    [Theory]
    [InlineData("public", "public", "internal", false)]
    [InlineData("public", "internal", "internal", true)]
    [InlineData("internal", "public", "internal", true)]
    [InlineData("public", "public", "public", true)]
    public void ConditionalSpecificationUsesTheIntersection(string typeAccess, string exportAccess, string contractAccess, bool valid)
    {
        Check($"{contractAccess} contract Hidden\n    associate Element\npublic struct Source\n    Self is Hidden\n    associate Hidden.Element is i32\n{exportAccess} contract Export\n    associate Item\n{typeAccess} struct Api<T>\n    Self is Export when T is Copy\n        associate Export.Item is Source.Hidden.Element", valid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NarrowChildCannotHideAnAncestorsAssociatedExposure(bool conditional)
    {
        var declaration = conditional
            ? "public struct Api<T>\n    Self is Child when T is Copy\n        associate Export.Item is Source.Hidden.Element"
            : "public struct Api\n    Self is Child\n    associate Export.Item is Source.Hidden.Element";
        Check("contract Hidden\n    associate Element\npublic struct Source\n    Self is Hidden\n    associate Hidden.Element is i32\npublic contract Export\n    associate Item\ncontract Child: Export\n" + declaration, false);
    }

    [Theory]
    [InlineData("Source.Element")]
    [InlineData("[1 of Source.Hidden.Element]")]
    [InlineData("(i32, Source.Hidden.Element)")]
    [InlineData("Box<Source.Hidden.Element>")]
    public void CompoundSpecificationsRetainProjectionDomains(string type)
    {
        Check("contract Hidden\n    associate Element\npublic struct Source\n    Self is Hidden\n    associate Hidden.Element is i32\npublic struct Box<T>\npublic contract Export\n    associate Item\npublic struct Api\n    Self is Export\n    associate Export.Item is " + type, false);
    }

    [Fact]
    public void NarrowConditionalPathDoesNotBorrowAnotherPathsDomain()
    {
        Check("contract Hidden\n    associate Element\npublic struct Source\n    Self is Hidden\n    associate Hidden.Element is i32\npublic contract Export\n    associate Item\ncontract Local\n    associate Other\npublic struct Api<T>\n    Self is Export when T is Copy\n        associate Export.Item is i32\n    Self is Local when T is Owned\n        associate Local.Other is Source.Hidden.Element", true);
    }

    [Theory]
    [InlineData("public", false)]
    [InlineData("internal", true)]
    public void ReloadAndRebindRetainAccess(string access, bool valid)
    {
        var c = Check("contract Hidden\n    associate Element\npublic struct Source\n    Self is Hidden\n    associate Hidden.Element is i32\n" + access + " contract Export\n    associate Item\npublic struct Api<T>\n    Self is Export when T is Copy\n        associate Export.Item is Source.Hidden.Element", valid);
        Assert.Equal(valid, c.Bind().IsComplete);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha), ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        Assert.Equal(valid, restored.Bind().IsComplete);
        Assert.Equal(c.Binding.Issues.Select(x => x.Code), restored.Binding.Issues.Select(x => x.Code));
    }

    [Fact]
    public void WarmSpecificationChecksAllocateNothing()
    {
        var c = Check("contract Hidden\n    associate Element\npublic struct Source\n    Self is Hidden\n    associate Hidden.Element is i32\ncontract Export\n    associate Item\npublic struct Api<T>\n    Self is Export when T is Copy\n        associate Export.Item is Source.Hidden.Element", true);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Associated specification Binding failed.");
            }
        }));
    }

    private static Compilation Check(string source, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.True(c.Binding.Result.IsComplete == valid, MinimalEmissionTest.Describe(c, null));
        var api = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Api");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Export");
        var definition = c.Binding.GetConformanceDefinition(api.BoundType!, contract.BoundSymbol!);
        Assert.NotNull(definition);
        Assert.Equal(valid, definition.IsVerified);
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, x => x.Node is IsKoto && x.Node.BindingFailure == BindingFailure.Access);
            Assert.False(c.Emission.Validate(out _));
        }

        return c;
    }
}
