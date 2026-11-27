// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>A5's exact lent positions (Z5-F; SPEC 15.6.3, 15.6 Addresses, 5.7 Reading a Field through a dereference; MirBorrows.Lent):
/// a read through a dereference denotes the storage the borrow that formed the reference lent, a root and steps past no dereference,
/// found through the reference's one store, whole Copies and a Tuple's or fixed array's operands, and none where no one borrow forms it.
/// No public layer reads the fact yet (Z5-R1 does), so the rows read it after A5's run of the body.</summary>
public sealed partial class MirBorrowsTest
{
    // F's Packet (data .0, pair .1, arr .2, nest .3), Cell (Some case 0, Two case 1), Outer and Duo, and Mixed2 (mix .0) and Arr2.
    private const string LentShapes = """
        func usep(p: raw/u8) => ()
        struct Packet
            internal var data: raw/u8 during self
            internal var pair: (raw/u8 during self, i32)
            internal var arr: [2 of raw/u8 during self]
            internal var nest: ((raw/u8 during self, i32), i32)
            public init()
                self.data = null
                self.pair = (null, 0)
                self.arr = [null, null]
                self.nest = ((null, 1), 2)
        enum Cell
            Some(raw/u8 during self)
            Two((raw/u8 during self, i32))
            None
        struct Mixed2
            internal var mix: (raw/u8 during self, raw/u8)
            public init() => self.mix = (null, null)
        struct Outer
            internal var inner: Packet
            public init() => self.inner = Packet.init()
        struct Duo
            internal var duo: (Packet, i32)
            public init() => self.duo = (Packet.init(), 1)
        struct Arr2
            internal var ps: [2 of Packet]
            public init() => self.ps = [Packet.init(), Packet.init()]

        """;

