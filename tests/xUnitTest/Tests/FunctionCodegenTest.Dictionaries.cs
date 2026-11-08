// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Xunit;

namespace XunitTest;

// The Dictionary family of MIR code generation (R5 C8e; SPEC 4.3, 4.7.3, 4.9.4-4.9.7, 16.3.2, 22.2; DECISIONS R5-M124): the storage kernels
// written inline over their call's entry layout; placeEntry calling its LegacyRow item's Place helper, which calls append's instance; a
// Dictionary from none calling initialize's; a Dictionary's glue calling its own Drop helper, which calls the clearEntries instance only when
// an entry may need a drop; a Dictionary's length; natively. The keys are user Equatable structs: a built-in key's
// equality is C8f's shim (BuiltinEquals).
public sealed partial class FunctionCodegenTest
{
    // The user keys: Key, an i64 (with an i32 value, the key at 16, the value at 24, a stride of 32), and Small, a u8 (the key at 16, an i32
    // value at 20, a stride of 24).
    private const string DictionaryKeys =
        "struct Key\n    Self is Equatable\n    public let code: i64\n    public init(code: i64) => self.code = code\n" +
        "    public func equals(self: ref/Self, other: ref/Self) -> bool => self.code == other.code\n" +
        "struct Small\n    Self is Equatable\n    public let tag: u8\n    public init(tag: u8) => self.tag = tag\n" +
        "    public func equals(self: ref/Self, other: ref/Self) -> bool => self.tag == other.tag\n";

    // A Library over Dictionaries of i32, string, drop-needing and zero-sized values: a Dictionary from none, placeEntry through tryInsert and
    // insertOrReplace, Len (length, and a test of it against 0), the shared, exclusive and owning iterations (borrowStorage,
    // borrowStorageUniq, ownStorage, keyAt, valueAt), reserve and shrinkToFit (dictionaryStorage) and a Dictionary's glue.
    private const string Dictionaries = DictionaryKeys +
        "struct Res\n    public let id: i64\n    public init(id: i64) => self.id = id\n    drop => Console.writeLine(\"res\")\n" +
        "struct Empty\n    public init() => ()\n" +
        "func dcNone() -> Dictionary<Key, i32> => [:]\n" +
        "func dcPut(d: uniq/Dictionary<Key, i32>, k: i64, v: i32)\n    _ = d.tryInsert(Key.init(k), v)\n" +
        "func dcPutText(d: uniq/Dictionary<Key, string>, k: i64, v: string)\n    _ = d.insertOrReplace(Key.init(k), v@move)\n" +
        "func dcPutEmpty(d: uniq/Dictionary<Key, Empty>, k: i64)\n    _ = d.tryInsert(Key.init(k), Empty.init())\n" +
        "func dcCount(d: ref/Dictionary<Key, i32>) -> isize => d.length\n" +
        "func dcNothing(d: ref/Dictionary<Key, i32>) -> bool => d.length == 0\n" +
        "func dcTotal(d: ref/Dictionary<Small, i32>) -> i32\n    var sum: i32 = 0\n    for (k, v) in d => sum += v\n    return sum\n" +
        "func dcBump(d: uniq/Dictionary<Small, i32>)\n    for (k, v) in d\n        v@follow += 1\n" +
        "func dcDrain(d: Dictionary<Small, i32>) -> i32\n    var sum: i32 = 0\n    for (k, v) in d@move => sum += v\n    return sum\n" +
        "func dcRoom(d: uniq/Dictionary<Key, i32>) => d.reserve(8)\n" +
        "func dcFit(d: uniq/Dictionary<Key, i32>) => d.shrinkToFit()\n" +
        "func dcKeep(d: Dictionary<Key, Res>) => ()\n" +
        "func dcKeepInts(d: Dictionary<Key, i32>) => ()\n";

