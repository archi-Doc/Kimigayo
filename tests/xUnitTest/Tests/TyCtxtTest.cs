// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>The Type context (<c>Compilation.Types</c>, docs/dev/COMPILER_ARCHITECTURE.md, R2b): a substitution is interned, so a warm
/// lookup returns the same object and allocates nothing, and a call's, closure's or function item's substitution applied to a declared
/// Type is the instantiation Binding makes for it, over the Kimi library and every milestone program.</summary>
public sealed class TyCtxtTest
{
    // A generic function whose closure an instance closes, called with two lengths.
    private const string ClosureSource = "func identity<length N>(value: [N of i32]) -> [N of i32]\n    let action = func [] (inner: [N of i32]) -> [N of i32] => inner\n" +
        "    return action(value)\n_ = identity([1, 2])\n_ = identity([5])\n";

    // A generic function with a closure, called with two borrows of different Origins, and a nongeneric function with Origins.
    private const string EraseSource = "func keep<T>(value: T) -> T\n    T is Copy\n    let probe = func [] (x: T) -> T => x\n    return probe(value)\n" +
        "func host(a: ref/i32, b: ref/i32) -> i32\n    _ = keep(a)\n    _ = keep(b)\n    return 0\nlet x = 1\nlet y = 2\n_ = host(x@ref, y@ref)\n";

    // Defaults that call a generic function, a length-generic one, one with an Origin, a requirement and, in a generic struct's method, a
    // function over the struct's Type parameter.
    private const string ComposeSource = "func copy<T>(x: T) -> T\n    T is Copy\n    return x\nfunc f<T>(x: T, y: T = copy(x)) -> T\n    T is Copy\n    return y\n" +
        "func count<length N>(values: ref/[N of i32]) -> isize => values.length\nfunc size<length N>(values: ref/[N of i32], k: isize = count(values)) -> isize => k\n" +
        "func first<T>(values: ref/[2 of T] during a) -> ref/T during a => values[0]@ref/T\n" +
        "func pick<T>(values: ref/[2 of T] during s, r: ref/T during s = first(values)) -> ref/T during s => r\n" +
        "func same<T>(x: T, equal: bool = x == x) -> bool\n    T is Equatable\n    return equal\n" +
        "struct Box<T>\n    T is Copy\n    public var value: T\n    public init(value: T) => self.value = value\n    public func get(self: ref/Self) -> T => self.value\n" +
        "    public func orElse(self: ref/Self, other: T = self.get()) -> T => other\n" +
        "let fixed: [3 of i32] = [1, 2, 3]\nlet two: [2 of i32] = [4, 5]\nlet box = Box<i32>.init(6)\nlet p = pick(two@ref)\n" +
        "require f(7) == 7 and f(true) and size(fixed@ref) == 3 and p@follow == 4 and same(8) and same(true) and box.orElse() == 6 else => $abort(\"compose\")\n";

    // An Origin-generic function; a generic function with a closure and its specialization with one; a length-generic function with a
    // default; named functions nested one and two levels in a generic function and one in a block of it; one nested in a nongeneric function;
    // a generic struct whose method nests a function, with an accessor and a generic method whose specialization has a closure and a nested
    // function (the F3 reproducer spec2.kimi); a nongeneric struct with an accessor; a group's static.
    private const string InstanceSource = "func plain(value: ref/i32 during a) -> ref/i32 during a => value\n" +
        "func pass<T>(value: T) -> T\n    T is Copy\n    let keep = func [] (inner: T) -> T => inner\n    return keep(value)\n" +
        "specialize func pass<i32>(value: i32) -> i32\n    let keep = func [] (inner: i32) -> i32 => inner\n    return keep(value)\n" +
        "func size<length N>(values: ref/[N of i32], k: isize = values.length) -> isize => k\n" +
        "func outer<T>(x: T) -> T\n    T is Copy\n    func middle(y: T) -> T\n        func inner(z: T) -> T => z\n        return inner(y)\n" +
        "    if true\n        func scoped() -> i32 => 1\n        _ = scoped()\n    return middle(x)\n" +
        "func host() -> i32\n    func helper() -> i32 => 1\n    return helper()\n" +
        "struct Box<T>\n    T is Copy\n    public var value: T\n    public computed current: T\n        get() -> T => self.value\n" +
        "    public init(value: T) => self.value = value\n    public func get(self: ref/Self) -> T\n        func local() -> i32 => 1\n" +
        "        _ = local()\n        return self.value\n" +
        "    public func f<U>(self: ref/Self, x: U) -> T => self.value\n    specialize func f<i32>(self: ref/Self, x: i32) -> T\n" +
        "        let g = func [] (y: T) -> T => y\n        func nested() -> i32 => 1\n        _ = nested()\n        return g(self.value)\n" +
        "struct Fixed\n    public var value: i32\n    public computed current: i32\n        get() -> i32 => self.value\n    public init(value: i32) => self.value = value\n" +
        "group Limits\n    public let limit: i32 = seed()\n    public func seed() -> i32 => 3\n";