    // The read's Place, as the row writes it (the body's local names, `_n` for a temp), and the position A5 publishes for every Place of
    // that text the body's statements use ("none": -1), each body line indented under `func f(fresh: raw/u8)`, `|` separating lines.
    [Theory]
    [InlineData("t6a", "let b = Packet.init()|let e: (raw/u8, i32) = (null, 0)|let r = b@ref|let t = (e@ref, r@follow.pair@ref)|let x = t.1@follow.0", "", "(*t.1).0", "b.1.0")]
    [InlineData("t5a", "let b = Packet.init()|let d = Packet.init()|let rd = d@ref|let t = (b.pair@ref, rd@follow.pair@ref)|let x = t.1@follow.0", "", "(*t.1).0", "d.1.0")]
    [InlineData("t5c", "let b = Packet.init()|let d = Packet.init()|let t = (b.pair@ref, d.pair@ref)|let x = t.1@follow.0", "", "(*t.1).0", "d.1.0")]
    [InlineData("c3", "let b = Packet.init()|let d = Packet.init()|let t = (b.pair@ref, d.pair@ref)|let s = t.1|let x = s@follow.0", "", "(*s).0", "d.1.0")]
    [InlineData("a06t", "let c = Cell.Two((null, 1))|let p: raw/u8 = match c@ref|    Cell.Two(let t) => t@follow.0|    Cell.Some(let q) => q@follow|    Cell.None => null", "", "(*t).0", "(c as 1).0.0")]
    [InlineData("a06q", "let c = Cell.Two((null, 1))|let p: raw/u8 = match c@ref|    Cell.Two(let t) => t@follow.0|    Cell.Some(let q) => q@follow|    Cell.None => null", "", "(*q)", "(c as 0).0")]
    [InlineData("a14", "let b = Mixed2.init()|let r = b.mix@ref|let q = r@follow.1@ref|let y = q@follow", "", "(*q)", "b.0.1")]
    [InlineData("a02", "let b = Packet.init()|let r = b.pair@ref|let x = r@follow.0", "", "(*r).0", "b.1.0")]
    [InlineData("a20", "let b = Packet.init()|let r = b.pair@ref|let r2 = r|let x = r2@follow.0", "", "(*r2).0", "b.1.0")]
    [InlineData("a21", "let b = Packet.init()|let r = b.pair@ref|let q = r@follow@ref|let x = q@follow.0", "", "(*q).0", "b.1.0")]
    [InlineData("a17", "let b = Packet.init()|let p = b.pair@raw|unsafe|    let q = (*p).0@raw|    let x = *q|    usep(x)", "", "(*q)", "b.1.0")]
    [InlineData("t3a", "let b = Packet.init()|let r = b@ref|let p = r@follow.pair@raw|unsafe|    let x = (*p).0|    usep(x)", "", "(*p).0", "b.1.0")]
    [InlineData("t4b", "let b = Packet.init()|let n: i32 = 1|let t = (n@ref, b.pair@raw)|unsafe|    let x = (*t.1).0|    usep(x)", "", "(*t.1).0", "b.1.0")]
    [InlineData("t1a", "var b = Packet.init()|let r = b@uniq|let w = r.pair@uniq|let q = w@follow.0@uniq|let x = q@follow", "", "(*q)", "b.1.0")]
    [InlineData("a05", "let b = Packet.init()|let x: raw/u8 = match b.pair|    (let p, _) => p@follow", "", "(*p)", "b.1.0")]
    [InlineData("d6", "let n: i32 = 1|let m: i32 = 2|let a: [2 of ref/i32] = [n@ref, m@ref]|let x = a[1]@follow", "", "(*a[1])", "m")]
    [InlineData("d7", "let n: i32 = 1|let m: i32 = 2|let a: [2 of ref/i32] = [n@ref, m@ref]|var i: isize = 1|let x = a[i]@follow", "", "(*a[_])", "none")]
    [InlineData("d16", "let b = Packet.init()|let d = Packet.init()|let n: i32 = 1|var t = (n@ref, b.pair@raw)|let p = t@raw|unsafe|    (*p).1 = d.pair@raw|    let x = (*t.1).0|    usep(x)", "", "(*t.1).0", "none")]
    [InlineData("a04", "let b = Packet.init()|let s = b.arr.slice()|let x = s[0]", "", "s[_]", "none")]
    [InlineData("pa", "let b = Packet.init()|let x = g(b@ref)", "func g(r: ref/Packet) -> raw/u8 => r@follow.pair.0\n", "(*r).1.0", "none")]
    [InlineData("tw", "let b = Packet.init()|let d = Packet.init()|var r = b@ref|r = d@ref|let x = r@follow.pair.0", "", "(*r).1.0", "none")]
    [InlineData("pt", "let b = Packet.init()|let d = Packet.init()|var t = (b.pair@ref, d.pair@ref)|t.1 = b.pair@ref|let x = t.1@follow.0", "", "(*t.1).0", "none")]
    public void AReadThroughADereferenceIsPlacedByItsLender(string id, string lines, string declarations, string read, string position)
    {
        var function = id == "pa" ? "g" : "f";
        var source = LentShapes + declarations + "func f(fresh: raw/u8)\n" + string.Concat(lines.Split('|').Select(static x => "    " + x + "\n"));
        Assert.Equal(position, Assert.Single(LentPositions(source, function, read).Positions));
    }

    // A program whose body Copies a borrow of b.pair n times, each Copy from the one before and read through right after it is made
    // (CallableRegionScalingTest.PlacedReadsAreLinearInACopyChain).
    internal static string LentChain(int n)
    {
        var text = new StringBuilder(LentShapes).Append("func f(fresh: raw/u8)\n    let b = Packet.init()\n    let r0 = b.pair@ref\n");
        for (var i = 1; i <= n; i++)
        {
            text.Append($"    let r{i} = r{i - 1}\n    usep(r{i}@follow.0)\n");
        }

        return text.ToString();
    }

    // A program whose body Copies b.pair, a Tuple enclosing a `during self` layer, n times through no dereference, each Copy used right
    // after it is made (CallableRegionScalingTest.EnclosingCopiesScaleWithTheDenseTerm).
    internal static string EnclosingCopyChain(int n)
    {
        var text = new StringBuilder(LentShapes).Append("func f(fresh: raw/u8)\n    let b = Packet.init()\n");
        for (var i = 1; i <= n; i++)
        {
            text.Append($"    let t{i} = b.pair\n    usep(t{i}.0)\n");
        }

        return text.ToString();
    }

