// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class PropertyApiAccessBindingTest
{
    [Theory]
    [InlineData("struct Hidden\npublic struct Api\n    public let item: Hidden\n        private get")]
    [InlineData("struct Hidden\npublic struct Api\n    public var item: Hidden\n        private get\n        private set")]
    [InlineData("struct Hidden\npublic struct Api\n    public computed item: Hidden\n        private get(self: ref/Self) -> Hidden => loop => ()")]
    [InlineData("enum Hidden\n    A\npublic group Api\n    public let item = Hidden.A\n        private get")]
    [InlineData("struct Hidden\npublic struct Api\n    public let item: (i32, [1 of Hidden])\n        private get")]
    [InlineData("struct Hidden\npublic struct Box<T>\npublic struct Api\n    public let item: Box<Hidden>\n        private get")]
    [InlineData("struct Hidden\npublic struct Api\n    public let item: (Hidden) -> ()\n        private get")]
    [InlineData("internal struct Hidden\npublic open struct Api\n    protected let item: Hidden\n        private get")]
    [InlineData("public group Api\n    private struct Hidden\n    internal let item: Hidden\n        private get")]
    public void RestrictedAccessorsDoNotNarrowThePropertyTypeDomain(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var property = Assert.Single(c.Binding.Issues, x => x.Node is PropertyKoto && x.Node.BindingFailure == BindingFailure.Access);
        Assert.False(((PropertyKoto)property.Node).BoundSymbol!.Property!.IsVerified);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("struct Hidden\nstruct Api\n    public let item: Hidden\n        private get")]
    [InlineData("struct Hidden\npublic struct Api\n    private let item: Hidden")]
    [InlineData("internal struct Hidden\npublic open struct Api\n    private protected let item: Hidden\n        private get")]
    [InlineData("internal struct Hidden\ninternal open struct Api\n    protected let item: Hidden\n        private get")]
    [InlineData("public group Api\n    private struct Hidden\n    private group Inner\n        public let item: Hidden\n            private get")]
    [InlineData("public struct Api<T>\n    public let item: T\n        private get")]
    [InlineData("public struct Value\n    private let storage: i32\npublic struct Api\n    public let item: Value\n        private get")]
    [InlineData("enum Hidden\n    A\ngroup Api\n    public let item = Hidden.A\n        private get")]
    public void EffectivePropertyDomainsPreserveValidHeaders(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("public", false)]
    [InlineData("internal", true)]
    public void RequirementHeadersUseTheContractDomain(string access, bool valid)
    {
        var c = MinimalEmissionTest.Analyze($"internal struct Hidden\n{access} contract Api\n    property item: Hidden has get");
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        var property = Assert.Single(c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Api").Members.OfType<PropertyKoto>());
        Assert.Equal(valid, property.BoundSymbol!.Property!.IsVerified);
        if (!valid)
        {
            Assert.Equal(BindingFailure.Access, property.BindingFailure);
        }
    }

    [Theory]
    [InlineData("struct Hidden\npublic enum Api\n    Item((i32, [1 of Hidden]))", false)]
    [InlineData("struct Hidden\nenum Api\n    Item((i32, [1 of Hidden]))", true)]
    [InlineData("struct Hidden\npublic struct Box<T>\npublic enum Api\n    Item(Box<Hidden>)", false)]
    [InlineData("public enum Api<T>\n    Item(T)", true)]
    public void EnumPayloadsContinueToUseTheEnumDomain(string source, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Access);
        }
    }

    [Theory]
    [InlineData("public", false)]
    [InlineData("internal", true)]
    [InlineData("private", true)]
    public void RebindingAndReloadRecheckThePropertyDomain(string access, bool valid)
    {
        var restriction = access == "private" ? string.Empty : "\n        private get";
        var c = MinimalEmissionTest.Analyze($"internal struct Hidden\npublic struct Api\n    {access} let item: Hidden{restriction}");
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
    public void ReplacementClearsAndRestoresPropertyAccessFailures()
    {
        var c = MinimalEmissionTest.Analyze("struct Hidden\npublic struct Api\n    public let item: Hidden\n        private get");
        Assert.False(c.Binding.Result.IsComplete);
        var container = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Api");
        var original = Assert.Single(container.Members.OfType<PropertyKoto>());
        var valid = MinimalEmissionTest.Analyze("struct Hidden\npublic struct Api\n    private let item: Hidden");
        var replacement = Assert.Single(valid.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Api").Members.OfType<PropertyKoto>());
        Assert.True(KotoHelper.Replace(container, original, replacement));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(replacement.BoundSymbol!.Property!.IsVerified);
        Assert.True(KotoHelper.Replace(container, replacement, original));
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(BindingFailure.Access, original.BindingFailure);
        Assert.False(original.BoundSymbol!.Property!.IsVerified);
    }

    [Fact]
    public void WarmPropertyHeaderAccessChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("public struct Api<T>\n    public let item: T\n        private get\npublic enum Choice<T>\n    Item(T)");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Property header API accessibility Binding failed.");
            }
        }));
    }
}
