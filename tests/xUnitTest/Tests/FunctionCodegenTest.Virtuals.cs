// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// The virtual tables and virtual dispatch of MIR code generation (R5 C6c; SPEC 6.4.1-6.4.6, 10.7, IMPL 21.1.3; DECISIONS R5-M74, R5-M114):
// a descriptor's table of the bodies its slots run, a refuted slot without an entry, and a call through a slot (a Virtual callee, a
// virtual function's Item called and erased, a virtual witness), which loads its entry from its receiver's object and calls it under the
// slot's signature; a direct base call calls its body.
public sealed partial class FunctionCodegenTest
{
    // A Library of slot-bearing payloads made by each handle (A -> B -> C with an inherited slot and base calls; a receiver behind a zero-sized
    // parameter, an aggregate, a borrow and a Never result; a generic family with a conditional slot; a virtual witness) and of the calls
    // through their slots: Virtual calls, one in a generic body, a virtual Item called and erased, a Never-result one erased to another
    // result, and a requirement call and a requirement Item, called and erased, of the virtual witness.
    private const string Virtuals =
        "open struct A\n    public let n: i32\n    public init(n: i32) => self.n = n\n" +
        "    public virtual func value(self: objref/Self, bias: i32 = 1) -> i32 => self.n + bias\n" +
        "    public virtual func nested(self: objref/Self) -> i32 => self.value(3) + 100\n" +
        "open struct B : A\n    public init(n: i32) : base(n) => ()\n    override func value(self: objref/Self, bias: i32) -> i32 => base.value(bias) + 10\n" +
        "    public virtual func extra(self: objref/Self) -> i32 => 7\n" +
        "struct C : B\n    public init(n: i32) : base(n) => ()\n    override func value(self: objref/Self, bias: i32) -> i32 => base.value(bias) + 20\n" +
        "open struct Pairs\n    public let number: i32 = 2\n    public init() => ()\n" +
        "    public virtual func pair(zero: (), self: objref/Self, text: string) -> (i32, string) => (1, text@move)\n" +
        "    public virtual func view(self: objref/Self) -> ref/i32 => self.number@ref\n" +
        "    public virtual func stop(self: objref/Self, code: i32) -> Never => $abort(\"stop\")\n" +
        "struct NonCopy\n    public init() => ()\n    drop => ()\n" +
        "open struct Box<T>\n    T is Owned\n    public init() => ()\n    public virtual func first(self: objref/Self) -> i32 => 1\n" +
        "    public virtual func copy(self: objref/Self, value: ref/T) -> T\n        T is Copy\n        return value@follow\n" +
        "struct Leaf<T> : Box<T>\n    T is Owned\n    public init() => ()\n    override func copy(self: objref/Self, value: ref/T) -> T => value@follow\n" +
        "contract Reader\n    Self is ObjectPayload\n    func score(self: objref/Self, bias: i32) -> i32\n" +
        "open struct Scored\n    Self is Reader\n    public init() => ()\n    public virtual func score(self: objref/Self, bias: i32) -> i32 => bias\n" +
        "func makeC() -> obj/C => Kimi.Intrinsics.makeObj(C.init(3))\nfunc makeRcC() -> rc/C => Kimi.Intrinsics.makeRc(C.init(4))\n" +
        "func makePairs() -> arc/Pairs => Kimi.Intrinsics.makeArc(Pairs.init())\n" +
        "func makeLeaf() -> obj/Leaf<i32> => Kimi.Intrinsics.makeObj(Leaf<i32>.init())\n" +
        "func makeNonCopy() -> obj/Leaf<NonCopy> => Kimi.Intrinsics.makeObj(Leaf<NonCopy>.init())\n" +
        "func makeScored() -> obj/Scored => Kimi.Intrinsics.makeObj(Scored.init())\n" +
        "func call(v: objref/A) -> i32 => v.value(2)\nfunc third(v: objref/B) -> i32 => v.extra()\n" +
        "func pairOf(v: objref/Pairs) -> (i32, string) => v.pair((), \"text\")\nfunc viewOf(v: objref/Pairs) -> i32 => v.view()@follow\n" +
        "func through<T>(v: objref/Box<T>) -> i32\n    T is Owned\n    return v.first()\nfunc boxes(v: objref/Box<i32>) -> i32 => through(v)\n" +
        "func item(v: objref/A) -> i32\n    let f = A.value\n    return f(v, 1)\nfunc erased() -> (objref/A, i32) -> i32 => A.value\n" +
        "func stopper() -> (objref/Pairs, i32) -> string => Pairs.stop\n" +
        "func witness<T>(v: objref/T) -> i32\n    T is Reader\n    return v.score(1)\nfunc scores(v: objref/Scored) -> i32 => witness(v)\n" +
        "func witnessItem<T>(v: objref/T) -> i32\n    T is Reader\n    let f = T.score\n    return f(v, 1)\n" +
        "func witnessErased<T>() -> (objref/T, i32) -> i32\n    T is Reader and Owned\n    return T.score\n" +
        "func scoreItem(v: objref/Scored) -> i32 => witnessItem(v)\nfunc scoreErased() -> (objref/Scored, i32) -> i32 => witnessErased<Scored>()\n";

