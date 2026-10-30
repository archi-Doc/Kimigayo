// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;
using Xunit.Sdk;

namespace XunitTest;

/// <summary>A5's rules on live Loans of static Places (SPEC 15.6.2, 15.6.4, 15.6.7, 16.3.3, 22.4.3, 7.2.3; docs/dev/DECISIONS.md R4-M92,
/// R4-M97) on hand-built MIR until P42: Binding reports every Borrow SPEC lends of a static-rooted Place as the located Unsupported_Kd until
/// then (R4-M90 PQ-1), so no source program reaches them. Each program is a twin of a source test's (MirBorrowsTest.Effects, .Defaults) whose
/// static Borrows borrow stand-ins, and A5 runs over its bodies with each stand-in rooted at its static (<see cref="Rooted"/>), asserting
/// what the source test asserted with the stand-in's name. P42's static-Borrow unit moves these witnesses back to source.</summary>
public sealed partial class MirBorrowsTest
{
    // The stand-ins' markers (Rooted): group Root's functions, each named after the static it stands for, and readers of the statics that no
    // other body of the effect programs accesses (a stand-in's static is one the program's bodies access, the Fields A4 numbers).
    private const string EffectRoots = """
            public func readLimits() -> i32 => State.limits.n
            public func readRefs() -> isize => State.refs.length
        group Root
            public func counter() -> Counter => Counter.init()
            public func limits() -> Counter => Counter.init()
            public func count() -> i32 => 0
            public func list() -> Array<i32> => []
            public func refs() -> Array<ref/i32> => []
            public func x() -> Counter => Counter.init()
            public func log() -> Counter => Counter.init()
            public func d() -> Counter => Counter.init()
            public func lim2() -> Counter => Counter.init()
            public func y5() -> Counter => Counter.init()
            public func cx() -> Counter => Counter.init()
            public func clog() -> Counter => Counter.init()
            public func tally() -> Counter => Counter.init()
            public func gx() -> Counter => Counter.init()
            public func watch() -> Watch => Watch.init()

        """;

    // Round 20's group State as Copies has it, each default's Borrow of a static a stand-in's: a temporary in an expression default, so the
    // copy keeps its shape, a local in a label block's. Only the templates the rooted witnesses call.
    private const string CopyTwins = """
        struct Counter
            public var count: i32 = 0
            public init() => ()
            public func add(self: uniq/Self, x: i32 = State.touch()) => ()
            public func addO(self: uniq/Self, x: i32 = State.touchOther()) => ()
            public func addR(self: uniq/Self, x: i32 = State.peekCount()) => ()
        group Lim
            public func run() => _ = Nest<i32>.init(value: 1)
        group State
            public var counter: Counter = Counter.init()
            public var other: Counter = Counter.init()
            public func inspect<T>(value: ref/T) => ()
            public func storeL(l: uniq/Loan<uniq/(ref/(ref/i32 during p) during q) during a>, v: ref/(ref/i32 during p) during q) => ()
            public func poke(c: uniq/Counter) -> i32 => 1
            public func peek(c: ref/Counter) -> i32 => 1
            public func touch() -> i32
                counter.count = 1
                return 1
            public func touchOther() -> i32
                other.count = 1
                return 1
            public func peekCount() -> i32 => counter.count
            public func add(c: uniq/Counter, x: i32 = State.poke(Root.counter()@uniq)) => ()
            public func addOther(c: uniq/Counter, x: i32 = State.poke(Root.other()@uniq)) => ()
            public func addRead(c: uniq/Counter, x: i32 = State.peek(Root.counter()@ref)) => ()
            public func g(x: i32 = State.poke(Root.counter()@uniq)) -> i32 => x
            public func f(y: i32 = State.g()) -> i32 => y
            public func f2(y: i32 = label w: do
                var sc = Root.counter()
                let r = sc@ref
                exit to w State.g() + r.count
            ) -> i32 => y
            public func addg<s/T>(a: s/T, c: uniq/Counter, x: i32 = State.poke(Root.counter()@uniq)) -> ()
                s is ref or uniq
                return
            public func addh(c: uniq/Counter, x: i32 = label w: do
                var x0 = 1
                var z: ref/i32 = x0@ref
                var w: ref/(ref/i32) = z@ref
                var lh = Loan<uniq/(ref/(ref/i32))>.init(w@uniq)
                var b: ref/i32 = x0@ref
                State.storeL(lh@uniq, b@ref)
                var sc = Root.counter()
                let r = sc@ref
                exit to w State.poke(sc@uniq) + r.count
            ) => ()
            public func addl(c: uniq/Counter, x: i32 = label w: do
                Lim.run()
                exit to w 1
            ) => ()
            public func effOwn(y: i32 = label w: do
                var sc = Root.counter()
                let r = sc@ref
                let t = State.touch()
                exit to w t + r.count
            ) -> i32 => y

        """;

    // The copies' markers (Rooted).
    private const string CopyRoots = """
        group Root
            public func counter() -> Counter => Counter.init()
            public func other() -> Counter => Counter.init()

        """;

