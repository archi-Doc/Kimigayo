// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>A5's Loan stratum on MIR (SPEC 15.6; design A3 with its 12 errata; docs/dev/DECISIONS.md R4-M57): each rule with valid and
/// invalid programs, through the problems <see cref="MirBorrows"/> adds over every body view of a built program, each as its function and
/// the first line of its subject.</summary>
public sealed class MirBorrowsTest(ITestOutputHelper output)
{
    private const string Prelude = """
        func inspect<T>(value: ref/T) => ()
        func pin(slot: ref/(ref/i32 during a)) -> ref/i32 during slot => slot@follow
        func keep(value: ref/i32) -> ref/i32 during value => value
        func put(slot: uniq/(ref/i32 during a), value: ref/i32 during a) => slot@follow = value
        func size(value: ref/i32) -> i32 => value@follow
        struct Pair
            public var a: i32 = 1
            public var b: i32 = 2
            public init() => ()
        struct S {a}
            public var r: ref/i32 during a
            public var n: i32 = 0
            public init(r: ref/i32 during a) => self.r = r
        struct S3 {a}
            public var rs: ref/(uniq/i32 during a) during a
            public init(rs: ref/(uniq/i32 during a) during a) => self.rs = rs
        struct U {a}
            public var p: uniq/i32 during a
            public init(p: uniq/i32 during a) => self.p = p
        struct T39 {o}
            public var p: uniq/Array<i32> during o
            public init(p: uniq/Array<i32> during o) => self.p = p

        """;

    // SPEC 15.6, 15.6.2: an access conflicts with a Loan live after it that it hits: a write, a Move or an exclusive borrow with any Loan
    // of an overlapping Place, a read or a shared borrow with an exclusive one; Loans end with their last use, and distinct Fields are
    // disjoint.
    [Fact]
    public void AnAccessConflictsWithALiveLoanItHits()
        => Assert.Equal(
            ["sharedThenWrite: x = 2", "readUnderUnique: x", "twoUnique: x@uniq", "sameField: p.a = 3", "moveWhileShared: s"],
            Problems("""
                func sharedThenWrite()
                    var x = 1
                    let r = x@ref
                    x = 2
                    inspect(r)
                func writeAfterLastUse()
                    var x = 1
                    let r = x@ref
                    inspect(r)
                    x = 2
                func readUnderShared()
                    var x = 1
                    let r = x@ref
                    let y = x
                    inspect(r)
                func readUnderUnique()
                    var x = 1
                    let r = x@uniq
                    let y = x
                    inspect(r)
                func twoUnique()
                    var x = 1
                    let a = x@uniq
                    let b = x@uniq
                    inspect(a)
                func disjointFields()
                    var p = Pair.init()
                    let r = p.a@uniq
                    p.b = 3
                    inspect(r)
                func sameField()
                    var p = Pair.init()
                    let r = p.a@uniq
                    p.a = 3
                    inspect(r)
                func moveWhileShared()
                    let s = "text"
                    let r = s@ref
                    let t = s@move
                    inspect(r)
                """));

    // SPEC 14.9, 14.10.3 (F10): Loans flow forward over every path, joined at heads, around loops into the next iteration, and into code
    // only the checks reach; a holder reassigned before its next use carries nothing across.
    [Fact]
    public void LoansFlowOverJoinsLoopsAndCheckOnlyCode()
        => Assert.Equal(
            ["loopCarry: x = 3", "joined: x = 3", "afterReturn: x = 2"],
            Problems("""
                func loopCarry(flag: bool)
                    var x = 1
                    var y = 2
                    var r = y@ref
                    while flag
                        x = 3
                        inspect(r)
                        r = x@ref
                func loopReassigned(flag: bool)
                    var x = 1
                    var y = 2
                    var r = y@ref
                    while flag
                        inspect(r)
                        x = 3
                        r = x@ref
                func joined(ready: bool)
                    var x = 1
                    var y = 2
                    let r = if ready => x@ref else => y@ref
                    x = 3
                    inspect(r)
                func afterReturn() -> i32
                    var x = 1
                    let r = x@ref
                    return 1
                    x = 2
                    inspect(r)
                    return 2
                """));

