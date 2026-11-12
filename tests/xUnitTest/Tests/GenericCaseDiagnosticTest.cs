// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 8.15, 23.1.1, 23.1.2: a problem that only some Semantics cases of a generic definition find carries those cases as its
// `case` Reason fact, names them in its Note and relates the Semantics bindings it names; a problem every case finds shows no
// case; the cases of each definition are bounded before its parts are checked.
public sealed class GenericCaseDiagnosticTest
{
    private const string Main = "public func main() -> ()\n    var n: i32 = 1\n    f(n, 9)\n";

    private const string Conflict = "func f<s/T>(value: s/T, x: T) -> ()\n    s is owner or uniq\n    T is Copy\n    var v = value@move\n    let local = v\n    v@follow = x\n    _ = local@follow\n" + Main;

    private const string Borrow = "borrow that created the loan";

    private const string Retained = "later use of the value retaining the conflicting loan";

    [Fact]
    public void AProblemOfOneCaseNamesTheCaseAndItsBinding()
    {
        var error = Assert.Single(DiagnosticCorpus.Check(Conflict).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.ComparisonLoanConflict_Kd), DiagnosticSeverity.Error, DiagnosticCategory.Language), (error.Code, error.Severity, error.Category));
        Assert.Equal(Conflict.IndexOf("v@follow = x", StringComparison.Ordinal), error.Span!.Value.Start);
        var fact = Assert.Single(error.Reason!);
        Assert.Equal(("case", "s = uniq"), (fact.Name, fact.Value));
        Assert.Contains("Found under the Semantics case s = uniq (SPEC 8.15)", error.Note, StringComparison.Ordinal);
        // Related locations are ordered by role name (DIAGNOSTICS.md §4.4). Since the MIR decides acceptance (R6-check H5), the record is
        // A5's: it relates the borrow that created the Loan and the later use that retains it (H3b T49, class 3).
        Assert.Collection(
            error.Related!,
            x => Assert.Equal(
                ("borrow", Conflict.IndexOf("local = v", StringComparison.Ordinal) + 8, "v", Borrow),
                (x.Role, x.Span!.Value.Start, Text(Conflict, x.Span), x.Label)),
            x => Assert.Equal(("declaration", "s/T", "the Semantics binding s"), (x.Role, Text(Conflict, x.Span), x.Label)),
            x => Assert.Equal(("loan", "local@follow", Retained), (x.Role, Text(Conflict, x.Span), x.Label)));
    }

    // DIAGNOSTICS "Semantics cases": a found set that is no product lists its cases, binders by `, ` and cases by `; ` (Binding's
    // Semantics-case renderer, DECISIONS BU-82); each binding it names is related.
    [Fact]
    public void ACaseSetThatIsNoProductListsItsCases()
    {
        var source = "func f<t/U, s/T>(value: s/t/U) -> ()\n    s is owner or uniq\n    t is owner or uniq\n    U is Copy\n    let local = value\n    _ = value\n" +
            "    _ = local\npublic func main() => ()\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.ComparisonLoanConflict_Kd), error.Code);
        const string Cases = "t = owner, s = uniq; t = uniq, s = owner; t = uniq, s = uniq";
        Assert.Equal(("case", Cases), (Assert.Single(error.Reason!).Name, error.Reason![0].Value));
        Assert.Contains($"Found under the Semantics cases {Cases} (SPEC 8.15)", error.Note, StringComparison.Ordinal);
        Assert.Equal(["the Semantics binding t", "the Semantics binding s"], error.Related!.Where(static x => x.Role == "declaration").Select(static x => x.Label));
    }

    [Fact]
    public void AProblemOfEveryCaseShowsNoCase()
    {
        var source = "func f<s/T>(value: s/T, x: T) -> ()\n    s is owner or uniq\n    T is Copy\n    var m: i32 = 1\n    let r = m@uniq\n    m = 2\n    _ = r@follow\n    _ = value\n" + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.ComparisonLoanConflict_Kd), error.Code);
        Assert.Equal(source.IndexOf("m = 2", StringComparison.Ordinal), error.Span!.Value.Start);
        Assert.Null(error.Reason);
        Assert.DoesNotContain("Semantics case", error.Note ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal([("borrow", "m@uniq", Borrow), ("loan", "r@follow", Retained)], error.Related!.Select(x => (x.Role, Text(source, x.Span), x.Label)));
    }

    [Fact]
    public void TheCasesOfSeveralBindersAreFactored()
    {
        var source = "func f<s/T, t/U>(value: s/T, other: t/U, x: T) -> ()\n    s is owner or uniq\n    t is owner or uniq\n    T is Copy\n    U is Copy\n    var v = value@move\n    let local = v\n    v@follow = x\n    _ = local@follow\n    _ = other\n" +
            "public func main() -> ()\n    var n: i32 = 1\n    var m: i32 = 2\n    f(n, m, 9)\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.ComparisonLoanConflict_Kd), error.Code);
        var fact = Assert.Single(error.Reason!);
        Assert.Equal(("case", "s = uniq"), (fact.Name, fact.Value)); // t is found under both of its cases and is omitted.
        Assert.Collection(
            error.Related!,
            x => Assert.Equal(
                ("borrow", source.IndexOf("local = v", StringComparison.Ordinal) + 8, "v", Borrow),
                (x.Role, x.Span!.Value.Start, Text(source, x.Span), x.Label)),
            x => Assert.Equal(("declaration", "s/T", "the Semantics binding s"), (x.Role, Text(source, x.Span), x.Label)),
            x => Assert.Equal(("loan", "local@follow", Retained), (x.Role, Text(source, x.Span), x.Label)));
    }

    [Fact]
    public void ATransferRequiredInOneCaseKeepsItsTargetAndRepair()
    {
        var source = "func f<s/T>(value: s/T) -> ()\n    s is owner or uniq\n    let local = value\n    _ = local@move\npublic func main() -> ()\n    var n: i32 = 1\n    f(n)\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.TransferRequired_Kd), error.Code);
        Assert.Equal("value", Text(source, error.Span));
        Assert.Collection(
            error.Reason!,
            x => Assert.Equal(("target", "value"), (x.Name, x.Value)),
            x => Assert.Equal(("case", "s = owner"), (x.Name, x.Value)));
        Assert.Equal("value cannot be read as a Copy value", error.Label);
        Assert.Contains("s = owner", error.Note, StringComparison.Ordinal);
        Assert.Contains(error.Repairs!, x => x.Title.Contains("@move", StringComparison.Ordinal));
        var related = Assert.Single(error.Related!);
        Assert.Equal(("declaration", "s/T"), (related.Role, Text(source, related.Span)));
    }

    // The CLI shows the case in the Note and the binding among the related locations; the language server sends the record.
    [Fact]
    public void TheOutputsShowTheCase()
    {
        var output = DiagnosticCorpus.Check(Conflict);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(new(output.Diagnostics, output.Sources), string.Empty);
        Assert.Contains("Found under the Semantics case s = uniq (SPEC 8.15)", console.Text, StringComparison.Ordinal);
        Assert.Contains("declaration:", console.Text, StringComparison.Ordinal);
        Assert.Contains("the Semantics binding s", console.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCasesOfOneBodyAreBounded()
    {
        const string Within = "func f<a/A, b/B, c/C, d/D, e/E, g/G>(p: a/A, q: b/B, r: c/C, u: d/D, v: e/E, w: g/G) -> ()\n" +
            "    a is owner or uniq\n    b is owner or uniq\n    c is owner or uniq\n    d is owner or uniq\n    e is owner or uniq\n    g is owner or uniq\n    _ = p@move\n" +
            "public func main() -> ()\n    var n: i32 = 1\n    f(n, n@uniq, n, n, n, n)\n";
        var accepted = DiagnosticCorpus.Check(Within);
        Assert.DoesNotContain(accepted.Diagnostics, static x => x.Severity == DiagnosticSeverity.Error);

        const string Beyond = "func f<a/A, b/B, c/C, d/D, e/E, g/G, h/H>(p: a/A, q: b/B, r: c/C, u: d/D, v: e/E, w: g/G, y: h/H) -> ()\n" +
            "    a is owner or uniq\n    b is owner or uniq\n    c is owner or uniq\n    d is owner or uniq\n    e is owner or uniq\n    g is owner or uniq\n    h is owner or uniq\n    _ = p@move\n" +
            "public func main() -> ()\n    var n: i32 = 1\n    f(n, n@uniq, n, n, n, n, n)\n";
        var error = Assert.Single(DiagnosticCorpus.Check(Beyond).Diagnostics, static x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal((nameof(DiagnosticCode.OwnershipCaseLimit_Kd), DiagnosticCategory.Resource), (error.Code, error.Category));
        Assert.Contains("128 cases", error.Message, StringComparison.Ordinal);
        Assert.Contains("at most 64", error.Message, StringComparison.Ordinal);
        Assert.StartsWith("f<a/A", Text(Beyond, error.Span), StringComparison.Ordinal);
        Assert.Collection(
            error.Reason!,
            x => Assert.Equal(("cases", "128"), (x.Name, x.Value)),
            x => Assert.Equal(("limit", "64"), (x.Name, x.Value)));
    }

    // SPEC 8.15, 23.1.1: the bound counts every admitted Semantics of each binder, nine for an unconstrained one, of a definition with
    // parts: one unconstrained binder (9 cases) is within it, two (81) are beyond it, and a requirement without parts is never bounded.
    [Theory]
    [InlineData("func f<a/A>(p: a/A) -> ()\n    _ = p@move\npublic func main() -> ()\n    var n: i32 = 1\n    f(n)\n", null)]
    [InlineData("func f<a/A, b/B>(p: a/A, q: b/B) -> ()\n    _ = p@move\n    _ = q@move\npublic func main() -> ()\n    var n: i32 = 1\n    f(n, n)\n", "81")]
    [InlineData("contract C\n    func need<a/A, b/B, c/C, d/D, e/E, g/G, h/H>(self: ref/Self, p: a/A, q: b/B, r: c/C, u: d/D, v: e/E, w: g/G, y: h/H) -> ()\npublic func main() -> ()\n    ()\n", null)]
    public void TheBoundCountsTheCasesOfADefinitionWithParts(string source, string? cases)
    {
        var errors = DiagnosticCorpus.Check(source).Diagnostics.Where(static x => x.Severity == DiagnosticSeverity.Error).ToArray();
        if (cases is null)
        {
            Assert.Empty(errors);
            return;
        }

        var error = Assert.Single(errors);
        Assert.Equal((nameof(DiagnosticCode.OwnershipCaseLimit_Kd), "cases", cases), (error.Code, error.Reason![0].Name, error.Reason[0].Value));
    }

    // SPEC 8.15, 23.1.1, 23.1.2: beyond the bound the definition's parts are still bound, so an independent error in its body is
    // reported, and its signature stays valid: a caller's independent error and a call its signature rejects are reported too.
    [Fact]
    public void ADefinitionBeyondTheBoundKeepsItsIndependentRecords()
    {
        const string Source = "func f<a/A, b/B>(p: a/A, q: b/B) -> ()\n    let x: i32 = \"text\"\n    _ = p@move\n" +
            "public func main() -> ()\n    var n: i32 = 1\n    let y: bool = 3\n    f(n)\n";
        Assert.Equal(
            [(nameof(DiagnosticCode.OwnershipCaseLimit_Kd), Source.IndexOf("f<a/A", StringComparison.Ordinal)), (nameof(DiagnosticCode.TypeMismatch_Kd), Source.IndexOf("\"text\"", StringComparison.Ordinal)),
                (nameof(DiagnosticCode.TypeMismatch_Kd), Source.IndexOf("= 3", StringComparison.Ordinal) + 2), (nameof(DiagnosticCode.NoApplicableOverload_Kd), Source.IndexOf("f(n)", StringComparison.Ordinal))],
            DiagnosticCorpus.Check(Source).Diagnostics.Select(static x => (x.Code, x.Span!.Value.Start)));
    }

    // SPEC 8.15, 23.1.1: each definition with parts is bounded on its own: a seven-binder struct's synthesized constructor (located at the
    // struct), initialized Field, computed Property and method have one record each.
    [Fact]
    public void EachDefinitionOfAContainerBeyondTheBoundHasItsRecord()
    {
        const string Source = "struct S<a/A, b/B, c/C, d/D, e/E, g/G, h/H>\n    a is owner or uniq\n    b is owner or uniq\n    c is owner or uniq\n    d is owner or uniq\n" +
            "    e is owner or uniq\n    g is owner or uniq\n    h is owner or uniq\n    var count: i32 = 1 + 2\n    computed size: i32\n" +
            "        get(self: ref/Self) -> i32 => self.count + 1\n    func touch(self: ref/Self) -> i32 => self.count + 2\npublic func main() -> ()\n    ()\n";
        var errors = DiagnosticCorpus.Check(Source).Diagnostics;
        Assert.All(errors, static x => Assert.Equal((nameof(DiagnosticCode.OwnershipCaseLimit_Kd), "128"), (x.Code, x.Reason![0].Value)));
        Assert.Equal(
            [0, Source.IndexOf("var count", StringComparison.Ordinal), Source.IndexOf("computed size", StringComparison.Ordinal), Source.IndexOf("touch(", StringComparison.Ordinal)],
            errors.Select(static x => x.Span!.Value.Start));
    }

    // SPEC 8.3.4, 8.15.2, 23.1.2 (PLAN G750): a binder whose premises empty its set admits no case, so a Semantics requirement read from
    // it (a case adaptation's rows) is the emptying environment's Error and adds no record of its own: in a conformance block member whose
    // `when` premise empties D's set, in an emptied struct's member and in a function whose own clauses empty its set. Only the
    // environments' InvalidConstraint_Kd stay (BU-161: a use of an Invalid environment keeps its record). Before, the struct member's and
    // the function's `7@s` were verified in all nine cases (TypeMismatch_Kd from a case the premises exclude) and the struct's init in
    // some (UninitializedPlace_Kd).
    [Theory]
    [InlineData("    s is owner\n", "    Self is Copy when s is ref\n        public func put(x: s/i32) -> ()\n            _ = 7@s\n")]
    [InlineData("    s is owner\n", "    Self is Copy when s is ref\n        public func put(x: s/i32) -> ()\n            let y = x@move@s\n            _ = y\n")]
    [InlineData("    s is owner\n    s is ref\n", "    public func get(self: ref/Self, x: s/i32) -> ()\n        _ = 7@s\n")]
    [InlineData(null, "func one<s/T>(x: s/i32) -> ()\n    s is owner\n    s is ref\n    _ = 7@s\n")]
    public void ASetThatAdmitsNoCaseGivesItsRequirementsNoRecord(string? premises, string members)
    {
        var source = (premises is null ? string.Empty : "struct Cell<s/T>\n" + premises + "    public var v: s/T\n    public init(v: s/T) => self.v = v@move\n") +
            members + "public func main() => ()\n";
        Assert.Equal([nameof(DiagnosticCode.InvalidConstraint_Kd)], DiagnosticCorpus.Check(source).Diagnostics.Select(static x => x.Code).Distinct());
    }

    private static string Text(string source, SourceSpan? span)
        => span is { } at ? source.Substring(at.Start, at.Length) : string.Empty;
}
