// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>PLAN G950's kept path (design §R1, coverage rows g7, g8, M1, M2 and the focused re-review's M4): a later version of one compilation gives what a fresh
/// MIR pipeline gives over the same compilation state, and the code its first version generated, whether the library's MIR segment is
/// built again (a version that ends its segment, R-K2'') or kept (G950-K with G951 U2-K-b). The fresh pipeline
/// is built in Compilation's configuration (PLAN G951 U2: the source modules' root range, collected through Compilation.Collect's loop). M3, the library
/// check over two versions, is <see cref="MirAnalysisTest.TheLibraryHasNoAnalysisProblem"/> and, for the collector's A3b with a record,
/// <see cref="InstanceCollectorTest.TheLibraryRangeChecksALibraryBodyInEveryVersion"/>; the analysis's replay of the library step's
/// problems is <see cref="MirAnalysisTest.ALibraryStepProblemIsInEveryVersion"/>. Labels (SPEC, before any output): the
/// corpora's own, tests/mir's MirDumpTest's (valid but the three syntax-recovery witnesses), the diagnostic snapshot's cases their
/// corpora's (milestones and examples valid; mutation and syntax cases as tests/diagnostics records them); Bad invalid (SPEC 23.1.2's
/// example: `1` does not fit string, one independent Language Error), and Hello, Milestones 26, 27, 30 and 31 valid. M4's: Confined
/// invalid (SPEC 8.11.2 through 15.6.4's transitive summary: S.put implements Sink.put, `effect confined`, and reaches the library's
/// Console.writeLine through H.a, H.b and H.c; IncompatibleContractImplementation_Kd at `Self is Sink`), Pure, its twin whose H.c calls
/// nothing, valid.</summary>
public sealed class G950KeptPathTest
{
    // The library's generic swap erased at a declared Function Type (SPEC 10.5.3, 7.7.4) over a Type: the Back's program for row K1-K.
    private const string Swap = "let action: (uniq/{0}, uniq/{0}) -> () = ::Kimi.Intrinsics.swap\n";

    // The programs the class writes: Bad, row M4's (the focused re-review), whose record rests on a library callee's A4 summary, and
    // PLAN G951 U2-K's: X3 (MirBuildTest's) and swap at u8 and at 70 nested arrays.
    private static readonly Dictionary<string, string> Own = new(StringComparer.Ordinal)
    {
        ["bad"] = "let text: string = 1\n::Kimi.Console.writeLine(\"Hello, world!\")\n",
        ["confined"] = MirEffectsTest.Transitive("    public func c() => Console.writeLine(\"c\")\n") + "public func main() => ()\n",
        ["pure"] = MirEffectsTest.Transitive("    public func c() => ()\n") + "public func main() => ()\n",
        ["x1"] = MirAnalysisTest.LazySource("X1"),
        ["x2"] = MirAnalysisTest.LazySource("X2"),
        ["x3"] = MirBuildTest.StopwatchAction,
        ["swap"] = string.Format(Swap, "u8"),
        ["swap70"] = string.Format(Swap, string.Concat(Enumerable.Repeat("[1 of ", 70)) + "u8" + new string(']', 70)),
    };

