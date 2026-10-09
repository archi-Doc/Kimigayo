// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 15.6.2, 15.6.5, 16.2.2: a borrow of a block-local root that escapes through the block's result, a closure capture or a
// longer-lived value is a Loan conflict at the destruction that ends the root while that value keeps its Loan. It is one record
// per destroyed root and holder, at the transfer or statement whose cleanup destroys the root, relating the value keeping the
// Loan (`loan`) and the Borrow or capture entry that created it (`borrow`). The join that the block's value reaches only after
// the destruction, later uses of the value and an exit that delivers another source's borrow add no record.
public class BlockEscapeLoanTest(ITestOutputHelper output)
{
    private const string Closure = "public func main() -> ()\n    let g = label block: do\n        let n = 5\n        exit to block func [n@ref] () => n@follow\n    Console.writeLine(\"\\(g())\")\n";

    private const string Plain = "public func main() -> ()\n    let r = label block: do\n        let n = 5\n        exit to block n@ref\n    Console.writeLine(\"\\(r@follow)\")\n";

    // SPEC 16.2.2: the delivery of the block's value is a use of its Loan, so a value never used afterwards still escapes.
    private const string NoUse = "public func main() -> ()\n    let r = label block: do\n        let n = 5\n        exit to block n@ref\n    Console.writeLine(\"x\")\n";

    private const string Two = "func show(c: bool)\n    let z = 1\n    let r = label block: do\n        let n = 5\n        if c\n            exit to block n@ref\n        exit to block z@ref\n" +
        "    Console.writeLine(\"\\(r@follow)\")\n\npublic func main() -> ()\n    show(true)\n";

    private const string Parameter = "func pick(c: bool, z: ref/i32) -> i32\n    let r = label block: do\n        let n = 5\n        if c\n            exit to block n@ref\n        exit to block z\n    return r@follow\n\n" +
        "public func main() -> ()\n    let z = 1\n    Console.writeLine(\"\\(pick(true, z@ref))\")\n";

    // The exit delivering z@ref comes first in operation order; its cleanup also destroys n, but the value it delivers keeps no
    // Loan of n.
    private const string OtherFirst = "func show(c: bool)\n    let z = 1\n    let r = label block: do\n        let n = 5\n        if c\n            exit to block z@ref\n        exit to block n@ref\n" +
        "    Console.writeLine(\"\\(r@follow)\")\n\npublic func main() -> ()\n    show(true)\n";

    private const string BothExits = "func show(c: bool)\n    let r = label block: do\n        let n = 5\n        if c\n            exit to block n@ref\n        exit to block n@ref\n" +
        "    Console.writeLine(\"\\(r@follow)\")\n\npublic func main() -> ()\n    show(true)\n";

    // SPEC 14.10.3: code after a transfer is checked under a type-checking continuation, so its escape is still reported.
    private const string Dead = "public func main() -> ()\n    let z = 1\n    let r = label block: do\n        exit to block z@ref\n        let n = 5\n        exit to block n@ref\n" +
        "    Console.writeLine(\"\\(r@follow)\")\n";

    private const string Temporary = "func make() -> i32 => 4\n\npublic func main() -> ()\n    let r = make()@ref\n    Console.writeLine(\"\\(r@follow)\")\n";

    // SPEC 8.4.10.4: a loop over `C is Iterable` gives the body requirement results; the exit delivering z@ref still keeps no Loan
    // of n, so the escaping exit has the record (review k06; it was at the z@ref exit only).
    private const string GenericRequirement = "func show<C>(values: ref/C, c: bool) -> ()\n    C is Iterable\n    var count: i32 = 0\n    for a in values\n        count += 1\n" +
        "    let z = 1\n    let r = label block: do\n        let n = 5\n        if c\n            exit to block z@ref\n        exit to block n@ref\n    Console.writeLine(\"\\(r@follow) \\(count)\")\n\n" +
        "public func main() -> ()\n    let xs: Array<i32> = [1]\n    show(xs@ref, true)\n";

    // SPEC 8.9: `let local = value` over `s/T` is a conditional reborrow plan (review k01; it was at the z@ref exit only).
    private const string GenericReborrow = "func show<s/T>(value: s/T, c: bool) -> ()\n    s is ref or uniq\n    T is Copy\n    let local = value\n    let z = 1\n" +
        "    let r = label block: do\n        let n = 5\n        if c\n            exit to block z@ref\n        exit to block n@ref\n    Console.writeLine(\"\\(r@follow)\")\n\n" +
        "public func main() -> ()\n    let v = 3\n    show(v@ref, true)\n";

