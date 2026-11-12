// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Tinyhand;
using Xunit;

namespace XunitTest;

public class ContainerNestingTest
{
    private const string NestedEnum = "struct Outer<O>\n    public enum E<T>\n        A(O, T)\n        N\n        public func make() -> i32 => 0\n    public enum E2<T>\n        P(T, T)\n" +
        "    public enum R<T> {src}\n        B(O, ref/T during src)\n    public struct Mid<Y>\n        public enum K<T>\n            C(O, Y, T)\n";

    [Theory]
    [InlineData("struct Outer<T> {source}\n    public group Helpers\n        public func identity(value: T) -> T => value@move\nfunc f() -> i32\n    let result = (Outer<i32>.Helpers{h}).identity(42)\n        origin h.source == static\n    return result")]
    [InlineData("alias H => (Outer<i32>.Helpers{h})\n    origin h.source == static\nstruct Outer<T> {source}\n    public group Helpers\n        public func identity(value: T) -> T => value@move\nlet x = H.identity(42)")]
    [InlineData("contract C\nstruct Outer<T>\n    public struct Inner\n        Self is C when T is Copy\nfunc accept<T>(value: T)\n    T is C\n    ()\naccept(Outer<i32>.Inner.init())")]
    [InlineData("struct Outer\n    private struct Hidden\n    public struct Inner\n        private func use(value: Hidden) => ()")]
    [InlineData("open struct Outer\n    protected struct Inner")]
    [InlineData("struct Outer<T>\n    T is Owned\n    public group Storage\n        var value: T")]
    [InlineData("struct Outer<T>\n    public struct Cell {a}\n        let value: ref/T during a\n        public init(value: ref/T during a) => self.value = value\nfunc f(value: ref/i32 during x)\n    let cell = (Outer<i32>.Cell{c}).init(value)\n        origin c.a == x")]
    [InlineData("struct Outer<T> {a}\n    public struct Cell\n        let value: ref/T during a\n        public init(value: ref/T during a) => self.value = value\nfunc f(value: ref/i32 during x)\n    let cell = (Outer<i32>{o}).Cell.init(value)\n        origin o.a == x")]
    [InlineData("struct Outer\n    public group G\n        public struct Inner\n            public enum E\n                A\n            public contract C")]
    [InlineData("struct Outer\n    group Helpers\n        func identity(value: Self) -> Self => value@move")]
    [InlineData("struct Outer<T>\n    public struct Inner<U>\n        var first: T\n        var second: U\nfunc use(x: Outer<i32>.Inner<string>) => ()")]
    [InlineData("struct Outer<T>\n    public group Helpers\n        public struct Tag\nfunc use(x: Outer<i32>.Helpers.Tag) => ()")]
    [InlineData("struct Outer<T>\n    public struct Tag\n    func use(x: Tag) => ()")]
    [InlineData("struct Outer<T>\n    public struct Tag\nstruct Outer<T>\n    public struct Tag\n        var count: i32 = 0")]
    [InlineData("struct Outer<T>\n    public contract C\n        func read(value: T) -> T")]
    [InlineData("struct View<T> {source}\n    public struct Tag\nfunc f<T>(value: View<T>.Tag{v}) => ()")]
    [InlineData("struct View<T> {source}\n    public struct Tag {local}\nfunc f<T>(value: View<T>.Tag{v}) => ()")]
    [InlineData("open struct Base<T>\n    public struct Node\nstruct Derived: Base<i32>\nfunc f(value: Derived.Node) => ()")]
    [InlineData("alias Outer<i32>.Helpers\nstruct Outer<T>\n    public group Helpers\n        public struct Tag\nfunc f(value: Tag) => ()")]
    [InlineData("alias H => Outer<i32>.Helpers\nstruct Outer<T>\n    public group Helpers\n        public struct Tag\nfunc f(value: H.Tag) => ()")]
    [InlineData("alias Outer<i32>.Helpers\nalias Outer<i32>.Helpers\nstruct Outer<T>\n    public group Helpers\n        public struct Tag\nfunc f(value: Tag) => ()")]
    [InlineData("struct Family<T>\n    public contract Marker\nstruct Good\n    Self is Family<i32>.Marker\n    Self is Family<string>.Marker")]
    [InlineData("struct Family<T>\n    public contract Sink\n        func write(value: T)\nstruct Writer\n    Self is Family<i32>.Sink\n    public func write(value: i32) => ()")]
    [InlineData("struct View<T> {source}\n    public struct Tag\nfunc f<T>(value: (View<T>{v}).Tag) => ()")]
    [InlineData("struct View<T> {source}\n    public struct Tag {local}\nfunc f<T>(value: (View<T>{v}).Tag{t}) => ()")]
    [InlineData("struct Outer<T>\n    public struct Tag\nfunc f(value: ::Outer<i32>.Tag) => ()")]
    [InlineData("struct Family<T>\n    public contract Marker\nstruct S<T>\n    Self is Family<T>.Marker\nfunc accept<T>(x: T)\n    T is Family<i32>.Marker\n    ()\naccept(S<i32>.init())")]
    public void BindsNestedDeclarations(string source)
    {
        var c = CompilationTestHelper.Parse(source);
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Theory]
    [InlineData("struct Outer<T> {source}\n    public group Helpers\n        public func identity(value: T) -> T => value\nlet x = Outer<i32>.Helpers.identity(42)")]
    [InlineData("struct Outer<T>\n    public group Storage\n        var value: T")]
    [InlineData("struct Outer<T> {source}\n    public struct Tag\n    public group Storage\n        var value: Tag")]
    [InlineData("struct Family<T>\n    T is Copy\n    public contract Marker\nstruct NonCopy\nstruct Bad\n    Self is Family<NonCopy>.Marker")]
    [InlineData("struct Family<T>\n    T is Copy\n    public group Helpers\n        public func f() => ()\nstruct NonCopy\nFamily<NonCopy>.Helpers.f()")]
    [InlineData("struct Outer\n    public struct Inner\n        private struct Hidden\n    func use(value: Inner.Hidden) => ()")]
    [InlineData("struct Outer\n    group Helpers\n        protected struct Hidden")]
    [InlineData("contract C\nstruct Outer<T>\n    Self is C when T is Copy\n        struct Inner")]
    [InlineData("contract C\nstruct NonCopy\nstruct Outer<T>\n    public struct Inner\n        Self is C when T is Copy\nfunc accept<T>(value: T)\n    T is C\n    ()\naccept(Outer<NonCopy>.Inner.init())")]
    [InlineData("struct Outer<T>\n    public struct Tag {source}\nfunc f()\n    let value = (Outer<i32>.Tag{v})")]
    [InlineData("enum Outer\n    A\n    struct Inner")]
    [InlineData("contract Outer\n    struct Inner")]
    [InlineData("func f()\n    struct Inner")]
    [InlineData("struct Outer\n    rootgroup Inner")]
    [InlineData("group Outer\n    rootgroup Inner")]
    [InlineData("struct Outer<T>\n    struct T")]
    [InlineData("struct Outer<T>\n    public struct Tag\nfunc use(x: Outer.Tag) => ()")]
    [InlineData("struct Outer {source}\n    struct Inner {source}")]
    [InlineData("struct Outer\n    group G\n        Self is Copy")]
    [InlineData("open struct Base<T>\n    public struct Node\nstruct Derived: Base<i32>\n    public struct Node<U>")]
    [InlineData("struct Outer<T>\n    T is Copy\n    public struct Tag\nstruct NonCopy\nfunc f(value: Outer<NonCopy>.Tag) => ()")]
    [InlineData("struct Outer<T>\n    public group Helpers\n        public func echo(value: T) -> T => value\nlet result = Outer.Helpers.echo(1)")]
    [InlineData("alias Outer<i32>.Helpers\nalias Outer<string>.Helpers\nstruct Outer<T>\n    public group Helpers\n        public struct Tag\nfunc f(value: Tag) => ()")]
    [InlineData("alias H => Outer<i32>.Helpers\nalias H => Outer<string>.Helpers\nstruct Outer<T>\n    public group Helpers")]
    [InlineData("alias H => Outer<i32>.Helpers\nalias H.Tag\nstruct Outer<T>\n    public group Helpers\n        public struct Tag")]
    [InlineData("struct Family<T>\n    public contract Marker\nstruct Bad<A, B>\n    Self is Family<A>.Marker\n    Self is Family<B>.Marker")]
    [InlineData("struct Family<T>\n    public contract Marker\nstruct Bad\n    Self is Family<i32>.Marker\n    Self is Family<i32>.Marker")]
    [InlineData("struct Family<T>\n    public contract Sink\n        func write(value: T)\nstruct Writer\n    Self is Family<i32>.Sink\n    public func write(value: string) => ()")]
    [InlineData("struct View<T> {source}\n    public struct Tag\nfunc f<T>(value: (View<T>{source => a}).Tag{source => a}) => ()")]
    [InlineData("struct Family<T>\n    public contract Marker\nstruct S<T>\n    Self is Family<T>.Marker\nfunc accept<T>(x: T)\n    T is Family<i32>.Marker\n    ()\naccept(S<string>.init())")]
    public void RejectsInvalidPlacementAndUnboundEnvironment(string source)
    {
        var c = CompilationTestHelper.Parse(source);
        Assert.False(c.Bind().IsComplete && !c.Diagnostics.HasSyntaxErrors(c.Kotonoha), Describe(c));
    }

