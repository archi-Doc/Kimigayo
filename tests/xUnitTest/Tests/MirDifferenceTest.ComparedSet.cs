// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// The ledger's compared set (DECISIONS R4-M4, R4-M17, R4-M67): its analyses, its MIR column, the split of a compilation's records and
// the matching of the two sides.
public sealed partial class MirDifferenceTest
{
    // A fit's twin (SPEC 15.6.5's `bad`), a mixed fit whose first recorded chain is fixed-fixed (BU's `fncomp`) and an all-fixed-fixed fit.
    private const string TwinFits = "func bad(x: ref/i32) -> ref/i32 during x\n    let local: i32 = 1\n    return local@ref\n" +
        "func mixed(x: ref/i32, y: ref/i32, g: ref/((ref/i32 during y) -> i32)) -> (ref/((ref/i32 during x) -> i32), ref/i32 during x)\n" +
        "    let local = 1\n    let t = (g, local@ref)\n    return t\n" +
        "func fixed(x: ref/i32, y: ref/i32) -> ref/i32 during y => x\n";

    // A local's clause whose record is a body chain (finite over fixed) and one whose record is fixed-fixed.
    private const string TwinClauses = "struct H {s}\n    public let item: ref/i32 during s\n    public init(item: ref/i32 during s) => self.item = item\n" +
        "func twin(a: ref/i32) -> i32\n    let local = 3\n    let h: H{x} = H.init(local@ref)\n        origin x.s outlives a\n    return h.item@follow\n" +
        "func fixed(a: ref/i32, b: ref/i32) -> i32\n    let h0 = H.init(a)\n    let h: H{x} = h0@move\n        origin x.s outlives b\n    return 0\n";

    // A call argument with a fixed-fixed position (y over x, fixed by `s`) and a body-chain position (the local's borrow over x).
    private const string CallPositions = "func store(slot: uniq/(ref/i32 during a), pair: (ref/i32 during a, ref/i32 during a)) => ()\n" +
        "func caller(s: uniq/(ref/i32 during x), y: ref/i32)\n    let local = 1\n    let t = (y, local@ref)\n    store(s, t)\n";

    // R4-M17: the sides match as multisets on code and subject node: a node's second old record of one code stays old, and another code
    // at a matched node is new. R4-M67: a matched MIR problem that names another requirement writes the pair's as old>mir; a MIR record
    // that names none (-) agrees.
    [Fact]
    public void RecordsMatchOnCodeAndSubjectNode()
    {
        var c = CompilationTestHelper.Parse("func f(a: i32) -> i32 => a\n");
        var nodes = KotoTree.Walk(c.Kotonoha.RootKoto).ToArray();
        var (a, b) = (nodes[^1], nodes[^2]);
        var (moved, uninitialized, bound) = (DiagnosticCode.MovedPlace_Kd, DiagnosticCode.UninitializedPlace_Kd, DiagnosticCode.UnsatisfiedEffectBound_Kd);
        MirDifferenceLedger.Entry[] old =
        [
            new(moved, "Ownership.PossiblyMovedUse", "A1", a), new(moved, "Ownership.PossiblyMovedUse", "A1", a),
            new(uninitialized, "Ownership.UninitializedUse", "A1", b), new(bound, "Binding.VirtualEffectBound", "A4", b),
            new(DiagnosticCode.Unsupported_Kd, "Ownership.Unsupported", "U", b),
        ];
        MirDifferenceLedger.Entry[] mir =
        [
            new(moved, "Ownership.PossiblyMovedUse", "A1", a), new(uninitialized, "Ownership.UninitializedUse", "A1", a),
            new(bound, "Ownership.VirtualEffectBound", "A4", b), new(DiagnosticCode.Unsupported_Kd, "-", "B", b),
        ];
        var rows = MirDifferenceLedger.Match([.. old], [.. mir]);
        var renamed = old[3] with { Requirement = "Binding.VirtualEffectBound>Ownership.VirtualEffectBound" };
        Assert.Equal([("both", old[0]), ("old", old[1]), ("old", old[2]), ("both", renamed), ("both", old[4]), ("new", mir[1])], rows);
    }