    // ACallsEffectsMeetTheLiveStaticLoans's rows on rooted bodies (R1, W6, A6 row 1, the sets; R4-M92): a write key or an unknown effect meets
    // any Loan (e3, e6, e11, adapted, forWriter, e15), a read key a unique one only (e2, e7, forReader), a reservation the call activates is
    // unique at its effects (e4, e5), a Loan found in a reservation the call does not activate is CallReservationConflict_Kd (e1, e3, e15), an
    // activation's conflict and an effect conflict on one Loan are two problems (e4c); Loans on immutable statics (e11b) and dead ones (e14)
    // are never met. Its e1Writeback borrows no static Place.
    [Fact]
    public void ACallsEffectsMeetTheLiveStaticLoansRooted()
        => Assert.Equal(
            [
                "e1: g() CallReservationConflict_Kd", "e3: writes() CallReservationConflict_Kd", "e4: sc.bump()", "e4c: sc.reset() CallActivationConflict_Kd",
                "e4c: sc.reset()", "e6: let g = sc.guard()", "e11: g()", "adapted: takes(sk)", "forWriter: clears()",
                "e15: sink(x@move) CallReservationConflict_Kd",
            ],
            RootedEffects("""
                    public func e1(g: () -> i32)
                        var sc = Root.counter()
                        sc.add(g())
                    public func e2()
                        var sc = Root.counter()
                        sc.add(reads())
                    public func e3()
                        var sc = Root.counter()
                        sc.add(writes())
                    public func e4()
                        var sc = Root.counter()
                        sc.bump()
                    public func e5()
                        var sc = Root.counter()
                        sc.add(1)
                    public func e4c()
                        var sc = Root.counter()
                        let c = sc.view()
                        sc.reset()
                        inspect(c)
                    public func e6()
                        var sc = Root.counter()
                        let g = sc.guard()
                        inspect(g)
                    public func e7()
                        var sc = Root.counter()
                        let p = sc.peek()
                        inspect(p)
                    public func e11(g: () -> ())
                        var sc = Root.counter()
                        var sl = Root.limits()
                        let c = sc.view()
                        let l = sl.view()
                        g()
                        inspect(c)
                        inspect(l)
                    public func e11b(g: () -> ())
                        var sl = Root.limits()
                        let l = sl.view()
                        g()
                        inspect(l)
                    public func e14(g: () -> ())
                        var sc = Root.counter()
                        let c = sc.view()
                        inspect(c)
                        g()
                    public func adapted()
                        var sk = Root.count()
                        takes(sk)
                    public func forWriter()
                        var si = Root.list()
                        for v in si.slice()
                            clears()
                    public func forReader()
                        var si = Root.list()
                        for v in si.slice()
                            _ = length()
                    public func e15<T>(x: T)
                        var sc = Root.counter()
                        sc.add(sink(x@move))
                    public func e15b<T>(x: T)
                        T is Copy
                        var sc = Root.counter()
                        sc.add(sink(x))

                """));

    // ADestructionMeetsItsOwnStaticAndTheLiveStaticLoans's e9s and e9b on rooted bodies (SPEC 16.3.3, 16.4; R4-M92): an erased destruction
    // of a local, at its assignment's Drop, meets every live mutable-static Loan (e9s); exchange is R3's way out, its Loan on the static
    // ending at the call, so the old value's destruction, whose destructor reads the static, meets nothing (e9b; e8 at source its invalid
    // twin). Its other rows borrow no static Place.
    [Fact]
    public void AnErasedDestructionMeetsTheLiveStaticLoansRooted()
        => Assert.Equal(
            ["e9s: local = func []() => ()"],
            RootedEffects("""
                    public func e9s(g: () -> ())
                        var local: () -> () = g
                        var sc = Root.counter()
                        let c = sc.view()
                        local = func [] () => ()
                        inspect(c)
                    public func e9b()
                        var sw = Root.watch()
                        _ = Kimi.Intrinsics.exchange(sw@uniq, with: Watch.init())

                """));

    // AnEnsureWhoseProofWouldDecideIsUnsupported's rows on rooted bodies (SPEC 15.6.4, 22.4.3; R4-M92, OQ-F5-6): a staticEnsure's initializer
    // meets the live Loans (e10b) unless a live Loan on the ensured static would prove it initialized, a may-property (e10f), a key being its
    // Field (e10d); each such conflict is the located Unsupported_Kd (e10a, e10d, e10e, e10f), nothing to meet nothing (e10c).
    [Fact]
    public void AnEnsureWhoseProofWouldDecideIsUnsupportedRooted()
    {
        const string Cache = """
                struct Cache<T>
                    public group S
                        public var cx: Counter = S.makeX()
                        public var clog: Counter = Counter.init()
                        public func makeX() -> Counter
                            clog = Counter.init()
                            return Counter.init()
                        public func inspect<U>(value: ref/U) => ()
                        public func e10e()
                            var sx = Root.cx()
                            var sg = Root.clog()
                            let lx = sx.view()
                            let ll = sg.view()
                            let v = cx.n
                            inspect(lx)
                            inspect(ll)
                        public func e10d()
                            var sx = Root.cx()
                            var sg = Root.clog()
                            let lx = sx.view()
                            let ll = sg.view()
                            let v = Cache<string>.S.cx.n
                            inspect(lx)
                            inspect(ll)

                """;
        Assert.Equal(
            ["e10a: x Unsupported_Kd", "e10b: x", "e10f: x Unsupported_Kd", "e10e: cx Unsupported_Kd", "e10d: Cache<string>.S.cx Unsupported_Kd"],
            RootedEffects(
                """
                    public func e10a()
                        var sx = Root.x()
                        var sg = Root.log()
                        let lx = sx.view()
                        let ll = sg.view()
                        let v = x.n
                        inspect(lx)
                        inspect(ll)
                    public func e10b()
                        var sg = Root.log()
                        let ll = sg.view()
                        let v = x.n
                        inspect(ll)
                    public func e10c()
                        var sx = Root.x()
                        let lx = sx.view()
                        let v = x.n
                        inspect(lx)
                    public func e10f(k: bool)
                        var sx = Root.x()
                        var sg = Root.log()
                        var o = 1
                        var lx = o@ref
                        if k => lx = sx.view()
                        let ll = sg.view()
                        let v = x.n
                        inspect(lx)
                        inspect(ll)

                """,
                Cache));
    }

