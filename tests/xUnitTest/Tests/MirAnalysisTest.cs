// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>The MIR analyses' driver and problem record (docs/dev/DECISIONS.md R4-M2a, R4-M3a, R5-M23): <see cref="MirAnalysis"/> runs once
/// per program version, and <see cref="MirReport"/> keeps one problem per code and subject with its requirement and the views that have it;
/// the analyses' costs (V16), A3's (<see cref="MirLints"/>, R4-M7a) first.</summary>
public sealed class MirAnalysisTest
{
    // One problem of each recording rule (MirEffectsTest's, MirDropTest's and MirBorrowsTest's forms, A5's effect rule with A4's keys: R3,
    // a static's destruction reading itself, which needs no Loan, as Binding reports a static's Borrows as Unsupported_Kd until P42).
    internal const string Requirements = """
        contract Sink
            func put(self: uniq/Self, value: i32) -> i32
                effect confined
        struct S
            Self is Sink
            public func put(self: uniq/Self, value: i32) -> i32
                H.discard(Loud.init())
                return value
        func apply<F>(f: ref/F) -> i32
            F is Callable<() -> i32>
                effect confined
            return f()
        func noisy() -> i32
            Console.writeLine("noisy")
            return 0
        func entry() -> i32 => apply(noisy)
        struct Loud
            public var n: i32 = 0
            drop => Console.writeLine("drop")
        group H
            public func discard<T>(item: T) => ()
        open struct Base
            public virtual func read(self: objref/Self) -> i32
                effect confined
                return 1
        struct Derived : Base
            override func read(self: objref/Self) -> i32
                H.discard(Loud.init())
                return 1
        struct Resource
            var name: string
            public init(name: string) => self.name = name@move
        func make() -> Resource => Resource.init("r")
        func consume(r: Resource) => ()
        struct Box
            public var value: Resource
            public init(value: Resource) => self.value = value@move
            drop => ()
        func layer()
            var box = Box.init(make())
            consume(box.value@move)
        func moved(r: Resource)
            consume(r@move)
            consume(r@move)
        func twice()
            let resource: Resource
            resource = make()
            consume(resource@move)
            resource = make()
        func take(s: string) => ()
        func runtime(values: Array<string>, i: isize)
            take(values[i]@move)
        struct Nest<T>
            public var value: T
            public var next: Option<rc/Nest<(T, T)>> = .None
            public init(value: T) => self.value = value
        contract Store
            func keep(self: uniq/Self, value: i32) -> i32
                effect confined
        struct Deep
            Self is Store
            public func keep(self: uniq/Self, value: i32) -> i32
                _ = Nest<i32>.init(value: 1)
                return value
        func deep(n: Nest<ref/i32 during a>)
            let r = n@ref
        func unset(flag: bool) -> i32
            var n: i32
            if flag => n = 1
            return n
        func falls(flag: bool)
            require flag else => ()
        func inspect(value: ref/i32) => ()
        func conflict()
            var x = 1
            let r = x@ref
            x = 2
            inspect(r)
        struct Tally
            public var n: i32 = 0
            public init() => ()
            drop => _ = Statics.tally.n
        group Statics
            public var tally: Tally = Tally.init()
            public func effect() => tally = Tally.init()
        public func main() => ()

        """;

    private static readonly DiagnosticRequirement Moved = DiagnosticRequirement.Ownership(OwnershipRequirement.PossiblyMovedUse);
    private static readonly DiagnosticRequirement Uninitialized = DiagnosticRequirement.Ownership(OwnershipRequirement.UninitializedUse);

