// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class GenericClosureIdentityTest
{
    // Two closures of equal result Type in one generic function, run under two instances (InstanceCollectorTest reads it too).
    internal const string TwoClosures = "func pair<T>(value: T) -> i32\n    T is Copy\n    let first = func [] (x: T) -> i32 => 1\n    let second = func [] (x: T) -> i32 => 2\n" +
        "    return first(value) * 10 + second(value)\nrequire pair(3) == 12 and pair(true) == 12 else => $abort(\"two closures\")";

    private const string Identity = "func identity<length N>(value: [N of i32]) -> [N of i32]\n    let action = func [] (inner: [N of i32]) -> [N of i32] => inner\n    return action(value)\n";

    // A Contract whose static requirement tells A (1) from B (2), and a struct whose stored Field initializer's Closure returns T's tag.
    private static readonly string[] Tagged =
    [
        "contract Tag", "    func tag() -> isize", "struct A", "    Self is Tag", "    public init() => ()", "    public func tag() -> isize => 1",
        "struct B", "    Self is Tag", "    public init() => ()", "    public func tag() -> isize => 2",
    ];

    private static readonly string[] TaggedBox = [.. Tagged, "struct Box<T>", "    T is Tag", "    public var n = func [] () -> isize => T.tag()", "    public init() => ()"];

    // The form and its program lines.
    public static TheoryData<string, string[]> HeldCases => new()
    {
        {
            "WrapSelf",
            [
                .. Tagged, "struct W<X>", "    X is Tag", "    Self is Tag", "    public init() => ()", "    public func tag() -> isize => X.tag() + 100",
                "struct Box<T>", "    T is Tag", "    public var n = func [] () -> isize => T.tag()", "    public init() => ()",
                "    public func m(self: ref/Self, other: Box<W<T>>) -> isize", "        let g = other.n", "        return g()",
                "require Box<A>.init().m(Box<W<A>>.init()) == 101 else => $abort(\"wrapself\")", "Console.writeLine(\"ok\")",
            ]
        },
        {
            "Cross",
            [
                .. TaggedBox, "    public func cross(self: ref/Self, other: Box<B>) -> isize", "        let g = other.n", "        let e: () -> isize = other.n",
                "        return g() * 10 + e()", "require Box<A>.init().cross(Box<B>.init()) == 22 else => $abort(\"cross\")", "Console.writeLine(\"ok\")",
            ]
        },
        {
            "Generic",
            [
                .. TaggedBox, "func through<U>(box: Box<U>) -> isize", "    U is Tag", "    let g = box.n", "    let e: () -> isize = box.n", "    return g() * 10 + e()",
                "require through(Box<A>.init()) == 11 and through(Box<B>.init()) == 22 else => $abort(\"through\")", "Console.writeLine(\"ok\")",
            ]
        },
        {
            "Nested",
            [
                .. Tagged, "struct Outer<T>", "    T is Tag", "    public init() => ()", "    public func pick(self: ref/Self, i: Outer<T>.Inner<B>) -> isize",
                "        let g = i.m", "        return g()", "    public struct Inner<U>", "        U is Tag",
                "        public var m = func [] () -> isize => T.tag() * 10 + U.tag()", "        public init() => ()",
                "        public func read(self: ref/Self, other: Outer<T>.Inner<B>) -> isize", "            let g = other.m", "            return g()",
                "require Outer<A>.Inner<A>.init().read(Outer<A>.Inner<B>.init()) == 12 else => $abort(\"inner\")",
                "require Outer<B>.init().pick(Outer<B>.Inner<B>.init()) == 22 else => $abort(\"outer\")", "Console.writeLine(\"ok\")",
            ]
        },
        {
            "Inherited",
            [
                .. Tagged, "open struct Base<T>", "    T is Tag", "    public var n = func [] () -> isize => T.tag()", "    public init() => ()",
                "struct Derived<V> : Base<V>", "    V is Tag", "    public init() : base() => ()",
                "    public func cross(self: ref/Self, other: Derived<B>, own: Derived<V>) -> isize", "        let g = other.n", "        let h = own.n",
                "        return g() * 10 + h()", "func viaFn<W>(d: Derived<W>) -> isize", "    W is Tag", "    let g = d.n", "    return g()",
                "let d = Derived<A>.init()", "require d.n() == 1 and viaFn(Derived<B>.init()) == 2 else => $abort(\"top\")",
                "require Derived<A>.init().cross(Derived<B>.init(), Derived<A>.init()) == 21 else => $abort(\"cross\")", "Console.writeLine(\"ok\")",
            ]
        },
        {
            "Projection",
            [
                .. TaggedBox, "contract HasItem", "    associate Item", "struct SrcA", "    Self is HasItem", "    associate HasItem.Item is A", "    public init() => ()",
                "func viaWrap<S>(s: S) -> isize", "    S is HasItem", "    S.(HasItem).Item is Tag", "    let e: () -> isize = Box<S.Item>.init().n",
                "    let f: () -> isize = Box<A>.init().n", "    let b = Box<S.Item>.init()", "    let g = b.n", "    return e() * 100 + f() * 10 + g()",
                "require viaWrap(SrcA.init()) == 111 else => $abort(\"projection\")", "Console.writeLine(\"ok\")",
            ]
        },
        {
            "Signature",
            [
                "struct Box<T>", "    T is Copy", "    public var f = func [] (x: T) -> T => x", "    public init() => ()",
                "func through<T>(box: Box<T>, x: T) -> T", "    T is Copy", "    return box.f(x)", "let a = Box<i32>.init()",
                "require a.f(7) == 7 and through(Box<i64>.init(), 9000000002) == 9000000002 else => $abort(\"signature\")", "Console.writeLine(\"ok\")",
            ]
        },
        {
            "Forwarded",
            [
                "struct Box<T>", "    public var n = func [] () -> u16 => 5", "    public init() => ()", "func inner<W>(box: ref/Box<W>) -> isize", "    return 1",
                "func outer<U>(box: ref/Box<U>) -> isize", "    return inner(box)", "let a = Box<i32>.init()",
                "require outer(a@ref) == 1 and a.n() == 5 else => $abort(\"forwarded\")", "Console.writeLine(\"ok\")",
            ]
        },
    };

    [Fact]
    public void EmptyEnvironmentsKeepLengthOnlyAbiSubstitutions()
    {
        const string Source = Identity + "let empty: [0 of i32] = []\nlet first = identity(empty)\nlet second = identity([2, 4])\nrequire first.length == 0 and second[1] == 4 else => $abort(\"length\")";
        ScalarEmissionTest.EmitFixture("GenericClosureIdentityLength", Source, string.Empty);
    }

    [Fact]
    public void EmptyEnvironmentsKeepContainerOnlySubstitutions()
    {
        const string Source = "struct Factory<T>\n    public func identity(value: T) -> T\n        let action = func [] (inner: T) -> T => inner@move\n        return action(value@move)\nrequire Factory<i32>.identity(7) == 7 else => $abort(\"container\")\nConsole.writeLine(Factory<string>.identity(\"owned\"))";
        ScalarEmissionTest.EmitFixture("GenericClosureIdentityContainer", Source, "owned\n");
    }

    [Fact]
    public void LengthContextIdentityIsCanonicalAndDistinct()
    {
        var c = MinimalEmissionTest.Analyze(Identity + "_ = identity([1, 2])\n_ = identity([3, 4])\n_ = identity([5])");
        var closure = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsAnonymous).ClosureOf()!.EnvironmentType!;
        var calls = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Where(x => x.CallOf()?.Target.Name == "identity").Select(x => x.CallOf()!).ToArray();
        var first = c.Binding.InstantiateStorageType(closure, calls[0])!;
        var same = c.Binding.InstantiateStorageType(closure, calls[1])!;
        var different = c.Binding.InstantiateStorageType(closure, calls[2])!;
        Assert.Same(first, same);
        Assert.NotSame(first, different);
        Assert.False(Binding.FitsType(first, different));
        Assert.False(Binding.FitsType(different, first));
        Assert.Empty(first.Components);
    }

    // DECISIONS R5-M36, S2-S4 X10: two closures of equal result Type in one generic instance share the instance's context and keep their
    // own closed Types, which key their instances, so each instance runs each closure's own body.
    [Fact]
    public void TwoClosuresOfOneInstanceKeepTheirOwnBodies()
    {
        var c = MinimalEmissionTest.Analyze(TwoClosures);
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var call = nodes.OfType<InvocationKoto>().First(static x => x.CallOf()?.Target.Name == "pair").CallOf()!;
        var closures = nodes.OfType<FunctionKoto>().Where(static x => x.IsAnonymous).Select(x => c.Binding.InstantiateStorageType(x.ClosureOf()!.EnvironmentType!, call)!).ToArray();
        Assert.Equal(2, closures.Length);
        Assert.Same(closures[0].ClosureContext, closures[1].ClosureContext);
        Assert.Same(c.Types.SubstOf(call), closures[0].ClosureContext);
        ScalarEmissionTest.EmitFixture("GenericClosureIdentityTwo", TwoClosures, string.Empty);
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "zero\n")]
    public void ZeroSizedEnvironmentsAllocateNothing(bool destructor, string output)
    {
        var source = "struct Zero\n" + (destructor ? "    drop => Console.writeLine(\"zero\")\n" : string.Empty) +
            "func keep<T>(value: T) -> () -> i32\n    T is Owned\n    let action = func [value@move] () -> i32 => 7\n    return action@move\nlet result = keep(Zero.init())\nrequire result() == 7 else => $abort(\"zero\")";

        // A zero-sized environment is stored in the Function value's word, also one that needs destruction (PD19, IMPL 21.1.3; DECISIONS
        // R5-M104).
        NativeAllocationAudit.WriteFixture("GenericClosureIdentityZero" + destructor, source, 0, 0, 0, output);
    }

    // SPEC 6.1.3, 6.3.3 (DECISIONS BU-60, PLAN G300): a Closure in a generic struct's Field initializer holds its Type's instance, wherever
    // it is read: through another instance in a member (Box<W<T>> in Box<T>'s, Box<B> in Box<A>'s, stored locally or erased), through
    // a generic function's Box<U>, a nested Type's Outer<T>.Inner<B> in Outer's and Inner's members, a derived Type's base Field, a
    // projection's Box<S.Item> beside Box<A>, at the top level with a signature naming T, and in a function its holder's generic caller
    // passes on. Each run observes T, so another instance's body would abort (before: the EnclosingInstance gap, wrong code or
    // CheckFaulted_Kd under the first designs).
    [Theory]
    [MemberData(nameof(HeldCases))]
    public void AFieldInitializersClosureRunsInItsTypesInstance(string name, string[] lines)
        => ScalarEmissionTest.EmitFixture("GenericClosureHeld" + name, string.Join('\n', lines) + "\n", "ok\n");

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmLengthOnlyContextsAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze(Identity + "let empty: [0 of i32] = []\n_ = identity(empty)\n_ = identity([1, 2])");
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid, MinimalEmissionTest.Describe(c));
    }
}
