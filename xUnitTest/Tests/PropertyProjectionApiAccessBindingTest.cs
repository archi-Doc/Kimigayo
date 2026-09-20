// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class PropertyProjectionApiAccessBindingTest
{
    [Theory]
    [InlineData("public let item: S.C.Element\n        private get")]
    [InlineData("public computed item: S.Element\n        private get() -> i32 => 1")]
    public void RestrictedAccessorsCannotHideHeaderProjectionDomains(string declaration)
    {
        var c = MinimalEmissionTest.Analyze($"contract C\n    associate Element\npublic struct S\n    Self is C\n    associate C.Element is i32\npublic struct Api\n    {declaration}");
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var property = Property(c);
        Assert.Equal(BindingFailure.Access, property.BindingFailure);
        Assert.False(property.BoundSymbol!.Property!.IsVerified);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("get() -> S.C.Element => 1")]
    [InlineData("get() -> i32 => 1\n        set(value: S.C.Element) -> () => ()")]
    public void AdditionalAccessorSignaturesRetainProjectionDomains(string accessors)
    {
        var c = MinimalEmissionTest.Analyze($"contract C\n    associate Element\npublic struct S\n    Self is C\n    associate C.Element is i32\npublic group Api\n    public computed item: i32\n        {accessors}");
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Binding.Issues, x => x.Node is PropertyAccessorKoto && x.Node.BindingFailure == BindingFailure.Access);
        Assert.False(Property(c).BoundSymbol!.Property!.IsVerified);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("internal", "public", false)]
    [InlineData("public", "internal", false)]
    [InlineData("public", "public", true)]
    public void HeaderQualifiersAndContractsUseTheirEffectiveDomains(string typeAccess, string contractAccess, bool valid)
    {
        var c = MinimalEmissionTest.Analyze($"{contractAccess} contract C\n    associate Element\n{typeAccess} struct S\n    Self is C\n    associate C.Element is i32\npublic struct Api\n    public let item: S.C.Element");
        Check(c, valid, accessor: false);
    }

    [Theory]
    [InlineData("S.Element")]
    [InlineData("(S.C.Element)")]
    [InlineData("(i32, [1 of S.C.Element])")]
    [InlineData("ref{static}/S.C.Element")]
    [InlineData("(S.C.Element) -> ()")]
    public void NestedHeaderProjectionsRetainAccessChecks(string type)
    {
        var c = MinimalEmissionTest.Analyze($"public contract C\n    associate Element\nstruct S\n    Self is C\n    associate C.Element is i32\npublic struct Api\n    public let item: {type}");
        Check(c, false, accessor: false);
    }

    [Fact]
    public void ConstructedHeaderQualifierChecksItsArguments()
    {
        var c = MinimalEmissionTest.Analyze("struct Hidden\npublic contract C\n    associate Element\npublic struct Box<T>\n    Self is C\n    associate C.Element is i32\npublic struct Api\n    public let item: Box<Hidden>.C.Element");
        Check(c, false, accessor: false);
    }

    [Theory]
    [InlineData("public", false)]
    [InlineData("internal", true)]
    public void RequirementHeadersUseTheContractDomain(string access, bool valid)
    {
        var c = MinimalEmissionTest.Analyze($"contract C\n    associate Element\npublic struct S\n    Self is C\n    associate C.Element is i32\n{access} contract Api\n    property item: S.C.Element has get");
        Check(c, valid, accessor: false);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("private ", true)]
    [InlineData("internal ", true)]
    public void SetterInputProjectionUsesItsOwnDomain(string restriction, bool valid)
    {
        var c = MinimalEmissionTest.Analyze($"contract C\n    associate Element\npublic struct S\n    Self is C\n    associate C.Element is i32\npublic struct Api\n    public computed item: i32\n        get() -> i32 => 1\n        {restriction}set(value: (i32, S.C.Element)) -> () => ()");
        Check(c, valid, accessor: true);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("private ", true)]
    public void GetterResultProjectionUsesItsOwnDomain(string restriction, bool valid)
    {
        var c = MinimalEmissionTest.Analyze($"contract C\n    associate Element\npublic struct S\n    Self is C\n    associate C.Element is i32\npublic struct Api\n    public computed item: i32\n        {restriction}get() -> S.C.Element => 1");
        Check(c, valid, accessor: true);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("private ", true)]
    public void NormalizedReceiverProjectionUsesItsOwnDomain(string restriction, bool valid)
    {
        var c = MinimalEmissionTest.Analyze($"contract C\n    associate Element\npublic struct Api\n    Self is C\n    associate C.Element is Api\n    public computed item: i32\n        {restriction}get(self: ref/Api.C.Element) -> i32 => 1");
        Check(c, valid, accessor: true);
    }

    [Theory]
    [InlineData("public", false)]
    [InlineData("internal", true)]
    public void RequirementAccessorSignaturesUseTheContractDomain(string access, bool valid)
    {
        var c = MinimalEmissionTest.Analyze($"contract C\n    associate Element\npublic struct S\n    Self is C\n    associate C.Element is i32\n{access} contract Api\n    property item: i32\n        get() -> S.C.Element");
        Check(c, valid, accessor: true);
    }

    [Theory]
    [InlineData("public struct Api\n    private let item: S.C.Element")]
    [InlineData("struct Api\n    public let item: S.C.Element\n        private get")]
    [InlineData("public struct Api\n    public computed item: i32\n        get() -> i32\n            let value: S.C.Element = 1\n            return value")]
    [InlineData("public group Api\n    public let item: i32 = 1@S.C.Element")]
    public void PrivateDomainsAndImplementationUsesRemainValid(string declaration)
    {
        var c = MinimalEmissionTest.Analyze($"contract C\n    associate Element\npublic struct S\n    Self is C\n    associate C.Element is i32\n{declaration}");
        Check(c, true, accessor: false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidProjectionSignaturesCannotPublishConformanceWitnesses(bool inAccessor)
    {
        var header = inAccessor ? "i32" : "S.C.Element";
        var result = inAccessor ? "S.C.Element" : "i32";
        var c = MinimalEmissionTest.Analyze($"contract C\n    associate Element\npublic struct S\n    Self is C\n    associate C.Element is i32\npublic contract Required\n    property item: i32 has get\npublic struct Api\n    Self is Required\n    public computed item: {header}\n        get() -> {result} => 1");
        Check(c, false, inAccessor);
        var api = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Api");
        var required = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Required");
        var conformance = c.Binding.GetConformanceDefinition(api.BoundType!, required.BoundSymbol!);
        Assert.NotNull(conformance);
        Assert.False(conformance.IsVerified);
    }

    [Theory]
    [InlineData(false, "public", false)]
    [InlineData(false, "private", true)]
    [InlineData(true, "public", false)]
    [InlineData(true, "private", true)]
    public void RebindingAndReloadRecheckProjectionDomains(bool inAccessor, string access, bool valid)
    {
        var declaration = inAccessor
            ? $"public computed item: i32\n        {(access == "public" ? string.Empty : access + " ")}get() -> S.C.Element => 1"
            : $"{access} let item: S.C.Element";
        var c = MinimalEmissionTest.Analyze($"contract C\n    associate Element\npublic struct S\n    Self is C\n    associate C.Element is i32\npublic struct Api\n    {declaration}");
        Check(c, valid, inAccessor);
        Assert.Equal(valid, c.Bind().IsComplete);
        var bytes = Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var kotonoha = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(bytes, ref kotonoha);
        Assert.NotNull(kotonoha);
        kotonoha.OnDeserialized(restored);
        Assert.Equal(valid, restored.Bind().IsComplete);
        Assert.Equal(valid, Property(restored).BoundSymbol!.Property!.IsVerified);
        Assert.Equal(c.Binding.Issues.Select(x => x.Code), restored.Binding.Issues.Select(x => x.Code));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacementClearsAndRestoresPropertyVerification(bool inAccessor)
    {
        const string prefix = "contract C\n    associate Element\npublic struct S\n    Self is C\n    associate C.Element is i32\npublic struct Api\n    ";
        var c = MinimalEmissionTest.Analyze(prefix + (inAccessor ? "public computed item: i32\n        get() -> S.C.Element => 1" : "public let item: S.C.Element"));
        Check(c, false, inAccessor);
        var property = Property(c);
        var original = inAccessor ? (Koto)property.GetAccessor(PropertyAccessorKind.Get)! : property.TypeKoto!;
        var valid = MinimalEmissionTest.Analyze(prefix + (inAccessor ? "public computed item: i32\n        get() -> i32 => 1" : "public let item: i32"));
        var replacement = inAccessor ? (Koto)Property(valid).GetAccessor(PropertyAccessorKind.Get)! : Property(valid).TypeKoto!;
        Assert.True(KotoHelper.Replace(property, original, replacement));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(property.BoundSymbol!.Property!.IsVerified);
        Assert.True(KotoHelper.Replace(property, replacement, original));
        Assert.False(c.Bind().IsComplete);
        Assert.False(property.BoundSymbol.Property.IsVerified);
    }

    [Fact]
    public void WarmPropertyProjectionChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("public contract C\n    associate Element\npublic struct S\n    Self is C\n    associate C.Element is i32\npublic struct Api\n    public computed item: S.C.Element\n        get() -> S.Element => 1\n        set(value: S.C.Element) -> () => ()");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Property projection API access Binding failed.");
            }
        }));
    }

    private static void Check(Compilation c, bool valid, bool accessor)
    {
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid, Property(c).BoundSymbol!.Property!.IsVerified);
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Access &&
                (accessor ? x.Node is PropertyAccessorKoto : x.Node is PropertyKoto));
            Assert.False(c.Emission.Validate(out _));
        }
    }

    private static PropertyKoto Property(Compilation c)
        => Assert.Single(c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Api").Members.OfType<PropertyKoto>());
}