    // AnInitializationProofDecidesWhereverAnInitializerComposes's rows on rooted bodies (SPEC 15.6.4, 22.4.3; R4-M92): the proof applies
    // wherever a conflict's deciding effect lies in the initializer of a static a live Loan is on and the site may first-access it: a call that
    // reads x (pA), the ensure of x2, whose initializer reads d (pB), a call that reads the immutable lim2 (pQ), a destructor that reads x
    // (early); without the proof Loan each is the error (pA2, pB2), as is a site that never reaches d (direct).
    [Fact]
    public void AnInitializationProofDecidesWhereverAnInitializerComposesRooted()
        => Assert.Equal(
            [
                "pA: useX() Unsupported_Kd", "pA2: useX()", "pB: x2 Unsupported_Kd", "pB2: x2", "pQ: useLim() Unsupported_Kd", "direct: writeLog()",
                "early: let r = XReader.init() Unsupported_Kd",
            ],
            RootedEffects("""
                    public func pA()
                        var sx = Root.x()
                        var sg = Root.log()
                        let lx = sx.view()
                        let ll = sg.view()
                        _ = useX()
                        inspect(lx)
                        inspect(ll)
                    public func pA2()
                        var sg = Root.log()
                        let ll = sg.view()
                        _ = useX()
                        inspect(ll)
                    public func pB()
                        var sd = Root.d()
                        var sg = Root.log()
                        let ld = sd.view()
                        let ll = sg.view()
                        let v = x2.n
                        inspect(ld)
                        inspect(ll)
                    public func pB2()
                        var sg = Root.log()
                        let ll = sg.view()
                        let v = x2.n
                        inspect(ll)
                    public func pQ()
                        var sm = Root.lim2()
                        var sg = Root.log()
                        let lm = sm.view()
                        let ll = sg.view()
                        _ = useLim()
                        inspect(lm)
                        inspect(ll)
                    public func direct()
                        var sd = Root.d()
                        var sg = Root.log()
                        let ld = sd.view()
                        let ll = sg.view()
                        writeLog()
                        inspect(ld)
                        inspect(ll)
                    public func early()
                        var sx = Root.x()
                        var sg = Root.log()
                        let lx = sx.view()
                        let ll = sg.view()
                        do
                            let r = XReader.init()
                        inspect(lx)
                        inspect(ll)

                """));

    // AnUnprovedEffectStillDecides on rooted bodies (SPEC 15.6.4; R4-M92's R2-B1): the first deciding effect outside a proved initializer
    // decides the Language problem: multiImm's unknown g beside the proved lim2's write of log (EffectUnknown), r3Unproved's read of w5
    // beside the proved immutable y5's erased destruction (R3, EffectRead).
    [Fact]
    public void AnUnprovedEffectStillDecidesRooted()
    {
        var c = MirBuildTest.Compile(Prelude + EffectTypes + """
                public func multiImm(g: () -> ())
                    var sm = Root.lim2()
                    var sg = Root.log()
                    let lm = sm.view()
                    let ll = sg.view()
                    both3(g@move)
                    inspect(lm)
                    inspect(ll)
                public func r3Unproved()
                    var sy = Root.y5()
                    let ly = sy.view()
                    w5 = W5.init()
                    inspect(ly)

            """ + EffectRoots);
        Assert.Empty(c.Binding.Issues);
        var program = AnalyzeRooted(c);
        Assert.Equal(
            ["both3(g@move) ComparisonLoanConflict_Kd EffectUnknown", "w5 = W5.init() ComparisonLoanConflict_Kd EffectRead"],
            program.Problems.All.ToArray().Select((x, i) => (x, i))
                .Where(static x => x.x.Subject.Module == 0 && (LoanCodes.Contains(x.x.Code) || x.x.Code == DiagnosticCode.Unsupported_Kd))
                .Select(x => $"{Line(c, x.x.Subject)} {x.x.Code} {string.Join(",", program.Problems.Facts(x.i).ToArray().Select(static f => f.Kind)
                    .Where(static k => k is MirFactKind.EffectRead or MirFactKind.EffectWrite or MirFactKind.EffectUnknown))}"));
    }