    // SPEC 4.3, 4.9.4-4.9.7, 22.2 (DECISIONS R5-M124): every operation over user keys, string, drop-needing and zero-sized values, growth
    // through transferBytes, reverse-insertion destruction and an owning iteration's early exit; a drop-needing key (Named) in a struct
    // Field's Dictionary, whose entries are destroyed value before key, as are a rejected duplicate's and a removed entry's pairs; natively.
    public static TheoryData<string, string, string> DictionaryPrograms => new()
    {
        {
            "DictionarySlotStrides",
            "struct Z16\n    Self is Equatable\n    public var pad: [0 of i128]\n    public init() => self.pad = []\n    public func equals(self: ref/Self, other: ref/Self) -> bool => true\nfunc wideValue()\n    var d: Dictionary<i32, i128> = [:]\n    var i: i32 = 0\n    while i < 9\n        _ = d.tryInsert(i, (i * 3)@i128)\n        i += 1\n    _ = d.remove(4)\n    d.reserve(64)\n    var sum: i128 = 0\n    for (k, v) in d => sum += v\n    d.shrinkToFit()\n    for (k, v) in d@uniq => v@follow += 1\n    require d[8] == 25 else => $abort(\"wide value index\")\n    require sum == 96 else => $abort(\"wide value sum\")\n    Console.writeLine(\"wide value ok \\(d.length)\")\nfunc text()\n    var d: Dictionary<i32, string> = [:]\n    _ = d.tryInsert(1, \"a\")\n    _ = d.tryInsert(2, \"bb\")\n    _ = d.tryInsert(3, \"ccc\")\n    _ = d.remove(2)\n    _ = d.insertOrReplace(1, \"z\")\n    var n: isize = 0\n    for (k, v) in d => n += 1\n    require n == 2 else => $abort(\"text\")\n    require d[1] == \"z\" else => $abort(\"text value\")\n    d.clear()\n    require d.length == 0 else => $abort(\"cleared\")\n    Console.writeLine(\"text ok\")\nfunc empty()\n    var d: Dictionary<(), ()> = [:]\n    _ = d.tryInsert((), ())\n    require d.length == 1 else => $abort(\"unit\")\n    _ = d.remove(())\n    require d.length == 0 else => $abort(\"unit removed\")\n    Console.writeLine(\"unit ok\")\nfunc aligned()\n    var d: Dictionary<i32, Z16> = [:]\n    _ = d.tryInsert(1, Z16.init())\n    _ = d.tryInsert(2, Z16.init())\n    _ = d.tryInsert(3, Z16.init())\n    _ = d.remove(2)\n    d.reserve(16)\n    d.shrinkToFit()\n    var n: i32 = 0\n    for (k, v) in d => n += k\n    require n == 4 else => $abort(\"aligned value\")\n    var e: Dictionary<Z16, Z16> = [:]\n    _ = e.tryInsert(Z16.init(), Z16.init())\n    match e.tryInsert(Z16.init(), Z16.init())\n        .Ok(_) => $abort(\"aligned duplicate\")\n        .Err(_) => ()\n    var m: isize = 0\n    for (k, v) in e@move => m += 1\n    require m == 1 else => $abort(\"aligned key\")\n    Console.writeLine(\"aligned ok\")\nfunc narrowValue()\n    var d: Dictionary<i128, u8> = [:]\n    _ = d.tryInsert(7, 1)\n    _ = d.tryInsert(8, 2)\n    var total: i32 = 0\n    for (k, v) in d@move => total += v@i32\n    require total == 3 else => $abort(\"narrow value\")\n    Console.writeLine(\"narrow value ok\")\npublic func main()\n    wideValue()\n    text()\n    empty()\n    aligned()\n    narrowValue()\n",
            "wide value ok 8\ntext ok\nunit ok\naligned ok\nnarrow value ok\n"
        },
        {
            "DictionaryRows",
            DictionaryKeys +
            "struct Res\n    public let name: string\n    public init(name: string) => self.name = name@move\n    drop => Console.writeLine(self.name)\n" +
            "struct Token\n    public init() => ()\n    drop => Console.writeLine(\"token\")\n" +
            "struct Named\n    Self is Equatable\n    public let code: i64\n    public let name: string\n" +
            "    public init(code: i64, name: string)\n        self.code = code\n        self.name = name@move\n" +
            "    public func equals(self: ref/Self, other: ref/Self) -> bool => self.code == other.code\n    drop => Console.writeLine(self.name)\n" +
            "struct Holder\n    public var table: Dictionary<Named, Res>\n    public init() => self.table = [:]\n" +
            "var a: Dictionary<Key, i64> = [:]\n" +
            "require a.length == 0 and a.capacity == 0 else => $abort(\"empty\")\n" +
            "for i in 0..20\n    _ = a.tryInsert(Key.init(i@i64), (i * 10)@i64)\n" +
            "require a.length == 20 and a.capacity >= 20 else => $abort(\"grow\")\n" +
            "match a.tryInsert(Key.init(3), 1)\n    .Ok(()) => $abort(\"duplicate\")\n    .Err(let pair) => require pair.1 == 1 else => $abort(\"err\")\n" +
            "match a.insertOrReplace(Key.init(4), 44)\n    .Some(let old) => require old == 40 else => $abort(\"old\")\n    .None => $abort(\"replace\")\n" +
            "require a[Key.init(4)] == 44 else => $abort(\"index\")\n" +
            "match a.tryGet(Key.init(5))\n    .Some(let v) => require v == 50 else => $abort(\"get\")\n    .None => $abort(\"missing\")\n" +
            "match a.remove(Key.init(6))\n" +
            "    .Some(let pair) => require pair.0.code == 6 and pair.1 == 60 else => $abort(\"remove\")\n    .None => $abort(\"absent\")\n" +
            "require a.length == 19 else => $abort(\"length\")\n" +
            "var total: i64 = 0\nfor (k, v) in a => total += v\n" +
            "for (k, v) in a@uniq\n    v@follow += 1\n" +
            "var after: i64 = 0\nfor (k, v) in a => after += v\nrequire after == total + 19 else => $abort(\"uniq\")\n" +
            "a.reserve(64)\nrequire a.capacity >= 83 else => $abort(\"reserve\")\n" +
            "a.shrinkToFit()\nrequire a.capacity >= a.length else => $abort(\"shrink\")\n" +
            "a.clear()\nrequire a.length == 0 else => $abort(\"clear\")\n" +
            "var r: Dictionary<Key, Res> = [Key.init(1): Res.init(\"r1\"), Key.init(2): Res.init(\"r2\")]\n" +
            "_ = r.tryInsert(Key.init(3), Res.init(\"r3\"))\nr.clear()\nConsole.writeLine(\"cleared\")\n" +
            "_ = r.tryInsert(Key.init(4), Res.init(\"r4\"))\n" +
            "var s: Dictionary<Key, string> = [Key.init(1): \"a\", Key.init(2): \"b\", Key.init(3): \"c\"]\n" +
            "for (k, v) in s@move\n    Console.writeLine(v)\n    exit\n" +
            "var z: Dictionary<Key, Token> = [:]\n_ = z.tryInsert(Key.init(1), Token.init())\n_ = z.tryInsert(Key.init(2), Token.init())\n" +
            "var u: Dictionary<Small, ()> = [:]\n_ = u.tryInsert(Small.init(1), ())\nrequire u.length == 1 else => $abort(\"unit\")\n" +
            "let nested: Array<Dictionary<Key, string>> = [[Key.init(1): \"n\"]]\n" +
            "var h = Holder.init()\n_ = h.table.tryInsert(Named.init(7, \"k7\"), Res.init(\"v7\"))\n" +
            "_ = h.table.tryInsert(Named.init(8, \"k8\"), Res.init(\"v8\"))\n_ = h.table.tryInsert(Named.init(9, \"k9\"), Res.init(\"v9\"))\n" +
            "_ = h.table.tryInsert(Named.init(7, \"k7dup\"), Res.init(\"v7dup\"))\n" +
            "match h.table.remove(Named.init(8, \"q\"))\n    .Some(let pair) => Console.writeLine(\"removed\")\n    .None => $abort(\"named\")\n" +
            "require h.table.length == 2 else => $abort(\"holder\")\n" +
            "Console.writeLine(\"done\")\n",
            "r3\nr2\nr1\ncleared\na\nv7dup\nk7dup\nremoved\nv8\nk8\nq\ndone\nv9\nk9\nv7\nk7\ntoken\ntoken\nr4\n"
        },
    };

