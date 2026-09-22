// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class LayoutCertificateBindingTest
{
    [Theory]
    [InlineData("#Layout(\"bad\")\nstruct S {}\n    Self is Marker")]
    [InlineData("#Layout(\"C\")\n#Layout(\"C\")\nstruct S {}\n    Self is Marker")]
    [InlineData("#Layout(\"C\")\nstruct S {}\n    Self is Marker\n#Layout(\"Kimigayo\")\nstruct S {}")]
    [InlineData("#Layout(\"C\")\nstruct S {}\n    Self is Marker\n    var a: i32\nstruct S {}\n    var b: i32")]
    public void InvalidLayoutCannotCertifyConformance(string declaration)
    {
        var c = MinimalEmissionTest.Analyze("contract Marker\n" + declaration);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var marker = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Marker");
        Assert.False(c.Binding.GetConformanceDefinition(type.BoundType!, marker.BoundSymbol!)?.IsVerified ?? false);
        Assert.False(c.Bind().IsComplete);
        Assert.False(c.Binding.GetConformanceDefinition(type.BoundType!, marker.BoundSymbol!)?.IsVerified ?? false);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha), ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        Assert.False(restored.Bind().IsComplete);
        Assert.False(Definition(restored).IsVerified);
    }

    [Theory]
    [InlineData("contract Marker\n    func act(self: ref/Self)\nstruct S {}\n    Self is Marker\n    #Layout(\"C\")\n    public func act(self: ref/Self) => ()")]
    [InlineData("contract Marker\n    property value: i32 has get\nstruct S {}\n    Self is Marker\n    #Layout(\"C\")\n    public var value: i32 = 0")]
    [InlineData("#Layout(\"C\")\ncontract Marker\nstruct S {}\n    Self is Marker")]
    public void WrongLayoutTargetsCannotSupplyWitnesses(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidLayoutAttribute_Kd);
        Assert.False(Definition(c).IsVerified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidLayoutBaseInvalidatesDerivedConformance(bool reverseOrder)
    {
        const string invalidBase = "#Layout(\"bad\")\nopen struct Base {}\n";
        const string derived = "struct S {}: Base\n    Self is Marker\n";
        var c = MinimalEmissionTest.Analyze("contract Marker\n" + (reverseOrder ? derived + invalidBase : invalidBase + derived));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(Definition(c).IsVerified);
    }

    [Fact]
    public void RemovingConflictRestoresConformanceAfterRebind()
    {
        // SPEC 21.1 rejects empty C structs; the field keeps this certificate test valid.
        var c = MinimalEmissionTest.Analyze("contract Marker\n#Layout(\"C\")\nstruct S {}\n    var n: i32\n    Self is Marker");
        Assert.True(c.Binding.Result.IsComplete);
        var definition = Definition(c);
        Assert.True(definition.IsVerified);
        c.Kotonoha.AddSource(new SourceDocument("conflict.kimi", "#Layout(\"Kimigayo\")\nstruct S {}"));
        Assert.False(c.Bind().IsComplete);
        Assert.False(definition.IsVerified);
        Assert.False(Definition(c).IsVerified);
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        Assert.True(type.RemoveAttribute(Assert.IsType<AttributeKoto>(type.AttributeChain)));
        Assert.True(c.Bind().IsComplete);
        Assert.True(Definition(c).IsVerified);
    }

    private static BoundConformance Definition(Compilation c)
    {
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var marker = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Marker");
        return Assert.IsType<BoundConformance>(c.Binding.GetConformanceDefinition(type.BoundType!, marker.BoundSymbol!));
    }
}