    // ALimitIsUnsupportedAndADerivedEffectReportsNothing on rooted bodies (AGENTS Diagnostics; R4-M10, R4-M91, R4-M92): A4's Deep (Lim's
    // destruction past the walk's bound) is the depth limit where a live mutable-static Loan would be met (e12a; the owner's 「G272: (1)」,
    // R4-M153), nothing where none is (e12b), Unknown beside it decides (e12c); a dependent of an error reports nothing by itself (e13a), a
    // key beside it stays checked (e13b).
    [Fact]
    public void ALimitIsUnsupportedAndADerivedEffectReportsNothingRooted()
    {
        Assert.Equal(
            ["e12a: Lim.run() OwnershipDepthLimit_Kd", "e12c: both(g@move)"],
            RootedEffects("""
                    public func e12a()
                        var sc = Root.counter()
                        let c = sc.view()
                        Lim.run()
                        inspect(c)
                    public func e12b()
                        var sc = Root.counter()
                        let c = sc.view()
                        inspect(c)
                        Lim.run()
                    public func both(g: () -> ())
                        Lim.run()
                        g()
                    public func e12c(g: () -> ())
                        var sc = Root.counter()
                        let c = sc.view()
                        both(g@move)
                        inspect(c)

                """));
        var c = MirBuildTest.Compile(Prelude + EffectTypes + """
                public func opaque() => _ = missing
                public func opaqueWrite()
                    _ = missing
                    State.counter = Counter.init()
                public func e13a()
                    var sc = Root.counter()
                    let c = sc.view()
                    opaque()
                    inspect(c)
                public func e13b()
                    var sc = Root.counter()
                    let c = sc.view()
                    opaqueWrite()
                    inspect(c)

            """ + EffectRoots);
        Assert.NotEmpty(c.Binding.Issues);
        Assert.Equal(["e13b: opaqueWrite()"], Problems(c, AnalyzeRooted(c), LoanCodes));
    }

    // AStaticsLoanNamesEAndAWholeWriteEndsIt on rooted bodies (SPEC 15.6, 15.6.3; G136, R4-M92, OQ-F5-1 (a)): a whole write of a static ends
    // its Field's Loans, so a write meeting a live Loan is one problem however often it is written again (e16b), a dead Loan meets nothing
    // (e16c); a static's Loan names E, so a call's store through a static's reservation lands in E (stored).
    [Fact]
    public void AStaticsLoanNamesEAndAWholeWriteEndsItRooted()
        => Assert.Equal(
            ["e16a: counter = Counter.init()", "e16b: counter = Counter.init()", "stored: y = 2"],
            RootedEffects("""
                    public func e16a()
                        var sc = Root.counter()
                        let c = sc.view()
                        counter = Counter.init()
                        inspect(c)
                    public func e16b()
                        var sc = Root.counter()
                        let c = sc.view()
                        counter = Counter.init()
                        counter = Counter.init()
                        inspect(c)
                    public func e16c()
                        var sc = Root.counter()
                        let c = sc.view()
                        inspect(c)
                        counter = Counter.init()
                    public func stored()
                        var y = 1
                        var sr = Root.refs()
                        sr.append(y@ref)
                        y = 2

                """));

    // R4-M92, R-a on rooted bodies: an effect conflict is a Loan problem at its call, also after a generic loop whose iterator's store the
    // per-part guard decides (e17, a located Unsupported_Kd under P13's body hold until R4-M147); a static Loan a default's copy keeps meets
    // the call's unknown effect (copied).
    [Fact]
    public void AnEffectConflictIsALoanProblemRooted()
        => Assert.Equal(
            ["e17: g()", "copied: g()"],
            RootedEffects("""
                    public func e17<T>(a: T, g: () -> ())
                        T is Copy
                        var sc = Root.counter()
                        var xs: Array<T> = []
                        xs.append(a)
                        for e in xs
                            _ = e
                        let c = sc.view()
                        g()
                        inspect(c)
                    public func same(x: ref/i32 during a, y: ref/i32 during a = x) -> ref/i32 during a => y
                    public func copied(g: () -> ())
                        var sc = Root.counter()
                        let r = same(sc.view())
                        g()
                        inspect(r)

                """));

    // AWidenedEffectMeetsTheLiveStaticLoans on a rooted body (SPEC 15.6.4; R4-M87): a summary that would keep an effect on a projection with
    // a recursive base has unknown environment effects, which meet every live mutable-static Loan.
    [Fact]
    public void AWidenedEffectMeetsTheLiveStaticLoansRooted()
    {
        const string Widened = """
                contract Branch
                    associate Child is Branch
                    func child(self: ref/Self) -> Option<Self.Child>
                        effect confined
                contract CopyBranch: Branch, Copy
                    Self.(Branch).Child is CopyBranch
                group Trees
                    public func walk<T>(node: T, depth: i32)
                        T is Branch
                        if depth == 0 => return
                        match node.child()
                            .Some(let next) => Trees.walk(next@move, depth - 1)
                            .None => return
                group Counters
                    public var tally: Counter = Counter.init()
                    public func inspect<T>(value: ref/T) => ()
                    public func readTally() -> i32 => Counters.tally.n
                    public func visitCopies<U>(node: U)
                        U is CopyBranch
                        var st = Root.tally()
                        let c = st.view()
                        Trees.walk(node, 2)
                        inspect(c)

                """;
        Assert.Equal(["visitCopies: Trees.walk(node, 2)"], RootedEffects(string.Empty, Widened));
    }

