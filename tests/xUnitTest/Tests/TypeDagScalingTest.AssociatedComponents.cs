// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public partial class TypeDagScalingTest
{
    // R7-C4 commit 0 (PLAN G172; design Revision 2 §3.3 and §7, the delta check's D2-C2): associated-Type inference evaluates one
    // component of definitions per request, in an order where each component follows those it reads, so no request nests in another.
    // A one-value chain of 10,000 definitions (valid by SPEC 8.5.5: every X is i32) is 10,000 requests in sequence; a two-value ring of
    // 10,000 definitions (valid by SPEC 8.3.3 Finality: every X and Y is i32) is one component whose held states pass OQ2's items
    // (65,536), one Resource record at its least definition by name (SPEC 8.3.3 Limits, 23.1.1), as is a ring of 9 (the model's 65,536
    // states: past the bound). The record is AnalysisLimit_Kd naming its bound (the owner's O-484 (3 ii)); dev's nested batches recurse once
    // per definition of the chain.
    // R7-C4 (C4 ruling 11; design §3.3 and §6): a request evaluates the model's held states (`value_model_r2.py shapes`): each held state
    // keyed by (binding, hold n Reach(binding)), its own run collecting only its binding's sources, the requirements' Types read with the
    // definition's bindings held. Rings of 4, 6 and 8 definitions (valid by SPEC 8.3.3 Finality) are one component each; a two-value chain
    // of 30 (valid by SPEC 8.5.5) is 31 components of one definition. Each is accepted, every read in its reader's Reach (the validator).
    // C4 ruling 17 C2 (design §6's scaling rows, the counts the model's `value_model_r2.py shapes`, labelled before any run; each valid,
    // every value i32): `over` at 30 and 40 (acyclic in its real reads; the model's 64 and 84 plus Z.Y, a binding the program's Z declares,
    // since CC has two values, and the model's `over` leaves out), the diamond ladder (3n + 1 one-binding components), the ladder and the
    // two-value chain with a back edge (one component each).
    [Theory]
    [InlineData("ring", 4, 364)]
    [InlineData("ring", 6, 4122)]
    [InlineData("ring", 8, 41080)]
    [InlineData("two-value chain", 30, 62)]
    [InlineData("over", 30, 65)]
    [InlineData("over", 40, 85)]
    [InlineData("diamond ladder", 30, 91)]
    [InlineData("ladder, back edge", 4, 590)]
    [InlineData("ladder, back edge", 8, 18114)]
    [InlineData("two-value chain, back edge", 4, 352)]
    [InlineData("two-value chain, back edge", 8, 21594)]
    public void AComponentEvaluatesTheModelsHeldStates(string shape, int size, int states)
    {
        var c = MinimalEmissionTest.Analyze(Shape(shape, size));
        Assert.Equal((shape, size, states, 0), (shape, size, c.Binding.InferenceStates, CompilationTestHelper.BindingErrors(c).Length));
    }

    [Fact]
    public void AChainOfTenThousandDefinitionsStaysWithinTheStack()
        => CompilationTestHelper.AssertBindingRecordsAtLines("chain 10000", Verification.AssociatedInferenceWorkloads.OneValueChain(10000), string.Empty);

    // C4 ruling 17 C4: Explicit for its time (the review's 36.4 s cold in one Bind, 58.2 s in the Unit with the chain; dev did not finish
    // in 400 s): 65,536 states over 20,000 bindings, the decision at the limit reading every binding once.
    [Fact(Explicit = true)]
    public void ARingOfTenThousandDefinitionsStaysWithinTheStack() => AssertOneLimitRecord(10000);

    // C4 ruling 17 B2, C1 (the review's probes, labelled before any run, `C4\review-rev3.md` §1): past the bound the component's one
    // AnalysisLimit_Kd is at its least definition by full name, in every declaration order (nest9: GA.R0 before GB.R0), and a binding's
    // Error that stands is its conformance's own record beside it (std9: R0.Z's constant sources i32 and i64; conf9: R5.X's), whichever
    // of the contract's values or the sources is declared first; no other definition reports a Language record for a Resource failure.
    // Each entry names a record's code and the definition whose `Self is RC` holds it; the headers' records over a failed conformance stay
    // open (dependent). C4 ruling 19 D1 (the delta read's lim-dup, lim-layout and lim-dup-r1, labelled before any run by SPEC 8.3.3 Limits,
    // 23.1.1-23.1.2): std9-ok whose least definition's declaration fails (R0's header `{h, h}`: DuplicateBinding_Kd; R0 holding
    // `var next: Option<R0>`: InvalidInlineLayout_Kd) still holds the component's one AnalysisLimit_Kd, written on every path of R0's
    // conformance check, its early exit included, and no other definition reports a Language record for its Resource failure; the twin
    // fails R1's header instead. C4 ruling 20: the header's own record is independent of the limit (its check reads the header or the
    // layout, not the inference), so the rows read the header codes too: DuplicateBinding_Kd and InvalidInlineLayout_Kd
    // at the declaration (an entry's third part names its site, else the definition's `Self is RC`). C-545 B2 (F's twins rv1-f and
    // rv1-s, labelled before any run by SPEC 8.3.3 Limits, 23.1.1-23.1.2): std9-ok whose contract and definitions gain
    // `g(self: ref/Self, x: ref/Box)`, Box resting on a syntax recovery (lim-box) or well formed (lim-box-s), still holds the one
    // AnalysisLimit_Kd at R0, which depends on nothing the check found, and no AssociatedTypeInferenceFailed_Kd; records of a check
    // reading Box (g's comparison) may rest.
    [Theory]
    [InlineData("std9-z1", "AnalysisLimit_Kd@struct R0 | AssociatedTypeInferenceFailed_Kd@struct R0")]
    [InlineData("std9-z2", "AnalysisLimit_Kd@struct R0 | AssociatedTypeInferenceFailed_Kd@struct R0")]
    [InlineData("std9-ok", "AnalysisLimit_Kd@struct R0")]
    [InlineData("conf9-ef", "AnalysisLimit_Kd@struct R0 | AssociatedTypeInferenceFailed_Kd@struct R5")]
    [InlineData("conf9-fe", "AnalysisLimit_Kd@struct R0 | AssociatedTypeInferenceFailed_Kd@struct R5")]
    [InlineData("nest9-ab", "AnalysisLimit_Kd@struct GA")]
    [InlineData("nest9-ba", "AnalysisLimit_Kd@struct GA")]
    [InlineData("lim-dup", "AnalysisLimit_Kd@struct R0 {h, h} | DuplicateBinding_Kd@struct R0 {h, h}@struct")]
    [InlineData("lim-layout", "AnalysisLimit_Kd@struct R0 | InvalidInlineLayout_Kd@struct R0@struct")]
    [InlineData("lim-dup-r1", "AnalysisLimit_Kd@struct R0 | DuplicateBinding_Kd@struct R1 {h, h}@struct")]
    [InlineData("lim-box", "AnalysisLimit_Kd@struct R0")]
    [InlineData("lim-box-s", "AnalysisLimit_Kd@struct R0")]
    public void TheLimitRecordIsTheComponentsOneAndAStandingErrorStays(string form, string records)
    {
        var ok = Verification.AssociatedInferenceWorkloads.RingWithZ(true, "i32");
        var program = form switch
        {
            "std9-z1" => Verification.AssociatedInferenceWorkloads.RingWithZ(true, "i64"),
            "std9-z2" => Verification.AssociatedInferenceWorkloads.RingWithZ(false, "i64"),
            "std9-ok" => ok,
            "lim-dup" => ok.Replace("struct R0\n", "struct R0 {h, h}\n", StringComparison.Ordinal),
            "lim-layout" => ok.Replace("struct R0\n", "struct R0\n    var next: Option<R0>\n", StringComparison.Ordinal),
            "lim-dup-r1" => ok.Replace("struct R1\n", "struct R1 {h, h}\n", StringComparison.Ordinal),
            "lim-box" or "lim-box-s" => $"open struct L0<X>\n    public var n: i32 = 0\nopen struct Box : L0<i32{(form == "lim-box" ? string.Empty : ">")}\n" +
                ok.Replace("    func f(self: ref/Self) -> Self.Z\n", "    func f(self: ref/Self) -> Self.Z\n    func g(self: ref/Self, x: ref/Box) -> i32\n", StringComparison.Ordinal)
                    .Replace("    public func f(self: ref/Self) -> i32 => 0\n", "    public func f(self: ref/Self) -> i32 => 0\n    public func g(self: ref/Self, x: ref/Box) -> i32 => 0\n", StringComparison.Ordinal),
            "conf9-ef" => Verification.AssociatedInferenceWorkloads.RingWithConflict(true),
            "conf9-fe" => Verification.AssociatedInferenceWorkloads.RingWithConflict(false),
            _ => Verification.AssociatedInferenceWorkloads.NestedRing(form == "nest9-ab"),
        };
        var expected = records.Split(" | ").Select(x => x.Split('@')).Select(x => $"{x[0]}@{program.IndexOf(x.Length > 2 ? x[2] : "Self is RC", program.IndexOf(x[1] + "\n", StringComparison.Ordinal), StringComparison.Ordinal)}");
        var actual = CompilationTestHelper.BindingErrors(MinimalEmissionTest.Analyze(program))
            .Where(static x => x.Code is nameof(Kimi.DiagnosticCode.AnalysisLimit_Kd) or nameof(Kimi.DiagnosticCode.AssociatedTypeInferenceFailed_Kd) or
                nameof(Kimi.DiagnosticCode.DuplicateBinding_Kd) or nameof(Kimi.DiagnosticCode.InvalidInlineLayout_Kd)).Select(static x => $"{x.Code}@{x.Span.Start}");
        Assert.Equal((form, string.Join(" | ", expected.Order(StringComparer.Ordinal))), (form, string.Join(" | ", actual.Order(StringComparer.Ordinal))));
    }

    [Fact]
    public void ARingPastTheBoundIsOneResourceRecordAtItsLeastDefinition() => AssertOneLimitRecord(9);

    // A two-value ring past OQ2's items: its request holds 65,536 states, and its one Resource record is at R0's conformance, naming the bound.
    private static void AssertOneLimitRecord(int size)
    {
        var program = Verification.AssociatedInferenceWorkloads.TwoValueRing(size);
        var c = MinimalEmissionTest.Analyze(program);
        var limit = Assert.Single(CompilationTestHelper.BindingErrors(c), static x => x.Code == nameof(Kimi.DiagnosticCode.AnalysisLimit_Kd));
        var at = program.IndexOf("struct R0\n", StringComparison.Ordinal) + "struct R0\n    ".Length;
        const string Text = "A judgment here is not decided within the implementation limit of 65536 held states of one component of associated-Type inference";
        Assert.Equal((size, Kimi.Compiler.Binding.InferenceStateLimit, at, Text), (size, c.Binding.InferenceStates, limit.Span.Start, limit.Message));
    }

    private static string Shape(string shape, int size) => shape switch
    {
        "ring" => Verification.AssociatedInferenceWorkloads.TwoValueRing(size),
        "two-value chain" => Verification.AssociatedInferenceWorkloads.TwoValueChain(size),
        "over" => Verification.AssociatedInferenceWorkloads.Over(size),
        "diamond ladder" => Verification.AssociatedInferenceWorkloads.Ladder(size, "i32"),
        "ladder, back edge" => Verification.AssociatedInferenceWorkloads.Ladder(size, "L0.(LC).X"),
        _ => Verification.AssociatedInferenceWorkloads.TwoValueChainBackEdge(size),
    };
}
