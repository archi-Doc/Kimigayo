// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ApiAccessBindingTest
{
    [Theory]
    [InlineData("struct Hidden\npublic group Api\n    public func expose(value?: Hidden) => ()")]
    [InlineData("struct Hidden\npublic group Api\n    public func expose() -> Hidden => loop => ()")]
    [InlineData("enum Hidden\n    A\npublic group Api\n    private func get() -> Hidden => .A\n    public func expose() -> Hidden => get()")]
    [InlineData("struct Hidden\npublic group Api\n    public func expose(value?: ref/Hidden) => ()")]
    [InlineData("struct Hidden\npublic group Api\n    public func expose(value?: (i32, [1 of Hidden])) => ()")]
    [InlineData("struct Hidden\npublic struct Box<T>\npublic group Api\n    public func expose(value?: Box<Hidden>) => ()")]
    [InlineData("struct Hidden\npublic group Api\n    public func expose(value?: (Hidden) -> ()) => ()")]
    [InlineData("contract Hidden\npublic group Api\n    public func expose<T>(value?: T)\n        T is Hidden\n        ()")]
    [InlineData("struct Hidden\npublic group Api\n    public func expose<T>(value?: T)\n        T is Hidden\n        ()")]
    [InlineData("public group Api\n    private struct Hidden\n    internal func expose(value?: Hidden) => ()")]
    [InlineData("internal struct Hidden\npublic open struct Base\n    protected func expose(value?: Hidden) => ()")]
    [InlineData("public group Api\n    private struct Hidden\n    public open struct Base\n        protected func expose(value?: Hidden) => ()")]
    [InlineData("public group Api\n    private struct Hidden\n    public open struct Base\n        private protected func expose(value?: Hidden) => ()")]
    public void PublicFunctionApisCannotExposeRestrictedTypes(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Access);
        Assert.False(c.Emission.Validate(out _));
    }

    [Theory]
    [InlineData("struct Hidden\nfunc use(value?: Hidden) => ()")]
    [InlineData("struct Hidden\ninternal func use(value?: Hidden) => ()")]
    [InlineData("struct Hidden\ngroup Api\n    public func use(value?: Hidden) => ()")]
    [InlineData("public group Api\n    private struct Hidden\n    private func use(value?: Hidden) => ()")]
    [InlineData("public group Api\n    private struct Hidden\n    private group Inner\n        public func use(value?: Hidden) => ()")]
    [InlineData("public func identity<T>(value?: T) -> T => value")]
    [InlineData("public group Api\n    public func identity<T>(value?: T) -> T => value")]
    [InlineData("enum Hidden\n    A\nfunc get() -> Hidden => .A\npublic func discard() => get()")]
    [InlineData("internal struct Hidden\npublic open struct Base\n    private protected func use(value?: Hidden) => ()")]
    [InlineData("internal struct Hidden\ninternal open struct Base\n    protected func use(value?: Hidden) => ()")]
    [InlineData("struct Hidden\npublic open struct Base\n    private func use(value?: Hidden) => ()")]
    [InlineData("public group Api\n    private struct Hidden\n    public open struct Base\n        private func use(value?: Hidden) => ()")]
    public void EffectiveContainersAndSymbolicParametersKeepValidApis(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("contract Hidden\npublic struct Api<T>\n    T is Hidden", false)]
    [InlineData("struct Hidden\npublic struct Api<T>\n    T is Hidden", false)]
    [InlineData("contract Hidden\npublic enum Api<T>\n    T is Hidden\n    Empty", false)]
    [InlineData("contract Hidden\nstruct Api<T>\n    T is Hidden", true)]
    [InlineData("contract Hidden\npublic struct Api\n    Self is Hidden", true)]
    [InlineData("public struct Api<T>\n    T is Copy", true)]
    public void GenericDeclarationConstraintsUseTheDeclarationDomain(string source, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Access);
        }
    }

    [Theory]
    [InlineData("public", true)]
    [InlineData("internal", false)]
    public void ReloadRetainsDeclarationAccess(string access, bool valid)
    {
        var c = MinimalEmissionTest.Analyze($"{access} struct Value\npublic group Api\n    public func use(value?: Value) => ()");
        Assert.Equal(valid, c.Binding.Result.IsComplete);
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
    public void RebindingRechecksReplacedFunctionDomains()
    {
        var c = MinimalEmissionTest.Analyze("internal struct Value\npublic group Api\n    public func use(value?: Value) => ()");
        Assert.False(c.Binding.Result.IsComplete);
        var group = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Api");
        var original = Assert.Single(group.Members.OfType<FunctionKoto>());
        var replacementSource = MinimalEmissionTest.Analyze("internal struct Value\npublic group Api\n    internal func use(value?: Value) => ()");
        var replacement = Assert.Single(replacementSource.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Api").Members.OfType<FunctionKoto>());
        Assert.True(KotoHelper.Replace(group, original, replacement));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void WarmApiAccessChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("public struct Box<T>\n    T is Copy\npublic group Api\n    public func use<T>(value?: (Box<T>, T))\n        T is Copy\n        ()");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("API accessibility Binding failed.");
            }
        }));
    }
}