    // The same body with the escaping exit in code checked after the reachable z@ref exit (review k07; it was at the z@ref exit).
    private const string GenericDead = "func show<s/T>(value: s/T, c: bool) -> ()\n    s is ref or uniq\n    T is Copy\n    let local = value\n    let z = 1\n" +
        "    let r = label block: do\n        let n = 5\n        exit to block z@ref\n        exit to block n@ref\n    Console.writeLine(\"\\(r@follow)\")\n\n" +
        "public func main() -> ()\n    let v = 3\n    show(v@ref, true)\n";

    // A holder whose Type has an abstract part, in a body with requirement results: the carrying walk does not decide it, so every
    // destruction the Loan reaches keeps its record, the escaping one included.
    private const string AbstractHolder = "func show<C, T>(values: ref/C, other: ref/T, c: bool) -> ()\n    C is Iterable\n    T is Copy\n    var count: i32 = 0\n" +
        "    for a in values\n        count += 1\n    let r = label block: do\n        let n: T = other@follow\n        if c\n            exit to block other\n" +
        "        exit to block n@ref\n    let k: T = r@follow\n    Console.writeLine(\"\\(count)\")\n\n" +
        "public func main() -> ()\n    let xs: Array<i32> = [1]\n    let v = 3\n    show(xs@ref, v@ref, true)\n";

    // The Borrow is found through the one definition of a local, a nested block's value, a capture of a borrowed local, an element
    // borrow, a constructor's argument, a tuple payload, a match binding, a loop item and a temporary in a tuple (review a10, a01,
    // a03, a09, d04, d06, a15, f01, e07; each had only the `loan` role).
    private const string ReadLocal = "public func main() -> ()\n    let r = label b: do\n        let n = 5\n        let q = n@ref\n        exit to b q\n    Console.writeLine(\"\\(r@follow)\")\n";

    private const string NestedBlock = "public func main() -> ()\n    let r = label outer: do\n        let n = 5\n        let inner = label b: do\n            exit to b n@ref\n        exit to outer inner\n" +
        "    Console.writeLine(\"\\(r@follow)\")\n";

    private const string CapturedLocal = "public func main() -> ()\n    let g = label b: do\n        let n = 5\n        let q = n@ref\n        exit to b func [q] () => q@follow\n    Console.writeLine(\"\\(g())\")\n";

    private const string Element = "public func main() -> ()\n    let r = label b: do\n        let n: Array<i32> = [5, 6]\n        exit to b n[0]@ref\n    Console.writeLine(\"\\(r@follow)\")\n";

    private const string Constructor = "struct Holder {a}\n    public let item: ref/i32 during a\n\n    public init(item: ref/i32 during a) => self.item = item\n\n" +
        "public func main() -> ()\n    let h = label b: do\n        let n = 5\n        exit to b Holder.init(n@ref)\n    Console.writeLine(\"\\(h.item@follow)\")\n";

    private const string Tuple = "public func main() -> ()\n    let r = label b: do\n        let n = 5\n        exit to b (n@ref, n@ref)\n    Console.writeLine(\"\\(r.0@follow)\")\n";

    private const string MatchBinding = "public func main() -> ()\n    let r = label b: do\n        let n = 5\n        match n@ref\n            let x => exit to b x\n    Console.writeLine(\"\\(r@follow)\")\n";

    private const string LoopItem = "func show() -> i32\n    let r = label l: loop\n        let arr: Array<i32> = [1, 2]\n        for x in arr\n            exit to l x\n        exit to l arr[0]@ref\n" +
        "    return r@follow\n\npublic func main() -> ()\n    Console.writeLine(\"\\(show())\")\n";

    // SPEC 3.6.2, 13.5.5.2, 16.3.3: a temporary obj/rc/arc handle is a Loan root of the borrows lent through it, destroyed at its
    // full-expression boundary.
    private const string TemporaryHandle = "struct Item\n    public var id: i32 = 7\nfunc make() -> rc/Item => Kimi.Intrinsics.makeRc(Item.init())\n\npublic func main() -> ()\n";

    private const string TemporaryInTuple = "func make() -> i32 => 4\n\npublic func main() -> ()\n    let r = (make()@ref, 1)\n    Console.writeLine(\"\\(r.0@follow)\")\n";

    private const string Transfer = "; the transfer cleans up the scopes it leaves before it delivers its result (SPEC 16.2.2)";

