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
public sealed class InstanceCollectorTest
{
    // A generic function over a Copy T, and a generic struct over one, whose bodies the cases complete.
    private const string Outer = "func outer<T>(x: T) -> T\n    T is Copy\n";
    private const string CopyBox = "struct Box<T>\n    T is Copy\n    public var value: T\n    public init(value: T) => self.value = value\n";

    // The F3 reproducers' nested functions (X40): one level into a generic function, one there calling a selected specialization, two
    // levels in, in a block, in a generic struct's method and in a specialization inside a generic struct.
    private const string OneLevel = Outer + "    func middle(y: T) -> T => y\n    return middle(x)\n";

    private const string OneLevelSpecialized = "func describe<T>() -> i32 => 1\nspecialize func describe<i32>() -> i32 => 2\nfunc outer<T>(x: T) -> i32\n" +
        "    T is Copy\n    func inner() -> i32 => describe<T>()\n    return inner()\n";

    private const string TwoLevels = Outer + "    func middle(y: T) -> T\n        func inner(z: T) -> T => z\n        return inner(y)\n    return middle(x)\n";

    private const string InBlock = Outer + "    if true\n        func inner(z: T) -> T => z\n        return inner(x)\n    return x\n";

    private const string InMethod = CopyBox + "    public func get(self: ref/Self) -> T\n        func inner(z: T) -> T => z\n        return inner(self.value)\n" +
        Outer + "    return Box<T>.init(x).get()\n";

    private const string InSpecialization = CopyBox + "    public func f<U>(x: U) -> i32 => 1\n    specialize func f<i32>(x: i32) -> i32\n" +
        "        func inner(y: i32) -> i32 => y\n        return inner(2)\nfunc outer<T>(x: T) -> i32\n    T is Copy\n    return Box<T>.f(5)\n";

    // Generic functions whose bodies make Closures (one called, with a Closure in it; one never called), a function item constant and a
    // Callable parameter that is called.
    private const string ItemSource = "func apply<T>(x: T) -> T\n    T is Copy\n    let f = func [] (y: T) -> T\n        let g = func [] (z: T) -> T => z\n" +
        "        return g(y)\n    return f(x)\nfunc make<T>(x: T) -> T\n    T is Copy\n    let unused = func [] (y: T) -> T => y\n    return x\n" +
        "func pick<T>(value: T) -> T => value@move\nfunc one<T>() -> i32 => 1\nfunc run<F>(f: ref/F) -> i32\n    F is Callable<() -> i32>\n    return f()\n" +
        "let p = pick<i32>\n";

    // The first line of CLayoutP's constructor, the subject of its instance's by-value uses.
    private const string Init = "public init(n: i32, value: T)";

    // Exactly 2^63 - 1 bytes (649657 * 92737 * 153092023), every count within 2^31 - 1.
    private const string Big = "[649657 of [92737 of [153092023 of u8]]]";

    // Uses of a Type one byte over the layout bound: two functions' parameters, and two callers passing theirs to a generic function, whose
    // records are each caller's parameter and argument and the generic instance's parameter.
    private const string Over = "func over(value: (" + Big + ", u8)) => ()";
    private const string Again = "func again(value: (" + Big + ", u8)) => ()";
    private const string One = "func one(value: (" + Big + ", u8)) => pass(value@move)";
    private const string Two = "func two(value: (" + Big + ", u8)) => pass(value@move)";
    private const string PassRecords = "OversizedLayout_Kd " + One + "|OversizedLayout_Kd value@move|OversizedLayout_Kd " + Two +
        "|OversizedLayout_Kd value@move|OversizedLayout_Kd func pass<T>(value: T) => ()";

    // A C-layout struct over a Type parameter, which a zero-sized argument makes store a zero-sized Field.
    private const string CLayoutP = "#Layout(\"C\")\nstruct P<T>\n    public var n: i32\n    public var value: T\n" +
        "    public init(n: i32, value: T)\n        self.n = n\n        self.value = value@move\n";

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