    // SPEC 6.4.3, 6.4.4, 6.4.6 (DECISIONS R5-M114): dispatch by the Dynamic Type through every form, natively: member,
    // nested, inherited and qualified calls, a call through a reference to a handle, an Item called, passed and erased, a receiver behind a
    // zero-sized parameter with an aggregate and a borrow result, the Item of that slot erased, a generic family's slot, a virtual witness.
    private const string VirtualRows =
        "open struct A\n    public let n: i32\n    public init(n: i32) => self.n = n\n" +
        "    public virtual func value(self: objref/Self, bias: i32 = 1) -> i32 => self.n + bias\n" +
        "    public virtual func nested(self: objref/Self) -> i32 => self.value(3) + 100\n" +
        "open struct B : A\n    public init(n: i32) : base(n) => ()\n    override func value(self: objref/Self, bias: i32) -> i32 => base.value(bias) + 10\n" +
        "    public virtual func extra(self: objref/Self) -> i32 => 7\n" +
        "struct C : B\n    public init(n: i32) : base(n) => ()\n    override func value(self: objref/Self, bias: i32) -> i32 => base.value(bias) + 20\n" +
        "open struct Pairs\n    public let number: i32 = 2\n    public init() => ()\n" +
        "    public virtual func pair(zero: (), self: objref/Self, text: string) -> (i32, string) => (1, text@move)\n" +
        "    public virtual func view(self: objref/Self) -> ref/i32 => self.number@ref\n" +
        "struct Paired : Pairs\n    public let tail: i32 = 42\n    public init() => ()\n" +
        "    override func pair(zero: (), self: objref/Self, text: string) -> (i32, string) => (3, text@move)\n" +
        "    override func view(self: objref/Self) -> ref/i32 => self.tail@ref\n" +
        "open struct Box<T>\n    T is Owned\n    public init() => ()\n    public virtual func first(self: objref/Self) -> i32 => 1\n" +
        "struct Leaf<T> : Box<T>\n    T is Owned\n    public init() => ()\n    override func first(self: objref/Self) -> i32 => 10\n" +
        "contract Reader\n    Self is ObjectPayload\n    func score(self: objref/Self, bias: i32) -> i32\n" +
        "open struct Scored\n    Self is Reader\n    public init() => ()\n    public virtual func score(self: objref/Self, bias: i32) -> i32 => bias\n" +
        "struct Rated : Scored\n    public init() : base() => ()\n    override func score(self: objref/Self, bias: i32) -> i32 => bias + 100\n" +
        "func invoke<F>(f: ref/F, value: objref/A) -> i32\n    F is Callable<(objref/A, i32) -> i32>\n    return f(value, 2)\n" +
        "func through<T>(v: objref/Box<T>) -> i32\n    T is Owned\n    return v.first()\n" +
        "func witness<T>(v: objref/T) -> i32\n    T is Reader\n    return v.score(1)\n" +
        "let c = C.init(3)@obj\nrequire c.value() == 34 and c.nested() == 136 and c.extra() == 7 else => $abort(\"dispatch\")\n" +
        "require A.value(c@objref/A, 2) == 35 else => $abort(\"qualified\")\nlet held = c@ref\nrequire held.value(2) == 35 else => $abort(\"held\")\n" +
        "let f = A.value\nrequire f(c@objref/A, 2) == 35 and invoke(f, c@objref/A) == 35 else => $abort(\"item\")\n" +
        "let erased: (objref/A, i32) -> i32 = f\nlet shared = C.init(5)@arc\nrequire erased(shared@objref/A, 2) == 37 else => $abort(\"erased\")\n" +
        "let d = Paired.init()@rc\nlet p = d.pair((), \"payload\")\nrequire p.0 == 3 else => $abort(\"aggregate\")\nConsole.writeLine(p.1)\n" +
        "require d.view()@follow == 42 else => $abort(\"borrow\")\nlet g: ((), objref/Pairs, string) -> (i32, string) = Pairs.pair\n" +
        "let q = g((), d@objref/Pairs, \"erased\")\nrequire q.0 == 3 else => $abort(\"erased aggregate\")\nConsole.writeLine(q.1)\n" +
        "let leaf = Leaf<i32>.init()@obj\nrequire through(leaf@objref/Box<i32>) == 10 else => $abort(\"generic\")\n" +
        "let rated = Rated.init()@obj\nrequire witness<Scored>(rated@objref/Scored) == 101 else => $abort(\"witness\")\nConsole.writeLine(\"virtuals\")\n";

