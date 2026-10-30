// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>The instance collector (R5 K1, K4a, K6, K6b, K7; K package 4.3-4.6, DECISIONS R5-M35, R5-M40, R5-M60, R5-M67, R5-M70): the roots
/// are the closed bodies of the root modules, a Fn, getter, setter, default-template or closure call is a target at its site with whether a
/// runtime path reaches it, a generic callee an instance keyed by its erased substitution, a requirement call, a formatting call and an
/// erased writer the witness they run in the instance, a comparison shim its leaf witnesses, a callee only the checks reach is in the checked
/// set, a dropped value its Type's glue and a static access its initializer, the admission limits and Binding's storage bound are located
/// records, the work grows linearly and collecting again allocates nothing.</summary>
public sealed partial class InstanceCollectorTest
{
    // A generic function over a Copy T, and a generic struct over one, whose bodies the cases complete.
    private const string Outer = "func outer<T>(x: T) -> T\n    T is Copy\n";
    private const string CopyBox = "struct Box<T>\n    T is Copy\n    public var value: T\n    public init(value: T) => self.value = value\n";

    // The F3 reproducers' nested functions (X40): one level into a generic function, one there calling a selected specialization, two
    // levels in, in a block, in a generic struct's method and in a specialization inside a generic struct; and in its getter and setter.
    private const string OneLevel = Outer + "    func middle(y: T) -> T => y\n    return middle(x)\n";

    private const string OneLevelSpecialized = "func describe<T>() -> i32 => 1\nspecialize func describe<i32>() -> i32 => 2\nfunc outer<T>(x: T) -> i32\n" +
        "    T is Copy\n    func inner() -> i32 => describe<T>()\n    return inner()\n";

    private const string TwoLevels = Outer + "    func middle(y: T) -> T\n        func inner(z: T) -> T => z\n        return inner(y)\n    return middle(x)\n";

    private const string InBlock = Outer + "    if true\n        func inner(z: T) -> T => z\n        return inner(x)\n    return x\n";

    private const string InMethod = CopyBox + "    public func get(self: ref/Self) -> T\n        func inner(z: T) -> T => z\n        return inner(self.value)\n" +
        Outer + "    return Box<T>.init(x).get()\n";

    private const string InGetter = CopyBox + "    public computed got: T\n        get() -> T\n            func inner(z: T) -> T => z\n" +
        "            return inner(self.value)\n" + Outer + "    return Box<T>.init(x).got\n";

    private const string InSetter = CopyBox + "    public computed put: T\n        get() -> T => self.value\n        set(value: T) -> ()\n" +
        "            func inner(z: T) -> T => z\n            self.value = inner(value)\n" + Outer + "    var box = Box<T>.init(x)\n    box.put = x\n" +
        "    return box.value\n";

    // A Closure in a generic struct's getter and in its setter.
    private const string AccessorClosures = CopyBox + "    public computed got: T\n        get() -> T\n            let f = func [] (y: T) -> T => y\n" +
        "            return f(self.value)\n        set(value: T) -> ()\n            let g = func [] (y: T) -> T => y\n            self.value = g(value)\n" +
        Outer + "    var box = Box<T>.init(x)\n    box.got = x\n    return box.got\n";

    private const string InSpecialization = CopyBox + "    public func f<U>(x: U) -> i32 => 1\n    specialize func f<i32>(x: i32) -> i32\n" +
        "        func inner(y: i32) -> i32 => y\n        return inner(2)\nfunc outer<T>(x: T) -> i32\n    T is Copy\n    return Box<T>.f(5)\n";

    // Generic functions whose bodies make Closures (one called, with a Closure in it; one never called), a function item constant and a
    // Callable parameter that is called.
    private const string ItemSource = "func apply<T>(x: T) -> T\n    T is Copy\n    let f = func [] (y: T) -> T\n        let g = func [] (z: T) -> T => z\n" +
        "        return g(y)\n    return f(x)\nfunc make<T>(x: T) -> T\n    T is Copy\n    let unused = func [] (y: T) -> T => y\n    return x\n" +
        "func pick<T>(value: T) -> T => value@move\nfunc one<T>() -> i32 => 1\nfunc run<F>(f: ref/F) -> i32\n    F is Callable<() -> i32>\n    return f()\n" +
        "let p = pick<i32>\n";

    // The first line of CLayoutP's constructor, the subject of its instance's by-value uses.

    // Generic functions that request an arithmetic shim: a requirement call, an operator, a function item's formation and call, and a
    // requirement call in a body instantiated per tag Type.
    private const string ShimQuotient = "func quotient<T>(left: ref/T, right: ref/T) -> T\n    T is Dividable<T>\n    T.(Dividable<T>).Output is T\n" +
        "    return left.divided(right)\n";

    private const string ShimHalf = "func half<T>(value: ref/T, two: ref/T) -> T\n    T is Dividable<T>\n    T.(Dividable<T>).Output is T\n" +
        "    return value / two\n";

    private const string ShimItem = "func viaItem<T>(left: ref/T, right: ref/T) -> T\n    T is Dividable<T>\n    T.(Dividable<T>).Output is T\n" +
        "    let operation = T.divided\n    return operation(left, right)\n";

    private const string ShimTagged = "func tagged<T, U>(left: ref/T, right: ref/T, tag: U) -> T\n    T is Dividable<T>\n    T.(Dividable<T>).Output is T\n" +
        "    return left.divided(right)\n";

    // Exactly 2^63 - 1 bytes (649657 * 92737 * 153092023), every count within 2^31 - 1.
    private const string Big = "[649657 of [92737 of [153092023 of u8]]]";

    // Uses of a Type one byte over the layout bound: two functions' parameters, and two callers passing theirs to a generic function, whose
    // records are each caller's parameter and argument and the generic instance's parameter.
    private const string Over = "func over(value: (" + Big + ", u8)) => ()";
    private const string Huge = "(" + Big + ", u8)"; // One byte over the bound.
    private const string Again = "func again(value: (" + Big + ", u8)) => ()";
    private const string One = "func one(value: (" + Big + ", u8)) => pass(value@move)";
    private const string Two = "func two(value: (" + Big + ", u8)) => pass(value@move)";
    private const string PassRecords = "OversizedLayout_Kd " + Huge + "|OversizedLayout_Kd value@move|OversizedLayout_Kd " + Huge +
        "|OversizedLayout_Kd value@move|OversizedLayout_Kd T";

    // A C-layout struct over a Type parameter, which a zero-sized argument makes store a zero-sized Field.
    private const string CLayoutP = "#Layout(\"C\")\nstruct P<T>\n    public var n: i32\n    public var value: T\n" +
        "    public init(n: i32, value: T)\n        self.n = n\n        self.value = value@move\n";

    // A user Dictionary key (a built-in key's equality is C8f's shim).
    private const string DictionaryKey = "struct Key\n    Self is Equatable\n    public let code: i64\n    public init(code: i64) => self.code = code\n" +
        "    public func equals(self: ref/Self, other: ref/Self) -> bool => self.code == other.code\n";

    // A Contract property a standard witness implements by a shared borrow and a set of a Field that needs a drop, and a generic function
    // using both.
    private const string HeldSource = "contract Held\n    property items: ref/Array<i32>\n        get() -> ref/Array<i32>\n" +
        "        set(value: Array<i32>) -> ()\nstruct Bag\n    Self is Held\n    public var items: Array<i32>\n    public init() => self.items = []\n" +
        "func refill<T>(x: uniq/T) -> i32\n    T is Held\n    x.items = [1, 2]\n    let view = x.items\n    return 1\n" +
        "var bag = Bag.init()\nrequire refill(bag@uniq) == 1 else => $abort(\"held\")\n";

    // A Type function requirement called on a generic function's Type parameter, which no local of its body stores.
    private const string TagSource = "contract Tag\n    func tag() -> i32\nstruct Box<T>\n    Self is Tag\n    public var value: T\n" +
        "    public init(value: T) => self.value = value@move\n    public func tag() -> i32 => 1\nfunc f<T>() -> i32\n    T is Tag\n    return T.tag()\n";

    // A struct that formats itself, a user BufferWriter, and generic functions formatting their Type parameter through Text.toString,
    // Utf8Writer.write and Text.tryFormat, with the struct and with i32, through erased writers over the user's sink and over a fixed buffer.
    private const string FormatSource = """
        struct Box
            Self is Utf8Format
            public var v: i32
            public init(v: i32) => self.v = v
            public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull> => writer.write(self.v)
        struct Sink
            Self is BufferWriter
            public init() => ()
            public func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull> => .Err(BufferFull.init())
        func show<T>(x: ref/T) -> string
            T is Utf8Format
            return Text.toString(x)
        func emit<T>(writer: uniq/Utf8Writer, x: ref/T) -> Result<(), BufferFull>
            T is Utf8Format
            return writer.write(x)
        func fill<T>(x: ref/T) -> i32
            T is Utf8Format
            var bytes = [8 of 0@u8]
            _ = Text.tryFormat(x, bytes@uniq)
            return 1
        func toSink() -> i32
            var sink = Sink.init()
            var writer = Text.writer(sink@uniq)
            _ = emit(writer@uniq, Box.init(5))
            return 1
        func toFixed() -> i32
            var bytes = [8 of 0@u8]
            var buffer = Text.fixed(bytes@uniq)
            var writer = Text.writer(buffer@uniq)
            _ = emit(writer@uniq, 6)
            return 1
        let one = show(Box.init(1))
        let two = show(2)
        require fill(Box.init(3)) + toSink() + toFixed() == 3 else => $abort("format")

        """;

    // An Equatable and a Comparable struct compared in tuples of borrows, nested, through generic functions' Contract mapping and by a tuple
    // operator in a generic function's instance.
    private const string CompareSource = """
        struct Box
            Self is Equatable
            public var v: i32
            public init(v: i32) => self.v = v
            public func equals(self: ref/Self, other: ref/Self) -> bool => self.v == other.v
        struct Rank
            Self is Comparable
            public var v: i32
            public init(v: i32) => self.v = v
            public func equals(self: ref/Self, other: ref/Self) -> bool => self.v == other.v
            public func compare(self: ref/Self, other: ref/Self) -> i32 => self.v - other.v
        func same<T>(a: ref/T, b: ref/T) -> bool
            T is Equatable
            return (a, 1) == (b, 1)
        func equal<T>(a: ref/T, b: ref/T) -> bool
            T is Equatable
            return a == b
        func differ<T>(a: ref/T, b: ref/T) -> bool
            T is Equatable
            return a != b
        func less<T>(a: ref/T, b: ref/T) -> bool
            T is Comparable
            return a < b
        let x = Box.init(1)
        let r = Rank.init(2)
        let pair = (x@ref, 1)
        let nest = ((x@ref, 2), x@ref)
        let ranked = (r@ref, 3)
        require equal(pair@ref, pair@ref) and equal(nest@ref, nest@ref) and not differ(pair@ref, pair@ref) else => $abort("equal")
        require less(ranked@ref, ranked@ref) and same(x@ref, x@ref) else => $abort("order")

        """;

    // A requirement Item of an Equatable Type parameter, called with a struct and with i32, and one of a bound Contract reference that the
    // Type conforms to twice, made, erased and called.
    private const string ItemWitnessSource = """
        struct Box
            Self is Equatable
            public var v: i32
            public init(v: i32) => self.v = v
            public func equals(self: ref/Self, other: ref/Self) -> bool => self.v == other.v
        contract Tag<R>
            func tag(x: R) -> i32
        struct Stamp
            Self is Tag<i32>
            Self is Tag<bool>
            public init() => ()
            public func tag(x: i32) -> i32 => x
            public func tag(x: bool) -> i32 => 2
        func same<T>(a: ref/T, b: ref/T) -> bool
            T is Equatable
            let f = T.equals
            return f(a, b)
        func pick<T>() -> i32
            T is Tag<bool>
            T is Owned
            let g: (bool) -> i32 = T.tag
            let h = T.tag
            return g(true) + h(false)
        let x = Box.init(1)
        require same(x@ref, x@ref) and same(1, 1) and pick<Stamp>() == 4 else => $abort("items")

        """;

    // A generic function nested in a closed function, and in a closed struct's getter, which have no instance to carry.
    private const string InClosed = "func host() -> i32\n    func pick<U>(x: U) -> U => x@move\n    return pick(1)\n" + Outer + "    _ = host()\n    return x\n";

    private const string InClosedGetter = "struct Fixed\n    public var value: i32\n    public init(value: i32) => self.value = value\n" +
        "    public computed got: i32\n        get() -> i32\n            func pick<U>(x: U) -> U => x@move\n            return pick(self.value)\n" + Outer +
        "    _ = Fixed.init(1).got\n    return x\n";

    // Closed functions, a generic one and a generic struct, a closure, calls only the checks reach, a computed property with both
    // accessors, mutually recursive defaults (template calls) and top-level statements that call them and the library.
    private const string Source = "func plain() -> i32 => 1\nfunc pick<T>(value: T) -> T => value\n" +
        "func host() -> i32\n    let add = func [] (x: i32) -> i32 => x + 1\n    return add(plain())\n" +
        "func after() -> i32\n    return 1\n    _ = later()\n    _ = Kimi.Time.Duration.init(microseconds: 5)\n    return 2\nfunc later() -> i32 => 3\n" +
        "func f(n: i32, value: i32 = if n == 0 => 1 else => g(n - 1)) -> i32 => value\n" +
        "func g(n: i32, value: i32 = if n == 0 => 2 else => f(n - 1)) -> i32 => value\n" +
        "struct Box<T>\n    public var value: T\n    public init(value: T) => self.value = value\n" +
        "struct Cell\n    var stored: i32\n    public computed value: i32\n        get(self: ref/Self) -> i32 => self.stored\n" +
        "        set(self: uniq/Self, value: i32) -> () => self.stored = value\n    public init(stored: i32) => self.stored = stored\n" +
        "var cell = Cell.init(3)\ncell.value = 4\nlet box = Box<i32>.init(cell.value)\nvar watch = Kimi.Time.Stopwatch.init()\n" +
        "require host() == 3 and pick(box.value) == 4 and after() == 1 and f(2) == 1 else => $abort(\"collect\")\n";

    // The F3 reproducers' shapes (X40): the declarations, the nested function, its items now and the refused call (null: none).
    public static TheoryData<string, string, int, string?> NestedCases => new()
    {
        { OneLevel, "middle", 2, null },
        { OneLevelSpecialized, "inner", 2, null },
        { TwoLevels, "inner", 0, "inner(y)" },
        { InBlock, "inner", 0, "inner(x)" },
        { InMethod, "inner", 0, "inner(self.value)" },
        { InSpecialization, "inner", 0, "inner(2)" },
        { InGetter, "inner", 0, "inner(self.value)" },
        { InSetter, "inner", 0, "inner(value)" },
        { InClosed, "pick", 1, null },
        { InClosedGetter, "pick", 1, null },
    };