    // Generic functions whose requirement calls, in the closed calls below, run each witness kind: a conformance witness, an inherited one, one
    // through a conditional conformance of a Type with an Origin slot, one through a bound Contract reference (also written in a generic Type's
    // own parameter), an accessor-call and a standard property witness (get and set), a fixed array's entry, the integer Position and built-in
    // comparison and arithmetic (the latter through a bound reference).
    private const string WitnessSource = """
        contract Shape
            func area(self: ref/Self) -> i32
        struct Square
            Self is Shape
            public var side: i32
            public init(side: i32) => self.side = side
            public func area(self: ref/Self) -> i32 => self.side * self.side
        public open struct Base
            public var side: i32 = 2
            public func area(self: ref/Self) -> i32 => self.side
        public struct Leaf : Base
            Self is Shape
            public init() => ()
        struct Held<T> {a}
            Self is Shape when T is Copy
            public var value: ref/T during a
            public init(value: ref/T during a) => self.value = value
            public func area(self: ref/Self) -> i32 => 4
        contract Counted
            property count: i32 has get, set
        struct Tally
            Self is Counted
            public var count: i32 = 1
            public init() => ()
        struct Gauge
            Self is Counted
            var stored: i32 = 2
            public computed count: i32
                get() -> i32 => self.stored
                set(value: i32) -> () => self.stored = value
            public init() => ()
        struct Plain
            Self is Addable<i32>
            associate Output is i32
            public init() => ()
            public func added(self: ref/Self, right: ref/i32) -> i32 => 6
        struct Wrap<V>
            V is PrimitiveInteger
            Self is Addable<V>
            associate Output is i32
            public init() => ()
            public func added(self: ref/Self, right: ref/V) -> i32 => 7
        func measure<T>(x: ref/T) -> i32
            T is Shape
            return x.area()
        func bump<T>(x: uniq/T) -> i32
            T is Counted
            x.count = x.count + 1
            return x.count
        func add<T>(left: ref/T, right: ref/i32) -> i32
            T is Addable<i32>
            T.(Addable<i32>).Output is i32
            return left.added(right)
        func same<T>(x: T) -> bool
            T is Equatable
            return x == x
        func count<C>(items: ref/C) -> i32
            C is Iterable
            var n = 0
            for item in items => n += 1
            return n
        func at<P>(p: P, length: isize) -> Option<isize>
            P is Position
            return p.tryResolve(length)
        let square = Square.init(3)
        let leaf = Leaf.init()
        let seven = 7
        let held = Held<i32>.init(seven@ref)
        var tally = Tally.init()
        var gauge = Gauge.init()
        let plain = Plain.init()
        let wrap = Wrap<i32>.init()
        let five = 5
        let fixed: [3 of i32] = [1, 2, 3]
        require measure(square) == 9 and measure(leaf) == 2 and measure(held) == 4 else => $abort("shape")
        require bump(tally@uniq) == 2 and bump(gauge@uniq) == 3 else => $abort("count")
        require add(plain, 2) == 6 and add(wrap, 2) == 7 and add(five, 2) == 7 else => $abort("add")
        require same(8) and count(fixed) == 3 else => $abort("built in")
        _ = at(1, 3)
        """;

    // C → B → A with a default and a nonvirtual member: C's base calls run B's override, B's runs the original.
    private const string BaseCallHierarchy = VirtualDispatchTest.Hierarchy +
        "let c = C.init(3)@obj\nrequire c.value() == 34 and c.direct() == 14 else => $abort(\"base\")\n";

    // P → Q → R whose slot takes and returns a borrow: R's base call runs Q's override, Q's runs the original.
    private const string BorrowingHierarchy = "open struct P\n    public init() => ()\n" +
        "    public virtual func pick(self: objref/Self, values: ref/[2 of i32] during s) -> ref/i32 during s => values[0]@ref\n" +
        "open struct Q : P\n    public init() => ()\n" +
        "    override func pick(self: objref/Self, values: ref/[2 of i32] during s) -> ref/i32 during s => base.pick(values)\n" +
        "struct R : Q\n    public init() => ()\n" +
        "    override func pick(self: objref/Self, values: ref/[2 of i32] during s) -> ref/i32 during s => base.pick(values)\n" +
        "let r = R.init()@obj\nlet two: [2 of i32] = [4, 5]\nrequire r.pick(two@ref)@follow == 4 else => $abort(\"pick\")\n";

    // SPEC 8.2, 8.10: the declared parameter and result Types of the function a call's substitution instantiates are the call's
    // instantiation (SPEC 8.4.3: a requirement's under its conforming Self and the bound Contract reference it dispatches through, which
    // SubstOf records as its declaring Type; SPEC 6.4.4: a direct base call's function is the implementation it runs, in the Type
    // implementing it), a closure in such a Type has its instance's signature, and a function item's substitution applies as its context
    // does.
    [Theory]
    [MemberData(nameof(HirColumnsTest.Corpus), MemberType = typeof(HirColumnsTest))]
    public void ApplyIsTheInstantiation(string name)
    {
        var (c, module) = SemanticSnapshotTest.Bind(name);
        CheckInstantiations(c, module.RootKoto);
    }