    // R4-M3a, R4-M71: a problem of a code, subject and body added again ORs its views into the first; another code, subject or body is its
    // own problem, so the views of one body's cases are never ORed into another's. Add returns the problem's index, the first's on a merge
    // (R4-M132).
    [Fact]
    public void ARepeatedProblemMergesItsViews()
    {
        var report = new MirReport();
        int[] indices =
        [
            report.Add(new(DiagnosticCode.MovedPlace_Kd, Moved, new(0, 5), 0, 0b01)),
            report.Add(new(DiagnosticCode.UninitializedPlace_Kd, Uninitialized, new(0, 5), 0, 0b01)),
            report.Add(new(DiagnosticCode.MovedPlace_Kd, Moved, new(0, 6), 0, 0b01)),
            report.Add(new(DiagnosticCode.MovedPlace_Kd, Moved, new(0, 5), 1, 0b100)),
            report.Add(new(DiagnosticCode.MovedPlace_Kd, Moved, new(0, 5), 0, 0b10)),
        ];
        Assert.Equal([0, 1, 2, 3, 0], indices);
        Assert.Equal(
            [
                new(DiagnosticCode.MovedPlace_Kd, Moved, new(0, 5), 0, 0b11), new(DiagnosticCode.UninitializedPlace_Kd, Uninitialized, new(0, 5), 0, 0b01),
                new(DiagnosticCode.MovedPlace_Kd, Moved, new(0, 6), 0, 0b01), new MirProblem(DiagnosticCode.MovedPlace_Kd, Moved, new(0, 5), 1, 0b100),
            ],
            report.All.ToArray());
        report.Clear();
        Assert.Equal(0, report.All.Length);
    }

    // R5-M23: the problems a key merges name one requirement, the first's; a problem that names another is a recording rule's defect, one
    // InternalInvariant_Kd problem at the subject with the views that disagreed, however often it disagrees, and Add returns -1 for it.
    [Fact]
    public void AMergeNamesOneRequirement()
    {
        var report = new MirReport();
        int[] indices =
        [
            report.Add(new(DiagnosticCode.MovedPlace_Kd, Moved, new(0, 5), 0, 0b01)),
            report.Add(new(DiagnosticCode.MovedPlace_Kd, Uninitialized, new(0, 5), 0, 0b10)),
            report.Add(new(DiagnosticCode.MovedPlace_Kd, Uninitialized, new(0, 5), 0, 0b100)),
        ];
        Assert.Equal([0, -1, -1], indices);
        Assert.Equal(
            [
                new(DiagnosticCode.MovedPlace_Kd, Moved, new(0, 5), 0, 0b01),
                new MirProblem(DiagnosticCode.InternalInvariant_Kd, DiagnosticRequirement.Ownership(OwnershipRequirement.Internal), new(0, 5), 0, 0b110),
            ],
            report.All.ToArray());
    }

    // R4-M69 (SPEC 7.2.3, 23.1.1): a problem with an Inlined fact, a Language problem found inside an inline default copy, is its template's
    // check and is dropped (Add returns -1); a limit record, which a copy records without that fact, stays, as does the template's own
    // problem.
    [Fact]
    public void AnInlinedProblemIsDropped()
    {
        var report = new MirReport();
        var loan = new MirProblem(DiagnosticCode.ComparisonLoanConflict_Kd, DiagnosticRequirement.Ownership(OwnershipRequirement.ComparisonLoanConflict), new(0, 5), 0, 0b10);
        var limit = new MirProblem(DiagnosticCode.Unsupported_Kd, DiagnosticRequirement.Ownership(OwnershipRequirement.Unsupported), new(0, 5), 0, 0b10);
        int[] indices =
        [
            report.Add(loan, [new(MirFactKind.Anchor, new(0, 4)), new(MirFactKind.Inlined, new(0, 9))]),
            report.Add(limit),
            report.Add(loan with { Views = 0b01 }, [new(MirFactKind.Anchor, new(0, 4))]),
        ];
        Assert.Equal([-1, 0, 1], indices);
        Assert.Equal([limit, loan with { Views = 0b01 }], report.All.ToArray());
        Assert.Equal([new MirFact(MirFactKind.Anchor, new(0, 4))], report.Facts(1).ToArray());
    }

