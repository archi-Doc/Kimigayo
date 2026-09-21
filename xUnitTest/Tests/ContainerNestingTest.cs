// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class ContainerNestingTest
{
    [Theory]
    [InlineData("struct Outer<T> {source}\n    public group Helpers\n        public func identity(value: T) -> T => value\nfunc f() -> i32\n    let result = (Outer<i32>.Helpers{h}).identity(42)\n        origin h.source == static\n    return result")]
    [InlineData("alias H => (Outer<i32>.Helpers{h})\n    origin h.source == static\nstruct Outer<T> {source}\n    public group Helpers\n        public func identity(value: T) -> T => value\nlet x = H.identity(42)")]
    [InlineData("contract C\nstruct Outer<T>\n    public struct Inner {}\n        Self is C when T is Copy\nfunc accept<T>(value: T)\n    T is C\n    ()\naccept(Outer<i32>.Inner.init())")]
    [InlineData("struct Outer {}\n    private struct Hidden {}\n    public struct Inner {}\n        private func use(value: Hidden) => ()")]
    [InlineData("open struct Outer {}\n    protected struct Inner {}")]
    [InlineData("struct Outer<T> {}\n    T is Owned\n    public group Storage\n        var value: T")]
    [InlineData("struct Outer<T> {}\n    public struct Cell {a}\n        let value: ref{a}/T\n        public init(value: ref{a}/T) => self.value = value\nfunc f(value: ref{x}/i32)\n    let cell = (Outer<i32>.Cell{c}).init(value)\n        origin c.a == x")]
    [InlineData("struct Outer<T> {a}\n    public struct Cell {}\n        let value: ref{a}/T\n        public init(value: ref{a}/T) => self.value = value\nfunc f(value: ref{x}/i32)\n    let cell = (Outer<i32>{o}).Cell.init(value)\n        origin o.a == x")]
    [InlineData("struct Outer {}\n    public group G\n        public struct Inner {}\n            public enum E\n                A\n            public contract C")]
    [InlineData("struct Outer {}\n    group Helpers\n        func identity(value: Self) -> Self => value")]
    [InlineData("struct Outer<T> {}\n    public struct Inner<U> {}\n        var first: T\n        var second: U\nfunc use(x: Outer<i32>.Inner<string>) => ()")]
    [InlineData("struct Outer<T> {}\n    public group Helpers\n        public struct Tag {}\nfunc use(x: Outer<i32>.Helpers.Tag) => ()")]
    [InlineData("struct Outer<T> {}\n    public struct Tag {}\n    func use(x: Tag) => ()")]
    [InlineData("struct Outer<T> {}\n    public struct Tag {}\nstruct Outer<T> {}\n    public struct Tag {}\n        var count: i32 = 0")]
    [InlineData("struct Outer<T> {}\n    public contract C\n        func read(value: T) -> T")]
    [InlineData("struct View<T> {source}\n    public struct Tag {}\nfunc f<T>(value: View<T>.Tag{v}) => ()")]
    [InlineData("struct View<T> {source}\n    public struct Tag {local}\nfunc f<T>(value: View<T>.Tag{v}) => ()")]
    [InlineData("open struct Base<T> {}\n    public struct Node {}\nstruct Derived: Base<i32>\nfunc f(value: Derived.Node) => ()")]
    [InlineData("alias Outer<i32>.Helpers\nstruct Outer<T>\n    public group Helpers\n        public struct Tag {}\nfunc f(value: Tag) => ()")]
    [InlineData("alias H => Outer<i32>.Helpers\nstruct Outer<T>\n    public group Helpers\n        public struct Tag {}\nfunc f(value: H.Tag) => ()")]
    [InlineData("alias Outer<i32>.Helpers\nalias Outer<i32>.Helpers\nstruct Outer<T>\n    public group Helpers\n        public struct Tag {}\nfunc f(value: Tag) => ()")]
    [InlineData("struct Family<T> {}\n    public contract Marker\nstruct Good\n    Self is Family<i32>.Marker\n    Self is Family<string>.Marker")]
    [InlineData("struct Family<T> {}\n    public contract Sink\n        func write(value: T)\nstruct Writer\n    Self is Family<i32>.Sink\n    public func write(value: i32) => ()")]
    [InlineData("struct View<T> {source}\n    public struct Tag {}\nfunc f<T>(value: (View<T>{v}).Tag) => ()")]
    [InlineData("struct View<T> {source}\n    public struct Tag {local}\nfunc f<T>(value: (View<T>{v}).Tag{t}) => ()")]
    [InlineData("struct Outer<T> {}\n    public struct Tag {}\nfunc f(value: ::Outer<i32>.Tag) => ()")]
    [InlineData("struct Family<T> {}\n    public contract Marker\nstruct S<T>\n    Self is Family<T>.Marker\nfunc accept<T>(x: T)\n    T is Family<i32>.Marker\n    ()\naccept(S<i32>.init())")]
    public void BindsNestedDeclarations(string source)
    {
        var c = Parse(source);
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Theory]
    [InlineData("struct Outer<T> {source}\n    public group Helpers\n        public func identity(value: T) -> T => value\nlet x = Outer<i32>.Helpers.identity(42)")]
    [InlineData("struct Outer<T> {}\n    public group Storage\n        var value: T")]
    [InlineData("struct Outer<T> {source}\n    public struct Tag {}\n    public group Storage\n        var value: Tag")]
    [InlineData("struct Family<T> {}\n    T is Copy\n    public contract Marker\nstruct NonCopy\nstruct Bad\n    Self is Family<NonCopy>.Marker")]
    [InlineData("struct Family<T> {}\n    T is Copy\n    public group Helpers\n        public func f() => ()\nstruct NonCopy\nFamily<NonCopy>.Helpers.f()")]
    [InlineData("struct Outer {}\n    public struct Inner {}\n        private struct Hidden {}\n    func use(value: Inner.Hidden) => ()")]
    [InlineData("struct Outer {}\n    group Helpers\n        protected struct Hidden {}")]
    [InlineData("contract C\nstruct Outer<T>\n    Self is C when T is Copy\n        struct Inner {}")]
    [InlineData("contract C\nstruct NonCopy\nstruct Outer<T>\n    public struct Inner {}\n        Self is C when T is Copy\nfunc accept<T>(value: T)\n    T is C\n    ()\naccept(Outer<NonCopy>.Inner.init())")]
    [InlineData("struct Outer<T> {}\n    public struct Tag {source}\nfunc f()\n    let value = (Outer<i32>.Tag{v})")]
    [InlineData("enum Outer\n    A\n    struct Inner {}")]
    [InlineData("contract Outer\n    struct Inner {}")]
    [InlineData("func f()\n    struct Inner {}")]
    [InlineData("struct Outer {}\n    rootgroup Inner")]
    [InlineData("group Outer\n    rootgroup Inner")]
    [InlineData("struct Outer<T> {}\n    struct T {}")]
    [InlineData("struct Outer<T> {}\n    public struct Tag {}\nfunc use(x: Outer.Tag) => ()")]
    [InlineData("struct Outer {source}\n    struct Inner {source}")]
    [InlineData("struct Outer {}\n    group G\n        Self is Copy")]
    [InlineData("open struct Base<T> {}\n    public struct Node {}\nstruct Derived: Base<i32>\n    public struct Node<U> {}")]
    [InlineData("struct Outer<T> {}\n    T is Copy\n    public struct Tag {}\nstruct NonCopy\nfunc f(value: Outer<NonCopy>.Tag) => ()")]
    [InlineData("struct Outer<T> {}\n    public group Helpers\n        public func echo(value: T) -> T => value\nlet result = Outer.Helpers.echo(1)")]
    [InlineData("alias Outer<i32>.Helpers\nalias Outer<string>.Helpers\nstruct Outer<T>\n    public group Helpers\n        public struct Tag {}\nfunc f(value: Tag) => ()")]
    [InlineData("alias H => Outer<i32>.Helpers\nalias H => Outer<string>.Helpers\nstruct Outer<T>\n    public group Helpers")]
    [InlineData("alias H => Outer<i32>.Helpers\nalias H.Tag\nstruct Outer<T>\n    public group Helpers\n        public struct Tag {}")]
    [InlineData("struct Family<T> {}\n    public contract Marker\nstruct Bad<A, B>\n    Self is Family<A>.Marker\n    Self is Family<B>.Marker")]
    [InlineData("struct Family<T> {}\n    public contract Marker\nstruct Bad\n    Self is Family<i32>.Marker\n    Self is Family<i32>.Marker")]
    [InlineData("struct Family<T> {}\n    public contract Sink\n        func write(value: T)\nstruct Writer\n    Self is Family<i32>.Sink\n    public func write(value: string) => ()")]
    [InlineData("struct View<T> {source}\n    public struct Tag {}\nfunc f<T>(value: (View<T>{source => a}).Tag{source => a}) => ()")]
    [InlineData("struct Family<T> {}\n    public contract Marker\nstruct S<T>\n    Self is Family<T>.Marker\nfunc accept<T>(x: T)\n    T is Family<i32>.Marker\n    ()\naccept(S<string>.init())")]
    public void RejectsInvalidPlacementAndUnboundEnvironment(string source)
    {
        var c = Parse(source);
        Assert.False(c.Bind().IsComplete && !c.Kotonoha.HasSourceErrors, Describe(c));
    }

    [Fact]
    public void UnusedOuterArgumentsRemainPartOfIdentity()
    {
        var c = Parse("struct Outer<T> {}\n    public struct Tag {}\nfunc a(x: Outer<i32>.Tag) => ()\nfunc b(x: Outer<i64>.Tag) => ()\nfunc same(x: Outer<i32>.Tag) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var functions = c.Kotonoha.GeneratedFunction!.Body!.Items.OfType<FunctionKoto>().ToArray();
        var first = functions[0].Parameters[0].Type.BoundType!;
        Assert.Same(first.Symbol, functions[1].Parameters[0].Type.BoundType!.Symbol);
        Assert.NotSame(first, functions[1].Parameters[0].Type.BoundType);
        Assert.Same(first, functions[2].Parameters[0].Type.BoundType);
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Same(first, functions[0].Parameters[0].Type.BoundType);
    }

    [Fact]
    public void NestedConstructionUsesOuterFieldSubstitution()
        => ScalarEmissionTest.EmitFixture("ContainerNestingFields", "struct Outer<T> {}\n    public struct Inner {}\n        public var value: T\n        public init(value: T) => self.value = value\nvar a = Outer<i32>.Inner.init(42)\nif a.value == 42 => Kimi.Console.writeLine(\"ok\")", "ok\n");

    [Fact]
    public void NestedGroupFunctionsRetainOuterBindings()
        => ScalarEmissionTest.EmitFixture("ContainerNestingGroup", "struct Outer<T> {}\n    public group Helpers\n        public func echo(value: T) -> T => value\nlet a = Outer<i32>.Helpers.echo(42)\nif a == 42 => Kimi.Console.writeLine(\"ok\")", "ok\n");

    [Fact]
    public void BoundAliasKeepsFunctionEnvironment()
        => ScalarEmissionTest.EmitFixture("ContainerNestingAlias", "alias H => Outer<i32>.Helpers\nstruct Outer<T>\n    public group Helpers\n        public func echo(value: T) -> T => value\nlet a = H.echo(42)\nif a == 42 => Kimi.Console.writeLine(\"ok\")", "ok\n");

    [Fact]
    public void NestedEnumConstructionRetainsOuterBindings()
        => ScalarEmissionTest.EmitFixture("ContainerNestingEnum", "struct Outer<T> {}\n    public enum Choice\n        Value(T)\nlet x = Outer<i32>.Choice.Value(42)\nmatch x\n    .Value(let value)\n        if value == 42 => Kimi.Console.writeLine(\"ok\")", "ok\n");

    [Fact]
    public void InheritedNestedReferenceKeepsDefiningIdentity()
    {
        var c = Parse("open struct Base<T> {}\n    public struct Node {}\nstruct Derived: Base<i32>\nfunc f(a: Base<i32>.Node, b: Derived.Node) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Assert.Single(c.Kotonoha.GeneratedFunction!.Body!.Items.OfType<FunctionKoto>());
        Assert.Same(f.Parameters[0].Type.BoundType, f.Parameters[1].Type.BoundType);
    }

    [Fact]
    public void EmptyNestedTypeRetainsUnusedBorrowDependency()
    {
        var c = Parse("struct Outer<T> {}\n    public struct Tag {}\nfunc f(x: Outer<ref{a}/i32>.Tag) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Assert.Single(c.Kotonoha.GeneratedFunction!.Body!.Items.OfType<FunctionKoto>());
        Assert.Equal(ConstraintProof.Unknown, c.Binding.ProveOwned(f.Parameters[0].Type.BoundType!, f));
    }

    [Fact]
    public void ExampleExecutesNestedTypesFunctionsAndBoundAliases()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var source = File.ReadAllText(Path.Combine(root, "examples", "ContainerNesting", "ContainerNesting.kimi"));
        ScalarEmissionTest.EmitFixture("ContainerNestingExample", source, "Nested bindings preserved.\n");
    }

    [Fact]
    public void InheritedSlotsShareIdentityButNotAnalysisState()
    {
        var c = Parse("struct Outer<T> {source}\n    public struct Inner<U> {local}\n        let value: T");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var outer = Assert.Single(c.Kotonoha.RootKoto.NestedContainers);
        var inner = Assert.Single(outer.NestedContainers);
        var parent = outer.BoundSymbol!.Schema!;
        var child = inner.BoundSymbol!.Schema!;
        Assert.Same(parent.GenericSlots[0].Symbol, child.GenericSlots[1].Symbol);
        Assert.NotSame(parent.GenericSlots[0], child.GenericSlots[1]);
        Assert.Same(parent.Origins[0].Origin, child.Origins[1].Origin);
        Assert.NotSame(parent.Origins[0], child.Origins[1]);
        Assert.Equal(OriginVariance.Invariant, parent.Origins[0].Variance);
        Assert.Equal(OriginVariance.Invariant, child.Origins[1].Variance);
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Same(child, inner.BoundSymbol.Schema);
    }

    [Fact]
    public void DeepNestingRetainsTheOriginalOuterSlot()
    {
        var source = new System.Text.StringBuilder("struct Outer<T> {}\n");
        const int depth = 64;
        for (var i = 1; i <= depth; i++)
        {
            source.Append(' ', i * 4).Append("public struct N").Append(i).Append(" {}\n");
        }

        source.Append(' ', (depth + 1) * 4).Append("let value: T\nfunc use(value: Outer<i32>");
        for (var i = 1; i <= depth; i++)
        {
            source.Append(".N").Append(i);
        }

        source.Append(") => ()");
        var c = Parse(source.ToString());
        Assert.True(c.Bind().IsComplete, Describe(c));
        var declaration = Assert.Single(c.Kotonoha.RootKoto.NestedContainers);
        var outerSlot = Assert.Single(declaration.BoundSymbol!.Schema!.GenericSlots).Symbol;
        for (var i = 0; i < depth; i++)
        {
            declaration = Assert.Single(declaration.NestedContainers);
            Assert.Same(outerSlot, Assert.Single(declaration.BoundSymbol!.Schema!.GenericSlots).Symbol);
        }
    }

    [Fact]
    public void RoundTripsMergedNestedDeclarations()
    {
        var c = Parse("struct Outer<T> {}\n    public struct Inner {}\n        var value: T\nstruct Outer<T> {}\n    public struct Inner {}\n        func echo(value: T) -> T => value");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var restored = TinyhandSerializer.Deserialize<Kotonoha>(TinyhandSerializer.Serialize(c.Kotonoha));
        Assert.NotNull(restored);
        var compilation = Compilation.CreateForTest();
        Assert.True(compilation.Prepare(WindowsProfile.Target));
        restored.OnDeserialized(compilation);
        Assert.Empty(restored.DiagnosticCollection.GetArray());
        Assert.Single(Assert.Single(restored.RootKoto.NestedContainers).NestedContainers);
    }

    private static Compilation Parse(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        return c;
    }

    private static string Describe(Compilation c) => c.Binding.Result + "\n" + string.Join('\n', c.Binding.Issues) + "\n" + string.Join('\n', c.Kotonoha.DiagnosticCollection.GetArray());
}