    // DECISIONS BU-38 (PLAN G242; designs A and B, the review's combination C): programs whose instance (a local, a Type argument, a
    // requirement Item or a LegacyRow's collection), composed substitution (Self or a Type argument) or bodyless callee's parameter rests on
    // storage Binding rejected, with Binding's records and the collector's; and their genuine-limit twins, which keep the storage-nesting gap,
    // beside a failed part in either order too, and C8d's LegacyRow over a Type nested past the bound (before: InternalInvariant_Kd). DECISIONS
    // R5-M151 (PLAN G285): a virtual slot whose applicability rests on a constraint Binding rejected, in the clause and the Block form, which
    // the VTable leaves undecided (before: InternalInvariant_Kd at the creation). DECISIONS R5-M155 (PLAN G293): a local, a parameter (with a
    // generic subject), an Array's element (its glue's) and a captured value typed by a qualified projection that names no Type after
    // Binding's constraint error, an abstract Type Layout gives Rejected (before: Layout.Row threw, CheckFaulted_Kd).
    public static TheoryData<string, string, string, string> RejectedCases
    {
        get
        {
            const string VirtualBase = "open struct Base<T>\n    T is Owned\n    public init() => ()\n";
            const string Slot = "    public virtual func copy(self: objref/Self, value: ref/T) -> T";
            const string Create = "func g() => Base<i32>.init()@obj\n";
            const string Inspect = "func inspect<T>(value: ref/T) => ()\n";
            const string Bad = "struct Bad\n    public var m: Missing\n";
            const string Box = "struct B<T>\n    public var v: T\n";
            const string Zero = "contract Zero\n    func zero() -> i32\nfunc call<T>() -> i32\n    T is Zero\n    return T.zero()\nstruct Bad\n    Self is Zero\n    public var m: Missing\n";
            const string Format = "    Self is Utf8Format\n    public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull> => writer.write(1)\n";
            const string Named = "contract Named\n    func name(self: ref/Self) -> i32\nfunc call<T>(x: ref/T) -> i32\n    T is Named\n    return x.name()\nstruct Bad\n    Self is Named\n";
            const string HasItem = "contract HasItem\n    associate Item\nstruct HA<S>\n    S is HasItem\n    var x: S.Item\n";
            const string NoType = HasItem + "struct A\n    Self is HasItem\n    associate HasItem.Item is A.Item\n";
            const string BaseA = HasItem + "open struct BaseA\n    Self is HasItem\n    associate HasItem.Item is BaseA.Item\n";
            const string Inherited = "InvalidConstraint_Kd InvalidConstraint_Kd InvalidTypeFormation_Kd InvalidConstraint_Kd " +
                "InvalidConstraint_Kd InvalidConstraint_Kd";
            const string Holder = "contract Holder<K>\n    associate Item\nstruct HB<S>\n    S is Holder<i32>\n    var x: S.Item\n";
            const string Bindings = "    associate Holder<T>.Item is i32\n    associate Holder<U>.Item is u8\n";
            const string HAdd = "struct HAdd<S>\n    S is Addable<S>\n    public var x: S.Output\n";
            const string SrcC = HasItem + "struct SrcC<T>\n    Self is HasItem when T is Copy\n        associate HasItem.Item is T\n";
            const string Iter = "contract Iter<T>\n    associate Tag\n";
            const string IterB = Iter + "struct IB\n    Self is Iter<i32>\n    associate Tag is bool\n";
            const string IterG = HasItem + Iter + "struct SrcU\n    Self is HasItem\n    associate HasItem.Item is u8\nstruct IG<S>\n    S is HasItem\n" +
                "    Self is Iter<S.(HasItem).Item>\n    associate Tag is bool\n";
            var b65 = Nest("B", 65, "i32");
            return new()
            {
                { "enum", Inspect + "enum E {s}\n    A(Missing)\n    B(ref/i32 during s)\nfunc g()\n    var v = 1\n    let e: E = .B(v@ref)\n    v = 2\n    inspect(e@ref)\n", "UnresolvedBinding_Kd", string.Empty },
                { "witness", Named + "    public var m: Missing\n    public func name(self: ref/Self) -> i32 => 1\nfunc g(b: ref/Bad) -> i32 => call(b)\n", "UnresolvedBinding_Kd", string.Empty },
                { "local", "struct Bad<T>\n    public var v: T\n    public var m: Missing\nfunc make<T>() -> i32\n    let v: Option<T> = .None\n    return 0\nfunc g() -> i32 => make<Bad<i32>>()\n", "UnresolvedBinding_Kd", string.Empty },
                { "handle", Inspect + Bad + "struct Sx\n    public var x: rc/Bad\nfunc h(s: ref/Sx) => inspect(s)\n", "UnresolvedBinding_Kd", string.Empty },
                { "static", Zero + "    public func zero() -> i32 => 1\nfunc g() -> i32 => call<Bad>()\n", "UnresolvedBinding_Kd", string.Empty },
                { "item", Zero.Replace("    return T.zero()", "    let f = T.zero\n    return f()", StringComparison.Ordinal) + "    public func zero() -> i32 => 1\nfunc g() -> i32 => call<Bad>()\n", "UnresolvedBinding_Kd", string.Empty },
                { "compose", "struct Bad<T>\n    public var v: T\n    public var m: Missing\nfunc make<T>() -> i32 => 0\nfunc outer<T>() -> i32 => make<Bad<T>>()\nfunc g() -> i32 => outer<i32>()\n", "UnresolvedBinding_Kd", string.Empty },
                { "outer", Bad + "func inner<T>() => ()\nfunc outer<T>() => inner<T>()\nfunc g() => outer<Bad>()\n", "UnresolvedBinding_Kd", string.Empty },
                { "toString", "struct Bad\n" + Format + "    public var m: Missing\nfunc g(b: ref/Bad) -> string => Text.toString(b)\n", "UnresolvedBinding_Kd", string.Empty },
                { "write", "struct Bad\n" + Format + "    public var m: Missing\nfunc g(w: uniq/Utf8Writer, b: ref/Bad) -> Result<(), BufferFull> => w.write(b)\n", "UnresolvedBinding_Kd", string.Empty },
                { "replace", Bad + "func g(slot: uniq/Option<Bad>) => Kimi.Intrinsics.replace(slot, with: .None)\n", "UnresolvedBinding_Kd", string.Empty },
                { "replaceGeneric", Bad + "func put<T>(slot: uniq/Option<T>) => Kimi.Intrinsics.replace(slot, with: .None)\nfunc g(slot: uniq/Option<Bad>) => put(slot)\n", "UnresolvedBinding_Kd", string.Empty },
                { "row", Bad + "struct Sx\n    public var x: Array<Bad>\nfunc g(s: uniq/Sx) => s.x.reserve(1)\n", "UnresolvedBinding_Kd", string.Empty },
                { "noType", NoType + "func g(b: HA<A>) => ()\n", "InvalidConstraint_Kd InvalidConstraint_Kd InvalidConstraint_Kd InvalidConstraint_Kd", string.Empty },
                { "closedPrimitive", HasItem + "func g(h: HA<i32>) => ()\n", "UnsatisfiedConstraint_Kd UnsatisfiedConstraint_Kd", string.Empty },
                { "closedTuple", HasItem + "func g(h: HA<(i32, u8)>) => ()\n", "UnprovenConstraint_Kd UnprovenConstraint_Kd", string.Empty },
                { "closedArray", HasItem + "func g(h: HA<[2 of i32]>) => ()\n", "UnprovenConstraint_Kd UnprovenConstraint_Kd", string.Empty },
                {
                    "closedProjection", HasItem + "struct Src\n    Self is HasItem\n    associate HasItem.Item is raw/u8\nfunc g(h: HA<Src.Item>) => ()\n",
                    "UnprovenConstraint_Kd UnprovenConstraint_Kd", string.Empty
                },
                { "closedOutput", HAdd + "func g(h: HAdd<bool>) => ()\n", "UnsatisfiedConstraint_Kd UnsatisfiedConstraint_Kd", string.Empty },
                { "closedOutputValid", HAdd + "func g(h: HAdd<i32>) -> i32 => h.x\n", string.Empty, string.Empty },
                {
                    "closedRefuted", SrcC.Replace("Item is T", "Item is i32", StringComparison.Ordinal) + "func g(h: HA<SrcC<string>>) => ()\n",
                    "UnsatisfiedConstraint_Kd UnsatisfiedConstraint_Kd", string.Empty
                },
                {
                    "closedPayload", HasItem + "enum EW<S>\n    S is HasItem\n    A(S.Item)\n    B\nfunc g(e: EW<i32>) => ()\n",
                    "UnsatisfiedConstraint_Kd UnsatisfiedConstraint_Kd", string.Empty
                },
                { "inherited", BaseA + "struct DerA : BaseA\n    var m: i32\nfunc g(b: HA<DerA>) => ()\n", Inherited, string.Empty },
                {
                    "inheritedTwice", BaseA + "open struct DerA : BaseA\n    var m: i32\nstruct DerB : DerA\n    var n: i32\nfunc g(b: HA<DerB>) => ()\n",
                    "InvalidConstraint_Kd InvalidConstraint_Kd InvalidTypeFormation_Kd InvalidConstraint_Kd InvalidTypeFormation_Kd " +
                    "InvalidConstraint_Kd InvalidConstraint_Kd InvalidConstraint_Kd", string.Empty
                },
                {
                    "inheritedGeneric", HasItem + "open struct BaseG<T>\n    Self is HasItem\n    associate HasItem.Item is BaseG<T>.Item\n    var t: T\n" +
                    "struct DerG : BaseG<i32>\n    var m: i32\nfunc g(b: HA<DerG>) => ()\n", Inherited, string.Empty
                },
                {
                    "sourcesDisagree", Holder + "contract Sub<A, B> : Holder<A>, Holder<B>\nopen struct BaseP<T, U>\n    Self is Sub<T, U>\n" + Bindings +
                    "struct DerP : BaseP<i32, i32>\n    var m: i32\nfunc g(h: HB<DerP>) => ()\n",
                    "IncompatibleContractImplementation_Kd InvalidConstraint_Kd InvalidConstraint_Kd", string.Empty
                },
                {
                    "pathsDisagree", Holder + "open struct BaseP<T, U>\n    Self is Holder<T>\n    Self is Holder<U>\n" + Bindings +
                    "struct DerP : BaseP<i32, i32>\n    var m: i32\nfunc g(h: HB<DerP>) => ()\n",
                    "DuplicateBinding_Kd DuplicateBinding_Kd InvalidConstraint_Kd InvalidConstraint_Kd InvalidConstraint_Kd", string.Empty
                },
                {
                    "refinedDisagree", HasItem + "contract HasMore: HasItem\n    associate Elem\n    associate HasItem.Item is Elem\nstruct Src\n" +
                    "    Self is HasMore\n    associate HasMore.Elem is i32\nstruct WB<S>\n    S is HasItem\n    Self is HasItem\n    associate HasItem.Item is u8\n" +
                    "    Self is HasMore when S is HasMore\n        associate HasMore.Elem is i32\nfunc g(h: HA<WB<Src>>) => ()\n",
                    "InvalidConstraint_Kd InvalidAssociatedType_Kd InvalidConstraint_Kd InvalidConstraint_Kd", string.Empty
                },
                {
                    "noConformance", HasItem + "open struct BaseG<T>\n    Self is HasItem\n    associate HasItem.Item is T\n    var t: T\n" +
                    "struct DerX : BaseG\n    var m: i32\nfunc g(b: HA<DerX>) => ()\n",
                    "InvalidTypeFormation_Kd InvalidTypeFormation_Kd InvalidConstraint_Kd InvalidConstraint_Kd", string.Empty
                },
                {
                    "noConformanceArity", HasItem + "open struct BaseG<T>\n    Self is HasItem\n    associate HasItem.Item is T\n    var t: T\n" +
                    "struct DerX : BaseG<i32, u8>\n    var m: i32\nfunc g(b: HA<DerX>) => ()\n",
                    "InvalidTypeFormation_Kd InvalidTypeFormation_Kd InvalidTypeFormation_Kd InvalidConstraint_Kd InvalidConstraint_Kd", string.Empty
                },
                {
                    "virtualClause", VirtualBase + Slot + "\n        T is Missing\n        return value@follow\n" + Create,
                    "InvalidConstraint_Kd UnresolvedBinding_Kd InvalidConstraint_Kd", string.Empty
                },
                {
                    "virtualBlock", "contract Marker\n" + VirtualBase + "    Self is Marker when T is Missing\n    " + Slot + " => value@follow\n" + Create,
                    "InvalidConstraint_Kd InvalidConstraint_Kd InvalidConstraint_Kd UnresolvedBinding_Kd InvalidConstraint_Kd", string.Empty
                },
                { "limitBesideError", Inspect + Box + $"func h(b: ref/{b65}) => inspect(b)\nfunc other() -> i32 => missing\n", "UnresolvedBinding_Kd", "Unsupported_Kd inspect(b)" },
                { "limitBesideField", Inspect + Box + $"struct Bad\n    public var m: Missing\n    public var big: {b65}\nfunc h(b: ref/Bad) => inspect(b)\n", "UnresolvedBinding_Kd", "Unsupported_Kd inspect(b)" },
                { "limitAfterLocal", Box + $"struct Bad<T>\n    public var v: T\n    public var m: Missing\nfunc two<T>(a: ref/Bad<T>, b: ref/{Nest("B", 65, "T")}) => ()\nfunc g(x: ref/Bad<i32>, y: ref/{b65}) => two(x, y)\n", "UnresolvedBinding_Kd", "Unsupported_Kd two(x, y)" },
                { "limitBeforeLocal", Box + $"struct Bad<T>\n    public var v: T\n    public var m: Missing\nfunc two<T>(b: ref/{Nest("B", 65, "T")}, a: ref/Bad<T>) => ()\nfunc g(x: ref/Bad<i32>, y: ref/{b65}) => two(y, x)\n", "UnresolvedBinding_Kd", "Unsupported_Kd two(y, x)" },
                { "staticLimit", Box + Zero + $"    public var b: {Nest("B", 64, "i32")}\n    public func zero() -> i32 => 1\nfunc g() -> i32 => call<Bad>()\n", "UnresolvedBinding_Kd", "Unsupported_Kd T.zero()" },
                { "outerLimit", Box + $"func inner<T>() => ()\nfunc outer<T>() => inner<T>()\nfunc g() => outer<{b65}>()\n", string.Empty, "Unsupported_Kd inner<T>()" },
                { "toStringLimit", "struct B<T>\n" + Format + $"    public var v: T\nfunc g(b: ref/{b65}) -> string => Text.toString(b)\n", string.Empty, "Unsupported_Kd Text.toString(b)" },
                { "rowLimit", Box + $"func g(a: uniq/Array<{b65}>) => a.reserve(1)\n", string.Empty, "Unsupported_Kd a.reserve(1)" },
                {
                    "localProjection", IterB + "func g() -> ()\n    let t: IB.(Iter<u8>).Tag = true\n    return\n",
                    "TypeMismatch_Kd UnsatisfiedConstraint_Kd", string.Empty
                },
                { "parameterProjection", IterG + "func g(t: IG<SrcU>.(Iter<i32>).Tag) => ()\n", "UnsatisfiedConstraint_Kd", string.Empty },
                { "elementProjection", IterB + "func g(a: Array<IB.(Iter<u8>).Tag>) => ()\n", "UnsatisfiedConstraint_Kd", string.Empty },
                {
                    "capturedProjection", IterB + "func g(t: IB.(Iter<u8>).Tag) -> ()\n    let c = func [t@move] () -> () => ()\n    return\n",
                    "UnsatisfiedConstraint_Kd Unsupported_Kd", string.Empty
                },
            };
        }
    }

    // K 4.4: every body of the root module whose definition does not instantiate, the top-level statements, the closure of a closed
    // function, both accessors of a closed struct and the default templates among them; generic bodies and library bodies only as the
    // targets of calls.
    [Fact]
    public void TheRootsAreTheClosedBodiesOfTheRootModules()
    {
        var (program, set) = Collect(Source);
        Assert.Equal(
            [
                "Closure host", "DefaultTemplate f", "DefaultTemplate g", "Fn after", "Fn f", "Fn g", "Fn host", "Fn init", "Fn later", "Fn plain",
                "Fn top", "Getter value.Get", "Setter value.Set",
            ],
            set.Items[..set.RootCount].ToArray().Select(x => Label(program, x)).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["Fn init [Box<i32>]", "Fn pick [i32]", "library Fn init in struct Duration", "library Fn init in struct Stopwatch"],
            set.Items[set.RootCount..].ToArray().Select(x => Label(program, x)).Order(StringComparer.Ordinal));
    }

    // K 4.5 rows 1, 5 and 11: each callee is a target at its call, in location order (a default omitted by a call of f is its template's
    // call, the Closure is a target where its environment is made and where it is called through a borrow, pick and Box's init are
    // instances); every target is at a call or at one of the statements rows 11-13 name.
    [Fact]
    public void ClosedCallsAreTargetsInLocationOrder()
    {
        var (program, set) = Collect(Source);
        Assert.Equal(["Closure host", "Fn plain", "Closure host"], Targets(program, set, "Fn host"));
        Assert.Equal(["DefaultTemplate g", "Fn g"], Targets(program, set, "DefaultTemplate f"));
        Assert.Equal(
            [
                "Fn init", "Setter value.Set", "Getter value.Get", "Fn init [Box<i32>]", "library Fn init in struct Stopwatch", "Fn host", "Fn pick [i32]",
                "Fn after", "DefaultTemplate f", "Fn f",
            ],
            Targets(program, set, "Fn top"));
        for (var item = 0; item < set.Items.Length; item++)
        {
            var targets = set.Targets(item).ToArray();
            Assert.Equal(targets.OrderBy(static x => x.At.Block).ThenBy(static x => x.At.Index), targets);
            var body = program.Bodies[set.Items[item].BodyOrdinal];
            Assert.All(targets, x => Assert.True(x.At.Index == body.Blocks[x.At.Block].Count
                ? body.Blocks[x.At.Block].Terminator.Kind == MirTerminatorKind.Call
                : body.Statements[body.Blocks[x.At.Block].Start + x.At.Index] is
                    { Kind: MirStatementKind.Assign, Rvalue.Kind: MirRvalueKind.Aggregate or MirRvalueKind.Cast or MirRvalueKind.Use }));
        }
    }

    // SPEC 8.15.3, K 4.5: a call only the checks reach (after a return) is a target without the runtime bit, and a library body only such a
    // call reaches is in the checked set.
    [Fact]
    public void ACheckOnlyCallKeepsItsCalleeInTheCheckedSet()
    {
        var (program, set) = Collect(Source);
        Assert.Equal(["Fn later (check)", "library Fn init in struct Duration (check)"], Targets(program, set, "Fn after"));
        Assert.All(set.Targets(Item(program, set, "Fn top")).ToArray(), static x => Assert.True(x.Runtime));
    }

    // K 4.10 (DECISIONS R5-M77): the Generated column holds exactly the roots Mark is given and the items their runtime targets reach,
    // transitively; a callee only the checks reach stays checked only, a mark replaces the previous one and a new collection has none.
    [Fact]
    public void TheMarkIsTheRuntimeClosureOfItsRoots()
    {
        var c = MirBuildTest.Compile(Source);
        var program = c.BuildMir();
        var collector = new InstanceCollector(c.Types, c.Layout);
        var set = collector.Run(program, 1);
        set.Mark([Item(program, set, "Fn top")]);
        Assert.Equal(["Fn later", "library Fn init in struct Duration"], Marked(false));
        set.Mark([Item(program, set, "Fn after")]);
        Assert.Equal(["Fn after"], Marked(true));
        set.Mark([]);
        Assert.Empty(Marked(true));
        set.Mark([0]);
        program.Version++;
        Assert.DoesNotContain(Enumerable.Range(0, collector.Run(program, 1).Items.Length), set.IsGenerated);

        // The labels of the items whose Generated mark is the given one.
        string[] Marked(bool generated)
            => [.. Enumerable.Range(0, set.Items.Length).Where(x => set.IsGenerated(x) == generated).Select(x => Label(program, set.Items[x]))
                .Order(StringComparer.Ordinal)];
    }

    // K 4.3: a repeated run of a program version keeps its set, and a program with validator findings has an empty one.
    [Fact]
    public void AVersionIsCollectedOnceAndFindingsEmptyTheSet()
    {
        var c = MirBuildTest.Compile(Source);
        var program = c.BuildMir();
        var collector = new InstanceCollector(c.Types, c.Layout);
        var set = collector.Run(program, 1);
        var items = set.Items.Length;
        Assert.Same(set, collector.Run(program, 1));
        Assert.Equal(items, set.Items.Length);
        program.Findings.Add(new(MirViolation.Callee, program.Bodies[0].Source));
        Assert.Equal(0, new InstanceCollector(c.Types, c.Layout).Run(program, 1).Items.Length);
    }

    // KD1, KD3: a generic callee is an instance keyed by its definition and erased substitution, represented by the substitution that first
    // admitted it, in the one view it selects; every item's key is erased exactly when its definition instantiates; Origin-only variants
    // are one item.
    [Fact]
    public void AGenericCalleeIsAnInstanceOfItsErasedSubstitution()
    {
        var c = MirBuildTest.Compile("func pick<T>(value: T) -> T => value@move\nfunc keep<T>(value: T) -> T\n    T is Copy\n    return value\n" +
            "func host(a: ref/i32, b: ref/i32) -> i32\n    _ = keep(a)\n    _ = keep(b)\n    return 0\nlet x = 1\nlet y = 2\n" +
            "require host(x@ref, y@ref) == 0 and pick(3) == 3 and pick(true) else => $abort(\"pick\")\n");
        var program = c.BuildMir();
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Empty(set.Records);
        Assert.All(set.Items.ToArray(), x => Assert.Equal(x.Key.Body.Def.Decl is { } decl && c.Types.Instantiates(decl), x.Key.Body.Erased is not null));
        Assert.All(set.Items.ToArray(), x => Assert.Same(x.Key.Body.Erased, x.Representative is null ? null : c.Types.Erase(x.Representative)));
        Assert.Equal(["Fn keep [ref/i32]", "Fn pick [bool]", "Fn pick [i32]"], set.Items[set.RootCount..].ToArray().Select(x => Label(program, x)).Order(StringComparer.Ordinal));
        var keep = Item(program, set, "Fn keep [ref/i32]");
        Assert.Equal([keep, keep], set.Targets(Item(program, set, "Fn host")).ToArray().Select(static x => x.Item).Where(x => x == keep));
    }

    // K 4.6: a recursion under one substitution reuses its item; one whose key grows stops when 16 items of its definition are on the chain,
    // with GenerationResourceLimit_Kd at the recursive call, and collection goes on.
    [Fact]
    public void RecursionReusesItsItemAndAGrowingKeyStopsAtItsCall()
    {
        var c = MirBuildTest.Compile(GenericStorageEmissionTest.Box + "func count<T>(n: i32, value: T) -> i32\n    T is Copy\n    if n == 0 => return 0\n" +
            "    return count(n - 1, value) + 1\nfunc grow<T>(value: T, n: i32) -> i32 => if n == 0 => 0 else => grow<Box<T>>(Box<T>.init(value@move), n - 1)\n" +
            "require count(2, 7) == 2 and grow<i32>(1, 3) == 0 else => $abort(\"recursion\")\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var program = c.BuildMir();
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        var count = Item(program, set, "Fn count [i32]");
        Assert.Contains(set.Targets(count).ToArray(), x => x.Item == count);
        Assert.Equal(16, set.Items.ToArray().Count(x => Label(program, x).StartsWith("Fn grow", StringComparison.Ordinal)));
        var record = Assert.Single(set.Records);
        Assert.Equal((DiagnosticCode.GenerationResourceLimit_Kd, "grow<Box<T>>(Box<T>.init(value@move), n - 1)"), (record.Code, c.SyntaxOf(record.Subject).ToString()));
    }

    // PLAN G128, PD30: an instance whose stored Types nest past Binding's bound is the capability table's storage-nesting gap, Unsupported_Kd
    // at the requesting call, while the twin one level below is an instance: Option^31<ref/i32> (storage depth 63) and Array^64<i32> pass,
    // Option^32<ref/i32> (65) and Array^65<i32> do not.
    [Theory]
    [InlineData("Option", 31, true)]
    [InlineData("Option", 32, false)]
    [InlineData("Array", 64, true)]
    [InlineData("Array", 65, false)]
    public void StorageNestingIsAGapAtTheRequestingCall(string wrapper, int depth, bool admitted)
    {
        var type = string.Concat(Enumerable.Repeat(wrapper + "<", depth)) + (wrapper == "Option" ? "ref/i32" : "i32") + new string('>', depth);
        var value = wrapper == "Option" ? ".None" : "[]";
        var c = MirBuildTest.Compile($"func pick<T>(value: T) -> T => value@move\npublic func main() -> ()\n    let v: {type} = {value}\n    let w = pick(v@move)\n    return\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var program = c.BuildMir();
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Equal(admitted, set.Items.ToArray().Any(x => Label(program, x).StartsWith("Fn pick", StringComparison.Ordinal)));
        Assert.Equal(admitted ? [] : [(DiagnosticCode.Unsupported_Kd, "pick(v@move)")], set.Records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString())));
    }

