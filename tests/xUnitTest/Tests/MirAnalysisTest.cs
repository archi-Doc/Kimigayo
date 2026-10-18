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
    // One problem of each recording rule (MirEffectsTest's, MirDropTest's and MirBorrowsTest's forms).
    private const string Requirements = """
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
        public func main() => ()

        """;

    private static readonly DiagnosticRequirement Moved = DiagnosticRequirement.Ownership(OwnershipRequirement.PossiblyMovedUse);
    private static readonly DiagnosticRequirement Uninitialized = DiagnosticRequirement.Ownership(OwnershipRequirement.UninitializedUse);

    // R4-M3a: a problem of a code and subject added again ORs its views into the first; another code or subject is its own problem.
    [Fact]
    public void ARepeatedProblemMergesItsViews()
    {
        var report = new MirReport();
        report.Add(new(DiagnosticCode.MovedPlace_Kd, Moved, new(0, 5), 0b01));
        report.Add(new(DiagnosticCode.UninitializedPlace_Kd, Uninitialized, new(0, 5), 0b01));
        report.Add(new(DiagnosticCode.MovedPlace_Kd, Moved, new(0, 6), 0b01));
        report.Add(new(DiagnosticCode.MovedPlace_Kd, Moved, new(0, 5), 0b10));
        Assert.Equal(
            [
                new(DiagnosticCode.MovedPlace_Kd, Moved, new(0, 5), 0b11), new(DiagnosticCode.UninitializedPlace_Kd, Uninitialized, new(0, 5), 0b01),
                new MirProblem(DiagnosticCode.MovedPlace_Kd, Moved, new(0, 6), 0b01),
            ],
            report.All.ToArray());
        report.Clear();
        Assert.Equal(0, report.All.Length);
    }

    // R5-M23: the problems a key merges name one requirement, the first's; a problem that names another is a recording rule's defect, one
    // InternalInvariant_Kd problem at the subject with the views that disagreed, however often it disagrees.
    [Fact]
    public void AMergeNamesOneRequirement()
    {
        var report = new MirReport();
        report.Add(new(DiagnosticCode.MovedPlace_Kd, Moved, new(0, 5), 0b01));
        report.Add(new(DiagnosticCode.MovedPlace_Kd, Uninitialized, new(0, 5), 0b10));
        report.Add(new(DiagnosticCode.MovedPlace_Kd, Uninitialized, new(0, 5), 0b100));
        Assert.Equal(
            [
                new(DiagnosticCode.MovedPlace_Kd, Moved, new(0, 5), 0b01),
                new MirProblem(DiagnosticCode.InternalInvariant_Kd, DiagnosticRequirement.Ownership(OwnershipRequirement.Internal), new(0, 5), 0b110),
            ],
            report.All.ToArray());
    }

    // R5-M23, DIAGNOSTICS rule 1: each recording rule names the requirement it judged, beside its code: A1's uses, Moves without a Move
    // Path and second initializations of a let, A2's incomplete layer at a Drop, A3's falling failure body, A4's confined obligations as
    // Binding's row carries them (a conformance, a virtual function, a Callable binding) and its limit, and A5's Loan conflicts and its
    // limit (a destruction past the walk's bound). The storage limits need large bodies: A5ChecksTheViewsA1Completed has A1's.
    [Fact]
    public void EachRecordingRuleNamesItsRequirement()
    {
        var c = MirBuildTest.Compile(Requirements);
        Assert.Empty(c.Binding.Issues);
        Assert.Equal(
            [
                "ControlFlow RequireFallthrough_Kd ()", "Ownership.CallableEffectBound UnsatisfiedEffectBound_Kd noisy",
                "Ownership.ComparisonLoanConflict ComparisonLoanConflict_Kd x = 2",
                "Ownership.EffectBound IncompatibleContractImplementation_Kd Self is Sink", "Ownership.PossiblyMovedUse MovedPlace_Kd r",
                "Ownership.PossiblyMovedUse MovedPlace_Kd var box = Box.init(make())", "Ownership.ReassignedLet ReassignedLet_Kd resource = make()",
                "Ownership.StaticMovePathRequired StaticMovePathRequired_Kd values[i]", "Ownership.UninitializedUse UninitializedPlace_Kd n",
                "Ownership.Unsupported Unsupported_Kd Self is Store", "Ownership.Unsupported Unsupported_Kd func deep(n: Nest<ref/i32 during a>)",
                "Ownership.VirtualEffectBound UnsatisfiedEffectBound_Kd override func read(self: objref/Self) -> i32",
            ],
            c.BuildMir().Problems.All.ToArray().Where(static x => x.Subject.Module == 0)
                .Select(x => $"{x.Requirement} {x.Code} {c.SyntaxOf(x.Subject).ToString().Split('\n')[0].Trim()}").Order(StringComparer.Ordinal));
    }

    // R4-M1: the analyses run once per program version: building an unchanged version again keeps its problems, and a new Binding pass
    // starts without any.
    [Fact]
    public void ProblemsBelongToTheirVersion()
    {
        var (c, _) = SemanticSnapshotTest.Bind("milestone/Milestone1");
        var program = c.BuildMir();
        program.Problems.Add(new(DiagnosticCode.MovedPlace_Kd, Moved, program.Bodies[0].Source, 1));
        Assert.Equal(1, c.BuildMir().Problems.All.Length);
        c.Binding.Bind(BindingMode.Final);
        Assert.Equal(0, c.BuildMir().Problems.All.Length);
    }

    // R4-M15a, design A3 6.1: A5 runs as the driver's step 4 over the views A1 and A2 completed, which A2's table publishes (ask A-2):
    // every view of a body, two for a pair-generic one, so a Loan conflict is a problem of the version, and none of a body whose A1 arena
    // exceeds the limit (N locals live across N branches, MirInitTest), which records only OwnershipStorageLimit_Kd, though A5's own arena
    // would fit; N reference locals live across N branches fit A1's arena but not A5's, whose own OwnershipStorageLimit_Kd stops it.
    [Fact]
    public void A5ChecksTheViewsA1Completed()
    {
        const string Scalars = "    var v{0} = {0}\n|    if flag => v{0} = 1\n|"; // Each part's line N times, with its index.
        const string References = "    var r{0} = x@ref\n|    if flag => r{0} = y@ref\n|    inspect(r{0})\n";
        const string Limit = "Ownership.StorageLimit OwnershipStorageLimit_Kd func f(flag: bool) -> i32";
        foreach (var (size, views, line, expected) in new[]
        {
            (64, 1, Scalars, "Ownership.ComparisonLoanConflict ComparisonLoanConflict_Kd x = 2"),
            (13_500, 0, Scalars, Limit),
            (2_000, 1, References, Limit),
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
            Assert.Equal([expected], program.Problems.All.ToArray().Where(static x => x.Subject.Module == 0)
                .Select(x => $"{x.Requirement} {x.Code} {c.SyntaxOf(x.Subject).ToString().Split('\n')[0].Trim()}"));
            Assert.Equal(
                [("f", (long)views), ("inspect", 1L), ("pair", 2L)],
                program.Bodies.ToArray().Where(static x => x.Key.Decl?.Name is "f" or "inspect" or "pair")
                    .Select(x => (x.Key.Decl!.Name, program.Drops.ViewsOf(x.Ordinal))).Order());
        }
    }

    // R4-M12, V17: the library bodies have no problems.
    [Fact]
    public void TheLibraryHasNoProblems()
    {
        var (c, _) = SemanticSnapshotTest.Bind("library");
        Assert.Equal(0, c.BuildMir().Problems.All.Length);
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
                report.Add(i % 2 == 0 ? new(DiagnosticCode.MovedPlace_Kd, Moved, new(0, (i % 48) + 1), 1UL << (i % 3))
                    : new(DiagnosticCode.UninitializedPlace_Kd, Uninitialized, new(0, (i % 48) + 1), 1UL << (i % 3)));
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
}