    // SPEC 6.4.4: in A → B → C, C's base calls (from its override and, with a default, from a nonvirtual member) run B's override and
    // B's base call runs the original; each call is its implementation's instantiation and keeps the slot's contract, borrowed inputs
    // and results with their Origins included.
    [Theory]
    [InlineData(BaseCallHierarchy, 3, 2)]
    [InlineData(BorrowingHierarchy, 2, 1)]
    public void ADirectBaseCallIsItsImplementationsInstantiation(string source, int direct, int intoOverrides)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var calls = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Select(static x => x.CallOf())
            .Where(static x => x is { Kind: CalleeKind.Virtual, VirtualIsDirect: true }).ToArray();
        Assert.Equal(direct, calls.Length);
        Assert.Equal(intoOverrides, calls.Count(static x => !ReferenceEquals(x!.VirtualImplementation, x.Target.Declaration)));
        CheckInstantiations(c, c.Kotonoha.RootKoto);
    }

    // SPEC 8.2: a closure an instance closed is in that instance's substitution and has its signature; a closure no instance closed is
    // in the identity of the function enclosing it, which keeps every Type.
    [Fact]
    public void AClosureIsInItsInstancesSubstitution()
    {
        var c = MinimalEmissionTest.Analyze(ClosureSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var function = nodes.OfType<FunctionKoto>().Single(static x => x.Name == "identity");
        var closure = nodes.OfType<FunctionKoto>().Single(static x => x.IsAnonymous).ClosureOf()!;
        var calls = nodes.OfType<InvocationKoto>().Where(static x => x.CallOf()?.Target.Name == "identity").Select(static x => x.CallOf()!).ToArray();
        var closed = calls.Select(x => c.Binding.InstantiateStorageType(closure.EnvironmentType!, x)!).ToArray();
        Assert.All(closed, static x => Assert.NotNull(x.ClosureContext));
        Assert.Equal(calls.Select(c.Types.SubstOf), closed.Select(c.Types.SubstOfClosure));
        Assert.NotSame(c.Types.SubstOf(calls[0]), c.Types.SubstOf(calls[1]));
        Assert.All(closed, x => Assert.Same(c.Binding.ClosureSignature(x), c.Types.Apply(closure.Signature, c.Types.SubstOfClosure(x)!)));
        var identity = c.Types.SubstOfClosure(closure.EnvironmentType!)!;
        Assert.Same(c.Types.Identity(function), identity);
        Assert.Same(closure.Signature, c.Types.Apply(closure.Signature, identity));
    }

    // SPEC 8.2, PLAN G142: a closure's context is the whole substitution of the instance that closed it, its conforming Self included, so two
    // substitutions that differ only in Self close it twice. No source call gives a body with a closure a Self (a Contract declares no default
    // implementation, SPEC 8.6.1), so the test synthesizes the two calls.
    [Fact]
    public void AClosureContextKeepsTheConformingSelf()
    {
        var c = MinimalEmissionTest.Analyze(ClosureSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var closure = nodes.OfType<FunctionKoto>().Single(static x => x.IsAnonymous).ClosureOf()!;
        var call = nodes.OfType<InvocationKoto>().Select(static x => x.CallOf()).First(static x => x?.Target.Name == "identity")!;
        var substs = new[] { BoundType.Boolean, BoundType.I32 }.Select(self => c.Types.SubstOf(Conforming(call, self))!).ToArray();
        var closed = substs.Select(x => c.Types.Apply(closure.EnvironmentType!, x)!).ToArray();
        Assert.Equal(substs, closed.Select(c.Types.SubstOfClosure));
        Assert.NotSame(closed[0], closed[1]);

        static CallPlan Conforming(CallPlan call, BoundType self)
        {
            var synthesized = new CallPlan();
            synthesized.Set(
                call.Target,
                call.ReturnType,
                null,
                [],
                call.TypeArguments,
                conformingType: self,
                declaringType: call.DeclaringType,
                origins: call.Origins,
                inputOrigins: call.InputOrigins,
                lengthArguments: call.LengthArguments);
            return synthesized;
        }
    }

    // SPEC 8.13.3: a substitution holds one length entry per slot whichever producer forms it, so a direct call, a function item and a
    // synthesized call of one instance are one substitution.
    [Fact]
    public void ASubstitutionHasOneLengthEntryPerSlot()
    {
        var c = MinimalEmissionTest.Analyze("func make<T>() -> i32 => 1\nlet f = make<i32>\nrequire make<i32>() == f() else => $abort(\"make\")\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var call = nodes.OfType<InvocationKoto>().Select(static x => x.CallOf()).Single(static x => x is { Kind: CalleeKind.Function, Target.Name: "make" })!;
        var item = nodes.Select(static x => x.TypeOf()).First(static x => x is { Kind: BoundTypeKind.FunctionItem })!;
        var subst = c.Types.SubstOf(call)!;
        Assert.Equal(1, subst.Lengths.Length);
        Assert.Same(subst, c.Types.SubstOfItem(item));
        Assert.Same(subst, c.Types.Synthesized(call.Target, null, call.TypeArguments));
    }

    // SPEC 3.4.3, 13.5.5: selection follows a reference or object handle as a Deref and a pair layer, the original s/T or an application
    // s/U, as a PairLayer of the binder in scope, never a raw pointer or an owned value. SPEC 23.1: a Type is named as diagnostics spell it.
    [Fact]
    public void LayersAreReferencesHandlesAndPairLayers()
    {
        var c = MinimalEmissionTest.Analyze("func f<s/T>(value: s/T, count: s/i32) -> s/T\n    s is owner or uniq\n    T is Copy\n    _ = count\n    return value\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var types = c.Types;
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "f");
        var pair = function.Parameters[0].Type.TypeOf()!;
        var application = function.Parameters[1].Type.TypeOf()!;
        var binders = new List<PairBinder>();
        PairBinder.Collect(function, binders);
        Assert.Equal(HirPlaceKind.Deref, TyCtxt.Layer(types.Reference(SemanticsKind.Uniq, BoundType.I32), out var below, out var binder));
        Assert.Same(BoundType.I32, below);
        Assert.Null(binder);
        Assert.Equal(HirPlaceKind.Deref, TyCtxt.Layer(types.Reference(SemanticsKind.ObjRef, BoundType.I32), out _, out _));
        Assert.Equal(HirPlaceKind.Value, TyCtxt.Layer(types.Pointer(BoundType.I32), out below, out binder));
        Assert.Equal((types.Pointer(BoundType.I32), null), (below, binder));
        Assert.Equal(HirPlaceKind.Value, TyCtxt.Layer(types.Tuple([BoundType.I32, BoundType.Boolean]), out _, out _));
        Assert.Equal(HirPlaceKind.PairLayer, TyCtxt.Layer(pair, out below, out binder));
        Assert.Same(Assert.Single(binders).Target, binder);
        Assert.Equal(HirPlaceKind.Value, TyCtxt.Layer(below, out _, out binder));
        Assert.Null(binder);
        Assert.Equal((BoundTypeKind.SemanticsApplication, HirPlaceKind.PairLayer), (application.Kind, TyCtxt.Layer(application, out below, out binder)));
        Assert.Equal((BoundType.I32, binders[0].Target), (below, binder));
        Assert.Equal(["uniq/i32", "s/T", "s/i32"], [TyCtxt.Name(types.Reference(SemanticsKind.Uniq, BoundType.I32)), TyCtxt.Name(pair), TyCtxt.Name(application)]);
    }

    // SPEC 6.1.3, 8.13, 8.15.1: the body of every MIR key symbol is instantiated exactly when the symbol or a declaration enclosing it has a
    // Type, Semantics or length slot, even an unused one; an Origin slot does not count. A named function nested two levels or in a block of a
    // generic function, or in a generic struct's method, is instantiated as one level is; a specialization declares no slot (SPEC 8.13.1), so a
    // top-level one is closed and one in a generic struct, with what it nests, is generic in the struct's slot. The old generator's view
    // counts the latter as closed (DECISIONS R5-M34, until G7).
    [Fact]
    public void InstantiatesFollowsTheEnclosingSlots()
    {
        var c = MinimalEmissionTest.Analyze(InstanceSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        string[] expected =
        [
            "Fn plain False", "Fn pass True", "Closure pass True", "Fn specialize pass False", "Closure specialize pass False", "Fn size True",
            "DefaultTemplate size True", "Fn outer True", "Fn middle True", "Fn inner True", "Fn scoped True", "Fn host False", "Fn helper False",
            "Fn init True", "Getter current.Get True", "Fn get True", "Fn local True", "Fn f True", "Fn specialize f True",
            "Closure specialize f True", "Fn nested True", "Fn init False", "Getter current.Get False", "StaticInit limit False", "Fn seed False",
        ];
        var keys = c.BuildMir().Bodies.ToArray().Where(static x => x.Key.Decl is not null && x.Source.Module == 0).Select(static x => x.Key).ToArray();
        var actual = keys.Select(x => $"{x.Kind} {Label(x.Decl!.Declaration)} {c.Types.Instantiates(x.Decl!)}");
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
        var legacy = keys.Where(x => c.Types.InstantiatesInLegacy(x.Decl!) != c.Types.Instantiates(x.Decl!))
            .Select(x => $"{x.Kind} {Label(x.Decl!.Declaration)}");
        Assert.Equal(["Closure specialize f", "Fn nested", "Fn specialize f"], legacy.Order(StringComparer.Ordinal));

        // A closure is labeled by the function enclosing it.
        static string Label(Koto declaration) => declaration switch
        {
            FunctionKoto { IsAnonymous: true } => Label(Enclosing(declaration.Parent!)),
            FunctionKoto { IsSpecialization: true } function => "specialize " + function.Name,
            FunctionKoto function => function.Name,
            _ => ((VariableKoto)declaration).NameKoto.IdentifierName,
        };

        static FunctionKoto Enclosing(Koto node) => node as FunctionKoto ?? Enclosing(node.Parent!);
    }

    // DECISIONS R5-M39 (K package 4.1): Erase makes every Origin static and keeps everything else; two calls whose substitutions differ in
    // Origins only have one erased substitution, and every Type of the callee's body and closure, the closure's context included, erases
    // alike in their instances; erasing is idempotent and keeps a Type without Origins; a binder that does not instantiate has none.
    [Fact]
    public void EraseKeepsEverythingButOrigins()
    {
        var c = MinimalEmissionTest.Analyze(EraseSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var types = c.Types;
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var calls = nodes.OfType<InvocationKoto>().Select(static x => x.CallOf()).Where(static x => x?.Target.Name == "keep")
            .Select(x => types.SubstOf(x!)!).ToArray();
        Assert.Equal(2, calls.Length);
        Assert.NotSame(calls[0], calls[1]);
        var erased = types.Erase(calls[0])!;
        Assert.Same(erased, types.Erase(calls[1]));
        Assert.Same(erased, types.Erase(erased));
        Assert.Equal((SemanticsKind.Ref, BoundOrigin.Static), (erased.Types[0]!.Semantics, erased.Types[0]!.Origin));
        Assert.All(erased.Inputs.ToArray().Concat(erased.Origins.ToArray()), static x => Assert.Same(BoundOrigin.Static, x));
        var locals = c.BuildMir().Bodies.ToArray().Where(static x => x.Key.Decl?.Name == "keep" || (x.Key.Kind == MirBodyKind.Closure && x.Source.Module == 0))
            .SelectMany(static x => x.Locals.ToArray()).Select(static x => x.Type).ToArray();
        Assert.True(locals.Length > 4);
        foreach (var local in locals)
        {
            var first = types.Erase(types.Apply(local, calls[0])!);
            Assert.Same(first, types.Erase(types.Apply(local, calls[1])!));
            Assert.Same(first, types.Erase(first));
        }

        var closure = Assert.Single(nodes.OfType<FunctionKoto>(), static x => x.IsAnonymous).ClosureOf()!.EnvironmentType!;
        var closed = calls.Select(x => types.Apply(closure, x)!).ToArray();
        Assert.NotSame(closed[0], closed[1]);
        Assert.Same(types.Erase(closed[0]), types.Erase(closed[1]));
        Assert.Same(types.Erase(closed[0].ClosureContext!), types.Erase(closed[0]).ClosureContext);
        Assert.Same(BoundType.I32, types.Erase(BoundType.I32));
        Assert.Same(types.Tuple([BoundType.I32, BoundType.Boolean]), types.Erase(types.Tuple([BoundType.I32, BoundType.Boolean])));
        var host = nodes.OfType<InvocationKoto>().Select(static x => x.CallOf()).Single(static x => x?.Target.Name == "host")!;
        Assert.Null(types.Erase(types.SubstOf(host)!));
    }

    // K package 4.1: an erased struct or enum instance stores the erasure of what the instance stores, for every instance the MIR locals of
    // the library and the milestones hold (Layout.Of lays out erased Types).
    [Theory]
    [MemberData(nameof(HirColumnsTest.Corpus), MemberType = typeof(HirColumnsTest))]
    public void AnErasedInstanceStoresTheErasedTypes(string name)
    {
        var (c, _) = SemanticSnapshotTest.Bind(name);
        var types = c.Types;
        var seen = new HashSet<BoundType>();
        var problems = new List<string>();
        var instances = 0;
        foreach (var body in c.BuildMir().Bodies)
        {
            foreach (var local in body.Locals)
            {
                Visit(local.Type);
            }
        }

        Assert.True(problems.Count == 0 && instances > 0, $"{instances} instances; {problems.Count} differ:\n{string.Join('\n', problems.Take(20))}");

        void Visit(BoundType type)
        {
            if (!seen.Add(type))
            {
                return;
            }

            foreach (var part in type.Components)
            {
                Visit(part);
            }

            if (AdtDef.IsStruct(type) || AdtDef.IsEnum(type))
            {
                instances++;
                if (!Stored(type).Select(x => x is null ? null : types.Erase(x)).SequenceEqual(Stored(types.Erase(type))))
                {
                    problems.Add(TyCtxt.Name(type));
                }
            }
        }
    }

    // Plan 5.6.2: a call in an omitted default composed with the substitution of the call omitting it is the call Binding forwards into that
    // instance (InstantiateForwardedCall), over the corpus and over generic, length, Origin, member and requirement calls in defaults.
    [Fact]
    public void ComposeIsTheForwardedInstantiation()
    {
        var problems = new List<string>();
        var corpus = 0;
        foreach (var name in HirColumnsTest.Corpus.Select(static x => x.Data))
        {
            var (c, module) = SemanticSnapshotTest.Bind(name);
            corpus += ComposeProblems(c, module, problems);
        }

        var direct = MinimalEmissionTest.Analyze(ComposeSource);
        Assert.True(direct.Binding.Result.IsComplete, MinimalEmissionTest.Describe(direct, null));
        var calls = ComposeProblems(direct, direct.Kotonoha, problems);
        Assert.True(corpus == 6 && calls == 7 && problems.Count == 0, $"{corpus} corpus and {calls} direct nested calls; {problems.Count} differ:\n{string.Join('\n', problems.Take(40))}");

        // copy, count, first, same (a requirement, so the forwarding resolves its witness) and get bind a Type, a length, an Origin, Self and
        // a declaring Type of the instance.
        var composed = NestedCalls(direct.Kotonoha).Select(x => (x.Inner.Target.Name, Subst: direct.Types.Compose(direct.Types.SubstOf(x.Inner)!, direct.Types.SubstOf(x.Outer)!)!)).ToArray();
        Assert.Equal(["i32", "bool"], composed.Where(static x => x.Name == "copy").Select(static x => x.Subst.Types[0]!.Name));
        Assert.Equal(3, composed.Single(static x => x.Name == "count").Subst.Lengths[0]!.Value);
        Assert.Contains(composed.Single(static x => x.Name == "first").Subst.Origins.ToArray().Concat(composed.Single(static x => x.Name == "first").Subst.Inputs.ToArray()), static x => x is not null);
        Assert.Equal(["i32", "bool"], composed.Where(static x => x.Subst.Target.Declaration is FunctionKoto { IsRequirement: true }).Select(static x => x.Subst.Self!.Name));
        Assert.Equal("Box", composed.Single(static x => x.Name == "get").Subst.Declaring!.Name);
        Assert.Same(BoundType.I32, composed.Single(static x => x.Name == "get").Subst.Declaring!.Components[0]);
    }

    // DECISIONS R5-M61 (F package 4.5): a requirement call in a generic body, in the instance a closed call of that body makes, has under
    // Witness the target Binding's forwarding into that instance materializes (InstantiateForwardedCall), over every such call of the
    // generic library and the milestones and over WitnessSource's kinds; a bound Contract reference is the substitution's declaring Type.
    [Fact]
    public void WitnessIsTheForwardedRequirementCall()
    {
        var problems = new List<string>();
        var corpus = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in HirColumnsTest.Corpus.Select(static x => x.Data))
        {
            var (c, module) = SemanticSnapshotTest.Bind(name);
            WitnessProblems(c, module, name, corpus, problems);
        }

        var direct = MinimalEmissionTest.Analyze(WitnessSource);
        Assert.True(direct.Binding.Result.IsComplete, MinimalEmissionTest.Describe(direct, null));
        var kinds = new SortedDictionary<string, int>(StringComparer.Ordinal);
        WitnessProblems(direct, direct.Kotonoha, "direct", kinds, problems);
        var counts = $"corpus {Counts(corpus)}; direct {Counts(kinds)}";
        Assert.True(
            problems.Count == 0 && counts == "corpus Body 34, Builtin 2, Storage 1, bound 3; direct Body 11, Builtin 2, Storage 3, bound 3",
            $"{counts}; {problems.Count} differ:\n{string.Join('\n', problems.Take(40))}");

        static string Counts(SortedDictionary<string, int> kinds) => string.Join(", ", kinds.Select(static x => $"{x.Key} {x.Value}"));
    }

    // DECISIONS R5-M61: a conforming Self whose storage nests past Binding's depth bound has the Limit target, not an exception (a Self from
    // Compose never does: Apply refuses it first).
    [Fact]
    public void ADeepSelfHasTheLimitTarget()
    {
        var c = MinimalEmissionTest.Analyze(WitnessSource);
        var (inner, outer) = RequirementCalls(c, c.Kotonoha).First(static x => x.Inner.Target.Name == "area");
        var wrap = KotoTree.Walk(c.Kotonoha.RootKoto).Select(static x => x.TypeOf()).First(static x => x is { Symbol.Name: "Wrap" })!;
        var deep = BoundType.I32;
        for (var i = 0; i < 80; i++)
        {
            deep = c.Types.Intern(wrap.Kind, wrap.Symbol, wrap.Semantics, [deep]);
        }

        var call = new CallPlan();
        call.Set(inner.Target, inner.ReturnType, null, [], [], conformingType: deep);
        Assert.Equal(RequirementTargetKind.Limit, c.Types.Witness(inner.Target, inner.RequirementContract, c.Types.SubstOf(call)!).Kind);
        Assert.Equal(RequirementTargetKind.Body, Witness(c, (inner, outer)).Kind);
    }

    // A warm rebind keeps every call's substitution the same object and looking it up allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmSubstitutionsAreTheSameAndAllocateNothing()
    {
        var c = SemanticSnapshotTest.Bind("milestone/Milestone34").Compilation;
        var calls = KotoTree.Walk(c.Kotonoha.RootKoto).Concat(KotoTree.Walk(c.Library.Kotonoha.RootKoto))
            .SelectMany(static x => Enumerable.Range(0, 8).Select(i => (Node: x, Ordinal: i)).TakeWhile(static y => CallPlan.Of(y.Node, y.Ordinal) is not null))
            .Where(x => c.Types.SubstOf(CallPlan.Of(x.Node, x.Ordinal)!) is not null).ToArray();
        var substs = calls.Select(x => c.Types.SubstOf(CallPlan.Of(x.Node, x.Ordinal)!)).ToArray();
        var changed = 0;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            for (var i = 0; i < calls.Length; i++)
            {
                changed += ReferenceEquals(c.Types.SubstOf(CallPlan.Of(calls[i].Node, calls[i].Ordinal)!), substs[i]) ? 0 : 1;
            }
        }));
        Assert.Equal(0, changed);
        Assert.Contains(substs, static x => x is { IsIdentity: false, Types.Length: > 0 });
    }

    // A warm rebind composes each call in a default to the same substitution and allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmComposeIsTheSameAndAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(ComposeSource);
        var pairs = NestedCalls(c.Kotonoha).ToArray();
        var composed = pairs.Select(x => c.Types.Compose(c.Types.SubstOf(x.Inner)!, c.Types.SubstOf(x.Outer)!)).ToArray();
        var changed = 0;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            for (var i = 0; i < pairs.Length; i++)
            {
                changed += ReferenceEquals(c.Types.Compose(c.Types.SubstOf(pairs[i].Inner)!, c.Types.SubstOf(pairs[i].Outer)!), composed[i]) ? 0 : 1;
            }
        }));
        Assert.Equal(0, changed);
        Assert.Contains(composed, static x => x is { IsIdentity: false, Types.Length: > 0 });
    }

    // A warm rebind gives each requirement call of WitnessSource the same target in each instance, and asking again allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmWitnessIsTheSameAndAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(WitnessSource);
        var calls = RequirementCalls(c, c.Kotonoha).ToArray();
        var targets = calls.Select(x => Witness(c, x)).ToArray();
        var changed = 0;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            for (var i = 0; i < calls.Length; i++)
            {
                changed += Witness(c, calls[i]) == targets[i] ? 0 : 1;
            }
        }));
        Assert.Equal(0, changed);
        Assert.Contains(targets, static x => x.Kind == RequirementTargetKind.Body);
    }

    // Erasing the Types of a program's MIR locals again allocates nothing: each erased form is memoized like the interning.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmEraseAllocatesNothing()
    {
        var c = SemanticSnapshotTest.Bind("milestone/Milestone34").Compilation;
        var locals = c.BuildMir().Bodies.ToArray().SelectMany(static x => x.Locals.ToArray()).Select(static x => x.Type).Distinct().ToArray();
        var erased = locals.Select(c.Types.Erase).ToArray();
        var changed = 0;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            for (var i = 0; i < locals.Length; i++)
            {
                changed += ReferenceEquals(c.Types.Erase(locals[i]), erased[i]) ? 0 : 1;
            }
        }));
        Assert.Equal(0, changed);
        Assert.Contains(locals, x => !ReferenceEquals(c.Types.Erase(x), x));
    }

    // A warm rebind keeps whether each MIR key symbol is instantiated, and asking again allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmInstantiatesIsTheSameAndAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(InstanceSource);
        var definitions = c.BuildMir().Bodies.ToArray().Where(static x => x.Key.Decl is not null).Select(static x => x.Key.Decl!).ToArray();
        var instantiates = definitions.Select(c.Types.Instantiates).ToArray();
        var changed = 0;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            for (var i = 0; i < definitions.Length; i++)
            {
                changed += c.Types.Instantiates(definitions[i]) == instantiates[i] ? 0 : 1;
            }
        }));
        Assert.Equal(0, changed);
        Assert.Contains(true, instantiates);
        Assert.Contains(false, instantiates);
    }

    // A struct's Fields and base, or an enum's Case payloads.
    private static IEnumerable<BoundType?> Stored(BoundType type) => AdtDef.IsStruct(type)
        ? Enumerable.Range(0, AdtDef.Count(type)).Select(x => AdtDef.FieldType(type, x)).Append(AdtDef.Base(type)) : AdtDef.CaseTypes(type) ?? [];

    // Each call in a default a call omits, with that call.
    private static IEnumerable<(CallPlan Inner, CallPlan Outer)> NestedCalls(Kotonoha module)
    {
        foreach (var node in KotoTree.Walk(module.RootKoto))
        {
            for (var i = 0; CallPlan.Of(node, i) is { } outer; i++)
            {
                foreach (var omitted in outer.DefaultArguments.ToArray())
                {
                    foreach (var nested in KotoTree.Walk(DefaultDecl.Of(omitted).Expression))
                    {
                        for (var k = 0; CallPlan.Of(nested, k) is { } inner; k++)
                        {
                            if (inner.Kind != CalleeKind.Value)
                            {
                                yield return (inner, outer);
                            }
                        }
                    }
                }
            }
        }
    }

    // The forwarded call has the composed substitution and instantiates each declared Type of the callee as Apply does; a requirement call,
    // which the forwarding resolves to its witness, composes as applying the two substitutions in turn.
    private static int ComposeProblems(Compilation c, Kotonoha module, List<string> problems)
    {
        var count = 0;
        foreach (var (inner, outer) in NestedCalls(module))
        {
            count++;
            var composed = c.Types.Compose(c.Types.SubstOf(inner)!, c.Types.SubstOf(outer)!);
            var forwarded = inner.Kind == CalleeKind.Requirement ? null : c.Binding.InstantiateForwardedCall(inner, outer);
            var where = $"{module.Compilation.Kotonoha.Name} {outer.Target.Name} > {inner.Target.Name}";
            if (inner.Kind != CalleeKind.Requirement && (forwarded is null || !ReferenceEquals(c.Types.SubstOf(forwarded), composed)))
            {
                problems.Add($"{where}: {composed is null} {forwarded is null}");
                continue;
            }

            foreach (var declared in ((FunctionKoto)inner.Target.Declaration).Parameters.Select(static x => x.Type.TypeOf()).Append(inner.Target.Type))
            {
                var expected = declared is null ? null : forwarded is not null ? c.Binding.InstantiateStorageType(declared, forwarded)
                    : c.Types.Apply(declared, c.Types.SubstOf(inner)!) is { } once ? c.Types.Apply(once, c.Types.SubstOf(outer)!) : null;
                if (composed is null || !ReferenceEquals(declared is null ? null : c.Types.Apply(declared, composed), expected))
                {
                    problems.Add($"{where} {declared}: {expected}");
                }
            }
        }

        return count;
    }

    private static void CheckInstantiations(Compilation c, Koto root)
    {
        var problems = new List<string>();

        // SPEC 6.4.4: a direct base call uses the slot's public contract, so the inputs other than the receiver and the result of the
        // implementation it runs, under its substitution, are the slot's under the call's instantiation.
        void Contract(Koto node, FunctionKoto implementation, FunctionKoto slot, Subst subst, CallPlan call)
        {
            var count = slot.Parameters.Count;
            if (implementation.Parameters.Count != count)
            {
                problems.Add($"{node.CodeContext.SourceDocument?.Path}:{node.Span.Start} contract arity");
                return;
            }

            for (var i = 0; i <= count; i++)
            {
                var own = i < count ? implementation.Parameters[i].Type.TypeOf() : subst.Target.Type;
                var contract = i < count ? slot.Parameters[i].Type.TypeOf() : call.Target.Type;
                if (i != call.Target.ReceiverIndex && (own is null || contract is null ||
                    c.Types.Apply(own, subst) is not { } applied || !ReferenceEquals(applied, c.Binding.InstantiateStorageType(contract, call))))
                {
                    problems.Add($"{node.CodeContext.SourceDocument?.Path}:{node.Span.Start} contract {(i < count ? $"slot {i}" : "result")}");
                }
            }
        }

        void Check(Koto node, BoundType? declared, Subst subst, CallPlan context)
        {
            var applied = declared is null ? null : c.Types.Apply(declared, subst);
            var instance = declared is null ? null : Instance(c, declared, subst, context);
            if (!ReferenceEquals(applied, instance) ||
                (applied is { Kind: BoundTypeKind.Closure, Symbol.Declaration: FunctionKoto function } && function.ClosureOf() is { } closure &&
                    !ReferenceEquals(c.Binding.ClosureSignature(applied), c.Types.Apply(closure.Signature, c.Types.SubstOfClosure(applied)!))))
            {
                problems.Add($"{node.CodeContext.SourceDocument?.Path}:{node.Span.Start} {node.Akind} {declared}: {applied}");
            }
        }

        var calls = 0;
        foreach (var node in KotoTree.Walk(root))
        {
            for (var i = 0; CallPlan.Of(node, i) is { } call; i++)
            {
                if (c.Types.SubstOf(call) is { } subst && subst.Target.Declaration is FunctionKoto callee)
                {
                    calls++;
                    foreach (var parameter in callee.Parameters)
                    {
                        Check(node, parameter.Type.TypeOf(), subst, call);
                    }

                    Check(node, subst.Target.Type, subst, call);
                    if (call.Target.Declaration is FunctionKoto slot && !ReferenceEquals(callee, slot))
                    {
                        Contract(node, callee, slot, subst, call);
                    }
                }
            }

            if (node.TypeOf() is { Kind: BoundTypeKind.FunctionItem, Symbol.Declaration: FunctionKoto function } item && c.Binding.FunctionItemContext(item, false) is { } context)
            {
                foreach (var parameter in function.Parameters)
                {
                    Check(node, parameter.Type.TypeOf(), c.Types.SubstOfItem(item)!, context);
                }
            }
        }

        Assert.True(calls > 0 && problems.Count == 0, $"{calls} calls; {problems.Count} differ:\n{string.Join('\n', problems.Take(40))}");
    }

    // Binding's instantiation for a call's context. A requirement's, under its conforming Self and bound Contract reference, and a direct
    // base call's into an implementation other than the slot's original, of that implementation in the implementing Type (as
    // Binding.EffectBounds forms that call), have no CallPlan form: theirs is Binding's instantiation under the substitution once it names the
    // call's target and conforming Self (a base call's: the implementation, no Self, the implementing Type) and binds the call's Type and
    // length arguments and Origins. Any other call's is its CallPlan form.
    private static BoundType? Instance(Compilation c, BoundType declared, Subst subst, CallPlan context) => context switch
    {
        { Kind: CalleeKind.Requirement } when ReferenceEquals(subst.Target, context.Target) && ReferenceEquals(subst.Self, context.ConformingType) &&
            Binds(subst, context) => c.Binding.InstantiateStorageType(declared, subst),
        { Kind: CalleeKind.Requirement } => null,
        { Kind: CalleeKind.Virtual, VirtualIsDirect: true, VirtualImplementation: { } body } when !ReferenceEquals(body, context.Target.Declaration)
            => ReferenceEquals(subst.Binder, body) && subst.Self is null && ReferenceEquals(subst.Declaring, context.VirtualImplementingType) &&
                Binds(subst, context) ? c.Binding.InstantiateStorageType(declared, subst) : null,
        _ => c.Binding.InstantiateStorageType(declared, context),
    };

    // The substitution binds the call's Type and length arguments, declared Origins and input Origins.
    private static bool Binds(Subst subst, CallPlan call)
        => subst.Types.SequenceEqual(call.TypeArguments) && subst.Lengths.SequenceEqual(call.LengthArguments) &&
            subst.Origins.SequenceEqual(call.Origins) && subst.Inputs.SequenceEqual(call.InputOrigins);

    // Each requirement call in the body of a generic function, with each call of that function in a module whose instance closes the
    // requirement's Self, as InstantiateForwardedCall pairs them (a call in a nested function or closure is its own binder's).
    private static IEnumerable<(CallPlan Inner, CallPlan Outer)> RequirementCalls(Compilation c, Kotonoha module)
    {
        var bodies = new Dictionary<FunctionKoto, CallPlan[]>();
        foreach (var node in KotoTree.Walk(module.RootKoto))
        {
            for (var i = 0; CallPlan.Of(node, i) is { } outer; i++)
            {
                if (outer.Kind != CalleeKind.Function || outer.Target.Declaration is not FunctionKoto callee || !c.Types.Instantiates(outer.Target))
                {
                    continue;
                }

                if (!bodies.TryGetValue(callee, out var inner))
                {
                    inner = KotoTree.Walk(callee).Where(x => ReferenceEquals(Binder(x), callee))
                        .SelectMany(static x => Enumerable.Range(0, 8).Select(k => CallPlan.Of(x, k)).TakeWhile(static y => y is not null).OfType<CallPlan>())
                        .Where(static x => x.Kind == CalleeKind.Requirement).ToArray();
                    bodies.Add(callee, inner);
                }

                foreach (var call in inner)
                {
                    if (Composed(c, (call, outer))?.Self is { HasAbstractPart: false })
                    {
                        yield return (call, outer);
                    }
                }
            }
        }

        static FunctionKoto? Binder(Koto node) => node.Parent is not { } parent ? null : parent as FunctionKoto ?? Binder(parent);
    }

    // A requirement call's substitution in its caller's instance, and its target there.
    private static Subst? Composed(Compilation c, (CallPlan Inner, CallPlan Outer) call)
        => c.Types.Compose(c.Types.SubstOf(call.Inner)!, c.Types.SubstOf(call.Outer)!);

    private static RequirementTarget Witness(Compilation c, (CallPlan Inner, CallPlan Outer) call)
        => c.Types.Witness(call.Inner.Target, call.Inner.RequirementContract, Composed(c, call)!);

    // Each Witness of RequirementCalls agrees with the forwarded call (the CallPlan materializer's): a Body is the function the forwarded call
    // runs under its substitution; a Builtin the same compiler function, and a Storage witness the Field its bridge reads with the bridge's
    // operation, each under the requirement call's own substitution, which binds what the forwarded call binds; Refused has no forwarded
    // call; None and Limit occur for no closed Self of an accepted program. A bound Contract reference is the substitution's declaring Type.
    // The views leave Binding's records and the HIR version unchanged. Counts the targets by kind and the bound references.
    private static void WitnessProblems(Compilation c, Kotonoha module, string label, IDictionary<string, int> kinds, List<string> problems)
    {
        var version = HirTables.Publish(c);
        var issues = c.Binding.Issues.Count;
        var calls = RequirementCalls(c, module).Select(x => (x.Inner, x.Outer, Subst: Composed(c, x)!, Target: Witness(c, x))).ToArray();
        if (HirTables.Publish(c) != version || c.Binding.Issues.Count != issues)
        {
            problems.Add($"{label}: a view changed Binding's state");
        }

        foreach (var (inner, outer, subst, target) in calls)
        {
            Count(target.Kind.ToString());
            var where = $"{label} {outer.Target.Name} > {inner.Target.Name} on {TyCtxt.Name(subst.Self!)}: {target.Kind}";
            if (inner.RequirementContract is { Type: { } bound } contract && !ReferenceEquals(bound.Symbol, contract))
            {
                Count("bound");
                if (!ReferenceEquals(subst.Declaring, c.Binding.InstantiateStorageType(bound, outer)))
                {
                    problems.Add($"{where} bound reference");
                }
            }

            var forwarded = c.Binding.InstantiateForwardedCall(inner, outer);
            var agrees = target.Kind switch
            {
                RequirementTargetKind.Body => ReferenceEquals(target.Target, forwarded?.Target) && ReferenceEquals(target.Subst, c.Types.SubstOf(forwarded!)),
                RequirementTargetKind.Builtin => ReferenceEquals(target.Target, forwarded?.Target) && ReferenceEquals(target.Subst, subst) &&
                    Same(c.Types.SubstOf(forwarded!)!, subst),
                RequirementTargetKind.Storage => forwarded?.Target.Declaration is FunctionKoto { IsPropertyWitness: true, Accessor: { } bridge } function &&
                    ReferenceEquals(target.Target, bridge.Property.Symbol) && target.Access == Access(function) && ReferenceEquals(target.Subst, subst) &&
                    c.Types.SubstOf(forwarded) is { } storage && ReferenceEquals(storage.Declaring, subst.Self) && Same(storage, subst, self: false),
                RequirementTargetKind.Refused => forwarded is null,
                _ => false,
            };

            if (!agrees)
            {
                problems.Add($"{where}; forwarded {forwarded?.Target.Name}");
            }
        }

        void Count(string key) => kinds[key] = kinds.TryGetValue(key, out var count) ? count + 1 : 1;

        // A standard bridge's operation, by the body PropertyWitnessFunction gives it.
        static PropertyWitnessKind Access(FunctionKoto bridge) => bridge.ExpressionBody switch
        {
            ConversionKoto => PropertyWitnessKind.StorageBorrow,
            BinaryKoto { Akind: KotoKind.Equals } => PropertyWitnessKind.StorageSet,
            _ => PropertyWitnessKind.StorageCopy,
        };

        // Two substitutions bind the same Self (when asked), Types, lengths, Origins and inputs.
        static bool Same(Subst a, Subst b, bool self = true) => (!self || ReferenceEquals(a.Self, b.Self)) && a.Types.SequenceEqual(b.Types) &&
            a.Lengths.SequenceEqual(b.Lengths) && a.Origins.SequenceEqual(b.Origins) && a.Inputs.SequenceEqual(b.Inputs);
    }
}
