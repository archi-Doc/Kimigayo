// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class UnknownAttributeCertificateBindingTest
{
    [Theory]
    [InlineData("contract Marker\n#Unknown\nstruct S\n    Self is Marker")]
    [InlineData("#Unknown\ncontract Marker\nstruct S\n    Self is Marker")]
    [InlineData("contract Marker\n    func act(self: ref/Self)\nstruct S\n    Self is Marker\n    #Unknown\n    public func act(self: ref/Self) => ()")]
    [InlineData("contract Marker\n    property value: i32 has get\nstruct S\n    Self is Marker\n    #Unknown\n    public var value: i32 = 0")]
    [InlineData("contract Marker\n    func act(self: ref/Self, value: i32)\nstruct S\n    Self is Marker\n    public func act(self: ref/Self, #Unknown value: i32) => ()")]
    [InlineData("contract Marker\n#Unknown\n#Layout(\"C\")\nstruct S\n    Self is Marker")]
    [InlineData("contract Marker\n#Layout(\"C\")\n#Unknown\nstruct S\n    Self is Marker")]
    public void UnknownAttributeCannotCertifyConformance(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Marker");
        Assert.False(c.Binding.GetConformanceDefinition(type.BoundType!, contract.BoundSymbol!)?.IsVerified ?? false);
        Assert.False(c.Bind().IsComplete);
        Assert.False(Definition(c).IsVerified);
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
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidAttributedBaseCannotCertifyDerivedType(bool reverseOrder)
    {
        const string invalidBase = "#Unknown\nopen struct Base\n";
        const string derived = "struct S: Base\n    Self is Marker\n";
        var c = MinimalEmissionTest.Analyze("contract Marker\n" + (reverseOrder ? derived + invalidBase : invalidBase + derived));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(Definition(c).IsVerified);
    }

    [Theory]
    [InlineData("#if false\n    #Unknown\n    struct Ignored")]
    [InlineData("#if false\n    #Unknown(Missing)\n    func ignored() => Missing")]
    public void ExcludedMarkersDoNotInvalidateSelectedDeclarations(string excluded)
    {
        var c = MinimalEmissionTest.Analyze(excluded + "\ncontract Marker\nstruct S\n    Self is Marker");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(Definition(c).IsVerified);
    }

    [Fact]
    public void ProvisionalSyntaxRemainsReadableAndRemovalRestoresCertification()
    {
        var c = MinimalEmissionTest.Analyze("contract Marker\n#Unknown(123)\nstruct S\n    Self is Marker");
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var attribute = Assert.IsType<AttributeKoto>(type.AttributeChain);
        Assert.False(c.Binding.Bind(BindingMode.Provisional).IsComplete);
        Assert.Empty(c.Binding.Issues);
        Assert.Equal("Unknown", Assert.IsType<IdentifierNameKoto>(attribute.IdentifierKoto).IdentifierName);
        Assert.Equal("123", Assert.IsType<NumberLiteralKoto>(Assert.Single(attribute.Arguments)).SourceSpelling.ToString());
        Assert.False(Definition(c).IsVerified);
        Assert.True(type.RemoveAttribute(attribute));
        Assert.True(c.Bind().IsComplete);
        Assert.True(Definition(c).IsVerified);
    }

    [Fact]
    public void WarmInvalidAttributeBindingAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("contract Marker\n#Unknown\nstruct S\n    Self is Marker");
        for (var i = 0; i < 100; i++)
        {
            Assert.False(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Unknown Attribute unexpectedly completed Binding.");
            }
        }));
    }

    private static BoundConformance Definition(Compilation c)
    {
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Marker");
        return Assert.IsType<BoundConformance>(c.Binding.GetConformanceDefinition(type.BoundType!, contract.BoundSymbol!));
    }
}
