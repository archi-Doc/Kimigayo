// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// The object descriptors and handle operations of MIR code generation (R5 C6a; IMPL 21.1.3, SPEC 3.3.1, 13.6.1-13.6.4, 22.5.1; DECISIONS
// R5-M74, R5-M110): a descriptor per erased payload and its factory, the module's numbering of runtime identities, creations, clones, type
// tests, upcasts and Derefs of handles and object borrows, the factory and clone Items, and the object runtime once per module.
public sealed partial class FunctionCodegenTest
{
    // A Library whose functions create, clone, test and read objects without destroying a handle (a handle's glue is C6b's): a payload
    // holding a string made by each handle, an i32 and a zero-sized one, a derived struct whose base no creation makes and a sibling nothing
    // creates, clones of rc and arc, tests against each, a Field read and an upcast through object borrows, and the factory and clone Items
    // erased.
    private const string Objects =
        "struct Named\n    public let name: string\n    public let id: i32\n    public init(name: string, id: i32)\n" +
        "        self.name = name@move\n        self.id = id\nstruct Empty\n    public init() => ()\n" +
        "open struct Base\n    public let legs: i32 = 4\n    protected init() => ()\nstruct Derived : Base\n    public let bark: i32 = 3\n" +
        "    public init() : base() => ()\nstruct Other : Base\n    public init() : base() => ()\n" +
        "func exclusive(n: string) -> obj/Named => Kimi.Intrinsics.makeObj(Named.init(n@move, 1))\n" +
        "func shared(n: string) -> rc/Named => Kimi.Intrinsics.makeRc(Named.init(n@move, 2))\n" +
        "func atomic(n: string) -> arc/Named => Kimi.Intrinsics.makeArc(Named.init(n@move, 3))\n" +
        "func number() -> obj/i32 => Kimi.Intrinsics.makeObj(7)\nfunc empty() -> rc/Empty => Kimi.Intrinsics.makeRc(Empty.init())\n" +
        "func derived() -> obj/Derived => Kimi.Intrinsics.makeObj(Derived.init())\n" +
        "func again(h: ref/(rc/Named)) -> rc/Named => Kimi.Intrinsics.clone(h)\n" +
        "func atomicAgain(h: ref/(arc/Named)) -> arc/Named => Kimi.Intrinsics.clone(h)\n" +
        "func isDerived(v: objref/Base) -> bool => v is Derived\nfunc isBase(v: objref/Base) -> bool => v is Base\n" +
        "func isOther(v: objref/Base) -> bool => v is Other\nfunc notDerived(v: objref/Base) -> bool => v is not Derived\n" +
        "func read(v: objref/Named) -> i32 => v.id\nfunc legs(v: objref/Derived) -> i32\n    let b = v@objref/Base\n    return b.legs\n" +
        "func erased() -> (i32) -> arc/i32 => Kimi.Intrinsics.makeArc\nfunc cloner() -> (ref/(rc/i32)) -> rc/i32 => Kimi.Intrinsics.clone\n";

    // SPEC 13.6 (DECISIONS R5-M110): objects made by each handle and by a factory Item, read through handles and borrows, cloned, upcast
    // as borrows and tested, natively under both generators; the program ends in an Abort, so it destroys no handle (C6b's glue).
    private const string ObjectRows =
        "struct Cell\n    public let value: i32\n    public init(value: i32) => self.value = value\n" +
        "open struct Base\n    public let legs: i32 = 4\n    protected init() => ()\nstruct Leaf : Base\n    public let id: i32 = 5\n" +
        "    public init() : base() => ()\nstruct Other : Base\n    public init() : base() => ()\n" +
        "let a = Kimi.Intrinsics.makeObj(Cell.init(1))\nlet r = Kimi.Intrinsics.makeRc(Cell.init(2))\nlet s = Kimi.Intrinsics.clone(r@ref)\n" +
        "let t = Kimi.Intrinsics.makeArc(Cell.init(3))\nlet u = Kimi.Intrinsics.clone(t@ref)\nlet leaf = Kimi.Intrinsics.makeObj(Leaf.init())\n" +
        "let view = leaf@objref\nlet root = view@objref/Base\nlet make = Kimi.Intrinsics.makeObj<i32>\nlet n = make(4)\n" +
        "require a.value + r.value + s.value + t.value + u.value + n@follow == 15 else => $abort(\"values\")\n" +
        "require root.legs == 4 and view.id == 5 and root is Leaf and root is Base and not (root is Other) else => $abort(\"tests\")\n" +
        "Console.writeLine(\"objects\")\n$abort(\"end\")\n";

    [Fact]
    public void ObjectProgramsRun()
        => ScalarEmissionTest.EmitFixture("ObjectRows", ObjectRows, "objects\n", 1, "Hello.kimi:25:1: abort KIMI_E_ABORT: end\n");