    // A program whose body reads whole a Field enclosing a `during self` layer k Tuples deep and uses the layer through the Copy's k steps
    // (CallableRegionScalingTest.ADeepEnclosingReadIsWithinItsWalk).
    internal static string DeepEnclosingRead(int k)
        => LentShapes + $"struct Deep\n    internal var t: {new string('(', k)}raw/u8 during self{string.Concat(Enumerable.Repeat(", i32)", k))}\n" +
            $"    public init() => self.t = {new string('(', k)}null{string.Concat(Enumerable.Repeat(", 1)", k))}\n" +
            $"func f(fresh: raw/u8)\n    let b = Deep.init()\n    let t = b.t\n    usep(t{string.Concat(Enumerable.Repeat(".0", k))})\n";

    // The distinct positions A5 publishes for the Places of the body's statements whose text is the read (every Place where it is null),
    // after A5's run of the body, and the Work those positions took.
    private static (string[] Positions, long Work) LentPositions(string source, string function, string? read)
    {
        var c = MirBuildTest.Compile(source);
        Assert.Empty(c.Binding.Issues);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var body = Body(program, function);
        var stratum = new Stratum(c.Types);
        stratum.Run(program, body);
        var (borrows, work, found, seen) = (stratum.Borrows, stratum.Borrows.Work, new SortedSet<string>(StringComparer.Ordinal), new List<string>());
        foreach (var statement in body.Statements)
        {
            if (statement.Kind != MirStatementKind.Assign)
            {
                continue;
            }

            var (rvalue, places) = (statement.Rvalue, new List<MirPlace> { statement.Place });
            places.AddRange(Enumerable.Range(rvalue.Start, rvalue.Kind is MirRvalueKind.Use or MirRvalueKind.Aggregate ? rvalue.Count : 0)
                .Where(x => body.Operands[x].Kind != MirOperandKind.Const).Select(x => body.Operands[x].Place));
            if (rvalue.Kind is MirRvalueKind.Ref or MirRvalueKind.Storage)
            {
                places.Add(rvalue.Place);
            }

            seen.AddRange(places.Select(x => PlaceText(body, x)));
            foreach (var place in places.Where(x => read is null || PlaceText(body, x) == read))
            {
                var lent = borrows.Lent(place);
                found.Add(lent < 0 ? "none" : PositionText(body, borrows.LentRun(lent, out var root), root));
            }
        }

        Assert.True(read is null || found.Count > 0, $"no Place {read} among {string.Join(", ", seen)}");
        return ([.. found], stratum.Borrows.Work - work);
    }

    // A Place's text: its root's name (a temp `_n`), `(*p)` for a Deref, `.k` for a Field, `[k]` for a constant index, `[_]` for a
    // runtime index, `(p as k)` for a Downcast.
    private static string PlaceText(MirBody body, MirPlace place)
    {
        var rows = new List<MirProjection>();
        for (var id = place.Projection; id > 0; id = body.Projections[id].Prefix)
        {
            rows.Add(body.Projections[id]);
        }

        var text = RootText(body, place.Root);
        foreach (var row in Enumerable.Reverse(rows))
        {
            var constant = row.Kind != MirProjectionKind.Index || row.Position >= 0;
            text = Step(text, row.Kind, row.Kind == MirProjectionKind.Index ? row.Position : row.Arg, constant);
        }

        return text;
    }

    // A position's text, as PlaceText writes a Place (a constant index as `[k]`).
    private static string PositionText(MirBody body, ReadOnlySpan<MirStep> steps, int root)
    {
        var text = RootText(body, root < body.Locals.Length ? root : ~(root - body.Locals.Length));
        foreach (var step in steps)
        {
            text = Step(text, step.Kind, step.Arg, step.Kind != MirProjectionKind.Index);
        }

        return text;
    }

    private static string RootText(MirBody body, int root) => root < 0 ? $"static{~root}" : body.Locals[root].Name ?? $"_{root}";

    private static string Step(string text, MirProjectionKind kind, int arg, bool constant) => kind switch
    {
        MirProjectionKind.Deref or MirProjectionKind.PairLayer => $"(*{text})",
        MirProjectionKind.Field => $"{text}.{arg}",
        MirProjectionKind.Downcast => $"({text} as {arg})",
        _ => constant ? $"{text}[{arg}]" : $"{text}[_]",
    };
}
