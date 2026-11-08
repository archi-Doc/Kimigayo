// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ApiAccessBindingTest
{
    [Theory]
    [InlineData("struct Hidden\npublic group Api\n    public func expose(value: Hidden) => ()")]
    [InlineData("struct Hidden\npublic group Api\n    public func expose() -> Hidden => loop => ()")]
    [InlineData("enum Hidden\n    A\npublic group Api\n    private func get() -> Hidden => .A\n    public func expose() -> Hidden => get()")]
    [InlineData("struct Hidden\npublic group Api\n    public func expose(value: ref/Hidden) => ()")]
    [InlineData("struct Hidden\npublic group Api\n    public func expose(value: (i32, [1 of Hidden])) => ()")]
    [InlineData("struct Hidden\npublic struct Box<T>\npublic group Api\n    public func expose(value: Box<Hidden>) => ()")]
    [InlineData("struct Hidden\npublic group Api\n    public func expose(value: (Hidden) -> ()) => ()")]
    [InlineData("contract Hidden\npublic group Api\n    public func expose<T>(value: T)\n        T is Hidden\n        ()")]
    [InlineData("struct Hidden\npublic group Api\n    public func expose<T>(value: T)\n        T is Hidden\n        ()")]
    [InlineData("public group Api\n    private struct Hidden\n    internal func expose(value: Hidden) => ()")]
    [InlineData("internal struct Hidden\npublic open struct Base\n    protected func expose(value: Hidden) => ()")]
    [InlineData("public group Api\n    private struct Hidden\n    public open struct Base\n        protected func expose(value: Hidden) => ()")]
    [InlineData("public group Api\n    private struct Hidden\n    public open struct Base\n        private protected func expose(value: Hidden) => ()")]
    public void PublicFunctionApisCannotExposeRestrictedTypes(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.FailureOf() == BindingFailure.Access);
        Assert.False(c.Generate(TextWriter.Null));
    }

    [Theory]
    [InlineData("struct Hidden\nfunc use(value: Hidden) => ()")]
    [InlineData("struct Hidden\ninternal func use(value: Hidden) => ()")]
    [InlineData("struct Hidden\ngroup Api\n    public func use(value: Hidden) => ()")]
    [InlineData("public group Api\n    private struct Hidden\n    private func use(value: Hidden) => ()")]
    [InlineData("public group Api\n    private struct Hidden\n    private group Inner\n        public func use(value: Hidden) => ()")]
    [InlineData("public func identity<T>(value: T) -> T => value@move")]
    [InlineData("public group Api\n    public func identity<T>(value: T) -> T => value@move")]
    [InlineData("enum Hidden\n    A\nfunc get() -> Hidden => .A\npublic func discard() => get()")]
    [InlineData("internal struct Hidden\npublic open struct Base\n    private protected func use(value: Hidden) => ()")]
    [InlineData("internal struct Hidden\ninternal open struct Base\n    protected func use(value: Hidden) => ()")]
    [InlineData("struct Hidden\npublic open struct Base\n    private func use(value: Hidden) => ()")]
    [InlineData("public group Api\n    private struct Hidden\n    public open struct Base\n        private func use(value: Hidden) => ()")]
    public void EffectiveContainersAndSymbolicParametersKeepValidApis(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
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
            Assert.Contains(c.Binding.Issues, x => x.Node.FailureOf() == BindingFailure.Access);
        }
    }

    [Theory]
    [InlineData("public", true)]
    [InlineData("internal", false)]
    public void ReloadRetainsDeclarationAccess(string access, bool valid)
    {
        var c = MinimalEmissionTest.Analyze($"{access} struct Value\npublic group Api\n    public func use(value: Value) => ()");
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        var restored = CompilationTestHelper.Reload(c);
        Assert.Equal(valid, restored.Bind().IsComplete);
        Assert.Equal(c.Binding.Issues.Select(x => x.Code), restored.Binding.Issues.Select(x => x.Code));
    }

    [Fact]
    public void RebindingRechecksReplacedFunctionDomains()
    {
        var c = MinimalEmissionTest.Analyze("internal struct Value\npublic group Api\n    public func use(value: Value) => ()");
        Assert.False(c.Binding.Result.IsComplete);
        var group = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Api");
        var original = Assert.Single(group.Members.OfType<FunctionKoto>());
        var replacementSource = MinimalEmissionTest.Analyze("internal struct Value\npublic group Api\n    internal func use(value: Value) => ()");
        var replacement = Assert.Single(replacementSource.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Api").Members.OfType<FunctionKoto>());
        ParseTestHelper.Detach(replacement, new CodeBlockKoto(replacement.CodeContext));
        Assert.True(KotoHelper.Replace(group, original, replacement));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
    }

    // SPEC 9.4.1, 9.4.2 (R7-N3a): a protected level is a union with Derived(C), so a domain inside a struct deriving from C, or one level
    // of the domain's chain with every atom inside the member's union, covers it: p2, ra3, rx1c, w1, w6-w8, rg1 were rejected before.
    [Theory]
    [InlineData("open struct X\n    protected open struct Y\n        public init() => ()\n    public init() => ()\nstruct D: X\n    public init() => ()\n    struct E: X.Y\n        public init() => ()")] // p2
    [InlineData("open struct X\n    protected struct Y\n        public func q(self) -> i32 => 1\n        public init() => ()\n    public init() => ()")] // ra3
    [InlineData("contract K<T>\ncontract HasBase\n    associate Parent\nstruct Source\n    Self is HasBase\n    associate HasBase.Parent is Q\nopen struct Q: R\n    protected open struct N\n        public init() => ()\n    public init() => ()\nopen struct R\n    private open struct N\n        public func far(self) -> i32 => 2\n        public init() => ()\n    public init() => ()\n    struct T: Source.HasBase.Parent\n        public init(): base() => ()\n        func h<X>(self, x: ref/X) -> i32\n            X is K<Q.N>\n            return 1")] // rx1c
    [InlineData("public open struct S\n    protected struct Y\n        public init() => ()\n    protected func make() -> Y => Y.init()\n    public init() => ()")] // w1
    [InlineData("public struct S\n    private struct Y\n        public init() => ()\n    protected func make() -> Y => Y.init()\n    public init() => ()")] // w6
    [InlineData("public open struct C\n    protected struct Y\n        public init() => ()\n    public init() => ()\npublic open struct S: C\n    public init(): base() => ()\n    protected func make() -> C.Y => C.Y.init()")] // w7
    [InlineData("public open struct S\n    private protected struct Y\n        public init() => ()\n    private protected func make() -> Y => Y.init()\n    public init() => ()")] // w8
    [InlineData("open struct B<T>\n    protected struct N<U>\n        public init() => ()\n    public func m(self) -> i32 => 1\n    public init() => ()\nopen struct D<T>: B<T>\n    public init(): base() => ()\nstruct E<T>: D<B<T>>\n    public init(): base() => ()\n    protected func g(self, n: E<T>.N<i8>) -> i32 => self.m()")] // rg1
    public void DerivedDomainsCoverProtectedLevels(string source)
    {
        var c = MinimalEmissionTest.Analyze(source, output: OutputKind.Library);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    // SPEC 9.4.2, 9.4.6, 6.2.2 (R7-N3a): a domain no level of which lies in the member's union stays uncovered: the invalid twins keep
    // their record (w10, rx1d: a conformance's domain, the intersection of Access(T) and Access(C), is not inside the union of Q's
    // protected N).
    [Theory]
    [InlineData("public open struct S\n    private struct Y\n        public init() => ()\n    protected func make() -> Y => Y.init()\n    public init() => ()", "InaccessibleBinding_Kd", 4)] // w1c
    [InlineData("open struct X\n    protected struct Y\n        public init() => ()\n    public init() => ()\nstruct D: X\n    public init(): base() => ()\n    public func make() -> X.Y => X.Y.init()", "InaccessibleBinding_Kd", 7)] // w2c
    [InlineData("open struct X\n    protected open struct Y\n        public init() => ()\n    public init() => ()\nstruct D: X\n    public init(): base() => ()\n    public struct E: X.Y\n        public init() => ()", "InvalidTypeFormation_Kd", 7)] // w3
    [InlineData("public struct S\n    private struct Y\n        public init() => ()\n    public func make() -> Y => Y.init()\n    public init() => ()", "InaccessibleBinding_Kd", 4)] // w6c
    [InlineData("public open struct C\n    protected struct Y\n        public init() => ()\n    public init() => ()\npublic open struct S: C\n    public init(): base() => ()\n    protected internal func make() -> C.Y => C.Y.init()", "InaccessibleBinding_Kd", 7)] // w7c
    [InlineData("internal struct Secret\n    public init() => ()\npublic open struct S\n    protected func make() -> Secret => Secret.init()\n    public init() => ()", "InaccessibleBinding_Kd", 4)] // w8c
    [InlineData("contract K<T>\nopen struct Q\n    protected open struct N\n        public init() => ()\n    public init() => ()\nstruct T2\n    public init() => ()\n    func h<X>(self, x: ref/X) -> i32\n        X is K<Q.N>\n        return 1", "InvalidConstraint_Kd", 8)] // w9c
    [InlineData("contract HasItem\n    associate Item\nopen struct Q\n    protected open struct N\n        public init() => ()\n    public init() => ()\nstruct T: Q\n    Self is HasItem\n    associate HasItem.Item is Q.N\n    public init(): base() => ()", "InaccessibleBinding_Kd", 8)] // w10
    [InlineData("contract HasItem\n    associate Item\ncontract HasBase\n    associate Parent\nstruct Source\n    Self is HasBase\n    associate HasBase.Parent is Q\nopen struct Q: R\n    protected open struct N\n        public init() => ()\n    public init() => ()\nopen struct R\n    private open struct N\n        public func far(self) -> i32 => 2\n        public init() => ()\n    public init() => ()\n    struct T: Source.HasBase.Parent\n        Self is HasItem\n        associate HasItem.Item is Q.N\n        public init(): base() => ()", "InaccessibleBinding_Kd", 18)] // rx1d
    public void UncoveredDomainsKeepTheirRecords(string source, string code, int line)
    {
        var c = MinimalEmissionTest.Analyze(source, output: OutputKind.Library);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code.ToString() == code && source.AsSpan(0, x.Node.Span.Start).Count('\n') + 1 == line);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmApiAccessChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("public struct Box<T>\n    T is Copy\npublic group Api\n    public func use<T>(value: (Box<T>, T))\n        T is Copy\n        ()");
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("API accessibility Binding failed.");
            }
        }));
    }
}
