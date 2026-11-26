// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public class AssociatedBindingSetTest
{
    private const string Related = "struct Related {first, second}\n    origin first outlives second\n    public let left: ref/i32 during first\n    public let right: ref/i32 during second\n";
    private const string Family = "contract C\n    associate Item(a, b) is Related{view}\n        origin view.first == a\n        origin view.second == b\nstruct S\n    Self is C\n";

    // A result in the family's applied spelling and in the direct one it denotes (SPEC 8.5.1), each before its relation line.
    private const string Applied = "func f(x: ref/i32 during a, y: ref/i32 during b) -> S.Item(a, b)\n";
    private const string Direct = "func f(x: ref/i32 during a, y: ref/i32 during b) -> Related{s}\n    origin s.first == a\n    origin s.second == b\n";

    // Callers of f: one whose clause proves p outlives q, one without it, and one passing a single Origin.
    private const string Proving = "func g(p: ref/i32, q: ref/i32) -> ref/i32 during q\n    origin p outlives q\n    let t = f(p, q)\n    return t.right\n";
    private const string Unproving = "func g(p: ref/i32, q: ref/i32) -> ref/i32 during q\n    let t = f(p, q)\n    return t.right\n";
    private const string OneOrigin = "func g(p: ref/i32) -> ref/i32 during p\n    let t = f(p, p)\n    return t.right\n";
    private const string Main = "public func main() => ()\n";

    [Fact]
    public void BoundNominalFamilyExecutes()
    {
        const string source = "struct View {source}\n    public let value: ref/i32 during source\n    public init(value: ref/i32 during source) => self.value = value\ncontract C\n    associate Item(a) is View{view}\n        origin view.source == a\nstruct S\n    Self is C\nfunc wrap(value: ref/i32 during a) -> S.Item(a) => View.init(value)\nvar value = 7\nlet view = wrap(value@ref)\nrequire view.value == 7 else => $abort(\"view\")\nvalue = 9\nConsole.writeLine(\"bound\")";
        ScalarEmissionTest.EmitFixture("AssociatedBindingSet", source, "bound\n");
    }

    // SPEC 8.5.1, 8.5.4, 8.2.3, 15.6.1, 15.3.7 (PLAN G962 rulings 11 and 18): `S.Item(a, b)` denotes Related{a, b}, and a named definition
    // assumes its result Type well formed, the bound nominal domain `a outlives b` included, as it does for the direct `-> Related{s}`; so
    // every row is valid. The rows "" and "origin b outlives a" were labelled invalid before that reading: a corrected label, not a
    // weakening, since each call still proves the domain (ACallProvesTheBoundNominalDomain).
    [Theory]
    [InlineData("origin a outlives b")]
    [InlineData("")]
    [InlineData("origin b outlives a")]
    public void BindingSetsPublishTheBoundNominalDomain(string relation)
    {
        var source = Related + Family + Applied + "    " + relation + "\n    $abort(\"unused\")";
        ParseTestHelper.ParseSuccess(source);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    // SPEC 8.5.4 ("A use proves the substituted conditions"), 15.6.4 step 3, 8.2.3: a call proves the instantiated domain, in the applied
    // and the direct spelling alike. A caller returning t.right as `ref/i32 during q` pins b to q while a stays below p, so `f(p, q)` needs
    // p outlives q: its clause proves it, else the call has the record; with one Origin both relations hold.
    [Theory]
    [InlineData("", false, Proving, null)]
    [InlineData("", true, Proving, null)]
    [InlineData("", false, Unproving, "f(p, q)")]
    [InlineData("", true, Unproving, "f(p, q)")]
    [InlineData("origin b outlives a", false, OneOrigin, null)]
    [InlineData("origin b outlives a", true, OneOrigin, null)]
    [InlineData("origin b outlives a", false, Unproving, "f(p, q)")]
    [InlineData("origin b outlives a", true, Unproving, "f(p, q)")]
    public void ACallProvesTheBoundNominalDomain(string relation, bool direct, string caller, string? at)
    {
        var source = Related + Family + (direct ? Direct : Applied) + "    " + relation + "\n    $abort(\"unused\")\n" + caller + Main;
        (string, string)[] expected = at is null ? [] : [(nameof(DiagnosticCode.UnprovenConstraint_Kd), at)];
        Assert.Equal(expected, DiagnosticCorpus.Check(source).Diagnostics.Select(x => (x.Code, Text(source, x.Span))));
    }

    // SPEC 15.6.4 step 3, 15.6.5 (PLAN G990 D1): over Borrows, the call's instantiated domain is the call's row, which A5 judges. Two
    // Borrows' finite Origins prove `first outlives second` (valid, c4 and c4d). A Borrow's region tied through the domain to the fixed
    // `p` is Refuted, in both spellings and through a local that holds the result (invalid, c4p): A5's record at the call, naming the
    // clause, and the Loan conflict at `let v` that SPEC 15.6.5 derives from it (DECISIONS R7-M66 F-H1-1). Each was a fact-less
    // UnprovenConstraint_Kd at the call, judged from the premises alone.
    [Theory]
    [InlineData(false, "func g() -> i32\n    let v: i32 = 1\n    let w: i32 = 2\n    let r = f(v@ref, w@ref)\n    return 0\n", null)]
    [InlineData(true, "func g() -> i32\n    let v: i32 = 1\n    let w: i32 = 2\n    let r = f(v@ref, w@ref)\n    return 0\n", null)]
    [InlineData(false, "func g(p: ref/i32) -> ref/i32 during p\n    let v: i32 = 1\n    let r = f(v@ref, p)\n    return r.right\n", "f(v@ref, p)")]
    [InlineData(true, "func g(p: ref/i32) -> ref/i32 during p\n    let v: i32 = 1\n    let r = f(v@ref, p)\n    return r.right\n", "f(v@ref, p)")]
    [InlineData(false, "func g(p: ref/i32) -> ref/i32 during p\n    let v: i32 = 1\n    let r = f(v@ref, p)\n    let s = r@move\n    return s.right\n", "f(v@ref, p)")]
    public void ACallOverBorrowsProvesTheDomainByItsRow(bool direct, string caller, string? at)
    {
        var source = Related + Family + (direct ? Direct : Applied) + "    $abort(\"unused\")\n" + caller + Main;
        var records = DiagnosticCorpus.Check(source).Diagnostics;
        (string, string)[] expected = at is null ? [] : [(nameof(DiagnosticCode.ComparisonLoanConflict_Kd), "let v: i32 = 1"), (nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), at)];
        Assert.Equal(expected, records.Select(x => (x.Code, Text(source, x.Span))));
        var clause = source.IndexOf("origin first outlives second", StringComparison.Ordinal);
        Assert.All(records.Where(static x => x.Code == nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd)), x => Assert.Contains(clause, x.Related!.Select(static y => y.Span!.Value.Start)));
    }

    // The same reading at the signature's other positions (SPEC 8.2.3, 15.6.1): a method's, a Contract requirement's and a nested result,
    // and an input; an application in a body under the direct result's premise, and one under `-> i32`, where nothing gives the domain.
    [Theory]
    [InlineData("struct H\n    public func get(self: ref/Self, x: ref/i32 during a, y: ref/i32 during b) -> S.Item(a, b)\n        $abort(\"unused\")", true)]
    [InlineData("contract D\n    func make(self: ref/Self, x: ref/i32 during a, y: ref/i32 during b) -> S.Item(a, b)", true)]
    [InlineData("func f(x: ref/i32 during a, y: ref/i32 during b) -> Option<S.Item(a, b)>\n    return .None", true)]
    [InlineData("func g(x: ref/i32 during a, y: ref/i32 during b, z: S.Item(a, b)) -> i32\n    return 0", true)]
    [InlineData("func k(x: ref/i32 during a, y: ref/i32 during b) -> Related{s}\n    origin s.first == a\n    origin s.second == b\n    let o: Option<S.Item(a, b)> = .None\n    $abort(\"unused\")", true)]
    [InlineData("func k(x: ref/i32 during a, y: ref/i32 during b) -> i32\n    let o: Option<S.Item(a, b)> = .None\n    return 0", false)]
    public void ASignaturesApplicationAssumesItsDomain(string declaration, bool valid)
    {
        var source = Related + Family + declaration;
        ParseTestHelper.ParseSuccess(source);
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Equal(!valid, c.Binding.Issues.Any(static x => x.Code == DiagnosticCode.InvalidOriginBinding_Kd));
    }

    [Theory]
    [InlineData("origin a outlives b", true)]
    [InlineData("", false)]
    public void ImplementationCannotAddTheNominalDomain(string relation, bool valid)
    {
        var source = Related + "contract C\n    associate Item(a, b)\n        " + relation + "\nstruct S\n    Self is C\n    associate C.Item(a, b) is Related{view}\n        origin view.first == a\n        origin view.second == b";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    private static string Text(string source, SourceSpan? span) => span is { } value ? source.Substring(value.Start, value.Length) : string.Empty;
}