    // IMPL 21.1.3, SHARED_OBJECT_RUNTIME (DECISIONS R5-M110): one descriptor per erased payload whatever the handle, its metadata the
    // payload's identity, layout, Copy and destroy entry, which calls the payload's glue; one factory serving obj, rc and arc, called with
    // the result, the payload at its kind (none when zero-sized), the static control of the handle (0, 2, 2) and the creation's location;
    // an erased factory Item's entry calls it with its received location.
    [Fact]
    public void ADescriptorIsOnePerErasedPayloadAndItsFactoryTakesTheHandlesControl()
    {
        var c = Analyze(Objects, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var factories = new[] { ("exclusive", 0), ("shared", 2), ("atomic", 2) }.Select(x =>
        {
            var (create, factory) = Creation(ir, c, x.Item1);
            Assert.Matches($@"^call void @\(ptr %, ptr %, i64 {x.Item2}, ptr @, i64 \d+\)$", create.Shape);
            return factory;
        }).ToArray();
        Assert.All(factories, x => Assert.Equal(factories[0], x));
        Assert.Equal("internal void (ptr, ptr, i64, ptr, i64)", ir.Function(factories[0]).Signature);
        Assert.Equal(4, Regex.Matches(text, @"(?m)^@__kimi_object_metadata\d+ = ").Count);
        Assert.Matches(@"^\{ i64 \d+, i64 32, i64 8, i64 0, ptr @__kimi_object_destroy_values\d+, ptr null \}$", Metadata(text, factories[0]));
        var destroy = Regex.Match(text, $@"(?s)define internal void @__kimi_object_destroy_values{Number(factories[0])}\(.*?call void @(\S+)\(ptr %value");
        Assert.Equal("internal void (ptr, ptr, i64)", ir.Function(destroy.Groups[1].Value).Signature);

        var (number, integer) = Creation(ir, c, "number");
        Assert.Matches(@"^call void @\(ptr %, i32 7, i64 0, ptr @, i64 \d+\)$", number.Shape);
        Assert.Matches(@"^\{ i64 \d+, i64 4, i64 4, i64 1, ptr null, ptr null \}$", Metadata(text, integer));
        var (empty, zero) = Creation(ir, c, "empty");
        Assert.Equal("internal void (ptr, i64, ptr, i64)", ir.Function(zero).Signature);
        Assert.Matches(@"^call void @\(ptr %, i64 2, ptr @, i64 \d+\)$", empty.Shape);
        Assert.Matches(@"^\{ i64 \d+, i64 0, i64 1, i64 0, ptr null, ptr null \}$", Metadata(text, zero));

        var entry = ir.Function(TableOf(text, Assert.Single(ItemTexts(c, "erased"))).Entry);
        var erased = Assert.Single(CallsOf(entry));
        Assert.Same(ir.Function(integer), erased.Callee);
        Assert.Equal("call void @(ptr %, i32 %, i64 2, ptr %, i64 %)", erased.Shape);
        Assert.Equal([entry.Parameters[1], entry.Parameters[2], entry.Parameters[4], entry.Parameters[5]], erased.Operands);
    }

    // SPEC 13.6.4, 3.3.1 (DECISIONS R5-M74, R5-M110): the module numbers the erased payloads of its descriptors and their chains' bases, a
    // base no creation makes included, so a test calls the runtime's test of the borrow's object (the payload address less the header) with
    // its target's number, a derived descriptor's base map holding its base's; a target on no chain is false without a call; `is not`
    // negates the test.
    [Fact]
    public void ATypeTestComparesTheModulesNumbersOfTheDescriptorsChains()
    {
        var c = Analyze(Objects, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var supports = ir.Function("__kimi_object_supports");
        var factory = Creation(ir, c, "derived").Factory;
        var derived = Regex.Match(Metadata(text, factory), @"^\{ i64 (\d+),").Groups[1].Value;
        var views = Regex.Match(text, $@"(?m)^@__kimi_object_views{Number(factory)} = private constant \[3 x i64\] \[i64 1, i64 (\d+), i64 0\], align 8$");
        Assert.True(views.Success);
        var @base = views.Groups[1].Value;
        Assert.NotEqual(derived, @base);
        Assert.DoesNotMatch($@"(?m)^@__kimi_object_metadata\d+ = private constant {{ i64, i64, i64, i64, ptr, ptr }} {{ i64 {@base},", text);
        foreach (var (name, token) in new[] { ("isDerived", derived), ("isBase", @base), ("notDerived", derived) })
        {
            var function = Assert.Single(FunctionsOf(ir, c, name));
            var test = Assert.Single(CallsOf(function));
            Assert.Same(supports, test.Callee);
            Assert.Equal($"call i1 @(ptr %, i64 {token})", test.Shape);
            Assert.Equal("getelementptr i8, ptr %, i64 -16", test.Operands[0].Shape);
            Assert.Equal(name == "notDerived", function.Instructions.Any(static x => x.Shape == "xor i1 %, -1"));
        }

        var other = Assert.Single(FunctionsOf(ir, c, "isOther"));
        Assert.Empty(CallsOf(other));
        Assert.Contains(other.Instructions, static x => x.Shape == "zext i1 0 to i8");
    }

    // SPEC 13.6.2 (DECISIONS R5-M110): a clone calls the runtime's retain of its handle's counting with the borrowed handle slot, the result
    // and the clone's location, and allocates nothing; an erased clone Item's entry calls it with its own parameters.
    [Fact]
    public void ACloneCallsItsHandlesRetainAndAllocatesNothing()
    {
        var c = Analyze(Objects, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        foreach (var (name, retain) in new[] { ("again", "__kimi_clone_rc"), ("atomicAgain", "__kimi_clone_arc") })
        {
            var function = Assert.Single(FunctionsOf(ir, c, name));
            var clone = Assert.Single(CallsOf(function));
            Assert.Same(ir.Function(retain), clone.Callee);
            Assert.Matches(@"^call void @\(ptr %, ptr %, ptr @, i64 \d+\)$", clone.Shape);
            Assert.DoesNotContain(ir.Function("__kimi_alloc"), CalleesOf(function));
        }

        var entry = ir.Function(TableOf(text, Assert.Single(ItemTexts(c, "cloner"))).Entry);
        var call = Assert.Single(CallsOf(entry));
        Assert.Same(ir.Function("__kimi_clone_rc"), call.Callee);
        Assert.Equal([entry.Parameters[1], entry.Parameters[2], entry.Parameters[4], entry.Parameters[5]], call.Operands);
    }

    // IMPL 21.1.3, SPEC 13.6.1 (DECISIONS R5-M110): an object borrow is its payload's address, so a Field through it is at the Field's
    // offset from the stored pointer and an upcast keeps the pointer; an owning handle stores its object's header, its payload 16 bytes on.
    [Fact]
    public void AnObjectBorrowIsItsPayloadsAddressAndAHandleItsHeader()
    {
        var c = Analyze(Objects, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var read = Assert.Single(FunctionsOf(ir, c, "read"));
        var id = Assert.Single(read.Instructions, static x => x.Opcode == "getelementptr");
        Assert.Equal("getelementptr i8, ptr %, i64 24", id.Shape);
        Assert.Equal("load ptr, ptr %, align 8", id.Operands[0].Shape);
        var legs = Assert.Single(FunctionsOf(ir, c, "legs"));
        Assert.DoesNotContain(legs.Instructions, static x => x.Opcode == "getelementptr");
        Assert.Contains(legs.Instructions, static x => x.Shape == "load i32, ptr %, align 4" && x.Operands[0].Shape == "load ptr, ptr %, align 8");

        var rows = Analyze(ObjectRows);
        var startup = GeneratedIr.Parse(Generate(rows)).Startup;
        Assert.Contains(startup.Instructions, static x => x.Shape == "getelementptr i8, ptr %, i64 16" && x.Operands[0].Shape == "load ptr, ptr %, align 8");
    }

    // DECISIONS R5-M110: the object runtime is written once in a module that uses objects, and not in one that does not.
    [Fact]
    public void TheObjectRuntimeIsWrittenOnceWhereObjectsAreUsed()
    {
        Assert.Single(Regex.Matches(Generate(Analyze(Objects, OutputKind.Library)), @"define internal i1 @__kimi_object_supports\("));
        Assert.DoesNotContain("__kimi_object_", Generate(Analyze(Arithmetic, OutputKind.Library)), StringComparison.Ordinal);
    }

    // DECISIONS R5-M74, R5-M110: a payload whose struct has a slot names its VTable item, C6c's: one located Unsupported_Kd at its creation
    // and no descriptor text.
    [Fact]
    public void ASlotBearingPayloadIsLeftToC6c()
    {
        const string Source = "open struct Shape\n    public virtual func area(self: objref/Self) -> i32 => 1\n    public init() => ()\n" +
            "func make() -> obj/Shape => Kimi.Intrinsics.makeObj(Shape.init())\n";
        var c = Analyze(Source, OutputKind.Library);
        Assert.False(c.Generate(TextWriter.Null));
        Assert.Equal([("Unsupported_Kd", (string?)"Kimi.Intrinsics.makeObj(Shape.init())")], Records(c));
    }

    // A function's creation, its one call of a factory (ObjectRuntime.Factory: __kimi_make_object and its descriptor's number), and the
    // factory's name.
    private static (IrValue Call, string Factory) Creation(GeneratedIr ir, Compilation c, string function)
    {
        var factory = Regex.Match(Assert.Single(ItemTexts(c, function)), @"call void @(__kimi_make_object\d+)\(").Groups[1].Value;
        return (Assert.Single(CallsOf(Assert.Single(FunctionsOf(ir, c, function))), x => x.Callee == ir.Function(factory)), factory);
    }

    // The metadata constant of a factory's descriptor, its braces' contents.
    private static string Metadata(string text, string factory)
    {
        const string Row = "private constant { i64, i64, i64, i64, ptr, ptr }";
        return Regex.Match(text, $@"(?m)^@__kimi_object_metadata{Number(factory)} = {Regex.Escape(Row)} (\{{ .* \}}), align 8$").Groups[1].Value;
    }

    // The descriptor's number in a factory's name (ObjectRuntime.Factory).
    private static string Number(string factory) => factory["__kimi_make_object".Length..];
}