    private const string Escaped = "`n` is destroyed here while a live value keeps the Loan of `n@ref`; the transfer cleans up the scopes it leaves before it delivers its result (SPEC 16.2.2)";

    private const string EscapedAdvice = "Declare `n` in a scope that outlives the value keeping its Loan, or keep an owned value instead of the borrow";

    private const string BorrowLabel = "borrow that created the loan";

    private const string LoanLabel = "value retaining the conflicting loan";

    // Top-level forms of the reproducers: the valid counterparts and the escapes, for the warm allocation checks.
    private const string ValidWorkload = "func pick(c: bool, z: ref/i32) -> i32\n    let r = label block: do\n        let n = 5\n        if c\n            exit to block z\n        exit to block z\n    return r@follow\n" +
        "let n = 5\nlet g = label block: do\n    let k = 1\n    exit to block func [n@ref] () => n@follow\nlet z = 1\nrequire g() + pick(true, z@ref) == 6 else => $abort(\"escape\")";

    private const string EscapingWorkload = "func show(c: bool)\n    let z = 1\n    let r = label block: do\n        let n = 5\n        if c\n            exit to block z@ref\n        exit to block n@ref\n" +
        "    Console.writeLine(\"\\(r@follow)\")\nlet g = label block: do\n    let n = 5\n    exit to block func [n@ref] () => n@follow\nConsole.writeLine(\"\\(g())\")\nshow(true)";

    // Escapes whose Borrow is found through a local's definition, a tuple payload and a constructor's argument, and one in a generic
    // body with requirement results.
    private const string FollowedWorkload = "struct Holder {a}\n    public let item: ref/i32 during a\n\n    public init(item: ref/i32 during a) => self.item = item\n" +
        "func show<C>(values: ref/C, c: bool) -> ()\n    C is Iterable\n    var count: i32 = 0\n    for a in values\n        count += 1\n    let z = 1\n" +
        "    let r = label block: do\n        let n = 5\n        if c\n            exit to block z@ref\n        exit to block n@ref\n    Console.writeLine(\"\\(r@follow) \\(count)\")\n" +
        "let r = label b: do\n    let n = 5\n    let q = n@ref\n    exit to b q\nlet t = label b: do\n    let m = 5\n    exit to b (m@ref, 1)\n" +
        "let h = label b: do\n    let k = 5\n    exit to b Holder.init(k@ref)\nConsole.writeLine(\"\\(r@follow) \\(t.0@follow) \\(h.item@follow)\")\n" +
        "let xs: Array<i32> = [1]\nshow(xs@ref, true)";