    // AnEffectProblemNamesTheFieldItMet's e3, e11 and unique on rooted bodies (PD31; R4-M92): an effect problem's facts are its Loan's anchor,
    // its witness and the static Field it met, by what decided it, a write key, else Unknown, else a read key. Its R3 row borrows no static.
    [Fact]
    public void AnEffectProblemNamesTheFieldItMetRooted()
    {
        var c = MirBuildTest.Compile(Prelude + EffectTypes + """
                public func e3()
                    var sc = Root.counter()
                    sc.add(writes())
                public func e11(g: () -> ())
                    var sc = Root.counter()
                    let c = sc.view()
                    g()
                    inspect(c)
                public func unique()
                    var sc = Root.counter()
                    let u = sc.viewU()
                    _ = reads()
                    u@follow = 1

            """ + EffectRoots);
        Assert.Empty(c.Binding.Issues);
        var program = AnalyzeRooted(c);
        Assert.Equal(
            [
                "writes(): Anchor sc, Use sc.add(writes()), EffectWrite public var counter: Counter = Counter.init()",
                "g(): Anchor sc, Use inspect(c), EffectUnknown public var counter: Counter = Counter.init()",
                "reads(): Anchor sc, Use u@follow = 1, EffectRead public var counter: Counter = Counter.init()",
            ],
            program.Problems.All.ToArray().Select((x, i) => (x, i)).Where(static x => LoanCodes.Contains(x.x.Code) && x.x.Subject.Module == 0).Select(x =>
                $"{Line(c, x.x.Subject)}: {string.Join(", ", program.Problems.Facts(x.i).ToArray().Select(f => $"{f.Kind} {Line(c, f.Site)}"))}"));
    }

    // AnEffectConflictHasTheViewsWhoseKeysMeetIt on a rooted body (R4-M11, R4-M91, R4-M92): forward's owner case destroys the Noisy it moves
    // into hold, whose destructor writes G.gx while c lives, and its reference case does not, so the conflict has view 0 only.
    [Fact]
    public void AnEffectConflictHasTheViewsWhoseKeysMeetItRooted()
    {
        var c = MirBuildTest.Compile(Prelude + EffectTypes + EffectRoots + """
            struct Noisy
                public var n: i32 = 0
                public init() => ()
                drop => G.gx = Counter.init()
            group G
                public var gx: Counter = Counter.init()
                public func inspect<T>(value: ref/T) => ()
                public func hold<s/T>(value: s/T)
                    s is owner or ref
                    ()
                public func forward<s/T>(value: s/Noisy, marker: s/T)
                    s is owner or ref
                    var sx = Root.gx()
                    let c = sx.view()
                    hold(value@move)
                    inspect(c)

            """);
        Assert.Empty(c.Binding.Issues);
        var program = AnalyzeRooted(c);
        var forward = Body(program, "forward");
        Assert.Equal(2, MirCases.Count(forward));
        Assert.Equal(["hold(value@move) 1"], program.Problems.All.ToArray().Where(x => x.Body == forward.Ordinal && LoanCodes.Contains(x.Code))
            .Select(x => $"{Line(c, x.Subject)} {x.Views}"));
    }

    // V16 "F5 effects" (DECISIONS R4-M92) on rooted bodies: one body with M Loans on M mutable statics live across N calls, each to a function
    // writing one of K other statics, checks each call's keys once per effect root (A4's row, the root's two bits and its candidates): the
    // work grows linearly in N at M = 4, K = 8 and within the bound's M × (holders) term in M at N = 32. A guard, not a regression: no problem
    // arises. Each rooted Borrow follows its static's staticEnsure (Rooted), as in its source form.
    [Fact]
    public void EffectsScaleWithCallsAndStaticLoans()
    {
        static string Shape(int n, int m, int k)
        {
            var (text, roots) = (new StringBuilder(EffectTypes).Append("group F\n"), new StringBuilder("group Root\n"));
            for (var i = 0; i < m; i++)
            {
                text.Append(System.Globalization.CultureInfo.InvariantCulture, $"    public var m{i}: Counter = Counter.init()\n")
                    .Append(System.Globalization.CultureInfo.InvariantCulture, $"    public func r{i}() -> i32 => F.m{i}.n\n");
                roots.Append(System.Globalization.CultureInfo.InvariantCulture, $"    public func m{i}() -> Counter => Counter.init()\n");
            }

            for (var j = 0; j < k; j++)
            {
                text.Append(System.Globalization.CultureInfo.InvariantCulture, $"    public var k{j}: i32 = 0\n    public func w{j}() => F.k{j} = 1\n");
            }

            text.Append("    public func inspect<T>(value: ref/T) => ()\n    public func shape()\n");
            for (var i = 0; i < m; i++)
            {
                text.Append(System.Globalization.CultureInfo.InvariantCulture, $"        var s{i} = Root.m{i}()\n        let c{i} = s{i}.view()\n");
            }

            for (var call = 0; call < n; call++)
            {
                text.Append(System.Globalization.CultureInfo.InvariantCulture, $"        w{call % k}()\n");
            }

            for (var i = 0; i < m; i++)
            {
                text.Append(System.Globalization.CultureInfo.InvariantCulture, $"        inspect(c{i})\n");
            }

            return text.Append(roots).Append(Prelude).ToString();
        }

        long[] inN = [.. new[] { 32, 64, 128 }.Select(n => RootedWork(Shape(n, 4, 8)))];
        long[] inM = [.. new[] { 8, 16, 32 }.Select(m => RootedWork(Shape(32, m, 8)))];
        output.WriteLine($"N 32, 64, 128 at M 4: work {string.Join(", ", inN)}; M 8, 16, 32 at N 32: work {string.Join(", ", inM)}");
        Assert.True(Grows(inN, 2), $"work in N {string.Join(", ", inN)}");
        Assert.True(Grows(inM, 4), $"work in M {string.Join(", ", inM)}");
    }