    // R4-M67: a MIR problem is one entry, as MirReport.Add keeps one per code, subject and body, with its own requirement and its pair's
    // analysis in the compared set; MirReport's disagreement record and a pair outside the set are contract-break rows (B), never a throw.
    [Fact]
    public void MirProblemsTakeTheirPairsAnalysisAndADefectIsARow()
    {
        var c = MirBuildTest.Compile("func f(a: i32) -> i32 => a\n");
        var program = c.BuildMir();
        var (subject, body) = (program.Bodies[^1].Source, program.Bodies[^1].Ordinal);
        var moved = DiagnosticRequirement.Ownership(OwnershipRequirement.PossiblyMovedUse);
        program.Problems.Add(new(DiagnosticCode.MovedPlace_Kd, moved, subject, body, 1));
        program.Problems.Add(new(DiagnosticCode.MovedPlace_Kd, DiagnosticRequirement.Ownership(OwnershipRequirement.UninitializedUse), subject, body, 2));
        program.Problems.Add(new(DiagnosticCode.UninitializedPlace_Kd, moved, subject, body, 1));
        var node = c.SyntaxOf(subject);
        MirDifferenceLedger.Entry[] expected =
        [
            new(DiagnosticCode.MovedPlace_Kd, "Ownership.PossiblyMovedUse", "A1", node),
            new(DiagnosticCode.InternalInvariant_Kd, "Ownership.Internal", "B", node),
            new(DiagnosticCode.UninitializedPlace_Kd, "Ownership.PossiblyMovedUse", "B", node),
        ];
        Assert.Equal(expected, MirDifferenceLedger.Sides(c, program).Mir.TakeLast(3));
    }

    // DECISIONS R4-M132 (rule W; the A5 step 2 plan's 2g-1 wording): a MIR problem with Cause facts is derived and leaves the compared MIR
    // side, as publication suppresses it while its causes publish; its cause, the Origin problem, stays.
    [Fact]
    public void ADerivedProblemLeavesTheMirSide()
    {
        var c = MirBuildTest.Compile("func bad(x: ref/i32) -> ref/i32 during x\n    let local: i32 = 1\n    var r = local@ref\n    return r\n");
        var program = c.BuildMir();
        Assert.Contains(program.Problems.All.ToArray(), static x => x.Code == DiagnosticCode.ComparisonLoanConflict_Kd);
        var mir = MirDifferenceLedger.Sides(c, program).Mir;
        Assert.Contains(mir, static x => x.Code == DiagnosticCode.UnsatisfiedOriginRelation_Kd);
        Assert.DoesNotContain(mir, static x => x.Code == DiagnosticCode.ComparisonLoanConflict_Kd);
    }

    // PLAN10 §7, R4-M67: the requirement each MIR recording rule names lies in the compared set's MIR column, so its problems take their
    // analysis there (MirAnalysisTest's program, one problem of each rule).
    [Fact]
    public void MirProblemsNameRequirementsOfTheComparedSet()
    {
        var problems = MirBuildTest.Compile(MirAnalysisTest.Requirements).BuildMir().Problems.All.ToArray();
        Assert.NotEmpty(problems);
        Assert.All(problems, static x => Assert.True(MirDifferenceLedger.MirAnalysisOf(x.Requirement, x.Code) is not null, $"{x.Code} {x.Requirement}"));
    }

    // R4-M4: the compared set names its owning analysis; pass-through records have none.
    [Theory]
    [InlineData("Ownership", (int)OwnershipRequirement.PossiblyMovedUse, DiagnosticCode.MovedPlace_Kd, "A1")]
    [InlineData("Ownership", (int)OwnershipRequirement.TransferRequired, DiagnosticCode.TransferRequired_Kd, null)]
    [InlineData("Ownership", (int)OwnershipRequirement.Unsupported, DiagnosticCode.Unsupported_Kd, "U")]
    [InlineData("ControlFlow", 0, DiagnosticCode.RequireFallthrough_Kd, "A3")]
    [InlineData("Binding", (int)BindingFailure.None, DiagnosticCode.FunctionFallthrough_Kd, null)]
    [InlineData("Binding", (int)BindingFailure.OriginRelation, DiagnosticCode.UnsatisfiedOriginRelation_Kd, "A5")]
    [InlineData("Binding", (int)BindingFailure.IncompatibleImplementation, DiagnosticCode.IncompatibleContractImplementation_Kd, "A4")]
    [InlineData("Binding", (int)BindingFailure.Unsupported, DiagnosticCode.Unsupported_Kd, "U")]
    public void TheComparedSetNamesItsAnalysis(string partition, int kind, DiagnosticCode code, string? analysis)
        => Assert.Equal(analysis, MirDifferenceLedger.AnalysisOf(new(Enum.Parse<DiagnosticPartition>(partition), (ushort)kind), code));

