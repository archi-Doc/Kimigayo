// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>The drop matrix (R5 C4s; SPEC 16.2-16.4, DECISIONS R4-M6a, R4-M6b; package C 3.6): programs whose values are each destroyed
/// exactly once, run natively with exact allocation counts (<c>NativeAllocationAudit</c>), each fixing the
/// verdict its body's MIR holds. A row's name begins with its class, the latest verdict it exercises: Keep (with Elide, written by C4a), Flag
/// (C4b) or Open (C4c); so under MIR code generation a row runs once its class's unit lands. They add the forms the other classes' native
/// destruction rows (static-string audits, destructor prints) lack.</summary>
public sealed partial class MirDropTest
{
    // A value whose destruction is seen twice: its destructor prints its name and releases the one byte its constructor allocated, so
    // the audit counts one allocation, one release and one byte per value. It follows each program, so a body the program names comes
    // first among the bodies of that name.
    private const string Counted = "struct Counted\n    let name: string\n    let data: raw/u8\n    public init(name: string)\n" +
        "        self.name = name@move\n        self.data = Raw.allocate<u8>(1)\n    drop\n        Console.writeLine(self.name)\n" +
        "        unsafe => Raw.release(self.data)\nfunc consume(c: Counted) => ()\n";

    // The verdicts in class order; Elide, which writes nothing, belongs to every class.
    private static readonly MirDropVerdict[] Classes = [MirDropVerdict.Keep, MirDropVerdict.Flag, MirDropVerdict.Open];

