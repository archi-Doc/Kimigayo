// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>A5 on values derived from Binding's errors (SPEC 23.1.2; docs/dev/DECISIONS.md R4-M109): an input Binding rejected after
/// selection is its consumer's derived value, which carries no Loan, so A5 reports no conflict derived from it, while the independent
/// records beside it stay.</summary>
public sealed partial class MirBorrowsTest
{
    // SPEC 23.1.2 (DECISIONS R4-M109): an argument Binding rejects after selection (an Origin relation it cannot prove) is an Opaque of the
    // reference the call takes, so a result kept past the statement carries no Loan on a temporary: no ComparisonLoanConflict_Kd beside
    // Binding's record, also where the rejected argument keeps a call that moves twice (A1's MovedPlace_Kd stays), while the true conflict of
    // a literal's temporary borrowed for static stays (fail-before: a conflict at each rejected argument).
    [Fact]
    public void ARejectedInputFormsNoLoan()
    {
        var c = MirBuildTest.Compile(Prelude + """
            struct Tally
                public var n: i32 = 0
                public init() => ()
            func keepStatic(x: ref/i32 during static) -> ref/i32 during static => x
            func chosen(c: ref/Tally, a: string, b: string) -> ref/Tally during c => c
            func field(c: ref/Tally)
                let r = keepStatic(c.n)
                inspect(r)
            func kept(c: ref/Tally, s: string)
                let r = keepStatic(chosen(c, s@move, s@move).n)
                inspect(r)
            func literal()
                let r = keepStatic(5)
                inspect(r)
            """);
        Assert.Equal(2, c.Binding.Issues.Count(static x => x.Code == DiagnosticCode.UnprovenOriginRelation_Kd));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        new Stratum(c.Types).Run(program);
        Assert.Equal(["kept: s MovedPlace_Kd", "literal: 5"], Problems(c, program, [.. LoanCodes, DiagnosticCode.MovedPlace_Kd]));
    }
}