    [Fact]
    public void ABaseTypeParameterIsNoInheritedMember()
    {
        // SPEC 9.1, 9.7: Base's T belongs to Base's own scope, so Derived's nested T collides with nothing.
        var c = CompilationTestHelper.Parse("open struct Base<T>\n    public var a: i32 = 0\nstruct Derived: Base<i32>\n    public var b: i32 = 0\n    public struct T\n        public var w: i32 = 0\nlet t = Derived.T.init()");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Theory]
    [InlineData("struct Box<T>\n    public var a: i32 = 0\nlet v: Box<i32>.T = 1", "Box<i32>.T")]
    [InlineData("struct Box<T>\n    public var a: i32 = 0\nfunc f(x: Box<i32>.T) => ()", "Box<i32>.T")]
    [InlineData("struct Box<T>\n    public var a: i32 = 0\nfunc f(x: ::Box<i32>.T) => ()", "::Box<i32>.T")]
    [InlineData("alias Box<i32>\nstruct Box<T>\n    public var a: i32 = 0\nfunc f(x: T) => ()", "T")]
    [InlineData("alias Box<i32>\nstruct Box<T>\n    public var a: i32 = 0\nfunc f(x: Box<T>) => ()", "T")]
    [InlineData("alias Box<i32>\nstruct Box<T>\n    public var a: i32 = 0\nfunc f() -> i32\n    let x: Option<T> = .None\n    return 1", "T")]
    [InlineData("alias Box<i32>\nalias Pair<i64>\nstruct Box<T>\n    public var a: i32 = 0\nstruct Pair<T>\n    public var b: i32 = 0\nfunc f(x: T) => ()", "T")]
    [InlineData("alias Box<i32>\nstruct Box<S/T>\n    public var a: i32 = 0\nfunc f(x: T) => ()", "T")]
    [InlineData("alias Outer<i32>.Inner<i64>\nstruct Outer<T>\n    public struct Inner<U>\n        public var a: i32 = 0\nfunc f(x: U) => ()", "U")]
    [InlineData("alias Kimi.Option<i32>\nfunc f(x: T) => ()", "T")]
    public void AFormalParameterIsNoMemberOfItsContainer(string source, string name)
    {
        var c = CompilationTestHelper.Parse(source);
        Assert.False(c.Bind().IsComplete);
        var issue = Assert.Single(c.Binding.Issues);
        Assert.Equal(Kimi.DiagnosticCode.UnresolvedBinding_Kd, issue.Code);
        Assert.Equal(name, issue.Node.ToString());
    }