    // DECISIONS R5-M74, R5-M114: a descriptor names its virtual table, the bodies its slots run in slot order (an override, an inherited
    // body, the base's virtual); one descriptor per erased payload; a refuted slot has no entry, its override's body none either.
    [Fact]
    public void AVirtualTableHoldsTheBodyEachSlotRuns()
    {
        var (c, ir, text) = ParseVirtuals();
        var id = Number(Creation(ir, c, "makeC").Factory);
        Assert.Equal(id, Number(Creation(ir, c, "makeRcC").Factory));
        Assert.Equal($"[3 x ptr] [ptr @{BodyOf(c, "C", "value")}, ptr @{BodyOf(c, "A", "nested")}, ptr @{BodyOf(c, "B", "extra")}]", Table(text, id));
        var descriptor = $"@__kimi_object_descriptor{id} = private constant {{ ptr, ptr, ptr, ptr }} {{ ptr @__kimi_object_metadata{id}, " +
            $"ptr @__kimi_object_free, ptr @__kimi_object_views{id}, ptr @__kimi_object_virtuals{id} }}, align 8\n";
        Assert.Contains(descriptor, text, StringComparison.Ordinal);
        var pairs = Table(text, Number(Creation(ir, c, "makePairs").Factory));
        Assert.Equal($"[3 x ptr] [ptr @{BodyOf(c, "Pairs", "pair")}, ptr @{BodyOf(c, "Pairs", "view")}, ptr @{BodyOf(c, "Pairs", "stop")}]", pairs);
        Assert.Matches(@"^\[2 x ptr\] \[ptr @__kimi_f\d+, ptr @__kimi_f\d+\]$", Table(text, Number(Creation(ir, c, "makeLeaf").Factory)));
        Assert.Matches(@"^\[2 x ptr\] \[ptr @__kimi_f\d+, ptr null\]$", Table(text, Number(Creation(ir, c, "makeNonCopy").Factory)));
        Assert.Equal([("copy", true)], Generated(c.Instances!).Where(static x => x.Name == "copy")); // Leaf<i32>'s: Leaf<NonCopy>'s has none.
    }

