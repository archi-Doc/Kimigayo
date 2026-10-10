// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class ElementBorrowEmissionTest
{
    private const string Same = "func same(left: ref/string, right: ref/string) -> bool => left == right\n";
    private const string Item = "struct Item\n    public var name: string\n    public var n: i32\n    public init(name: string, n: i32)\n        self.name = name@move\n        self.n = n\n";
    private const string Counter = "struct Counter\n    Self is Copy\n    public var value: i32\n    public init(value: i32) => self.value = value\n    public func inc(self: uniq/Self) => self.value += 1\n";
    private const string Bump = "func bump(target: uniq/i32) => target@follow += 1\n";
    private const string Inner = "struct Inner\n    public var name: string\n    public var items: Array<i32>\n    public init(name: string)\n        self.name = name@move\n        self.items = [1]\n    public func show(self) => Console.writeLine(self.name)\n    public func rename(self: uniq/Self) => self.name = \"renamed\"\n";
    private const string Pt = "struct Pt\n    Self is Copy\n    public var n: i32\n    public init(n: i32) => self.n = n\n";
    private const string Bank = "struct Bank\n    public var items: Array<Item>\n    public init(items: Array<Item>) => self.items = items@move\n";
    private const string SharedCopyPart = Pt + "func show(v: ref/i32) => Console.writeLine(\"\\(v@follow)\")\nfunc first(xs: ref/Array<Pt>) => show(xs[0].n)\nvar ps: Array<Pt> = [Pt.init(7)]\nfirst(ps@ref)";

    public static TheoryData<string, string> Fixtures => new()
    {
        { "Tuple", "let a = (\"first\", 42)\nif a.0 == \"first\" and a.0 != \"last\" => Console.writeLine(\"ok\")\nlet whole = a@move" },
        { "SameElement", "let a: [1 of string] = [\"first\"]\nif same(a[0], a[((0x0))]) => Console.writeLine(\"ok\")\nlet whole = a@move" },
        { "BothElements", "let a = (\"first\", \"first\")\nif same(a.0, a.1) and a.0 == a.1 => Console.writeLine(\"ok\")\nlet whole = a@move" },
        { "Nested", "let a: [1 of (string, [1 of string])] = [(\"first\", [\"first\"])]\nif same(a[0].0, a[0].1[0]) => Console.writeLine(\"ok\")" },
        { "Partial", "let a = ((\"first\", \"last\"), \"sibling\")\nlet taken = a.0.1@move\nif same(a.0.0, a.0.0) => Console.writeLine(\"ok\")" },
        { "Repair", "var a = (\"first\", \"last\")\nlet taken = a.0@move\na.0 = \"new\"\nif same(a.0, a.0) => Console.writeLine(\"ok\")\nlet whole = a@move" },
        { "SiblingMove", "func inspect(left: ref/string, right: string) -> bool => left == left\nlet a = (\"first\", \"last\")\nif inspect(a.0, a.1@move) => Console.writeLine(\"ok\")" },
        { "SiblingReplace", "var a = (\"first\", \"last\")\nif a.0 == (label work: do\n    a.1 = \"new\"\n    exit to work \"first\"\n) => Console.writeLine(\"ok\")" },
        { "SiblingUpdate", "var a = (\"first\", 40)\na.1 += if a.0 == \"first\" => 2 else => 0\nif a.1 == 42 => Console.writeLine(\"ok\")" },
        { "DynamicSibling", "func inspect(left: ref/string, ignored: ()) -> bool => left == left\nvar a: (string, [1 of string]) = (\"first\", [\"last\"])\nvar i: isize = 0\nif inspect(a.0, a.1[i] = \"new\") => Console.writeLine(\"ok\")" },
        { "CallRelease", "var a = (\"first\", \"last\")\nlet equal = same(a.0, a.0)\na.0 = \"new\"\nlet whole = a@move\nif equal => Console.writeLine(\"ok\")" },
        { "CompareRelease", "var a = (\"first\", \"last\")\nlet equal = a.0 == \"first\"\na.0 = \"new\"\nlet whole = a@move\nif equal => Console.writeLine(\"ok\")" },
        { "NestedCall", "func identity(value: bool) -> bool => value\nlet a = (\"first\", \"last\")\nif identity(same(a.0, a.0)) and same(a.1, a.1) => Console.writeLine(\"ok\")" },
        { "Named", "let a = (\"first\", \"first\")\nif same(right: a.1, left: a.0) => Console.writeLine(\"ok\")" },
        { "MixedTemporary", "let a = (\"first\", 0)\nif same(a.0, (label work: do\n    exit to work \"first\"\n)) => Console.writeLine(\"ok\")" },
        { "Loop", "var a = (\"first\", \"last\")\nvar i = 0\nloop\n    if not same(a.0, a.0) => exit\n    a.0 = \"new\"\n    i += 1\n    if i < 3 => continue\n    exit\nif i == 3 => Console.writeLine(\"ok\")" },
        { "Defer", "func f()\n    let a = (\"first\", \"last\")\n    defer\n        if same(a.0, a.0) => Console.writeLine(\"ok\")\n    let taken = a.1@move\nf()" },
        { "ConditionalPartial", "func f(take: bool) -> bool\n    let a = (\"first\", \"last\")\n    if take\n        let taken = a.1@move\n    return same(a.0, a.0)\nif f(true) and f(false) => Console.writeLine(\"ok\")" },
        { "AggregateResult", "func inspect(a: ref/string) -> (string, i32) => (\"new\", 42)\nvar a = (\"first\", 0)\nlet result = inspect(a.0)\na.0 = \"last\"\nif result.1 == 42 => Console.writeLine(\"ok\")" },
        { "Covered", "let a = (\"first\", 0)\nmatch true\n    _ => ()\n    true => (label work: do\n        let equal = same(a.0, a.0)\n    )\nConsole.writeLine(\"ok\")" },
        { "GuardCandidate", "let a = (\"first\", 0)\nmatch \"other\"@move\n    let s if same(a.0, s) => Console.writeLine(\"bad\")\n    _ => Console.writeLine(\"ok\")" },
        { "NestedGuardCandidate", "func three(a: ref/string, b: ref/string, c: ref/string) -> bool => b == c\nlet a = (\"first\", 0)\nmatch \"other\"@move\n    let s if three(a.0, s, \"other\") => Console.writeLine(\"ok\")\n    _ => Console.writeLine(\"bad\")" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void SharedElementsExecute(string name, string source)
        => ScalarEmissionTest.EmitFixture("ElementBorrow" + name, Same + source, "ok\n");

    [Theory]
    [InlineData("==", "first", true)]
    [InlineData("!=", "last", true)]
    [InlineData("<", "last", true)]
    [InlineData("<=", "first", true)]
    [InlineData(">", "aaa", true)]
    [InlineData(">=", "first", true)]
    [InlineData("==", "last", false)]
    [InlineData("!=", "first", false)]
    [InlineData("<", "first", false)]
    [InlineData("<=", "aaa", false)]
    [InlineData(">", "first", false)]
    [InlineData(">=", "last", false)]
    public void AllComparisonsInspectTheHandle(string op, string right, bool expected)
    {
        var source = $"let a = (\"first\", \"{right}\")\nif (a.0 {op} a.1) == {(expected ? "true" : "false")} => Console.writeLine(\"ok\")\nlet whole = a@move";
        ScalarEmissionTest.EmitFixture("ElementBorrowCompare" + Array.IndexOf(new[] { "==", "!=", "<", "<=", ">", ">=" }, op) + expected, source, "ok\n");
    }

    [Theory]
    [InlineData("func take(a: string) -> string => a@move\nlet a = (\"first\", 0)\nlet equal = a.0 == take(a.0@move)")]
    [InlineData("func inspect(a: ref/string, b: string) => ()\nlet a = (\"first\", 0)\ninspect(a.0, a.0@move)")]
    [InlineData("func inspect(a: ref/string, b: (string, i32)) => ()\nlet a = (\"first\", 0)\ninspect(a.0, a@move)")]
    [InlineData("func inspect(a: ref/string, b: ()) => ()\nvar a = (\"first\", 0)\ninspect(a.0, a.0 = \"new\")")]
    [InlineData("func inspect(a: ref/string, b: ()) => ()\nvar a = (\"first\", 0)\ninspect(a.0, a = (\"new\", 1))")]
    [InlineData("var a = ((\"first\", \"last\"), 0)\nlet equal = a.0.0 == (label work: do\n    a.0 = (\"new\", \"last\")\n    exit to work \"first\"\n)")]
    [InlineData("var a: [2 of string] = [\"first\", \"last\"]\nvar i: isize = 1\nlet equal = a[0] == (label work: do\n    a[i] = \"new\"\n    exit to work \"first\"\n)")]
    [InlineData("var a = (\"first\", 0)\nlet equal = a.0 == (label work: do\n    defer => a.0 = \"new\"\n    exit to work \"first\"\n)")]
    [InlineData("var a = (\"first\", 0)\nlet equal = a.0 == (label work: do\n    let nested = same(a.0, a.0)\n    a.0 = \"new\"\n    exit to work \"first\"\n)")]
    [InlineData("func inspect(a: ref/string, b: bool) => ()\nvar a = (\"first\", 0)\ninspect(a.0, (label work: do\n    let nested = same(a.0, a.0)\n    a.0 = \"new\"\n    exit to work nested\n))")]
    [InlineData("var a: (string, [1 of i32]) = (\"first\", [0])\nlet x = a.1[(label work: do\n    let equal = same(a.0, a.0)\n    a.0 = \"new\"\n    exit to work 0\n)]")]
    public void OverlappingLoansAreRejected(string source)
        => Reject(Same + source, OwnershipFailure.ComparisonLoanConflict);

    [Theory]
    [InlineData("let a = (\"first\", 0)\nlet taken = a.0@move\nlet equal = a.0 == \"first\"")]
    [InlineData("let a = (\"first\", 0)\nlet taken = a.0@move\nlet equal = same(a.0, a.0)")]
    [InlineData("let a = ((\"first\", \"last\"), 0)\nlet taken = a.0@move\nlet equal = same(a.0.0, a.0.0)")]
    [InlineData("func f(take: bool)\n    let a = (\"first\", 0)\n    if take\n        let taken = a.0@move\n    let equal = same(a.0, a.0)")]
    public void MissingElementsAreRejected(string source)
        => Reject(Same + source, OwnershipFailure.PossiblyMovedUse);

    [Theory]
    [InlineData("var a: (string, i32)\nlet equal = same(a.0, a.0)")]
    [InlineData("var a: [1 of string]\nlet equal = a[0] == \"first\"")]
    public void UnconstructedReceiversAreRejected(string source)
        => Reject(Same + source, OwnershipFailure.UninitializedUse);

    // SPEC 3.4.1, 4.6.9, 5.4, 10.2: an owned fixed-array element at a runtime index, and a stored part below an element of an Array,
    // a Slice or such a fixed array, owned or reached through references, is borrowed in place through the element's own borrow:
    // exclusive borrows, receivers and addresses change the element, and shared borrows, arguments and comparisons read it, with each
    // selector evaluated once. The path's references decide its authority.
    [Theory]
    [InlineData("RefCopy", Pt + Bump + "func set(xs: uniq/Array<Pt>) => bump(xs[0].n@uniq)\nvar ps: Array<Pt> = [Pt.init(41)]\nset(ps@uniq)\nConsole.writeLine(\"\\(ps[0].n)\")", "42\n")]
    [InlineData("RefCopyRaw", Pt + "func raw(xs: uniq/Array<Pt>)\n    unsafe\n        let p = xs[0].n@raw\n        *p = 42\nvar ps: Array<Pt> = [Pt.init(41)]\nraw(ps@uniq)\nConsole.writeLine(\"\\(ps[0].n)\")", "42\n")]
    [InlineData("RefTupleReceiver", Counter + "func touch(xs: uniq/Array<(Counter, i32)>) => xs[0].0.inc()\nvar ts: Array<(Counter, i32)> = [(Counter.init(41), 0)]\ntouch(ts@uniq)\nConsole.writeLine(\"\\(ts[0].0.value)\")", "42\n")]
    [InlineData("RefCopyShared", SharedCopyPart, "7\n")]
    [InlineData("RefFixed", Pt + Bump + "func set(ys: uniq/[2 of Pt], i: isize) => bump(ys[i].n@uniq)\nvar ps: [2 of Pt] = [Pt.init(41), Pt.init(1)]\nset(ps@uniq, 0)\nConsole.writeLine(\"\\(ps[0].n)\")", "42\n")]
    [InlineData("SlicePart", Pt + "func show(v: ref/i32) => Console.writeLine(\"\\(v@follow)\")\nvar ps: Array<Pt> = [Pt.init(41)]\nlet s = ps[..]\nshow(s[0].n)", "41\n")]
    [InlineData("RefBankRead", Item + Bank + "func first(b: ref/Bank) -> i32 => b.items[0].n\nlet bank = Bank.init([Item.init(\"a\", 7)])\nConsole.writeLine(\"\\(first(bank@ref))\")", "7\n")]
    [InlineData("UniqBankWrite", Item + Bank + Bump + "func set(b: uniq/Bank)\n    b.items[0].n = 3\n    bump(b.items[0].n@uniq)\nvar bank = Bank.init([Item.init(\"a\", 7)])\nset(bank@uniq)\nConsole.writeLine(\"\\(bank.items[0].n)\")", "4\n")]
    [InlineData("GenericPart", "func set<T>(target: uniq/T, value: T)\n    T is Owned\n    target@follow = value@move\nfunc put<T>(xs: uniq/Array<(T, i32)>, value: T)\n    T is Owned\n    set(xs[0].0@uniq, value@move)\nvar ts: Array<(string, i32)> = [(\"a\", 2)]\nput(ts@uniq, \"b\")\nConsole.writeLine(ts[0].0)", "b\n")]
    [InlineData("FixedUniq", Bump + "var ys: [2 of i32] = [41, 5]\nlet i: isize = 0\nbump(ys[i]@uniq)\nConsole.writeLine(\"\\(ys[0])\")", "42\n")]
    [InlineData("FixedPart", Item + Bump + "var ys: [2 of Item] = [Item.init(\"a\", 41), Item.init(\"b\", 2)]\nlet i: isize = 0\nbump(ys[i].n@uniq)\nConsole.writeLine(\"\\(ys[0].n)\")", "42\n")]
    [InlineData("FixedName", Item + "var ys: [2 of Item] = [Item.init(\"a\", 1), Item.init(\"b\", 2)]\nlet i: isize = 1\nConsole.writeLine(ys[i].name)\nrequire ys[i].name == \"b\" else => $abort(\"part\")", "b\n")]
    [InlineData("FixedString", Same + "let a: [2 of string] = [\"first\", \"second\"]\nvar i: isize = 0\nConsole.writeLine(\"\\(same(a[i], a[0]))\")\nrequire a[0 + 1] == \"second\" else => $abort(\"computed\")\nConsole.writeLine(a[i + 1])", "true\nsecond\n")]
    [InlineData("Uniq", Item + Bump + "var xs: Array<Item> = [Item.init(\"a\", 41)]\nbump(xs[0].n@uniq)\nConsole.writeLine(\"\\(xs[0].n)\")", "42\n")]
    [InlineData("Raw", Item + "var xs: Array<Item> = [Item.init(\"a\", 41)]\nunsafe\n    let p = xs[0].n@raw\n    *p = 42\nConsole.writeLine(\"\\(xs[0].n)\")", "42\n")]
    [InlineData("CopyReceiver", Counter + "struct Box\n    public var c: Counter\n    public init(c: Counter) => self.c = c\nvar xs: Array<Box> = [Box.init(Counter.init(41))]\nxs[0].c.inc()\nConsole.writeLine(\"\\(xs[0].c.value)\")", "42\n")]
    [InlineData("TuplePart", Bump + "var ts: Array<(string, i32)> = [(\"a\", 41)]\nbump(ts[0].1@uniq)\nConsole.writeLine(\"\\(ts[0].1)\")", "42\n")]
    [InlineData("Argument", Item + "var xs: Array<Item> = [Item.init(\"a\", 1)]\nConsole.writeLine(xs[0].name)", "a\n")]
    [InlineData("Retained", Item + "var xs: Array<Item> = [Item.init(\"a\", 1)]\nlet name = xs[0].name@ref\nConsole.writeLine(name)\nxs.append(Item.init(\"b\", 2))\nxs[0].n = 9\nConsole.writeLine(\"\\(xs.length) \\(xs[0].n)\")", "a\n2 9\n")]
    [InlineData("Interpolation", Item + "var xs: Array<Item> = [Item.init(\"a\", 1)]\nConsole.writeLine(\"\\(xs[0].name)!\")", "a!\n")]
    [InlineData("Receiver", Inner + "var xs: Array<Inner> = [Inner.init(\"a\")]\nxs[0].rename()\nxs[0].show()", "renamed\n")]
    [InlineData("Consumers", Inner + "var xs: Array<Inner> = [Inner.init(\"a\")]\nxs[0].items.append(3)\nfor v in xs[0].items\n    Console.writeLine(\"\\(v)\")\nConsole.writeLine(\"\\(xs[0].items.length) \\(xs[0].items[1])\")", "1\n3\n2 3\n")]
    [InlineData("Nested", Bump + "struct Row\n    public var cells: Array<i32>\n    public init(cells: Array<i32>) => self.cells = cells@move\nvar rows: Array<Row> = [Row.init([1, 2]), Row.init([3, 4])]\nlet i: isize = 1\nlet j: isize = 0\nbump(rows[i].cells[j]@uniq)\nConsole.writeLine(\"\\(rows[1].cells[0])\")", "4\n")]
    [InlineData("OwnedPath", Item + Bump + "struct Bank\n    public var items: Array<Item>\n    public init(items: Array<Item>) => self.items = items@move\nvar bank = Bank.init([Item.init(\"a\", 41)])\nbump(bank.items[0].n@uniq)\nConsole.writeLine(\"\\(bank.items[0].n)\")", "42\n")]
    [InlineData("SelectorOnce", Item + Bump + "var calls = 0\nfunc pick(c: uniq/i32) -> isize\n    c@follow += 1\n    return 0\nvar xs: Array<Item> = [Item.init(\"a\", 41)]\nbump(xs[pick(calls@uniq)].n@uniq)\nConsole.writeLine(\"\\(xs[0].n) \\(calls)\")", "42 1\n")]
    public void SelectedElementPlacesAreBorrowedInPlace(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("ElementBorrowPlace" + name, source, stdout);

    // SPEC 4.6.1: the element's bounds are checked before it or its part is borrowed.
    [Theory]
    [InlineData("Dynamic", Item + Bump + "var xs: Array<Item> = [Item.init(\"a\", 41)]\nlet i: isize = 3\nbump(xs[i].n@uniq)\nConsole.writeLine(\"unreachable\")", "xs[i]")]
    [InlineData("Fixed", Bump + "var ys: [2 of i32] = [41, 5]\nlet i: isize = 3\nbump(ys[i]@uniq)\nConsole.writeLine(\"unreachable\")", "ys[i]")]
    public void ASelectedElementBorrowChecksItsBounds(string name, string source, string selection)
    {
        var at = source.IndexOf(selection, StringComparison.Ordinal);
        var line = source.AsSpan(0, at).Count('\n') + 1;
        var column = at - source.LastIndexOf('\n', at);
        ScalarEmissionTest.EmitFixture("ElementBorrowPlaceBounds" + name, source, string.Empty, 1, $"Hello.kimi:{line}:{column}: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");
    }

    // SPEC 3.4, 15.6.2: a shared reference on the path grants Read only, whatever the element's own Type.
    [Theory]
    [InlineData("func set(b: ref/Bank) => b.items[0].n = 3")]
    [InlineData("func set(b: ref/Bank) => bump(b.items[0].n@uniq)")]
    [InlineData("func set(xs: ref/Array<Pt>) => bump(xs[0].n@uniq)")]
    public void APartThroughASharedReferenceStaysReadOnly(string source)
    {
        var c = MinimalEmissionTest.Analyze(Item + Pt + Bank + Bump + source + "\npublic func main() => ()");
        Assert.Contains(c.Binding.Issues, static x => x.Code == Kimi.DiagnosticCode.SharedPathAccess_Kd);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    // SPEC 3.4.1, 15.1.3: a runtime index proves no initialized element, so a fixed array with a moved element lends none.
    [Fact]
    public void ARuntimeSelectionOfAPartiallyMovedFixedArrayIsRejected()
    {
        var c = MinimalEmissionTest.Analyze(Item + Bump + "var ys: [2 of Item] = [Item.init(\"a\", 41), Item.init(\"b\", 2)]\nlet taken = ys[1]@move\nlet i: isize = 0\nbump(ys[i].n@uniq)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.PossiblyMovedUse);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    // SPEC 15.6.2, 15.6.3, 15.6.7: a part borrow keeps its Array borrowed while it is live: resizing, clearing, moving or replacing
    // the Array, or writing the part, is one conflict.
    [Theory]
    [InlineData("let name = xs[0].name@ref\nxs.append(Item.init(\"b\", 2))\nConsole.writeLine(name)", "CallActivationConflict_Kd")]
    [InlineData("let name = xs[0].name@ref\nxs.clear()\nConsole.writeLine(name)", "CallActivationConflict_Kd")]
    [InlineData("let name = xs[0].name@ref\nlet ys = xs@move\nConsole.writeLine(name)", "ComparisonLoanConflict_Kd")]
    [InlineData("let name = xs[0].name@ref\nxs[0] = Item.init(\"c\", 3)\nConsole.writeLine(name)", "ComparisonLoanConflict_Kd")]
    [InlineData("let n = xs[0].n@ref\nxs[0].n = 5\nConsole.writeLine(\"\\(n@follow)\")", "ComparisonLoanConflict_Kd")]
    public void APartBorrowKeepsItsArrayBorrowed(string use, string code)
    {
        var c = MinimalEmissionTest.Analyze(Item + "var xs: Array<Item> = [Item.init(\"a\", 1), Item.init(\"b\", 2)]\n" + use);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(code, Assert.Single(c.Ownership.Issues).Code.ToString());
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    // SPEC 3.4.1, 10.2, 15.6.2: element routes compose: nested Field, Tuple and Array projections select one Place, a stored reference
    // is lent as its referent or borrowed as its slot, borrows of different Arrays keep separate Loans, and an ordinary read of a part
    // stays an independent Copy.
    [Theory]
    [InlineData("Nested", Bump + "struct Inner\n    public var items: Array<i32>\n    public var t: (i32, i32)\n    public init() => (self.items = [1, 2], self.t = (3, 4))\nstruct Outer\n    public var inner: Inner\n    public init() => self.inner = Inner.init()\nvar xs: Array<Outer> = [Outer.init()]\nlet i: isize = 0\nlet j: isize = 1\nbump(xs[i].inner.items[j]@uniq)\nbump(xs[i].inner.t.1@uniq)\nConsole.writeLine(\"\\(xs[0].inner.items[1]) \\(xs[0].inner.t.1)\")", "3 5\n")]
    [InlineData("StoredReference", "struct Cell {source}\n    public var item: ref/i32 during source\n    public init(item: ref/i32 during source) => self.item = item\nfunc show(v: ref/i32) => Console.writeLine(\"\\(v@follow)\")\nlet n = 7\nvar cs: Array<Cell> = [Cell.init(n@ref)]\nshow(cs[0].item)\nlet slot = cs[0].item@ref\nshow(slot@follow)", "7\n7\n")]
    [InlineData("SeparateLoans", Item + "func show(v: ref/i32) => Console.writeLine(\"\\(v@follow)\")\nvar xs: Array<Item> = [Item.init(\"a\", 1)]\nvar ys: Array<Item> = [Item.init(\"b\", 2)]\nlet a = xs[0].n@ref\nlet b = ys[0].n@ref\nshow(b)\nys[0].n = 5\nshow(a)\nConsole.writeLine(\"\\(ys[0].n)\")", "2\n1\n5\n")]
    [InlineData("ValueRead", Item + "var xs: Array<Item> = [Item.init(\"a\", 41)]\nlet n = xs[0].n\nxs[0].n = 5\nConsole.writeLine(\"\\(n) \\(xs[0].n)\")", "41 5\n")]
    public void ElementRoutesCompose(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("ElementBorrowCompose" + name, source, stdout);

    // SPEC 15.6.3, 15.6.7: a part borrow through a reference keeps the referenced Array borrowed, and a local Array cannot be cleared
    // while a borrow of its part is still read.
    [Theory]
    [InlineData("func f(b: uniq/Bank)\n    let r = b.items[0].n@ref\n    b.items.clear()\n    Console.writeLine(\"\\(r@follow)\")")]
    [InlineData("func g() -> i32\n    var xs: Array<Item> = [Item.init(\"a\", 1)]\n    let r = xs[0].n@ref\n    xs.clear()\n    return r@follow")]
    public void ARetainedPartBorrowBlocksItsArray(string source)
    {
        var c = MinimalEmissionTest.Analyze(Item + Bank + source + "\npublic func main() => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal("CallActivationConflict_Kd", Assert.Single(c.Ownership.Issues).Code.ToString());
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    // SPEC 15.6.2: element indices prove no disjointness, so exclusive borrows of parts of two elements conflict exactly as exclusive
    // borrows of the two elements do.
    [Fact]
    public void ExclusivePartBorrowsOfTwoElementsConflictAsTheElementsDo()
    {
        static string[] Codes(string use)
        {
            var c = MinimalEmissionTest.Analyze(Item + "var xs: Array<Item> = [Item.init(\"a\", 1), Item.init(\"b\", 2)]\n" + use);
            Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
            return c.Ownership.Issues.Select(static x => x.Code.ToString()).ToArray();
        }

        var parts = Codes("func two(a: uniq/i32, b: uniq/i32) => a@follow += b@follow\ntwo(xs[0].n@uniq, xs[1].n@uniq)");
        Assert.Contains("CallActivationConflict_Kd", parts);
        Assert.Equal(Codes("func two(a: uniq/Item, b: uniq/Item) => ()\ntwo(xs[0]@uniq, xs[1]@uniq)"), parts);
    }

    // SPEC 3.4, 10.2: spellings that select the same Place have one meaning: a part through a stepwise element borrow (E1), the same
    // part of a local struct (E2), a runtime and a literal fixed-array index of the same element (E3), the same path through a
    // reference and on its owner (E4), and an adapted or explicit shared borrow (E5) agree in outcome and output.
    [Theory]
    [InlineData("PathRead", "func first(b: ref/Bank) -> i32 => b.items[0].n\nlet bank = Bank.init([Item.init(\"a\", 41)])\nConsole.writeLine(\"\\(first(bank@ref))\")", "let bank = Bank.init([Item.init(\"a\", 41)])\nConsole.writeLine(\"\\(bank.items[0].n)\")", "41\n")]
    [InlineData("PathWrite", "func set(b: uniq/Bank) => bump(b.items[0].n@uniq)\nvar bank = Bank.init([Item.init(\"a\", 41)])\nset(bank@uniq)\nConsole.writeLine(\"\\(bank.items[0].n)\")", "var bank = Bank.init([Item.init(\"a\", 41)])\nbump(bank.items[0].n@uniq)\nConsole.writeLine(\"\\(bank.items[0].n)\")", "42\n")]
    [InlineData("Selector", "bump(ys[i].n@uniq)\nConsole.writeLine(\"\\(ys[0].n)\")", "bump(ys[0].n@uniq)\nConsole.writeLine(\"\\(ys[0].n)\")", "42\n")]
    [InlineData("SelectorShared", "show(ys[i].n)\nConsole.writeLine(ys[i].name)", "show(ys[0].n)\nConsole.writeLine(ys[0].name)", "41\na\n")]
    [InlineData("StepUniq", "bump(xs[0].n@uniq)\nConsole.writeLine(\"\\(xs[0].n)\")", "let e = xs[0]@uniq\nbump(e.n@uniq)\nConsole.writeLine(\"\\(xs[0].n)\")", "42\n")]
    [InlineData("StepRetained", "let n = xs[0].n@ref\nxs[0].n = 5\nshow(n)", "let e = xs[0]@ref\nlet n = e.n@ref\nxs[0].n = 5\nshow(n)", null)]
    [InlineData("Storage", "bump(xs[0].n@uniq)\nConsole.writeLine(\"\\(xs[0].n)\")", "var s = Item.init(\"a\", 41)\nbump(s.n@uniq)\nConsole.writeLine(\"\\(s.n)\")", "42\n")]
    [InlineData("Adaptation", "show(xs[0].n)", "show(xs[0].n@ref)", "41\n")]
    public void EquivalentPlaceSpellingsAgree(string family, string first, string second, string? stdout)
    {
        const string Prefix = Item + Bank + Bump + "func show(v: ref/i32) => Console.writeLine(\"\\(v@follow)\")\nvar xs: Array<Item> = [Item.init(\"a\", 41)]\n" +
            "var ys: [2 of Item] = [Item.init(\"a\", 41), Item.init(\"b\", 2)]\nlet i: isize = 0\n";
        var outcomes = new[] { first, second }.Select(static use =>
        {
            var c = MinimalEmissionTest.Analyze(Prefix + use);
            return (Failures: string.Join(",", c.Ownership.Issues.Select(static x => x.Failure)), Emitted: c.Emission.WriteIr(TextWriter.Null, out _));
        }).ToArray();
        Assert.Equal(outcomes[0], outcomes[1]);
        Assert.Equal(stdout is not null, outcomes[0].Emitted);
        if (stdout is not null)
        {
            ScalarEmissionTest.EmitFixture("ElementBorrowEquivalent" + family + "A", Prefix + first, stdout);
            ScalarEmissionTest.EmitFixture("ElementBorrowEquivalent" + family + "B", Prefix + second, stdout);
        }
    }

    // SPEC 3.4, 5.4, 10.2: emission never admits a borrow of a temporary read from its own Place selection, in any mode.
    [Theory]
    [InlineData(LoanRequirement.Ref)]
    [InlineData(LoanRequirement.Uniq)]
    public void ABorrowOfATemporaryReadOfItsSelectionFailsBeforeOutputAndRecovers(LoanRequirement mode)
    {
        var c = MinimalEmissionTest.Analyze(SharedCopyPart);
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies.Single(static x => x.Operations.Any(static o => o.Kind == OwnershipOperationKind.Borrow && o.Source is Kimi.Compiler.Parsing.MemberAccessKoto));
        var borrow = Enumerable.Range(0, body.Operations.Count).Single(i => body.Operations[i] is { Kind: OwnershipOperationKind.Borrow, Source: Kimi.Compiler.Parsing.MemberAccessKoto });
        var part = body.Operations[borrow];
        body.PlaceStorage[part.Place] = body.Places[part.Place] with { Source = part.Source, Type = BoundType.I32 };
        body.OperationStorage[borrow] = part with { LoanMode = mode };

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }

    [Theory]
    [InlineData("Comparison", "func f() -> string\n    var a = (\"held\", \"sibling\")\n    defer => a.0 = \"new\"\n    a.0 == (return \"ok\")\n    return \"bad\"\nConsole.writeLine(f())", "held=1;sibling=1;new=1;ok=1;bad=0", new[] { 0, 1, 2, 3 })]
    [InlineData("Argument", "func inspect(a: ref/string, b: bool) => ()\nfunc f() -> string\n    var a = (\"held\", \"sibling\")\n    defer => a.0 = \"new\"\n    inspect(a.0, (return \"ok\"))\n    return \"bad\"\nConsole.writeLine(f())", "held=1;sibling=1;new=1;ok=1;bad=0", new[] { 0, 1, 2, 3 })]
    [InlineData("ReturnResult", "func inspect(a: ref/string) -> string\n    defer\n        if a == a => Console.writeLine(\"cleanup\")\n    return \"ok\"\nlet a = (\"held\", \"sibling\")\nConsole.writeLine(inspect(a.0))", "held=1;sibling=1;cleanup=1;ok=1", new[] { 2, 3, 1, 0 })]
    public void CleanupRetainsResponsibilityAndOrdering(string name, string source, string destructions, int[] order)
    {
        var stdout = name == "ReturnResult" ? "cleanup\nok\n" : "ok\n";
        var ir = ScalarEmissionTest.EmitFixture("ElementBorrowCleanup" + name, source, stdout);
        StringEmissionTest.WriteAuditedFixture("ElementBorrowCleanup" + name, source, ir, stdout, destructions, order: order);
    }

    [Fact]
    public void GuardCandidateKeepsItsOwnAddressUnderAnElementLoan()
    {
        var c = MinimalEmissionTest.Analyze(Same + "let a = (\"first\", 0)\nmatch \"other\"@move\n    let s if same(a.0, s) => ()\n    _ => ()");
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var body = c.Ownership.Bodies[0];
        var call = Assert.Single(body.CallLoans).Call;
        var function = module.GetFunction(0);
        var instruction = Assert.Single(function.Instructions, x => x.Operation == call && x.Opcode == EmissionOpcode.Call);
        Assert.Equal(EmissionOperandKind.ElementAddress, function.Operands[instruction.OperandStart].Kind);
        var candidate = function.Operands[instruction.OperandStart + 1];
        Assert.Equal(EmissionOperandKind.SlotAddress, candidate.Kind);
        Assert.Equal(Assert.Single(body.Matches).Subject, candidate.Value);
    }

    [Fact]
    public void ComparisonDoesNotAcquireOrDestroyItsElement()
    {
        const string Source = "let a = (\"held\", \"sibling\")\nlet same = a.0 == a.0\nif same => Console.writeLine(\"ok\")";
        var ir = ScalarEmissionTest.EmitFixture("ElementBorrowResponsibility", Source, "ok\n");
        StringEmissionTest.WriteAuditedFixture("ElementBorrowResponsibility", Source, ir, "ok\n", "held=1;sibling=1;ok=1", order: [2, 1, 0]);
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.TryPrepare(out var module, out var error), error);
        var body = c.Ownership.Bodies[0];
        Assert.DoesNotContain(body.Places, p => p.Kind == OwnershipPlaceKind.Temporary && p.Source is Kimi.Compiler.Parsing.MemberAccessKoto);
        Assert.Equal(0, body.MovePathCount);
        var comparison = Assert.Single(module.GetFunction(0).Instructions, x => x.Opcode == EmissionOpcode.StringEquals);
        Assert.All(module.GetFunction(0).Operands.Skip(comparison.OperandStart).Take(2), x => Assert.Equal(EmissionOperandKind.ElementAddress, x.Kind));
    }

    [Fact]
    public void LaterBoundsAbortDoesNotRunCleanup()
    {
        const string Source = "func inspect(a: ref/string, b: i32) => ()\nlet a = (\"held\", \"sibling\")\nlet empty: [0 of i32] = []\ndefer => Console.writeLine(\"bad\")\ninspect(a.0, empty[0])";
        const string Error = "Hello.kimi:5:14: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n";
        var ir = ScalarEmissionTest.EmitFixture("ElementBorrowBounds", Source, string.Empty, 1, Error);
        StringEmissionTest.WriteAuditedFixture("ElementBorrowBounds", Source, ir, string.Empty, "held=0;sibling=0;bad=0", 1, Error);
    }

    [Theory]
    [InlineData("Call", "func forever(a: ref/string) -> Never\n    loop => ()\nlet a = (\"held\", 0)\nforever(a.0)")]
    [InlineData("Cleanup", "func f() -> string\n    let a = (\"held\", 0)\n    defer => loop => ()\n    a.0 == (return \"ok\")\n    return \"bad\"\nConsole.writeLine(f())")]
    public void NonterminationCannotReleaseOrDeliverNormally(string name, string source)
        => ScalarEmissionTest.EmitFixture("ElementBorrowDivergent" + name, source, string.Empty, timeoutMilliseconds: 300);

    // SPEC 3.4.1, 4.6.9: emission accepts a part borrow only through its element's own borrow of the element's stored Type, and an
    // exclusive fixed-array element only in exclusive mode; a corrupted route record fails before output and a fresh analysis recovers.
    [Theory]
    [InlineData("receiver")]
    [InlineData("base")]
    [InlineData("mode")]
    public void CorruptedElementRoutesFailBeforeOutputAndRecover(string defect)
    {
        var c = MinimalEmissionTest.Analyze(Item + Bump + "var xs: Array<Item> = [Item.init(\"a\", 41)]\nbump(xs[0].n@uniq)\nvar ys: [2 of i32] = [1, 2]\nlet i: isize = 0\nbump(ys[i]@uniq)");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies.Single(static x => x.Operations.Any(static o => o is { Kind: OwnershipOperationKind.Borrow, Source: Kimi.Compiler.Parsing.MemberAccessKoto }));
        var part = Enumerable.Range(0, body.Operations.Count).Single(i => body.Operations[i] is { Kind: OwnershipOperationKind.Borrow, Source: Kimi.Compiler.Parsing.MemberAccessKoto });
        var fixedElement = Enumerable.Range(0, body.Operations.Count).Single(i => body.Operations[i] is { Kind: OwnershipOperationKind.Borrow, LoanMode: LoanRequirement.Uniq, Source: Kimi.Compiler.Parsing.IndexKoto { Left.BoundType.Kind: BoundTypeKind.FixedArray } });
        var element = body.ValueOperands[body.Values[part].Start];
        switch (defect)
        {
            case "receiver":
                var receiver = body.Places[body.Operations[part].Place];
                body.PlaceStorage[body.Operations[part].Place] = receiver with { Type = c.Binding.Reference(Kimi.Compiler.Parsing.SemanticsKind.Uniq, BoundType.I32, receiver.Type.Origin) };
                break;
            case "base": body.OperationStorage[element] = body.Operations[element] with { Source = body.Operations[part].Source }; break;
            case "mode": body.OperationStorage[fixedElement] = body.Operations[fixedElement] with { LoanMode = LoanRequirement.Ref }; break;
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }

    // SPEC 15.6.3: an edit that moves a part borrow to another Array removes its conflict, and the reverse edit restores it; each
    // analysis follows only the current source.
    [Fact]
    public void EditedPartBorrowsReanalyzeFromTheCurrentSource()
    {
        var c = MinimalEmissionTest.Analyze(Item + Bump + "var xs: Array<Item> = [Item.init(\"a\", 41)]\nvar ys: Array<Item> = [Item.init(\"b\", 1)]\nlet r = xs[0].n@ref\nbump(xs[0].n@uniq)\nConsole.writeLine(\"\\(r@follow)\")\nlet other = ys[0].n");
        Assert.False(c.Ownership.Result.IsVerified);
        var conversion = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<Kimi.Compiler.Parsing.ConversionKoto>().Single(static x => x.ToString() == "xs[0].n@uniq");
        var borrowed = conversion.Left;
        var read = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<Kimi.Compiler.Parsing.MemberAccessKoto>().Single(static x => x.ToString() == "ys[0].n");
        var declaration = read.Parent!;
        for (var i = 0; i < 3; i++)
        {
            // Swap the borrowed part with the other Array's read: the exclusive borrow then names ys, and the Loan on xs no longer meets it.
            Assert.True(KotoHelper.Replace(conversion, borrowed, read) && KotoHelper.Replace(declaration, read, borrowed));
            Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
            Assert.True(KotoHelper.Replace(conversion, read, borrowed) && KotoHelper.Replace(declaration, borrowed, read));
            Assert.True(c.Bind().IsComplete);
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.False(c.Ownership.Analyze().IsVerified);
            Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("operation")]
    [InlineData("source")]
    [InlineData("selector")]
    [InlineData("dynamic")]
    [InlineData("loan")]
    [InlineData("mode")]
    [InlineData("parent")]
    [InlineData("release")]
    [InlineData("move")]
    [InlineData("type")]
    [InlineData("inspection")]
    public void CorruptedBorrowPlansFailBeforeOutputAndRecover(string defect)
    {
        var c = MinimalEmissionTest.Analyze(Same + "let a: [2 of string] = [\"held\", \"sibling\"]\nlet equal = same(a[0], a[0])\nlet inspected = a[1] == a[1]");
        Assert.True(c.Emission.Validate(out var error), error);
        var body = c.Ownership.Bodies[0];
        var plan = body.Projections[0];
        var loan = body.LoanStates[plan.Borrow];
        switch (defect)
        {
            case "missing": body.Projections[0] = plan with { Borrow = -1 }; break;
            case "operation": body.OperationStorage[plan.Borrow] = body.Operations[plan.Borrow] with { Projection = 1 }; break;
            case "source": body.OperationStorage[plan.Borrow] = body.Operations[plan.Borrow] with { Source = body.Operations[body.Projections[1].Borrow].Source }; break;
            case "selector": body.Projections[0] = plan with { Selector = 1 }; break;
            case "dynamic": body.Projections[0] = plan with { Path = -1 }; break;
            case "loan": body.ComparisonLoans[loan] = body.ComparisonLoans[loan] with { Projection = 1 }; break;
            case "mode": body.ComparisonLoans[loan] = body.ComparisonLoans[loan] with { Mode = LoanRequirement.Uniq }; break;
            case "parent": body.ComparisonLoans[loan] = body.ComparisonLoans[loan] with { Parent = plan.Loan }; break;
            case "release": body.LoanStates[plan.Borrow] = -1; break;
            case "move": body.OperationStorage[plan.Borrow] = body.Operations[plan.Borrow] with { Acquisition = AcquisitionKind.Move }; break;
            case "type":
                var place = body.Operations[plan.Borrow].Input;
                body.PlaceStorage[place] = body.Places[place] with { Type = BoundType.I32 };
                break;
            case "inspection": body.StringComparisons[0] = body.StringComparisons[0] with { LeftLoan = -1 }; break;
        }

        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), error);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmBindingAndBorrowEmissionAllocateNothing()
    {
        const string Source = Same + "func f(take: bool)\n    var a: (string, [1 of string], i32) = (\"held\", [\"sibling\"], 40)\n    if take\n        let moved = a.1[0]@move\n    defer\n        if same(a.0, a.0) => a.2 += 1\n    a.2 += if a.0 == \"held\" => 1 else => 0\n    a.1[0] = \"new\"\nf(true)\nf(false)";
        var c = MinimalEmissionTest.Analyze(Source);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        var success = true;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 128; i++)
        {
            success &= c.Bind().IsComplete;
        }

        var bindingBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        c.Binding.CheckStartup(OutputKind.Application);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 128; i++)
        {
            success &= c.Ownership.Analyze().IsVerified;
            success &= c.Emission.WriteIr(TextWriter.Null, out _);
        }

        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(success);
        Assert.Equal(0, bindingBytes);
        Assert.Equal(0, bytes);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmRouteClassificationAllocatesNothing()
    {
        var shared = MinimalEmissionTest.Analyze(SharedCopyPart);
        var referenced = MinimalEmissionTest.Analyze(Item + Pt + Bank + Bump + "func set(xs: uniq/Array<Pt>) => bump(xs[0].n@uniq)\nfunc first(b: ref/Bank) -> i32 => b.items[0].n\n" +
            "var ps: Array<Pt> = [Pt.init(41)]\nset(ps@uniq)\nlet bank = Bank.init([Item.init(\"a\", 7)])\nlet n = first(bank@ref)");
        var parts = MinimalEmissionTest.Analyze(Item + Bump + "var xs: Array<Item> = [Item.init(\"a\", 41)]\nbump(xs[0].n@uniq)\nlet name = xs[0].name@ref\nConsole.writeLine(name)\n" +
            "var ys: [2 of Item] = [Item.init(\"a\", 41), Item.init(\"b\", 2)]\nlet i: isize = 0\nbump(ys[i].n@uniq)\nConsole.writeLine(ys[i].name)");
        Assert.True(parts.Emission.WriteIr(TextWriter.Null, out var error), error);
        var success = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            success &= shared.Ownership.Analyze().IsVerified;
            success &= referenced.Ownership.Analyze().IsVerified;
            success &= parts.Ownership.Analyze().IsVerified;
            success &= parts.Emission.WriteIr(TextWriter.Null, out _);
        }));
        Assert.True(success);
    }

    // The element-place workloads of `Benchmark --element-places`: warm Binding, analysis and emission of the value-path controls and
    // the element borrow routes allocate nothing.
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("reads")]
    [InlineData("updates")]
    [InlineData("borrows")]
    [InlineData("paths")]
    public void WarmElementPlaceWorkloadsAllocateNothing(string axis)
    {
        var c = MinimalEmissionTest.Analyze(Verification.VerificationWorkloads.ElementPlaces(axis, 8));
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid);
    }

    private static void Reject(string source, OwnershipFailure failure)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == failure);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }
}