    // SPEC 23.1.2, 9.1, 9.4.1, 9.7.1 (PLAN G250 (4)): the qualifier of an inaccessible member is bound in its container role, so its own
    // failures, independent of the access, are reported beside the member's: an arity mismatch, omitted outer arguments and an
    // inaccessible argument.
    [Theory]
    [InlineData("T<i32, bool>.G.Box.init()", "InaccessibleBinding_Kd T<i32, bool>.G|InvalidTypeFormation_Kd T<i32, bool>")]
    [InlineData("T.G.Box.init()", "InaccessibleBinding_Kd T.G|InvalidTypeFormation_Kd T")]
    [InlineData("T<H.Hidden>.G.Box.init()", "InaccessibleBinding_Kd H.Hidden|InaccessibleBinding_Kd T<H.Hidden>.G")]
    public void AQualifierOfAnInaccessibleMemberReportsItsOwnFailure(string use, string records)
    {
        var c = CompilationTestHelper.Parse("struct T<U>\n    group G\n        public struct Box\n            public init() => ()\ngroup H\n    struct Hidden\nfunc f()\n    let b = " + use + "\n");
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(records, string.Join('|', c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}").Order(StringComparer.Ordinal)));
    }

    // SPEC 23.1.2, 9.1, 9.7, 9.7.1, 6.1 (PLAN G481 (1), (3)): a qualifier's own failure is reported at the qualifier whatever form reads
    // it, and stays failed: a struct qualifier's wrong arity or omitted outer arguments (a call, a Type position, a `::` path, also
    // a middle segment), a qualifier resting on its failed argument, and a group qualifier's written arguments, since a group has no
    // own parameters. The consumer rests on it. A failed qualifier's arguments keep their own failures (SPEC 9.7; PLAN G513). The
    // valid twins bind.
    [Theory]
    [InlineData(false, "func f()\n    let b = T<i32, Missing>.G.Box.init()\n", "InvalidTypeFormation_Kd T<i32, Missing>|UnresolvedBinding_Kd Missing")]
    [InlineData(false, "func f(b: T<i32, Missing>.G.Box) => ()\n", "InvalidTypeFormation_Kd T<i32, Missing>|UnresolvedBinding_Kd Missing")]
    [InlineData(true, "func f()\n    let b = T<Missing>.G.Box.init()\n", "InvalidTypeFormation_Kd T<Missing>|UnresolvedBinding_Kd Missing")]
    [InlineData(true, "func f() -> i32 => T<Missing>.one()\n", "InvalidTypeFormation_Kd T<Missing>|UnresolvedBinding_Kd Missing")]
    [InlineData(true, "group H\n    struct Hidden\nfunc f() -> i32 => T<H.Hidden>.value\n", "InaccessibleBinding_Kd H.Hidden|InvalidTypeFormation_Kd T<H.Hidden>")]
    [InlineData(false, "func f()\n    let b = T<i32, bool>.G.Box.init()\n", "InvalidTypeFormation_Kd T<i32, bool>")]
    [InlineData(false, "func f()\n    let b = T.G.Box.init()\n", "InvalidTypeFormation_Kd T")]
    [InlineData(false, "func f(b: T<i32, bool>.G.Box) => ()\n", "InvalidTypeFormation_Kd T<i32, bool>")]
    [InlineData(false, "func f(b: ::T<i32, bool>.G.Box) => ()\n", "InvalidTypeFormation_Kd T<i32, bool>")]
    [InlineData(false, "struct Outer\n    public struct T<U>\n        public group G\n            public struct Box\nfunc f(b: ::Outer.T.G.Box) => ()\n", "InvalidTypeFormation_Kd Outer.T")]
    [InlineData(false, "func f()\n    let b = T<Missing>.G.Box.init()\n", "UnresolvedBinding_Kd Missing")]
    [InlineData(false, "group H\n    struct Hidden\nfunc f()\n    let b = T<H.Hidden>.G.Box.init()\n", "InaccessibleBinding_Kd H.Hidden")]
    [InlineData(false, "func f()\n    let b = T<i32>.G.Box.init()\n", "")]
    [InlineData(true, "func f()\n    let b = T<i32>.G.Box.init()\n", "InvalidTypeFormation_Kd T<i32>")]
    [InlineData(true, "func f(b: T<i32>.G.Box) => ()\n", "InvalidTypeFormation_Kd T<i32>")]
    [InlineData(true, "func f(b: ::T<i32>.G.Box) => ()\n", "InvalidTypeFormation_Kd T<i32>")]
    [InlineData(true, "func f() -> i32 => T<i32>.value\n", "InvalidTypeFormation_Kd T<i32>")]
    [InlineData(true, "func f() -> i32 => T<i32>.one()\n", "InvalidTypeFormation_Kd T<i32>")]
    [InlineData(true, "func f()\n    let b = T.G.Box.init()\n", "")]
    public void AQualifierReportsItsOwnFailure(bool group, string use, string records)
    {
        var declaration = group ? "group T\n    public let value: i32 = 1\n    public func one() -> i32 => 1\n" : "struct T<U>\n";
        var c = CompilationTestHelper.Parse(declaration + "    public group G\n        public struct Box\n            public init() => ()\n" + use);
        Assert.Equal(records.Length == 0, c.Bind().IsComplete);
        Assert.Equal(records, string.Join('|', c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}").Order(StringComparer.Ordinal)));
    }

    // SPEC 9.7.1, 23.1.2 (PLAN G481 (2)): a missing member's qualifier is bound in its container role, as an inaccessible member's, so
    // its own failure is reported beside the missing Name. Only a group or a struct declares nested Containers (SPEC 6.1, 6.5.1), so an
    // enum or a Contract qualifier of a missing member gets no container record.
    [Theory]
    [InlineData("struct T<U>\n    public init() => ()\nfunc f()\n    let b = T<i32, bool>.Nope.init()\n", "InvalidTypeFormation_Kd T<i32, bool>|UnresolvedBinding_Kd T<i32, bool>.Nope.init")]
    [InlineData("struct T<U>\n    public init() => ()\nfunc f()\n    let b = T<i32, bool>.Nope.Box.init()\n", "InvalidTypeFormation_Kd T<i32, bool>|UnresolvedBinding_Kd T<i32, bool>.Nope.Box.init")]
    [InlineData("group T\n    public group G\n        public struct Box\nfunc f()\n    let b = T<i32>.G.Nope.init()\n", "InvalidTypeFormation_Kd T<i32>|UnresolvedBinding_Kd T<i32>.G.Nope.init")]
    [InlineData("enum E<T>\n    A(T)\nfunc f()\n    let b = E.Nope.init()\n", "UnresolvedBinding_Kd E.Nope.init")]
    [InlineData("contract C\n    func g(self) -> i32\nfunc f(b: C.Nope) => ()\n", "UnresolvedBinding_Kd C.Nope")]
    public void AMissingMembersQualifierReportsItsOwnFailure(string source, string records)
    {
        var c = CompilationTestHelper.Parse(source);
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(records, string.Join('|', c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}").Order(StringComparer.Ordinal)));
    }

    // SPEC 9.7, 9.7.1, 6.5.2 steps 2-3 (PLAN G745): a nested enum's qualifier with its own list omitted before a Case reference takes its
    // environment from the path and infers only its own slots, as `Outer<i32>.Inner.init(value)` does: from the payloads (the environment
    // fixed before any evidence, so a payload at it is read as a value), from the expected Type or from the Pattern position, at any
    // nesting depth and in a non-generic container (`Plain.E.A(n)`, no environment). The twins keep the records of the top-level forms
    // (`Option.None`, a payload that misfits, conflicting evidence), a written argument that differs fails, and an omitted outer or
    // middle list stays the qualifier's own failure (SPEC 9.7.1).
    [Theory]
    [InlineData(NestedEnum + "func f(v: i32)\n    let e = Outer<i32>.E.A(v, 1@i64)\n", "")]
    [InlineData(NestedEnum + "func f(n: ref/i32)\n    let e = Outer<i32>.E.A(n, 1@i64)\n", "")]
    [InlineData(NestedEnum + "func f()\n    let e = Outer<i32>.E.A(1, 2)\n", "")]
    [InlineData(NestedEnum + "func f()\n    let e: Outer<i32>.E<i64> = Outer<i32>.E.N\n", "")]
    [InlineData(NestedEnum + "func f(e: ref/Outer<i32>.E<i64>) -> i32\n    return match e\n        Outer<i32>.E.A(let a, let b) => 0\n        .N => 1\n", "")]
    [InlineData("struct Outer<O>\n    public enum E<T>\n        A(O, T)\n    public func f(o: O)\n        let e = E.A(o@move, 1@i64)\n", "")]
    [InlineData(NestedEnum + "func f(n: ref/i64)\n    let e = Outer<i32>.E.A(n, 1@i64)\n", "TypeMismatch_Kd Outer<i32>.E.A(n, 1@i64)")]
    [InlineData(NestedEnum + "func f()\n    let e = Outer<i32>.E.N\n", "UnresolvedBinding_Kd Outer<i32>.E.N")]
    [InlineData(NestedEnum + "func f(v: i32)\n    let e: Outer<i64>.E<i64> = Outer<i32>.E.A(v, 1)\n", "TypeMismatch_Kd Outer<i32>.E.A(v, 1)")]
    [InlineData(NestedEnum + "func f(e: ref/Outer<i32>.E<i64>) -> i32\n    return match e\n        Outer<i64>.E.A(let a, let b) => 0\n        .N => 1\n", "InvalidPattern_Kd Outer<i64>.E.A(let a, let b)")]
    [InlineData(NestedEnum + "func f() -> i32 => Outer<i32>.E.make()\n", "InvalidTypeFormation_Kd Outer<i32>.E")]
    [InlineData(NestedEnum + "func f(v: i32)\n    let k = Outer<i32>.Mid<i64>.K.C(v, 2@i64, true)\n", "")]
    [InlineData(NestedEnum + "func f(n: ref/i32, m: ref/i64)\n    let k = Outer<i32>.Mid<i64>.K.C(n, m, true)\n", "")]
    [InlineData(NestedEnum + "func f(n: ref/i32)\n    let k = Outer<i32>.Mid<i64>.K.C(n, n, true)\n", "TypeMismatch_Kd Outer<i32>.Mid<i64>.K.C(n, n, true)")]
    [InlineData(NestedEnum + "func f(v: i32)\n    let k = Outer<i32>.Mid.K.C(v, 2@i64, true)\n", "InvalidTypeFormation_Kd Outer<i32>.Mid")]
    [InlineData(NestedEnum + "func f(v: i32)\n    let e = Outer.E<i64>.A(v, 1)\n", "InvalidTypeFormation_Kd Outer")]
    [InlineData(NestedEnum + "func f(e: Outer<i32>.E<i64>) -> i32\n    return match e\n        Outer<i32>.E.A(let a, let b) => a\n        .N => 0\n", "")]
    [InlineData(NestedEnum + "func f(e: Outer<i32>.E<i64>) -> i32\n    return match e\n        Outer<i32>.E.A(let a, let b) => b\n        .N => 0\n", "TypeMismatch_Kd match e\n    Outer<i32>.E.A(let a, let b) => b\n    .N => 0")]
    [InlineData(NestedEnum + "func mk<X>(x: X) -> Outer<X>.E<i64>\n    return Outer<X>.E.A(x@move, 1@i64)\n", "")]
    [InlineData(NestedEnum + "func mk<X>(x: X) -> Outer<X>.E<i64>\n    let e = Outer<X>.E.A(x@move, true)\n    return e\n", "TypeMismatch_Kd return e")]
    [InlineData(NestedEnum + "func f(n: ref/i32, r: ref/i64)\n    let e = Outer<i32>.R.B(n, r)\n", "")]
    [InlineData(NestedEnum + "func f(r: ref/i64)\n    let e = Outer<i32>.R.B(r, r)\n", "TypeMismatch_Kd Outer<i32>.R.B(r, r)")]
    [InlineData(NestedEnum + "func f()\n    let e = Outer<i32>.E2.P(1@i64, true)\n", "TypeMismatch_Kd Outer<i32>.E2.P(1@i64, true)")]
    [InlineData(NestedEnum + "func f(v: i32)\n    let e: Outer<i32>.E<i64> = Outer<i32>.E.A(v, true)\n", "TypeMismatch_Kd Outer<i32>.E.A(v, true)")]
    [InlineData("struct Plain\n    public enum E<T>\n        A(T)\nfunc f(n: ref/i32)\n    let e = Plain.E.A(n)\n", "")]
    [InlineData("struct Plain\n    public enum E<T>\n        A(T)\nfunc f()\n    let e: Plain.E<i32> = Plain.E.A(true)\n", "TypeMismatch_Kd Plain.E.A(true)")]
    public void ANestedEnumCaseInfersOnlyItsOwnSlots(string source, string records)
    {
        var c = CompilationTestHelper.Parse(source);
        Assert.Equal(records.Length == 0, c.Bind().IsComplete);
        Assert.Equal(records, string.Join('|', c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}").Order(StringComparer.Ordinal)));
    }

    // SPEC 9.7, 10.8.1 (PLAN G745): an inferred construction qualifier, a Case's as a structure's, names the constructed Type once the
    // construction is solved, never its open own parameters.
    [Theory]
    [InlineData(NestedEnum + "func f(v: i32)\n    let e = Outer<i32>.E.A(v, 1@i64)\n", "Outer<i32>.E")]
    [InlineData("struct Outer<O>\n    public enum E<T>\n        A(O, T)\n    public func f(o: O)\n        let e = E.A(o@move, 1@i64)\n", "E")]
    [InlineData("struct Outer<O>\n    public struct S<U>\n        public let b: U\n        public init(b: U) => self.b = b@move\nfunc f()\n    let s = Outer<i32>.S.init(1@i64)\n", "Outer<i32>.S")]
    public void AnInferredQualifierNamesTheConstructedType(string source, string qualifier)
    {
        var c = CompilationTestHelper.Parse(source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        var member = HirColumnsTest.Reachable(c.Kotonoha).OfType<MemberAccessKoto>().Single(x => x.Left.ToString() == qualifier);
        Assert.Same(member.Parent!.TypeOf(), member.Left.TypeOf());
    }

    // SPEC 9.6 (PLAN G488): a qualifier naming both a Type and a parameter or local needs both namespaces' complete paths before either
    // publishes (R7 N1). The value path read the Type path's binding, so where neither path or both found the member, the form is one
    // located Unsupported_Kd at the qualifier (the owner's decision O-32 (a)), never a Language error from one path: the valid Type-only
    // (`group T`, `T.one()`) and value-only (`T: T`) forms among them. A group qualifier is never taken as a value (that ended the check
    // in Layout). The valid form without the Type binds.
    [Theory]
    [InlineData("struct T\n    public group G\n        public let value: i32 = 1\n", "func f(T: V) -> i32 => T.G.n\n", "Unsupported_Kd T.G")]
    [InlineData("struct T\n    public group G\n        public let n: i32 = 1\n", "func f(T: V) -> i32 => T.G.n\n", "Unsupported_Kd T.G")]
    [InlineData("struct T\n    group G\n        public let value: i32 = 1\n", "func f(T: V) -> i32 => T.G.n\n", "Unsupported_Kd T")]
    [InlineData("struct T\n    group G\n        public let value: i32 = 1\n", "func f() -> i32\n    let T = V.init()\n    return T.G.n\n", "Unsupported_Kd T")]
    [InlineData("struct T\n    public let k: i32 = 1\n", "func f(T: V) -> W => T.G\n", "Unsupported_Kd T")]
    [InlineData("group T\n    public let G: i32 = 1\n", "func f(T: V) -> W => T.G\n", "Unsupported_Kd T")]
    [InlineData("group T\n    public let n: i32 = 1\n", "func f(T: V) -> i32 => T.n\n", "Unsupported_Kd T")]
    [InlineData("struct T\n    public func one() -> i32 => 1\n", "func f(T: V) -> i32 => T.one()\n", "Unsupported_Kd T")]
    [InlineData("struct T\n    public let n: i32\n    public init() => self.n = 1\n", "func f(T: T) -> i32 => T.n\n", "Unsupported_Kd T")]
    [InlineData("", "func f(T: V) -> i32 => T.G.n\n", "")]
    public void AQualifierOfBothNamespacesIsUnsupported(string type, string use, string records)
    {
        var c = CompilationTestHelper.Parse(type + "struct W\n    public let n: i32\n    public init() => self.n = 1\nstruct V\n    public let G: W\n    public init() => self.G = W.init()\n" + use);
        Assert.Equal(records.Length == 0, c.Bind().IsComplete);
        Assert.Equal(records, string.Join('|', c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}")));
        c.Analyze();
    }

    // SPEC 9.6, 18.2.1 (R7-N1b): a Type `N` opened from `Box<i32>` and a value `N` opened from `Other<i64>` qualify one member. The value
    // path compares only its own sources' environments, never the Type path's import of the node, so the qualifier is the located
    // Unsupported_Kd of its class (BU-117, the owner's decision O-32 (a)), never AmbiguousBinding_Kd: where the Type path finds the
    // member (valid) and where neither path does (invalid).
    [Theory]
    [InlineData("N.m()")]
    [InlineData("N.k()")]
    public void AQualifierOpenedFromTwoInstantiationsIsUnsupported(string use)
    {
        var c = CompilationTestHelper.Parse("alias Box<i32>\nalias Other<i64>\nstruct Box<T>\n    public struct N\n        public func m() -> i32 => 1\n" +
            "struct Other<T>\n    public func N() -> i32 => 2\nfunc g() -> i32 => " + use + "\n");
        Assert.False(c.Bind().IsComplete);
        Assert.Equal("Unsupported_Kd N", string.Join('|', c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}")));
    }

    // SPEC 9.6, 6.1 (PLAN G481 (3)): a group qualifier that failed selects no member, as a failed struct qualifier does; the scope lookup
    // is the root container's and a Kotonoha reference's, never a fallback for a failed group.
    [Theory]
    [InlineData("T<i32>.value", "value")]
    [InlineData("T<i32>.one()", "one")]
    public void AFailedGroupQualifierSelectsNoMember(string use, string member)
    {
        var c = CompilationTestHelper.Parse("group T\n    public let value: i32 = 1\n    public func one() -> i32 => 1\nfunc f() -> i32 => " + use + "\n");
        Assert.False(c.Bind().IsComplete);
        var name = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<IdentifierNameKoto>().Single(x => x.IdentifierName == member && x.Parent is MemberAccessKoto);
        Assert.Null(name.SymbolOf());
    }

    // SPEC 9.4.1 (PLAN G250 (4)): a value, a Function Item or a Case selected through an inaccessible Container reports the access at
    // that Container, as a call through it does, and its struct qualifier is bound as its Type.
    [Theory]
    [InlineData("    group G\n        public let value: i32 = 1\n", "T.G.value", "T.G")]
    [InlineData("    group G\n        public func one() -> i32 => 1\n", "T.G.one", "T.G")]
    [InlineData("    enum K\n        A\n", "T.K.A", "T.K")]
    public void AMemberThroughAnInaccessibleContainerReportsTheAccess(string members, string use, string container)
    {
        var c = CompilationTestHelper.Parse("struct T\n" + members + "func f()\n    let b = " + use + "\n");
        Assert.False(c.Bind().IsComplete);
        var issue = Assert.Single(c.Binding.Issues);
        Assert.Equal($"InaccessibleBinding_Kd {container}", $"{issue.Code} {issue.Node}");
        Assert.Equal("T", Qualifier(c).TypeOf()?.Symbol?.Name);
    }

    // SPEC 9.4.1 (PLAN G250 (4)): a group qualifier of an inaccessible member is bound as its Container, symbol and Type, as when the
    // member is accessible.
    [Theory]
    [InlineData("group G")]
    [InlineData("public group G")]
    public void AGroupQualifierOfAnInaccessibleMemberIsBoundAsItsContainer(string member)
    {
        var c = CompilationTestHelper.Parse("group T\n    " + member + "\n        public struct Box\n            public init() => ()\nfunc f()\n    let b = T.G.Box.init()\n");
        c.Bind();
        var qualifier = Qualifier(c);
        Assert.Equal((BindingSymbolKind.Container, "T"), (qualifier.SymbolOf()?.Kind, qualifier.SymbolOf()?.Name));
        Assert.Same(qualifier.SymbolOf(), qualifier.TypeOf()?.Symbol);
    }

    [Fact]
    public void UnusedOuterArgumentsRemainPartOfIdentity()
    {
        var c = CompilationTestHelper.Parse("struct Outer<T>\n    public struct Tag\nfunc a(x: Outer<i32>.Tag) => ()\nfunc b(x: Outer<i64>.Tag) => ()\nfunc same(x: Outer<i32>.Tag) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var functions = c.Kotonoha.GeneratedFunction!.Body!.Items.OfType<FunctionKoto>().ToArray();
        var first = functions[0].Parameters[0].Type.TypeOf()!;
        Assert.Same(first.Symbol, functions[1].Parameters[0].Type.TypeOf()!.Symbol);
        Assert.NotSame(first, functions[1].Parameters[0].Type.TypeOf());
        Assert.Same(first, functions[2].Parameters[0].Type.TypeOf());
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Same(first, functions[0].Parameters[0].Type.TypeOf());
    }

    [Fact]
    public void NestedConstructionUsesOuterFieldSubstitution()
        => ScalarEmissionTest.EmitFixture("ContainerNestingFields", "struct Outer<T>\n    public struct Inner\n        public var value: T\n        public init(value: T) => self.value = value@move\nvar a = Outer<i32>.Inner.init(42)\nif a.value == 42 => Kimi.Console.writeLine(\"ok\")", "ok\n");

    [Fact]
    public void NestedGroupFunctionsRetainOuterBindings()
        => ScalarEmissionTest.EmitFixture("ContainerNestingGroup", "struct Outer<T>\n    public group Helpers\n        public func echo(value: T) -> T => value@move\nlet a = Outer<i32>.Helpers.echo(42)\nif a == 42 => Kimi.Console.writeLine(\"ok\")", "ok\n");

    [Fact]
    public void BoundAliasKeepsFunctionEnvironment()
        => ScalarEmissionTest.EmitFixture("ContainerNestingAlias", "alias H => Outer<i32>.Helpers\nstruct Outer<T>\n    public group Helpers\n        public func echo(value: T) -> T => value@move\nlet a = H.echo(42)\nif a == 42 => Kimi.Console.writeLine(\"ok\")", "ok\n");

    [Fact]
    public void NestedEnumConstructionRetainsOuterBindings()
        => ScalarEmissionTest.EmitFixture("ContainerNestingEnum", "struct Outer<T>\n    public enum Choice\n        Value(T)\nlet x = Outer<i32>.Choice.Value(42)\nmatch x@move\n    .Value(let value)\n        if value == 42 => Kimi.Console.writeLine(\"ok\")", "ok\n");

    [Fact]
    public void InheritedNestedReferenceKeepsDefiningIdentity()
    {
        var c = CompilationTestHelper.Parse("open struct Base<T>\n    public struct Node\nstruct Derived: Base<i32>\nfunc f(a: Base<i32>.Node, b: Derived.Node) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Assert.Single(c.Kotonoha.GeneratedFunction!.Body!.Items.OfType<FunctionKoto>());
        Assert.Same(f.Parameters[0].Type.TypeOf(), f.Parameters[1].Type.TypeOf());
    }

    [Fact]
    public void EmptyNestedTypeRetainsUnusedBorrowDependency()
    {
        var c = CompilationTestHelper.Parse("struct Outer<T>\n    public struct Tag\nfunc f(x: Outer<ref/i32 during a>.Tag) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Assert.Single(c.Kotonoha.GeneratedFunction!.Body!.Items.OfType<FunctionKoto>());
        Assert.Equal(ConstraintProof.Unknown, c.Binding.ProveOwned(f.Parameters[0].Type.TypeOf()!, f));
    }

    [Fact]
    public void ExampleExecutesNestedTypesFunctionsAndBoundAliases()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var source = File.ReadAllText(Path.Combine(root, "docs", "examples", "ContainerNesting", "ContainerNesting.kimi"));
        ScalarEmissionTest.EmitFixture("ContainerNestingExample", source, "Nested bindings preserved.\n");
    }

    [Fact]
    public void InheritedSlotsShareIdentityButNotAnalysisState()
    {
        var c = CompilationTestHelper.Parse("struct Outer<T> {source}\n    public struct Inner<U> {local}\n        let value: T");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var outer = Assert.Single(c.Kotonoha.RootKoto.NestedContainers);
        var inner = Assert.Single(outer.NestedContainers);
        var parent = outer.SymbolOf()!.Schema!;
        var child = inner.SymbolOf()!.Schema!;
        Assert.Same(parent.GenericSlots[0].Symbol, child.GenericSlots[1].Symbol);
        Assert.NotSame(parent.GenericSlots[0], child.GenericSlots[1]);
        Assert.Same(parent.Origins[0].Origin, child.Origins[1].Origin);
        Assert.NotSame(parent.Origins[0], child.Origins[1]);
        Assert.Equal(OriginVariance.Invariant, parent.Origins[0].Variance);
        Assert.Equal(OriginVariance.Invariant, child.Origins[1].Variance);
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Same(child, inner.SymbolOf()!.Schema);
    }

    [Fact]
    public void DeepNestingRetainsTheOriginalOuterSlot()
    {
        var source = new System.Text.StringBuilder("struct Outer<T>\n");
        const int depth = 64;
        for (var i = 1; i <= depth; i++)
        {
            source.Append(' ', i * 4).Append("public struct N").Append(i).Append('\n');
        }

        source.Append(' ', (depth + 1) * 4).Append("let value: T\nfunc use(value: Outer<i32>");
        for (var i = 1; i <= depth; i++)
        {
            source.Append(".N").Append(i);
        }

        source.Append(") => ()");
        var c = CompilationTestHelper.Parse(source.ToString());
        Assert.True(c.Bind().IsComplete, Describe(c));
        var declaration = Assert.Single(c.Kotonoha.RootKoto.NestedContainers);
        var outerSlot = Assert.Single(declaration.SymbolOf()!.Schema!.GenericSlots).Symbol;
        for (var i = 0; i < depth; i++)
        {
            declaration = Assert.Single(declaration.NestedContainers);
            Assert.Same(outerSlot, Assert.Single(declaration.SymbolOf()!.Schema!.GenericSlots).Symbol);
        }
    }

    [Fact]
    public void RoundTripsMergedNestedDeclarations()
    {
        var c = CompilationTestHelper.Parse("struct Outer<T>\n    public struct Inner\n        var value: T\nstruct Outer<T>\n    public struct Inner\n        func echo(value: T) -> T => value@move");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var restored = TinyhandSerializer.Deserialize<Kotonoha>(TinyhandSerializer.Serialize(c.Kotonoha));
        Assert.NotNull(restored);
        var compilation = Compilation.CreateForTest();
        Assert.True(compilation.Prepare(WindowsProfile.Target));
        restored.OnDeserialized(compilation);
        Assert.Empty(TestDiagnostics.Of(restored));
        Assert.Single(Assert.Single(restored.RootKoto.NestedContainers).NestedContainers);
    }

    private static string Describe(Compilation c) => c.Binding.Result + "\n" + string.Join('\n', c.Binding.Issues) + "\n" + string.Join('\n', TestDiagnostics.Of(c));

    // The written qualifier `T` of the member that f uses.
    private static IdentifierNameKoto Qualifier(Compilation c)
        => HirColumnsTest.Reachable(c.Kotonoha).OfType<IdentifierNameKoto>().Single(static x => x.IdentifierName == "T" && x.Parent is MemberAccessKoto);
}
