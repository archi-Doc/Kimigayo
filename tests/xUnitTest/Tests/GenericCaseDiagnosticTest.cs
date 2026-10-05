// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 8.10, 23.3.6.1, 23.3.6.4: a problem that only some Semantics cases of a generic definition find carries those cases as its
// `case` Reason fact, names them in its Note and relates the Semantics bindings it names; a problem every case finds shows no
// case; the cases of one body are bounded.
public sealed class GenericCaseDiagnosticTest
{
    private const string Main = "public func main() -> ()\n    var n: i32 = 1\n    f(n, 9)\n";

    private const string Conflict = "func f<s/T>(value: s/T, x: T) -> ()\n    s is owner or uniq\n    T is Copy\n    var v = value@move\n    let local = v\n    v@follow = x\n    _ = local@follow\n" + Main;

    [Fact]
    public void AProblemOfOneCaseNamesTheCaseAndItsBinding()
    {
        var error = Assert.Single(DiagnosticCorpus.Check(Conflict).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.ComparisonLoanConflict_Kd), DiagnosticSeverity.Error, DiagnosticCategory.Language), (error.Code, error.Severity, error.Category));
        Assert.Equal(Conflict.IndexOf("v@follow = x", StringComparison.Ordinal), error.Span!.Value.Start);
        var fact = Assert.Single(error.Reason!);
        Assert.Equal(("case", "s = uniq"), (fact.Name, fact.Value));
        Assert.Contains("Found under the Semantics case s = uniq (SPEC 8.10)", error.Note, StringComparison.Ordinal);
        // Related locations are ordered by role name (DIAGNOSTICS.md §4.4).
        Assert.Collection(
            error.Related!,
            x => Assert.Equal(("declaration", "s/T", "the Semantics binding s"), (x.Role, Text(Conflict, x.Span), x.Label)),
            x => Assert.Equal(("loan", "let local = v"), (x.Role, Text(Conflict, x.Span))));
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
        var related = Assert.Single(error.Related!);
        Assert.Equal("loan", related.Role);
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
            x => Assert.Equal(("declaration", "s/T"), (x.Role, Text(source, x.Span))),
            x => Assert.Equal("loan", x.Role));
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
        Assert.Contains("Found under the Semantics case s = uniq (SPEC 8.10)", console.Text, StringComparison.Ordinal);
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

    private static string Text(string source, SourceSpan? span)
        => span is { } at ? source.Substring(at.Start, at.Length) : string.Empty;
}