    public static TheoryData<string, string, string, string, string, string, string> Escapes => new()
    {
        // Name, source, primary text, the context of the related borrow and the borrow in it, the start of the related value, and the Note.
        { "Closure", Closure, "exit to block func [n@ref] () => n@follow", "[n@ref]", "n@ref", "do", Escaped },
        { "Plain", Plain, "exit to block n@ref", "exit to block n@ref", "n@ref", "do", Escaped },
        { "NoUse", NoUse, "exit to block n@ref", "exit to block n@ref", "n@ref", "do", Escaped },
        { "Two", Two, "exit to block n@ref", "exit to block n@ref", "n@ref", "do", Escaped },
        { "Parameter", Parameter, "exit to block n@ref", "exit to block n@ref", "n@ref", "do", Escaped },
        { "OtherFirst", OtherFirst, "exit to block n@ref", "exit to block n@ref", "n@ref", "do", Escaped },
        { "BothExits", BothExits, "exit to block n@ref", "exit to block n@ref", "n@ref", "do", Escaped },
        { "Dead", Dead, "exit to block n@ref", "exit to block n@ref", "n@ref", "do", Escaped },
        { "Temporary", Temporary, "let r = make()@ref", "make()@ref", "make()@ref", "let r", "The temporary `make()` is destroyed here while a live value keeps the Loan of `make()@ref`" },
        { "GenericRequirement", GenericRequirement, "exit to block n@ref", "exit to block n@ref", "n@ref", "do", Escaped },
        { "GenericReborrow", GenericReborrow, "exit to block n@ref", "exit to block n@ref", "n@ref", "do", Escaped },
        { "GenericDead", GenericDead, "exit to block n@ref", "exit to block n@ref", "n@ref", "do", Escaped },
        { "ReadLocal", ReadLocal, "exit to b q", "let q = n@ref", "n@ref", "do", Escaped },
        { "NestedBlock", NestedBlock, "exit to outer inner", "exit to b n@ref", "n@ref", "do", Escaped },
        { "CapturedLocal", CapturedLocal, "exit to b func [q] () => q@follow", "let q = n@ref", "n@ref", "do", Escaped },
        { "Element", Element, "exit to b n[0]@ref", "exit to b n[0]@ref", "n[0]@ref", "do", "`n` is destroyed here while a live value keeps the Loan of `n[0]@ref`" + Transfer },
        { "Constructor", Constructor, "exit to b Holder.init(n@ref)", "Holder.init(n@ref)", "n@ref", "do", Escaped },
        { "Tuple", Tuple, "exit to b (n@ref, n@ref)", "(n@ref", "n@ref", "do", Escaped },
        { "MatchBinding", MatchBinding, "exit to b x", "match n@ref", "n@ref", "do", Escaped },
        { "LoopItem", LoopItem, "exit to l x", "for x in arr", "arr", "loop", "`arr` is destroyed here while a live value keeps the Loan of `arr`" + Transfer },
        { "TemporaryInTuple", TemporaryInTuple, "let r = (make()@ref, 1)", "(make()@ref", "make()@ref", "let r", "The temporary `make()` is destroyed here while a live value keeps the Loan of `make()@ref`" },
        { "TemporaryHandleView", TemporaryHandle + "    let r = make()@objref\n    Console.writeLine(\"\\(r.id)\")\n", "let r = make()@objref", "make()@objref", "make()@objref", "let r", "The temporary `make()` is destroyed here while a live value keeps the Loan of `make()@objref`" },
        { "TemporaryHandleAnnotated", TemporaryHandle + "    let r: objref/Item = make()\n    Console.writeLine(\"\\(r.id)\")\n", "let r: objref/Item = make()", "let r: objref/Item = make()", "make()", "let r", "The temporary `make()` is destroyed here while a live value keeps the Loan of `make()`" },
        { "TemporaryHandlePayload", TemporaryHandle + "    let r = make()@follow@ref\n    Console.writeLine(\"\\(r.id)\")\n", "let r = make()@follow@ref", "let r = make()@follow@ref", "make()", "let r", "The temporary `make()` is destroyed here while a live value keeps the Loan of `make()`" },
        { "TemporaryHandleField", TemporaryHandle + "    let r = make().id@ref\n    Console.writeLine(\"\\(r@follow)\")\n", "let r = make().id@ref", "let r = make().id@ref", "make()", "let r", "The temporary `make()` is destroyed here while a live value keeps the Loan of `make()`" },
        { "TemporaryHandleSlot", TemporaryHandle + "    let r = make()@ref\n    let c = Kimi.Intrinsics.clone(r)\n", "let r = make()@ref", "make()@ref", "make()@ref", "let r", "The temporary `make()` is destroyed here while a live value keeps the Loan of `make()@ref`" },
    };

