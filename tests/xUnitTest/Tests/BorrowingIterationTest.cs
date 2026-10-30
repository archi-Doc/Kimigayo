// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public class BorrowingIterationTest
{
    public static string Declarations(bool exclusive, bool lending = false)
    {
        var entry = exclusive ? "UniqIterable" : "Iterable";
        var method = exclusive ? "iterateUniq" : "iterate";
        var mode = exclusive ? "uniq" : "ref";
        var item = lending ? "ref/i32 during step" : "i32";
        return "struct Cursor {source}\n    Self is LendingIterator\n    associate LentItem(step) is " + item + "\n    let value: ref/i32 during source\n    var count: i32 = 0\n    public init(value: ref/i32 during source) => self.value = value\n    public func next(self: uniq/Self during step) -> Option<" + item + ">\n        if self.count == 3 => return .None\n        self.count += 1\n        return .Some(self.value)\nstruct Values\n    Self is " + entry + "\n    associate " + entry + ".IteratorType(source) is Cursor during source\n    var value: i32 = 7\n    public init() => ()\n    public func " + method + "(self: " + mode + "/Self during source) -> Cursor during source => Cursor.init(self.value@ref)\n";
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void BorrowingLoopsUseTheirSelectedEntry(bool exclusive, bool lending)
    {
        var source = Declarations(exclusive, lending) + "var values = Values.init()\nvar sum = 0\nfor item in values" + (exclusive ? "@uniq" : string.Empty) + "\n    sum += item\nrequire sum == 21 else => $abort(\"sum\")\nConsole.writeLine(\"borrow loop\")";
        ScalarEmissionTest.EmitFixture("AssociatedBorrowingLoop" + (exclusive ? "Uniq" : "Ref") + (lending ? "Lending" : "Value"), source, "borrow loop\n");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenericBorrowingLoopCountsUnknownItems(bool exclusive)
    {
        var entry = exclusive ? "UniqIterable" : "Iterable";
        var mode = exclusive ? "uniq" : "ref";
        var source = Declarations(exclusive) + "func count<T>(values: " + mode + "/T) -> i32\n    T is " + entry + "\n    var result = 0\n    for _ in values\n        result += 1\n    return result\nvar values = Values.init()\nrequire count(values@" + mode + ") == 3 else => $abort(\"count\")\nConsole.writeLine(\"generic loop\")";
        ScalarEmissionTest.EmitFixture("AssociatedBorrowingGenericLoop" + mode, source, "generic loop\n");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelfLendingStepEndsBeforeBackedge(bool skipMiddle)
    {
        var source = Declarations(false, true).Replace(".Some(self.value)", ".Some(self.count@ref)", StringComparison.Ordinal) +
            "var values = Values.init()\nvar sum = 0\nfor item in values\n" + (skipMiddle ? "    if item == 2 => continue\n" : string.Empty) + "    sum += item\nrequire sum == " + (skipMiddle ? "4" : "6") + " else => $abort(\"step\")\nConsole.writeLine(\"step\")";
        ScalarEmissionTest.EmitFixture("AssociatedBorrowingSelfStep" + (skipMiddle ? "Continue" : "Backedge"), source, "step\n");
    }

    // SPEC 3.4.3, 14.6.2: the entry is selected at the referent of every reference layer of the Subject.
    [Fact]
    public void EntryIsSelectedThroughEveryReferenceLayer()
    {
        var source = Declarations(false) + "let values = Values.init()\nlet r = values@ref\nlet rr = r@ref\nvar sum = 0\nfor item in rr\n    sum += item\nrequire sum == 21 else => $abort(\"layers\")\nConsole.writeLine(\"layers\")";
        ScalarEmissionTest.EmitFixture("AssociatedBorrowingLayers", source, "layers\n");
    }

    [Fact]
    public void SharedEntryMayReturnMutableOwnedItems()
    {
        var source = Declarations(false) + "var values = Values.init()\nvar sum = 0\nfor var item in values\n    item = 2\n    sum += item\nrequire sum == 6 else => $abort(\"mutable item\")\nConsole.writeLine(\"mutable item\")";
        ScalarEmissionTest.EmitFixture("AssociatedBorrowingMutableItem", source, "mutable item\n");
    }

    [Theory]
    [InlineData(false, "@uniq")]
    [InlineData(true, "")]
    [InlineData(false, "@move")]
    public void OnlyTheSelectedEntryAuthorizesIteration(bool exclusive, string acquisition)
        => Assert.False(MinimalEmissionTest.Analyze(Declarations(exclusive) + "var values = Values.init()\nfor _ in values" + acquisition + " => ()").Binding.Result.IsComplete);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoopProtectsItsBorrowedSource(bool exclusive)
    {
        var c = MinimalEmissionTest.Analyze(Declarations(exclusive) + "var values = Values.init()\nfor item in values" + (exclusive ? "@uniq" : string.Empty) + "\n    values = Values.init()\n    require item == 7 else => $abort(\"source\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.False(c.Analysis.IsVerified);
    }

    // SPEC 15.6.6, 16.3.3, 8.15.1 (PLAN10-X1): a generic body is checked for every instance, so a holder whose Type has an abstract part
    // not proven Copy may observe its Loans when it is destroyed: replacing `local` while the iterator or view it lent lives to its cleanup
    // conflicts, also with the abstract part inside an Option, while the concrete twin, whose Cursor has no drop, and a Copy view are valid.
    [Theory]
    [InlineData("<T>(first: T, second: T)\n    T is Iterable", "var it = local.iterate()\n    _ = it.next()", "var it")]
    [InlineData("<T>(first: T, second: T)\n    T is Iterable", "let it = Option.Some(local.iterate())", "let it")]
    [InlineData("(first: Values, second: Values)", "var it = local.iterate()\n    _ = it.next()", null)]
    [InlineData("<T>(first: T, second: T)\n    T is Lens", "let v = local.look()", "let v")]
    [InlineData("<T>(first: T, second: T)\n    T is CopyLens", "let v = local.look()", null)]
    public void AnAbstractHolderMayObserveItsLoansWhenDestroyed(string signature, string hold, string? loan)
    {
        var source = Declarations(false) + "contract Lens\n    associate View(source)\n    func look(self: ref/Self during source) -> Self.View(source)\n" +
            "contract CopyLens\n    associate View(source) is Copy\n    func look(self: ref/Self during source) -> Self.View(source)\n" +
            "func hold" + signature + "\n    var local = first@move\n    " + hold + "\n    local = second@move\npublic func main() => ()\n";
        AssertSourceReplacement(source, loan);
    }

    // SPEC 13.7.1, 15.6.6 (PLAN G176): `=` destroys the holder's old value before placing the new one, so where that destruction may
    // observe (a Cursor with a drop, an abstract iterator), replacing the source the old value borrows first conflicts with it, also where
    // the holder was moved out on one path only (a conditional replacement); moving the holder out first, assigning it first, also after a
    // conditional move, or a Cursor whose destruction observes nothing is valid.
    [Theory]
    [InlineData("(first: Values, second: Values, third: Values)", true, "local = second@move\n    it = other.iterate()", "var it")]
    [InlineData("(first: Values, second: Values, third: Values, c: bool)", true, "if c => sink(it@move)\n    local = second@move\n    it = other.iterate()", "var it")]
    [InlineData("(first: Values, second: Values, third: Values, c: bool)", true, "if c => sink(it@move)\n    it = other.iterate()\n    local = second@move", null)]
    [InlineData("<T>(first: T, second: T, third: T)\n    T is Iterable", false, "local = second@move\n    it = other.iterate()", "var it")]
    [InlineData("(first: Values, second: Values, third: Values)", true, "sink(it@move)\n    local = second@move\n    it = other.iterate()", null)]
    [InlineData("(first: Values, second: Values, third: Values)", true, "it = other.iterate()\n    local = second@move", null)]
    [InlineData("(first: Values, second: Values, third: Values)", false, "local = second@move\n    it = other.iterate()", null)]
    public void ReplacingAHolderMayObserveItsOldValue(string signature, bool drop, string replace, string? loan)
    {
        var declarations = Declarations(false);
        if (drop)
        {
            declarations = declarations.Replace(
                "=> self.value = value\n", "=> self.value = value\n    drop\n        Console.writeLine(\"\\(self.value@follow)\")\n", StringComparison.Ordinal);
        }

        var source = declarations + "func sink<T>(value: T) => ()\nfunc hold" + signature + "\n    var local = first@move\n    var other = third@move\n" +
            "    var it = local.iterate()\n    _ = it.next()\n    " + replace + "\n    _ = it.next()\npublic func main() => ()\n";
        AssertSourceReplacement(source, loan);
    }

    [Theory]
    [InlineData("origin a outlives b", true)]
    [InlineData("", false)]
    [InlineData("origin b outlives a", false)]
    public void EnumPayloadShorteningNeedsProof(string relation, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("func f(value: ref/i32 during a, other: ref/i32 during b) -> Option<ref/i32 during b>\n    " + relation + "\n    return .Some(value)");
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    // A source's one Loan conflict at `local = second@move`, whose Loan the value declared by `loan` keeps; no diagnostic for null.
    private static void AssertSourceReplacement(string source, string? loan)
    {
        var diagnostics = DiagnosticCorpus.Check(source).Diagnostics;
        if (loan is null)
        {
            Assert.Empty(diagnostics);
            return;
        }

        var record = Assert.Single(diagnostics);
        Assert.Equal(nameof(DiagnosticCode.ComparisonLoanConflict_Kd), record.Code);
        Assert.Equal("local = second@move", Text(record.Span));
        Assert.StartsWith(loan, Text(Assert.Single(record.Related!, static x => x.Role == "loan").Span), StringComparison.Ordinal);

        string Text(SourceSpan? span) => span is { } value ? source.Substring(value.Start, value.Length) : string.Empty;
    }
}
