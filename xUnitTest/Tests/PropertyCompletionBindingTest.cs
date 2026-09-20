// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class PropertyCompletionBindingTest
{
    [Theory]
    [InlineData("E<Source>")]
    [InlineData("unsafe/E<Source>")]
    [InlineData("(E<Source>, i32)")]
    public void LateInvalidPropertyTypeCannotRemainVerified(string type)
    {
        var c = MinimalEmissionTest.Analyze("contract Hidden\npublic struct Source\n    Self is Hidden\npublic enum E<T>\n    T is Hidden\n    A\nstruct S\n    var value: " + type);
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Access);
        Assert.False(Property(c).IsVerified);
        Assert.False(c.Bind().IsComplete);
        Assert.False(Property(c).IsVerified);
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.False(Property(restored).IsVerified);
    }

    [Theory]
    [InlineData("E<Source>")]
    [InlineData("unsafe/E<Source>")]
    [InlineData("(E<Source>, i32)")]
    public void ValidApiTypesRemainVerified(string type)
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "struct S\n    var value: " + type);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(Property(c).IsVerified);
        var restored = Reload(c);
        Assert.True(restored.Bind().IsComplete);
        Assert.True(Property(restored).IsVerified);
    }

    [Fact]
    public void LateInvalidOwnerCannotCertifyIndependentPropertyType()
    {
        var c = MinimalEmissionTest.Analyze("contract Hidden\npublic struct S<T>\n    T is Hidden\n    var value: i32");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Access);
        Assert.False(Property(c).IsVerified);
    }

    [Fact]
    public void StaticBorrowCannotRetainPropertyCertificate()
    {
        var c = MinimalEmissionTest.Analyze("group S\n    var value: ref{static}/i32");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(Property(c).IsVerified);
    }

    [Fact]
    public void ValidDependentPropertyUsesItsDeclaredEvidence()
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "struct S<U>\n    U is Hidden\n    var value: E<U>");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(Property(c).IsVerified);
    }

    [Fact]
    public void CorrectingTypeRestoresSamePropertyCertificate()
    {
        var c = MinimalEmissionTest.Analyze(Prefix.Replace("public contract Hidden", "internal contract Hidden", StringComparison.Ordinal) + "struct S\n    var value: i32");
        Assert.False(c.Binding.Result.IsComplete);
        var property = Property(c);
        Assert.True(property.IsVerified);
        var original = property.Declaration.TypeKoto!;
        var donor = MinimalEmissionTest.Analyze(Prefix + "struct S\n    var value: E<Source>");
        var replacement = Property(donor).Declaration.TypeKoto!;
        Assert.True(KotoHelper.Replace(property.Declaration, original, replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.False(property.IsVerified);
        Assert.True(KotoHelper.Replace(property.Declaration, replacement, original));
        Assert.False(c.Bind().IsComplete); // The unused E declaration still has an invalid API.
        Assert.Same(property, Property(c));
        Assert.True(property.IsVerified);
    }

    [Fact]
    public void WarmPropertyCompletionAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(Prefix + "struct S\n    var value: E<Source>");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Property completion failed.");
            }
        }));
    }

    private const string Prefix = "public contract Hidden\npublic struct Source\n    Self is Hidden\npublic enum E<T>\n    T is Hidden\n    A\n";

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

    private static BoundProperty Property(Compilation c)
        => Assert.IsType<PropertyKoto>(c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S").Members.OfType<PropertyKoto>().Single()).BoundSymbol!.Property!;
}