    [Theory]
    [MemberData(nameof(Escapes))]
    public void AnEscapedBorrowIsOneRecordAtTheDestruction(string name, string source, string at, string context, string borrow, string value, string note)
    {
        var record = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.ComparisonLoanConflict_Kd), DiagnosticSeverity.Error), (record.Code, record.Severity));
        Assert.Equal((source.IndexOf(at, StringComparison.Ordinal), at), (record.Span!.Value.Start, Text(source, record.Span)));
        Assert.Equal(note, record.Note);
        Assert.Equal(["borrow", "loan"], record.Related!.Select(static x => x.Role));
        var lending = record.Related![0];
        Assert.Equal((source.IndexOf(context, StringComparison.Ordinal) + context.IndexOf(borrow, StringComparison.Ordinal), borrow), (lending.Span!.Value.Start, Text(source, lending.Span)));
        Assert.Equal(BorrowLabel, lending.Label);
        Assert.StartsWith(value, Text(source, record.Related![1].Span), StringComparison.Ordinal);
        Assert.Equal(LoanLabel, record.Related![1].Label);
        output.WriteLine(name);
    }

    // SPEC 15.6.5: the destruction states the escape, so the Advice keeps the root alive or the value owned.
    [Fact]
    public void TheAdviceKeepsTheRootAlive()
    {
        var record = Assert.Single(DiagnosticCorpus.Check(Plain).Diagnostics);
        Assert.Equal(EscapedAdvice, record.Advice);
    }

    // DIAGNOSTICS.md rule 1: an independent Loan conflict in the same body stays visible beside the escape.
    [Fact]
    public void AnIndependentConflictStaysVisible()
    {
        var source = "public func main() -> ()\n    var k = 1\n    let q = k@ref\n    k = 2\n    Console.writeLine(\"\\(q@follow)\")\n" + Plain[(Plain.IndexOf('\n', StringComparison.Ordinal) + 1)..];
        var records = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Equal([nameof(DiagnosticCode.ComparisonLoanConflict_Kd), nameof(DiagnosticCode.ComparisonLoanConflict_Kd)], records.Select(static x => x.Code));
        Assert.Equal(["k = 2", "exit to block n@ref"], records.Select(x => Text(source, x.Span)));
        Assert.Null(records[0].Note);
    }

    // SPEC 14.10.3: a conflict that is not a destruction, inside code after a transfer, keeps its record.
    [Fact]
    public void AGenuineConflictInDeadCodeIsReported()
    {
        var source = "public func main() -> ()\n    let r = label block: do\n        exit to block 1\n        var m = 1\n        let q = m@ref\n        m = 2\n        exit to block q@follow\n    Console.writeLine(\"\\(r)\")\n";
        var record = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.ComparisonLoanConflict_Kd), "m = 2"), (record.Code, Text(source, record.Span)));
        Assert.Equal("loan", Assert.Single(record.Related!).Role);
    }

    // SPEC 15.6.5: where the carrying walk gives up, the per-root deduplication never drops the escaping exit; every destruction the
    // Loan reaches keeps a record relating only the value keeping it, while the join still adds none.
    [Fact]
    public void AnUndecidedHolderKeepsEveryDestruction()
    {
        var records = DiagnosticCorpus.Check(AbstractHolder).Diagnostics;
        Assert.All(records, static x => Assert.Equal(nameof(DiagnosticCode.ComparisonLoanConflict_Kd), x.Code));
        Assert.Equal(["exit to block other", "exit to block n@ref"], records.Select(x => Text(AbstractHolder, x.Span)));
        Assert.Equal(AbstractHolder.IndexOf("exit to block n@ref", StringComparison.Ordinal), records[1].Span!.Value.Start);
        Assert.All(records, x => Assert.Equal(("loan", "do"), (Assert.Single(x.Related!).Role, Text(AbstractHolder, x.Related![0].Span)[..2])));
        Assert.All(records, static x => Assert.Equal("`n` is destroyed here while a live value keeps its Loan" + Transfer, x.Note));
    }

    // SPEC 15.6.2, 16.2.2: borrows that never outlive their root's scope are valid and run.
    [Theory]
    [InlineData("CapturedOuter", "let n = 5\nlet g = label block: do\n    let k = 1\n    exit to block func [n@ref] () => n@follow\nConsole.writeLine(\"\\(g())\")", "5\n")]
    [InlineData("ParameterBothExits", "func pick(c: bool, z: ref/i32) -> i32\n    let r = label block: do\n        let n = 5\n        if c\n            exit to block z\n        exit to block z\n    return r@follow\nlet z = 1\nConsole.writeLine(\"\\(pick(true, z@ref))\")", "1\n")]
    [InlineData("Copied", "let r = label block: do\n    let n = 5\n    exit to block n\nConsole.writeLine(\"\\(r)\")", "5\n")]
    [InlineData("InnerValue", "func show(c: bool) -> i32\n    let z = 1\n    let r = label outer: do\n        let n = 5\n        let inner = label block: do\n            if c\n                exit to block n@ref\n            exit to block z@ref\n        let v = inner@follow\n        exit to outer v\n    return r\nConsole.writeLine(\"\\(show(true)) \\(show(false))\")", "5 1\n")]
    [InlineData("GenericBothExits", "func show<C>(values: ref/C, c: bool) -> ()\n    C is Iterable\n    var count: i32 = 0\n    for a in values\n        count += 1\n    let z = 1\n    let r = label block: do\n        let n = 5\n        if c\n            exit to block z@ref\n        exit to block z@ref\n    Console.writeLine(\"\\(r@follow) \\(count)\")\nlet xs: Array<i32> = [1]\nshow(xs@ref, true)", "1 1\n")]
    [InlineData("ReborrowBothExits", "func show<s/T>(value: s/T, c: bool) -> ()\n    s is ref or uniq\n    T is Copy\n    let local = value\n    let z = 1\n    let r = label block: do\n        let n = 5\n        if c\n            exit to block z@ref\n        exit to block z@ref\n    Console.writeLine(\"\\(r@follow)\")\nlet v = 3\nshow(v@ref, true)", "1\n")]
    [InlineData("AbstractBothExits", "func show<C, T>(values: ref/C, other: ref/T, c: bool) -> ()\n    C is Iterable\n    T is Copy\n    var count: i32 = 0\n    for a in values\n        count += 1\n    let r = label block: do\n        let n: T = other@follow\n        if c\n            exit to block other\n        exit to block other\n    let k: T = r@follow\n    Console.writeLine(\"\\(count)\")\nlet xs: Array<i32> = [1]\nlet v = 3\nshow(xs@ref, v@ref, true)", "1\n")]
    [InlineData("LocalCopied", "let r = label b: do\n    let n: Array<i32> = [5, 6]\n    let q = n[1]@ref\n    let t = (q, 1)\n    exit to b t.0@follow + n[0]\nConsole.writeLine(\"\\(r)\")", "11\n")]
    public void BorrowsWithinTheirRootsScopeRun(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("BlockEscapeLoan" + name, source, stdout);

    // SPEC 23.3.6.8: the console shows both related locations and the Note, the language server sends them in both placements,
    // and the JSON form keeps the borrow role, for a conversion and for a capture entry.
    [Theory]
    [InlineData(Plain)]
    [InlineData(Closure)]
    public void EveryOutputRelatesTheBorrow(string source)
    {
        var checkOutput = DiagnosticCorpus.Check(source);
        var record = Assert.Single(checkOutput.Diagnostics);
        var result = new DiagnosticResult(checkOutput.Diagnostics, checkOutput.Sources);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        Assert.Contains(nameof(DiagnosticCode.ComparisonLoanConflict_Kd), console.Text, StringComparison.Ordinal);
        Assert.Contains(": " + BorrowLabel, console.Text, StringComparison.Ordinal);
        Assert.Contains(": " + LoanLabel, console.Text, StringComparison.Ordinal);
        Assert.Contains("Note: " + Escaped, console.Text, StringComparison.Ordinal);
        Assert.Contains("Advice: " + EscapedAdvice, console.Text, StringComparison.Ordinal);

        var identity = SourceIdentity.FromPath(checkOutput.Sources[record.Source].Path);
        var plain = Assert.Single(WorkspaceCheck.Place(checkOutput, [identity], identity, false)[identity]);
        Assert.Equal((record.Code, record.Display!.Range), (plain.Code, plain.Range));
        Assert.Contains(BorrowLabel, plain.Message, StringComparison.Ordinal);
        Assert.Contains("note: " + Escaped, plain.Message, StringComparison.Ordinal);
        var related = Assert.Single(WorkspaceCheck.Place(checkOutput, [identity], identity, true)[identity]);
        Assert.Equal(record.Related!.Select(static x => x.Range), related.RelatedInformation!.Select(static x => (SourceRange?)x.Location.Range));

        var json = JsonSerializer.Serialize(result, DiagnosticJsonContext.Default.DiagnosticResult);
        Assert.Contains("\"role\":\"borrow\"", json, StringComparison.Ordinal);
        Assert.Contains(BorrowLabel, json, StringComparison.Ordinal);
        Assert.Equal(result, JsonSerializer.Deserialize(json, DiagnosticJsonContext.Default.DiagnosticResult));
    }

    // Warm ownership analysis allocates nothing, for valid bodies whose block values keep body-local Loans and for escapes,
    // whose destruction check, carrying-definition walk, Borrow search through definitions, constructions and calls, and record
    // reuse retained storage.
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(ValidWorkload, 0)]
    [InlineData(EscapingWorkload, 2)]
    [InlineData(FollowedWorkload, 4)]
    public void WarmEscapeChecksAllocateNothing(string workload, int escapes)
    {
        var c = MinimalEmissionTest.Analyze(workload);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(escapes == 0, c.Ownership.Result.IsVerified);
        Assert.Equal(escapes, c.Ownership.Issues.Count(static x => x.Failure == OwnershipFailure.ComparisonLoanConflict));
        Assert.Equal(escapes, c.Ownership.Issues.Count(static x => x.Borrow is not null));
        var verified = escapes == 0;
        Assert.Equal(0, AllocationMeasurement.Measure(() => verified = c.Ownership.Analyze().IsVerified));
        Assert.Equal(escapes == 0, verified);
        Assert.Equal(escapes, c.Ownership.Issues.Count(static x => x.Failure == OwnershipFailure.ComparisonLoanConflict));
    }

    private static string Text(string source, SourceSpan? span) => span is { } value ? source.Substring(value.Start, value.Length) : string.Empty;
}