    // V16 (R4-M92) on rooted bodies: A5 with the effect rule on a program version it checked before allocates nothing: the effect roots, the
    // Field numbers, the activation stamps and A4's row keep their storage, each view's keys are A4's pooled row and an initializer's summary
    // its stored one (pA's proof); e8's R3 borrows no static.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmEffectRunAllocatesNothing()
    {
        var c = MirBuildTest.Compile(Prelude + EffectTypes + """
                public func e1(g: () -> i32)
                    var sc = Root.counter()
                    sc.add(g())
                public func e6()
                    var sc = Root.counter()
                    let g = sc.guard()
                    inspect(g)
                public func e8() => watch = Watch.init()
                public func e10a()
                    var sx = Root.x()
                    var sg = Root.log()
                    let lx = sx.view()
                    let ll = sg.view()
                    let v = x.n
                    inspect(lx)
                    inspect(ll)
                public func e16b()
                    var sc = Root.counter()
                    let c = sc.view()
                    counter = Counter.init()
                    counter = Counter.init()
                    inspect(c)
                public func pA()
                    var sx = Root.x()
                    var sg = Root.log()
                    let lx = sx.view()
                    let ll = sg.view()
                    _ = useX()
                    inspect(lx)
                    inspect(ll)

            """ + EffectRoots);
        Assert.Empty(c.Binding.Issues);
        var program = c.BuildMir();
        program.Problems.Clear();
        var (bodies, stratum) = (Rooted(c, program), new Stratum(c.Types));
        Assert.Equal(0, AllocationMeasurement.Measure(() => Run(stratum, program, bodies)));
        Assert.Equal(4, Sound(program).Problems.All.ToArray().Count(static x => LoanCodes.Contains(x.Code) && x.Subject.Module == 0));
    }

    // ACopysFailureIsItsTemplatesOnlyWithinOneEvaluation's rows that need a live static Loan, on rooted bodies (D11, D13; SPEC 7.2.3, 15.6,
    // 23.1.1; R4-M97): a failure found in a copy is the caller's unless every event taking part lies in one evaluation of one copy, recorded at
    // the outermost omitting call with the default's operation as its Evaluated fact: the copy's poke activating against the caller's
    // reservation (g171; facts Reservation and Evaluated), its effect form (fx), the access in a copy nested in an explicit argument's copy
    // (n1, at State.f()), two calls (twiceBoth), a two-view generic caller (gen: both views), and a caller whose own conflict precedes a
    // copy of addh, whose generic loop the per-part guard decides (held: its reservation conflict at the omitting call; held under P13's body
    // hold until R4-M147). Kept: the disjoint, read and explicit twins; a Limit inside a copy, at its operation (limit, so the record names
    // addl's Lim.run()). Template-owned: f2's own Loan met in g's copy, f2's record at its State.g() (facts Anchor, Use, Reservation,
    // Evaluated), and effOwn's effect conflict on its own Loan, omitted in its callers alone and in a loop; addh's activation conflict.
    [Fact]
    public void ACopysFailureIsItsTemplatesOnlyWithinOneEvaluationRooted()
    {
        var c = MirBuildTest.Compile(Prelude + CopyTwins + """
                public func g171()
                    var sc = Root.counter()
                    State.add(sc@uniq)
                public func g171Disjoint()
                    var sc = Root.counter()
                    State.addOther(sc@uniq)
                public func g171Read()
                    var sc = Root.counter()
                    State.addRead(sc@uniq)
                public func g171Explicit()
                    var sc = Root.counter()
                    State.add(sc@uniq, State.poke(sc@uniq))
                public func fx()
                    var sc = Root.counter()
                    sc.add()
                public func fxDisjoint()
                    var sc = Root.counter()
                    sc.addO()
                public func fxRead()
                    var sc = Root.counter()
                    sc.addR()
                public func fxExplicit()
                    var sc = Root.counter()
                    sc.add(State.touch())
                public func n1()
                    var sc = Root.counter()
                    State.add(sc@uniq, State.f())
                public func twiceBoth()
                    var sc = Root.counter()
                    State.add(sc@uniq)
                    State.add(sc@uniq)
                public func gen<s/T>(v: s/T)
                    s is ref or uniq
                    var sc = Root.counter()
                    State.addg(v, sc@uniq)
                public func held()
                    var y0 = 1
                    let r0 = y0@ref
                    y0 = 2
                    inspect(r0)
                    var sc = Root.counter()
                    State.addh(sc@uniq)
                public func limit()
                    var y0 = 1
                    let r0 = y0@ref
                    var sc = Root.counter()
                    State.addl(sc@uniq)
                    y0 = 2
                    inspect(r0)
                public func callEffOwn() => _ = State.effOwn()
                public func loopEffOwn(c: bool)
                    while c
                        _ = State.effOwn()

            """ + CopyRoots);
        Assert.Empty(c.Binding.Issues);
        var program = AnalyzeRooted(c);
        Assert.Equal(
            [
                "f2: State.g() CallActivationConflict_Kd", "addh: State.poke(sc@uniq) CallActivationConflict_Kd", "effOwn: State.touch()",
                "g171: State.add(sc@uniq) CallReservationConflict_Kd", "g171Explicit: State.poke(sc@uniq) CallReservationConflict_Kd",
                "fx: sc.add() CallReservationConflict_Kd", "fxExplicit: State.touch() CallReservationConflict_Kd", "n1: State.f() CallReservationConflict_Kd",
                "twiceBoth: State.add(sc@uniq) CallReservationConflict_Kd", "twiceBoth: State.add(sc@uniq) CallReservationConflict_Kd",
                "gen: State.addg(v, sc@uniq) CallReservationConflict_Kd", "held: y0 = 2", "held: State.addh(sc@uniq) CallReservationConflict_Kd",
                "addl: Lim.run() OwnershipDepthLimit_Kd", "limit: y0 = 2",
            ],
            Problems(c, program, DefaultCodes));
        Assert.Equal(
            ["Anchor sc@uniq", "Use State.add(sc@uniq)", "Reservation Root.counter()@uniq", "Evaluated State.poke(Root.counter()@uniq)"],
            FactsOf(c, program, "g171"));
        Assert.Equal(
            ["Anchor sc@ref", "Use State.g() + r.count", "Reservation Root.counter()@uniq", "Evaluated State.poke(Root.counter()@uniq)"],
            FactsOf(c, program, "f2"));
        Assert.Equal(0b11UL, program.Problems.All.ToArray().Single(x => program.Bodies[x.Body].Key.Decl?.Name == "gen").Views);
    }

