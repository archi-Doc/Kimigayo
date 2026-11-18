// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

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
/// built again (the base) or kept (G950-K). g8 and M2 are explicit until G950-K keeps the segment; the base fails them, and M2's premise
/// (a library body in Compilation's library segment) waits for G951 U2-K too. The fresh pipeline
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

    // PLAN G951 U2-K (the design review's N1 rows on the product path; their kept-path twins are U2-K-b's): every version of a longer
    // sequence equals a fresh pipeline: (a) a library body demanded in one version and not in the next ones, then again; (c) the library's
    // generic instances across versions, and a library generic function refused at one Type and collected at another; (d) an edit that
    // removes the caller that demanded library bodies. Labels (SPEC, before any output): Hello, X3, the milestones and swap at u8 valid;
    // swap at 70 nested [1 of ...] valid (SPEC 10.5.3, 7.7.4; no rule bounds a Type's nesting), rejected by the implementation's storage
    // bound with one located Unsupported_Kd (PLAN G128).
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

    // Row K1-K on the product path (the Back's C1 on C-378; U2-K-b's kept twin rests on it): a library body its version built and no
    // step stepped. swap erased at 70 nested [1 of ...u8] is valid (SPEC 10.5.3, 7.7.4) and rejected by the implementation's storage
    // bound (PLAN G128: one located Unsupported_Kd, made where the collector refuses the instance after its lookup built swap's body),
    // so the version ends with swap built and not held by A2's table: the premise, asserted, and Transitional: it holds until R7 removes
    // the bound, and the row then needs another refusal. The next version, the same erasure at u8 (valid), equals a fresh pipeline and
    // steps swap. (Its ordinal is the library check's from U2-K-b's slots on.)
    [Fact]
    public void ARefusedLibraryInstanceLeavesItsBodyBuiltAndUnstepped()
    {
        var c = Compile("swap70");
        _ = c.Analyze();
        Assert.Equal([DiagnosticCode.Unsupported_Kd], c.Problems.Select(static x => x.Code));
        var program = c.BuildMir();
        var swap = Assert.Single(program.Bodies.ToArray(), static x => x.Source.IsLibrary);
        Assert.Equal(("swap", false), (swap.Key.Decl?.Name, program.Drops.Holds(swap.Ordinal)));
        var (own, fresh) = Next(c, "swap");
        Assert.Equal(fresh, own);
        Assert.Empty(c.Problems);
        var later = c.BuildMir();
        Assert.True(later.Drops.Holds(Assert.Single(later.Bodies.ToArray(), static x => x.Source.IsLibrary).Ordinal));
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

    // g8 (design §4; explicit until G950-K, the base builds the segment again): a later version keeps the library's bodies, the same objects
    // at the same ordinals, none begun again, so a body's validator verdict set after the first version (the seam
    // MirAnalysisTest.ABodyWithAFindingLeavesOtherBodiesProblems uses) stays. A builder with the library check's root range, whose library
    // segment holds every library body (PLAN G951 U2; the review's N5): Compilation's build holds the library's templates only until
    // U2-K, so its row (a later version keeps the library bodies the compilation demanded) is U2-K's completion row and G950-K's
    // prerequisite.
    [Fact(Explicit = true)]
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

    // g8 for the collector (the Back's C-378 (2); explicit until G950-K, the base templates every body again): a later version walks only
    // the program segment's bodies into templates, the library segment's template store kept.
    [Fact(Explicit = true)]
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

    // M2 (design §R1.4; explicit until G950-K, the base's rebuild drops the injection): a kept library row is published in every version:
    // a validator finding of a library body in Compilation's library segment (the seam MirAnalysisTest.ABodyWithAFindingLeavesOtherBodiesProblems
    // uses; G950-K gives it at the validator's library step) is published by the second version again, at the same index. Its premise,
    // asserted first, waits for U2-K as g8's Compilation form does (PLAN G951 U2): until then Compilation's library segment holds the
    // library's templates only, and the library body Hello reaches (Console.writeLine's) takes a program ordinal, built again in every
    // version; publication is Compilation's, so no explicit builder can carry the row.
    [Fact(Explicit = true)]
    public void AKeptLibraryRowIsPublishedInEveryVersion()
    {
        var c = Compile("example/Hello");
        _ = c.Analyze();
        var program = c.BuildMir();
        var body = program.Bodies.ToArray().First(static x => x.Source.IsLibrary); // The first library body Hello reaches.
        Assert.True(body.Ordinal < program.LibraryCount, $"{body.Key.Kind} {body.Key.Decl?.Name} is at ordinal {body.Ordinal}, after the library segment's {program.LibraryCount}");
        program.Findings.Add(new(MirViolation.Callee, body.Source));
        body.HasFinding = true;
        Problem[] first = [.. c.Problems];
        Assert.Contains(new Problem(DiagnosticCode.InternalInvariant_Kd, body.Source), first);
        Finish(c, OutputKind.Application);
        _ = c.Analyze();
        Assert.Equal(first, c.Problems);
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
        _ = c.Analyze();
        var own = Snapshot(c.Types, c.BuildMir(), c.Instances!);
        var analysis = new MirAnalysis(c.Types);
        var fresh = analysis.Run(new MirBuilder(c).Build(c.SourceModules.Length), c.SourceModules.Length);
        return (own, Snapshot(c.Types, fresh, Compilation.Collect(analysis, new InstanceCollector(c.Types, c.Layout), fresh, c.SourceModules.Length)));
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
    // Types of one signature (a closure is shown by its signature).
    private static string Snapshot(TyCtxt types, MirProgram program, CheckedSet set)
    {
        var text = new StringBuilder();
        text.Append("library ").Append(program.LibraryCount).Append(" of ").Append(program.Bodies.Length).Append('\n');
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
        MirDump.Write(program, SyntaxTable.LibraryModule, text);
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