    // g7's sequences: each tests/mir program after a warm rebind and after Provisional then Final; the design review's six edits into
    // another program, a program Binding rejects edited into a valid one and back (design §R1.2); and M4's programs in each form, each
    // edited into the other.
    public static TheoryData<string, string> Sequences
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var name in MirDumpTest.Programs)
            {
                data.Add("mir/" + name.Data, "warm");
                data.Add("mir/" + name.Data, "provisional");
            }

            data.Add("milestone/Milestone27", "example/Hello");
            data.Add("example/Hello", "milestone/Milestone27");
            data.Add("milestone/Milestone30", "milestone/Milestone26");
            data.Add("milestone/Milestone26", "milestone/Milestone31");
            data.Add("bad", "example/Hello");
            data.Add("example/Hello", "bad");
            foreach (var (program, other) in new[] { ("confined", "pure"), ("pure", "confined") })
            {
                data.Add(program, "warm");
                data.Add(program, "provisional");
                data.Add(program, other);
            }

            return data;
        }
    }

    // M1's programs: tests/mir's, generated as libraries, and the milestones with the library's generic instances with drops (Array,
    // Dictionary), generated as applications.
    public static TheoryData<string> Generated
        => new(MirDumpTest.Programs.Select(static x => "mir/" + x.Data).Concat(new[] { 26, 27, 30, 31 }.Select(static n => $"milestone/Milestone{n}")));

    // g7 (design §R1.2) and M4: after a later version (a warm rebind, Provisional then Final, or an edit into another program), the
    // compilation's own MIR equals a fresh builder's, analysis's and collector's over the same compilation state: every report row with its
    // facts (M4's record rests on Console.writeLine's A4 summary), every finding, the program's and the library's dumps with A2's tables,
    // and the checked set by every part of each item's key (N10) with each item's targets and their runtime bits (RN5).
    [Theory]
    [MemberData(nameof(Sequences))]
    public void ALaterVersionEqualsAFreshPipeline(string program, string then)
    {
        var (own, fresh) = Later(program, then);
        Assert.Equal(fresh, own);
    }

    // PLAN G951 U2-K (the design review's N1 rows on the product path; their kept-path twins are U2-K-b's): every version of
    // a longer sequence equals a fresh pipeline, with the library's bodies at their fixed ordinals in each: (a) a library body demanded
    // in one version and not in the next ones, then again; (c) the library's generic instances across versions, and a library generic
    // function refused at one Type and collected at another; (d) an edit that removes the caller that demanded library bodies.
    // Labels (SPEC, before any output): Hello, X3, the milestones and swap at u8 valid; swap at 70 nested [1 of ...] valid
    // (SPEC 10.5.3, 7.7.4; no rule bounds a Type's nesting), rejected by the implementation's storage bound with one located
    // Unsupported_Kd (PLAN G128).
    [Theory]
    [InlineData("example/Hello x3 example/Hello warm x3")]
    [InlineData("milestone/Milestone26 milestone/Milestone31 milestone/Milestone26 warm")]
    [InlineData("swap70 swap swap70 swap")]
    [InlineData("milestone/Milestone27 example/Hello milestone/Milestone27")]
    public void EveryVersionOfASequenceEqualsAFreshPipeline(string sequence)
    {
        var steps = sequence.Split(' ');
        var c = Compile(steps[0]);
        _ = c.Analyze();
        foreach (var then in steps[1..])
        {
            var (own, fresh) = Next(c, then);
            Assert.Equal(fresh, own);
        }
    }

    // N1 (b): two compilations in one process, each warm and then edited into the other's program alternately: every version equals a
    // fresh pipeline (a keeper of library facts is keyed on its own program, never on a stamp two compilations may share). Hello and X3
    // valid.
    [Fact]
    public void TwoCompilationsVersionsEachEqualAFreshPipeline()
    {
        var (first, second) = (Compile("example/Hello"), Compile("x3"));
        _ = (first.Analyze(), second.Analyze());
        foreach (var (c, then) in new[] { (first, "warm"), (second, "warm"), (first, "x3"), (second, "example/Hello"), (first, "example/Hello"), (second, "x3") })
        {
            var (own, fresh) = Next(c, then);
            Assert.Equal(fresh, own);
        }
    }

    // g7 (a) over every case of the diagnostic snapshot (N11), a warm rebind each, in parallel as SemanticSnapshotTest's corpus run, so
    // the class stays short: a failure gives the count and each case whose later version differs from the fresh pipeline, with its first
    // differing line (fresh -> own, as the semantic snapshot reports one), or throws.
    [Fact]
    public void AWarmVersionOfEverySnapshotCaseEqualsAFreshPipeline()
    {
        var names = DiagnosticSnapshotTest.CaseNames();
        var failed = new string?[names.Length];
        Parallel.For(0, names.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 4) }, i =>
        {
            try
            {
                var (own, fresh) = Later(names[i], "warm");
                failed[i] = SemanticSnapshotTest.FirstDifference(fresh, own) is { } difference ? $"{names[i]}:{difference}" : null;
            }
            catch (Exception exception)
            {
                failed[i] = $"{names[i]}: {exception.GetType().Name} {exception.Message}";
            }
        });
        string[] failures = [.. failed.OfType<string>()];
        Assert.True(failures.Length == 0, $"{failures.Length} of {names.Length} cases differ from a fresh pipeline:\n{string.Join('\n', failures)}");
    }

    // M1 (design §R1.4): the code a later version generates, after a warm rebind and after Provisional then Final, is the first version's.
    [Theory]
    [MemberData(nameof(Generated))]
    public void TheGeneratedCodeOfALaterVersionEqualsTheFirst(string program)
    {
        var kind = program.StartsWith("mir/", StringComparison.Ordinal) ? OutputKind.Library : OutputKind.Application;
        var c = Compile(program);
        Finish(c, kind);
        var first = Generate(c);
        Finish(c, kind);
        Assert.Equal(first, Generate(c));
        c.Binding.Bind(BindingMode.Provisional);
        Finish(c, kind);
        Assert.Equal(first, Generate(c));
    }

    // g8 (design §4): a later version keeps the library's bodies, the same objects
    // at the same ordinals, none begun again, so a body's validator verdict set after the first version (the seam
    // MirAnalysisTest.ABodyWithAFindingLeavesOtherBodiesProblems uses) stays. A builder with the library check's root range, whose library
    // segment holds every library body (PLAN G951 U2; the review's N5): Compilation's build holds the library's templates only until
    // U2-K, so its row (a later version keeps the library bodies the compilation demanded) is U2-K's completion row and G950-K's
    // prerequisite.
    [Fact]
    public void ALaterVersionKeepsTheLibrarysBodies()
    {
        var c = Compile("milestone/Milestone27");
        var builder = new MirBuilder(c);
        var program = builder.Build(SyntaxTable.LibraryModule + 1);
        MirBody[] library = [.. program.Bodies[..program.LibraryCount]];
        library[0].HasFinding = true;
        Finish(c, OutputKind.Application);
        var later = builder.Build(SyntaxTable.LibraryModule + 1);
        Assert.Equal(library, later.Bodies[..later.LibraryCount].ToArray());
        Assert.True(library[0].HasFinding);
    }

    // g8 for the collector (the Back's C-378 (2)): a later version walks only
    // the program segment's bodies into templates, the library segment's template store kept.
    [Fact]
    public void ALaterVersionKeepsTheLibrarysTemplates()
    {
        var c = Compile("milestone/Milestone27");
        var (builder, analysis, collector) = (new MirBuilder(c), new MirAnalysis(c.Types), new InstanceCollector(c.Types, c.Layout));
        _ = collector.Run(analysis.Run(builder.Build(SyntaxTable.LibraryModule + 1), 1), c.SourceModules.Length); // Stepped (PLAN G951).
        Finish(c, OutputKind.Application);
        var later = analysis.Run(builder.Build(SyntaxTable.LibraryModule + 1), 1);
        _ = collector.Run(later, c.SourceModules.Length);
        Assert.Equal(later.VersionBodies.Length, collector.Templated);
    }

    // M2 as row K2-a (M-K3; R-K2'' with a record; its expectation changed with G950-K, M-B5: an injected finding is not
    // republished under R-K2'', its version's library segment is built again): a validator finding set on a library body Hello reaches,
    // in the version that builds it (the seam MirAnalysisTest.ABodyWithAFindingLeavesOtherBodiesProblems uses), is published by that
    // version; the next version takes a new library segment and its report equals a fresh pipeline's, which has none. K2-b: the same on
    // utf8, a callee of writeLine. Hello valid (SPEC 22.7).
    [Theory]
    [InlineData("writeLine")]
    [InlineData("utf8")]
    public void ALibraryBodyWithAFindingEndsTheKeptSegment(string name)
    {
        var c = Compile("example/Hello");
        _ = c.Analyze();
        var program = c.BuildMir();
        var body = program.Bodies.ToArray().First(x => x.Source.IsLibrary && x.Key.Decl?.Name == name);
        program.Findings.Add(new(MirViolation.Callee, body.Source));
        body.HasFinding = true;
        Assert.Contains(new Problem(DiagnosticCode.InternalInvariant_Kd, body.Source), c.Problems);
        var segment = program.LibrarySegment;
        var (own, fresh) = Next(c, "warm");
        Assert.Equal(fresh, own);
        Assert.Equal(segment + 1, c.BuildMir().LibrarySegment);
    }

    // Row M2-e (G950-R's M2 on the library check's root range; M-B6): a finding of a library walk body in the library segment's
    // findings is published by every kept version again, at the same index. The seam puts the finding in the library prefix as the
    // validator's library pass would (MirProgram's libraryFindings). Milestone27 valid.
    [Fact]
    public void ALibraryFindingIsPublishedByEveryKeptVersion()
    {
        var c = Compile("milestone/Milestone27");
        var builder = new MirBuilder(c);
        var program = builder.Build(SyntaxTable.LibraryModule + 1);
        var body = program.LibraryBodies[^1];
        body.HasFinding = true;
        program.Findings.Insert(0, new(MirViolation.Callee, body.Source));
        var prefix = typeof(MirProgram).GetField("libraryFindings", BindingFlags.Instance | BindingFlags.NonPublic)!;
        prefix.SetValue(program, (int)prefix.GetValue(program)! + 1);
        MirFinding[] first = [.. program.Findings];
        Finish(c, OutputKind.Application);
        var later = builder.Build(SyntaxTable.LibraryModule + 1);
        Assert.Equal(first, later.Findings);
        Assert.Same(body, later.LibraryBodies[^1]);
    }

    // Row KV-1 (PLAN G951 U2-K-b; K-A, the Back's C1): a library body first demanded in a version is stepped into A2's program store there,
    // joins the library list at the next version, whose library step steps it into the library store, and is not built again; every
    // version equals a fresh pipeline. Hello and X3 valid.
    [Fact]
    public void ALibraryBodyFirstDemandedInAVersionJoinsTheLibraryList()
    {
        var c = Compile("example/Hello");
        _ = c.Analyze();
        var start = MirBuildTest.Library(new MirBuilder(c).Build(SyntaxTable.LibraryModule + 1), MirBodyKind.Fn, "start");
        var (own, fresh) = Next(c, "x3");
        Assert.Equal(fresh, own);
        var program = c.BuildMir();
        var body = program[start]!;
        Assert.Equal((true, false, true), (program.Drops.Holds(body.Ordinal), program.Drops.LibraryHolds(body.Ordinal), program.VersionBodies.ToArray().Contains(body)));
        var segment = program.LibrarySegment;
        (own, fresh) = Next(c, "warm");
        Assert.Equal(fresh, own);
        program = c.BuildMir();
        Assert.Same(body, program[start]);
        Assert.Equal((segment, true, true), (program.LibrarySegment, program.Drops.LibraryHolds(body.Ordinal), program.LibraryBodies.ToArray().Contains(body)));
    }

    // Row KV-4 (M-B3, F20): a kept library step builds nothing: each body it steps was stepped, its lookups made, in the version that first
    // built it. Milestones 26, 31, 26 and a warm version over an explicit pipeline in Compilation's configuration: the third version's
    // library step steps the second's demanded library bodies (the library list grew), and in the third and fourth versions the analysis
    // and the collector build no body. Valid (SPEC).
    [Fact]
    public void AKeptLibraryStepBuildsNothing()
    {
        var c = Compile("milestone/Milestone26");
        var roots = c.SourceModules.Length;
        var (builder, analysis, collector) = (new MirBuilder(c), new MirAnalysis(c.Types), new InstanceCollector(c.Types, c.Layout));
        var program = builder.Build(roots);
        _ = Compilation.Collect(analysis, collector, analysis.Run(program, roots), roots);
        var segment = program.LibrarySegment;
        foreach (var (then, again) in new[] { ("milestone/Milestone31", false), ("milestone/Milestone26", true), ("warm", true) })
        {
            Edit(c, then);
            var kept = program.LibraryBodies.Length;
            program = builder.Build(roots);
            var (built, list) = (program.Bodies.Length, program.LibraryBodies.Length);
            _ = Compilation.Collect(analysis, collector, analysis.Run(program, roots), roots);
            Assert.Equal(segment, program.LibrarySegment);
            Assert.Equal(Fresh(c), Codes(program));
            if (again)
            {
                Assert.Equal(built, program.Bodies.Length);
                Assert.True(then == "warm" || list > kept, $"{then}: the library list stayed at {list}");
            }
        }
    }

    // Row K1-K (R-K2'': A2 does not hold a demanded library body; the Back's C1 on C-378, with C4: the product row
    // ARefusedLibraryInstanceLeavesItsBodyBuiltAndUnstepped merged into this one at G950-K): swap70's version ends
    // with swap built at its library ordinal and not held (the premise, asserted; the collector refuses the instance after its lookup
    // built swap's body), so the next version (swap at
    // u8) takes a new library segment, steps swap and equals a fresh pipeline; the one after it (warm) keeps that segment, and swap70
    // again keeps it too, swap held by the library store while the fresh pipeline builds swap and steps nothing: the case Snapshot's
    // rule compares with an eager step of the body (the Back's C3 (a)), asserted to arise here. Transitional: it holds until R7 removes
    // the bound, and the row then needs another refusal. swap erased at 70 nested [1 of ...u8] is valid (SPEC 10.5.3, 7.7.4) and rejected
    // by the implementation's storage bound (PLAN G128: one located Unsupported_Kd); swap at u8 is valid.
    [Fact]
    public void ARefusedLibraryInstanceEndsTheKeptSegment()
    {
        var c = Compile("swap70");
        _ = c.Analyze();
        Assert.Equal([DiagnosticCode.Unsupported_Kd], c.Problems.Select(static x => x.Code));
        var (program, eager) = (c.BuildMir(), new MirBuilder(c).Build(SyntaxTable.LibraryModule + 1));
        var swap = Assert.Single(program.Bodies.ToArray(), static x => x.Source.IsLibrary);
        Assert.Equal(("swap", eager[swap.Key]!.Ordinal, false), (swap.Key.Decl?.Name, swap.Ordinal, program.Drops.Holds(swap.Ordinal)));
        var segment = program.LibrarySegment;
        var (own, fresh) = Next(c, "swap");
        Assert.Equal(fresh, own);
        Assert.Empty(c.Problems);
        Assert.Equal(segment + 1, c.BuildMir().LibrarySegment);
        foreach (var then in new[] { "warm", "swap70" })
        {
            (own, fresh) = Next(c, then);
            Assert.Equal(fresh, own);
            program = c.BuildMir();
            Assert.Equal((segment + 1, true), (program.LibrarySegment, program.Drops.LibraryHolds(program[swap.Key]!.Ordinal)));
        }

        var analysis = new MirAnalysis(c.Types);
        var unstepped = analysis.Run(new MirBuilder(c).Build(c.SourceModules.Length), c.SourceModules.Length);
        _ = Compilation.Collect(analysis, new InstanceCollector(c.Types, c.Layout), unstepped, c.SourceModules.Length);
        Assert.False(unstepped.Drops.Holds(Assert.Single(unstepped.Bodies.ToArray(), static x => x.Source.IsLibrary).Ordinal));
    }

    // Row B1-S (the review's B1; R-K2''): a library body its version built and no step stepped ends the kept segment. Hello over an
    // explicit pipeline in Compilation's configuration; after its collection a lookup while the version is Open builds Stopwatch.start
    // (U2-H's seam), which no step steps; the next version (warm) takes a new library segment, throws nothing and equals a fresh pipeline.
    // The seeded twin (g951Broken, a step-2 record) is the prototype's probe: the shipped library has no defect.
    // Hello valid.
    [Fact]
    public void ALibraryBodyNoStepSteppedEndsTheKeptSegment()
    {
        var c = Compile("example/Hello");
        var roots = c.SourceModules.Length;
        var start = MirBuildTest.Library(new MirBuilder(c).Build(SyntaxTable.LibraryModule + 1), MirBodyKind.Fn, "start");
        var (builder, analysis, collector) = (new MirBuilder(c), new MirAnalysis(c.Types), new InstanceCollector(c.Types, c.Layout));
        var program = builder.Build(roots);
        _ = Compilation.Collect(analysis, collector, analysis.Run(program, roots), roots);
        Assert.False(program.Drops.Holds(program[start]!.Ordinal));
        var segment = program.LibrarySegment;
        Edit(c, "warm");
        program = builder.Build(roots);
        _ = Compilation.Collect(analysis, collector, analysis.Run(program, roots), roots);
        Assert.Equal(segment + 1, program.LibrarySegment);
        Assert.Equal(Fresh(c), Codes(program));
    }

    // Row MR-1 (ST-1 rewritten as F18's fix (a)'s fail-before; the design review's B1, the owner's O-396 (a)): A2's per-version store is
    // cleared where a version starts (MirProgram.Clear, ClearProgram), so a keep test reads only step facts of its own version. X3 over an
    // explicit pipeline in Compilation's configuration (a version built and not analyzed is reachable only so, or after a Build that
    // throws past its EndLibrary; never in a healthy Compilation). V1 is analyzed and collected (the collector demands Stopwatch.start,
    // stepped into A2's program store); a lookup then builds Stopwatch.reset, which no step steps, so V2 takes a new library segment
    // (R-K2''). V2 is built and not analyzed, and a lookup builds start again at its slot: V2's build cleared V1's entries, so A2 does not
    // hold it, and V3 takes a new segment too, builds start again among its own bodies, steps it, throws nothing and equals a fresh
    // pipeline. Fail-before (f4b259493): V2 read V1's leftover entry of start and V3 kept V2's segment with start in its library list,
    // unstepped (with a library callee that has a record, B1's seeded probe rejects Hello there). Both paths: every version here is a new
    // segment. X3 valid (SPEC 10.5.3, 7.7.4).
    [Fact]
    public void AVersionBuiltAndNotAnalyzedEndsTheKeptSegment()
    {
        var c = Compile("x3");
        var roots = c.SourceModules.Length;
        var eager = new MirBuilder(c).Build(SyntaxTable.LibraryModule + 1);
        var (start, reset) = (MirBuildTest.Library(eager, MirBodyKind.Fn, "start"), MirBuildTest.Library(eager, MirBodyKind.Fn, "reset"));
        var (builder, analysis, collector) = (new MirBuilder(c), new MirAnalysis(c.Types), new InstanceCollector(c.Types, c.Layout));
        var program = builder.Build(roots);
        _ = Compilation.Collect(analysis, collector, analysis.Run(program, roots), roots);
        Assert.True(program.Drops.Holds(program[start]!.Ordinal));
        _ = program[reset];
        var segment = program.LibrarySegment;
        Edit(c, "warm");
        program = builder.Build(roots);
        var body = program[start]!;
        Assert.Equal((segment + 1, true, false), (program.LibrarySegment, program.VersionBodies.ToArray().Contains(body), program.Drops.Holds(body.Ordinal)));
        Edit(c, "warm");
        program = builder.Build(roots);
        Assert.Equal((segment + 2, 0), (program.LibrarySegment, program.LibraryBodies.Length));
        _ = Compilation.Collect(analysis, collector, analysis.Run(program, roots), roots);
        Assert.True(program.Drops.Holds(program[start]!.Ordinal));
        Assert.Equal(Fresh(c), Codes(program));
    }

    // Row N1 (the design review's N1 and its probe R2): a body excluded for a validator finding has an empty A2 entry, held and without a
    // record (MirDrops.Exclude), which is not what a step of the body gives: the keep test refuses such a body by its finding, and no later
    // version reads that entry (F18's fix (a)). Hello over an explicit pipeline in Compilation's configuration; writeLine is given a finding
    // in V1 before its analysis (the seam MirAnalysisTest.ABodyWithAFindingLeavesOtherBodiesProblems uses), so V2 takes a new segment; V2 is
    // built and not analyzed, and a lookup builds writeLine again without the finding, not held; V3 takes a new segment, steps writeLine
    // and equals a fresh pipeline. Fail-before (f4b259493): V2 read V1's empty entry as held, and V3 kept V2's segment with writeLine in its
    // library list on it. Both paths. Hello valid (SPEC 22.7).
    [Fact]
    public void ABodyExcludedForAFindingIsNotKeptOnItsEmptyEntry()
    {
        var c = Compile("example/Hello");
        var roots = c.SourceModules.Length;
        var first = new MirAnalysis(c.Types).Run(new MirBuilder(c).Build(roots), roots);
        var key = first.Bodies.ToArray().First(static x => x.Source.IsLibrary && x.Key.Kind == MirBodyKind.Fn && x.Key.Decl?.Name == "writeLine").Key;
        var (builder, analysis, collector) = (new MirBuilder(c), new MirAnalysis(c.Types), new InstanceCollector(c.Types, c.Layout));
        var program = builder.Build(roots);
        program[key]!.HasFinding = true;
        _ = Compilation.Collect(analysis, collector, analysis.Run(program, roots), roots);
        Assert.Equal((true, 0), (program.Drops.Holds(program[key]!.Ordinal), program.Drops.RowsOf(program[key]!.Ordinal).Length));
        var segment = program.LibrarySegment;
        Edit(c, "warm");
        program = builder.Build(roots);
        var body = program[key]!;
        Assert.Equal((segment + 1, false, false), (program.LibrarySegment, body.HasFinding, program.Drops.Holds(body.Ordinal)));
        Edit(c, "warm");
        program = builder.Build(roots);
        Assert.Equal((segment + 2, 0), (program.LibrarySegment, program.LibraryBodies.Length));
        _ = Compilation.Collect(analysis, collector, analysis.Run(program, roots), roots);
        Assert.Equal(Fresh(c), Codes(program));
    }

    // Row MR-4 (the design review's N5): a program whose demanded library body is never held ends its segment in every version: swap
    // erased at 70 nested [1 of ...u8] (valid, SPEC 10.5.3, 7.7.4; rejected by the storage bound with one located Unsupported_Kd, PLAN
    // G128), then two warm versions, each a new segment (the refused swap is built and never stepped), each equal to a fresh pipeline,
    // nothing thrown; each such version numbers the library's slots again (K-4's cold cost per version, M-B2). Both paths. Transitional
    // with K1-K.
    [Fact]
    public void AProgramEndingItsSegmentEndsItInEveryVersion()
    {
        var c = Compile("swap70");
        _ = c.Analyze();
        var segment = c.BuildMir().LibrarySegment;
        for (var version = 1; version <= 2; version++)
        {
            var (own, fresh) = Next(c, "warm");
            Assert.Equal(fresh, own);
            Assert.Equal([DiagnosticCode.Unsupported_Kd], c.Problems.Select(static x => x.Code));
            Assert.Equal(segment + version, c.BuildMir().LibrarySegment);
        }
    }

    // Row NUM (K-4 kept with the segment, F19; G950-K): every warm version of a compilation keeps the library segment its first version
    // built, so the library's slots are numbered once (MirBuilder.Library runs only where a version builds a new segment), and each
    // version equals a fresh pipeline. Hello valid (SPEC 22.7).
    [Fact]
    public void AWarmVersionKeepsTheLibrarySegment()
    {
        var c = Compile("example/Hello");
        _ = c.Analyze();
        var segment = c.BuildMir().LibrarySegment;
        for (var version = 1; version <= 3; version++)
        {
            var (own, fresh) = Next(c, "warm");
            Assert.Equal(fresh, own);
            Assert.Equal(segment, c.BuildMir().LibrarySegment);
        }
    }

    // Row LV (G950-K's stamp; G949 ruling 174, S1-1's B4): MirBuilder.Build stamps a version's library segment with Binding's library
    // publication version, Binding.Frozen once the library is frozen (PLAN G949 Stage 2 U3), so the stamp is the same in every version of
    // a compilation: after its first Bind, a second Bind, and an edit then a Bind, which keeps the segment. Hello and X3 valid.
    [Fact]
    public void ALibraryVersionStampsEveryVersionOfACompilation()
    {
        var c = Compile("example/Hello");
        _ = c.Analyze();
        var (stamp, segment) = (HirTables.LibraryVersion(c), c.BuildMir().LibrarySegment);
        Assert.NotEqual(0ul, stamp);
        Assert.Equal(stamp, c.BuildMir().LibraryVersion);
        foreach (var then in new[] { "warm", "x3" })
        {
            Edit(c, then);
            _ = c.Analyze();
            Assert.Equal((stamp, stamp, segment), (HirTables.LibraryVersion(c), c.BuildMir().LibraryVersion, c.BuildMir().LibrarySegment));
        }
    }

    // Row MR-3 (the design review's N5 and 6.2 (4); the Front's C-379 (1); S1-1's c13 two-compilation clause in
    // KimiLibraryShapeTest.ASecondBindBindsNoLibraryNode; from PLAN G949 Stage 2 U3 both read one frozen library, whose
    // HirTables.LibraryVersion is Binding.Frozen in each, and the segment is each program's): two compilations interleaved, each warm and edited
    // into the other's program, each keep their own library segment in every version, and every version equals a fresh pipeline: no
    // compilation's stamp moves another's. Hello and X3 valid.
    [Fact]
    public void EachCompilationKeepsItsOwnLibrarySegment()
    {
        var (first, second) = (Compile("example/Hello"), Compile("x3"));
        _ = (first.Analyze(), second.Analyze());
        var segments = (first.BuildMir().LibrarySegment, second.BuildMir().LibrarySegment);
        foreach (var (c, then) in new[] { (first, "warm"), (second, "warm"), (first, "x3"), (second, "example/Hello"), (first, "example/Hello"), (second, "x3") })
        {
            var (own, fresh) = Next(c, then);
            Assert.Equal(fresh, own);
            Assert.Equal(segments, (first.BuildMir().LibrarySegment, second.BuildMir().LibrarySegment));
        }
    }

    // Row MR-2 (the design review's N5 and R4; F23): the code an edited version generates is the code of a compilation whose MIR pipeline
    // first builds that version (its first program bound once, then the same edit, so both read the edit's document), in every version
    // of the edit sequences, whose kept segment holds across each edit; M1 covers warm and Provisional versions. Valid (SPEC).
    [Theory]
    [InlineData("milestone/Milestone26 milestone/Milestone31 milestone/Milestone26 warm")]
    [InlineData("milestone/Milestone27 example/Hello milestone/Milestone27")]
    [InlineData("milestone/Milestone30 milestone/Milestone26 milestone/Milestone30")]
    public void TheGeneratedCodeOfAnEditedVersionEqualsAFreshPipelines(string sequence)
    {
        var steps = sequence.Split(' ');
        var c = Compile(steps[0]);
        Finish(c, OutputKind.Application);
        _ = Generate(c);
        var program = steps[0];
        foreach (var then in steps[1..])
        {
            Edit(c, then);
            program = then == "warm" ? program : then;
            var fresh = Compile(steps[0]);
            Edit(fresh, program);
            Assert.Equal(Generate(fresh), Generate(c));
        }
    }

    // Row KV-2 (K-A over two reaching versions; the design review's N5): Hello, X1, X2, X1 through Compilation. X1 (valid) passes
    // Stopwatch.reset to apply, whose Callable bound declares confined; X2 passes Stopwatch.start (invalid: UnsatisfiedEffectBound_Kd at the
    // argument, SPEC 8.11.2, 8.11.4). X1's version steps reset into A2's program store; the next version's library step holds it in the
    // library store, and the last version keeps it, the same body; X2's record is in its version only; each version equals a fresh
    // pipeline. Hello valid (SPEC 22.7).
    [Fact]
    public void TwoReachingVersionsKeepTheirLibraryBodiesAndRecords()
    {
        var c = Compile("example/Hello");
        _ = c.Analyze();
        var reset = MirBuildTest.Library(new MirBuilder(c).Build(SyntaxTable.LibraryModule + 1), MirBodyKind.Fn, "reset");
        var (own, fresh) = Next(c, "x1");
        Assert.Equal(fresh, own);
        Assert.Empty(c.Problems);
        var body = c.BuildMir()[reset]!;
        Assert.Equal((true, false), (c.BuildMir().Drops.Holds(body.Ordinal), c.BuildMir().Drops.LibraryHolds(body.Ordinal)));
        (own, fresh) = Next(c, "x2");
        Assert.Equal(fresh, own);
        Assert.Equal([DiagnosticCode.UnsatisfiedEffectBound_Kd], c.Problems.Select(static x => x.Code));
        Assert.True(c.BuildMir().Drops.LibraryHolds(body.Ordinal));
        (own, fresh) = Next(c, "x1");
        Assert.Equal(fresh, own);
        Assert.Empty(c.Problems);
        Assert.Equal((body, true), (c.BuildMir()[reset], c.BuildMir().Drops.LibraryHolds(body.Ordinal)));
    }

    // Row g8-C in Compilation's configuration (the design review's N5): a later version keeps the library bodies the compilation demanded,
    // the same bodies at the same ordinals, as its library list, and builds none of them again (its own bodies are the program's).
    // Milestone27 valid.
    [Fact]
    public void ALaterVersionKeepsTheLibraryBodiesItsProgramDemanded()
    {
        var c = Compile("milestone/Milestone27");
        _ = c.Analyze();
        MirBody[] demanded = [.. c.BuildMir().VersionBodies.ToArray().Where(static x => x.Source.IsLibrary)];
        int[] ordinals = [.. demanded.Select(static x => x.Ordinal)];
        Assert.NotEmpty(demanded);
        Finish(c, OutputKind.Application);
        _ = c.Analyze();
        var later = c.BuildMir();
        Assert.Equal(demanded, later.LibraryBodies.ToArray());
        Assert.Equal(ordinals, demanded.Select(static x => x.Ordinal));
        Assert.DoesNotContain(later.VersionBodies.ToArray(), static x => x.Source.IsLibrary);
    }

    // Row g8-T in Compilation's configuration (the design review's N5) with U2-K-a's review N2: three versions of Milestone27 over an
    // explicit pipeline: the second templates its own bodies and the library bodies its library step newly holds (the first's demanded
    // ones, templated again into the library store: the double templating, unit M), the third its own bodies only; the demanded library
    // bodies are built out of ordinal order (the premise), so both template stores are read at entries closed out of ordinal order with
    // holes, and each version's checked set, with every target, equals a fresh pipeline's. Milestone27 valid.
    [Fact]
    public void ALaterVersionTemplatesTheLibraryBodiesItsProgramDemandedOnce()
    {
        var c = Compile("milestone/Milestone27");
        var roots = c.SourceModules.Length;
        var (builder, analysis, collector) = (new MirBuilder(c), new MirAnalysis(c.Types), new InstanceCollector(c.Types, c.Layout));
        var program = builder.Build(roots);
        _ = Compilation.Collect(analysis, collector, analysis.Run(program, roots), roots);
        int[] built = [.. program.VersionBodies.ToArray().Where(static x => x.Source.IsLibrary).Select(static x => x.Ordinal)];
        Assert.Contains(Enumerable.Range(1, built.Length - 1), i => built[i] < built[i - 1]);
        foreach (var added in new[] { built.Length, 0 })
        {
            Finish(c, OutputKind.Application);
            program = builder.Build(roots);
            var set = Compilation.Collect(analysis, collector, analysis.Run(program, roots), roots);
            Assert.Equal(program.VersionBodies.Length + added, collector.Templated);
            var freshAnalysis = new MirAnalysis(c.Types);
            var fresh = freshAnalysis.Run(new MirBuilder(c).Build(roots), roots);
            var freshSet = Compilation.Collect(freshAnalysis, new InstanceCollector(c.Types, c.Layout), fresh, roots);
            Assert.Equal(Snapshot(c, fresh, freshSet, fresh), Snapshot(c, program, set, fresh));
        }
    }

    // A sequence's program: one the class writes, a tests/mir program as MirDumpTest compiles it, or a case of the snapshots as
    // SemanticSnapshotTest binds it.
    private static Compilation Compile(string program)
        => Own.TryGetValue(program, out var source) ? MirBuildTest.Compile(source)
            : program.StartsWith("mir/", StringComparison.Ordinal) ? MirDumpTest.Compile(program[4..]) : SemanticSnapshotTest.Bind(program).Compilation;

    // g7's two snapshots of a program's later version (a warm rebind, Provisional then Final, or an edit into another program): the
    // compilation's own MIR and a fresh builder's, analysis's and collector's over the same compilation state, in Compilation's
    // configuration (the source modules' root range, collected through Compilation.Collect's loop; PLAN G951 U2).
    private static (string Own, string Fresh) Later(string program, string then)
    {
        var c = Compile(program);
        _ = c.Analyze();
        return Next(c, then);
    }

    // The next version of an analyzed compilation (a warm rebind, Provisional then Final, or an edit into another program) and its two
    // snapshots (Later).
    private static (string Own, string Fresh) Next(Compilation c, string then)
    {
        Edit(c, then);
        _ = c.Analyze();
        var (own, set) = (c.BuildMir(), c.Instances!);
        var analysis = new MirAnalysis(c.Types);
        var fresh = analysis.Run(new MirBuilder(c).Build(c.SourceModules.Length), c.SourceModules.Length);
        var freshSet = Compilation.Collect(analysis, new InstanceCollector(c.Types, c.Layout), fresh, c.SourceModules.Length);
        return (Snapshot(c, own, set, fresh), Snapshot(c, fresh, freshSet, fresh));
    }

    // The next Binding pass of a compilation: a warm rebind, Provisional then Final, or an edit into another program, then Finish.
    private static void Edit(Compilation c, string then)
    {
        if (then == "provisional")
        {
            c.Binding.Bind(BindingMode.Provisional);
        }
        else if (then != "warm")
        {
            c.Kotonoha.RootKoto.Clear();
            c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, Own.TryGetValue(then, out var source) ? source : SemanticSnapshotTest.Source(then));
        }

        Finish(c, OutputKind.Application);
    }

    // A fresh pipeline's report over a compilation's state, in Compilation's configuration (Codes).
    private static string Fresh(Compilation c)
    {
        var (analysis, roots) = (new MirAnalysis(c.Types), c.SourceModules.Length);
        var program = analysis.Run(new MirBuilder(c).Build(roots), roots);
        _ = Compilation.Collect(analysis, new InstanceCollector(c.Types, c.Layout), program, roots);
        return Codes(program);
    }

    // A report's rows, each its code and location.
    private static string Codes(MirProgram program)
    {
        var text = new StringBuilder();
        foreach (var problem in program.Problems.All)
        {
            text.Append(problem.Code).Append('@').Append(problem.Subject).Append(' ');
        }

        return text.ToString();
    }

    // A later version: a final pass and the startup check of the output kind (a test build's test startup).
    private static void Finish(Compilation c, OutputKind kind)
    {
        c.Binding.Bind(BindingMode.Final);
        _ = c.IsTestBuild ? c.Binding.CheckTestStartup() : c.Binding.CheckStartup(kind);
    }

    // The verdict of the latest version's analysis and, when verified, the code it generates.
    private static string Generate(Compilation c)
    {
        var verified = c.Analyze().IsVerified;
        var text = new StringWriter();
        var generated = verified && c.Generate(text);
        return $"{verified} {generated}\n{text}";
    }

    // What g7 compares: the report's rows with their facts, the findings, the dumps of the program and of the library, and the checked set,
    // each item by every part of its key, and its body's kind and location, with its targets. Every identity-bearing part prints through
    // one rendering: a Type or a length as MirDump spells it (TyCtxt.Name), an erased substitution by its target, Self, declaring Type and
    // each slot's Type or length (with the Origins it binds, none once erased), a symbol by its declaration's written location (read
    // without numbering a node), and a Reason's end by its index in its body's C1-F4 atom table, the canonical atoms a Reason names
    // (MirProblem's contract). Two instances of one declaration print alike only where TyCtxt.Name spells their Types alike: two closure
    // Types of one signature (a closure is shown by its signature). Per M-K1's Q1 (PLAN G951 U2-K) a kept library list is a superset of
    // a fresh pipeline's reach, so the library's part is the library's range, the library bodies are the fresh pipeline's, each the
    // program's body of its key with its ordinal and A2's facts (missing where the program has none), and an extra line names each
    // library body among the version's own bodies (VersionBodies) the fresh pipeline did not build, the superset confined to the kept
    // list (the Back's C3 (b)). A body the fresh pipeline built and no step stepped (K1-K's refused instance; the case arises only in a
    // version the fresh pipeline rejects with a collection record, asserted) may be a kept, stepped body: its A2 facts are compared with an
    // eager library-range step of the same body (R5-M168; the design review's N4, the Back's C3 (a)) and printed without them on both
    // sides. Bodies are read by key from the build-order lists, never by a lookup, which an open version would answer by building.
    private static string Snapshot(Compilation c, MirProgram program, CheckedSet set, MirProgram fresh)
    {
        var (text, types) = (new StringBuilder(), c.Types);
        var (own, mine, built) = (0, new Dictionary<MirDefKey, MirBody>(), new HashSet<MirDefKey>());
        foreach (var body in program.Bodies)
        {
            own += body.Source.IsLibrary ? 0 : 1;
            mine.Add(body.Key, body);
        }

        foreach (var body in fresh.Bodies)
        {
            built.Add(body.Key);
        }

        text.Append("library ").Append(program.LibraryCount).Append(" program ").Append(own).Append('\n');
        foreach (var body in program.VersionBodies)
        {
            if (body.Source.IsLibrary && !built.Contains(body.Key))
            {
                text.Append("extra ").Append(body.Key).Append('\n');
            }
        }

        for (var i = 0; i < program.Problems.All.Length; i++)
        {
            var problem = program.Problems.All[i];
            IReadOnlyList<OriginAtomEntry> atoms = problem.Reason == default || problem.Body < 0 ? [] : types.OriginRows(program.Body(problem.Body).Key.Body).Atoms;
            text.Append($"problem {problem.Code} {problem.Requirement} {problem.Subject} body {problem.Body} views {problem.Views} ")
                .Append($"reason {End(atoms, problem.Reason.Longer)} {End(atoms, problem.Reason.Shorter)} {problem.Reason.Equality} facts ")
                .AppendJoin(", ", program.Problems.Facts(i).ToArray()).Append('\n');
        }

        foreach (var finding in program.Findings)
        {
            text.Append("finding ").Append(finding).Append('\n');
        }

        MirDump.Write(program, 0, text);
        foreach (var body in fresh.Bodies)
        {
            if (!body.Source.IsLibrary)
            {
                continue;
            }

            if (!mine.TryGetValue(body.Key, out var kept))
            {
                text.Append("missing ").Append(body.Key).Append('\n');
                continue;
            }

            if (fresh.Drops.Holds(body.Ordinal))
            {
                MirDump.Write(kept, text.Append(kept.Ordinal).Append(' '), program.Drops);
                continue;
            }

            if (program.Drops.Holds(kept.Ordinal))
            {
                Assert.NotEmpty(fresh.Records(MirRecordClass.Collection));
                var step = new MirAnalysis(types).Run(new MirBuilder(c).Build(SyntaxTable.LibraryModule + 1), SyntaxTable.LibraryModule + 1);
                var (keptFacts, stepFacts) = (new StringBuilder(), new StringBuilder());
                MirDump.Write(kept, keptFacts, program.Drops);
                MirDump.Write(step[kept.Key]!, stepFacts, step.Drops);
                Assert.Equal(stepFacts.ToString(), keptFacts.ToString());
            }

            MirDump.Write(kept, text.Append(kept.Ordinal).Append(' '), fresh.Drops);
        }

        text.Append("roots ").Append(set.RootCount).Append(" items ").Append(set.Items.Length).Append('\n');
        for (var i = 0; i < set.Items.Length; i++)
        {
            var (item, key) = (set.Items[i], set.Items[i].Key);
            var body = item.BodyOrdinal >= 0 ? program.Body(item.BodyOrdinal) : null;
            text.Append($"{key.Kind} {Id(key.Body.Def.Decl)} {key.Body.Def.Kind} {key.Body.Def.Index} {Bindings(key.Body.Erased)} {Name(key.Type)} ")
                .Append($"{Id(key.Symbol)} {key.Variant} {Name(key.Function)} view {item.View} parent {item.Parent} site {item.Site} body {body?.Key.Kind} ")
                .Append($"{body?.Source} ").AppendJoin(", ", set.Targets(i).ToArray()).Append('\n');
        }

        return text.ToString();

        static string Id(BindingSymbol? symbol) => symbol is null ? "-" : $"{symbol.Name}@{SourceRef.Written(symbol.Declaration)}";

        static string Name(BoundType? type) => type is null ? "-" : TyCtxt.Name(type);

        static string Bindings(Subst? subst) => subst is null ? "-"
            : $"{Id(subst.Target)} self {Name(subst.Self)} declaring {Name(subst.Declaring)} <{string.Join(", ", subst.Types.ToArray().Zip(subst.Lengths.ToArray(), static (type, length)
                => type is not null ? TyCtxt.Name(type) : length is not null ? TyCtxt.Name(length) : "_"))}> origins {subst.Origins.Length} inputs {subst.Inputs.Length}";

        static string End(IReadOnlyList<OriginAtomEntry> atoms, BoundOrigin? end)
            => end is null ? "-" : "atom " + Enumerable.Range(0, atoms.Count).FirstOrDefault(i => ReferenceEquals(atoms[i].Atom, end), -1);
    }
}