    // DECISIONS R4-M132 (rule W; R4-M96 per report, DIAGNOSTICS §4.3): a merged problem is derived exactly when every report of it is, its
    // Cause facts (their tail) the union of the reports' causes: a derived problem meeting a direct report becomes direct and stays so, two
    // derived reports keep their causes once each, the first's span moved after another problem's facts, so the other problem keeps its own.
    [Fact]
    public void AMergeIsDerivedOnlyWhenEveryReportIs()
    {
        var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.ComparisonLoanConflict);
        var (loan, other) = (new MirProblem(DiagnosticCode.ComparisonLoanConflict_Kd, requirement, new(0, 5), 0, 0b01), new MirProblem(
            DiagnosticCode.ComparisonLoanConflict_Kd, requirement, new(0, 6), 0, 0b01));
        var (anchor, first, second) = (new MirFact(MirFactKind.Anchor, new(0, 4)), new MirFact(MirFactKind.Cause, new(0, 1), 7), new MirFact(
            MirFactKind.Cause, new(0, 2), 8));
        var report = new MirReport();
        Assert.Equal([0, 1, 0, 0, 0], new[]
        {
            report.Add(loan, [anchor, first]), report.Add(other, [anchor]), report.Add(loan with { Views = 0b10 }, [anchor, second]),
            report.Add(loan, [anchor, first, second]), report.Add(loan, [anchor, second]),
        });
        Assert.Equal([anchor, first, second], report.Facts(0).ToArray());
        Assert.Equal([anchor], report.Facts(1).ToArray());
        Assert.Equal(0b11UL, report.All[0].Views);
        Assert.Equal(0, report.Add(loan, [anchor]));
        Assert.Equal([anchor], report.Facts(0).ToArray());
        Assert.Equal(0, report.Add(loan, [anchor, first]));
        Assert.Equal([anchor], report.Facts(0).ToArray());
    }

    // R4-M69 (G125): the Language problems of a default's inline copies are its template's: A5's conflict in the default of the 1c design's
    // p11 and A1's and A2's in another keep the template's one view, though a caller of two views and another caller call both; a limit
    // record a copy finds stays (SPEC 23.1.1): A5's at a call whose contract Binding does not instantiate (the depth limit, R4-M153), one
    // problem in the template and one with the two-view copy's views (R4-M71).
    [Fact]
    public void ADefaultsProblemsAreItsTemplates()
    {
        var c = MirBuildTest.Compile("""
            struct Resource
                var name: string
                public init(name: string) => self.name = name@move
            func make() -> Resource => Resource.init("r")
            func consume(r: Resource) => ()
            struct Box
                public var value: Resource
                public init(value: Resource) => self.value = value@move
                drop => ()
            func f(n: i32, y: i32 = label w: do
                var a = 1
                let r = a@ref
                a = 2
                exit to w r@follow
            ) => ()
            func g(n: i32, y: i32 = label w: do
                var box = Box.init(make())
                consume(box.value@move)
                let s = make()
                consume(s@move)
                consume(s@move)
                exit to w 1
            ) => ()
            struct Nest<T>
                public var value: T
                public var next: Option<rc/Nest<(T, T)>> = .None
                public init(value: T) => self.value = value
            func h(n: i32, y: i32 = label w: do
                var z = 1
                let nest = Nest<ref/i32>.init(value: z@ref)
                exit to w 1
            ) => ()
            func pair<s/T>(v: s/T)
                s is ref or uniq
                f(1)
                g(1)
                h(1)
            func c1()
                f(1)
                f(2)
                g(2)
            func c2() => f(3)

            """);
        Assert.Empty(c.Binding.Issues);
        Assert.Equal(
            [
                "ComparisonLoanConflict_Kd a = 2 1", "MovedPlace_Kd s 1", "MovedPlace_Kd var box = Box.init(make()) 1",
                "OwnershipDepthLimit_Kd Nest<ref/i32>.init(value: z@ref) 1", "OwnershipDepthLimit_Kd Nest<ref/i32>.init(value: z@ref) 3",
            ],
            c.BuildMir().Problems.All.ToArray().Where(static x => x.Subject.Module == 0)
                .Select(x => $"{x.Code} {c.SyntaxOf(x.Subject).ToString().Split('\n')[0].Trim()} {x.Views}").Order(StringComparer.Ordinal));
    }

    // R4-M71 (G125, the 1c design review's r05): a Field initializer is built into each constructor, so its conflict is one problem per
    // constructor body with that body's views: S's two constructors have one view each, G's two enumerate two and nine cases. One problem
    // with the bodies' views ORed (0x1FF for G) loses which constructor's case a bit names.
    [Fact]
    public void AFieldInitializersProblemIsEachConstructors()
    {
        var c = MirBuildTest.Compile("""
            struct S
                public var v: i32 = label w: do
                    var a = 1
                    let r = a@ref
                    a = 2
                    exit to w r@follow
                public var k: i32
                public init(k: i32) => self.k = k
                public init(k: i32, extra: i32) => self.k = k + extra
            struct G<s/T>
                public var v: i32 = label w: do
                    var a = 1
                    let r = a@ref
                    a = 2
                    exit to w r@follow
                public var t: s/T
                public init(t: s/T)
                    s is ref or uniq
                    self.t = t
                public init(t: s/T, n: i32)
                    self.t = t

            """);
        Assert.Empty(c.Binding.Issues);
        var program = c.BuildMir();
        var problems = program.Problems.All.ToArray().Where(static x => x.Subject.Module == 0).ToArray();
        Assert.Equal(
            [
                "ComparisonLoanConflict_Kd a = 2 in public init(k: i32) => self.k = k: 1",
                "ComparisonLoanConflict_Kd a = 2 in public init(k: i32, extra: i32) => self.k = k + extra: 1",
                "ComparisonLoanConflict_Kd a = 2 in public init(t: s/T): 3", "ComparisonLoanConflict_Kd a = 2 in public init(t: s/T, n: i32): 511",
            ],
            problems.Select(x => $"{x.Code} {Line(c, x.Subject)} in {Line(c, program.Bodies[x.Body].Source)}: {x.Views}"));
        Assert.All(problems, x => Assert.Equal(0UL, x.Views >> (int)MirCases.Count(program.Bodies[x.Body])));
    }

    // R5-M23, DIAGNOSTICS rule 1: each recording rule names the requirement it judged, beside its code: A1's uses, Moves without a Move
    // Path and second initializations of a let, A2's incomplete layer at a Drop, A3's falling failure body, A4's confined obligations as
    // Binding's row carries them (a conformance, a virtual function, a Callable binding) and its depth limit, and A5's Loan conflicts and
    // its depth limit (a destruction past the walk's bound; R4-M153) and its effect rule, which the driver gives A4's keys (R4-M92). The
    // storage limits need large bodies: A5ChecksTheViewsA1Completed has A1's. A parameter's record is at its Type occurrence
    // (DECISIONS BU-48's locations, settled (a): R4-M90 D6's rule extended).
    [Fact]
    public void EachRecordingRuleNamesItsRequirement()
    {
        var c = MirBuildTest.Compile(Requirements);
        Assert.Empty(c.Binding.Issues);
        Assert.Equal(
            [
                "ControlFlow RequireFallthrough_Kd ()", "Ownership.CallableEffectBound UnsatisfiedEffectBound_Kd noisy",
                "Ownership.ComparisonLoanConflict ComparisonLoanConflict_Kd tally = Tally.init()",
                "Ownership.ComparisonLoanConflict ComparisonLoanConflict_Kd x = 2",
                "Ownership.DepthLimit OwnershipDepthLimit_Kd Nest<ref/i32 during a>",
                "Ownership.DepthLimit OwnershipDepthLimit_Kd Self is Store",
                "Ownership.EffectBound IncompatibleContractImplementation_Kd Self is Sink", "Ownership.PossiblyMovedUse MovedPlace_Kd r",
                "Ownership.PossiblyMovedUse MovedPlace_Kd var box = Box.init(make())", "Ownership.ReassignedLet ReassignedLet_Kd resource = make()",
                "Ownership.StaticMovePathRequired StaticMovePathRequired_Kd values[i]", "Ownership.UninitializedUse UninitializedPlace_Kd n",
                "Ownership.VirtualEffectBound UnsatisfiedEffectBound_Kd override func read(self: objref/Self) -> i32",
            ],
            c.BuildMir().Problems.All.ToArray().Where(static x => x.Subject.Module == 0)
                .Select(x => $"{x.Requirement} {x.Code} {c.SyntaxOf(x.Subject).ToString().Split('\n')[0].Trim()}").Order(StringComparer.Ordinal));
    }

    // R4-M71: each recording rule names the body whose views number its problem's views: A1's, A2's, A3's and A5's the body they analyze,
    // and A4's obligation rows (a conformance, a virtual function, a Callable binding and its depth limit) none, since they lie at Binding's
    // rows.
    // A parameter's record is at its Type occurrence (DECISIONS BU-48's locations, settled (a): R4-M90 D6's rule extended).
    [Fact]
    public void EachRecordingRuleNamesItsBody()
    {
        var c = MirBuildTest.Compile(Requirements);
        var program = c.BuildMir();
        Assert.Equal(
            [
                "ComparisonLoanConflict_Kd tally = Tally.init() effect", "ComparisonLoanConflict_Kd x = 2 conflict",
                "IncompatibleContractImplementation_Kd Self is Sink -",
                "MovedPlace_Kd r moved",
                "MovedPlace_Kd var box = Box.init(make()) layer", "OwnershipDepthLimit_Kd Nest<ref/i32 during a> deep",
                "OwnershipDepthLimit_Kd Self is Store -",
                "ReassignedLet_Kd resource = make() twice", "RequireFallthrough_Kd () falls",
                "StaticMovePathRequired_Kd values[i] runtime", "UninitializedPlace_Kd n unset", "UnsatisfiedEffectBound_Kd noisy -",
                "UnsatisfiedEffectBound_Kd override func read(self: objref/Self) -> i32 -",
            ],
            program.Problems.All.ToArray().Where(static x => x.Subject.Module == 0)
                .Select(x => $"{x.Code} {Line(c, x.Subject)} {(x.Body == MirProblem.NoBody ? "-" : program.Bodies[x.Body].Key.Decl!.Name)}")
                .Order(StringComparer.Ordinal));
    }

    // R4-M1: the analyses run once per program version: building an unchanged version again keeps its problems, and a new Binding pass
    // starts without any.
    [Fact]
    public void ProblemsBelongToTheirVersion()
    {
        var (c, _) = SemanticSnapshotTest.Bind("milestone/Milestone1");
        var program = c.BuildMir();
        program.Problems.Add(new(DiagnosticCode.MovedPlace_Kd, Moved, program.Bodies[0].Source, 0, 1));
        Assert.Equal(1, c.BuildMir().Problems.All.Length);
        c.Binding.Bind(BindingMode.Final);
        Assert.Equal(0, c.BuildMir().Problems.All.Length);
    }

    // R4-M15a, design A3 6.1: A5 runs as the driver's step 4 over the views A1 and A2 completed, which A2's table publishes (ask A-2):
    // every view of a body, two for a pair-generic one, so a Loan conflict is a problem of the version, and none of a body whose A1 arena
    // exceeds the limit (N locals live across N branches, MirInitTest), which records only OwnershipStorageLimit_Kd, though A5's own arena
    // would fit; N reference locals live across N branches fit A1's arena but not A5's, whose own OwnershipStorageLimit_Kd stops it; each
    // limit names its arena (G213, R4-M132).
    [Fact]
    public void A5ChecksTheViewsA1Completed()
    {
        const string Scalars = "    var v{0} = {0}\n|    if flag => v{0} = 1\n|"; // Each part's line N times, with its index.
        const string References = "    var r{0} = x@ref\n|    if flag => r{0} = y@ref\n|    inspect(r{0})\n";
        const string Limit = "Ownership.StorageLimit OwnershipStorageLimit_Kd func f(flag: bool) -> i32";
        foreach (var (size, views, line, expected) in new[]
        {
            (64, 1, Scalars, "Ownership.ComparisonLoanConflict ComparisonLoanConflict_Kd x = 2"),
            (13_500, 0, Scalars, Limit + " Initialization over"),
            (2_000, 1, References, Limit + " Loans over"),
        })
        {
            var text = new StringBuilder("func inspect(value: ref/i32) => ()\nfunc pair<s/T>(p: s/T) -> ()\n    s is owner or uniq\n    let v = p@move\n")
                .Append("func f(flag: bool) -> i32\n    var x = 1\n    var y = 2\n    let r = x@ref\n");
            foreach (var part in line.Split('|'))
            {
                for (var n = 0; n < size; n++)
                {
                    text.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, part, n);
                }
            }

            var c = MirBuildTest.Compile(text.Append("    x = 2\n    inspect(r)\n    return x\n").ToString());
            var program = c.BuildMir();
            Assert.Equal([expected], program.Problems.All.ToArray().Select(static (x, i) => (Problem: x, Index: i))
                .Where(static x => x.Problem.Subject.Module == 0)
                .Select(x => $"{x.Problem.Requirement} {x.Problem.Code} {c.SyntaxOf(x.Problem.Subject).ToString().Split('\n')[0].Trim()}" +
                    (x.Problem.Code == DiagnosticCode.OwnershipStorageLimit_Kd ? $" {MirDataflowTest.Limit(program.Problems, x.Index)}" : string.Empty)));
            Assert.Equal(
                [("f", (long)views), ("inspect", 1L), ("pair", 2L)],
                program.Bodies.ToArray().Where(static x => x.Key.Decl?.Name is "f" or "inspect" or "pair")
                    .Select(x => (x.Key.Decl!.Name, program.Drops.ViewsOf(x.Ordinal))).Order());
        }
    }

    // G308 S3 (V16): the analyses (A1-A6) of the program of P_n's equality (MirBuildTest.Dag) at n = 16 and 20 have no problem. Fails before
    // G308: the comparison's body builds its 2^n leaves, whose arena exceeds A1's, OwnershipStorageLimit_Kd at n = 16.
    [Fact]
    public void ADagComparisonHasNoAnalysisProblem()
    {
        foreach (var n in new[] { 16, 20 })
        {
            var c = MirBuildTest.Compile(MirBuildTest.Dag(n, MirBuildTest.DagOperator));
            var program = c.BuildMir();
            Assert.Empty(program.Findings);
            Assert.Equal([], program.Problems.All.ToArray().Select(static (x, i) => (Problem: x, Index: i)).Where(static x => x.Problem.Subject.Module == 0)
                .Select(x => $"n = {n}: {x.Problem.Requirement} {x.Problem.Code} {Line(c, x.Problem.Subject)}" +
                    (x.Problem.Code == DiagnosticCode.OwnershipStorageLimit_Kd ? $" {MirDataflowTest.Limit(program.Problems, x.Index)}" : string.Empty)));
        }
    }

    // DECISIONS R5-M168: at check, steps 1 and 4 (A3, A5) judge only the root modules' bodies, so this is the library's A3 and A5 check, on
    // each target, at full strength through the driver check uses with the library as a root module: a library construction record,
    // validator finding or analysis problem fails it (the library ships with the compiler), A1's arena limit and the C1-F4 view's findings
    // included. Every unit changing A3 or A5 runs it.
    [Theory]
    [InlineData(WindowsProfile.Target)]
    public void TheLibraryHasNoAnalysisProblem(string target)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(target));
        c.Kotonoha.AddSource(new SourceDocument("Program.kimi", "public func main() => ()\n"));
        Assert.True(c.Bind().IsComplete);
        var program = c.BuildMir();
        var library = c.SourceModules.Length;
        Assert.Contains(program.Bodies.ToArray(), x => x.Source.Module == library);
        new MirAnalysis(c.Types).Run(program, library + 1);
        Assert.Equal([], program.Records.Select(x => $"record {x.Code} {Line(c, x.Subject)}")
            .Concat(program.Findings.Select(x => $"finding {x.Rule} {Line(c, x.Subject)}"))
            .Concat(program.Problems.All.ToArray().Select(x =>
                $"{x.Requirement} {x.Code} in {(x.Body >= 0 ? program.Bodies[x.Body].Key.Decl?.Name : "-")}: {Line(c, x.Subject)}")));
    }

    // V16: a warm report allocates nothing: clearing keeps the storage the problems of a version need.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmReportAllocatesNothing()
    {
        var report = new MirReport();
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            report.Clear();
            for (var i = 0; i < 64; i++)
            {
                report.Add(i % 2 == 0 ? new(DiagnosticCode.MovedPlace_Kd, Moved, new(0, (i % 48) + 1), 0, 1UL << (i % 3))
                    : new(DiagnosticCode.UninitializedPlace_Kd, Uninitialized, new(0, (i % 48) + 1), 0, 1UL << (i % 3)));
            }
        }));
        Assert.Equal(64 - 16, report.All.Length);
    }

    // V16: A3 on a body it judged before allocates nothing: its walk and the report keep their storage.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmLintRunAllocatesNothing()
    {
        var body = MirBuildTest.Compile(RequireEnds(8)).BuildMir().Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "nest");
        var (lints, report) = (new MirLints(new MirReach()), new MirReport());
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            report.Clear();
            lints.Run(body, report);
        }));
        Assert.Equal(8, report.All.Length);
    }

    // V16, DECISIONS R4-M7a: N falling requires in sequence and N nested are N problems each, every one in every view, and the MIR and the
    // cold build's allocation, A3's included, grow linearly with N.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RequireEndsScaleLinearly()
    {
        var (sizes, work, allocated) = (new[] { 16, 32, 64 }, new long[3], new long[3]);
        for (var i = 0; i < sizes.Length; i++)
        {
            var c = MirBuildTest.Compile(RequireEnds(sizes[i]));
            var before = GC.GetAllocatedBytesForCurrentThread();
            var program = c.BuildMir();
            allocated[i] = GC.GetAllocatedBytesForCurrentThread() - before;
            work[i] = program.Bodies.ToArray().Sum(static x => x.Blocks.Length + x.Statements.Length);
            var problems = program.Problems.All.ToArray();
            Assert.All(problems, static x => Assert.Equal((DiagnosticCode.RequireFallthrough_Kd, ulong.MaxValue), (x.Code, x.Views)));
            Assert.Equal([sizes[i], sizes[i]], new[] { "sequence", "nest" }.Select(name => problems.Count(x => Function(c.SyntaxOf(x.Subject)) == name)));
        }

        Assert.True(work[2] - work[1] <= (2 * (work[1] - work[0])) + 8, $"MIR size {string.Join(", ", work)}");
        Assert.True(allocated[2] - allocated[1] <= 3 * (allocated[1] - allocated[0]), $"cold build allocation {string.Join(", ", allocated)}");

        static string? Function(Koto node) => node is FunctionKoto function ? function.Name : node.Parent is { } parent ? Function(parent) : null;
    }

    // N requires whose failure bodies complete: in sequence in sequence(), nested in nest().
    private static string RequireEnds(int n)
    {
        var text = new StringBuilder("func sequence(flag: bool)\n");
        for (var i = 0; i < n; i++)
        {
            text.Append("    require flag else => ()\n");
        }

        text.Append("func nest(flag: bool)\n");
        for (var i = 1; i <= n; i++)
        {
            text.Append(' ', 4 * i).Append("require flag else\n");
        }

        return text.Append(' ', 4 * (n + 1)).Append("()\n").ToString();
    }

    // The first line of a syntax's text.
    private static string Line(Compilation c, SourceRef syntax) => c.SyntaxOf(syntax).ToString().Split('\n')[0].Trim();
}