    // SPEC 15.6.3 (R2): a Reborrow carries the Loans of every reference it follows, so its parent's Place stays in use while it lives; a
    // read through the parent is an access of the child's Place; a parameter's referent is storage the body does not own (Φ_E).
    [Fact]
    public void AReborrowCarriesItsParents()
        => Assert.Equal(
            ["parentRead: x", "readThroughParent: h@follow + 1", "parentWrite: h@follow = 5", "parameterReborrow: p@follow = 5", "parameterField: p.n = 3"],
            Problems("""
                func parentRead()
                    var x = 1
                    var h = x@uniq
                    let c = h@follow@uniq
                    let v = x
                    c@follow = 2
                func parentReadAfter()
                    var x = 1
                    var h = x@uniq
                    let c = h@follow@uniq
                    c@follow = 2
                    let v = x
                func readThroughParent()
                    var x = 1
                    var h = x@uniq
                    let c = h@follow@uniq
                    let y = h@follow + 1
                    c@follow = 2
                func copiedThrough()
                    var x = 1
                    let h = x@ref
                    let y = h@follow + 1
                    x = 3
                    inspect(y)
                func parentWrite()
                    var x = 1
                    var h = x@uniq
                    let c = h@follow@uniq
                    h@follow = 5
                    c@follow = 2
                func parameterReborrow(p: uniq/i32)
                    let c = p@follow@uniq
                    p@follow = 5
                    c@follow = 2
                func parameterReborrowEnded(p: uniq/i32)
                    let c = p@follow@uniq
                    c@follow = 2
                    p@follow = 5
                func parameterField(p: uniq/S)
                    let v = p.n@ref
                    p.n = 3
                    inspect(v)
                """));

    // SPEC 15.6, 15.6.4 (A3's transit, 25a, 25b, 29c): a value a location stores into a holder live after it is checked with its access,
    // so storing what borrows the destination into it is an error, unless the destination is dead; a write through a reference stores
    // into the holders its Loans name (P10).
    [Fact]
    public void AStoredValueIsCheckedInTransit()
        => Assert.Equal(
            ["selfStore: x = pin(x@ref)", "partStore: s.r = pin(s.r@ref)", "throughStore: x = 3"],
            Problems("""
                func selfStore(y: ref/i32)
                    var x = y
                    x = pin(x@ref)
                    inspect(x)
                func selfStoreDead(y: ref/i32)
                    var x = y
                    x = pin(x@ref)
                func partStore()
                    var x = 1
                    var s = S.init(r: x@ref)
                    s.r = pin(s.r@ref)
                    inspect(s)
                func partStoreDead()
                    var x = 1
                    var s = S.init(r: x@ref)
                    s.r = pin(s.r@ref)
                func throughStore()
                    var x = 1
                    var y = 2
                    var slot = y@ref
                    let p = slot@uniq
                    p@follow = x@ref
                    x = 3
                    inspect(slot)
                func throughStoreEnded()
                    var x = 1
                    var y = 2
                    var slot = y@ref
                    let p = slot@uniq
                    p@follow = x@ref
                    inspect(slot)
                    x = 3
                """));

    // SPEC 15.6.4: a call's result and the storage it writes carry the Loans of the inputs its contract lets them retain (TyCtxt.Retains),
    // and nothing else.
    [Fact]
    public void ACallCarriesWhatItRetains()
        => Assert.Equal(
            ["retained: x = 2", "stored: x = 3"],
            Problems("""
                func retained()
                    var x = 1
                    let r = keep(x@ref)
                    x = 2
                    inspect(r)
                func notRetained()
                    var x = 1
                    let n = size(x@ref)
                    x = 2
                    inspect(n)
                func stored()
                    var x = 1
                    var y = 2
                    var slot = y@ref
                    put(slot@uniq, x@ref)
                    x = 3
                    inspect(slot)
                """));