    [Theory]
    [MemberData(nameof(DictionaryPrograms))]
    public void DictionaryProgramsRun(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture(name, source, stdout);

    // SPEC 4.7.3, 22.2 (DECISIONS R5-M124, R8-K1a): the storage bodies read the Handle's words: borrowStorage and borrowStorageUniq the
    // first link (32) and the live count (24), no remainder's init storing the slot stride (slotStride's, 24 for Small and i32, is computed
    // where a slot is addressed), ownStorage moving the 56-byte Handle whole into its remainder; the bodies keyAt and valueAt (R8-K1b)
    // address the slot, keyAt at the key's byte offset 16 (valueAt's offset is computed from K's stride and V's alignment, which the
    // natives run); dictionaryStorage's kernel writes the address and the stride (32 for Key and i32); a Dictionary's length is its live
    // count, the word at 24.
    [Fact]
    public void TheDictionaryKernelsWriteTheHandlesWordsOverTheEntryLayout()
    {
        var c = Analyze(Dictionaries, OutputKind.Library);
        var ir = GeneratedIr.Parse(Generate(c));
        var borrows = FunctionsOf(ir, c, "borrowStorage").Concat(FunctionsOf(ir, c, "borrowStorageUniq"))
            .Where(static x => x.Instructions.Any(static y => y.Shape == "getelementptr i8, ptr %, i64 32")).ToArray();
        Assert.Equal(5, borrows.Length); // findEntry's over Key and i32, string and Empty, dcTotal's and dcBump's over Small and i32.
        Assert.All(borrows, static x => Assert.Contains(x.Instructions, static y => y.Shape == "getelementptr i8, ptr %, i64 24"));
        Assert.DoesNotContain(FunctionsOf(ir, c, "init"), static x => x.Instructions.Any(static y => y.Shape == "store i64 24, ptr %, align 8"));
        var own = Assert.Single(FunctionsOf(ir, c, "ownStorage"));
        Assert.Contains(own.Instructions, static x => x.Shape == "call void @(ptr align 8 %, ptr align 8 %, i64 56, i1 false)");
        var keys = FunctionsOf(ir, c, "keyAt");
        Assert.NotEmpty(keys);
        Assert.All(keys, static x => Assert.Contains(x.Instructions, static y => y.Shape == "mul i64 16, 1"));
        Assert.NotEmpty(FunctionsOf(ir, c, "valueAt"));
        var layout = Assert.Single(FunctionsOf(ir, c, "reserveEntries"));
        Assert.Contains(layout.Instructions, static x => x.Shape == "store i64 32, ptr %, align 8");
        var count = Assert.Single(FunctionsOf(ir, c, "dcCount"));
        Assert.Contains(count.Instructions, static x => x.Shape == "load i64, ptr %, align 8" && x.Operands[0].Shape == "getelementptr i8, ptr %, i64 24");
        Assert.Contains(Assert.Single(FunctionsOf(ir, c, "dcNothing")).Instructions, static x => x.Shape == "getelementptr i8, ptr %, i64 24");
    }

    // SPEC 4.9.6, 22.2 (DECISIONS R5-M124): placeEntry calls the Place helper of its LegacyRow item with the handle, the key and the value at
    // their slots (a struct key by its storage, an i32 by value, a zero-sized value by an address) and the location tryInsert received (a
    // caller-location operation); the helper calls append's instance with the entry's stride and the location, then moves the key and the
    // value to their offsets.
    [Fact]
    public void PlaceEntryCallsTheHelperOfItsInstanceWhichCallsAppend()
    {
        var c = Analyze(Dictionaries, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var append = Assert.Single(FunctionsOf(ir, c, "append"));
        var helpers = Regex.Matches(text, @"define internal void @(__kimi_dictionary_place_\d+)\(").Select(x => ir.Function(x.Groups[1].Value)).ToArray();
        Assert.Equal(3, helpers.Length); // Of <Key, i32>, <Key, string> and <Key, Empty>.
        Assert.All(helpers, x => Assert.Equal(new[] { append }, CalleesOf(x).Where(static y => y.Blocks.Count > 0).ToArray()));
        var places = FunctionsOf(ir, c, "tryInsert").Concat(FunctionsOf(ir, c, "insertOrReplace")).SelectMany(CallsOf)
            .Where(x => helpers.Contains(x.Callee)).ToArray();
        Assert.Equal(3, places.Length);
        Assert.Contains(places, static x => x.Shape == "call void @(ptr %, ptr %, i32 %, ptr %, i64 %)");
        Assert.Equal(
            ["internal void (ptr, ptr, i32, ptr, i64)", "internal void (ptr, ptr, ptr, ptr, i64)"],
            helpers.Select(static x => x.Signature).Distinct().Order(StringComparer.Ordinal));
        var grow = Assert.Single(CallsOf(helpers.Single(x => x.Signature.Contains("i32", StringComparison.Ordinal))), x => x.Callee == append);
        Assert.Equal("call ptr @(ptr %, i64 32, ptr %, i64 %)", grow.Shape);
    }

    // SPEC 4.3, 4.9.5, 4.9.7, 16.3.2 (DECISIONS R5-M124): `[:]` calls initialize's instance at its Place, a slot of the entry block the result
    // is then moved from, without the old generator's bridge; a Dictionary's glue calls its own Drop helper with its parameters, which calls
    // the clearEntries instance only for entries that may need a drop, then releases the buffer.
    [Fact]
    public void ADictionaryIsMadeByInitializeAndDestroyedThroughItsDropHelper()
    {
        var c = Analyze(Dictionaries, OutputKind.Library);
        var text = Generate(c);
        var ir = GeneratedIr.Parse(text);
        var none = Assert.Single(FunctionsOf(ir, c, "dcNone"));
        var init = Assert.Single(CallsOf(none), x => x.Callee == Assert.Single(FunctionsOf(ir, c, "initialize")));
        Assert.Equal("call void @(ptr %)", init.Shape);
        Assert.Equal("alloca { [0 x i64], [56 x i8] }, align 8", Assert.Single(init.Operands).Shape);
        Assert.DoesNotContain("__kimi_dictionary_init", text, StringComparison.Ordinal);

        var free = ir.Function("__kimi_array_free");
        var clear = Assert.Single(FunctionsOf(ir, c, "clearEntries"));
        foreach (var (name, callees) in new[] { ("dcKeep", new[] { clear, free }), ("dcKeepInts", new[] { free }) })
        {
            var glue = Assert.Single(CalleesOf(Assert.Single(FunctionsOf(ir, c, name))), static x => x.Signature == "internal void (ptr, ptr, i64)");
            var call = Assert.Single(CallsOf(glue));
            Assert.Equal([glue.Parameters[0], glue.Parameters[1], glue.Parameters[2]], call.Operands);
            Assert.Equal(callees, CalleesOf(call.Callee!));
        }
    }

    // PD8, R5-M46 (DECISIONS R5-M124): the Dictionary helpers precede every definition, each written once, with the memory declarations
    // placeEntry's row declares, so a module that only destroys a Dictionary has no Place helper; a module whose bodies shrink one defines the storage
    // byte primitives once (DECISIONS R5-M145).
    [Fact]
    public void DictionaryHelpersPrecedeTheDefinitionsWithTheRuntimeTheirRowsDeclare()
    {
        var text = Generate(Analyze(Dictionaries, OutputKind.Library));
        var first = Regex.Match(text, @"define internal \S+ @__kimi_f\d+\(").Index;
        var helpers = Regex.Matches(text, @"define internal \S+ @(__kimi_dictionary_[a-z]+_\d+)\(").ToArray();
        Assert.NotEmpty(helpers);
        Assert.All(helpers, x => Assert.True(x.Index < first, x.Value));
        Assert.Equal(helpers.Length, helpers.Select(static x => x.Groups[1].Value).Distinct().Count());
        Assert.True(text.IndexOf(LlvmModuleText.MemoryDeclarations, StringComparison.Ordinal) is >= 0 and var memory && memory < first);
        Assert.Single(Regex.Matches(text, @"define internal ptr @__kimi_try_allocate\("));
        Assert.Single(Regex.Matches(text, @"define internal void @__kimi_transfer_bytes\("));

        var kept = Generate(Analyze(DictionaryKeys + "func dcKeep(d: Dictionary<Key, string>) => ()\n", OutputKind.Library));
        Assert.Matches(@"define internal void @__kimi_dictionary_drop_\d+\(", kept);
        Assert.DoesNotMatch(@"define internal \S+ @__kimi_dictionary_place_\d+\(", kept);
    }
}
