// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 8.11.1: effect items declare bounds of their Contract; an invalid item is InvalidEffectBound_Kd
// at the item, with the cause as its Reason and the target requirement as a related location.
public class EffectBoundDeclarationTest
{
    private const string Source = "contract Source\n    associate Item\n    func take(self: uniq/Self) -> Option<Self.Item>\n    func size(self: ref/Self) -> isize\n    func size(self: ref/Self, extra: isize) -> isize\n";

    private const string Main = "public func main() => ()\n";

    // An effect specification anywhere but among a Contract's items (SPEC 8.11.1, F.6; PLAN G219).
    private const string Misplaced = "an effect specification outside a Contract's items";

    // Its whole Note: a SPEC fact that holds at every position, never what the position takes (bu25 review R1: a clause is
    // rejected too under `F is Copy`, a combined requirement or a Contract-wide clause).
    private const string MisplacedNote = "An effect specification is a Contract item and bounds a requirement the Contract inherits";

    private const string Override = "open struct Base\n    public virtual func score(self: objref/Self) -> i32 => 1\n" +
        "struct Derived : Base\n    override func score(self: objref/Self) -> i32\n        ";

    [Fact]
    public void AcceptsClausesAndSpecifications()
    {
        // The Iterator pattern: an inherited lending requirement whose item the child fixes as step-independent.
        const string Declarations = "contract StableSource: Source\n    effect Source.take confined\n" +
            "contract View\n    func read(self: ref/Self) -> ref/i32\n        effect confined\n" +
            "contract Lending\n    associate LentItem(step) for uniq/Self during step\n    func next(self: uniq/Self during step) -> Option<Self.LentItem(step)>\n" +
            "contract Stepping: Lending\n    associate Item\n    associate Lending.LentItem(step) is Item\n    effect Lending.next confined\n" +
            "contract Indexed<K>\n    func at(self: ref/Self, key: K) -> i32\ncontract StrictIndexed: Indexed<i32>\n    effect (Indexed<i32>).at confined\n" +
            "contract Settled: StableSource\n    effect Source.take confined\n";
        var c = MinimalEmissionTest.Analyze(Source + Declarations + Main);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("contract Bounded: Source\n    effect Source.take confined\n    effect Source.take confined\n", "effect Source.take confined", "take already has confined", "Bounded already declares confined")]
    [InlineData("contract Bounded: Source\n    effect Source.size confined\n", "effect Source.size confined", "size names 2 function requirements of Source", "exactly one")]
    [InlineData("contract Bounded: Source\n    effect Source.missing confined\n", "effect Source.missing confined", "Source has no function requirement missing", null)]
    [InlineData("contract Other\n    func run(self: ref/Self) -> ()\ncontract Bounded: Source\n    effect Other.run confined\n", "effect Other.run confined", "Other is not an ancestor of Bounded", null)]
    [InlineData("contract Bounded: Source\n    func own(self: ref/Self) -> ()\n    effect Bounded.own confined\n", "effect Bounded.own confined", "Bounded is this Contract", "effect clause")]
    [InlineData("struct Plain\ncontract Bounded: Source\n    effect Plain.take confined\n", "effect Plain.take confined", "Plain names no Contract", null)]
    [InlineData("contract Bounded\n    func own(self: ref/Self) -> ()\n    effect confined\n", "effect confined", "an effect clause outside a requirement", "Constraint region")]
    [InlineData("contract Bounded: Source\n    func own(self: ref/Self) -> ()\n        effect Source.take confined\n", "effect Source.take confined", Misplaced, MisplacedNote)]
    [InlineData("func check<F>(f: F) -> ()\n    F is Callable<() -> i32>\n        effect Source.take confined\n    return\n", "effect Source.take confined", Misplaced, MisplacedNote)]
    [InlineData("func check<F>(f: F) -> ()\n    F is Copy\n        effect Source.take confined\n    return\n", "effect Source.take confined", Misplaced, MisplacedNote)]
    [InlineData("open struct Base\n    public virtual func score(self: objref/Self) -> i32\n        effect Source.take confined\n        return 1\n", "effect Source.take confined", Misplaced, MisplacedNote)]
    [InlineData("struct S\n    effect confined\n", "effect confined", "an effect item outside a Contract, original virtual function or Callable Constraint", "S declares no bound of its own")]
    [InlineData("func f() -> ()\n    effect confined\n", "effect confined", "an effect item outside a Contract, original virtual function or Callable Constraint", "the function f declares no bound of its own")]
    [InlineData(Override + "effect confined\n        return 2\n", "effect confined", "an effect item outside a Contract, original virtual function or Callable Constraint", "the function score declares no bound")]
    [InlineData(Override + "effect Source.take confined\n        return 2\n", "effect Source.take confined", "an effect item outside a Contract, original virtual function or Callable Constraint", "the function score declares no bound")]
    public void RejectsInvalidEffectItems(string declarations, string text, string cause, string? note)
    {
        var c = MinimalEmissionTest.Analyze(Source + declarations + Main);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c), static x => x.Severity == Kimi.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal(nameof(DiagnosticCode.InvalidEffectBound_Kd), error.Code);
        Assert.Equal(text, error.Text);
        Assert.Equal(cause, error.Label);
        if (cause == Misplaced)
        {
            Assert.Equal(note, error.Note);
        }
        else if (note is not null)
        {
            Assert.Contains(note, error.Note);
        }
    }

    // SPEC 8.11.1, 8.11.1: a bound declared twice in one Contract relates the target requirement and the earlier bound;
    // restating an ancestor's bound is valid (AcceptsClausesAndSpecifications).
    [Fact]
    public void RelatesTheRequirementAndTheEarlierBound()
    {
        var result = DiagnosticCorpus.Check(Source + "contract StableSource: Source\n    effect Source.take confined\n    effect Source.take confined\n" + Main);
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.InvalidEffectBound_Kd), error.Code);
        Assert.Equal(["bound", "requirement"], error.Related!.Select(static x => x.Role).ToArray());
    }

    // Warm rebinding of valid and rejected effect items reuses every table and the rejection store.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmRebindOfEffectDeclarationsDoesNotAllocate()
    {
        const string Declarations = "contract StableSource: Source\n    effect Source.take confined\n" +
            "contract Doubled: Source\n    effect Source.size confined\n" +
            "open struct Base\n    public virtual func score(self: objref/Self) -> i32\n        effect Source.take confined\n        return 1\n";
        var c = MinimalEmissionTest.Analyze(Source + Declarations + Main);
        Assert.False(c.Binding.Result.IsComplete);
        var allocated = AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(0, allocated);
    }
}