    // SPEC 15.6.3 (S1, A3 E3, PLAN G110): a Borrow past a shared Deref forms no Loan; it carries the Loans of the layers from its last
    // shared reference on, so its source may be moved or written while it lives, but not what it refers to (39a–39f, 39l–39n).
    [Fact]
    public void ABorrowPastASharedDerefFormsNoLoan()
        => Assert.Equal(
            ["g39b: x = 5", "g39d: x = 5", "g39f: x = 5", "g39l: slot@follow = 5", "g39n: t = [4]"],
            Problems("""
                func g39a()
                    var x = 1
                    var s = S.init(r: x@ref)
                    let v = s.r@follow@ref
                    let t = s@move
                    inspect(v)
                func g39b()
                    var x = 1
                    var s = S.init(r: x@ref)
                    let v = s.r@follow@ref
                    let t = s@move
                    x = 5
                    inspect(v)
                func g39c()
                    var x = 1
                    var s = S.init(r: x@ref)
                    var p = s@uniq
                    let v = p.r@follow@ref
                    s.n = 1
                    inspect(v)
                func g39d()
                    var x = 1
                    var s = S.init(r: x@ref)
                    var p = s@uniq
                    let v = p.r@follow@ref
                    x = 5
                    inspect(v)
                func g39e()
                    var x = 1
                    var y = 2
                    var s = S.init(r: x@ref)
                    let v = s.r@follow@ref
                    s.r = y@ref
                    inspect(v)
                func g39f()
                    var x = 1
                    var y = 2
                    var s = S.init(r: x@ref)
                    let v = s.r@follow@ref
                    s.r = y@ref
                    x = 5
                    inspect(v)
                func g39l()
                    var x = 1
                    var slot = x@uniq
                    var s = S3.init(rs: slot@ref)
                    var p = s@uniq
                    let v = p.rs@follow@follow@ref
                    slot@follow = 5
                    inspect(v)
                func g39m()
                    var x = 1
                    var o = 2
                    var other = o@uniq
                    var slot = x@uniq
                    var s = S3.init(rs: slot@ref)
                    var p = s@uniq
                    let v = p.rs@follow@follow@ref
                    s.rs = other@ref
                    inspect(v)
                func g39n()
                    var t: Array<i32> = [1, 2, 3]
                    var s = T39.init(p: t@uniq)
                    let q = s@ref
                    let e = q.p@follow@ref
                    t = [4]
                    inspect(e@follow)
                """));

    // SPEC 15.6 ("a value is also used wherever…"; A3 E5, 39o, 100): a borrow is checked with the states of the holders its own Loans name,
    // so borrowing a holder that refers to itself conflicts with that reference though the holder is not used afterwards.
    [Fact]
    public void ABorrowsOwnLoansSeedTheClosure()
        => Assert.Equal(
            ["e39o: s@uniq", "e100: r@follow@uniq"],
            Problems("""
                func e39o()
                    var z = 0
                    var s = S.init(r: z@ref)
                    s.r = s.n@ref
                    var p = s@uniq
                    p.n = 5
                    inspect(p.r@follow)
                func e100()
                    var z = 0
                    var s = S.init(r: z@ref)
                    let r = s@uniq
                    r.r = r.n@ref
                    let q = r@follow@uniq
                    q.n = 5
                    inspect(q.r@follow)
                func e100b()
                    var z = 0
                    var s = S.init(r: z@ref)
                    let r = s@uniq
                    r.r = r.n@ref
                    let q = r@follow@ref
                    inspect(q.r@follow)
                """));

    // SPEC 4.8.1, 4.8.2 (A3 E6, 42a, 42b, 42d): a Slice element is its handle's referent, so a borrow of one carries the backing's Loan
    // and none on the handle variable.
    [Fact]
    public void ASliceIndexIsASharedDeref()
        => Assert.Equal(
            ["s42a: arr[0] = 5"],
            Problems("""
                func s42a(i: isize)
                    var arr: [3 of i32] = [1, 2, 3]
                    var arr2: [1 of i32] = [4]
                    var s = arr[..]
                    var other = arr2[..]
                    let e = s[i]@ref
                    s = other
                    arr[0] = 5
                    inspect(e)
                func s42b(i: isize)
                    var arr: [3 of i32] = [1, 2, 3]
                    var arr2: [1 of i32] = [4]
                    var s = arr[..]
                    var other = arr2[..]
                    let e = s[i]@ref
                    s = other
                    inspect(e)
                func first<T>(s: Slice<T>) -> ref/T during s.source
                    return s[0]@ref
                """));

