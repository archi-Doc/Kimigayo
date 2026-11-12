// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// R7-C3a, SPEC 8.2.3: a declaration's identity premises form classes whose compared attributes each domain decides; a premise set that
// makes a Refuted identity, a cycle or an unproven attribute is the table's premise error, recorded once at the declaration whose
// premises form it, in every order, and never again at uses (no dependent record at its clauses or at a use reading the class).
public partial class TypeIdentityTest
{
    private const string Ref3 = "func f<T>(first: ref/i32 during a, mid: ref/i32 during b, second: ref/i32 during c, value: T) -> i32\n";
    private const string Pair3 = "func f<s/W, U, V>(x: s/W, first: ref/i32 during a, u: U, v: V) -> i32\n";

    [Theory]
    [InlineData(Ref3 + "    T is ref/i32 during a\n    T is ref/i32 during c\n    origin a outlives b\n    origin b outlives c\n    origin c outlives a\n", "")]
    [InlineData(Ref3 + "    T is ref/i32 during a\n    T is ref/i32 during c\n    origin a outlives b\n    origin b outlives c\n", "UnprovenConstraint_Kd@1")]
    [InlineData("func f<T>(value: T) -> i32\n    T is (ref/i32) -> i32\n    T is (ref/i32) -> i32\n", "")]
    [InlineData("func f<T>(value: T) -> i32\n    T is Option<(ref/i32) -> i32>\n    T is Option<(ref/i32) -> i32>\n", "")]
    [InlineData("func f<T, U>(value: T, u: U) -> i32\n    T is (ref/U, ref/i32) -> i32\n    T is (ref/i32, ref/U) -> i32\n", "")]
    [InlineData("func f<T>(value: T) -> i32\n    T is (ref/bool) -> i32\n    T is (ref/i32) -> i32\n", "InvalidConstraint_Kd@1")]
    [InlineData("func f<T>(value: T) -> i32\n    T is (ref/i32) -> i32\n    T is (ref/i32, ref/i32) -> i32\n", "InvalidConstraint_Kd@1")]
    [InlineData("func f<T, U>(value: T, u: U) -> i32\n    T is [4 of U]\n    T is [(2 + 2) of i32]\n", "")]
    [InlineData("func f<T, U>(value: T, u: U) -> i32\n    T is [4 of U]\n    T is [5 of i32]\n", "InvalidConstraint_Kd@1")]
    [InlineData(Pair3 + "    s is ref\n    V is s/U during a\n    V is ref/i32 during a\n", "")]
    [InlineData(Pair3 + "    s is ref or uniq\n    V is s/U during a\n    V is ref/i32 during a\n", "UnprovenConstraint_Kd@1")]
    [InlineData(Pair3 + "    s is owner\n    V is s/U\n    V is Option<i32>\n", "")]
    [InlineData(Pair3 + "    s is owner\n    V is s/U\n    V is ref/i32 during a\n", "")]
    [InlineData(Pair3 + "    s is owner or ref\n    V is s/U\n    V is Option<i32>\n", "MissingOriginBinding_Kd@3 UnprovenConstraint_Kd@1")]
    [InlineData("func f<s/W, U>(x: s/W, u: U) -> i32\n    s is owner\n    U is s/U\n", "")]
    [InlineData("func f<s/W, U>(x: s/W, u: U) -> i32\n    s is owner or ref\n    U is s/U\n", "MissingOriginBinding_Kd@3 UnprovenConstraint_Kd@1")]
    [InlineData("func f<s/W, U>(x: s/W, first: ref/i32 during a, u: U) -> i32\n    s is ref\n    U is s/U during a\n", "InvalidConstraint_Kd@1")]
    public void PremiseErrorsFollowTheIdentityTable(string source, string records)
        => Assert.Equal(records, PremiseRecords(source + "    return 0\n"));

    // Order-free: the classes are a function of the premise set, so each order gives the record at the declaration and no other.
    [Theory]
    [InlineData("    T is (ref/U) -> i32\n", "    T is (ref/i32) -> i32\n", "    V is ref/W during a\n", "    V is ref/i32 during b\n")]
    [InlineData("    T is ref/i32 during b\n", "    T is X.(K).A\n", "    X.(K).A is ref/U during a\n", "    X is K\n")]
    public void APremiseErrorIsOneRecordInEveryOrder(string a, string b, string c, string d)
    {
        string[] premises = [a, b, c, d];
        var header = premises.Any(static x => x.Contains("X.(K)", StringComparison.Ordinal)) ?
            "contract K\n    associate A\nfunc f<T, X, U>(first: ref/U during a, second: ref/i32 during b, value: T, x: X.(K).A) -> ref/U during a\n" :
            "func f<T, U, V, W>(first: ref/W during a, second: ref/i32 during b, value: T, u: U, v: V) -> i32\n";
        var body = header.StartsWith("contract", StringComparison.Ordinal) ? "    return x\n" : "    return 0\n";
        var line = header.Count(static x => x == '\n');
        foreach (var order in new[] { premises, [d, c, b, a], [b, d, a, c] })
        {
            Assert.Equal($"UnprovenConstraint_Kd@{line}", PremiseRecords(header + string.Concat(order) + body));
        }
    }

    // N4: a use reading an erroneous class rests on the declaration's record (two Contracts' clauses form the unproven meet).
    [Fact]
    public void AUseReadingAnErroneousClassRestsOnTheDeclarationsRecord()
        => Assert.Equal("UnprovenConstraint_Kd@9", PremiseRecords(
            "contract K1\n    associate A\n    associate C\n    Self.(K1).A is Option<Self.(K1).C>\n" +
            "contract K2\n    associate B\n    associate D\n    Self.(K2).B is Option<Self.(K2).D>\n" +
            "func f<T, U>(first: ref/U during a, second: ref/i32 during b, c: T.(K1).C, d: T.(K2).D) -> ref/U during a\n" +
            "    T is K1\n    T is K2\n    T.(K1).A is T.(K2).B\n    T.(K1).C is ref/U during a\n    T.(K2).D is ref/i32 during b\n    return c\n"));

    private static string PremiseRecords(string source)
        => string.Join(' ', DiagnosticCorpus.Check(source, "Library").Diagnostics
            .Select(x => $"{x.Code}@{source[..x.Span!.Value.Start].Count(static c => c == '\n') + 1}").Order(StringComparer.Ordinal));
}