    // V16 (R4-M97) on a rooted body: the caller's record of a copy's failure, in a loop (ACopysEntriesWarmRunAllocatesNothing's G171 form),
    // allocates nothing on a program version A5 checked before.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ACopysCallerRecordWarmRunAllocatesNothing()
    {
        var c = MirBuildTest.Compile(Prelude + CopyTwins + "    public func g171(c: bool)\n        var sc = Root.counter()\n        while c\n" +
            "            State.add(sc@uniq)\n" + CopyRoots);
        Assert.Empty(c.Binding.Issues);
        var program = c.BuildMir();
        program.Problems.Clear();
        var (bodies, stratum) = (Rooted(c, program), new Stratum(c.Types));
        Assert.Equal(0, AllocationMeasurement.Measure(() => Run(stratum, program, bodies)));
        Assert.Contains("g171: State.add(sc@uniq) CallReservationConflict_Kd", Problems(c, Sound(program), DefaultCodes));
    }

    // G136 N1: construction records must not hide an internal failure; an unsupported form is still an expected test outcome.
    [Fact]
    public void SoundRejectsInternalConstructionRecords()
    {
        var program = new MirProgram();
        Assert.Same(program, Sound(program));
        program.Report(default, DiagnosticCode.Unsupported_Kd);
        Assert.Same(program, Sound(program));
        program.Report(default, DiagnosticCode.InternalInvariant_Kd);
        Assert.ThrowsAny<XunitException>(() => Sound(program));
    }

    // The Loan problems and limit records (a located Unsupported_Kd, the depth limit) of an effect program's rooted bodies: the prelude, the
    // effect Types and group State with the given members, the markers, then the given declarations.
    private static string[] RootedEffects(string members, string declarations = "")
    {
        var c = MirBuildTest.Compile(Prelude + EffectTypes + members + EffectRoots + declarations);
        Assert.Empty(c.Binding.Issues);
        return Problems(c, AnalyzeRooted(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd, DiagnosticCode.OwnershipDepthLimit_Kd]);
    }

    // A5's work on a program's rooted bodies with no Loan problem.
    private static long RootedWork(string source)
    {
        var c = MirBuildTest.Compile(source);
        Assert.Empty(c.Binding.Issues);
        var program = c.BuildMir();
        program.Problems.Clear();
        var stratum = new Stratum(c.Types);
        Run(stratum, program, Rooted(c, program));
        Assert.DoesNotContain(Sound(program).Problems.All.ToArray(), static x => LoanCodes.Contains(x.Code));
        return stratum.Borrows.Work;
    }

    // A5 over the rooted bodies of a program (Rooted), after its summaries; the problems the build's driver found are cleared first.
    private static MirProgram AnalyzeRooted(Compilation c)
    {
        var program = c.BuildMir();
        program.Problems.Clear();
        Run(new Stratum(c.Types), program, Rooted(c, program));
        return Sound(program);
    }

    // A program after A5's runs over its rooted bodies: no validator finding or internal failure in construction or analysis
    // (InternalInvariant_Kd, which the codes the tests read leave out).
    private static MirProgram Sound(MirProgram program)
    {
        Assert.Empty(program.Findings);
        Assert.DoesNotContain(program.Records, static x => x.Code == DiagnosticCode.InternalInvariant_Kd);
        Assert.DoesNotContain(program.Problems.All.ToArray(), static x => x.Code == DiagnosticCode.InternalInvariant_Kd);
        return program;
    }

    private static void Run(Stratum stratum, MirProgram program, MirBody[] bodies)
    {
        foreach (var body in bodies)
        {
            stratum.Run(program, body);
        }
    }