    // SPEC 6.4.3, IMPL 21.1.3 (DECISIONS R5-M110, R5-M114): a Virtual call loads its slot's entry from the object its receiver borrows (the
    // header 16 bytes before the payload, its descriptor, the table at 24, the slot's word) and calls it under the slot's signature: a slot
    // past the first, a receiver behind a zero-sized parameter with a result slot, a borrow result, a call in a generic body's instance.
    [Fact]
    public void AVirtualCallLoadsItsSlotFromItsReceiversObject()
    {
        var (c, ir, _) = ParseVirtuals();
        var call = Dispatched(FunctionOf(ir, c, null, "call"));
        Assert.Equal("call i32 %(ptr %, i32 2)", call.Shape);
        Assert.Equal((0, call.Operands[1]), Dispatch(call));
        var extra = Dispatched(FunctionOf(ir, c, null, "third"));
        Assert.Equal((2, extra.Operands[1]), Dispatch(extra));
        var pair = Dispatched(FunctionOf(ir, c, null, "pairOf"));
        Assert.StartsWith("call void %(ptr %, ptr %, ptr ", pair.Shape, StringComparison.Ordinal);
        Assert.Equal((0, pair.Operands[2]), Dispatch(pair));
        var view = Dispatched(FunctionOf(ir, c, null, "viewOf"));
        Assert.Equal("call ptr %(ptr %)", view.Shape);
        Assert.Equal((1, view.Operands[1]), Dispatch(view));
        var generic = Dispatched(FunctionOf(ir, c, null, "through"));
        Assert.Equal((0, generic.Operands[1]), Dispatch(generic));
    }

    // SPEC 6.4.3, 7.7.4, 10.7 (DECISIONS R5-M108, R5-M114): a virtual function's Item runs its slot: its Value call through the receiver's
    // table, no adapter; its adapter's entry, the erased virtual Item's thunk, through its receiver parameter's; a Never-result slot erased to
    // a string result is called under its own signature, without the entry's result slot, then unreachable.
    [Fact]
    public void AVirtualItemRunsItsSlotInItsCallAndItsAdapter()
    {
        var (c, ir, text) = ParseVirtuals();
        var item = Dispatched(FunctionOf(ir, c, null, "item"));
        Assert.Equal("call i32 %(ptr %, i32 1)", item.Shape);
        Assert.Equal((0, item.Operands[1]), Dispatch(item));
        var entry = ir.Function(Adapter(c, text, "erased"));
        var forwarded = Assert.Single(entry.Instructions, static x => x.Opcode == "call");
        Assert.Equal("call i32 %(ptr %, i32 %)", forwarded.Shape);
        Assert.Equal((0, entry.Parameters[1]), Dispatch(forwarded));
        var stop = ir.Function(Adapter(c, text, "stopper"));
        Assert.Equal("internal void (i64, ptr, ptr, i32, ptr, ptr, i64)", stop.Signature);
        var never = Assert.Single(stop.Instructions, static x => x.Opcode == "call");
        Assert.Equal("call void %(ptr %, i32 %)", never.Shape);
        Assert.Equal((2, stop.Parameters[2]), Dispatch(never));
        Assert.Equal("unreachable", stop.Blocks[^1].Terminator.Shape);
    }

    // SPEC 6.4.4, 6.4.6 (DECISIONS R5-M114): a requirement call whose witness is a virtual function runs its slot, as the witness maps to it;
    // a direct base call calls its base's body.
    [Fact]
    public void AVirtualWitnessRunsItsSlotAndABaseCallItsBody()
    {
        var (c, ir, _) = ParseVirtuals();
        var witness = Dispatched(FunctionOf(ir, c, null, "witness"));
        Assert.Equal("call i32 %(ptr %, i32 1)", witness.Shape);
        Assert.Equal((0, witness.Operands[1]), Dispatch(witness));
        var derived = FunctionOf(ir, c, "B", "value");
        Assert.Contains(FunctionOf(ir, c, "A", "value"), CalleesOf(derived));
        Assert.DoesNotContain(derived.Instructions, static x => x is { Opcode: "call", Callee: null });
    }

