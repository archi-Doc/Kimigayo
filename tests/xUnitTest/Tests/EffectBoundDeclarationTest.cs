// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 8.4.10.1, 8.4.10.3, 8.4.10.6: effect items declare bounds of their Contract; an invalid item is InvalidEffectBound_Kd
// at the item, with the cause as its Reason and the target requirement as a related location.
public class EffectBoundDeclarationTest
{
    private const string Source = "contract Source\n    associate Item\n    func take(self: uniq/Self) -> Option<Self.Item>\n    func size(self: ref/Self) -> isize\n    func size(self: ref/Self, extra: isize) -> isize\n";

    private const string Main = "public func main() => ()\n";

    [Fact]
    public void AcceptsClausesAndSpecifications()
    {
        // The Iterator pattern: an inherited lending requirement whose item the child fixes as step-independent.
        const string Declarations = "contract StableSource: Source\n    effect Source.take preserves results\n    effect Source.take confined\n" +
            "contract View\n    func read(self: ref/Self) -> ref/i32\n        effect confined\n" +
            "contract Lending\n    associate LentItem(step) for uniq/Self during step\n    func next(self: uniq/Self during step) -> Option<Self.LentItem(step)>\n" +
            "contract Stepping: Lending\n    associate Item\n    associate Lending.LentItem(step) is Item\n    effect Lending.next preserves results\n" +
            "contract Indexed<K>\n    func at(self: ref/Self, key: K) -> i32\ncontract StrictIndexed: Indexed<i32>\n    effect (Indexed<i32>).at confined\n" +
            "contract Settled: StableSource\n    effect Source.take preserves results\n";
        var c = MinimalEmissionTest.Analyze(Source + Declarations + Main);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("contract Cursor\n    associate Lent(step) for uniq/Self during step\n    func advance(self: uniq/Self during step) -> Option<Self.Lent(step)>\n        effect preserves results\n", "effect preserves results", "Self.Lent(step) may depend on the receiver borrow", "receiver's Origin step")]
    [InlineData("contract Peek\n    associate Item\n    func peek(self: ref/Self) -> Option<ref/Self.Item>\n        effect preserves results\n", "effect preserves results", "ref/Self.Item may depend on the receiver borrow", "omitted Origin")]
    [InlineData("contract Taker\n    func take(self: Self) -> i32\n        effect preserves results\n", "effect preserves results", "take has no borrowed receiver", "borrowed receiver")]
    [InlineData("contract Loose: Lending\n    effect Lending.next preserves results\ncontract Lending\n    associate LentItem(step) for uniq/Self during step\n    func next(self: uniq/Self during step) -> Option<Self.LentItem(step)>\n", "effect Lending.next preserves results", "Self.LentItem(step) may depend on the receiver borrow", null)]
    [InlineData("contract Bounded: Source\n    effect Source.take confined\n    effect Source.take confined\n", "effect Source.take confined", "take already has confined", "Bounded already declares confined")]
    [InlineData("contract Bounded: Source\n    effect Source.size confined\n", "effect Source.size confined", "size names 2 function requirements of Source", "exactly one")]
    [InlineData("contract Bounded: Source\n    effect Source.missing confined\n", "effect Source.missing confined", "Source has no function requirement missing", null)]
    [InlineData("contract Other\n    func run(self: ref/Self) -> ()\ncontract Bounded: Source\n    effect Other.run confined\n", "effect Other.run confined", "Other is not an ancestor of Bounded", null)]
    [InlineData("contract Bounded: Source\n    func own(self: ref/Self) -> ()\n    effect Bounded.own confined\n", "effect Bounded.own confined", "Bounded is this Contract", "effect clause")]
    [InlineData("struct Plain\ncontract Bounded: Source\n    effect Plain.take confined\n", "effect Plain.take confined", "Plain names no Contract", null)]
    [InlineData("contract Bounded\n    func own(self: ref/Self) -> ()\n    effect confined\n", "effect confined", "an effect clause outside a requirement", "Constraint region")]
    [InlineData("contract Bounded: Source\n    func own(self: ref/Self) -> ()\n        effect Source.take confined\n", "effect Source.take confined", "an effect specification in a requirement", "Contract item")]
    [InlineData("struct S\n    effect confined\n", "effect confined", "an effect item outside a Contract or a Callable Constraint", "S declares no bound of its own")]
    [InlineData("func f() -> ()\n    effect confined\n", "effect confined", "an effect item outside a Contract or a Callable Constraint", "the function f declares no bound of its own")]
    public void RejectsInvalidEffectItems(string declarations, string text, string cause, string? note)
    {
        var c = MinimalEmissionTest.Analyze(Source + declarations + Main);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal(nameof(DiagnosticCode.InvalidEffectBound_Kd), error.Code);
        Assert.Equal(text, error.Text);
        Assert.Equal(cause, error.Label);
        if (note is not null)
        {
            Assert.Contains(note, error.Note);
        }
    }

    // SPEC 8.4.10.1, 8.4.10.6: a bound declared twice in one Contract relates the target requirement and the earlier bound;
    // restating an ancestor's bound is valid (AcceptsClausesAndSpecifications).
    [Fact]
    public void RelatesTheRequirementAndTheEarlierBound()
    {
        var result = DiagnosticCorpus.Check(Source + "contract StableSource: Source\n    effect Source.take preserves results\n    effect Source.take preserves results\n" + Main);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.InvalidEffectBound_Kd), error.Code);
        Assert.Equal(["bound", "requirement"], error.Related!.Select(static x => x.Role).ToArray());
    }

    // Warm rebinding of valid and rejected effect items reuses every table and the rejection store.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmRebindOfEffectDeclarationsDoesNotAllocate()
    {
        const string Declarations = "contract StableSource: Source\n    effect Source.take preserves results\n" +
            "contract Peek\n    associate Item\n    func peek(self: ref/Self) -> Option<ref/Self.Item>\n        effect preserves results\n";
        var c = MinimalEmissionTest.Analyze(Source + Declarations + Main);
        Assert.False(c.Binding.Result.IsComplete);
        var allocated = AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(0, allocated);
    }
}
