// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// A5 over what a value holds (TyCtxt.Holdings, G416 U2): the rows the entry table must keep, each form labelled from SPEC.
public sealed partial class MirBorrowsTest
{
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
}
