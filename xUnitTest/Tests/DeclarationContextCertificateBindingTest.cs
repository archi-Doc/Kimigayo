// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class DeclarationContextCertificateBindingTest
{
    [Theory]
    [InlineData("#Unknown")]
    [InlineData("#Layout(\"C\")")]
    public void InvalidGroupCannotCertifyNestedConformance(string attribute)
    {
        var c = MinimalEmissionTest.Analyze("contract Marker\n" + attribute + "\ngroup Invalid\n    struct S\n        Self is Marker");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Invalid").NestedContainers.Single();
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Marker");
        Assert.False(c.Binding.GetConformanceDefinition(type.BoundType!, contract.BoundSymbol!)?.IsVerified ?? false);
        Assert.False(c.Bind().IsComplete);
        Assert.False(c.Binding.GetConformanceDefinition(type.BoundType!, contract.BoundSymbol!)?.IsVerified ?? false);
    }

    [Fact]
    public void InvalidGroupCannotCertifyNestedProperty()
    {
        var c = MinimalEmissionTest.Analyze("#Unknown\ngroup Invalid\n    group Inner\n        public let value: i32 = 1");
        Assert.False(c.Binding.Result.IsComplete);
        var property = Assert.IsType<PropertyKoto>(c.Kotonoha.RootKoto.NestedContainers.Single().NestedContainers.Single().Members.Single());
        Assert.False(property.BoundSymbol!.Property!.IsVerified);
    }

    [Theory]
    [InlineData("#Unknown")]
    [InlineData("#Layout(\"C\")")]
    public void InvalidGroupCannotSupplyNestedContract(string attribute)
    {
        var c = MinimalEmissionTest.Analyze(attribute + "\ngroup Invalid\n    public contract Marker\nstruct S\n    Self is Invalid.Marker");
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Invalid").NestedContainers.Single();
        Assert.False(c.Binding.GetConformanceDefinition(type.BoundType!, contract.BoundSymbol!)?.IsVerified ?? false);
    }

    [Fact]
    public void IndependentSiblingCertificatesRemainValid()
    {
        var c = MinimalEmissionTest.Analyze("contract Marker\n#Unknown\ngroup Invalid\n    struct Bad\n        Self is Marker\n    func stillChecked() -> i32 => true\ngroup Valid\n    struct Good\n        Self is Marker\n    let value: i32 = 1");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.TypeMismatch);
        var valid = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Valid");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Marker");
        Assert.True(c.Binding.GetConformanceDefinition(valid.NestedContainers.Single().BoundType!, contract.BoundSymbol!)!.IsVerified);
        Assert.True(Assert.IsType<PropertyKoto>(valid.Members.Single()).BoundSymbol!.Property!.IsVerified);
    }

    [Fact]
    public void RemovingEnclosingMarkerRestoresBothCertificates()
    {
        var c = MinimalEmissionTest.Analyze("contract Marker\n#Unknown\ngroup Outer\n    group Inner\n        struct S\n            Self is Marker\n        let value: i32 = 1");
        var outer = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Outer");
        var inner = outer.NestedContainers.Single();
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Marker");
        var definition = c.Binding.GetConformanceDefinition(inner.NestedContainers.Single().BoundType!, contract.BoundSymbol!)!;
        var property = Assert.IsType<PropertyKoto>(inner.Members.Single()).BoundSymbol!.Property!;
        Assert.False(definition.IsVerified);
        Assert.False(property.IsVerified);
        Assert.True(outer.RemoveAttribute(Assert.IsType<AttributeKoto>(outer.AttributeChain)));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(definition.IsVerified);
        Assert.True(property.IsVerified);
    }

    [Fact]
    public void ReloadRetainsInvalidEnclosingDeclaration()
    {
        var c = MinimalEmissionTest.Analyze("contract Marker\n#Unknown\ngroup Outer\n    struct S\n        Self is Marker\n    let value: i32 = 1");
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha), ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        Assert.False(restored.Bind().IsComplete);
        var outer = restored.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Outer");
        var contract = restored.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Marker");
        Assert.False(restored.Binding.GetConformanceDefinition(outer.NestedContainers.Single().BoundType!, contract.BoundSymbol!)!.IsVerified);
        Assert.False(Assert.IsType<PropertyKoto>(outer.Members.Single()).BoundSymbol!.Property!.IsVerified);
    }

    [Fact]
    public void WarmContextChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("contract Marker\ngroup Outer\n    group Inner\n        struct S\n            Self is Marker\n        let value: i32 = 1");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Nested declaration Binding failed.");
            }
        }));
    }
}
