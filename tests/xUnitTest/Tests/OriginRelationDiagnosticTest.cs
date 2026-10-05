// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi;
using Kimi.Checking;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 15.6.1, 15.6.5, 23.3.6.5: a fit that holds structurally but fails in an Origin part is an Origin relation, reported at the
// value that supplies the longer end. A body-local Borrow reaching a fixed Origin is Refuted (UnsatisfiedOriginRelation_Kd, Language)
// and an underivable relation between fixed Origins is Unknown (UnprovenOriginRelation_Kd, Proof); both ends are Origin displays and
// neither is ever a Type mismatch.
public class OriginRelationDiagnosticTest
{
    private const string Main = "public func main() => ()\n";

    private const string Holder = "struct Holder {a}\n    public let item: ref/i32 during a\n\n    public init(item: ref/i32 during a) => self.item = item\n\n";

    private const string Bad = "func bad(x: ref/i32) -> ref/i32 during x\n    let local: i32 = 1\n    let r = local@ref\n    return r\n";

    private const string Refuted = "Return or store an owned value, or a borrow of an input, instead of a borrow of storage that ends with the body";

    [Theory]
    [InlineData(Bad, nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "r", "requires the borrow local@ref outlives x, which is false", "local@ref", Refuted)]
    [InlineData("func pick(a: ref/i32, b: ref/i32) -> ref/i32 during a\n    return b\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "b", "requires b outlives a, which is not proven", null, "If b always outlives a, add 'origin b outlives a', which changes the public contract, or bound the result by b")]
    [InlineData("func store(anchor: ref/i32, target: uniq/(ref/i32 during anchor))\n    let local: i32 = 5\n    target@follow = local@ref\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "local@ref", "requires the borrow local@ref outlives anchor, which is false", "local@ref", Refuted)]
    [InlineData("func f(x: ref/i32, y: ref/i32, c: bool) -> ref/i32 during x\n    return if c => x else => y\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "if c => x else => y", "requires y outlives x, which is not proven", null, "If y always outlives x, add 'origin y outlives x', which changes the public contract, or bound the result by y")]
    [InlineData(Holder + "func pick(p: ref/Holder, q: ref/i32) -> ref/(ref/i32 during q) during p\n    return p.item@ref\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "p.item@ref", "requires p.a outlives q, which is not proven", null, "If p.a always outlives q, add 'origin p.a outlives q', which changes the public contract, or bound the inner result by p.a")]
    [InlineData("func toStatic(x: ref/i32) -> ref/i32 during static\n    return x\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "x", "requires x outlives static, which is not proven", null, "Bind x to static where it is introduced, as in 'x: ref/i32 during static', or bound the result by x")]
    [InlineData("func f(a: ref/i32, b: ref/i32) -> i32\n    let r: ref/i32 during a = b\n    return r@follow\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "b", "requires b outlives a, which is not proven", null, "If b always outlives a, add 'origin b outlives a', which changes the public contract")]
    [InlineData("func g(a: ref/i32) -> i32\n    let local = 4\n    let r: ref/i32 during a = local@ref\n    return r@follow\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "local@ref", "requires the borrow local@ref outlives a, which is false", "local@ref", Refuted)]
    [InlineData("func g(b: ref/i32) -> ref/(ref/i32 during b) during b\n    let c = b\n    return c@ref\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "c@ref", "requires the borrow c@ref outlives b, which is false", "c@ref", Refuted)]
    [InlineData("func g(b: ref/i32) -> ref/(ref/i32 during b) during b\n    return b@ref\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "b@ref", "requires the borrow b@ref outlives b, which is false", "b@ref", Refuted)]
    [InlineData("func pick<length N, T>(values: ref/[N of T] during source) -> ref/T during sorce\n    return values[0]@ref/T\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "values[0]@ref/T", "requires source outlives sorce, which is not proven", null, "; sorce is introduced by this signature, so if source was meant, write it instead")]
    public void AnOriginOnlyFailureIsOneRelationRecord(string body, string code, string text, string label, string? origin, string advice)
    {
        var source = body + Main;
        var output = DiagnosticCorpus.Check(source);
        var error = Assert.Single(output.Diagnostics);
        Assert.Equal(code, error.Code);
        Assert.Equal(code == nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd) ? DiagnosticCategory.Language : DiagnosticCategory.Proof, error.Category);
        Assert.Equal(text, Text(source, error.Span));
        Assert.Equal(label, error.Label);
        Assert.Contains(advice, error.Advice, StringComparison.Ordinal);
        var reason = error.Reason!;
        Assert.Equal(["relation", "longer", "shorter", "source", "destination"], reason.Select(static x => x.Name));
        Assert.Equal(("outlives", "fit"), (reason[0].Value, reason[3].Value));
        Assert.All(reason[1..3], static x => Assert.Equal(DiagnosticValueKind.Origin, x.Kind));
        Assert.All(reason.Where(static x => x.Kind != DiagnosticValueKind.Origin), static x => Assert.Null(x.Origin));
        if (origin is null)
        {
            Assert.Equal("expression", reason[1].Origin);
            Assert.Null(error.Related);
        }
        else
        {
            Assert.Equal(("borrow", origin), (reason[1].Origin, reason[1].Value));
            var related = Assert.Single(error.Related!);
            Assert.Equal(("origin", origin), (related.Role, Text(source, related.Span)));
        }
    }

    // SPEC 15.6.1: a relation that a premise proves is no failure.
    [Theory]
    [InlineData("func f(x: ref/i32, y: ref/i32, c: bool) -> ref/i32 during x\n    origin y outlives x\n    return if c => x else => y\n")]
    [InlineData("func g(x: ref/i32, y: ref/i32) -> ref/i32 during x\n    origin y outlives x\n    return y\n")]
    [InlineData(Holder + "func pick(p: ref/Holder) -> ref/(ref/i32 during p) during p => p.item@ref\n")]
    [InlineData("func ok(x: ref/i32, y: ref/i32) -> ref/i32 during (x and y) => x\n")]
    [InlineData("func viaLocal(x: ref/i32) -> ref/i32 during x\n    let r = x\n    return r\n")]
    [InlineData("func f(b: ref/i32) -> i32\n    let rr = b@ref\n    return rr@follow@follow\n")]
    [InlineData("func f(p: ref/(ref/i32 during a) during b) -> ref/(ref/i32 during a) during b\n    return p@follow@ref\n")]
    public void AProvenRelationIsAccepted(string body)
    {
        var output = DiagnosticCorpus.Check(body + Main);
        Assert.True(output.Accepted, string.Join("\n", output.Diagnostics.Select(static x => $"{x.Code}: {x.Label}")));
    }

    // SPEC 15.6.1, 23.3.6.5: a Type occurrence whose nested Origin is not proven to outlive the outer one is a wellFormed record at
    // that occurrence, independent of the fit of the initializer, which is its own record.
    [Fact]
    public void AWellFormednessRelationIsRecordedAtItsTypeOccurrence()
    {
        var source = "func f(a: ref/i32, b: ref/i32, slot: ref/(ref/i32 during a) during b) -> i32\n    let r: ref/(ref/i32 during b) during a = slot\n    return 0\n" + Main;
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Equal(2, errors.Length);
        Assert.All(errors, static x => Assert.Equal(nameof(DiagnosticCode.UnprovenOriginRelation_Kd), x.Code));
        Assert.Equal(("ref/(ref/i32 during b) during a", "wellFormed"), (Text(source, errors[0].Span), errors[0].Reason![3].Value));
        Assert.Equal(("slot", "fit"), (Text(source, errors[1].Span), errors[1].Reason![3].Value));
        Assert.All(errors, static x => Assert.Equal("requires b outlives a, which is not proven", x.Label));
        Assert.All(errors, static x => Assert.DoesNotContain("bound the", x.Advice, StringComparison.Ordinal));
    }

    // SPEC 15.6.5: a chain between two body Origins is Proven by the spec and constrains region inference, but ownership analysis
    // keeps Loans only through the Origins written in a holder's Type, so accepting it would lose the longer Borrow's Loan (a use
    // after the Move of `w` was accepted). It is a located limit at the value, never a relation record and never accepted.
    [Theory]
    [InlineData("struct H {s}\n    public let item: ref/string during s\n    public init(item: ref/string during s) => self.item = item\nfunc eat(n: string) -> () => ()\nfunc peek(n: ref/string) -> () => ()\nfunc f() -> ()\n    let v = \"v\"\n    let h = H.init(v@ref)\n    let w = \"w\"\n    let r: ref/string during h.s = w@ref\n    eat(w@move)\n    peek(r)\n")]
    [InlineData("struct H {s}\n    public let item: ref/i32 during s\n    public init(item: ref/i32 during s) => self.item = item\nfunc f() -> ()\n    let v = 7\n    let h = H.init(v@ref)\n    var keep = h.item\n    if true\n        let w = 9\n        let r: ref/i32 during h.s = w@ref\n        keep = r\n    Console.writeLine(\"\\(keep@follow)\")\n")]
    [InlineData("func f() -> ()\n    var x: i32 = 4\n    var y: i32 = 6\n    let r: ref/i32 = x@ref\n    let s: ref/i32 during r = y@ref\n    y = 7\n    Console.writeLine(\"\\(s@follow) \\(r@follow)\")\n")]
    public void AChainBetweenBodyOriginsIsALocatedLimit(string body)
    {
        var source = body + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.UnsupportedOwnership_Kd), error.Code);
        Assert.EndsWith("@ref", Text(source, error.Span), StringComparison.Ordinal);
    }

    // DIAGNOSTICS.md rule 1: a failed relation leaves the Loan checks of the same body to proceed without it, so an independent Loan
    // conflict stays visible beside it.
    [Fact]
    public void AnIndependentLoanConflictStaysVisible()
    {
        var source = "func bad(x: ref/i32, y: ref/i32) -> i32\n    var n: i32 = 1\n    let s = n@ref\n    n = 2\n    Console.writeLine(\"\\(s@follow)\")\n    let r: ref/i32 during x = y\n    return r@follow\n" + Main;
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Equal([nameof(DiagnosticCode.ComparisonLoanConflict_Kd), nameof(DiagnosticCode.UnprovenOriginRelation_Kd)], errors.Select(static x => x.Code));
        Assert.Equal(("n = 2", "y"), (Text(source, errors[0].Span), Text(source, errors[1].Span)));
    }

    // SPEC 23.3.6.8: the console names the borrow and relates it, the language server sends the record with its related location,
    // and the JSON form carries the display kind beside each Origin value.
    [Fact]
    public void EveryOutputCarriesTheOriginDisplay()
    {
        var output = DiagnosticCorpus.Check(Bad + Main);
        var record = Assert.Single(output.Diagnostics);
        var result = new DiagnosticResult(output.Diagnostics, output.Sources);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("UnsatisfiedOriginRelation_Kd", console.Text, StringComparison.Ordinal);
        Assert.Contains("requires the borrow local@ref outlives x, which is false", console.Text, StringComparison.Ordinal);
        Assert.Contains(" = origin: ", console.Text, StringComparison.Ordinal);

        var identity = SourceIdentity.FromPath(output.Sources[record.Source].Path);
        var plain = Assert.Single(WorkspaceCheck.Place(output, [identity], identity, false)[identity]);
        Assert.Equal((record.Code, record.Display!.Range), (plain.Code, plain.Range));
        Assert.Null(plain.RelatedInformation);
        var related = Assert.Single(WorkspaceCheck.Place(output, [identity], identity, true)[identity]);
        Assert.Equal(record.Related![0].Range, Assert.Single(related.RelatedInformation!).Location.Range);

        var json = JsonSerializer.Serialize(result, DiagnosticJsonContext.Default.DiagnosticResult);
        Assert.Contains("{\"name\":\"longer\",\"kind\":\"Origin\",\"value\":\"local@ref\",\"elided\":false,\"origin\":\"borrow\"}", json, StringComparison.Ordinal);
        Assert.Contains("{\"name\":\"shorter\",\"kind\":\"Origin\",\"value\":\"x\",\"elided\":false,\"origin\":\"expression\"}", json, StringComparison.Ordinal);
        Assert.Contains("{\"name\":\"relation\",\"kind\":\"Text\",\"value\":\"outlives\",\"elided\":false}", json, StringComparison.Ordinal);
        Assert.Equal(result, JsonSerializer.Deserialize(json, DiagnosticJsonContext.Default.DiagnosticResult));
    }

    private static string Text(string source, SourceSpan? span) => span is { } value ? source.Substring(value.Start, value.Length) : string.Empty;
}