    // SPEC 15.6.3 (A3 E8; 23a, 34a, 34c, 29a): destroying a Move Path in place ends every Loan whose path it exactly prefixes, across
    // borrow Derefs: replacing a holder ends the children through it, but a constant index ends nothing borrowed at a runtime one, and
    // a deep borrow of a holder still meets its children.
    [Fact]
    public void DestroyingAMovePathEndsTheLoansItPrefixes()
        => Assert.Equal(
            ["k29a: q@follow@ref", "k34a: arr[i]@follow = 7"],
            Problems("""
                func k23a()
                    var x = 1
                    var z = 3
                    var slot = x@uniq
                    var slot2 = z@uniq
                    var rr = slot@uniq
                    let c = rr@follow@follow@uniq
                    rr = slot2@uniq
                    rr@follow@follow = 7
                    c@follow = 1
                func k29a()
                    var t = 1
                    var u = 2
                    var s = U.init(p: t@uniq)
                    var o = U.init(p: u@uniq)
                    var q = s@uniq
                    let c = q.p@follow@uniq
                    let w = q@follow@ref
                    q = o@uniq
                    let a = w.p@follow
                    c@follow = 5
                    inspect(w.p@follow)
                func k34a(i: isize)
                    var x = 1
                    var y = 2
                    var z = 3
                    var arr: [2 of uniq/i32] = [x@uniq, y@uniq]
                    let c = arr[i]@follow@uniq
                    arr[0] = z@uniq
                    arr[i]@follow = 7
                    c@follow = 1
                func k34c(i: isize)
                    var x = 1
                    var y = 2
                    var z = 3
                    var w = 4
                    var arr: [2 of uniq/i32] = [x@uniq, y@uniq]
                    let c = arr[i]@follow@uniq
                    arr = [z@uniq, w@uniq]
                    arr[i]@follow = 7
                    c@follow = 1
                """));

    // A3 6.4, R4-M3a: a problem's facts are its Loan's anchor and its witness: the holder's next use, or the store that carries the Loan.
    [Fact]
    public void AProblemNamesItsLoanAndWitness()
    {
        var c = MirBuildTest.Compile(Prelude + """
            func sharedThenWrite()
                var x = 1
                let r = x@ref
                x = 2
                inspect(r)
            func selfStore(y: ref/i32)
                var x = y
                x = pin(x@ref)
                inspect(x)
            """);
        var program = Analyze(c);
        var problems = program.Problems.All.ToArray();
        Assert.Equal(
            ["x = 2: Anchor x@ref, Use inspect(r)", "x = pin(x@ref): Anchor x@ref, Store x = pin(x@ref)"],
            problems.Select((x, i) => (x, i)).Where(static x => x.x.Code == DiagnosticCode.ComparisonLoanConflict_Kd).Select(x =>
                $"{Line(c, x.x.Subject)}: {string.Join(", ", program.Problems.Facts(x.i).ToArray().Select(f => $"{f.Kind} {Line(c, f.Site)}"))}"));
    }

