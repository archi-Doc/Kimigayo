// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class TestAttributeCertificateBindingTest
{
    [Theory]
    [InlineData("contract C\n#Test\nstruct S\n    Self is C")]
    [InlineData("#Test\ncontract C\nstruct S\n    Self is C")]
    public void InvalidTestTargetCannotCertifyConformance(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidTestDefinition_Kd);
        Assert.False(Definition(c).IsVerified);
        Assert.False(c.Bind().IsComplete);
        Assert.False(Definition(c).IsVerified);
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.False(Definition(restored).IsVerified);
    }

    [Fact]
    public void InvalidTestTargetCannotCertifyProperty()
    {
        var c = MinimalEmissionTest.Analyze("struct S\n    #Test\n    var value: i32");
        Assert.False(c.Binding.Result.IsComplete);
        var property = Assert.IsType<PropertyKoto>(c.Kotonoha.RootKoto.NestedContainers.Single().Members.Single());
        Assert.False(property.BoundSymbol!.Property!.IsVerified);
    }

    [Theory]
    [InlineData("#Unknown\n#Test")]
    [InlineData("#Test\n#Unknown")]
    public void OtherInvalidMarkerIsStillChecked(string markers)
    {
        var c = MinimalEmissionTest.Analyze(markers + "\nstruct S");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidTestDefinition_Kd);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnresolvedBinding_Kd);
    }

    [Theory]
    [InlineData("#Layout(\"bad\")\n#Test")]
    [InlineData("#Test\n#Layout(\"bad\")")]
    public void LayoutMarkerIsCheckedInEitherOrder(string markers)
    {
        var c = MinimalEmissionTest.Analyze(markers + "\nstruct S");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidTestDefinition_Kd);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidLayoutAttribute_Kd);
    }

    [Fact]
    public void InvalidGroupTestMarkerInvalidatesNestedCertificates()
    {
        var c = MinimalEmissionTest.Analyze("contract C\n#Test\ngroup Outer\n    struct S\n        Self is C");
        Assert.False(c.Binding.Result.IsComplete);
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Outer").NestedContainers.Single();
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
        Assert.False(c.Binding.GetConformanceDefinition(type.BoundType!, contract.BoundSymbol!)!.IsVerified);
    }

    [Fact]
    public void RemovingWrongTargetMarkerRestoresCertificate()
    {
        var c = MinimalEmissionTest.Analyze("contract C\n#Test\nstruct S\n    Self is C");
        var definition = Definition(c);
        Assert.False(definition.IsVerified);
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        Assert.True(type.RemoveAttribute(Assert.IsType<AttributeKoto>(type.AttributeChain)));
        Assert.True(c.Bind().IsComplete);
        Assert.True(definition.IsVerified);
    }

    [Fact]
    public void ProductExcludedFunctionsRemainExcluded()
    {
        var c = MinimalEmissionTest.Analyze("#Test\nfunc example()\n    missing()\ncontract C\nstruct S\n    Self is C");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(Definition(c).IsVerified);
        Assert.True(c.Bind().IsComplete);
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
        return c.Binding.GetConformanceDefinition(type.BoundType!, contract.BoundSymbol!)!;
    }
}