    // Each body of the program's own module with its stand-ins rooted at their statics, validated: a stand-in is a Var or Temp local a call of
    // group Root initializes, standing for the static that function names, which a body of the program accesses, and of its Type up to
    // Origins (MirTypes.Agree, MIR's Type agreement). The copy leaves out the stand-in's storage statements, turns that call into the
    // static's staticEnsure (into a Unit temp of its own, the builder's form, which MirValidator requires of a static's Places, G191) and
    // the stand-in's Drop into a Goto to its target, and roots each of its Places at the static (so its projections are the static's);
    // every other row and the block numbers are the built body's, so A2's verdicts of the body (its Ordinal) apply. So a Borrow of a
    // stand-in is a Borrow of its static after the static's staticEnsure, as P42's source form has it. A4's summaries are the built
    // program's, so a twin function's lacks its stand-ins' static accesses; no witness composes one (copies are inlined).
    private static MirBody[] Rooted(Compilation c, MirProgram program)
    {
        var statics = program.Bodies.ToArray().SelectMany(static x => x.Statics.ToArray()).DistinctBy(static x => x.Symbol).ToArray();
        return [.. program.Bodies.ToArray().Where(static x => x.Source.Module == 0).Select(built => Validated(c, Copy(built)))];

        MirBody Copy(MirBody built)
        {
            var body = new MirBody();
            body.Reset(built.Key, built.Source);
            body.Ordinal = built.Ordinal;
            body.Binders.AddRange(built.Binders);
            foreach (var scope in built.Scopes[1..])
            {
                body.AddScope(scope.Outer, scope.Kind, scope.Source);
            }

            foreach (var entry in built.Statics)
            {
                body.Static(entry);
            }

            var roots = new int[built.Locals.Length]; // Each local's root in the copy: itself, or a stand-in's static.
            for (var i = 0; i < roots.Length; i++)
            {
                roots[i] = body.AddLocal(built.Locals[i]);
            }

            foreach (var block in built.Blocks)
            {
                if (Marker(block.Terminator) is { } name)
                {
                    var (stand, entry) = (block.Terminator.Place, Assert.Single(statics, x => x.Symbol.Name == name));
                    Assert.True(stand.Projection == 0 && built.Locals[stand.Root].Kind is MirLocalKind.Var or MirLocalKind.Temp);
                    Assert.True(MirTypes.Agree(entry.Type, built.Locals[stand.Root].Type));
                    roots[stand.Root] = body.Static(entry).Root;
                }
            }

            bool StandIn(MirPlace place) => place is { Root: >= 0, Projection: 0 } && roots[place.Root] < 0;
            int Chain(int root, int id)
            {
                if (id == 0)
                {
                    return 0;
                }

                var step = built.Projections[id];
                return body.Project(new(root, Chain(root, step.Prefix)), step.Kind, step.Arg).Projection;
            }

            MirPlace Map(MirPlace place)
            {
                var root = place.Root >= 0 ? roots[place.Root] : place.Root;
                return new(root, Chain(root, place.Projection));
            }

            foreach (var constant in built.Consts)
            {
                body.Const(constant);
            }

            foreach (var operand in built.Operands)
            {
                body.AddOperand(operand with { Place = Map(operand.Place) });
            }

            foreach (var item in built.Cases)
            {
                body.AddCase(item.Value, item.Target);
            }

            foreach (var site in built.OpaqueSites)
            {
                body.AddOpaque(site.Source, [.. built.OpaqueRoots.Slice(site.Start, site.Count).ToArray().Select(x => x >= 0 ? roots[x] : x)]);
            }

            foreach (var end in built.MustNotComplete)
            {
                body.AddMustNotComplete(end);
            }

            foreach (var block in built.Blocks)
            {
                _ = body.AddBlock();
            }

            for (var b = 0; b < built.Blocks.Length; b++)
            {
                var (block, terminator) = (built.Blocks[b], built.Blocks[b].Terminator);
                foreach (var statement in built.Statements.Slice(block.Start, block.Count))
                {
                    if (!(StandIn(statement.Place) && statement.Kind is MirStatementKind.StorageLive or MirStatementKind.StorageDead))
                    {
                        body.Push(b, statement with { Place = Map(statement.Place), Rvalue = statement.Rvalue with { Place = Map(statement.Rvalue.Place) } });
                    }
                }

                MirTerminator copied;
                if (Marker(terminator) is not null)
                {
                    var slot = body.AddOperand(MirOperand.Const(body.Const(new(MirConstKind.Integer, BoundType.ISize, ~roots[terminator.Place.Root]))));
                    var unit = new MirPlace(body.AddLocal(new(BoundType.Unit, MirLocalKind.Temp, terminator.Source)));
                    copied = MirTerminator.Call(MirCallee.Intrinsic(IntrinsicId.StaticEnsure), slot, 1, unit, terminator.Target, terminator.Source);
                }
                else
                {
                    copied = terminator.Kind == MirTerminatorKind.Drop && StandIn(terminator.Place)
                        ? MirTerminator.Goto(terminator.Target, terminator.Source, terminator.CheckTarget)
                        : terminator with { Place = Map(terminator.Place), Operand = terminator.Operand with { Place = Map(terminator.Operand.Place) } };
                }

                body.Terminate(b, copied);
            }

            return body;
        }

        // The static a stand-in's initializing call names: a call of group Root's function of that name.
        static string? Marker(in MirTerminator terminator) => terminator is { Kind: MirTerminatorKind.Call, Callee.Symbol: var function } &&
            function?.Declaration.Parent is GroupKoto { Name: "Root" } ? function.Name : null;
    }
}
