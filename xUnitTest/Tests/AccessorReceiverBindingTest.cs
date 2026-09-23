// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class AccessorReceiverBindingTest
{
    [Theory]
    [InlineData("struct Hidden\npublic struct Api\n    public computed item: i32\n        get(self: ref/Hidden) -> i32 => 1")]
    [InlineData("struct Api\n    computed item: i32\n        get(self: ref/(ref/Self during static)) -> i32 => 1")]
    [InlineData("struct Api\n    computed item: i32\n        get(self: unsafe/Self) -> i32 => 1")]
    [InlineData("struct Hidden\npublic contract Api\n    property item: i32\n        get(self: ref/Hidden) -> i32")]
    [InlineData("group Api\n    computed item: i32\n        get(self: i32) -> i32 => 1")]
    [InlineData("public struct Other\npublic struct Api\n    public computed item: i32\n        get() -> i32 => 1\n        private set(self: uniq/Other, value: i32) -> () => ()")]
    [InlineData("struct Api<T>\n    computed item: i32\n        get(self: T) -> i32 => 1")]
    [InlineData("struct Api\n    computed item: i32\n        get(self: i32) -> i32 => 1")]
    [InlineData("contract Api\n    property item: i32\n        get() -> i32\n        set(self: unsafe/Self, value: i32) -> ()")]
    [InlineData("group Api\n    var item: i32 = 0\n        set(self: i32, value: i32) -> () => storage = value")]
    public void InvalidReceiversFailAtTheUnusedDeclaration(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var issue = Assert.Single(c.Binding.Issues, x => x.Node is PropertyAccessorKoto && x.Node.BindingFailure == BindingFailure.InvalidTypeFormation);
        var accessor = (PropertyAccessorKoto)issue.Node;
        Assert.NotNull(accessor.ReceiverType!.BoundType);
        Assert.False(((PropertyKoto)accessor.Parent!).BoundSymbol!.Property!.IsVerified);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("Self")]
    [InlineData("ref/Self")]
    [InlineData("uniq/Self")]
    [InlineData("obj/Self")]
    [InlineData("rc/Self")]
    [InlineData("arc/Self")]
    [InlineData("objref/Self")]
    [InlineData("objuniq/Self")]
    public void ComputedAccessorsRetainOrdinaryFunctionReceiverForms(string receiver)
    {
        var c = MinimalEmissionTest.Analyze($"public struct Api<T>\n    public computed item: i32\n        get(self: {receiver}) -> i32 => 1\n        set(self: {receiver}, value: i32) -> () => ()\n    public func read(self: {receiver}) -> i32 => 1");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(Property(c).BoundSymbol!.Property!.IsVerified);
    }

    [Theory]
    [InlineData("struct Api\n    var item: i32\n        get() -> i32 => storage\n        set(value: i32) -> () => storage = value")]
    [InlineData("public contract Api\n    property item: i32\n        get(self: Self) -> i32\n        set(self: uniq/Self, value: i32) -> ()")]
    [InlineData("public group Api\n    public computed item: i32\n        get() -> i32 => 1\n        set(value: i32) -> () => ()")]
    [InlineData("public group Api\n    public var item: i32 = 0\n        get() -> i32 => storage\n        set(value: i32) -> () => storage = value")]
    [InlineData("struct Api<T> {source}\n    public computed item: i32\n        get(self: ref/Self during static) -> i32 => 1")]
    public void ValidShorthandStaticAndRequirementReceiversRemainSupported(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(Property(c).BoundSymbol!.Property!.IsVerified);
    }

    [Theory]
    [InlineData("public", false)]
    [InlineData("private", true)]
    public void AccessorInputExposureStillUsesItsOwnDomain(string access, bool valid)
    {
        var restriction = access == "public" ? string.Empty : access + " ";
        var c = MinimalEmissionTest.Analyze($"struct Hidden\npublic struct Api\n    public computed item: i32\n        get() -> i32 => 1\n        {restriction}set(self: Self, value: Hidden) -> () => ()");
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        if (!valid)
        {
            Assert.Equal(BindingFailure.Access, Property(c).BoundSymbol!.Property!.Setter.Declaration!.BindingFailure);
        }
    }

    [Theory]
    [InlineData("ref/Self", true)]
    [InlineData("ref/Other", false)]
    [InlineData("unsafe/Self", false)]
    public void RebindingAndReloadPreserveReceiverValidation(string receiver, bool valid)
    {
        var c = MinimalEmissionTest.Analyze($"struct Other\nstruct Api\n    computed item: i32\n        get(self: {receiver}) -> i32 => 1");
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(valid, c.Bind().IsComplete);
        var bytes = Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var kotonoha = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(bytes, ref kotonoha);
        Assert.NotNull(kotonoha);
        kotonoha.OnDeserialized(restored);
        Assert.Equal(valid, restored.Bind().IsComplete);
        Assert.Equal(c.Binding.Issues.Select(x => x.Code), restored.Binding.Issues.Select(x => x.Code));
    }

    [Fact]
    public void ReplacingAStaticAccessorClearsAndRestoresItsReceiverFailure()
    {
        var c = MinimalEmissionTest.Analyze("group Api\n    computed item: i32\n        get(self: i32) -> i32 => 1");
        Assert.False(c.Binding.Result.IsComplete);
        var property = Property(c);
        var original = property.GetAccessor(PropertyAccessorKind.Get)!;
        var valid = MinimalEmissionTest.Analyze("group Api\n    computed item: i32\n        get() -> i32 => 1");
        var replacement = Property(valid).GetAccessor(PropertyAccessorKind.Get)!;
        Assert.True(KotoHelper.Replace(property, original, replacement));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Null(property.BoundSymbol!.Property!.Getter.Receiver);
        Assert.True(property.BoundSymbol.Property.IsVerified);
        Assert.True(KotoHelper.Replace(property, replacement, original));
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(BindingFailure.InvalidTypeFormation, original.BindingFailure);
        Assert.False(property.BoundSymbol.Property.IsVerified);
    }

    [Fact]
    public void WarmAccessorReceiverChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("public struct Api<T>\n    public computed item: i32\n        get() -> i32 => 1\n        set(self: Self, value: i32) -> () => ()\n    public func read(self) -> i32 => 1");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Accessor receiver Binding failed.");
            }
        }));
    }

    private static PropertyKoto Property(Compilation c)
        => Assert.Single(c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Api").Members.OfType<PropertyKoto>());
}