    // SPEC 6.4.3, 6.4.6, 7.7.4 (DECISIONS R5-M108, R5-M114): a requirement Item whose witness is a virtual function runs the witness's slot, as
    // a requirement call does (BodyRun), whatever the Item does: its Value call loads the entry from the receiver's table, and so does its
    // adapter's entry once the Item is erased into a Function value; neither calls the witness's body.
    [Fact]
    public void AVirtualWitnessItemRunsItsSlotCalledAndErased()
    {
        var (c, ir, text) = ParseVirtuals();
        var item = Dispatched(FunctionOf(ir, c, null, "witnessItem"));
        Assert.Equal("call i32 %(ptr %, i32 1)", item.Shape);
        Assert.Equal((0, item.Operands[1]), Dispatch(item));
        var entry = ir.Function(Adapter(c, text, "witnessErased"));
        var forwarded = Dispatched(entry);
        Assert.Equal("call i32 %(ptr %, i32 %)", forwarded.Shape);
        Assert.Equal((0, entry.Parameters[1]), Dispatch(forwarded));
    }

    [Fact]
    public void VirtualProgramsRun() => ScalarEmissionTest.EmitFixture("VirtualRows", VirtualRows, "payload\nerased\nvirtuals\n");

    // The function name of a function's one body item (its index in the checked set), by its container (null: any) and its name.
    internal static string BodyOf(Compilation c, string? owner, string name)
    {
        var set = c.Instances!;
        return "__kimi_f" + Enumerable.Range(0, set.Items.Length).Single(x =>
            set.Items[x].Key is { Kind: MonoKind.Body, Body.Def: { Kind: MirBodyKind.Fn, Decl: { } function } } && function.Name == name &&
            (owner is null || (function.Scope.Owner is DeclarationContainerKoto container && container.Name == owner)));
    }

    // The Virtuals Library generated under MIR code generation: its compilation, its IR and its text.
    private static (Compilation C, GeneratedIr Ir, string Text) ParseVirtuals()
    {
        var c = Analyze(Virtuals, OutputKind.Library);
        var text = Generate(c);
        return (c, GeneratedIr.Parse(text), text);
    }

    // A function's one indirect call, a call through a slot.
    private static IrValue Dispatched(IrFunction function) => Assert.Single(function.Instructions, static x => x is { Opcode: "call", Callee: null });

    // The slot a call through a virtual slot loads its entry from and the receiver it reads (FunctionCodegen.EntryOf): the entry the table's
    // word at 8 times the slot, the table the descriptor's word at 24, the descriptor the header's first word, the header 16 bytes before the
    // receiver's payload.
    private static (int Slot, IrValue Receiver) Dispatch(IrValue call)
    {
        var entry = call.Operands[0];
        Assert.Equal("load ptr, ptr %, align 8", entry.Shape);
        var word = entry.Operands[0];
        var slot = 0;
        if (word.Opcode == "getelementptr")
        {
            var offset = Assert.Single(Regex.Matches(word.Shape, @"^getelementptr i8, ptr %, i64 (\d+)$")).Groups[1].Value;
            slot = int.Parse(offset, System.Globalization.CultureInfo.InvariantCulture) / 8;
            word = word.Operands[0];
        }

        Assert.Equal("load ptr, ptr %, align 8", word.Shape);
        Assert.Equal("getelementptr i8, ptr %, i64 24", word.Operands[0].Shape);
        var descriptor = word.Operands[0].Operands[0];
        Assert.Equal("load ptr, ptr %, align 8", descriptor.Shape);
        Assert.Equal("getelementptr i8, ptr %, i64 -16", descriptor.Operands[0].Shape);
        return (slot, descriptor.Operands[0].Operands[0]);
    }

    // The function of a function's one body item (BodyOf).
    private static IrFunction FunctionOf(GeneratedIr ir, Compilation c, string? owner, string name) => ir.Function(BodyOf(c, owner, name));

    // The entry of the adapter a function's definition stores into its Function value (FunctionCodegen.Erasures), read from the module's
    // text.
    private static string Adapter(Compilation c, string text, string function)
    {
        var definition = Regex.Match(text, $@"(?ms)^define [^\n]*@{BodyOf(c, null, function)}\(.*?^}}").Value;
        return Regex.Match(definition, @"store ptr @(__kimi_erase\d+)_table,").Groups[1].Value;
    }

    // A descriptor's virtual table constant, its brackets.
    private static string Table(string text, string id)
        => Regex.Match(text, $@"(?m)^@__kimi_object_virtuals{id} = private constant (\[.*\]), align 8$").Groups[1].Value;
}