    // V16 (A3 6.3, shapes 1–3): N Copies of one reference keep the work linear; a straight line of N borrowed locals grows it within the
    // bound's dense terms, quadratically at most: each local's StorageLive, write and StorageDead end its Loans in every holder ((ended
    // points) × holders), and N references used at the end make each borrow scan the live holders (accesses × live nodes).
    [Theory]
    [InlineData("    var x{0} = {0}\n    let r{0} = x{0}@ref\n    inspect(r{0})\n", "", true)]
    [InlineData("    var x{0} = {0}\n    let r{0} = x{0}@ref\n", "    inspect(r{0})\n", true)]
    [InlineData("    let c{0} = r\n", "    inspect(c{0})\n", false)]
    public void TheWorkScalesWithTheProgram(string each, string after, bool dense)
    {
        var (sizes, work) = (new[] { 32, 64, 128 }, new long[3]);
        for (var i = 0; i < sizes.Length; i++)
        {
            var text = new StringBuilder(Prelude).Append("func shape()\n    var x = 1\n    let r = x@ref\n");
            for (var pass = 0; pass < 2; pass++)
            {
                for (var n = 0; n < sizes[i]; n++)
                {
                    text.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, pass == 0 ? each : after, n);
                }
            }

            var c = MirBuildTest.Compile(text.ToString());
            var stratum = new Stratum(c.Types);
            stratum.Run(c.BuildMir());
            Assert.DoesNotContain(c.BuildMir().Problems.All.ToArray(), static x => x.Code == DiagnosticCode.ComparisonLoanConflict_Kd);
            work[i] = stratum.Borrows.Work;
        }

        output.WriteLine($"N {string.Join(", ", sizes)}: work {string.Join(", ", work)}");
        Assert.True(work[2] - work[1] <= ((dense ? 5 : 2) * (work[1] - work[0])) + 64, $"work {string.Join(", ", work)}");
    }

    // V16: A5 on a program version it checked before allocates nothing: its sets, tables and the dataflow keep their storage.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRunAllocatesNothing()
    {
        var c = MirBuildTest.Compile(Prelude + "func loopCarry(flag: bool)\n    var x = 1\n    var y = 2\n    var r = y@ref\n    while flag\n" +
            "        x = 3\n        inspect(r)\n        r = x@ref\nfunc stored()\n    var x = 1\n    var y = 2\n    var slot = y@ref\n" +
            "    put(slot@uniq, x@ref)\n    x = 3\n    inspect(slot)\n");
        var program = c.BuildMir();
        var stratum = new Stratum(c.Types);
        Assert.Equal(0, AllocationMeasurement.Measure(() => stratum.Run(program)));
        Assert.Equal(2, program.Problems.All.ToArray().Count(static x => x.Code == DiagnosticCode.ComparisonLoanConflict_Kd));
    }

    // The Loan conflicts of a program's own bodies: function and the subject's first line.
    private static string[] Problems(string source)
    {
        var c = MirBuildTest.Compile(Prelude + source);
        var program = Analyze(c);
        return [.. program.Problems.All.ToArray().Where(static x => x.Code == DiagnosticCode.ComparisonLoanConflict_Kd && x.Subject.Module == 0)
            .Select(x => $"{Function(c.SyntaxOf(x.Subject))}: {Line(c, x.Subject)}")];

        static string? Function(Koto node) => node is FunctionKoto function ? function.Name : node.Parent is { } parent ? Function(parent) : null;
    }

    private static MirProgram Analyze(Compilation c)
    {
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.Empty(c.Binding.Issues);
        new Stratum(c.Types).Run(program);
        return program;
    }

    private static string Line(Compilation c, SourceRef site) => c.SyntaxOf(site).ToString().Split('\n')[0].Trim();

    // A5 over every body view of a built program, as the driver's step 4 will run it (R4-M57).
    private sealed class Stratum
    {
        private readonly MirDataflow flow = new(new MirReach());
        private readonly MirMoveData moves = new();
        private readonly MirCases cases;

        internal Stratum(TyCtxt types)
        {
            this.cases = new(types);
            this.Borrows = new(types, this.flow, this.moves, this.cases);
        }

        internal MirBorrows Borrows { get; }

        internal void Run(MirProgram program)
        {
            foreach (var body in program.Bodies)
            {
                this.flow.Begin(body);
                this.moves.Build(body, this.flow);
                for (var view = 0L; view < MirCases.Count(body); view++)
                {
                    this.cases.Select(body, view);
                    this.Borrows.Run(program, body, view);
                }
            }
        }
    }
}
