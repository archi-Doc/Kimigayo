// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// A5 over what a value holds (TyCtxt.Holdings, G416 U2): the rows the entry table must keep, each form labelled from SPEC.
public sealed partial class MirBorrowsTest
{
    private const string Limited = "grow(w@uniq, y@ref) OwnershipDepthLimit_Kd";

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

    // The owner's O-47, PLAN G408: row 9's `(K, V)` entry stays and the bound stays 32, so a Dictionary level costs two steps and a nest reaches
    // the bound at half a Tuple's depth: a named limit, A5's located OwnershipDepthLimit_Kd at the call, where Binding's folds are silent past
    // theirs. With an exclusive reference at depth n the call may store y's Loan in `slot` (invalid, SPEC 15.6.4, 15.6.2): through 15 the
    // conflict is reported, from 16 the limit record alone, which stands for it (review B1). A Loan-free nest under an exclusive reference (valid)
    // is exact through 15 and limited from 16, by value through 16 and limited from 17: a Resource record, IMPL 21.3.2.
    [Theory]
    [InlineData(15, "slot@ref ComparisonLoanConflict_Kd|y = 5 ComparisonLoanConflict_Kd", "", "")]
    [InlineData(16, Limited, "store(d@uniq, value@ref) OwnershipDepthLimit_Kd", "")]
    [InlineData(17, Limited, "store(d@uniq, value@ref) OwnershipDepthLimit_Kd", "store(d@uniq, value@ref) OwnershipDepthLimit_Kd")]
    [InlineData(31, Limited, "store(d@uniq, value@ref) OwnershipDepthLimit_Kd", "store(d@uniq, value@ref) OwnershipDepthLimit_Kd")]
    public void ADictionaryNestPastTheBoundIsALocatedLimit(int depth, string invalid, string pastExclusive, string byValue)
    {
        const string Target = "uniq/(ref/i32 during a) during a";
        var nest = Nest(Target);
        var loanFree = Nest("i32");
        Assert.Equal(Rows(invalid), Records($"func make(target: {Target}) -> {nest} => $abort(\"x\")\nfunc grow(h: uniq/({nest}) during c, extra: ref/i32 during a) => ()\n" +
            "func inspect<T>(value: ref/T) => ()\nfunc f()\n    var z = 1\n    var y = 2\n    var slot = z@ref\n    var w = make(slot@uniq)\n    grow(w@uniq, y@ref)\n    y = 5\n    inspect(slot@ref)\n"));
        Assert.Equal(Rows(pastExclusive), Records(Guard($"uniq/{loanFree} during e", $"uniq/{loanFree}", "z")));
        Assert.Equal(Rows(byValue), Records(Guard(loanFree, loanFree, "z@move")));

        string Nest(string inner) => string.Concat(Enumerable.Repeat("Dictionary<i32, ", depth)) + inner + string.Concat(Enumerable.Repeat(">", depth));

        static string Guard(string part, string parameter, string read) => "func inspect<T>(value: ref/T) => ()\n" +
            $"func store(t: uniq/((ref/i32 during a, ref/(ref/i32 during b) during c, {part})), v: ref/i32 during a) => ()\n" +
            $"func f(z: {parameter}) -> ()\n    var value = 42\n    var x = 1\n    var r = x@ref\n    var d = (x@ref, r@ref, {read})\n" +
            "    store(d@uniq, value@ref)\n    value = 99\n    inspect(r@ref)\n    return\n";

        static IEnumerable<string> Records(string source)
        {
            var c = CompileDepthForm(source + "public func main() => ()\n");
            return DepthRecords(c, Analyze(c), "f").Where(static x => x.Line != "var d = (x@ref, r@ref, z@move)").Select(static x => $"{x.Line} {x.Code}");
        }
    }
}