    // Name, the body whose verdict the row fixes, a pattern of that verdict and its number of matches in the body's annotated lines (Lines),
    // the program before Counted, its output and its number of values.
    public static TheoryData<string, string, string, int, string, string, int> Matrix => new()
    {
        // SPEC 16.3.2: a struct's Fields last to first, a Tuple's and a fixed array's elements by decreasing index, the active Case's payload.
        {
            "KeepNested", "f", @"^drop _1 -> bb\d+ \[keep\]$", 1,
            "struct Bag\n    let one: Counted\n    let pair: (Counted, Counted)\n    let some: Option<Counted>\n    let none: Option<Counted>\n" +
            "    let row: [2 of Counted]\n    public init(one: Counted, pair: (Counted, Counted), some: Option<Counted>, row: [2 of Counted])\n" +
            "        self.one = one@move\n        self.pair = pair@move\n        self.some = some@move\n        self.none = .None\n" +
            "        self.row = row@move\nfunc f()\n    let bag = Bag.init(Counted.init(\"one\"), (Counted.init(\"pair0\"), Counted.init(\"pair1\")), " +
            ".Some(Counted.init(\"some\")), [Counted.init(\"row0\"), Counted.init(\"row1\")])\n    consume(Counted.init(\"moved\"))\n" +
            "    Console.writeLine(\"end\")\nf()\n",
            "moved\nend\nrow1\nrow0\nsome\npair1\npair0\none\n", 7
        },

        // R4-M6b, PD18: a destructor's body destroys its own Fields, last to first, then its base, on each of its two returns; nothing else
        // does. Leaf precedes Base, so its destructor is the first body named drop.
        {
            "KeepLayers", "drop", @"^drop _1\.base1 -> bb\d+ \[keep\]$", 2,
            "struct Leaf : Base\n    let early: bool\n    let x: Counted\n    let y: Counted\n" +
            "    public init(early: bool, x: Counted, y: Counted, b: Counted) : base(b@move)\n        self.early = early\n" +
            "        self.x = x@move\n        self.y = y@move\n    drop\n        Console.writeLine(\"leaf\")\n        if self.early => return\n" +
            "        Console.writeLine(\"late\")\nopen struct Base\n    let b: Counted\n    protected init(b: Counted) => self.b = b@move\n" +
            "    drop => Console.writeLine(\"base\")\nfunc f()\n" +
            "    let late = Leaf.init(false, Counted.init(\"x1\"), Counted.init(\"y1\"), Counted.init(\"b1\"))\n" +
            "    let early = Leaf.init(true, Counted.init(\"x2\"), Counted.init(\"y2\"), Counted.init(\"b2\"))\nf()\n",
            "leaf\ny2\nx2\nbase\nb2\nleaf\nlate\ny1\nx1\nbase\nb1\n", 6
        },

        // Plan 5.6.1: a pair-generic body destroys its value in its owner case and nothing in its reference case.
        {
            "KeepViews", "f", @"\[keep in v0; elide in v1\]", 1,
            "func f<s/T>(value: s/T) -> ()\n    s is owner or ref\n    Console.writeLine(\"hold\")\nfunc run()\n    let kept = Counted.init(\"kept\")\n" +
            "    f(Counted.init(\"owned\"))\n    f(kept@ref)\n    Console.writeLine(\"end\")\nrun()\n",
            "hold\nowned\nhold\nend\nkept\n", 2
        },

        // SPEC 16.2.1: a value moved on some paths is destroyed where its flag is set: an argument (set at the entry), one node two exits
        // share past a defer, and a constructor's Field placed on some paths before its replacement (a local's is StructEmissionTest's
        // ConditionalMove).
        {
            "FlagArgument", "f", @"^flags 1 \[set f0\]$", 1,
            "func f(c: bool, r: Counted)\n    if c => consume(r@move)\n    Console.writeLine(\"end\")\nf(true, Counted.init(\"first\"))\n" +
            "f(false, Counted.init(\"second\"))\n",
            "first\nend\nend\nsecond\n", 2
        },
        {
            "FlagExits", "f", @"^drop _2 -> bb\d+ \[flag f0\]", 1,
            "func f(c: bool) -> i32\n    let r = Counted.init(\"exits\")\n    defer => Console.writeLine(\"defer\")\n    if c\n        consume(r@move)\n" +
            "        return 1\n    return 2\n_ = f(true)\n_ = f(false)\n",
            "exits\ndefer\ndefer\nexits\n", 2
        },
        {
            "FlagField", "init", @"^drop _0\.0 -> bb\d+ \[flag f0\]", 1,
            "struct Slot\n    var item: Counted\n    public init(c: bool)\n        if c => self.item = Counted.init(\"first\")\n" +
            "        self.item = Counted.init(\"second\")\nfunc f(c: bool)\n    let slot = Slot.init(c)\n    Console.writeLine(\"end\")\nf(true)\nf(false)\n",
            "first\nend\nsecond\nend\nsecond\n", 3
        },

        // SPEC 16.2.2: after a Partial Move the remaining parts are destroyed, each child path by its verdict and the other components by
        // the own bit: through a Field, a constant index, a Case of a decomposed Subject, and in a pair-generic body's owner case.
        {
            "OpenField", "f", @"\[open \(\.0 flag f0\)\]", 1,
            "struct Pair\n    public var first: Counted\n    public var second: Counted\n    public init(first: Counted, second: Counted)\n" +
            "        self.first = first@move\n        self.second = second@move\nfunc f(c: bool)\n" +
            "    var p = Pair.init(Counted.init(\"first\"), Counted.init(\"second\"))\n    if c => consume(p.first@move)\n" +
            "    Console.writeLine(\"end\")\nf(true)\nf(false)\n",
            "first\nend\nsecond\nend\nsecond\nfirst\n", 4
        },
        {
            "OpenElement", "f", @"\[open \(\[1\] flag f0\)\]", 1,
            "func f(c: bool)\n    var a: [3 of Counted] = [Counted.init(\"a0\"), Counted.init(\"a1\"), Counted.init(\"a2\")]\n" +
            "    if c => consume(a[1]@move)\n    Console.writeLine(\"end\")\nf(true)\nf(false)\n",
            "a1\nend\na2\na0\nend\na2\na1\na0\n", 6
        },
        {
            "OpenCase", "f", @"\[open \(\(_ as case 0\) open \(\.0 flag f0\)\)\]", 1,
            "enum Packet\n    Data(Counted, Counted)\n    Empty\nfunc f(c: bool, packet: Packet)\n    match packet@move\n        .Data(let r, _)\n" +
            "            if c => consume(r@move)\n            Console.writeLine(\"arm\")\n        _ => ()\n    Console.writeLine(\"end\")\n" +
            "f(true, .Data(Counted.init(\"p0\"), Counted.init(\"p1\")))\nf(false, .Data(Counted.init(\"q0\"), Counted.init(\"q1\")))\nf(true, .Empty)\n",
            "p0\narm\np1\nend\narm\nq0\nq1\nend\nend\n", 4
        },
        {
            "OpenViews", "f", @"\[open \(\.0 elide\) in v0; elide in v1\]", 1,
            "func f<s/T>(pair: (s/T, s/T)) -> s/T\n    s is owner or ref\n    return pair.0@move\nfunc run()\n    let kept = Counted.init(\"kept\")\n" +
            "    let one = f((Counted.init(\"first\"), Counted.init(\"second\")))\n    Console.writeLine(\"after\")\n    let two = f((kept@ref, kept@ref))\n" +
            "    Console.writeLine(\"end\")\nrun()\n",
            "second\nafter\nend\nfirst\nkept\n", 3
        },

        // SPEC 16.3.2 (DECISIONS R5-M100): each layer's remaining parts by its own state: a derived struct whose nested Field's Field and
        // base's Field are moved on one path and the whole on the other, so the own bits of the value, its Field and its base are flags;
        // its own Fields last to first (z, then inner's remaining second), then its base's remaining d.
        {
            "OpenLayers", "f", @"\[open f0 \(\.0 open f1 \(\.0 elide\), \.base1 open f2 \(\.0 elide\)\)\]", 1,
            "struct Pair\n    public var first: Counted\n    public var second: Counted\n    public init(first: Counted, second: Counted)\n" +
            "        self.first = first@move\n        self.second = second@move\nopen struct Base\n    public var b: Counted\n" +
            "    public var d: Counted\n    protected init(b: Counted, d: Counted)\n        self.b = b@move\n        self.d = d@move\n" +
            "struct Outer : Base\n    public var inner: Pair\n    public var z: Counted\n" +
            "    public init(inner: Pair, z: Counted, b: Counted, d: Counted) : base(b@move, d@move)\n        self.inner = inner@move\n" +
            "        self.z = z@move\nfunc whole(o: Outer) => ()\nfunc f(c: bool)\n" +
            "    var o = Outer.init(Pair.init(Counted.init(\"first\"), Counted.init(\"second\")), Counted.init(\"z\"), Counted.init(\"b\"), " +
            "Counted.init(\"d\"))\n    if c\n        whole(o@move)\n    else\n        consume(o.inner.first@move)\n        consume(o.b@move)\n" +
            "    Console.writeLine(\"end\")\nf(true)\nf(false)\n",
            "z\nsecond\nfirst\nd\nb\nend\nfirst\nb\nend\nz\nsecond\nd\n", 10
        },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void EachValueIsDestroyedExactlyOnce(string name, string body, string verdict, int count, string scenario, string stdout, int values)
    {
        var source = scenario + Counted;
        Assert.Equal(count, Lines(source, body).Count(x => Regex.IsMatch(x, verdict)));
        var kinds = Kinds(source);
        var rank = Array.FindIndex(Classes, x => name.StartsWith(x.ToString(), StringComparison.Ordinal));
        Assert.Contains(Classes[rank], kinds);
        Assert.DoesNotContain(kinds, x => Array.IndexOf(Classes, x) > rank);
        NativeAllocationAudit.WriteFixture("MirDrop" + name, source, values, values, values, stdout);
    }

    // The verdicts A2 judged in the document's bodies, each Drop group's rows and their child rows (R4-M6a); Lines gives one body's.
    private static HashSet<MirDropVerdict> Kinds(string source)
    {
        var program = MirBuildTest.Compile(source).BuildMir();
        var (kinds, bodies) = (new HashSet<MirDropVerdict>(), program.Bodies.ToArray());
        for (var i = 0; i < bodies.Length; i++)
        {
            if (bodies[i].Source.Module == 0)
            {
                foreach (var row in program.Drops.RowsOf(i))
                {
                    kinds.Add(row.Verdict);
                }
            }
        }

        return kinds;
    }
}