    // DECISIONS R5-M146 (X2, PD27; PLAN G157 (10)): a requirement whose witness has no MIR body, HeapBuffer's reserve through BufferWriter or
    // a #LibraryImport function, is the BodylessWitness gap, Unsupported_Kd at each site that requests its target (K 4.9, as OCC-X's): a
    // requirement call, and a requirement Item where it is formed and where it is called, whether or not the output calls the function
    // (code generation runs a witness only as a body or shim item).
    [Theory]
    [InlineData("    match w.reserve(4)\n", "w.reserve(4)")]
    [InlineData("    let reserve = W.reserve\n    match reserve(w, 4)\n", "W.reserve | reserve(w, 4)")]
    [InlineData(null, "C.now()")]
    public void ABodylessWitnessIsAGapAtTheCall(string? reserve, string sites)
    {
        var source = reserve is null
            ? "contract Clock\n    func now() -> u32\nstruct Win\n    Self is Clock\n    #LibraryImport(\"kernel32\", \"GetLastError\")\n" +
                "    public func now() -> u32\nfunc tick<C>() -> u32\n    C is Clock\n    return C.now()\nfunc use() -> u32 => tick<Win>()\n"
            : "func room<W>(w: uniq/W) -> i32\n    W is BufferWriter\n" + reserve + "        .Ok(_) => return 1\n        .Err(_) => return 0\n" +
                "func use(heap: uniq/Text.HeapBuffer) -> i32 => room(heap)\n";
        var c = MirBuildTest.Compile(source + "public func main() -> () => ()\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var set = new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1);
        Assert.All(set.Records, static x => Assert.Equal(DiagnosticCode.Unsupported_Kd, x.Code));
        Assert.Equal(sites, string.Join(" | ", set.Records.Select(x => c.SyntaxOf(x.Subject).ToString())));
    }

    // SPEC 23.1.2 (DECISIONS R5-M146, X2 review B1): the gap is the witness's own (TyCtxt.Bodyless: a compiler or foreign function), so an
    // independent Binding error elsewhere leaves an import witness's record beside it.
    [Fact]
    public void ABodylessWitnessIsAGapBesideAnUnrelatedError()
    {
        var c = MirBuildTest.Compile(
            "contract Clock\n    func now() -> u32\nstruct Win\n    Self is Clock\n    #LibraryImport(\"kernel32\", \"GetLastError\")\n" +
            "    public func now() -> u32\nfunc tick<C>() -> u32\n    C is Clock\n    return C.now()\nfunc use() -> u32 => tick<Win>()\n" +
            "func broken() -> i32\n    let text: string = 1\n    return 0\npublic func main() -> () => ()\n");
        Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.TypeMismatch_Kd);
        var set = new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1);
        Assert.Equal([(DiagnosticCode.Unsupported_Kd, "C.now()")], set.Records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString())));
    }

    // DECISIONS BU-11 (PLAN G153 (2)): the storage bound is a property of each instance, whatever was prepared before it and in whatever
    // order its parts are walked. `h` places its Type argument 3 levels down, so Box^61 (i32 at level 64) is an instance and Box^62 is the
    // gap, alone and before or after the other; AB and BA store Box^57 in one Field and 4 levels deeper in the other, in either Field
    // order (AB^56 is the twin within the bound); Tag stores no Field, so its Type argument alone refuses it, at each requesting call.
    [Theory]
    [InlineData("Box^61", "")]
    [InlineData("Box^62", "Box^62")]
    [InlineData("Box^61 Box^62", "Box^62")]
    [InlineData("Box^62 Box^61", "Box^62")]
    [InlineData("AB^56", "")]
    [InlineData("AB^57", "AB^57")]
    [InlineData("BA^57", "BA^57")]
    [InlineData("Tag^61 Tag^61", "Tag^61 Tag^61")]
    public void StorageNestingIsAPropertyOfTheInstance(string arguments, string refused)
    {
        var c = MirBuildTest.Compile(
            "struct Box<T>\n    public let value: T\nstruct AB<A, B>\n    public let a: A\n    public let b: B\nstruct BA<A, B>\n    public let b: B\n" +
            "    public let a: A\nstruct Tag<T>\nfunc h<T>(x: Option<raw/T>) => ()\npublic func main() -> ()\n" +
            string.Concat(arguments.Split(' ').Select(x => $"    h<{Text(x)}>(.None)\n")) + "    return\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var program = c.BuildMir();
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Equal(
            refused.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => (DiagnosticCode.Unsupported_Kd, $"h<{Text(x)}>(.None)")),
            set.Records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString())));

        static string Text(string argument)
        {
            var depth = int.Parse(argument[(argument.IndexOf('^') + 1)..], CultureInfo.InvariantCulture);
            var box = Nest("Box", depth, "i32");
            return argument[..argument.IndexOf('^')] switch
            {
                "Box" => box,
                "Tag" => $"Tag<{Nest("Array", depth, "i32")}>",
                var holder => $"{holder}<{box}, {Nest("Box", 4, box)}>",
            };
        }
    }

    // DECISIONS BU-12 (PLAN G153 (2)): the Types of one strongly connected component of storage share its height, the largest exit, so a
    // cycle is decided by its exits in every order. R and X (X stores R through a handle, R stores Box^200) are refused at every call;
    // `L`, which stores itself through Option<rc/L<T>>, is an instance with Box^59 (h's 3 levels + 60) and refused with Box^61, either
    // way round; a ring of 16 or 17 structs is an instance (height 1), a ring of 63 too, while a ring of 64 passes the 256 Types the walk's
    // path may hold. A growing G reaches that cap in every order, while G2, which repeats, is an instance. N stores its T, a Tuple tree as
    // deep as the growth, before its growth Field, so it reaches the cap only because the walk enters each Type once (structural Types
    // too): walked as trees, its Tuples would not end within the test run (RetentionTest.GrowingRecursiveContractsAreTheLimit guards the
    // same through A5's retention). Over the cap the verdict may depend on the other Types the program prepares: a 63-ring behind Box^4
    // is refused alone and admitted once S1 is prepared anywhere (the owner's accepted residual).
    [Theory]
    [InlineData(0, "R X", "R X")]
    [InlineData(0, "X R", "X R")]
    [InlineData(0, "X", "X")]
    [InlineData(0, "OL59", "")]
    [InlineData(0, "L59 OL59", "")]
    [InlineData(0, "OL59 L59", "")]
    [InlineData(0, "OL61 L61", "OL61 L61")]
    [InlineData(16, "S1", "")]
    [InlineData(17, "S1", "")]
    [InlineData(17, "S9 OS1", "")]
    [InlineData(63, "S1", "")]
    [InlineData(64, "S1", "S1")]
    [InlineData(63, "W4", "W4")]
    [InlineData(63, "W4 W4", "W4 W4")]
    [InlineData(63, "W4 S1 W4", "")]
    [InlineData(0, "G", "G")]
    [InlineData(0, "G2 G", "G")]
    [InlineData(0, "G G2", "G")]
    [InlineData(0, "N", "N")]
    public void StorageNestingIsAPropertyOfTheComponent(int ring, string arguments, string refused)
    {
        var c = MirBuildTest.Compile(
            $"struct Box<T>\n    public let value: T\nstruct R<T>\n    public let x: X<T>\n    public let y: {Nest("Box", 200, "T")}\nstruct X<T>\n" +
            "    public let r: Option<rc/R<T>>\nstruct L<T>\n    public let next: Option<rc/L<T>>\n    public let v: T\nstruct G<T>\n" +
            "    public let next: Option<rc/G<(T, T)>>\nstruct G2<T>\n    public let next: Option<rc/G2<T>>\nstruct N<T>\n    public let v: T\n" +
            "    public let next: Option<rc/N<(T, T)>>\n" + Ring(ring) + "func h<T>(x: Option<raw/T>) => ()\npublic func main() -> ()\n" +
            string.Concat(arguments.Split(' ').Select(x => $"    h<{Text(x)}>(.None)\n")) + "    return\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var program = c.BuildMir();
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Equal(
            refused.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => (DiagnosticCode.Unsupported_Kd, $"h<{Text(x)}>(.None)")),
            set.Records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString())));

        static string Text(string argument) => argument switch
        {
            "R" or "X" or "G" or "G2" or "N" => argument + "<i32>",
            "OS1" => "Option<rc/S1>",
            "W4" => Nest("Box", 4, "S1"),
            ['O', 'L', ..] => $"Option<rc/{Text(argument[1..])}>",
            ['L', ..] => $"L<{Nest("Box", int.Parse(argument[1..], CultureInfo.InvariantCulture), "i32")}>",
            _ => argument,
        };
    }

    // DECISIONS BU-12: a ring of structs linked by Option<rc/…> is an instance up to the storage walk's cap, so a value of it reaches the
    // collector's drop glue, whose depth limit it meets past 42 structs: GenerationResourceLimit_Kd at the destroyed value, and no
    // storage-nesting gap at the call.
    [Theory]
    [InlineData(42, "")]
    [InlineData(50, "GenerationResourceLimit_Kd v@move")]
    public void StorageRingsMeetTheGlueDepth(int ring, string records)
    {
        var c = MirBuildTest.Compile(
            Ring(ring) + "func pick<T>(value: T) -> T => value@move\npublic func main() -> ()\n    let v: Option<rc/S1> = .None\n    let w = pick(v@move)\n" +
            "    return\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var program = c.BuildMir();
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Equal(records, string.Join(" | ", set.Records.Select(x => $"{x.Code} {c.SyntaxOf(x.Subject)}")));
    }

    // DECISIONS BU-38 (R5's guard), R5-M151: the collector's one reading of a storage verdict at a site (Made) and Layout's Rejected row map
    // Rejected to a broken contract, which the collector's one record path keeps only where TyCtxt.BindingComplete holds. A part Binding
    // rejected exists only in an incomplete compilation, where Rejected is a dependent without a record; in a complete one, which no source
    // reaches, it is InternalInvariant_Kd at the site. Prepared makes the instance and Limit is the storage-nesting gap in both.
    [Theory]
    [InlineData(false, "Unsupported_Kd")]
    [InlineData(true, "InternalInvariant_Kd Unsupported_Kd")]
    public void ARejectedVerdictIsABrokenContractInACompleteCompilation(bool complete, string records)
    {
        var c = MirBuildTest.Compile((complete ? string.Empty : "struct Bad\n    public var m: Missing\n") + "public func main() -> () => ()\n");
        Assert.Equal(complete, c.Binding.Result.IsComplete);
        var collector = new InstanceCollector(c.Types, c.Layout);
        var site = SourceRef.Of(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "main"));
        var verdicts = new[] { Binding.StorageOutcome.Prepared, Binding.StorageOutcome.Rejected, Binding.StorageOutcome.Limit };
        Assert.Equal([true, false, false], verdicts.Select(x => collector.Made(x, site)));
        Assert.Equal(records, string.Join(' ', collector.Set.Records.Select(static x => x.Code)));
        Assert.Equal(DiagnosticCode.InternalInvariant_Kd, collector.RecordOf(Layout.Outcome.Rejected));
    }

    // SPEC 8.6.1, 8.2.3, IMPL 21.1 (DECISIONS BU-50, PLAN G271): an instance whose stored part is an associated projection is collected and
    // laid out with the Type its subject's conformance binds (W<Src>'s `e: S.Element` as i32), and a body reads the part as that Type, the
    // one producer's: a Field read (`w.e`) and a Case payload read (`.A(let n)`) give an i32 local, and so does a subject inheriting its
    // base's binding (`DerI : BaseI`, the valid twin of the inherited rejection). Before, the uncompleted projection
    // reached Layout through AdtDef's view or a read's local (the collection threw "Layout has no row for the Type Element", sel's
    // `check` CheckFaulted_Kd), and a payload read's `n + 1` was TypeMismatch_Kd.
    [Theory]
    [InlineData("    let w = W<Src>.init(e: 1)\n    let p = (w@move, 2)\n")]
    [InlineData("    let w = W<Src>.init(e: 1)\n    let v = w.e\n")]
    [InlineData("    let e: EM<Src> = .A(1)\n    let y = match e@move\n        .A(let n) => n + 1\n        .B => 0\n")]
    [InlineData("    let w = W<DerI>.init(e: 1)\n    let v = w.e\n")]
    public void AProjectionPartIsLaidOutAndReadCompleted(string body)
    {
        var (_, set) = Collect("contract HasElem\n    associate Element\nstruct Src\n    Self is HasElem\n    associate Element is i32\nstruct W<S>\n" +
            "    S is HasElem\n    public var e: S.Element\n    public init(e: S.Element) => self.e = e@move\nenum EM<S>\n    S is HasElem\n" +
            "    A(S.Element)\n    B\nopen struct BaseI\n    Self is HasElem\n    associate Element is i32\nstruct DerI : BaseI\n    var m: i32\n" +
            "public func main() -> ()\n" + body + "    return\n");
        Assert.Empty(set.Records);
        Assert.NotEqual(0, set.Items.Length);
    }

    // SPEC 23.1.2, 23.1.1 (DECISIONS BU-38): a part Binding rejected is a dependent, never a limit. A program whose instance, composed
    // substitution or bodyless callee's parameter rests on storage Binding rejected keeps exactly its Binding record and gains no generation
    // record (before: the storage-nesting gap at the requesting call, C8d's LegacyRow item InternalInvariant_Kd), while a Type nested past the
    // bound keeps the gap at its site, beside a failed part in either order too: the limit wins. A Field whose projection finally names no
    // Type is such a part, whatever its subject and reason (DECISIONS BU-50): its specification rejected by SPEC 8.3.3 (noType), also
    // through an inherited conformance one or two levels down or under a generic base (inherited*), two inherited sources or two
    // conformance paths disagreeing (sourcesDisagree, pathsDisagree; refinedDisagree, a direct and a refinement path binding u8 and i32
    // for the receiver, unlike TyCtxtTest.ProvenPathsBindTheirCommonType's), a subject whose base clause gives it no conformance
    // (noConformance*), a closed subject that is no struct or enum (closed*: a primitive, a Tuple, an array, a completed `Src.Item`,
    // `bool` without Addable, a Case payload) or whose conformance's condition is refuted (closedRefuted); before, Layout's "no row for the
    // Type Item" ("Output"). A builtin output names its Type (closedOutputValid). The valid twins whose projection stays pending in a
    // generic declaration's storage are MirBorrowsTest.APendingProjectionIsCompletedByTheInstanceAUseForms, at the MIR problem layer that
    // observed them.
    [Theory]
    [MemberData(nameof(RejectedCases))]
    public void APartBindingRejectedIsNoGenerationRecord(string name, string source, string issues, string records)
    {
        var c = MirBuildTest.Compile(source + "public func main() -> () => ()\n");
        Assert.Equal(issues, string.Join(' ', c.Binding.Issues.Select(static x => x.Code)));
        var program = c.BuildMir();
        Assert.True(program.Findings.Count == 0, name); // A program with findings has an empty checked set.
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Equal(records, string.Join(" | ", set.Records.Select(x => $"{x.Code} {c.SyntaxOf(x.Subject)}")));
    }

    // SPEC 6.1.3, 8.13.3, DECISIONS R5-M40 (the F3 reproducers, X40): an instance of a nested function holds its enclosing instance. A call
    // one level into a generic function carries it, so each instance of the enclosing function has its own item; a call two levels in,
    // from a block, in a generic struct's method or accessor (SPEC 11.3: an accessor's body is its executing function's; before PLAN
    // G157 (49), InternalInvariant_Kd at the getter's and the setter's call) or in a specialization inside a generic struct does not, so the
    // edge is the EnclosingInstance gap at the call until a representation of the enclosing chain carries it (DECISIONS R5-M43; then two
    // items each). Every admitted representative closes its body; a closed enclosing function or accessor has no instance to carry.
    [Theory]
    [MemberData(nameof(NestedCases))]
    public void NestedFunctionsNeedTheirEnclosingInstance(string declarations, string nested, int items, string? refused)
    {
        var c = MirBuildTest.Compile(declarations + "require outer(3) == outer(3) and outer(true) == outer(true) else => $abort(\"nested\")\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var program = c.BuildMir();
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Equal(items, set.Items.ToArray().Count(x => Label(program, x).StartsWith("Fn " + nested + " [", StringComparison.Ordinal)));
        Assert.Equal(refused is null ? [] : [(DiagnosticCode.Unsupported_Kd, refused)], set.Records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString())));
        Assert.All(set.Items.ToArray(), x => Assert.True(Closed(c, program, x), Label(program, x)));
    }

    // K 4.5 rows 5, 11 and 12, DECISIONS R5-M43: a Closure of an instance is an instance of its body under the instance's substitution,
    // where its environment is made (also when it is never called) and where it is called, one per enclosing instance and a Closure in a
    // Closure among them; a function item constant and an Item called through a Callable parameter are their function's instances.
    [Fact]
    public void ClosuresAndItemsOfInstancesAreTheirBodies()
    {
        var c = MirBuildTest.Compile(
            ItemSource + "require apply(3) == 3 and apply(true) and make(1) == 1 and p(4) == 4 and run(one<bool>) == 1 else => $abort(\"k5\")\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var program = c.BuildMir();
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Empty(set.Records);
        Assert.Equal(
            [
                "Closure apply [bool]", "Closure apply [bool]", "Closure apply [i32]", "Closure apply [i32]", "Closure make [i32]", "Fn apply [bool]",
                "Fn apply [i32]", "Fn make [i32]", "Fn one [bool]", "Fn pick [i32]", "Fn run [function item one<bool>]",
            ],
            set.Items[set.RootCount..].ToArray().Select(x => Label(program, x)).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["Fn pick [i32]", "Fn apply [i32]", "Fn apply [bool]", "Fn make [i32]", "Fn pick [i32]", "Fn one [bool]", "Fn run [function item one<bool>]"],
            Targets(program, set, "Fn top"));
        Assert.Equal(["Fn one [bool]"], Targets(program, set, "Fn run [function item one<bool>]"));
        Assert.All(set.Items.ToArray(), x => Assert.True(Closed(c, program, x), Label(program, x)));
    }

    // SPEC 11.3, 6.1.3 (PLAN G157 (49)): a Closure in a generic struct's accessor is an instance of its body under the accessor's instance,
    // as one in a function is: one per instance of the getter and of the setter, with no record (before: InternalInvariant_Kd where it is
    // made and where it is called, since no context carried the accessor's instance).
    [Fact]
    public void ClosuresOfAccessorInstancesAreTheirBodies()
    {
        var c = MirBuildTest.Compile(AccessorClosures + "require outer(3) == 3 and outer(true) else => $abort(\"accessor\")\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var program = c.BuildMir();
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Empty(set.Records);
        var closures = set.Items.ToArray().Select(x => Label(program, x)).Where(static x => x.StartsWith("Closure ", StringComparison.Ordinal));
        Assert.Equal(
            ["Closure got.Get [Box<bool>]", "Closure got.Get [Box<i32>]", "Closure got.Set [Box<bool>]", "Closure got.Set [Box<i32>]"],
            closures.Order(StringComparer.Ordinal));
        Assert.All(set.Items.ToArray(), x => Assert.True(Closed(c, program, x), Label(program, x)));
    }

    // K 4.5 row 13: an erasure of a Closure or function item to its Function Type is its ClosureErasure or ItemAdapter item, keyed by the
    // erased Type and the erased Function Type (DECISIONS R5-M108) and represented by the first Type that requests it, whose target is the
    // body the Closure or Item runs; a call of the Function Type value has no target.
    [Fact]
    public void AnErasureIsItsAdapterItem()
    {
        var c = MirBuildTest.Compile("func adder<T>(x: T) -> T\n    T is Copy\n    let f = func [] (y: T) -> T => y\n    let e: (T) -> T = f\n" +
            "    return e(x)\n" +
            "func pick<T>(value: T) -> T => value@move\nlet h: (i32) -> i32 = pick<i32>\nrequire adder(1) == 1 and h(2) == 2 else => $abort(\"erasure\")\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var program = c.BuildMir();
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Empty(set.Records);
        var adapters = Enumerable.Range(0, set.Items.Length).Where(x => set.Items[x].Key.Kind is MonoKind.ClosureErasure or MonoKind.ItemAdapter).ToArray();
        Assert.Equal([MonoKind.ClosureErasure, MonoKind.ItemAdapter], adapters.Select(x => set.Items[x].Key.Kind).Order());
        Assert.All(adapters, x => Assert.Same(c.Types.Erase(set.Items[x].RepresentativeType!), set.Items[x].Key.Type));
        Assert.All(adapters, x => Assert.Equal("(i32) -> i32", TyCtxt.Name(set.Items[x].Key.Function!)));
        Assert.Equal(
            ["Closure adder [i32]", "Fn pick [i32]"],
            adapters.SelectMany(x => set.Targets(x).ToArray()).Select(x => Label(program, set.Items[x.Item])).Order(StringComparer.Ordinal));
        var adder = set.Targets(Item(program, set, "Fn adder [i32]")).ToArray();
        Assert.DoesNotContain(adder, x => Label(program, set.Items[x.Item]).StartsWith("Fn pick", StringComparison.Ordinal));
    }

    // SPEC 10.7 (DECISIONS R5-M108): a Never-result source fits Function Types of other results, so it has one adapter item per erased
    // target Function Type, each running the source's body: two items for three erasures into two Types.
    [Fact]
    public void ANeverSourceHasAnAdapterPerTargetFunctionType()
    {
        var (program, set) = Collect("func fail() -> Never => $abort(\"x\")\nfunc i() -> () -> i32 => fail\nfunc s() -> () -> string => fail\n" +
            "func j() -> () -> i32 => fail\nlet x = 1\n");
        Assert.Empty(set.Records);
        var adapters = set.Items.ToArray().Where(static x => x.Key.Kind == MonoKind.ItemAdapter).ToArray();
        Assert.Equal(["() -> i32", "() -> string"], adapters.Select(static x => TyCtxt.Name(x.Key.Function!)).Order(StringComparer.Ordinal));
        Assert.Single(adapters.Select(static x => x.Key.Type).Distinct());
        Assert.All(adapters, x => Assert.Equal(["Fn fail"], set.Targets(Array.IndexOf(set.Items.ToArray(), x)).ToArray()
            .Select(y => Label(program, set.Items[y.Item]))));
    }

    // K 4.5 rows 4 and 12 (X5): the Item of a virtual slot, whose calls dispatch, is no target where it is made: its receiver's VTable holds
    // the entries, and a program that makes no object has neither.
    [Fact]
    public void AVirtualSlotsItemHasNoTarget()
    {
        var (program, set) = Collect("open struct Base\n    public virtual func value(self: objref/Self) -> i32 => 1\nlet v = Base.value\n");
        Assert.Empty(set.Records);
        Assert.Empty(Targets(program, set, "Fn top"));
        Assert.DoesNotContain(set.Items.ToArray(), static x => x.Key.Kind is MonoKind.ObjectDescriptor or MonoKind.VTable);
    }

    // K 4.5 row 8, K 4.3, DECISIONS R5-M62, R5-M74 (VirtualGenericDispatchTest's closed tables): a creation is its payload's descriptor, one
    // per erased payload whatever the handle, whose targets are the payload's glue and its VTable; the VTable's are each slot's
    // implementation in the first layer that owns it, in slot order, and a Virtual call has no target; the bodies a table reaches call
    // their bases directly.
    [Fact]
    public void ACreationIsItsPayloadsDescriptorAndItsTableTheSlotsBodies()
    {
        var (program, set) = Collect(VirtualGenericDispatchTest.Hierarchy + "let a = Leaf<i32>.init()@obj\nlet b = Leaf<string>.init()@rc\n" +
            "let c = Leaf<i32>.init()@arc\nrequire a.read() == 31 and b.nested() == 132 and a.direct() == 11 else => $abort(\"dispatch\")\n");
        Assert.Empty(set.Records);
        Assert.Equal(1, set.Items.ToArray().Count(x => Label(program, x) == "ObjectDescriptor Leaf<i32>"));
        Assert.Equal(2, Targets(program, set, "Fn top").Count(static x => x == "ObjectDescriptor Leaf<i32>"));
        Assert.Equal(["VTable Leaf<i32>"], Targets(program, set, "ObjectDescriptor Leaf<i32>"));
        Assert.Equal(["Fn read [Leaf<i32>]", "Fn nested [Base<i32>]"], Targets(program, set, "VTable Leaf<i32>"));
        Assert.Equal(["Fn read [Leaf<string>]", "Fn nested [Base<string>]"], Targets(program, set, "VTable Leaf<string>"));
        Assert.Equal(["Fn read [Middle<i32>]"], Calls(program, set, "Fn read [Leaf<i32>]"));
        Assert.Equal(["Fn read [Base<i32>]"], Calls(program, set, "Fn read [Middle<i32>]"));
        Assert.Empty(Calls(program, set, "Fn nested [Base<i32>]"));
    }

    // K 4.5 rows 5, 8, 12 and 13 (VirtualGenericDispatchTest's factory Item): an Item of a handle operation's function runs what its call
    // does, so in an instance it is the descriptor of its payload where it is made, called and erased, and that VTable holds the bodies
    // the instance's object dispatches to.
    [Fact]
    public void AFactoryItemIsItsPayloadsDescriptorInTheInstance()
    {
        var (program, set) = Collect(VirtualGenericDispatchTest.Hierarchy + "func build<T>() -> obj/Leaf<T>\n    T is Owned\n" +
            "    let factory = Kimi.Intrinsics.makeObj<Leaf<T>>\n    return factory(Leaf<T>.init())\nlet a = build<i32>()\n" +
            "let erased: (Leaf<string>) -> obj/Leaf<string> = Kimi.Intrinsics.makeObj<Leaf<string>>\nlet b = erased(Leaf<string>.init())\n" +
            "require a.nested() == 132 and b.nested() == 132 else => $abort(\"factory item\")\n");
        Assert.Empty(set.Records);
        Assert.Equal(
            ["ObjectDescriptor Leaf<i32>", "ObjectDescriptor Leaf<i32>"],
            Calls(program, set, "Fn build [i32]").Where(static x => x.StartsWith("Object", StringComparison.Ordinal)));
        Assert.Equal(["ObjectDescriptor Leaf<string>"], Targets(program, set, "ItemAdapter function item Kimi.Intrinsics.makeObj<Leaf<string>>"));
        Assert.Equal(["Fn read [Leaf<i32>]", "Fn nested [Base<i32>]"], Targets(program, set, "VTable Leaf<i32>"));
        Assert.Equal(["Fn read [Leaf<string>]", "Fn nested [Base<string>]"], Targets(program, set, "VTable Leaf<string>"));
    }

    // K 4.5 row 8, SPEC 13.6.2, 8.14 (DECISIONS R5-M111): a case adaptation whose instance's case creates an object is a creation, its
    // payload's descriptor, one for the obj and rc instances; its owner instance, an identity, has none.
    [Fact]
    public void ACaseAdaptationThatCreatesIsItsPayloadsDescriptor()
    {
        var (program, set) = Collect("func box<s/T>(value: T) -> s/T\n    s is owner or object\n    T is ObjectPayload\n    return value@move@s\n" +
            "let a = box<i32>(6)\nlet b = box<obj/i32>(7)\nlet c = box<rc/i32>(8)\n");
        Assert.Empty(set.Records);
        var boxes = Enumerable.Range(0, set.Items.Length).Select(x => Label(program, set.Items[x])).Where(static x => x.StartsWith("Fn box", StringComparison.Ordinal));
        Assert.Equal([0, 1, 1], boxes.Select(x => Calls(program, set, x).Count(static y => y == "ObjectDescriptor i32")));
        Assert.Single(set.Items.ToArray(), x => Label(program, x) == "ObjectDescriptor i32");
    }

    // SPEC 15.7.1, 4.9.6 (DECISIONS R5-M116): replace destroys its target's old value in place, Binding's published row, so its call
    // targets the glue of that Type when it may need a drop, as its Item's Value call and its adapter do; an i32 needs none, and exchange
    // and swap destroy nothing.
    [Fact]
    public void AReplaceTargetsTheGlueOfTheValueItDestroysInPlace()
    {
        var (program, set) = Collect("struct Res\n    public let id: i32\n    public init(id: i32) => self.id = id\n    drop => ()\n" +
            "func r(t: uniq/Res, v: Res) => Kimi.Intrinsics.replace(t, with: v@move)\nfunc n(t: uniq/i32) => Kimi.Intrinsics.replace(t, with: 2)\n" +
            "func e(t: uniq/Res, v: Res) -> Res => Kimi.Intrinsics.exchange(t, with: v@move)\n" +
            "func s(a: uniq/Res, b: uniq/Res) => Kimi.Intrinsics.swap(a, b)\n" +
            "func v(t: uniq/Res, w: Res)\n    let f = Kimi.Intrinsics.replace<Res>\n    f(t, w@move)\n" +
            "func a() -> (uniq/Res, Res) -> () => Kimi.Intrinsics.replace\n");
        Assert.Empty(set.Records);
        Assert.Equal(["DropGlue Res"], AtCalls(program, set, "Fn r"));
        Assert.Equal([string.Empty], AtCalls(program, set, "Fn n"));
        Assert.Equal([string.Empty], AtCalls(program, set, "Fn e"));
        Assert.Equal([string.Empty], AtCalls(program, set, "Fn s"));
        Assert.Equal(["DropGlue Res"], AtCalls(program, set, "Fn v"));
        var adapter = Assert.Single(Enumerable.Range(0, set.Items.Length), x => set.Items[x].Key.Kind == MonoKind.ItemAdapter);
        Assert.Equal(["DropGlue Res"], set.Targets(adapter).ToArray().Select(x => Label(program, set.Items[x.Item])));
    }

    // K 4.3, 4.5 row 2, X4 (DECISIONS R5-M121): a call of an Array operation targets the LegacyRow item of its function's instance, keyed by
    // the function and the erased substitution and represented by the collection, as its Item's Value call and its adapter do; its targets
    // are its row's declared needs: clear's the glue of an element that may need a drop, none over i32; an Array<isize> is another
    // instance; setArrayLength, written in place, has none.
    [Fact]
    public void AnArrayOperationIsItsFunctionInstancesLegacyRow()
    {
        var (program, set) = Collect("struct Res\n    public let id: i32\n    public init(id: i32) => self.id = id\n    drop => ()\n" +
            "func p(values: uniq/Array<i32>) => values.append(1)\nfunc w(values: uniq/Array<isize>) => values.append(1)\n" +
            "func c(values: uniq/Array<Res>) => values.clear()\nfunc n(values: uniq/Array<i32>) => values.clear()\n" +
            "func v(values: uniq/Array<i32>)\n    let f = Array<i32>.reserve\n    f(values, 2)\n" +
            "func a() -> (uniq/Array<Res>) -> () => Array<Res>.clear\n");
        Assert.Empty(set.Records);
        Assert.Equal(["LegacyRow Array<i32> ArrayAppend"], AtCalls(program, set, "Fn p"));
        Assert.Equal(["LegacyRow Array<isize> ArrayAppend"], AtCalls(program, set, "Fn w"));
        Assert.Equal(["LegacyRow Array<Res> ArrayClear"], AtCalls(program, set, "Fn c"));
        Assert.All(RowTargets(program, set, "LegacyRow Array<Res> ArrayClear"), static x => Assert.Equal(["DropGlue Res"], x));
        Assert.Empty(Targets(program, set, "LegacyRow Array<i32> ArrayClear"));
        Assert.Equal(["LegacyRow Array<i32> ArrayReserve"], AtCalls(program, set, "Fn v").Where(static x => x.Length != 0));
        Assert.Empty(Targets(program, set, "LegacyRow Array<i32> ArrayReserve"));
        var adapter = Assert.Single(Enumerable.Range(0, set.Items.Length), x => set.Items[x].Key.Kind == MonoKind.ItemAdapter);
        Assert.Equal(["LegacyRow Array<Res> ArrayClear"], set.Targets(adapter).ToArray().Select(x => Label(program, set.Items[x.Item])));
        Assert.All(set.Items.ToArray().Where(static x => x.Key.Kind == MonoKind.LegacyRow), static x => Assert.Equal(-1, x.BodyOrdinal));
    }

    // G157 (6), X4 (DECISIONS R5-M95, R5-M121): the Array rows that declare ElementGlue, clear's item and an Array's glue (its Drop row), target
    // exactly the glue of their collection's Element parts that may need a drop, as DropGlue.Parts enumerates them.
    [Theory]
    [InlineData("Res", new[] { "DropGlue Res" })]
    [InlineData("string", new[] { "DropGlue string" })]
    [InlineData("i32", new string[0])]
    public void TheRowsDeclaringElementGlueTargetTheirElementPartsGlue(string element, string[] glue)
    {
        var (program, set) = Collect("struct Res\n    public let id: i32\n    public init(id: i32) => self.id = id\n    drop => ()\n" +
            $"func c(values: uniq/Array<{element}>) => values.clear()\nfunc d(values: Array<{element}>) => ()\n");
        Assert.Empty(set.Records);
        Assert.Equal(glue, Assert.Single(RowTargets(program, set, $"LegacyRow Array<{element}> ArrayClear")));
        Assert.Equal(glue, Targets(program, set, $"DropGlue Array<{element}>"));
        Assert.True((LegacyRuntimeHelpers.ArrayRow(ArrayHelperKind.Clear).Needs & HelperNeeds.ElementGlue) != 0);
        Assert.True((LegacyRuntimeHelpers.ArrayRow(ArrayHelperKind.Drop).Needs & HelperNeeds.ElementGlue) != 0);
    }

    // K 4.8, X34 (DECISIONS R5-M121): a LegacyRow item lays out its collection's element, as the Array's glue does: an element whose layout is
    // oversized (OversizedLayout_Kd), rejected (InvalidCLayout_Kd) or over a limit (the erased Type's one GenerationResourceLimit_Kd) is the
    // record at the call and no item; the row itself admits (the counterpart, AnArrayOperationIsItsFunctionInstancesLegacyRow).
    [Theory]
    [InlineData(Huge, "OversizedLayout_Kd values.clear()")]
    [InlineData("P<()>", "InvalidCLayout_Kd values.clear()")]
    [InlineData("[2147483648 of u8]", "GenerationResourceLimit_Kd values.clear()")]
    public void ALegacyRowLaysOutItsElement(string element, string records)
    {
        var c = MirBuildTest.Compile(CLayoutP + $"func c(values: uniq/Array<{element}>) => values.clear()\n");
        var set = new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1);
        Assert.Equal(records, string.Join("|", set.Records.Select(x => $"{x.Code} {c.SyntaxOf(x.Subject).ToString().Split('\n')[0]}")));
        Assert.DoesNotContain(set.Items.ToArray(), static x => x.Key.Kind == MonoKind.LegacyRow);
    }

    // K 4.8, X34 (DECISIONS R5-M124): placeEntry's LegacyRow item lays out each component of the Dictionary its handle designates, the value
    // too: a literal's oversized or rejected value refuses the item at the literal and at its entry's key, where the entry's insertion is
    // (SPEC 22.5.1; DECISIONS R5-M144) (the records of its other uses are at the same sites); an i64 value admits it (the counterpart).
    [Theory]
    [InlineData(Huge, "OversizedLayout_Kd")]
    [InlineData("P<()>", "InvalidCLayout_Kd")]
    [InlineData("i64", null)]
    public void ALegacyRowLaysOutADictionarysValue(string value, string? code)
    {
        var c = MirBuildTest.Compile(CLayoutP + DictionaryKey + $"func put(v: {value})\n    let d: Dictionary<Key, {value}> = [Key.init(1): v@move]\n");
        var set = new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1);
        var records = code is null ? Array.Empty<string>()
            : [$"{code} {value}", $"{code} v@move", $"{code} Key.init(1)", $"{code} [Key.init(1): v@move]",
                $"{code} let d: Dictionary<Key, {value}> = [Key.init(1): v@move]"];
        Assert.Equal(records, set.Records.Select(x => $"{x.Code} {c.SyntaxOf(x.Subject).ToString().Split('\n')[0]}"));
        Assert.Equal(code is null ? 1 : 0, set.Items.ToArray().Count(static x => x.Key.Kind == MonoKind.LegacyRow));
    }

    // SPEC 13.6.4, 8.15.3 (DECISIONS R5-M74): a runtime Type test and a clone acquire nothing and need no layout, so neither has a target:
    // a test of a struct no creation makes, oversized or with a table, collects nothing of it, and a clone of a handle to an oversized
    // payload records nothing; only creations are descriptors.
    [Fact]
    public void ATypeTestAndACloneHaveNoTarget()
    {
        const string Huge = "struct Huge\n    public var v: (" + Big + ", u8)\n";
        var (program, set) = Collect(Huge + VirtualGenericDispatchTest.Hierarchy + "open struct S\n    public init() => ()\n" +
            "let s = S.init()@obj\nlet t = s is Huge\nlet a = Middle<i32>.init()@obj\nlet u = a is Leaf<i32>\n");
        Assert.Empty(set.Records);
        Assert.Equal(
            ["ObjectDescriptor S", "ObjectDescriptor Middle<i32>"],
            Targets(program, set, "Fn top").Where(static x => x.StartsWith("Object", StringComparison.Ordinal)));
        Assert.DoesNotContain(set.Items.ToArray(), x => Label(program, x) is var label && (label.Contains("Huge", StringComparison.Ordinal) ||
            label.Contains("Leaf<i32>", StringComparison.Ordinal)));
        (program, set) = Collect(Huge + "func f(x: ref/(rc/Huge)) -> rc/Huge => Kimi.Intrinsics.clone(x)\n");
        Assert.Empty(set.Records);
        Assert.Empty(Targets(program, set, "Fn f"));
    }

    // K 4.5 rows 2, 6, 12 (DECISIONS R5-M74): an Item of a function without a MIR body runs what its call does, the requirement its
    // published row runs (Text.toString's format witness) or what its intrinsic's row names (Text.writer's destination's reserve witness).
    [Fact]
    public void ABodylessFunctionsItemIsWhatItsCallRuns()
    {
        var source = FormatSource
            .Replace("let one = show(Box.init(1))", "let print = Text.toString<Box>\nlet box = Box.init(1)\nlet one = print(box@ref)", StringComparison.Ordinal)
            .Replace("var writer = Text.writer(sink@uniq)", "let make = Text.writer<Sink>\n    var writer = make(sink@uniq)", StringComparison.Ordinal);
        var (program, set) = Collect(source);
        Assert.Empty(set.Records);
        Assert.Equal(["Fn format", "Fn format"], Calls(program, set, "Fn top").Where(static x => x == "Fn format"));
        Assert.Equal(["Fn reserve", "Fn reserve"], Calls(program, set, "Fn toSink").Where(static x => x == "Fn reserve"));
    }

    // K 4.5 rows 2, 12 and 13 (DECISIONS R5-M136): an intrinsic's Type argument is read only where its row reads one
    // (IntrinsicRow.ReadsTypeArgument). Test.tempDirectory's function has no Type parameter and its row neither lays out nor targets one, so its
    // Item's Value call and its adapter (an erasure to `() -> string`) have no target and no record (fail-before: an index outside the Item's
    // substitution, a fault that ended the check).
    [Fact]
    public void AnIntrinsicsItemReadsATypeArgumentOnlyWhereItsRowDoes()
    {
        var c = MirBuildTest.Compile(
            "#Test\nfunc t()\n    let make = Test.tempDirectory\n    Console.writeLine(make())\n    Console.writeLine(run(Test.tempDirectory))\n" +
            "func run(make: () -> string) -> string => make()\n",
            test: true);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Empty(set.Records);
        var adapter = Assert.Single(Enumerable.Range(0, set.Items.Length), x => set.Items[x].Key.Kind == MonoKind.ItemAdapter);
        Assert.Empty(set.Targets(adapter).ToArray());
    }

    // K 4.6 for tables (VirtualGenericDispatchTest's dependency programs): an override that creates an object of its own struct's instance
    // reuses the descriptor of a finite cycle, and one of a growing instance stops when 16 bodies of its definition are on the chain,
    // GenerationResourceLimit_Kd at the creation, a root-module site.
    [Fact]
    public void ATablesDependenciesReuseAFiniteCycleAndStopAGrowingKey()
    {
        var (program, set) = Collect(VirtualGenericDispatchTest.DependencyProgram("i32"));
        Assert.Empty(set.Records);
        Assert.Equal(["Fn read [Leaf<i32>]"], Targets(program, set, "VTable Leaf<i32>"));
        Assert.Contains("ObjectDescriptor Leaf<i32>", Calls(program, set, "Fn read [Leaf<i32>]"));
        var c = MirBuildTest.Compile(VirtualGenericDispatchTest.DependencyProgram("Next<T>"));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var growing = c.BuildMir();
        var records = new InstanceCollector(c.Types, c.Layout).Run(growing, 1);
        Assert.Equal(16, records.Items.ToArray().Count(x => Label(growing, x).StartsWith("Fn read [Leaf<", StringComparison.Ordinal)));
        Assert.Equal(
            [(DiagnosticCode.GenerationResourceLimit_Kd, "Leaf<Next<T>>.init()@obj")],
            records.Records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString())));
    }

    // K 4.3 (VirtualGenericDispatchTest): an original that a table replaces adds nothing the table does not reach, and a program that makes
    // no object has no object item and no instance of a slot's body.
    [Fact]
    public void OnlyTheTablesSlotBodiesAreInstances()
    {
        var (program, set) = Collect("struct Next<T>\n    T is Owned\nopen struct Base<T>\n    T is Owned\n    public init() => ()\n" +
            "    public virtual func read(self: objref/Self) -> i32\n        let child = Base<Next<T>>.init()@obj\n        return 1\n" +
            "struct Leaf<T> : Base<T>\n    T is Owned\n    public init() => ()\n    override func read(self: objref/Self) -> i32 => 7\n" +
            "let a = Leaf<string>.init()@obj\nrequire a.read() == 7 else => $abort(\"replaced\")\n");
        Assert.Empty(set.Records);
        Assert.Equal(["Fn read [Leaf<string>]"], Targets(program, set, "VTable Leaf<string>"));
        Assert.DoesNotContain(set.Items.ToArray(), x => Label(program, x).StartsWith("ObjectDescriptor Base<", StringComparison.Ordinal));
        (program, set) = Collect(VirtualGenericDispatchTest.Hierarchy + "()");
        Assert.DoesNotContain(set.Items.ToArray(), static x => x.Key.Kind is MonoKind.ObjectDescriptor or MonoKind.VTable || x.Representative is not null);
    }

    // R4-M28, F 4.3 (VirtualConditionalDispatchTest): a conditional slot, a Block member or a clause, that Applies refutes at its layer has no
    // target in that instance's table, the other slots keep theirs in slot order, and the override's body is not an instance there; an
    // inapplicable body's own creations add nothing.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARefutedSlotHasNoTarget(bool block)
    {
        var (program, set) = Collect(VirtualConditionalDispatchTest.Program(block));
        Assert.Empty(set.Records);
        Assert.Equal(["Fn first [Base<i32>]", "Fn copy [Leaf<i32>]", "Fn last [Leaf<i32>]"], Targets(program, set, "VTable Leaf<i32>"));
        Assert.Equal(["Fn first [Base<NonCopy>]", "Fn last [Leaf<NonCopy>]"], Targets(program, set, "VTable Leaf<NonCopy>"));
        Assert.DoesNotContain(set.Items.ToArray(), x => Label(program, x) == "Fn copy [Leaf<NonCopy>]");
        const string Copy = "override func copy(self: objref/Self, value: ref/T) -> T";
        var (growing, inapplicable) = Collect(VirtualConditionalDispatchTest.Program(block)
            .Replace(Copy + " => value@follow", Copy + "\n        let child = Leaf<(T, T)>.init()@obj\n        return value@follow", StringComparison.Ordinal)
            .Replace("let a = Leaf<i32>.init()@obj\n", string.Empty, StringComparison.Ordinal)
            .Replace("a.copy(7) == 7 and a.last() == 30 and ", string.Empty, StringComparison.Ordinal));
        Assert.Empty(inapplicable.Records);
        Assert.Equal(
            ["VTable Leaf<NonCopy>"],
            inapplicable.Items.ToArray().Where(static x => x.Key.Kind == MonoKind.VTable).Select(x => Label(growing, x)));
    }

    // F4's two published outcomes of a failed override (DECISIONS R5-M62, R4-M18): a struct whose override failed at slot selection has no
    // table, so its descriptor has no VTable; a definition that failed later stays in the table without a MIR body, so its slot has no
    // target. Both are Binding's errors, so the collector records nothing.
    [Fact]
    public void AFailedOverrideIsNoTargetAndNoRecord()
    {
        var unresolved = MirBuildTest.Compile(VirtualConditionalDispatchTest.Program(false).Replace(
            "override func last(self: objref/Self) -> i32 => 30", "override func last(self: objref/Self) -> i64 => 30", StringComparison.Ordinal));
        Assert.False(unresolved.Binding.Result.IsComplete);
        var program = unresolved.BuildMir();
        var set = new InstanceCollector(unresolved.Types, unresolved.Layout).Run(program, 1);
        Assert.Empty(set.Records);
        Assert.Empty(Targets(program, set, "ObjectDescriptor Leaf<i32>"));
        var later = MirBuildTest.Compile("open struct Base\n    public init() => ()\n    public virtual func one(self: objref/Self) -> i32 => 1\n" +
            "    public virtual func bump(self: objuniq/Self) -> i32 => 2\nlet a = Base.init()@obj\n");
        Assert.False(later.Binding.Result.IsComplete);
        program = later.BuildMir();
        set = new InstanceCollector(later.Types, later.Layout).Run(program, 1);
        Assert.Empty(set.Records);
        Assert.Equal(["Fn one"], Targets(program, set, "VTable Base"));
    }

    // K 4.5 rows 5, 12 and 13, DECISIONS R5-M70: a requirement's function item is, in the instance, the item of the witness its requirement
    // runs on the Item's Self through the Contract reference it records (TyCtxt.Witness(item)), where it is made, where it is called and as
    // its adapter's target: a user conformance's body, a built-in Type's Builtin shim, and through a bound reference the witness of the
    // conformance it names (Tag<bool>, not Tag<i32>), the body Binding's Item context also runs.
    [Fact]
    public void ARequirementItemIsItsWitnessInTheInstance()
    {
        var c = MirBuildTest.Compile(ItemWitnessSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var program = c.BuildMir();
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Empty(set.Records);
        Assert.Equal(["Fn equals", "Fn equals"], Targets(program, set, "Fn same [Box]"));
        Assert.Equal(["Builtin i32 BuiltinEquals", "Builtin i32 BuiltinEquals"], Targets(program, set, "Fn same [i32]"));
        Assert.Equal(["Fn tag", "ItemAdapter function item Tag.tag<Stamp, Tag<bool>>", "Fn tag", "Fn tag"], Calls(program, set, "Fn pick [Stamp]"));
        var adapter = Item(program, set, "ItemAdapter function item Tag.tag<Stamp, Tag<bool>>");
        var witness = set.Targets(adapter).ToArray().Single().Item;
        Assert.All(
            set.Targets(Item(program, set, "Fn pick [Stamp]")).ToArray().Where(x => set.Items[x.Item].BodyOrdinal >= 0),
            x => Assert.Equal(witness, x.Item));
        Assert.Equal("bool", TyCtxt.Name(((FunctionKoto)program.Bodies[set.Items[witness].BodyOrdinal].Key.Decl!.Declaration).Parameters[0].Type.TypeOf()!));
        var item = set.Items[adapter].RepresentativeType!;
        Assert.Same(c.Types.SubstOfItem(item), c.Types.Witness(item)!.Value.Subst);
    }

    // K 4.5 row 3, F package 4.5 (DECISIONS R5-M61, R5-M67): a requirement call in an instance is the item of its witness there. A conformance
    // witness, an inherited one, one of a generic Type's instance, one through a bound Contract reference, an accessor-call witness's
    // accessors, a fixed array's entry member and the integer Position (its i32 specialization) are body items; a standard property
    // witness's operations are property-witness items keyed by the Field and a built-in comparison or arithmetic a Builtin item keyed by
    // its compiler function, each with the erased Self.
    [Fact]
    public void ARequirementCallIsItsWitnessInTheInstance()
    {
        var c = MirBuildTest.Compile(TyCtxtTest.WitnessSource);
        var program = c.BuildMir();
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Empty(set.Records);
        Assert.Equal(["Fn area"], Targets(program, set, "Fn measure [Square]"));
        Assert.Equal(["Fn area"], Targets(program, set, "Fn measure [Leaf]"));
        Assert.Equal(["Fn area [Held<i32>]"], Targets(program, set, "Fn measure [Held<i32>]"));
        var inherited = set.Items[set.Targets(Item(program, set, "Fn measure [Leaf]"))[0].Item];
        Assert.Equal("struct Base", $"{program.Bodies[inherited.BodyOrdinal].Key.Decl!.Scope.Owner}");
        Assert.Equal(
            ["PropertyWitness Tally StorageCopy", "PropertyWitness Tally StorageSet", "PropertyWitness Tally StorageCopy"],
            Targets(program, set, "Fn bump [Tally]"));
        Assert.Equal(["Getter count.Get", "Setter count.Set", "Getter count.Get"], Targets(program, set, "Fn bump [Gauge]"));
        Assert.Equal(["Fn added"], Targets(program, set, "Fn add [Plain]"));
        Assert.Equal(["Fn added [Wrap<i32>]"], Targets(program, set, "Fn add [Wrap<i32>]"));
        Assert.Equal(["Builtin i32 BuiltinArithmetic"], Targets(program, set, "Fn add [i32]"));
        Assert.Equal(["Builtin i32 BuiltinEquals"], Targets(program, set, "Fn same [i32]"));
        Assert.Equal(
            ["library Fn iterate in group FixedArray [i32, ]", "library Fn next in struct ArrayIterator [ArrayIterator<i32>]"],
            Targets(program, set, "Fn count [[3 of i32]]"));
        Assert.Equal(["library Fn tryResolve in group IntegerPosition"], Targets(program, set, "Fn at [i32]"));
        var shims = set.Items.ToArray().Where(static x => x.Key.Kind is MonoKind.Builtin or MonoKind.PropertyWitness).ToArray();
        Assert.Equal(4, shims.Length);
        Assert.All(shims, x => Assert.Same(c.Types.Erase(x.RepresentativeType!), x.Key.Type));
        Assert.All(shims, static x => Assert.Equal(-1, x.BodyOrdinal));
    }

    // K 4.5 row 3, S2-S4 X14: a standard property witness's operations are items keyed by the Field, the erased Self and the operation, whose
    // requirement signatures agree with the key's; a set drops the Field's old value, so its item targets the Field Type's glue. The key's
    // signature (StorageSignature, the one statement the agreement and the shim's function read) is the requirement's in the instance:
    // `set(self: uniq/Self, value: Array<i32>) -> ()` and `get(self: ref/Self) -> ref/Array<i32>` (SPEC 11.3.1) on Bag.
    [Fact]
    public void AStandardPropertyWitnessIsItsFieldsShim()
    {
        var c = MirBuildTest.Compile(HeldSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var program = c.BuildMir();
        var collector = new InstanceCollector(c.Types, c.Layout);
        var set = collector.Run(program, 1);
        Assert.Equal(["()", "uniq/Bag", "Array<i32>"], Signature(Item(program, set, "PropertyWitness Bag StorageSet")));
        Assert.Equal(["ref/Array<i32>", "ref/Bag"], Signature(Item(program, set, "PropertyWitness Bag StorageBorrow")));
        Assert.Empty(set.Records);
        Assert.Equal(
            ["PropertyWitness Bag StorageSet", "DropGlue Array<i32>", "PropertyWitness Bag StorageBorrow"],
            Targets(program, set, "Fn refill [Bag]")); // `[1, 2]` is one Array aggregate, which targets no routine (DECISIONS R5-M129).
        Assert.Equal(["DropGlue Array<i32>"], Targets(program, set, "PropertyWitness Bag StorageSet"));
        Assert.Empty(Targets(program, set, "PropertyWitness Bag StorageBorrow"));
        Assert.All(set.Items.ToArray().Where(static x => x.Key.Kind == MonoKind.PropertyWitness), static x => Assert.Equal("items", x.Key.Symbol!.Name));

        // The result, then each parameter slot.
        string[] Signature(int item)
        {
            var result = TyCtxt.Name(collector.StorageSignature(item, -1, out var count)!);
            return [result, .. Enumerable.Range(0, count).Select(slot => TyCtxt.Name(collector.StorageSignature(item, slot, out _)!))];
        }
    }

    // K 4.5 row 2, DECISIONS R5-M30, R5-M70: a call of a function without a body whose published row formats a value (Text.toString,
    // Text.tryFormat, Utf8Writer.write) is the item of its Type's Utf8Format witness in the instance: a user Type's format body, a built-in
    // Type's BuiltinFormat shim, keyed by its compiler function and the erased Type, which calls no witness.
    [Fact]
    public void AFormattingCallIsItsValuesFormatWitness()
    {
        var (program, set) = Collect(FormatSource);
        Assert.Empty(set.Records);
        Assert.Equal(["Fn format"], Calls(program, set, "Fn show [Box]"));
        Assert.Equal(["Builtin i32 BuiltinFormat"], Calls(program, set, "Fn show [i32]"));
        Assert.Equal(["Builtin i32 BuiltinFormat"], Calls(program, set, "Fn format"));
        Assert.Equal(["Fn format"], Calls(program, set, "Fn fill [Box]"));
        Assert.Equal(["Fn format"], Calls(program, set, "Fn emit [Box]"));
        Assert.Equal(["Builtin i32 BuiltinFormat"], Calls(program, set, "Fn emit [i32]"));
        Assert.Empty(Targets(program, set, "Builtin i32 BuiltinFormat"));
    }

    // K 4.5 row 6, SPEC 22.10.1: an erased writer over a user BufferWriter is the item of its destination's reserve witness; a fixed
    // buffer's reserve, a compiler function without a body, has none (its legacy row's, C8*).
    [Fact]
    public void AnErasedWriterIsItsDestinationsReserveWitness()
    {
        var (program, set) = Collect(FormatSource);
        Assert.Equal(["Fn init", "Fn reserve", "Fn init", "Fn emit [Box]"], Calls(program, set, "Fn toSink"));
        var reserve = set.Items[set.Targets(Item(program, set, "Fn toSink"))[1].Item];
        Assert.Equal("struct Sink", $"{program.Bodies[reserve.BodyOrdinal].Key.Decl!.Scope.Owner}");
        Assert.Equal(["Fn emit [i32]"], Calls(program, set, "Fn toFixed"));
    }

    // K 4.3, F package 4.6: a built-in comparison of a composite Self, a generic instance's comparison over its Type parameter, is its
    // Builtin shim, keyed by its compiler function and the erased Self, whose targets are its leaves' witnesses under the Contract's mapping
    // (TyCtxt.Comparison), depth first: each user leaf's equals or compare body, none for a built-in leaf; two instances comparing one
    // erased Self share its shim. A tuple operator is no shim: its expansion calls its leaves' witnesses in the instance (F package 4.8,
    // DECISIONS R5-M75).
    [Fact]
    public void AComparisonShimsTargetsAreItsLeafWitnesses()
    {
        var (program, set) = Collect(CompareSource);
        Assert.Empty(set.Records);
        Assert.Equal(["Fn equals"], Targets(program, set, "Builtin (ref/Box, i32) BuiltinEquals"));
        Assert.Equal(["Fn equals", "Fn equals"], Targets(program, set, "Builtin ((ref/Box, i32), ref/Box) BuiltinEquals"));
        Assert.Equal(["Fn compare"], Targets(program, set, "Builtin (ref/Rank, i32) BuiltinCompare"));
        Assert.Equal(["Builtin (ref/Box, i32) BuiltinEquals"], Targets(program, set, "Fn equal [(ref/Box, i32)]"));
        Assert.Equal(
            set.Targets(Item(program, set, "Fn equal [(ref/Box, i32)]"))[0].Item,
            set.Targets(Item(program, set, "Fn differ [(ref/Box, i32)]"))[0].Item);
        Assert.Equal(["Fn equals"], Targets(program, set, "Fn same [Box]"));
        Assert.Equal(
            ["struct Box", "struct Box", "struct Box", "struct Rank"],
            new[] { "Builtin (ref/Box, i32) BuiltinEquals", "Builtin ((ref/Box, i32), ref/Box) BuiltinEquals", "Builtin (ref/Rank, i32) BuiltinCompare" }
                .SelectMany(x => set.Targets(Item(program, set, x)).ToArray())
                .Select(x => $"{program.Bodies[set.Items[x.Item].BodyOrdinal].Key.Decl!.Scope.Owner}"));
    }

    // K 4.3, DECISIONS R5-M70: a Builtin item is keyed by its compiler function and the erased Self, so two built-in operations on one Self
    // (i32's addition and subtraction) are two items, each its operation's Contract's.
    [Fact]
    public void TwoBuiltinOperationsOnOneSelfAreTwoItems()
    {
        var (program, set) = Collect("func both<T>(left: ref/T, right: ref/i32) -> i32\n    T is Addable<i32>\n    T is Subtractable<i32>\n" +
            "    T.(Addable<i32>).Output is i32\n    T.(Subtractable<i32>).Output is i32\n    return left.added(right) + left.subtracted(right)\n" +
            "let five = 5\nrequire both(five, 2) == 10 else => $abort(\"both\")\n");
        Assert.Empty(set.Records);
        var shims = set.Targets(Item(program, set, "Fn both [i32]")).ToArray().Select(x => set.Items[x.Item]).ToArray();
        Assert.Equal(["Builtin i32 BuiltinArithmetic", "Builtin i32 BuiltinArithmetic"], shims.Select(x => Label(program, x)));
        Assert.Equal(["contract Addable", "contract Subtractable"], shims.Select(static x => $"{x.Key.Symbol!.Scope.Owner}"));
        Assert.Equal(2, shims.Select(x => set.Find(x.Key)).Distinct().Count());
    }

    // K 4.3 (DECISIONS R5-M126, its departure (i)): a shim item is represented by its Self and by the substitution of the call that first
    // admits it, so its requirement's signature in its instance (TyCtxt.Signature through TypeOf) is closed, also a binary arithmetic
    // requirement's, whose Rhs and Output its Self alone leaves open; a second call of one key keeps the item (fail-before: no representative,
    // added's right operand ref/Rhs and its result open).
    [Fact]
    public void AShimItemIsRepresentedByItsAdmittingCall()
    {
        var c = MirBuildTest.Compile(
            "func sum<T>(left: ref/T, right: ref/T) -> T\n    T is Addable<T>\n    T.(Addable<T>).Output is T\n    return left + right\n" +
            "func same<T>(left: ref/T, right: ref/T) -> bool\n    T is Equatable\n    return left == right\n" +
            "let a = 2\nlet b = 3\nrequire sum(a@ref, b@ref) == 5 and same(a@ref, a@ref) and not same(b@ref, a@ref) else => $abort(\"shims\")\n");
        var program = c.BuildMir();
        var collector = new InstanceCollector(c.Types, c.Layout);
        var set = collector.Run(program, 1);
        Assert.Empty(set.Records);
        var shims = Enumerable.Range(0, set.Items.Length).Where(x => set.Items[x].Key.Kind == MonoKind.Builtin).ToArray();
        var labels = shims.Select(x => Label(program, set.Items[x])).Order(StringComparer.Ordinal);
        Assert.Equal(["Builtin i32 BuiltinArithmetic", "Builtin i32 BuiltinEquals"], labels);
        foreach (var shim in shims)
        {
            var (function, representative) = (set.Items[shim].Key.Symbol!, set.Items[shim].Representative);
            Assert.Same(set.Items[shim].RepresentativeType, representative?.Self);
            _ = TyCtxt.Signature(function, null, int.MaxValue, out var count);
            var expected = function.CompilerFunction == CompilerFunctionKind.BuiltinEquals ? "bool" : "i32";
            var types = Enumerable.Range(-1, count + 1).Select(x => TyCtxt.Name(collector.TypeOf(shim, TyCtxt.Signature(function, null, x, out _)!)!));
            Assert.Equal([expected, "ref/i32", "ref/i32"], types);
        }
    }

    // PLAN G157 (4), PD30: a requirement call whose Self nests past Binding's storage bound in the instance (Box^65<i32>, storage depth 65;
    // no local of the body stores it) is the storage-nesting gap at the call, while the twin one level below is its witness's instance.
    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void ADeepSelfIsAGapAtTheRequirementCall(int depth, bool admitted)
    {
        var type = string.Concat(Enumerable.Repeat("Box<", depth)) + "i32" + new string('>', depth);
        var c = MirBuildTest.Compile(TagSource + $"require f<{type}>() == 1 else => $abort(\"tag\")\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var program = c.BuildMir();
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Equal(admitted, set.Items.ToArray().Any(x => Label(program, x).StartsWith("Fn tag [", StringComparison.Ordinal)));
        Assert.Equal(admitted ? [] : [(DiagnosticCode.Unsupported_Kd, "T.tag()")], set.Records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString())));
    }

    // R5-M61: a requirement call of a closed Self has a witness; none is a broken contract, InternalInvariant_Kd at the call. Binding never
    // gives one, so the test drops the Contract reference of measure's requirement call.
    [Fact]
    public void NoWitnessForAClosedSelfIsABrokenContract()
    {
        var c = MirBuildTest.Compile(TyCtxtTest.WitnessSource);
        var program = c.BuildMir();
        var measure = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "measure");
        var block = Enumerable.Range(0, measure.Blocks.Length).Single(x => measure.Blocks[x].Terminator.Callee.Kind == MirCalleeKind.Requirement);
        var call = measure.Blocks[block].Terminator;
        measure.Terminate(block, call with { Callee = call.Callee with { Contract = null } });
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Equal([(DiagnosticCode.InternalInvariant_Kd, "x.area()")], set.Records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString())));
        Assert.Empty(Targets(program, set, "Fn measure [Square]"));
    }

    // K 4.3: a type-driven item's representative is closed; an open one is a broken contract, InternalInvariant_Kd at its request. Binding
    // never gives one, so the test appends an erasure of an open function item to the top-level body in a new unreachable block, into a
    // Function Type (a parameter's) as the validator requires of an erasure's target.
    [Fact]
    public void AnOpenAdapterTypeIsABrokenContract()
    {
        var c = MirBuildTest.Compile("func pick<T>(value: T) -> T => value@move\nfunc hold<T>(value: T) -> i32\n    let item = pick<T>\n    return 1\n" +
            "func take(f: (i32) -> i32) => ()\nlet z = 1\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var program = c.BuildMir();
        var hold = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "hold");
        var open = hold.Consts.ToArray().Single(static x => x.Kind == MirConstKind.FnItem).Type;
        var top = program.Bodies.ToArray().Single(static x => x.Key.Decl is null && x.Source.Module == 0);
        var statement = top.Statements.ToArray().First(static x => x.Kind == MirStatementKind.Assign);
        var operand = top.AddOperand(MirOperand.Const(top.Const(new(MirConstKind.FnItem, open))));
        var block = top.AddBlock();
        var function = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "take").Locals[1].Type;
        top.Push(block, statement with { Rvalue = new(MirRvalueKind.Cast, (byte)MirCastKind.Erasure, Start: operand, Count: 1, Type: function) });
        top.Terminate(block, MirTerminator.Unreachable(statement.Source));
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Equal([(DiagnosticCode.InternalInvariant_Kd, statement.Source.Syntax)], set.Records.Select(static x => (x.Code, x.Subject)));
        Assert.DoesNotContain(set.Items.ToArray(), static x => x.Key.Kind == MonoKind.ItemAdapter);
    }

    // K 4.5 row 10, SPEC 16.3.2 (S2-S4 X3): a Drop is its dropped Type's DropGlue item, whose targets are the glues of its direct parts that
    // need a drop, in destruction order: a struct's Fields last to first (an i32 Field none), then its base; a Tuple's elements in
    // decreasing index; an enum's Case payloads, each Case's last to first. A Type with nothing to destroy has no glue.
    [Fact]
    public void AGluesTargetsAreItsPartsInDestructionOrder()
    {
        var (program, set) = Collect("open struct Base\n    public var log: Array<u8> = []\n    public init() => ()\nstruct Triple: Base\n" +
            "    public var a: string = \"a\"\n    public var n: i32 = 0\n    public var b: Array<i32> = []\n" +
            "    public var c: (Array<bool>, string) = ([], \"c\")\n    public init() : base() => ()\n" +
            "enum Shape\n    Pair(string, Array<i32>)\n    Single(Array<bool>)\n    Empty\nstruct Plain\n    public var n: i32 = 1\n    public init() => ()\n" +
            "func use() -> ()\n    let t = Triple.init()\n    let s = Shape.Pair(\"a\", [1])\n    let p = Plain.init()\n    return\n");
        Assert.Empty(set.Records);
        Assert.Equal(["DropGlue (Array<bool>, string)", "DropGlue Array<i32>", "DropGlue string", "DropGlue Base"], Targets(program, set, "DropGlue Triple"));
        Assert.Equal(["DropGlue string", "DropGlue Array<bool>"], Targets(program, set, "DropGlue (Array<bool>, string)"));
        Assert.Equal(["DropGlue Array<u8>"], Targets(program, set, "DropGlue Base"));
        Assert.Equal(["DropGlue Array<i32>", "DropGlue string", "DropGlue Array<bool>"], Targets(program, set, "DropGlue Shape"));
        Assert.Contains("DropGlue Triple", Targets(program, set, "Fn use"));
        Assert.DoesNotContain(set.Items.ToArray(), x => Label(program, x) == "DropGlue Plain");
    }

    // PLAN G157 (6), K 4.5 "DropGlue" (X27; DECISIONS R5-M95): DropGlue.Parts, which the collector targets and code generation writes from,
    // and MirTypes.Destruction, which drop elaboration and the effects walk, decompose a Type alike. Over a glue Type of each kind (a struct
    // with a base, its base, a Tuple, an enum, a struct that declares a drop, a nested generic struct, a fixed array, an Array, a Dictionary, a
    // Sealed rc handle and a Closure environment) the walk's direct descents are the parts in reverse, a struct's base last and an enum's Case
    // payloads each reversed; a struct that declares a drop and a handle are not descended, their part the destructor and the Sealed payload.
    // Each part's position names its component in the aggregate writer's numbering (a struct's base 0, its Fields after it).
    [Fact]
    public void DropGluePartsAgreeWithTheDestructionWalk()
    {
        var c = MirBuildTest.Compile("open struct Base\n    public var log: Array<u8> = []\n    public init() => ()\nstruct Triple: Base\n" +
            "    public var a: string = \"a\"\n    public var n: i32 = 0\n    public var b: Array<i32> = []\n" +
            "    public var c: (Array<bool>, string) = ([], \"c\")\n    public init() : base() => ()\n" +
            "enum Shape\n    Pair(string, Array<i32>)\n    Single(Array<bool>)\n    Empty\n" +
            "struct Res\n    public var s: string = \"s\"\n    public init() => ()\n    drop => ()\n" +
            "struct Holder<T>\n    public var value: T\n    public var n: i32 = 0\n    public init(value: T) => self.value = value@move\n" +
            "struct Leaf\n    public var name: string\n    public init(name: string) => self.name = name@move\n" +
            "func use(text: string) -> ()\n    let t = Triple.init()\n    let s = Shape.Pair(\"a\", [1])\n    let r = Res.init()\n" +
            "    let h = Holder<(string, Holder<string>)>.init((\"a\", Holder<string>.init(\"b\")))\n    let f: [2 of string] = [\"x\", \"y\"]\n" +
            "    let d: Dictionary<string, Array<i32>> = [:]\n    let o = Kimi.Intrinsics.makeRc(Leaf.init(\"o\"))\n" +
            "    let e = func [text@move] () -> i32 => 1\n    return\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var set = new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1);
        var (parts, descents, names) = (new List<DropPart>(), new List<BoundType>(), new HashSet<string>());
        foreach (var type in set.Items.ToArray().Where(static x => x.Key.Kind == MonoKind.DropGlue).Select(static x => x.RepresentativeType!))
        {
            parts.Clear();
            descents.Clear();
            DropGlue.Parts(c.Types, type, parts);
            var walk = new Descents(descents);
            _ = MirTypes.Destruction(type, ref walk);
            Assert.Equal(Expected(c.Types, type, descents), parts.Select(static x => x.Type));
            Assert.All(parts, x => Assert.Same(Component(type, x), x.Type));
            names.Add(TyCtxt.Name(type));
        }

        Assert.Subset(names, new HashSet<string>(["Triple", "Base", "(Array<bool>, string)", "Shape", "Res", "Holder<(string, Holder<string>)>",
            "[2 of string]", "Array<i32>", "Dictionary<string, Array<i32>>", "rc/Leaf", "closure () -> i32"]));
    }

    // K 4.5 "DropGlue", SPEC 16.3.2: a fixed array's glue destroys its elements through its element Type's glue, and a fixed array of
    // elements that need no drop has none; a derived struct's glue ends with its base's, which is the base's declared destructor alone, as
    // a base without one ends with its Fields.
    [Fact]
    public void AFixedArraysAndADerivedStructsGluesEndWithTheirParts()
    {
        var (program, set) = Collect("open struct Res\n    public var id: i32\n    public init(id: i32) => self.id = id\n    drop => ()\n" +
            "struct Derived: Res\n    public var name: string = \"d\"\n    public var count: i32 = 0\n    public init() : base(1) => ()\n" +
            "func use() -> ()\n    let f: [2 of string] = [\"x\", \"y\"]\n    let g: [2 of i32] = [1, 2]\n    let d = Derived.init()\n    return\n");
        Assert.Empty(set.Records);
        Assert.Equal(["DropGlue string"], Targets(program, set, "DropGlue [2 of string]"));
        Assert.DoesNotContain(set.Items.ToArray(), x => Label(program, x) == "DropGlue [2 of i32]");
        Assert.Equal(["DropGlue string", "DropGlue Res"], Targets(program, set, "DropGlue Derived"));
        Assert.Equal(["Fn drop"], Targets(program, set, "DropGlue Res"));
    }

    // K 4.5 "DropGlue", R4-M6b, SPEC 16.3.2: a struct that declares a drop is destroyed by its destructor alone, the Body of the struct's
    // instance (TyCtxt.Synthesized), which drops the struct's own Fields: one destructor item per instance of a generic struct, the root
    // for a closed one.
    [Fact]
    public void ADestructorIsItsStructsGlue()
    {
        var (program, set) = Collect("struct Holder<T>\n    public var value: T\n    public init(value: T) => self.value = value@move\n    drop => ()\n" +
            "struct Res\n    public var id: i32\n    public init(id: i32) => self.id = id\n    drop => ()\n" +
            "func use() -> ()\n    let h = Holder<i32>.init(1)\n    let g = Holder<string>.init(\"s\")\n    let r = Res.init(1)\n    return\n");
        Assert.Empty(set.Records);
        Assert.Equal(["Fn drop [Holder<i32>]"], Targets(program, set, "DropGlue Holder<i32>"));
        Assert.Equal(["Fn drop [Holder<string>]"], Targets(program, set, "DropGlue Holder<string>"));
        Assert.Equal(["Fn drop"], Targets(program, set, "DropGlue Res"));
        Assert.InRange(Item(program, set, "Fn drop"), 0, set.RootCount - 1);
        Assert.Empty(Targets(program, set, "Fn drop [Holder<i32>]"));
        Assert.Equal(["DropGlue string"], Targets(program, set, "Fn drop [Holder<string>]"));
    }

    // K 4.6 for destructors: a destructor that destroys a value of a growing instance of its struct stops when 16 destructor items of its
    // definition are on the chain, GenerationResourceLimit_Kd at the destroyed local in the destructor, a root-module site.
    [Fact]
    public void AGrowingDestructorStopsAtItsDrop()
    {
        var c = MirBuildTest.Compile("struct Grow<T>\n    public var value: Option<T>\n    public init(value: Option<T>) => self.value = value@move\n" +
            "    drop\n        let next = Grow<(T,)>.init(.None)\nfunc use() -> ()\n    let g = Grow<i32>.init(.None)\n    return\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var program = c.BuildMir();
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Equal(16, set.Items.ToArray().Count(x => Label(program, x).StartsWith("Fn drop [", StringComparison.Ordinal)));
        Assert.Equal(
            [(DiagnosticCode.GenerationResourceLimit_Kd, "let next = Grow<(T,)>.init(.None)")],
            set.Records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString())));
    }

    // K 4.5 "DropGlue", SPEC 16.3.3: an owning handle's glue destroys a Sealed payload through the payload's glue; an open payload is destroyed
    // as its actual Type, which the object descriptor's finalizer runs (K8), so that glue has no target.
    [Fact]
    public void AHandlesGlueDestroysASealedPayload()
    {
        var (program, set) = Collect("struct Leaf\n    public var name: string\n    public init(name: string) => self.name = name@move\nopen struct Node\n" +
            "    public var name: string\n    public init(name: string) => self.name = name@move\nfunc use() -> ()\n" +
            "    let a = Kimi.Intrinsics.makeRc(Leaf.init(\"a\"))\n    let b = Kimi.Intrinsics.makeArc(Leaf.init(\"b\"))\n" +
            "    let c = Kimi.Intrinsics.makeRc(Node.init(\"c\"))\n    return\n");
        Assert.Equal(["DropGlue Leaf"], Targets(program, set, "DropGlue rc/Leaf"));
        Assert.Equal(["DropGlue Leaf"], Targets(program, set, "DropGlue arc/Leaf"));
        Assert.Empty(Targets(program, set, "DropGlue rc/Node"));
    }

    // K 4.5 "DropGlue" (X23), rows 10 and 13: a Closure environment's glue destroys its captures last to first, and the Closure's erasure
    // targets the environment's glue after its body; a Function value's glue has no target, since its environment's destruction is erased.
    [Fact]
    public void AClosuresGlueDestroysItsCapturesLastToFirst()
    {
        var (program, set) = Collect("func capture(text: string, names: Array<i32>) -> i32\n    let f = func [text@move, names@move] () -> i32 => 1\n" +
            "    let e: () -> i32 = f\n    return e()\n");
        Assert.Equal(["Closure capture", "DropGlue closure () -> i32"], Targets(program, set, "ClosureErasure closure () -> i32"));
        Assert.Equal(["DropGlue Array<i32>", "DropGlue string"], Targets(program, set, "DropGlue closure () -> i32"));
        Assert.Empty(Targets(program, set, "DropGlue () -> i32"));
    }

    // SPEC 8.10 (DECISIONS R5-M102): an owner Callable call whose callee borrows its environment destroys the operand after the call, so the
    // operand Type's glue is the call's second target, after the body; a common Function value, whose entries borrow, has the glue as its one
    // target; a Consuming body destroys it itself, and a call through a borrow transfers nothing, so neither has one.
    [Fact]
    public void AnOwnerCallOfABorrowingCalleeTargetsItsOperandsGlue()
    {
        var (program, set) = Collect("func invoke<F>(action: F) -> i32\n    F is Callable<owner, () -> i32>\n    return action@move()\n" +
            "func lend<F>(action: ref/F) -> i32\n    F is Callable<() -> i32>\n    return action()\nfunc take(s: string) -> i32 => 2\n" +
            "func host(a: string, b: string, c: string) -> i32\n    let shared = func [a@move] () -> i32 => 1\n" +
            "    let consuming = func [b@move] () -> i32 => take(b@move)\n    let lent = func [c@move] () -> i32 => 3\n" +
            "    return invoke(shared@move) + invoke(consuming@move) + lend(lent@ref)\nfunc erased(f: () -> i32) -> i32 => invoke(f@move)\n");
        var calls = Enumerable.Range(0, set.Items.Length).Where(x => Label(program, set.Items[x]).StartsWith("Fn invoke", StringComparison.Ordinal) ||
            Label(program, set.Items[x]).StartsWith("Fn lend", StringComparison.Ordinal)).Select(x => AtValueCall(x)).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["Closure host", "Closure host", "Closure host|DropGlue closure () -> i32", "DropGlue () -> i32"], calls);

        // The labels of an item's targets at its one Value call, joined.
        string AtValueCall(int item)
        {
            var body = program.Bodies[set.Items[item].BodyOrdinal];
            var block = Enumerable.Range(0, body.Blocks.Length)
                .Single(x => body.Blocks[x].Terminator is { Kind: MirTerminatorKind.Call, Callee.Kind: MirCalleeKind.Value });
            return string.Join("|", set.Targets(item).ToArray().Where(x => x.At == new MirLocation(block, body.Blocks[block].Count))
                .Select(x => Label(program, set.Items[x.Item])));
        }
    }

    // K 4.5 "DropGlue" (X4; DECISIONS R5-M124, the owner's R5-M125): an Array's glue targets the glues of its element Types that need a drop;
    // a Dictionary's the clearEntries instance its Drop row declares, whose body reaches the entries' glues (their destroyDictionaryEntries).
    [Fact]
    public void ACollectionsGlueDestroysItsElements()
    {
        var (program, set) = Collect("func use() -> ()\n    let names: Array<string> = []\n    let counts: Array<i32> = []\n" +
            "    let table: Dictionary<string, Array<i32>> = [:]\n    return\n");
        Assert.Equal(["DropGlue string"], Targets(program, set, "DropGlue Array<string>"));
        Assert.Empty(Targets(program, set, "DropGlue Array<i32>"));
        const string Clear = "library Fn clearEntries in group DictionaryStorage [string, Array<i32>]";
        Assert.Equal([Clear], Targets(program, set, "DropGlue Dictionary<string, Array<i32>>"));
        Assert.Contains(Calls(program, set, Clear), static x => x.StartsWith("library Fn destroyDictionaryEntries", StringComparison.Ordinal));
    }

    // G157 (6), SPEC 4.9.7, X4 (DECISIONS R5-M27, R5-M124, the owner's R5-M125): a Dictionary's glue targets exactly the instance of the Entries
    // callee its Drop row declares, clearEntries over its entry Types, when an entry may need a drop (CallsEntries), else nothing; a value or a
    // key that needs one is enough.
    [Theory]
    [InlineData("i32, Res", true)]
    [InlineData("string, i32", true)]
    [InlineData("i32, i64", false)]
    public void ADictionarysGlueTargetsTheClearEntriesItsDropRowDeclares(string entry, bool clears)
    {
        var (program, set) = Collect("struct Res\n    public let id: i32\n    public init(id: i32) => self.id = id\n    drop => ()\n" +
            $"func d(values: Dictionary<{entry}>) => ()\n");
        Assert.Empty(set.Records);
        var glue = Targets(program, set, $"DropGlue Dictionary<{entry}>");
        Assert.Equal(clears ? new[] { $"library Fn clearEntries in group DictionaryStorage [{entry}]" } : Array.Empty<string>(), glue);
        var row = LegacyRuntimeHelpers.DictionaryRow(DictionaryHelperKind.Drop);
        Assert.Equal((DictionaryCallee.ClearEntries, HelperNeeds.ElementGlue), (row.Entries, row.Needs));
    }

    // K 4.3, 4.5 rows 2 and 11, X4, F2c (DECISIONS R5-M124): placeEntry's call targets the LegacyRow item of its function's instance,
    // represented by the Dictionary its raw handle designates, whose target is the body its row declares, append; a Dictionary from none
    // targets the body of the function the Dictionary runtime declares for construction, initialize; neither instantiates, so each has one
    // item.
    [Fact]
    public void PlaceEntryAndADictionaryFromNoneTargetTheirDeclaredBodies()
    {
        var (program, set) = Collect(DictionaryKey + "func put(d: uniq/Dictionary<Key, i64>)\n    _ = d.tryInsert(Key.init(1), 2)\n" +
            "func text(d: uniq/Dictionary<Key, string>)\n    _ = d.tryInsert(Key.init(1), \"a\")\n" +
            "func make() -> Dictionary<Key, i64> => [:]\n");
        Assert.Empty(set.Records);
        const string Append = "library Fn append in group DictionaryStorage";
        Assert.Equal(new[] { Append }, Assert.Single(RowTargets(program, set, "LegacyRow Dictionary<Key, i64> StoragePlaceDictionaryEntry")));
        Assert.Equal(new[] { Append }, Assert.Single(RowTargets(program, set, "LegacyRow Dictionary<Key, string> StoragePlaceDictionaryEntry")));
        Assert.Equal(["library Fn initialize in group DictionaryStorage"], Calls(program, set, "Fn make"));
        Assert.Single(set.Items.ToArray(), x => Label(program, x) == Append);
        Assert.Equal(DictionaryCallee.Append, LegacyRuntimeHelpers.Row(CompilerFunctionKind.StoragePlaceDictionaryEntry)!.Value.Callee);
    }

    // K 4.8, S2-S4 X34: a collection's own layout is its handle words, but its glue lays out its element Types, also those that need no drop,
    // whose verdicts join the glue item's: a C-layout element storing a zero-sized Field is InvalidCLayout_Kd at each request of the glue,
    // an empty local's declaration (written or inferred alike; Binding reports none, DECISIONS R4-M134 Q8), and a limit element the erased
    // Type's one Resource record; such a glue has no item. Records by code and the subject's first line, in record order.
    [Theory]
    [InlineData(
        "func use() -> ()\n    let a: Array<P<()>> = []\n    let b: Array<P<()>> = []\n    return\n",
        "InvalidCLayout_Kd let b: Array<P<()>> = []|InvalidCLayout_Kd let a: Array<P<()>> = []")]
    [InlineData("func g<T>(value: T) -> i32\n    let a: Array<P<T>> = []\n    return 0\nlet r = g(())\n", "InvalidCLayout_Kd let a: Array<P<T>> = []")]
    [InlineData("func g<T>(value: T) -> i32\n    let a: Array<P<T>> = []\n    return 0\nlet r = g(1)\n", "")]
    [InlineData(
        "func use() -> ()\n    let a: Array<[2147483648 of u8]> = []\n    let b: Array<[2147483648 of u8]> = []\n    return\n",
        "GenerationResourceLimit_Kd let b: Array<[2147483648 of u8]> = []")]
    public void ACollectionsGlueLaysOutItsElements(string uses, string records)
    {
        var c = MirBuildTest.Compile(CLayoutP + uses);
        var program = c.BuildMir();
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Equal(records, string.Join("|", set.Records.Select(x => $"{x.Code} {c.SyntaxOf(x.Subject).ToString().Split('\n')[0]}")));
        Assert.Equal(records.Length == 0, set.Items.ToArray().Any(x => Label(program, x).StartsWith("DropGlue Array<", StringComparison.Ordinal)));
    }

    // K 4.5 row 7: a static's access is its initializer's StaticInit body (MirProgram.StaticInit), a root in the root module, also from an
    // instance; a static of a generic container's group has no instance key (X33 (a): generic storage keys are P42's), so its accesses admit
    // nothing and its declaration is the static-storage gap once (DECISIONS R5-M82, superseding R5-M60's record at each access), which the
    // old path accepts for this immutable scalar (an R5-M18 (e) row).
    [Fact]
    public void AStaticAccessIsItsInitializer()
    {
        var c = MirBuildTest.Compile("struct Cache<T>\n    public group Statistics\n        public let count: i32 = 0\n" +
            "group Registry\n    public let total: i32 = 5\nfunc use() -> i32 => Cache<i32>.Statistics.count + Registry.total\n" +
            "func read<T>() -> i32 => Cache<T>.Statistics.count + Registry.total\nlet n = read<bool>()\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var program = c.BuildMir();
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Equal(["StaticInit total"], Targets(program, set, "Fn use"));
        Assert.Equal(["StaticInit total"], Targets(program, set, "Fn read [bool]"));
        Assert.InRange(Item(program, set, "StaticInit total"), 0, set.RootCount - 1);
        Assert.Equal([(DiagnosticCode.Unsupported_Kd, "public let count: i32 = 0")], set.Records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString())));
    }

    // DECISIONS R5-M82: a root-module static of storage P42 has not brought (a var, a Type that is not a scalar, a static of a generic
    // container's group, a literal one included) is the capability table's static-storage gap, Unsupported_Kd at its declaration, whether or
    // not anything reads it (K11 generates only what an output requires); an immutable scalar of a closed group is none, also one with a
    // first-access initializer.
    [Theory]
    [InlineData("group State\n    public var counter: i32 = 0\nlet x = 1\n", "public var counter: i32 = 0")]
    [InlineData("group State\n    public var counter: i32 = 0\nlet x = State.counter\n", "public var counter: i32 = 0")]
    [InlineData("group State\n    public let pair: (i32, i32) = (1, 2)\nlet x = 1\n", "public let pair: (i32, i32) = (1, 2)")]
    [InlineData("group State\n    public let counter: i32 = 0\nlet x = State.counter\n", null)]
    [InlineData("group State\n    public let counter: i32 = 0\nlet x = 1\n", null)]
    [InlineData("group State\n    public let counter: i32 = State.start()\n    public func start() -> i32 => 3\nlet x = State.counter\n", null)]
    [InlineData("struct Cache<T>\n    public group Statistics\n        public let count: i32 = 0\nlet x = 1\n", "public let count: i32 = 0")]
    public void UnsupportedStaticStorageIsAGapAtItsDeclaration(string source, string? declaration)
    {
        var c = MirBuildTest.Compile(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var set = new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1);
        Assert.Equal(
            declaration is null ? [] : [(DiagnosticCode.Unsupported_Kd, declaration)],
            set.Records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString())));
    }

    // K 4.5 row 9: a DestroyValues call is its Type argument's glue in the item's instance when that Type needs a drop. No library call makes
    // one before R8 (PD21), so the test turns a Raw.allocate call into one.
    [Fact]
    public void ADestroyValuesCallIsItsTypesGlue()
    {
        var c = MirBuildTest.Compile("func make<T>() -> raw/T => Raw.allocate<T>(0)\nlet p = make<string>()\nlet q = make<i32>()\nunsafe => Raw.release(p)\n" +
            "unsafe => Raw.release(q)\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var program = c.BuildMir();
        var make = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "make");
        var block = Enumerable.Range(0, make.Blocks.Length).Single(b => make.Blocks[b].Terminator is
            { Kind: MirTerminatorKind.Call, Callee: { Kind: MirCalleeKind.Intrinsic, Arg: (int)IntrinsicId.RawAllocate } });
        var call = make.Blocks[block].Terminator;
        make.Terminate(block, call with { Callee = call.Callee with { Arg = (int)IntrinsicId.DestroyValues } });
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Equal(["DropGlue string"], Targets(program, set, "Fn make [string]"));
        Assert.Empty(Targets(program, set, "Fn make [i32]"));
    }

    // K 4.8, IMPL 21.3.2, DECISIONS R5-M44: a by-value use whose Type passes a layout limit is GenerationResourceLimit_Kd at the Type's first
    // use: a parameter (at its Type occurrence, SPEC 8.15.3), a generic instance's intrinsic Type argument, a Closure local
    // whose environment holds the Type (its erasure then has no adapter item); a struct whose storage Binding rejected derives from Binding's
    // error and Option<Never> is laid out (X31), neither with a record. Subjects by their first line.
    [Theory]
    [InlineData("func f(value: [2147483648 of u8]) -> i32 => 1\n", "[2147483648 of u8]")]
    [InlineData("func f(value: [2147483648 of ()]) -> i32 => 1\n", "[2147483648 of ()]")]
    [InlineData("func make<T>() -> raw/T => Raw.allocate<T>(0)\nlet p = make<[2147483648 of u8]>()\nunsafe => Raw.release(p)\n", "Raw.allocate<T>(0)")]
    [InlineData(
        "func host(big: [1073741824 of i32]) -> i32\n    let f = func [big] () -> i32 => 1\n    let e: () -> i32 = f\n    return e()\n",
        "[1073741824 of i32]|let f = func [big]() -> i32 => 1")]
    [InlineData("struct A\n    let b: B\nstruct B\n    let a: A\nfunc f(a: A) -> i32 => 1\n", "")]
    [InlineData("let a: Option<Never> = .None\n", "")]
    public void ALayoutLimitIsARecordAtItsTypesFirstUse(string source, string subjects)
    {
        var c = MirBuildTest.Compile(source);
        var program = c.BuildMir();
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        var expected = subjects.Length == 0 ? [] : subjects.Split('|').Select(static x => (DiagnosticCode.GenerationResourceLimit_Kd, x)).ToArray();
        Assert.Equal(expected, set.Records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString().Split('\n')[0])));
        Assert.DoesNotContain(set.Items.ToArray(), static x => x.Key.Kind == MonoKind.ClosureErasure);
    }

    // DECISIONS R5-M44 (P7c's Q6, R5-M6): the depth bound counts placed levels (R5-M37) for a struct chain and a one-component Tuple chain,
    // read down to the scalar leaf, at 64 and 65 levels; the height-64 difference R5-M37 anticipated does not arise. Code generation, which
    // reads Layout's verdicts, agrees: it generates the 64-level program check accepts, and check rejects the 65-level one before it
    // (DECISIONS R5-M156; R5-M73's half on the old generator moved to MIR code generation at G4, R5-M160).
    [Theory]
    [InlineData("Struct", 64, false)]
    [InlineData("Struct", 65, true)]
    [InlineData("Tuple", 64, false)]
    [InlineData("Tuple", 65, true)]
    public void TheDepthLimitCountsPlacedLevels(string shape, int height, bool limited)
    {
        var source = new System.Text.StringBuilder();
        string type;
        if (shape == "Struct")
        {
            source.Append("struct S1\n    public let v: i32\n");
            for (var i = 2; i <= height; i++)
            {
                source.Append("struct S").Append(i).Append("\n    public let v: S").Append(i - 1).Append('\n');
            }

            type = "S" + height.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            type = new string('(', height) + "i32" + string.Concat(Enumerable.Repeat(",)", height));
        }

        var leaf = string.Concat(Enumerable.Repeat(shape == "Struct" ? ".v" : ".0", height));
        source.Append("func f(value: ").Append(type).Append(") -> i32 => value").Append(leaf).Append("\nConsole.writeLine(\"deep\")\n");
        var c = MirBuildTest.Compile(source.ToString());
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var set = new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1);
        var records = set.Records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString().Split('\n')[0]));
        Assert.Equal(limited ? [(DiagnosticCode.GenerationResourceLimit_Kd, type)] : [], records);
        if (!limited)
        {
            var generated = MinimalEmissionTest.Analyze(source.ToString());
            Assert.True(generated.Validate(out var error), error);
        }
    }

    // K 4.7, IMPL 21.3.3, DECISIONS R5-M53: Binding refuses a closed 128-bit division, remainder (compound forms included) or float
    // conversion at the node, which MIR builds as an Opaque value, so the definition-stage check (A3b) does not repeat its record; a direct
    // literal conversion is a constant and no record at all.
    [Theory]
    [InlineData("let x: i128 = 10\nlet y = x / 3\n", "x / 3")]
    [InlineData("var x: i128 = 10\nx /= 3\n", "x /= 3")]
    [InlineData("var x: u128 = 10\nx %= 3\n", "x %= 3")]
    [InlineData("let x: i128 = 1\nlet y = x@f64\n", "x@f64")]
    [InlineData("let x: f64 = 3.9\nlet y = x@i128\n", "x@i128")]
    [InlineData("let a = 170141183460469231731687303715884105727@f64\nlet c = 3.5e10@i128\n", null)]
    public void BindingsRefusalsAreNotRepeated(string source, string? refused)
    {
        var c = MirBuildTest.Compile(source);
        Assert.Equal(refused is null ? [] : [(DiagnosticCode.Unsupported_Kd, refused)], c.Binding.Issues.Select(static x => (x.Code, x.Node.ToString())));
        var program = c.BuildMir();
        Assert.Empty(program.Findings); // A program with findings is never collected, so the check would not run.
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.NotEqual(0, set.Items.Length);
        Assert.Empty(set.Records);
    }

    // A3b (R4-M8): once Binding's gate is gone (H7), a closed operation of the table is Unsupported_Kd at its own location, in any block,
    // an unreachable one included, and its supported twin is none; an operation whose Types depend on slots is left to the instance stage.
    // The tests append the operation, rebuilt from an addition the program has, in a new unreachable block of the top-level body.
    [Theory]
    [InlineData("let x: i128 = 10\nlet y = x + 3\n", "Div", true)]
    [InlineData("let x: u128 = 10\nlet y = x + 3\n", "Rem", true)]
    [InlineData("let x: i64 = 10\nlet y = x + 3\n", "Div", false)]
    [InlineData("let x: i128 = 10\nlet y = x + 3\n", "f64", true)]
    [InlineData("let x: f32 = 1.5\nlet y = x + 1.0\n", "i128", true)]
    [InlineData("let x: i64 = 10\nlet y = x + 3\n", "f64", false)]
    public void AClosedOperationOfTheTableIsAGapAtItsLocation(string source, string form, bool gap)
    {
        var c = MirBuildTest.Compile(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var program = c.BuildMir();
        var top = program.Bodies.ToArray().Single(static x => x.Key.Decl is null && x.Source.Module == 0);
        var add = top.Statements.ToArray().Single(static x => x.Rvalue is { Kind: MirRvalueKind.BinaryOp, Op: (byte)MirBinaryOp.Add });
        var rvalue = form is "Div" or "Rem"
            ? add.Rvalue with { Op = (byte)(form == "Div" ? MirBinaryOp.Div : MirBinaryOp.Rem) }
            : new MirRvalue(MirRvalueKind.Cast, (byte)MirCastKind.Numeric, Start: add.Rvalue.Start, Count: 1, Type: BoundType.Primitives[form]);
        var block = top.AddBlock();
        top.Push(block, add with { Rvalue = rvalue });
        top.Terminate(block, MirTerminator.Unreachable(add.Source));
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Equal(gap ? [(DiagnosticCode.Unsupported_Kd, add.Source.Syntax)] : [], set.Records.Select(static x => (x.Code, x.Subject)));
    }

    // K 4.7: an operation whose Types depend on slots is no definition-stage record; without an instance it has none.
    [Fact]
    public void AnOpenOperationIsNoDefinitionStageRecord()
    {
        var c = MirBuildTest.Compile("func half<T>(value: T) -> T\n    T is PrimitiveInteger\n    return value / 2\nfunc keep() -> i32 => 1\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var program = c.BuildMir();
        Assert.Empty(program.Findings); // A program with findings is never collected, so the check would not run.
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.NotEqual(0, set.Items.Length);
        Assert.Empty(set.Records);
    }

    // K 4.7 instance stage, IMPL 21.3.3, DECISIONS R5-M54: an operation whose Types depend on slots is checked in each instance of the
    // checked set; a division or remainder of an i128 or u128 instance is Unsupported_Kd at its own location in the generic body, once
    // however many instances or forwarding calls reach it, also after a return; the i64 instance has none; independent operations keep their
    // own locations.
    [Theory]
    [InlineData("    return value / 2\n", "let result = half(10@i128)\n", "value / 2")]
    [InlineData("    return value % 2\n", "let result = half(10@u128)\nlet other = half(20@i128)\n", "value % 2")]
    [InlineData("    return value / 2\n", "func forward<T>(value: T) -> T\n    T is PrimitiveInteger\n    return half(value)\nlet result = forward(10@i128)\n", "value / 2")]
    [InlineData("    return value\n    return value / 2\n", "let result = half(10@i128)\n", "value / 2")]
    [InlineData("    let first = value / 2\n    return value % 2\n", "let result = half(10@i128)\n", "value / 2|value % 2")]
    [InlineData("    return value / 2\n", "let result = half(10@i64)\n", "")]
    public void AnInstanceOperationOfTheTableIsAGapAtItsLocation(string body, string uses, string refused)
    {
        var c = MirBuildTest.Compile("func half<T>(value: T) -> T\n    T is PrimitiveInteger\n" + body + uses);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var set = new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1);
        var expected = refused.Length == 0 ? [] : refused.Split('|').Select(static x => (DiagnosticCode.Unsupported_Kd, x)).ToArray();
        Assert.Equal(expected, set.Records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString())));
    }

    // K 4.7 instance stage, IMPL 21.3.3 (DECISIONS R5-M54, R5-M126): an arithmetic shim's operation, its Contract's MIR operator
    // (BuiltinArithmetic) on its Self, is checked at each site that requests the shim, in Requirement's refusal decision: a division or
    // remainder of an i128, u128 or Wrapping<u128> Self is Unsupported_Kd at the requesting site (K 4.9), also when only an unused function
    // reaches it. Two sites requesting one key are two records, as two statements' divisions are: a requirement call and an operator; a
    // function item's formation and its call (SPEC 23.1.2 leaves a dependent failure's report implementation-defined); one site in two
    // instances is one (fail-before: once per item, at its first site). A refused edge admits no item, so a gap key has none; an addition
    // of i128 and a division of i64 have no record and their shim is admitted.
    [Theory]
    [InlineData(ShimQuotient + "func unused(a: ref/i128, b: ref/i128) -> i128 => quotient(a, b)\n", "left.divided(right)")]
    [InlineData(
        "func rest<T>(left: ref/T, right: ref/T) -> T\n    T is RemainderProvider<T>\n    T.(RemainderProvider<T>).Output is T\n    return left % right\n" +
        "let a: u128 = 7\nlet b: u128 = 2\nlet r = rest(a@ref, b@ref)\n",
        "left % right")]
    [InlineData(ShimQuotient + "let a: Wrapping<u128> = 7\nlet r = quotient(a@ref, a@ref)\n", "left.divided(right)")]
    [InlineData(ShimQuotient + ShimHalf + "func unused(a: ref/i128, b: ref/i128) -> i128 => quotient(a, b) + half(a, b)\n", "left.divided(right)|value / two")]
    [InlineData(ShimItem + "func unused(a: ref/i128, b: ref/i128) -> i128 => viaItem(a, b)\n", "T.divided|operation(left, right)")]
    [InlineData(
        ShimQuotient + ShimItem + "func unused(a: ref/i128, b: ref/i128) -> i128 => quotient(a, b) + viaItem(a, b)\n",
        "left.divided(right)|T.divided|operation(left, right)")]
    [InlineData(ShimTagged + "func unused(a: ref/i128, b: ref/i128) -> i128 => tagged(a, b, 1) + tagged(a, b, true)\n", "left.divided(right)")]
    [InlineData(
        "func third<T>(value: T) -> T\n    T is PrimitiveInteger\n    return value / 3\nfunc fifth<T>(value: T) -> T\n    T is PrimitiveInteger\n" +
        "    return value / 5\nfunc unused(a: i128) -> i128 => third(a) + fifth(a)\n",
        "value / 3|value / 5")]
    [InlineData(
        "func sum<T>(left: ref/T, right: ref/T) -> T\n    T is Addable<T>\n    T.(Addable<T>).Output is T\n    return left.added(right)\n" +
        "func unused(a: ref/i128, b: ref/i128) -> i128 => sum(a, b)\n",
        "")]
    [InlineData(ShimQuotient + ShimHalf + "func unused(a: ref/i64, b: ref/i64) -> i64 => quotient(a, b) + half(a, b)\n", "")]
    public void AnArithmeticShimsOperationOfTheTableIsAGapAtEachRequestingSite(string source, string refused)
    {
        var c = MirBuildTest.Compile(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var set = new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1);
        var expected = refused.Length == 0 ? [] : refused.Split('|').Select(static x => (DiagnosticCode.Unsupported_Kd, x)).Order().ToArray();
        Assert.Equal(expected, set.Records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString())).Order());
        var shims = set.Items.ToArray().Where(static x => x.Key.Kind == MonoKind.Builtin);
        Assert.Equal(refused.Length == 0, shims.Any(static x => x.Key.Symbol!.CompilerFunction == CompilerFunctionKind.BuiltinArithmetic));
    }

    // K 4.8, SPEC 6.7.1, 8.15.3, DECISIONS R5-M55: a by-value use whose Type places a C-layout instance storing a zero-sized Field, at any
    // depth, is InvalidCLayout_Kd at each such use (one record per subject): an inferred instance in a generic body (make(())) and at the
    // top level (P.init(1, ()), P.init(1, Q.init()) for a zero-sized struct Q), a written parameter of an unused function (at its Type
    // occurrence), nested in an Option and through an inferred Option<P<T>>, with the .None that makes it; each valid twin has none. The
    // instance's constructor is none: its construction receiver is storage the construction expression made (SPEC 6.3.2-6.3.4; DECISIONS
    // BU-48, superseding R5-M55's "with the instance's constructor"). Binding reports no instance, written or inferred (DECISIONS R4-M134
    // Q8). Subjects by their first line.
    [Theory]
    [InlineData("func make<T>(value: T) -> i32\n    let p = P<T>.init(1, value@move)\n    return p.n\nlet r = make(())\n", "let p = P<T>.init(1, value@move)")]
    [InlineData("func make<T>(value: T) -> i32\n    let p = P<T>.init(1, value@move)\n    return p.n\nlet r = make(5)\n", "")]
    [InlineData("let q = P.init(1, ())\n", "let q = P.init(1, ())")]
    [InlineData("let q = P.init(1, 2)\n", "")]
    [InlineData("struct Q\n    public init() => ()\nlet q = P.init(1, Q.init())\n", "let q = P.init(1, Q.init())")]
    [InlineData("struct Q\n    public var n: i32 = 1\n    public init() => ()\nlet q = P.init(1, Q.init())\n", "")]
    [InlineData("func f(p: P<()>) -> i32 => 1\n", "P<()>")]
    [InlineData("func f(p: P<i32>) -> i32 => 1\n", "")]
    [InlineData("let x: Option<P<()>> = .None\n", "let x: Option<P<()>> = .None|.None")]
    [InlineData("func g<T>(value: T) -> i32\n    let x: Option<P<T>> = .None\n    return 0\nlet r = g(())\n", "let x: Option<P<T>> = .None|.None")]
    [InlineData("func g<T>(value: T) -> i32\n    let x: Option<P<T>> = .None\n    return 0\nlet r = g(1)\n", "")]
    [InlineData("func g<T>(value: T) -> i32\n    let x: Option<ref/P<T>> = .None\n    return 0\nlet r = g(())\n", "")]
    public void AZeroSizedFieldOfACLayoutInstanceIsARecordAtEachUse(string uses, string subjects)
    {
        var c = MirBuildTest.Compile(CLayoutP + uses);
        Assert.DoesNotContain(c.Binding.Issues, static x => x.Code == DiagnosticCode.InvalidCLayout_Kd);
        var set = new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1);
        var expected = subjects.Length == 0 ? [] : subjects.Split('|').Select(static x => (DiagnosticCode.InvalidCLayout_Kd, x)).ToArray();
        Assert.Equal(expected, set.Records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString().Split('\n')[0])));
    }

    // IMPL 21.1.1, SPEC 8.15.3, DECISIONS R5-M52: a by-value use needing a layout over 2^63 - 1 bytes is OversizedLayout_Kd at each
    // use (one record per subject), not a Resource record: a parameter (at its Type occurrence), two functions' parameters, a generic instance's
    // parameter and each caller's uses, a generic instance's Raw.allocate Type argument, an erasure's target Function Type's inputs and
    // result (a Never source's, a body's beside its own result local, a compiler function Item's) and Raw.allocate's Type argument through
    // its Item, erased or called as a value (DECISIONS R5-M108); M, exactly at the bound, and a 2^31-byte array are one Resource record
    // each. Subjects by their first line, codes in record order.
    [Theory]
    [InlineData(Over + "\n", "OversizedLayout_Kd " + Huge)]
    [InlineData(Over + "\n" + Again + "\n", "OversizedLayout_Kd " + Huge + "|OversizedLayout_Kd " + Huge)]
    [InlineData("func pass<T>(value: T) => ()\n" + One + "\n" + Two + "\n", PassRecords)]
    [InlineData("func make<T>() -> raw/T => Raw.allocate<T>(0)\nlet p = make<(" + Big + ", u8)>()\nunsafe => Raw.release(p)\n", "OversizedLayout_Kd Raw.allocate<T>(0)")]
    [InlineData("func fail() -> Never => $abort(\"x\")\nfunc big() -> () -> " + Huge + " => fail\n", "OversizedLayout_Kd fail")]
    [InlineData(
        "func make() -> " + Huge + " => $abort(\"x\")\nfunc get() -> () -> " + Huge + " => make\n",
        "OversizedLayout_Kd " + Huge + "|OversizedLayout_Kd make")] // The result's record at its result Type (settled (a)).
    [InlineData("func swap() -> (uniq/" + Huge + ", " + Huge + ") -> " + Huge + " => Kimi.Intrinsics.exchange\n", "OversizedLayout_Kd Kimi.Intrinsics.exchange")]
    [InlineData("func huge() -> (isize) -> raw/" + Huge + " => Raw.allocate\n", "OversizedLayout_Kd Raw.allocate")]
    [InlineData(
        "func huge() -> raw/" + Huge + "\n    let f = Raw.allocate<" + Huge + ">\n    return f(1)\n",
        "OversizedLayout_Kd Raw.allocate<" + Huge + ">|OversizedLayout_Kd f(1)")]
    [InlineData("func at(value: " + Big + ") => ()\n", "GenerationResourceLimit_Kd " + Big)]
    [InlineData("func at(value: [2 of [1073741824 of u8]]) => ()\n", "GenerationResourceLimit_Kd [2 of [1073741824 of u8]]")]
    public void ALayoutOverTheBoundIsARecordAtEachUse(string source, string records)
    {
        var c = MirBuildTest.Compile(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var set = new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1);
        Assert.Equal(records, string.Join("|", set.Records.Select(x => $"{x.Code} {c.SyntaxOf(x.Subject).ToString().Split('\n')[0]}")));
    }

    // IMPL 21.1.1, SPEC 23.1.2 (G242; DECISIONS R5-M52, BU-37): an oversized Case beside a Case whose payload Binding did not bind is the
    // use's OversizedLayout_Kd, as in its repair: it rests on the oversized Case alone (fail-before: no record, the enum Rejected).
    [Theory]
    [InlineData("Missing")]
    [InlineData("i32")]
    public void AnOversizedCaseBesideAFailedOneIsARecord(string payload)
    {
        var c = MirBuildTest.Compile("enum E\n    A(" + payload + ")\n    B(" + Huge + ")\nfunc h(e: E) => ()\n");
        Assert.Equal(payload == "i32", c.Binding.Result.IsComplete);
        var set = new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1);
        Assert.Equal("OversizedLayout_Kd E", string.Join("|", set.Records.Select(x => $"{x.Code} {c.SyntaxOf(x.Subject).ToString().Split('\n')[0]}")));
    }

    // SPEC 8.15.3, DECISIONS R5-M52: a Type formed only behind a borrow or a raw pointer, or by a struct nothing uses by value, is no use.
    [Theory]
    [InlineData("func r(value: ref/(" + Big + ", u8)) => ()\n")]
    [InlineData("func p(value: raw/(" + Big + ", u8)) => ()\n")]
    [InlineData("struct Huge\n    var a: " + Big + "\n    var b: u8\n")]
    public void AnOversizedTypeBehindAnIndirectionIsNoUse(string source)
    {
        var c = MirBuildTest.Compile(source + "let x = 1\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Empty(new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1).Records);
    }

    // DECISIONS R5-M52 (KC6): one use of a Type that places a C-layout instance storing a zero-sized Field and needs a layout over
    // 2^63 - 1 bytes reports both Language errors there.
    [Fact]
    public void BothLanguageLayoutErrorsAtOneUse()
    {
        var c = MirBuildTest.Compile(CLayoutP + "func both(value: (P<()>, (" + Big + ", u8))) => ()\n");
        var records = new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1).Records;
        var use = "(P<()>, (" + Big + ", u8))";
        Assert.Equal(
            [(DiagnosticCode.OversizedLayout_Kd, use), (DiagnosticCode.InvalidCLayout_Kd, use)],
            records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString().Split('\n')[0])));
    }

    // K 4.3: a representative that leaves its body open is a broken contract, InternalInvariant_Kd at the call and no item. Binding never
    // gives such a call, so the test forges one: its substitution becomes the callee's own identity.
    [Fact]
    public void AnOpenRepresentativeIsABrokenContract()
    {
        var c = MirBuildTest.Compile("func pick<T>(value: T) -> T => value@move\nlet x = pick(1)\n");
        var program = c.BuildMir();
        var top = program.Bodies.ToArray().Single(static x => x.Key.Decl is null && x.Source.Module == 0);
        var block = Enumerable.Range(0, top.Blocks.Length)
            .Single(b => top.Blocks[b].Terminator is { Kind: MirTerminatorKind.Call, Callee: { Kind: MirCalleeKind.Fn, Symbol.Name: "pick" } });
        var call = top.Blocks[block].Terminator;
        top.Terminate(block, call with { Callee = call.Callee with { Subst = c.Types.Identity(call.Callee.Symbol!.Declaration) } });
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Equal([(DiagnosticCode.InternalInvariant_Kd, "pick(1)")], set.Records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString())));
        Assert.DoesNotContain(set.Items.ToArray(), x => Label(program, x).StartsWith("Fn pick", StringComparison.Ordinal));
    }

    // DECISIONS R5-M155 (PLAN G187's R5 half): in a complete compilation an abstract Type reaching Layout is a broken contract, a located
    // InternalInvariant_Kd at its use, never a fault. Binding and the builder never give a closed body such a local, so the test forges one:
    // main's `x` typed by probe's slot T.
    [Fact]
    public void AnAbstractLocalIsABrokenContractInACompleteCompilation()
    {
        var c = MirBuildTest.Compile("func probe<T>(value: T) => ()\npublic func main() -> ()\n    let x = 1\n    return\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var program = c.BuildMir();
        var main = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "main");
        var slot = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "probe").Parameters[0].Type.TypeOf()!;
        main.AddLocal(main.Locals.ToArray().Single(static x => x.Name == "x") with { Type = slot });
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Equal([(DiagnosticCode.InternalInvariant_Kd, "let x = 1")], set.Records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString())));
    }

    // X19: an item past depth 128 on its admission chain, and the 1,025th item of one definition, are GenerationResourceLimit_Kd at their calls.
    [Fact]
    public void TheDepthAndSetLimitsStopAtTheirCalls()
    {
        var chain = string.Concat(Enumerable.Range(0, 130).Select(static i => $"func step{i}<T>(value: T) -> T => {(i == 129 ? "value@move" : $"step{i + 1}<T>(value@move)")}\n"));
        var c = MirBuildTest.Compile(chain + "let x = step0<i32>(1)\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var set = new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1);
        Assert.Equal([(DiagnosticCode.GenerationResourceLimit_Kd, "step128<T>(value@move)")], set.Records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString())));
        Assert.Equal(128, set.Items.ToArray().Max(static x => x.Depth));

        var sizes = string.Concat(Enumerable.Range(1, 1025).Select(static n => $"_ = size<{n}>()\n"));
        c = MirBuildTest.Compile("func size<length N>() -> isize => N\n" + sizes);
        var program = c.BuildMir();
        set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Equal([(DiagnosticCode.GenerationResourceLimit_Kd, "size<1025>()")], set.Records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString())));
        Assert.Equal(1024, set.Items.ToArray().Count(x => Label(program, x).StartsWith("Fn size", StringComparison.Ordinal)));
    }

    // K 4.9 with a second, non-root module: a record caused in its inlined default moves to the root module's omitting call, and one caused
    // in its body to the root-module call that first admitted the chain. take's default, grow<u8>(1, 3), instantiates under the call, so
    // the builder copies it (DECISIONS R4-M127) with no record of its own, and its resource limit arises inside the copy, at its call in
    // main's Inlined scope at the library's location; a default its call does not instantiate is no copy, but the builder's Unsupported_Kd at
    // the call (MirBuildTest.ACopyTheCallDoesNotInstantiateIsAnOpaqueValue, this test's former probe<Array<…65…>> default).
    [Fact]
    public void TheLocationRuleLeavesNonRootModules()
    {
        var library = "public struct Box<T>\n    let value: T\n    public init(value: T) => self.value = value@move\npublic group Api\n" +
            "    public func take(x: i32, extra: i32 = grow<u8>(1, 3)) -> i32 => x + extra\n" +
            "    public func grow<T>(value: T, n: i32) -> i32 => if n == 0 => 0 else => grow<Box<T>>(Box<T>.init(value@move), n - 1)\n";
        var c = ModuleBindingTest.Create("public func main() -> ()\n    _ = Lib.Api.take(1)\n    _ = Lib.Api.grow<i32>(1, 3)\n    return\n", library);
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        var program = c.BuildMir();
        Assert.Empty(program.Records);
        var main = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn, Decl.Name: "main" });
        Assert.Contains(
            main.Blocks.ToArray(),
            x => x.Terminator is { Kind: MirTerminatorKind.Call, Callee.Symbol.Name: "grow", Source.Syntax.Module: 1 } &&
                main.Scopes[x.Terminator.Source.Scope].Kind == MirScopeKind.Inlined);
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Equal(
            [(DiagnosticCode.GenerationResourceLimit_Kd, "Lib.Api.take(1)"), (DiagnosticCode.GenerationResourceLimit_Kd, "Lib.Api.grow<i32>(1, 3)")],
            set.Records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString())));
        Assert.All(set.Records, static x => Assert.Equal(0, x.Subject.Module));
    }

    // K 4.6, the K package's 2x/4x counters: with twice and four times the item Types, the collector admits and replays twice and four times
    // as much.
    [Fact]
    public void TheWorkGrowsLinearlyWithTheInstances()
    {
        var work = new[] { 8, 16, 32 }.Select(static n =>
        {
            var c = MirBuildTest.Compile(Shape(n));
            var collector = new InstanceCollector(c.Types, c.Layout);
            var set = collector.Run(c.BuildMir(), 1);
            Assert.Empty(set.Records);
            return (Items: (double)set.Items.Length, Work: (double)collector.Work);
        }).ToArray();
        Assert.InRange(work[1].Items / work[0].Items, 1.8, 2.2);
        Assert.InRange(work[2].Items / work[0].Items, 3.6, 4.4);
        Assert.InRange(work[1].Work / work[0].Work, 1.8, 2.2);
        Assert.InRange(work[2].Work / work[0].Work, 3.6, 4.4);

        // N struct Types, each passed through a chain of three generic functions.
        static string Shape(int n) => "func first<T>(value: T) -> T => second(value@move)\nfunc second<T>(value: T) -> T => third(value@move)\n" +
            "func third<T>(value: T) -> T => value@move\n" + string.Concat(Enumerable.Range(0, n).Select(static i =>
                $"struct S{i}\n    public var value: i32\n    public init(value: i32) => self.value = value\nlet s{i} = first(S{i}.init({i}))\n"));
    }

    // K4b, K7: every milestone analyzed under MIR code generation has a checked set without a record, but Milestone 42 (P42's completion
    // program, which the old analysis rejects at the same accesses), whose statics of P42's storage (vars, Resource lets, the unused one
    // included, and the static of a generic container's group, X33 (a)) are the static-storage gap at their declarations (DECISIONS R5-M82).
    [Theory]
    [MemberData(nameof(HirColumnsTest.Corpus), MemberType = typeof(HirColumnsTest))]
    public void TheMilestonesHaveNoCollectorRecordUnderMirCodegen(string name)
    {
        if (!name.StartsWith("milestone/", StringComparison.Ordinal))
        {
            return;
        }

        string[] statics =
        [
            "public var count: i32 = Registry.initialCount()", "public let primary: Resource = Registry.makeResource(1)",
            "public var replaced: Resource = Registry.makeResource(2)", "public let unused: Resource = Resource.init(9)", "public var hits: i32 = 0",
        ];
        string[] expected = name != "milestone/Milestone42" ? [] : [.. statics.Select(static x => "Unsupported_Kd " + x)];

        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Main.kimi", File.ReadAllText(DiagnosticCorpus.RepositoryPath("tests", "milestones", name["milestone/".Length..] + ".kimi"))));
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Analyze();
        var set = c.Instances;
        Assert.NotNull(set);
        Assert.NotEqual(0, set.Items.Length);
        Assert.Equal(expected, set.Records.Select(x => $"{x.Code} {c.SyntaxOf(x.Subject)}"));
    }

    // A warm rebind rebuilds the MIR and collects it again with the same items and targets, instances included, and no allocation.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRunAllocatesNothing()
    {
        var c = SemanticSnapshotTest.Bind("milestone/Milestone30").Compilation;
        var collector = new InstanceCollector(c.Types, c.Layout);
        var set = collector.Run(c.BuildMir(), 1);
        var (items, targets) = (set.Items.Length, Enumerable.Range(0, set.Items.Length).Sum(x => set.Targets(x).Length));
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            collector.Run(c.BuildMir(), 1);
        }));
        Assert.True(set.Items.ToArray().Count(static x => x.Representative is not null) > 40);
        Assert.True(items > 50 && targets > 20, $"{items} items, {targets} targets");
        Assert.Equal((items, targets), (set.Items.Length, Enumerable.Range(0, set.Items.Length).Sum(x => set.Targets(x).Length)));
    }

    // A warm rebind collects a program's descriptors and tables again (VirtualGenericDispatchTest's warm closed tables) with no allocation.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRunOfTablesAllocatesNothing()
    {
        var c = MirBuildTest.Compile(
            VirtualGenericDispatchTest.Hierarchy + "let a = Leaf<i32>.init()@obj\nrequire a.nested() == 132 else => $abort(\"dispatch\")\n");
        var collector = new InstanceCollector(c.Types, c.Layout);
        var set = collector.Run(c.BuildMir(), 1);
        var tables = set.Items.ToArray().Count(static x => x.Key.Kind is MonoKind.ObjectDescriptor or MonoKind.VTable);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            collector.Run(c.BuildMir(), 1);
        }));
        Assert.Equal(2, tables);
        Assert.Equal(tables, set.Items.ToArray().Count(static x => x.Key.Kind is MonoKind.ObjectDescriptor or MonoKind.VTable));
    }

    // A warm rebind collects a program's Dictionary paths again (DECISIONS R5-M124): placeEntry's LegacyRow items and the append their rows
    // declare, a Dictionary from none's initialize and the clearEntries a glue's Drop row declares, with no allocation.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRunOfDictionariesAllocatesNothing()
    {
        var c = MirBuildTest.Compile(DictionaryKey + "struct Res\n    public let id: i32\n    public init(id: i32) => self.id = id\n    drop => ()\n" +
            "func put(d: uniq/Dictionary<Key, Res>)\n    _ = d.tryInsert(Key.init(1), Res.init(2))\nfunc make() -> Dictionary<Key, Res> => [:]\n" +
            "func keep(d: Dictionary<Key, Res>) => ()\nlet table: Dictionary<Key, i64> = [Key.init(1): 2]\n");
        var collector = new InstanceCollector(c.Types, c.Layout);
        var program = c.BuildMir();
        var set = collector.Run(program, 1);
        var reached = Reached();
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            c.Binding.Bind(BindingMode.Final);
            collector.Run(c.BuildMir(), 1);
        }));
        Assert.Equal(
            ["LegacyRow Dictionary<Key, Res> StoragePlaceDictionaryEntry", "LegacyRow Dictionary<Key, i64> StoragePlaceDictionaryEntry",
                "library Fn append in group DictionaryStorage", "library Fn clearEntries in group DictionaryStorage [Key, Res]",
                "library Fn initialize in group DictionaryStorage"],
            reached);
        c.Binding.Bind(BindingMode.Final);
        program = c.BuildMir();
        set = collector.Run(program, 1);
        Assert.Empty(set.Records);
        Assert.Equal(reached, Reached());

        // The items of the Dictionary paths, each once (G157 (30)'s duplicates are of the same function reached two ways, none here).
        string[] Reached() => set.Items.ToArray().Select(x => Label(program, x))
            .Where(static x => x.StartsWith("LegacyRow ", StringComparison.Ordinal) || x.StartsWith("library Fn append ", StringComparison.Ordinal) ||
                x.StartsWith("library Fn initialize ", StringComparison.Ordinal) || x.StartsWith("library Fn clearEntries ", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();
    }

    // S2-S4 X15 with DECISIONS R5-M74: one row per intrinsic, and only ErasedWriter, StaticEnsure, the creations and DestroyValues name a
    // target (a type test and a clone acquire nothing, SPEC 13.6.4).
    [Fact]
    public void OnlyTheWhitelistedIntrinsicsCarryATarget()
    {
        var targets = Enum.GetValues<IntrinsicId>().Where(static x => x != IntrinsicId.None && IntrinsicRows.Of(x).Target != IntrinsicTarget.None);
        Assert.Equal(
            [IntrinsicId.DestroyValues, IntrinsicId.ErasedWriter, IntrinsicId.MakeObj, IntrinsicId.MakeRc, IntrinsicId.MakeArc, IntrinsicId.StaticEnsure],
            targets);
        Assert.Throws<InvalidOperationException>(static () => IntrinsicRows.Of(IntrinsicId.None));
    }

    // SPEC 13.3.3 (DECISIONS R5-M126): the Backend's statement of the table (BuiltinArithmetic) and Binding's ArithmeticContracts.Operator,
    // composed with the MIR builder's operator for that token, agree: each Contract with a built-in witness on i32 has the MIR operation the
    // builder writes for its operator on i32 operands, one each; any other Contract, a comparison or a Left one, has none.
    [Fact]
    public void TheBuiltinArithmeticRowsAreTheBuildersOperators()
    {
        var contracts = Enum.GetValues<KimiDeclarationId>().Where(static x => ArithmeticContracts.Supports(BoundType.Primitives["i32"], x)).ToArray();
        Assert.Equal(6, contracts.Length);
        foreach (var contract in contracts)
        {
            var token = ArithmeticContracts.Operator(contract);
            var spelling = token switch
            {
                KotoKind.Plus => "+",
                KotoKind.Minus or KotoKind.PrefixMinus => "-",
                KotoKind.Asterisk => "*",
                KotoKind.Slash => "/",
                KotoKind.Percent => "%",
                _ => throw new InvalidOperationException($"{contract} has no operator."),
            };
            var source = token == KotoKind.PrefixMinus ? $"func f(a: i32) -> i32 => {spelling}a\n" : $"func f(a: i32, b: i32) -> i32 => a {spelling} b\n";
            var body = Assert.Single(MirBuildTest.Compile(source).BuildMir().Bodies.ToArray(), static x => x.Key.Decl?.Name == "f");
            var rvalue = Assert.Single(body.Statements.ToArray(), static x => x.Rvalue.Kind is MirRvalueKind.BinaryOp or MirRvalueKind.UnaryOp).Rvalue;
            Assert.Equal((rvalue.Kind, rvalue.Op), BuiltinArithmetic.Operation(contract));
        }

        Assert.Equal(contracts.Length, contracts.Select(static x => BuiltinArithmetic.Operation(x)).Distinct().Count());
        var others = Enum.GetValues<KimiDeclarationId>().Except(contracts);
        Assert.All(others, static x => Assert.Throws<InvalidOperationException>(() => BuiltinArithmetic.Operation(x)));
    }

    // R5-M39 (DECISIONS BU-24): a raw Origin enters no instance key, so variants of a generic callee that differ in raw Origins alone are
    // one item.
    [Fact]
    public void RawOriginVariantsAreOneInstance()
    {
        var c = MirBuildTest.Compile("func keep<T>(value: T) -> T\n    T is Copy\n    return value\n" +
            "func host(a: ref/i32, b: ref/i32, p: raw/i32 during a, q: raw/i32 during b, r: raw/i32) -> i32\n    _ = keep(p)\n    _ = keep(q)\n    _ = keep(r)\n    return 0\n" +
            "let x = 1\nlet y = 2\nrequire host(x@ref, y@ref, null, null, null) == 0 else => $abort(\"keep\")\n");
        var program = c.BuildMir();
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Empty(set.Records);
        Assert.Equal(["Fn keep [raw/i32]"], set.Items[set.RootCount..].ToArray().Select(x => Label(program, x)));
    }

    // K 4.3: a representative is closed and makes every local Type of its body closed.
    private static bool Closes(TyCtxt types, MirBody body, Subst representative)
        => representative.Types.ToArray().Append(representative.Declaring).Append(representative.Self).All(static x => x is not { ContainsParameter: true }) &&
            body.Locals.ToArray().All(x => types.Apply(x.Type, representative) is { ContainsParameter: false });

    private static (MirProgram Program, CheckedSet Set) Collect(string source)
    {
        var c = MirBuildTest.Compile(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        return (program, new InstanceCollector(c.Types, c.Layout).Run(program, 1));
    }

    private static int Item(MirProgram program, CheckedSet set, string label)
        => Enumerable.Range(0, set.Items.Length).Single(x => Label(program, set.Items[x]) == label);

    // A Type of `depth` nested wrappers around a leaf, as Box^3<i32> = Box<Box<Box<i32>>>.
    private static string Nest(string wrapper, int depth, string leaf)
        => string.Concat(Enumerable.Repeat(wrapper + "<", depth)) + leaf + new string('>', depth);

    // A ring of `count` structs S1, S2, ..., each storing the next through Option<rc/…>, the last S1.
    private static string Ring(int count)
        => string.Concat(Enumerable.Range(1, count).Select(k => $"struct S{k}\n    public let next: Option<rc/S{(k % count) + 1}>\n"));

    private static string[] Targets(MirProgram program, CheckedSet set, string label)
        => set.Targets(Item(program, set, label)).ToArray().Select(x => Label(program, set.Items[x.Item]) + (x.Runtime ? string.Empty : " (check)")).ToArray();

    // The targets at each call site of an item's body whose callee is a compiler function or a value, joined (empty: none).
    private static string[] AtCalls(MirProgram program, CheckedSet set, string label)
    {
        var item = Item(program, set, label);
        var body = program.Bodies[set.Items[item].BodyOrdinal];
        var sites = new List<string>();
        for (var b = 0; b < body.Blocks.Length; b++)
        {
            if (body.Blocks[b].Terminator is { Kind: MirTerminatorKind.Call } call &&
                (call.Callee.Kind == MirCalleeKind.Value || call.Callee.Symbol?.CompilerFunction is not (null or CompilerFunctionKind.None)))
            {
                var at = new MirLocation(b, body.Blocks[b].Count);
                sites.Add(string.Join(", ", set.Targets(item).ToArray().Where(x => x.At == at).Select(x => Label(program, set.Items[x.Item]))));
            }
        }

        return [.. sites];
    }

    // The targets of each item of a label: a function instance its call and its Item reach with erased substitutions of different input-Origin
    // counts has two LegacyRow items (DECISIONS R5-M121).
    private static string[][] RowTargets(MirProgram program, CheckedSet set, string label)
        => [.. Enumerable.Range(0, set.Items.Length).Where(x => Label(program, set.Items[x]) == label)
            .Select(x => set.Targets(x).ToArray().Select(y => Label(program, set.Items[y.Item])).ToArray())];

    // An item's targets but the glue of the values its body drops.
    private static string[] Calls(MirProgram program, CheckedSet set, string label)
        => Targets(program, set, label).Where(static x => !x.StartsWith("DropGlue ", StringComparison.Ordinal)).ToArray();

    // Whether an item's representative closes its body (Closes), a body item's being the one that has a body (BodyOrdinal; a LegacyRow item's
    // representative has none, DECISIONS R5-M121).
    private static bool Closed(Compilation c, MirProgram program, in MonoItem item)
        => item.Representative is not { } representative || item.BodyOrdinal < 0 || Closes(c.Types, program.Bodies[item.BodyOrdinal], representative);

    // A body's kind and definition, a closure by the named function enclosing it, a module's top-level statements as top, a library body with
    // the declaration it is in, an instance with its representative's declaring Type and Type arguments; a type-driven item's kind and Type.
    private static string Label(MirProgram program, in MonoItem item)
    {
        if (item.BodyOrdinal < 0)
        {
            var variant = item.Key.Kind switch
            {
                MonoKind.Builtin => " " + item.Key.Symbol!.CompilerFunction,
                MonoKind.PropertyWitness => " " + (PropertyWitnessKind)item.Key.Variant,
                MonoKind.LegacyRow => " " + item.Key.Body.Def.Decl!.CompilerFunction,
                _ => string.Empty,
            };
            return $"{item.Key.Kind} {TyCtxt.Name(item.RepresentativeType!)}{variant}";
        }

        var body = program.Bodies[item.BodyOrdinal];
        var name = body.Key.Decl?.Declaration switch
        {
            null => "top",
            FunctionKoto { IsAnonymous: true } closure => Enclosing(closure.Parent!),
            FunctionKoto function => function.Name,
            _ => body.Key.Decl!.Name, // A static's initializer.
        };
        var instance = item.Representative is { } subst
            ? " [" + string.Join(", ", (subst.Declaring is { } owner ? [owner] : Array.Empty<BoundType?>()).Concat(subst.Types.ToArray()).Select(static x => TyCtxt.Name(x!))) + "]"
            : string.Empty;
        return (body.Source.Module == 0 ? $"{body.Key.Kind} {name}" : $"library {body.Key.Kind} {name} in {body.Key.Decl!.Scope.Owner}") + instance;

        // The named function or accessor whose body holds a node (an accessor as its executing function is named, SPEC 11.3).
        static string Enclosing(Koto node) => node switch
        {
            FunctionKoto { IsAnonymous: false } named => named.Name,
            PropertyAccessorKoto accessor => ((PropertyKoto)accessor.Parent!).NameKoto.IdentifierName + "." + accessor.AccessorKind,
            _ => Enclosing(node.Parent!),
        };
    }

    // The parts the destruction walk's direct descents give: a destructor's struct and a handle are not descended (the destructor; a Sealed
    // payload), an enum's Case payloads each last to first, any other Type's descents last to first with a struct's base, the last, kept last.
    private static IEnumerable<BoundType> Expected(TyCtxt types, BoundType type, List<BoundType> descents)
    {
        if (AdtDef.Destructor(type) is not null || type.Kind == BoundTypeKind.Semantics)
        {
            Assert.Empty(descents);
            return AdtDef.Destructor(type) is not null ? [type] : types.Sealed(null, type.Components[0]) ? [type.Components[0]] : [];
        }

        if (AdtDef.IsEnum(type))
        {
            return descents.SelectMany(static x => x.Components.Reverse());
        }

        var fields = AdtDef.Base(type) is null ? descents.Count : descents.Count - 1;
        return descents.Take(fields).Reverse().Concat(descents.Skip(fields));
    }

    // The component a part's position names in the aggregate writer's numbering (FunctionCodegen.Component), or the part's own Type for a
    // part without one (Index -1).
    private static BoundType Component(BoundType type, DropPart part)
    {
        if (part.Index < 0)
        {
            return part.Type;
        }

        if (AdtDef.IsEnum(type))
        {
            return AdtDef.CaseTypes(type)![part.Ordinal]!.Components[part.Index];
        }

        if (!AdtDef.IsStruct(type))
        {
            return type.Components[part.Index];
        }

        var parent = AdtDef.Base(type);
        return parent is not null && part.Index == 0 ? parent : AdtDef.FieldType(type, parent is null ? part.Index : part.Index - 1)!;
    }

    // The destruction walk's direct descents below its root: each Type it enters after the root, none entered further.
    private struct Descents(List<BoundType> into) : IMirDestruction
    {
        private bool root;

        public bool First(BoundType type, int depth)
        {
            if (!this.root)
            {
                return this.root = true;
            }

            into.Add(type);
            return false;
        }

        public readonly bool Visit(BoundType type, int depth) => false;

        public readonly bool Deep(BoundType type) => false;
    }
}
