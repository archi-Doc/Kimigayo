// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>A5's activations through parent Loans (A3 5's err(activate); SPEC 15.6.3, 15.6.7, 23.1.2): overlap is judged through the Loans an
/// argument carries, so an activation is checked at the Places of the parent Loans its holder carries unique as at its own, on the same
/// check set: a Loan live into the call on what a reference the argument was Reborrowed through names is the activation's own record.</summary>
public sealed partial class MirBorrowsTest
{
    // An exclusive Dictionary element `uniq/i32 during a` and `found`, a shared Loan on the Dictionary through `index`'s result.
    private const string StoredElement = """
        func change(value: uniq/i32) => value@follow = 99
        func change2(value: uniq/i32, check: i32) => value@follow = check
        func run(value: uniq/i32 during a, other: uniq/i32 during a)
            var entries: Dictionary<i32, uniq/i32 during a> = [:]
            var others: Dictionary<i32, uniq/i32 during a> = [:]
            _ = entries.tryInsert(1, value@move)
            _ = others.tryInsert(1, other@move)
            let found = entries[1]@follow@ref

        """;

    // DictionaryStoredReferenceTest's forms (DECISIONS BU-27): each call's argument Reborrows through indexUniq's result, whose parent Loan
    // on `entries` meets `found`'s shared Loan at the activation, and indexUniq's receiver meets it at its own: two records per call.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AnActivationMeetsALoanThroughItsParentLoans(int calls)
    {
        string[] call = ["run: entries[1] CallActivationConflict_Kd", "run: change(entries[1]) CallActivationConflict_Kd"];
        var source = StoredElement + string.Concat(Enumerable.Repeat("    change(entries[1])\n", calls));
        Assert.Equal([.. Enumerable.Repeat(call, calls).SelectMany(x => x)], Problems(source + "    require found == 42 else => $abort(\"value\")\n"));
    }

    // The twins: `found` live into indexUniq's call only, read by the later argument as a value that carries no Loan, keeps the receiver's
    // record alone; `found` dead at the calls, or a Reborrow through another Dictionary, records nothing.
    [Theory]
    [InlineData("    change2(entries[1], found@follow)\n", "run: entries[1] CallActivationConflict_Kd")]
    [InlineData("    require found == 42 else => $abort(\"value\")\n    change(entries[1])\n", null)]
    [InlineData("    change(others[1])\n    require found == 42 else => $abort(\"value\")\n", null)]
    public void AnActivationMeetsOnlyTheParentLoansLiveIntoItsCall(string calls, string? record)
        => Assert.Equal(record is null ? [] : [record], Problems(StoredElement + calls));

    // V16: N calls whose argument Reborrows through a reference carrying N unique Loans, on N roots or on one root's Place (R4-M169's
    // review B1), compare each distinct parent Place once, so the work stays within the dense bound the calls' gathers already take.
    [Theory]
    [InlineData("    var z = 3\n    var p = z@uniq\n", "    var z{1} = {1}\n    if flag\n        p = z{1}@uniq\n")]
    [InlineData("    var z = 3\n    var p = z@uniq\n", "    if flag\n        p = z@uniq\n")]
    [InlineData("    var z = Pair.init()\n    var p = z.a@uniq\n", "    if flag\n        p = z.a@uniq\n")]
    public void AnActivationsParentLoansKeepTheWorkDense(string head, string each)
    {
        var (work, bytes) = this.Scaled(head, each, "    setM(p@follow@uniq)\n");
        Assert.True(Grows(work, 5), $"work {string.Join(", ", work)}");
        Assert.True(Grows(bytes, 5), $"bytes {string.Join(", ", bytes)}");
    }
}
