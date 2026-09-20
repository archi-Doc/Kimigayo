// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class GenericConstraintCertificateBindingTest
{
    [Theory]
    [InlineData("Box<string>")]
    [InlineData("(i32, Box<string>)")]
    [InlineData("[2 of Box<string>]")]
    [InlineData("unsafe/Box<string>")]
    [InlineData("() -> Box<string>")]
    public void InvalidGenericPropertyTypeCannotCertify(string type)
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\nstruct S\n    var value: " + type);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnsatisfiedConstraint_Kd);
        var property = Assert.IsType<PropertyKoto>(c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S").Members.Single());
        Assert.False(property.BoundSymbol!.Property!.IsVerified);
        Assert.False(c.Bind().IsComplete);
        Assert.False(property.BoundSymbol.Property.IsVerified);
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.False(Property(restored).BoundSymbol!.Property!.IsVerified);
    }

    [Theory]
    [InlineData("func accept(self: ref/Self, value?: Box<string>)", "public func accept(self: ref/Self, value?: Box<string>) => ()")]
    [InlineData("property value: Box<string> has get, set", "public var value: Box<string>")]
    [InlineData("func accept(self: ref/Self, value?: Box<string>) -> Box<string>", "public func accept(self: ref/Self, value?: Box<string>) -> Box<string> => value")]
    public void InvalidGenericSignatureCannotCertifyWitness(string requirement, string implementation)
    {
        var source = "public struct Box<T>\n    T is i32\n    Self is Copy\npublic contract C\n    " + requirement + "\npublic struct S\n    Self is C\n    " + implementation;
        var valid = MinimalEmissionTest.Analyze(source.Replace("Box<string>", "Box<i32>", StringComparison.Ordinal));
        Assert.True(valid.Binding.Result.IsComplete, MinimalEmissionTest.Describe(valid, null));
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnsatisfiedConstraint_Kd);
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
        Assert.False(c.Binding.GetConformanceDefinition(type.BoundType!, contract.BoundSymbol!)?.IsVerified ?? false);
        Assert.False(c.Bind().IsComplete);
        Assert.False(c.Binding.GetConformanceDefinition(type.BoundType!, contract.BoundSymbol!)?.IsVerified ?? false);
    }

    [Theory]
    [InlineData("contract C\n    property value: i32 has set")]
    [InlineData("struct S\n    computed value: i32\n        set(value: i32) -> () => ()")]
    public void MissingMandatoryGetterIsRejectedWithoutCrashing(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Contains(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray(), x => x.Entry.Name == nameof(DiagnosticCode.IncompleteSyntax_Kd));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(Assert.IsType<PropertyKoto>(c.Kotonoha.RootKoto.NestedContainers.Single().Members.Single()).BoundSymbol!.Property!.IsVerified);
        Assert.False(c.Bind().IsComplete);
        Assert.False(Reload(c).Bind().IsComplete);
    }

    [Fact]
    public void ChangedPropertyTypeRevokesAndRestoresTheSameCertificate()
    {
        const string source = "struct Box<T>\n    T is i32\nstruct S\n    var value: Box<i32>";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        var property = Property(c);
        var certificate = property.BoundSymbol!.Property!;
        var original = property.TypeKoto!;
        var replacement = Property(MinimalEmissionTest.Analyze(source.Replace("Box<i32>", "Box<string>", StringComparison.Ordinal))).TypeKoto!;
        Assert.True(KotoHelper.Replace(property, original, replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.False(certificate.IsVerified);
        Assert.True(KotoHelper.Replace(property, replacement, original));
        Assert.True(c.Bind().IsComplete);
        Assert.True(certificate.IsVerified);
    }

    [Fact]
    public void DependentPropertyConstraintsUseDefinitionEvidence()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is Copy\nstruct S<U>\n    U is Copy\n    var value: Box<U>");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(Property(c).BoundSymbol!.Property!.IsVerified);
        Assert.True(Reload(c).Bind().IsComplete);
    }

    [Fact]
    public void WarmGenericCertificateChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\nstruct S\n    var value: Box<i32>");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Generic certificate Binding failed.");
            }
        }));
    }

    private static PropertyKoto Property(Compilation c)
        => Assert.IsType<PropertyKoto>(c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S").Members.Single(x => x is PropertyKoto));

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