    // A generic function nested in a closed function, which has no instance to carry.
    private const string InClosed = "func host() -> i32\n    func pick<U>(x: U) -> U => x@move\n    return pick(1)\n" + Outer + "    _ = host()\n    return x\n";

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
        { InClosed, "pick", 1, null },
    };

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

    // SPEC 6.1.3, 8.13.3, DECISIONS R5-M40 (the F3 reproducers, X40): an instance of a nested function holds its enclosing instance. A call
    // one level into a generic function carries it, so each instance of the enclosing function has its own item; a call two levels in,
    // from a block, in a generic struct's method or in a specialization inside a generic struct does not, so the edge is the
    // EnclosingInstance gap at the call until a representation of the enclosing chain carries it (DECISIONS R5-M43; then two items each).
    // Every admitted representative closes its body.
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
        Assert.All(set.Items.ToArray(), x => Assert.True(x.Representative is null || Closes(c.Types, program.Bodies[x.BodyOrdinal], x.Representative), Label(program, x)));
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
        Assert.All(
            set.Items.ToArray(),
            x => Assert.True(x.Representative is null || Closes(c.Types, program.Bodies[x.BodyOrdinal], x.Representative), Label(program, x)));
    }

    // K 4.5 row 13: an erasure of a Closure or function item to its Function Type is its ClosureErasure or ItemAdapter item, keyed by the
    // erased Type and represented by the first Type that requests it, whose target is the body the Closure or Item runs; a call of the
    // Function Type value has no target.
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
        Assert.Equal(
            ["Closure adder [i32]", "Fn pick [i32]"],
            adapters.SelectMany(x => set.Targets(x).ToArray()).Select(x => Label(program, set.Items[x.Item])).Order(StringComparer.Ordinal));
        var adder = set.Targets(Item(program, set, "Fn adder [i32]")).ToArray();
        Assert.DoesNotContain(adder, x => Label(program, set.Items[x.Item]).StartsWith("Fn pick", StringComparison.Ordinal));
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
    // requirement signatures agree with the key's; a set drops the Field's old value, so its item targets the Field Type's glue.
    [Fact]
    public void AStandardPropertyWitnessIsItsFieldsShim()
    {
        var c = MirBuildTest.Compile(HeldSource);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var program = c.BuildMir();
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Empty(set.Records);
        Assert.Equal(["PropertyWitness Bag StorageSet", "DropGlue Array<i32>", "PropertyWitness Bag StorageBorrow"], Targets(program, set, "Fn refill [Bag]"));
        Assert.Equal(["DropGlue Array<i32>"], Targets(program, set, "PropertyWitness Bag StorageSet"));
        Assert.Empty(Targets(program, set, "PropertyWitness Bag StorageBorrow"));
        Assert.All(set.Items.ToArray().Where(static x => x.Key.Kind == MonoKind.PropertyWitness), static x => Assert.Equal("items", x.Key.Symbol!.Name));
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
    // never gives one, so the test appends an erasure of an open function item to the top-level body in a new unreachable block.
    [Fact]
    public void AnOpenAdapterTypeIsABrokenContract()
    {
        var c = MirBuildTest.Compile("func pick<T>(value: T) -> T => value@move\nfunc hold<T>(value: T) -> i32\n    let item = pick<T>\n    return 1\nlet z = 1\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var program = c.BuildMir();
        var hold = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "hold");
        var open = hold.Consts.ToArray().Single(static x => x.Kind == MirConstKind.FnItem).Type;
        var top = program.Bodies.ToArray().Single(static x => x.Key.Decl is null && x.Source.Module == 0);
        var statement = top.Statements.ToArray().First(static x => x.Kind == MirStatementKind.Assign);
        var operand = top.AddOperand(MirOperand.Const(top.Const(new(MirConstKind.FnItem, open))));
        var block = top.AddBlock();
        top.Push(block, statement with { Rvalue = new(MirRvalueKind.Cast, (byte)MirCastKind.Erasure, Start: operand, Count: 1, Type: BoundType.Unit) });
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

    // K 4.5 "DropGlue" (X4): an Array's or Dictionary's glue targets the glues of its element Types that need a drop.
    [Fact]
    public void ACollectionsGlueDestroysItsElements()
    {
        var (program, set) = Collect("func use() -> ()\n    let names: Array<string> = []\n    let counts: Array<i32> = []\n" +
            "    let table: Dictionary<string, Array<i32>> = [:]\n    return\n");
        Assert.Equal(["DropGlue string"], Targets(program, set, "DropGlue Array<string>"));
        Assert.Empty(Targets(program, set, "DropGlue Array<i32>"));
        Assert.Equal(["DropGlue Array<i32>", "DropGlue string"], Targets(program, set, "DropGlue Dictionary<string, Array<i32>>"));
    }

    // K 4.8, S2-S4 X34: a collection's own layout is its handle words, but its glue lays out its element Types, also those that need no drop,
    // whose verdicts join the glue item's: a C-layout element storing a zero-sized Field is InvalidCLayout_Kd at each request of the glue,
    // an empty local's declaration (also for an inferred instance, which Binding does not report), and a limit element the erased Type's one
    // Resource record; such a glue has no item. Records by code and the subject's first line, in record order.
    [Theory]
    [InlineData(
        "func use() -> ()\n    let a: Array<P<()>> = []\n    let b: Array<P<()>> = []\n    return\n",
        2,
        "InvalidCLayout_Kd let b: Array<P<()>> = []|InvalidCLayout_Kd let a: Array<P<()>> = []")]
    [InlineData("func g<T>(value: T) -> i32\n    let a: Array<P<T>> = []\n    return 0\nlet r = g(())\n", 0, "InvalidCLayout_Kd let a: Array<P<T>> = []")]
    [InlineData("func g<T>(value: T) -> i32\n    let a: Array<P<T>> = []\n    return 0\nlet r = g(1)\n", 0, "")]
    [InlineData(
        "func use() -> ()\n    let a: Array<[2147483648 of u8]> = []\n    let b: Array<[2147483648 of u8]> = []\n    return\n",
        0,
        "GenerationResourceLimit_Kd let b: Array<[2147483648 of u8]> = []")]
    public void ACollectionsGlueLaysOutItsElements(string uses, int eager, string records)
    {
        var c = MirBuildTest.Compile(CLayoutP + uses);
        Assert.Equal(eager, c.Binding.Issues.Count(static x => x.Code == DiagnosticCode.InvalidCLayout_Kd));
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

    // F10's contract: the builder ensures only the statics whose initializers it builds, so a staticEnsure that names a static without one is
    // a broken contract, InternalInvariant_Kd at the access and no target. Binding never gives one, so the test points the ensure at a
    // function's symbol.
    [Fact]
    public void AnEnsureWithoutAnInitializerIsABrokenContract()
    {
        var c = MirBuildTest.Compile("group Registry\n    public let total: i32 = 5\nfunc plain() -> i32 => 1\nfunc use() -> i32 => Registry.total\n");
        var program = c.BuildMir();
        var use = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "use");
        var plain = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "plain").Key.Decl!;
        var block = Enumerable.Range(0, use.Blocks.Length).Single(b => use.Blocks[b].Terminator is
            { Kind: MirTerminatorKind.Call, Callee: { Kind: MirCalleeKind.Intrinsic, Arg: (int)IntrinsicId.StaticEnsure } });
        var root = use.Static(new(plain, BoundType.Unit, false, false)).Root;
        var operand = use.AddOperand(MirOperand.Const(use.Const(new(MirConstKind.Integer, BoundType.ISize, ~root))));
        use.Terminate(block, use.Blocks[block].Terminator with { Start = operand });
        var set = new InstanceCollector(c.Types, c.Layout).Run(program, 1);
        Assert.Equal([(DiagnosticCode.InternalInvariant_Kd, "Registry.total")], set.Records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString())));
        Assert.Empty(Targets(program, set, "Fn use"));
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
    // use: a parameter (at its function, as MIR records no parameter syntax), a generic instance's intrinsic Type argument, a Closure local
    // whose environment holds the Type (its erasure then has no adapter item); a struct whose storage Binding rejected derives from Binding's
    // error and Option<Never> is laid out (X31), neither with a record. Subjects by their first line.
    [Theory]
    [InlineData("func f(value: [2147483648 of u8]) -> i32 => 1\n", "func f(value: [2147483648 of u8]) -> i32 => 1")]
    [InlineData("func f(value: [2147483648 of ()]) -> i32 => 1\n", "func f(value: [2147483648 of ()]) -> i32 => 1")]
    [InlineData("func make<T>() -> raw/T => Raw.allocate<T>(0)\nlet p = make<[2147483648 of u8]>()\nunsafe => Raw.release(p)\n", "Raw.allocate<T>(0)")]
    [InlineData(
        "func host(big: [1073741824 of i32]) -> i32\n    let f = func [big] () -> i32 => 1\n    let e: () -> i32 = f\n    return e()\n",
        "func host(big: [1073741824 of i32]) -> i32|let f = func [big]() -> i32 => 1")]
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

    // DECISIONS R5-M44 (P7c's Q6, R5-M6): the depth bound counts placed levels (R5-M37), and the old generator, whose pool takes Layout's
    // verdict, agrees for a struct chain and a one-component Tuple chain, read down to the scalar leaf, at 64 and 65 levels; the height-64
    // difference R5-M37 anticipated does not arise. The old generator is selected whatever the process's code generator (R5-M73).
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
        Assert.Equal(limited ? [(DiagnosticCode.GenerationResourceLimit_Kd, $"func f(value: {type}) -> i32 => value{leaf}")] : [], records);
        var legacy = MinimalEmissionTest.Analyze(source.ToString(), codegen: CompilerCodegen.Legacy);
        Assert.Equal(!limited, legacy.Validate(out var error));
        Assert.True(limited == (error is { } text && text.Contains("generation depth limit of 64 levels", StringComparison.Ordinal)), error);
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

    // K 4.8, SPEC 6.7.1, 8.15.3, DECISIONS R5-M55: a by-value use whose Type places a C-layout instance storing a zero-sized Field, at any
    // depth, is InvalidCLayout_Kd at each such use (one record per subject): an inferred instance in a generic body (make(())) and at the
    // top level (P.init(1, ()), P.init(1, Q.init()) for a zero-sized struct Q), with the instance's constructor, a written parameter of an
    // unused function, nested in an Option and through an inferred Option<P<T>>, with the .None that makes it; each valid twin has none.
    // Binding reports exactly a written instance's eager InvalidCLayout_Kd, so an inferred one is Binding-clean. Subjects by their first line.
    [Theory]
    [InlineData(
        "func make<T>(value: T) -> i32\n    let p = P<T>.init(1, value@move)\n    return p.n\nlet r = make(())\n",
        0,
        "let p = P<T>.init(1, value@move)|" + Init)]
    [InlineData("func make<T>(value: T) -> i32\n    let p = P<T>.init(1, value@move)\n    return p.n\nlet r = make(5)\n", 0, "")]
    [InlineData("let q = P.init(1, ())\n", 0, "let q = P.init(1, ())|" + Init)]
    [InlineData("let q = P.init(1, 2)\n", 0, "")]
    [InlineData("struct Q\n    public init() => ()\nlet q = P.init(1, Q.init())\n", 0, "let q = P.init(1, Q.init())|" + Init)]
    [InlineData("struct Q\n    public var n: i32 = 1\n    public init() => ()\nlet q = P.init(1, Q.init())\n", 0, "")]
    [InlineData("func f(p: P<()>) -> i32 => 1\n", 1, "func f(p: P<()>) -> i32 => 1")]
    [InlineData("func f(p: P<i32>) -> i32 => 1\n", 0, "")]
    [InlineData("let x: Option<P<()>> = .None\n", 1, "let x: Option<P<()>> = .None|.None")]
    [InlineData("func g<T>(value: T) -> i32\n    let x: Option<P<T>> = .None\n    return 0\nlet r = g(())\n", 0, "let x: Option<P<T>> = .None|.None")]
    [InlineData("func g<T>(value: T) -> i32\n    let x: Option<P<T>> = .None\n    return 0\nlet r = g(1)\n", 0, "")]
    [InlineData("func g<T>(value: T) -> i32\n    let x: Option<ref/P<T>> = .None\n    return 0\nlet r = g(())\n", 0, "")]
    public void AZeroSizedFieldOfACLayoutInstanceIsARecordAtEachUse(string uses, int eager, string subjects)
    {
        var c = MirBuildTest.Compile(CLayoutP + uses);
        Assert.Equal(Enumerable.Repeat(DiagnosticCode.InvalidCLayout_Kd, eager), c.Binding.Issues.Select(static x => x.Code));
        var set = new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1);
        var expected = subjects.Length == 0 ? [] : subjects.Split('|').Select(static x => (DiagnosticCode.InvalidCLayout_Kd, x)).ToArray();
        Assert.Equal(expected, set.Records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString().Split('\n')[0])));
    }

    // DECISIONS R5-M55: a C-layout declaration Binding rejects for a direct zero-sized Field is rejected storage for Layout, derived from
    // Binding's records (at the Field and the struct), so its by-value uses add no collector record.
    [Fact]
    public void ARejectedCLayoutDeclarationIsBindingsAlone()
    {
        var c = MirBuildTest.Compile("#Layout(\"C\")\nstruct Q\n    public var n: i32 = 1\n    public var u: () = ()\n    public init() => ()\n" +
            "func use(q: Q) -> i32 => q.n\nlet q = Q.init()\nlet n = use(q@move)\n");
        Assert.Equal(
            [(DiagnosticCode.InvalidTypeFormation_Kd, true), (DiagnosticCode.InvalidCLayout_Kd, false)],
            c.Binding.Issues.Select(static x => (x.Code, x.Node is StructKoto)).OrderBy(static x => x.Code));
        Assert.Empty(new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1).Records);
    }

    // DECISIONS R5-M55 (a declared overlap, removed at G2b): Binding still reports a written instance eagerly at the written Type, so under
    // MIR code generation a written by-value use is also reported at the use; an inferred one only at its uses.
    [Theory]
    [InlineData("func f(p: P<()>) -> i32 => 1\n", 1, "func f(p: P<()>) -> i32 => 1")]
    [InlineData("let q = P.init(1, ())\n", 0, "let q = P.init(1, ())")]
    public void BindingsEagerReportOfAWrittenInstanceStaysBesideTheUse(string uses, int eager, string use)
    {
        var c = MirBuildTest.Compile(CLayoutP + uses);
        Assert.Equal(eager, c.Binding.Issues.Count(static x => x.Code == DiagnosticCode.InvalidCLayout_Kd));
        var records = new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1).Records;
        Assert.Contains((DiagnosticCode.InvalidCLayout_Kd, use), records.Select(x => (x.Code, c.SyntaxOf(x.Subject).ToString().Split('\n')[0])));
    }

    // IMPL 21.1.1, SPEC 8.15.3, DECISIONS R5-M52: a by-value use needing a layout over 2^63 - 1 bytes is OversizedLayout_Kd at each
    // use (one record per subject), not a Resource record: a parameter (at its function), two functions' parameters, a generic instance's
    // parameter and each caller's uses, a generic instance's Raw.allocate Type argument; M, exactly at the bound, and a 2^31-byte array are
    // one Resource record each. Subjects by their first line, codes in record order.
    [Theory]
    [InlineData(Over + "\n", "OversizedLayout_Kd " + Over)]
    [InlineData(Over + "\n" + Again + "\n", "OversizedLayout_Kd " + Over + "|OversizedLayout_Kd " + Again)]
    [InlineData("func pass<T>(value: T) => ()\n" + One + "\n" + Two + "\n", PassRecords)]
    [InlineData("func make<T>() -> raw/T => Raw.allocate<T>(0)\nlet p = make<(" + Big + ", u8)>()\nunsafe => Raw.release(p)\n", "OversizedLayout_Kd Raw.allocate<T>(0)")]
    [InlineData("func at(value: " + Big + ") => ()\n", "GenerationResourceLimit_Kd func at(value: " + Big + ") => ()")]
    [InlineData("func at(value: [2 of [1073741824 of u8]]) => ()\n", "GenerationResourceLimit_Kd func at(value: [2 of [1073741824 of u8]]) => ()")]
    public void ALayoutOverTheBoundIsARecordAtEachUse(string source, string records)
    {
        var c = MirBuildTest.Compile(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var set = new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1);
        Assert.Equal(records, string.Join("|", set.Records.Select(x => $"{x.Code} {c.SyntaxOf(x.Subject).ToString().Split('\n')[0]}")));
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
        var use = "func both(value: (P<()>, (" + Big + ", u8))) => ()";
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
    // in its body to the root-module call that first admitted the chain.
    [Fact]
    public void TheLocationRuleLeavesNonRootModules()
    {
        var deep = string.Concat(Enumerable.Repeat("Array<", 65)) + "i32" + new string('>', 65);
        var library = "public struct Box<T>\n    let value: T\n    public init(value: T) => self.value = value@move\npublic group Api\n" +
            $"    public func probe<T>(value: T) -> i32 => 0\n    public func take(x: i32, extra: i32 = probe<{deep}>([])) -> i32 => x + extra\n" +
            "    public func grow<T>(value: T, n: i32) -> i32 => if n == 0 => 0 else => grow<Box<T>>(Box<T>.init(value@move), n - 1)\n";
        var c = ModuleBindingTest.Create("public func main() -> ()\n    _ = Lib.Api.take(1)\n    _ = Lib.Api.grow<i32>(1, 3)\n    return\n", library);
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        var set = new InstanceCollector(c.Types, c.Layout).Run(c.BuildMir(), 1);
        Assert.Equal(
            [(DiagnosticCode.Unsupported_Kd, "Lib.Api.take(1)"), (DiagnosticCode.GenerationResourceLimit_Kd, "Lib.Api.grow<i32>(1, 3)")],
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
        c.Pipeline = CompilerPipeline.Mir;
        c.Codegen = CompilerCodegen.Mir;
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

    // An item's targets but the glue of the values its body drops.
    private static string[] Calls(MirProgram program, CheckedSet set, string label)
        => Targets(program, set, label).Where(static x => !x.StartsWith("DropGlue ", StringComparison.Ordinal)).ToArray();

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
                _ => string.Empty,
            };
            return $"{item.Key.Kind} {TyCtxt.Name(item.RepresentativeType!)}{variant}";
        }

        var body = program.Bodies[item.BodyOrdinal];
        var name = body.Key.Decl?.Declaration switch
        {
            null => "top",
            FunctionKoto { IsAnonymous: true } closure => Enclosing(closure.Parent!).Name,
            FunctionKoto function => function.Name,
            _ => body.Key.Decl!.Name, // A static's initializer.
        };
        var instance = item.Representative is { } subst
            ? " [" + string.Join(", ", (subst.Declaring is { } owner ? [owner] : Array.Empty<BoundType?>()).Concat(subst.Types.ToArray()).Select(static x => TyCtxt.Name(x!))) + "]"
            : string.Empty;
        return (body.Source.Module == 0 ? $"{body.Key.Kind} {name}" : $"library {body.Key.Kind} {name} in {body.Key.Decl!.Scope.Owner}") + instance;

        static FunctionKoto Enclosing(Koto node) => node is FunctionKoto { IsAnonymous: false } named ? named : Enclosing(node.Parent!);
    }
}
