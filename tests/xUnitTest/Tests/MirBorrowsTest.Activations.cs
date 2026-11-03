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

    // V16: N calls whose argument Reborrows through a reference carrying N unique Loans, on N roots, on one root's Place, on N Fields of one
    // root, and on N Fields of a root whose N other Fields carry live shared Loans (R4-M169's review B1): an activation scans each parent
    // root's candidates once and meets the shared ones through the parents' prefix trie, so the work stays within the dense bound the
    // calls' gathers already take.
    [Theory]
    [InlineData("func shape(flag: bool)\n    var z = 3\n    var p = z@uniq\n«N:    var z{1} = {1}\n    if flag\n        p = z{1}@uniq\n»" +
        "«N:    setM(p@follow@uniq)\n»")]
    [InlineData("func shape(flag: bool)\n    var z = 3\n    var p = z@uniq\n«N:    if flag\n        p = z@uniq\n»«N:    setM(p@follow@uniq)\n»")]
    [InlineData("func shape(flag: bool)\n    var z = Pair.init()\n    var p = z.a@uniq\n«N:    if flag\n        p = z.a@uniq\n»" +
        "«N:    setM(p@follow@uniq)\n»")]
    [InlineData("struct Big\n«N:    public var g{0}: i32 = 0\n»    public init() => ()\nfunc shape(flag: bool)\n    var z = Big.init()\n" +
        "    var p = z.g0@uniq\n«N:    if flag\n        p = z.g{0}@uniq\n»«N:    setM(p@follow@uniq)\n»")]
    [InlineData("struct Big\n«N:    public var g{0}: i32 = 0\n    public var h{0}: i32 = 0\n»    public init() => ()\nfunc shape(flag: bool)\n" +
        "    var z = Big.init()\n    var p = z.g0@uniq\n«N:    let s{0} = z.h{0}@ref\n    if flag\n        p = z.g{0}@uniq\n»" +
        "«N:    setM(p@follow@uniq)\n»«N:    inspect(s{0})\n»")]
    public void AnActivationsParentLoansKeepTheWorkDense(string template)
    {
        var runs = new[] { 32, 64, 128 }.Select(n => this.Measured(Prelude + Expand(template, n, 0))).ToArray();
        long[] work = [.. runs.Select(static x => x.Work)];
        long[] bytes = [.. runs.Select(static x => x.Bytes)];
        output.WriteLine($"N 32, 64, 128: work {string.Join(", ", work)}, bytes {string.Join(", ", bytes)}");
        Assert.True(Grows(work, 5), $"work {string.Join(", ", work)}");
        Assert.True(Grows(bytes, 5), $"bytes {string.Join(", ", bytes)}");
    }

    // V16 (R4-M169): the parents' trie and its scans on a program version A5 checked before allocate nothing: the Dictionary form's two
    // records (the trie's Field steps, a Deref parent and the ActivateThrough scan).
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmActivationThroughParentsAllocatesNothing()
    {
        var c = MirBuildTest.Compile(Prelude + StoredElement + "    change(entries[1])\n    require found == 42 else => $abort(\"value\")\n");
        var program = c.BuildMir();
        var stratum = new Stratum(c.Types);
        Assert.Equal(0, AllocationMeasurement.Measure(() => stratum.Run(program)));
        Assert.Equal(2, Problems(c, program, LoanCodes).Length);
    }

    // V16 (R4-M169's review B2): one activation whose parent's Place indexes a fixed array nested d deep inserts each step once, so the work
    // is linear in d (8 / 16 / 32), whatever the index steps' ambiguity.
    [Fact]
    public void AnActivationsParentIndexStepsAreLinear()
    {
        const string Nested = "func shape(flag: bool)\n    var a: «N:[1 of »i32«N:]» = «N:[»0«N:]»\n    let i: isize = 0\n" +
            "    var p = a«N:[i]»@uniq\n    setM(p)\n";
        long[] work = [.. new[] { 8, 16, 32 }.Select(d => this.Measured(Prelude + Expand(Nested, d, 0)).Work)];
        output.WriteLine($"d 8, 16, 32: work {string.Join(", ", work)}");
        Assert.True(Grows(work, 5), $"work {string.Join(", ", work)}");
    }
}