    // R4-M4: every code an ownership failure reports is compared, except TransferRequired's and Internal's. R4-M67: each MIR pair of the
    // compared set names its row's analysis, and PLAN10 §7's consistency holds: an Ownership requirement with a code the old analysis
    // gives that failure (OwnershipIssue.Code), or ControlFlow with the require fallthrough; a pair outside it, such as MirReport's
    // disagreement record, has none.
    [Fact]
    public void TheComparedSetCoversEachOwnershipCode()
    {
        var images = new HashSet<(DiagnosticRequirement, DiagnosticCode)>();
        foreach (var (failure, code) in PipelineSwitchTest.OwnershipCodes())
        {
            var passes = failure is OwnershipRequirement.TransferRequired or OwnershipRequirement.Internal;
            Assert.True(passes == (MirDifferenceLedger.AnalysisOf(DiagnosticRequirement.Ownership(failure), code) is null), $"{failure} {code}");
            images.Add((DiagnosticRequirement.Ownership(failure), code));
        }

        images.Add((DiagnosticRequirement.ControlFlow, DiagnosticCode.RequireFallthrough_Kd));
        foreach (var (_, code, analysis, mir, _) in MirDifferenceLedger.Compared)
        {
            if (mir is { } requirement)
            {
                Assert.True(images.Contains((requirement, code)), $"{requirement.Name} {code}");
                Assert.Equal(analysis, MirDifferenceLedger.MirAnalysisOf(requirement, code));
            }
        }

        var (transfer, defect) = (OwnershipRequirement.TransferRequired, OwnershipRequirement.Internal);
        Assert.Null(MirDifferenceLedger.MirAnalysisOf(DiagnosticRequirement.Ownership(transfer), DiagnosticCode.TransferRequired_Kd));
        Assert.Null(MirDifferenceLedger.MirAnalysisOf(DiagnosticRequirement.Ownership(defect), DiagnosticCode.InternalInvariant_Kd));
    }

    // R4-M111 (R4-M108): Binding's Origin relation records are compared with A5 only where they are twins, the records of a node whose
    // relation is also a row A5 judges: at a fit every record of the node, the mixed fit's fixed-fixed first chain included, and at a
    // local's clause its body-chain record. An all-fixed-fixed node's records pass through, Binding's until R7; a Closure result over
    // call-local storage is a twin, its Refuted source at the returned value (DECISIONS R4-M152); a call argument's fixed-fixed record beside a body-chain position of the
    // same call, a row without a record at the same node (BU's review N6). Each row has a record that passes through.
    [Theory]
    [InlineData(TwinFits, "UnprovenOriginRelation_Kd A5 return t|UnsatisfiedOriginRelation_Kd A5 return local@ref", "UnprovenOriginRelation_Kd - x")]
    [InlineData(TwinClauses, "UnsatisfiedOriginRelation_Kd A5 origin x.s outlives a", "UnprovenOriginRelation_Kd - origin x.s outlives b")]
    [InlineData("public func main()\n    let f = func (x: i32) => x@ref\n    let r = f(1)\n", "UnsatisfiedOriginRelation_Kd A5 x@ref", "")]
    [InlineData(CallPositions, "", "UnprovenOriginRelation_Kd - t")]
    public void OnlyTwinsOfBindingsOriginRecordsAreCompared(string source, string compared, string passed)
    {
        var c = MirBuildTest.Compile(source);
        var (common, old, _, _) = MirDifferenceLedger.Sides(c, c.BuildMir());
        Assert.Equal(compared.Split('|', StringSplitOptions.RemoveEmptyEntries), OriginRecords(old));
        Assert.Equal(passed.Split('|', StringSplitOptions.RemoveEmptyEntries), OriginRecords(common));

        // Binding's Origin relation records, each `code analysis subject` by the subject's source text, in ordinal order.
        string[] OriginRecords(List<MirDifferenceLedger.Entry> entries) => [.. entries.Where(static x => x.Requirement == "Binding.OriginRelation")
            .Select(x => $"{x.Code} {x.Analysis} {source.Substring(x.Node.Span.Start, x.Node.Span.Length)}").Order(StringComparer.Ordinal)];
    }
}
