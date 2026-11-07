// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// A5 over what a value holds (TyCtxt.Holdings, G416 U2): the rows the entry table must keep, each form labelled from SPEC.
public sealed partial class MirBorrowsTest
{
    private const string Limited = "grow(w@uniq, y@ref) OwnershipDepthLimit_Kd";
    private const string Stored = "store(d@uniq, value@ref) OwnershipDepthLimit_Kd";
    private const string Conflicts = "slot@ref ComparisonLoanConflict_Kd|y = 5 ComparisonLoanConflict_Kd";

    private const string HoldingShapes = """
        struct Window {a}
            internal let loan: Loan<uniq/(ref/i32 during a) during a>
            public init(target: uniq/(ref/i32 during a) during a) => self.loan = Loan<uniq/(ref/i32 during a) during a>.init(target)
        func grow(h: uniq/(uniq/(Window during a) during b), extra: ref/i32 during a) => ()

        """;

    // SPEC 15.3.5, 15.6.4 item 1: a `Loan<T>` Field counts as storing a T for all slot properties, with no address and no access, so its slot
    // keeps a real requirement. Past an exclusive reference a call may replace the whole value, so the Type argument stays Exclusive and the
    // write reaches the holder: chainBad (invalid) stores y's Loan in w's slot past `hh`, by hh's guard alone. In the value's own storage the
    // same entry is LoanOnly (windowOk, oracle E).
    [Fact]
    public void ATypeArgumentStaysExclusivePastAnExclusiveReference()
    {
        var problems = Problems(HoldingShapes + """
            func chainBad()
                var z = 1
                var y = 2
                var slot = z@ref
                var w = Window.init(target: slot@uniq)
                var hh = w@uniq
                grow(hh@uniq, y@ref)
                y = 5
                inspect(w@ref)

            """);
        Assert.Contains("chainBad: w@ref", problems);
    }

    // The owner's O-47, O-49 (a), PLAN G408: row 9's `(K, V)` entry stays and the bound stays 32, so the named limit is a storage nest past 32
    // entries, a Dictionary level counting two: A5's located OwnershipDepthLimit_Kd at the call where a check rests on the guard; a program whose
    // answer comes from Binding's row has no record past it on either analysis (rulings-4). This row is a nest of Dictionaries alone. With an
    // exclusive reference at depth n the call may store y's Loan in `slot` (invalid, SPEC 15.6.4, 15.6.2): through 15 the conflict is reported,
    // from 16 the limit record alone, which stands for it (review B1). A Loan-free nest under an exclusive reference (valid) is exact through
    // 15 and limited from 16, by value through 16 and limited from 17: a Resource record, IMPL 21.3.2.
    [Theory]
    [InlineData(15, Conflicts, "", "")]
    [InlineData(16, Limited, "", "", true)]
    [InlineData(17, Limited, Stored, Stored)]
    [InlineData(31, Limited, Stored, Stored)]
    public void ADictionaryNestPastTheBoundIsALocatedLimit(int depth, string invalid, string pastExclusive, string byValue, bool limitedPastExclusive = false)
        => AssertNestLimit(x => Repeated("Dictionary<i32, ", depth) + x + Repeated(">", depth), string.Empty, invalid, limitedPastExclusive ? Stored : pastExclusive, byValue);

    // The same limit on a mixed nest (review R1), which is the limit's general form: the edge is entries from the holder's root, not Dictionary
    // levels, so it shifts with the holder and with what else the nest holds. A level of a Field, a Tuple or an Array over a Dictionary is three
    // entries: exact at 10 levels, 30 entries; at 11, 33, an invalid program loses its conflict records to the limit record, and a valid one
    // past an exclusive reference is newly rejected, a named Resource record; by value, with no reference step on the path, the valid one
    // follows at 12. One Dictionary under 30 Tuples is past the limit by the same count, past an exclusive reference only.
    [Theory]
    [InlineData("field", 10, Conflicts, "", "")]
    [InlineData("field", 11, Limited, Stored, "")]
    [InlineData("field", 12, Limited, Stored, Stored)]
    [InlineData("tuple", 10, Conflicts, "", "")]
    [InlineData("tuple", 11, Limited, Stored, "")]
    [InlineData("tuple", 12, Limited, Stored, Stored)]
    [InlineData("array", 10, Conflicts, "", "")]
    [InlineData("array", 11, Limited, Stored, "")]
    [InlineData("array", 12, Limited, Stored, Stored)]
    [InlineData("tuples", 30, Limited, Stored, "")]
    public void AMixedNestPastTheBoundIsALocatedLimit(string form, int levels, string invalid, string pastExclusive, string byValue)
    {
        var (open, close) = form switch
        {
            "field" => ("Hd<Dictionary<i32, ", ">>"),
            "tuple" => ("(i32, Dictionary<i32, ", ">)"),
            "array" => ("Array<Dictionary<i32, ", ">>"),
            _ => ("(", ", i32)"),
        };
        var inner = form == "tuples" ? (Func<string, string>)(static x => $"Dictionary<i32, {x}>") : static x => x;
        AssertNestLimit(x => Repeated(open, levels) + inner(x) + Repeated(close, levels), "struct Hd<T>\n    public var item: T\n", invalid, pastExclusive, byValue);
    }

    private static string Repeated(string text, int count) => string.Concat(Enumerable.Repeat(text, count));

    // A nest's records in the three forms: an exclusive reference at its leaf (invalid: the call may store y's Loan in `slot`), and a Loan-free
    // nest under an exclusive reference and by value (valid, MirBorrowsTest.DepthLimit's GuardForm).
    private static void AssertNestLimit(Func<string, string> nest, string shapes, string invalid, string pastExclusive, string byValue)
    {
        const string Target = "uniq/(ref/i32 during a) during a";
        var (held, loanFree) = (nest(Target), nest("i32"));
        Assert.Equal(Rows(invalid), Records($"func make(target: {Target}) -> {held} => $abort(\"x\")\nfunc grow(h: uniq/({held}) during c, extra: ref/i32 during a) => ()\n" +
            "func inspect<T>(value: ref/T) => ()\nfunc f()\n    var z = 1\n    var y = 2\n    var slot = z@ref\n    var w = make(slot@uniq)\n    grow(w@uniq, y@ref)\n    y = 5\n    inspect(slot@ref)\n"));
        Assert.Equal(Rows(pastExclusive), Records(Guard($"uniq/{loanFree} during e", $"uniq/{loanFree}", "z")));
        Assert.Equal(Rows(byValue), Records(Guard(loanFree, loanFree, "z@move")));

        static string Guard(string part, string parameter, string read) => "func inspect<T>(value: ref/T) => ()\n" +
            $"func store(t: uniq/((ref/i32 during a, ref/(ref/i32 during b) during c, {part})), v: ref/i32 during a) => ()\n" +
            $"func f(z: {parameter}) -> ()\n    var value = 42\n    var x = 1\n    var r = x@ref\n    var d = (x@ref, r@ref, {read})\n" +
            "    store(d@uniq, value@ref)\n    value = 99\n    inspect(r@ref)\n    return\n";

        IEnumerable<string> Records(string source)
        {
            var c = CompileDepthForm(shapes + source + "public func main() => ()\n");
            return DepthRecords(c, Analyze(c), "f").Select(static x => $"{x.Line} {x.Code}");
        }
    }
}
