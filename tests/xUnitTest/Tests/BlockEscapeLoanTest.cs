// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 15.6.2, 15.6.6, 16.2.2: a borrow of a block-local root that escapes through the block's result, a closure capture or a
// longer-lived value is a Loan conflict at the destruction that ends the root while that value keeps its Loan. It is one record
// per destroyed root and holder, at the MIR's storage end of the root: its declaration, or the temporary itself (H5, the flip),
// relating the use of the value keeping the Loan (`loan`: the label block, which delivers an exit's value (DECISIONS R5-M50), or
// a temporary's later use) and the Borrow or capture that created it (`borrow`). The join that the block's value reaches only
// after the destruction, later uses of the value and an exit that delivers another source's borrow add no record.
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

    // Both exits escape: the one record relates the later exit's Borrow, the MIR's Anchor (TheMirLoanIsUsedAtTheLabelBlock).
    private const string BothExits = "func show(c: bool)\n    let r = label block: do\n        let n = 5\n        if c\n            exit to block n@ref\n        exit to block n@ref\n" +
        "    Console.writeLine(\"\\(r@follow)\")\n\npublic func main() -> ()\n    show(true)\n";

    private const string Temporary = "func make() -> i32 => 4\n\npublic func main() -> ()\n    let r = make()@ref\n    Console.writeLine(\"\\(r@follow)\")\n";

    // SPEC 15.6.6: in a generic body with a loop over `C is Iterable`, the exit delivering z@ref keeps no Loan of n, so the escaping
    // exit has the record (review k06; it was at the z@ref exit only).
    private const string GenericRequirement = "func show<C>(values: ref/C, c: bool) -> ()\n    C is Iterable\n    var count: i32 = 0\n    for a in values\n        count += 1\n" +
        "    let z = 1\n    let r = label block: do\n        let n = 5\n        if c\n            exit to block z@ref\n        exit to block n@ref\n    Console.writeLine(\"\\(r@follow) \\(count)\")\n\n" +
        "public func main() -> ()\n    let xs: Array<i32> = [1]\n    show(xs@ref, true)\n";

    // SPEC 8.14: `let local = value` over `s/T` is a conditional reborrow plan (review k01; it was at the z@ref exit only).
    private const string GenericReborrow = "func show<s/T>(value: s/T, c: bool) -> ()\n    s is ref or uniq\n    T is Copy\n    let local = value\n    let z = 1\n" +
        "    let r = label block: do\n        let n = 5\n        if c\n            exit to block z@ref\n        exit to block n@ref\n    Console.writeLine(\"\\(r@follow)\")\n\n" +
        "public func main() -> ()\n    let v = 3\n    show(v@ref, true)\n";

    // SPEC 15.6.6: a holder whose Type has an abstract part is decided like any other, so the exit delivering `other` keeps no Loan of
    // n (the walk gave up on it while requirement calls had region dependencies, before the adoption of Abstract Calls Reach by Paths).
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

    private const string ReferenceSlot = "public func main() -> ()\n    let words: Array<string> = [\"a\"]\n    let r = label b: do\n        let q = words[0]@ref\n" +
        "        exit to b q@ref\n    Console.writeLine(r@follow)\n";

    // SPEC 3.6, 13.5.6, 16.3.3: a temporary obj/rc/arc handle is a Loan root of the borrows lent through it, destroyed at its
    // full-expression boundary.
    private const string TemporaryHandle = "struct Item\n    public var id: i32 = 7\nfunc make() -> rc/Item => Kimi.Intrinsics.makeRc(Item.init())\n\npublic func main() -> ()\n";

    private const string TemporaryInTuple = "func make() -> i32 => 4\n\npublic func main() -> ()\n    let r = (make()@ref, 1)\n    Console.writeLine(\"\\(r.0@follow)\")\n";

    // SPEC 3.6, 3.4.2: `@ref` or `@uniq` on a returned reference borrows the temporary slot holding it (`ref/(ref/T)`), which ends
    // at the full-expression boundary; a Reborrow of the referent (`@follow@ref`) does not depend on that slot.
    private const string FirstText = "func first(values: ref/Array<string>) -> ref/string during values => values[0]@ref\n";

    private const string SlotInt = "func slot(values: uniq/Array<i32>) -> uniq/i32 during values => values[0]@uniq\n";

    // SPEC 3.6, 8.15.1: a temporary of any Type ends at its full expression: an abstract one in every instance of its generic body, and a
    // Function value with the environment it owns.
    private const string TemporaryGeneric = "func inspect<T>(value: ref/T) => ()\nfunc keep<T>(make: () -> T) -> ()\n    let r = make()@ref\n    inspect(r)\n\n" +
        "public func main() -> ()\n    Console.writeLine(\"x\")\n";

    private const string TemporaryFunction = "func mk() -> (() -> i32)\n    let values: Array<i32> = [11, 22, 33]\n    return func [values@move] () -> i32 => values[1]\n" +
        "func call(f: ref/(() -> i32)) -> i32 => f()\n\npublic func main() -> ()\n    let r = mk()@ref\n    Console.writeLine(\"\\(call(r))\")\n";

    private const string Root = "let n = 5";

    private const string BorrowLabel = "borrow that created the loan";

    private const string LoanLabel = "later use of the value retaining the conflicting loan";

    private const string RetainedLabel = "value retaining the conflicting loan";

    // A local that keeps n's Loan through an Option payload written in the block, a definition the carrying walk does not decide; both
    // exits destroy n while it keeps the Loan.
    private const string UndecidedHolder = "func inspect<T>(value: ref/T) => ()\nfunc show(c: bool) -> ()\n    let z = 1\n    var o: Option<ref/i32> = .Some(z@ref)\n" +
        "    let k = label block: do\n        let n = 5\n        o = .Some(n@ref)\n        if c\n            exit to block 0\n        exit to block 1\n" +
        "    inspect(o@ref)\n    Console.writeLine(\"\\(k)\")\n\npublic func main() -> ()\n    show(true)\n";

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
        // Name, source, the text the primary span is found in and its text, the context of the related borrow and the borrow in it, and the
        // related use (its first occurrence; a label block's whole text).
        { "Closure", Closure, Root, Root, "exit to block func [n@ref] () => n@follow", "func [n@ref] () => n@follow", Block(Closure, "do\n") },
        { "Plain", Plain, Root, Root, "exit to block n@ref", "n@ref", Block(Plain, "do\n") },
        { "NoUse", NoUse, Root, Root, "exit to block n@ref", "n@ref", Block(NoUse, "do\n") },
        { "Two", Two, Root, Root, "exit to block n@ref", "n@ref", Block(Two, "do\n") },
        { "Parameter", Parameter, Root, Root, "exit to block n@ref", "n@ref", Block(Parameter, "do\n") },
        { "OtherFirst", OtherFirst, Root, Root, "exit to block n@ref", "n@ref", Block(OtherFirst, "do\n") },
        { "BothExits", BothExits, Root, Root, "exit to block n@ref\n    Console", "n@ref", Block(BothExits, "do\n") },
        { "Temporary", Temporary, "let r = make()@ref", "make()", "make()@ref", "make()@ref", "r@follow" },
        { "GenericRequirement", GenericRequirement, Root, Root, "exit to block n@ref", "n@ref", Block(GenericRequirement, "do\n") },
        { "GenericReborrow", GenericReborrow, Root, Root, "exit to block n@ref", "n@ref", Block(GenericReborrow, "do\n") },
        { "AbstractHolder", AbstractHolder, "let n: T = other@follow", "let n: T = other@follow", "exit to block n@ref", "n@ref", Block(AbstractHolder, "do\n") },
        { "ReadLocal", ReadLocal, Root, Root, "let q = n@ref", "n@ref", Block(ReadLocal, "do\n") },
        { "NestedBlock", NestedBlock, Root, Root, "exit to b n@ref", "n@ref", Block(NestedBlock, "do\n") },
        { "CapturedLocal", CapturedLocal, Root, Root, "let q = n@ref", "n@ref", Block(CapturedLocal, "do\n") },
        { "Element", Element, "let n: Array<i32> = [5, 6]", "let n: Array<i32> = [5, 6]", "exit to b n[0]@ref", "n[0]@ref", Block(Element, "do\n") },
        { "Constructor", Constructor, Root, Root, "Holder.init(n@ref)", "n@ref", Block(Constructor, "do\n") },
        { "Tuple", Tuple, Root, Root, "(n@ref", "n@ref", Block(Tuple, "do\n") },
        { "MatchBinding", MatchBinding, Root, Root, "match n@ref", "n@ref", Block(MatchBinding, "do\n") },
        { "LoopItem", LoopItem, "let arr: Array<i32> = [1, 2]", "let arr: Array<i32> = [1, 2]", "for x in arr", "arr", Block(LoopItem, "loop\n") },
        { "TemporaryInTuple", TemporaryInTuple, "let r = (make()@ref, 1)", "make()", "(make()@ref", "make()@ref", "r.0@follow" },
        { "TemporaryHandleView", TemporaryHandle + "    let r = make()@objref\n    Console.writeLine(\"\\(r.id)\")\n", "let r = make()@objref", "make()", "make()@objref", "make()@objref", "r.id" },
        { "TemporaryHandleAnnotated", TemporaryHandle + "    let r: objref/Item = make()\n    Console.writeLine(\"\\(r.id)\")\n", "let r: objref/Item = make()", "make()", "let r: objref/Item = make()", "make()", "r.id" },
        { "TemporaryHandlePayload", TemporaryHandle + "    let r = make()@follow@ref\n    Console.writeLine(\"\\(r.id)\")\n", "let r = make()@follow@ref", "make()", "let r = make()@follow@ref", "make()@follow@ref", "r.id" },
        { "TemporaryHandleField", TemporaryHandle + "    let r = make().id@ref\n    Console.writeLine(\"\\(r@follow)\")\n", "let r = make().id@ref", "make()", "let r = make().id@ref", "make().id@ref", "r@follow" },
        { "TemporaryHandleSlot", TemporaryHandle + "    let r = make()@ref\n    let c = Kimi.Intrinsics.clone(r)\n", "let r = make()@ref", "make()", "make()@ref", "make()@ref", "Kimi.Intrinsics.clone(r)" },
        { "ReferenceSlot", ReferenceSlot, "let q = words[0]@ref", "let q = words[0]@ref", "exit to b q@ref", "q@ref", Block(ReferenceSlot, "do\n") },
        { "TemporaryReferenceSlot", FirstText + "\npublic func main() -> ()\n    let words: Array<string> = [\"a\"]\n    let r = first(words@ref)@ref\n    Console.writeLine(r@follow)\n", "let r = first(words@ref)@ref", "first(words@ref)", "first(words@ref)@ref", "first(words@ref)@ref", "Console.writeLine(r@follow)" },
        { "TemporaryGeneric", TemporaryGeneric, "let r = make()@ref", "make()", "make()@ref", "make()@ref", "inspect(r)" },
        { "TemporaryFunction", TemporaryFunction, "let r = mk()@ref", "mk()", "mk()@ref", "mk()@ref", "call(r)" },
        { "TemporaryExclusiveReferenceSlot", SlotInt + "\npublic func main() -> ()\n    var xs: Array<i32> = [1]\n    let r = slot(xs@uniq)@uniq\n    r@follow@follow = 2\n", "let r = slot(xs@uniq)@uniq", "slot(xs@uniq)", "slot(xs@uniq)@uniq", "slot(xs@uniq)@uniq", "r@follow@follow = 2" },
    };

    [Theory]
    [MemberData(nameof(Escapes))]
    public void AnEscapedBorrowIsOneRecordAtTheDestruction(string name, string source, string statement, string at, string context, string borrow, string value)
    {
        var record = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.ComparisonLoanConflict_Kd), DiagnosticSeverity.Error), (record.Code, record.Severity));
        Assert.Equal(Locate(source, statement, at), Located(source, record.Span));
        Assert.Null(record.Note);
        Assert.Equal(["borrow", "loan"], record.Related!.Select(static x => x.Role));
        var lending = record.Related![0];
        Assert.Equal(Locate(source, context, borrow), Located(source, lending.Span));
        Assert.Equal(BorrowLabel, lending.Label);
        Assert.Equal(Locate(source, value, value), Located(source, record.Related![1].Span));
        Assert.Equal(LoanLabel, record.Related![1].Label);
        output.WriteLine(name);
    }

    // SPEC 16.2.2 on MIR (DECISIONS R5-M50): a transfer's secured result is delivered after the Scope Exit of the scopes it leaves, so a
    // block value borrowing a departing local conflicts with that local's storage end even where nothing reads the value later; a
    // Copied result does not.
    [Theory]
    [InlineData(NoUse, true)]
    [InlineData(Plain, true)]
    [InlineData("public func main() -> ()\n    let r = label block: do\n        let n = 5\n        exit to block n\n    Console.writeLine(\"x\")\n", false)]
    public void TheMirDeliversAfterTheScopeExit(string source, bool conflict)
    {
        var program = MirBuildTest.Compile(source).BuildMir();
        Assert.Empty(program.Findings);
        Assert.Equal(conflict, program.Problems.All.ToArray().Any(static x => x.Code == DiagnosticCode.ComparisonLoanConflict_Kd));
    }

    // SPEC 16.2.2, 15.6.6 on MIR (DECISIONS R5-M50, DIAGNOSTICS A6 `Use`): the delivery after the Scope Exit is the target's, whichever
    // exit builds it first, so the value keeping the Loan is used at the label block, never at an exit delivering another source; the
    // Anchor is the escaping exit's Borrow (the later one where both escape).
    // Over an abstract Iterable (GenericRequirement, AbstractHolder) A5 also errs twice at the valid `for` loop until PLAN G416: the lent
    // item's composed erased destruction stores next()'s Loan into the iterator, a self-row (G264 (S) S2 lands without GXS, the owner's
    // O-9); those two are H5's (e) rows, pinned here so the test fails when G416 removes them.
    [Theory]
    [InlineData(OtherFirst, 0)]
    [InlineData(BothExits, 0)]
    [InlineData(GenericRequirement, 2)]
    [InlineData(GenericReborrow, 0)]
    [InlineData(AbstractHolder, 2)]
    public void TheMirLoanIsUsedAtTheLabelBlock(string source, int g416Rows)
    {
        var c = MirBuildTest.Compile(source);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var conflicts = Enumerable.Range(0, program.Problems.All.Length).Where(x => program.Problems.All[x].Code == DiagnosticCode.ComparisonLoanConflict_Kd).ToArray();
        var lines = conflicts.Select(x => source.AsSpan(0, c.SyntaxOf(program.Problems.All[x].Subject).Span.Start).Count('\n')).ToArray();
        var loop = Array.FindIndex(source.Split('\n'), static x => x.StartsWith("    for ", StringComparison.Ordinal));
        Assert.Equal(g416Rows, lines.Count(x => x == loop));
        var index = conflicts[Assert.Single(Enumerable.Range(0, lines.Length), x => lines[x] != loop)];
        Assert.Equal(
            [("Anchor", source.LastIndexOf("n@ref", StringComparison.Ordinal)), ("Use", source.IndexOf("do\n", StringComparison.Ordinal))],
            program.Problems.Facts(index).ToArray().Select(x => ($"{x.Kind}", c.SyntaxOf(x.Site).Span.Start)));
    }

    // SPEC 15.6.6: the plain escape is one record, at the destruction.
    [Fact]
    public void ThePlainEscapeIsOneRecord()
    {
        var record = Assert.Single(DiagnosticCorpus.Check(Plain).Diagnostics);
    }

    // DIAGNOSTICS.md rule 1: an independent Loan conflict in the same body stays visible beside the escape.
    [Fact]
    public void AnIndependentConflictStaysVisible()
    {
        var source = "public func main() -> ()\n    var k = 1\n    let q = k@ref\n    k = 2\n    Console.writeLine(\"\\(q@follow)\")\n" + Plain[(Plain.IndexOf('\n', StringComparison.Ordinal) + 1)..];
        var records = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Equal([nameof(DiagnosticCode.ComparisonLoanConflict_Kd), nameof(DiagnosticCode.ComparisonLoanConflict_Kd)], records.Select(static x => x.Code));
        Assert.Equal(["k = 2", Root], records.Select(x => Text(source, x.Span)));
        Assert.Null(records[0].Note);
    }

    // SPEC 15.6.6: where the carrying walk gives up, the escaping exit is never dropped. Since the MIR decides acceptance (H5, DECISIONS
    // R5-M185) both exits' destructions of n are A5's one record at n's declaration (H3b T44: at each exit before; the second exit's own
    // record is the old analysis's, 1x by R5's F-c ruling), relating the Borrow and `o`, whose next use lies past the block, at its
    // declaration (DIAGNOSTICS A6's `Store` witness; the U-T2 amendment of DECISIONS R4-M57), without the destruction's Note (T49, until
    // A6). The same body over a plain reference, which the walk decides, has the same record.
    [Fact]
    public void AnUndecidedHoldersExitDestructionsAreOneRecord()
    {
        var decided = UndecidedHolder.Replace("var o: Option<ref/i32> = .Some(z@ref)", "var o = z@ref", StringComparison.Ordinal)
            .Replace("o = .Some(n@ref)", "o = n@ref", StringComparison.Ordinal);
        foreach (var (source, holder) in new[] { (UndecidedHolder, "var o: Option<ref/i32> = .Some(z@ref)"), (decided, "var o = z@ref") })
        {
            var record = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
            Assert.Equal((nameof(DiagnosticCode.ComparisonLoanConflict_Kd), Root, (string?)null), (record.Code, Text(source, record.Span), record.Note));
            Assert.Equal([("borrow", "n@ref", BorrowLabel), ("loan", holder, RetainedLabel)], record.Related!.Select(x => (x.Role, Text(source, x.Span), x.Label)));
        }
    }

    // SPEC 3.6, 8.15.1: an abstract receiver temporary ends with its statement as well, so the iterator it lends cannot outlive it.
    [Fact]
    public void AnAbstractReceiverTemporaryEndsWithItsStatement()
    {
        const string source = "func keep<C>(make: () -> C) -> ()\n    C is Iterable\n    var it = make().iterate()\n    _ = it.next()\n\npublic func main() -> ()\n    Console.writeLine(\"x\")\n";
        var record = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.ComparisonLoanConflict_Kd), "make()"), (record.Code, Text(source, record.Span)));
        Assert.Equal(Locate(source, "it.next()", "it"), Located(source, Assert.Single(record.Related!, static x => x.Role == "loan").Span));
        Assert.Null(record.Note);
    }

    // SPEC 3.6: the valid twins of TemporaryGeneric and TemporaryFunction: a borrow of an abstract or a Function temporary used within
    // its statement outlives no temporary.
    [Theory]
    [InlineData("func inspect<T>(value: ref/T) => ()\nfunc keep<T>(make: () -> T) -> ()\n    inspect(make()@ref)\n\npublic func main() -> ()\n    Console.writeLine(\"x\")\n")]
    [InlineData("func mk() -> (() -> i32)\n    let values: Array<i32> = [11, 22, 33]\n    return func [values@move] () -> i32 => values[1]\nfunc call(f: ref/(() -> i32)) -> i32 => f()\n\npublic func main() -> ()\n    Console.writeLine(\"\\(call(mk()@ref))\")\n")]
    public void ABorrowOfATemporaryWithinItsStatementIsValid(string source)
        => Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);

    // SPEC 15.6.2, 16.2.2: borrows that never outlive their root's scope are valid and run.
    [Theory]
    [InlineData("CapturedOuter", "let n = 5\nlet g = label block: do\n    let k = 1\n    exit to block func [n@ref] () => n@follow\nConsole.writeLine(\"\\(g())\")", "5\n")]
    [InlineData("ParameterBothExits", "func pick(c: bool, z: ref/i32) -> i32\n    let r = label block: do\n        let n = 5\n        if c\n            exit to block z\n        exit to block z\n    return r@follow\nlet z = 1\nConsole.writeLine(\"\\(pick(true, z@ref))\")", "1\n")]
    [InlineData("Copied", "let r = label block: do\n    let n = 5\n    exit to block n\nConsole.writeLine(\"\\(r)\")", "5\n")]
    [InlineData("InnerValue", "func show(c: bool) -> i32\n    let z = 1\n    let r = label outer: do\n        let n = 5\n        let inner = label block: do\n            if c\n                exit to block n@ref\n            exit to block z@ref\n        let v = inner@follow\n        exit to outer v\n    return r\nConsole.writeLine(\"\\(show(true)) \\(show(false))\")", "5 1\n")]
    [InlineData("GenericBothExits", "func show<C>(values: ref/C, c: bool) -> ()\n    C is Iterable\n    var count: i32 = 0\n    for a in values\n        count += 1\n    let z = 1\n    let r = label block: do\n        let n = 5\n        if c\n            exit to block z@ref\n        exit to block z@ref\n    Console.writeLine(\"\\(r@follow) \\(count)\")\nlet xs: Array<i32> = [1]\nshow(xs@ref, true)", "1 1\n")]
    [InlineData("ReborrowBothExits", "func show<s/T>(value: s/T, c: bool) -> ()\n    s is ref or uniq\n    T is Copy\n    let local = value\n    let z = 1\n    let r = label block: do\n        let n = 5\n        if c\n            exit to block z@ref\n        exit to block z@ref\n    Console.writeLine(\"\\(r@follow)\")\nlet v = 3\nshow(v@ref, true)", "1\n")]
    [InlineData("AbstractBothExits", "func show<C, T>(values: ref/C, other: ref/T, c: bool) -> ()\n    C is Iterable\n    T is Copy\n    var count: i32 = 0\n    for a in values\n        count += 1\n    let r = label block: do\n        let n: T = other@follow\n        if c\n            exit to block other\n        exit to block other\n    let k: T = r@follow\n    Console.writeLine(\"\\(count)\")\nlet xs: Array<i32> = [1]\nlet v = 3\nshow(xs@ref, v@ref, true)", "1\n")]
    [InlineData("TemporaryReferenceReborrow", FirstText + SlotInt + "let words: Array<string> = [\"a\"]\nlet r = first(words@ref)@follow@ref\nConsole.writeLine(r)\nConsole.writeLine(first(words@ref)@ref@follow)\nvar xs: Array<i32> = [1]\nlet s = slot(xs@uniq)@follow@uniq\ns@follow = 2\nslot(xs@uniq)@follow += 1\nConsole.writeLine(\"\\(xs[0])\")", "a\na\n3\n")]
    [InlineData("LocalCopied", "let r = label b: do\n    let n: Array<i32> = [5, 6]\n    let q = n[1]@ref\n    let t = (q, 1)\n    exit to b t.0@follow + n[0]\nConsole.writeLine(\"\\(r)\")", "11\n")]
    public void BorrowsWithinTheirRootsScopeRun(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("BlockEscapeLoan" + name, source, stdout);

    // SPEC 23.3: the console shows both related locations, the language server sends them in both placements, and the JSON form
    // keeps the borrow role, for a conversion and for a closure's capture. The MIR's record has no Note (H5).
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
        Assert.DoesNotContain("Note: ", console.Text, StringComparison.Ordinal);

        var identity = SourceIdentity.FromPath(checkOutput.Sources[record.Source].Path);
        var plain = Assert.Single(WorkspaceCheck.Place(checkOutput, [identity], identity, false)[identity]);
        Assert.Equal((record.Code, record.Display!.Range), (plain.Code, plain.Range));
        Assert.Contains(BorrowLabel, plain.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("note: ", plain.Message, StringComparison.Ordinal);
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Equal(escapes == 0, c.Analysis.IsVerified);
        Assert.Equal(escapes, c.Problems.Count(static x => x.Code.IsLoanConflict()));
        Assert.Equal(escapes, c.AnalyzeLegacyOwnership().Issues.Count(static x => x.Borrow is not null));
        var verified = escapes == 0;
        Assert.Equal(0, AllocationMeasurement.Measure(() => verified = c.Analyze().IsVerified));
        Assert.Equal(escapes == 0, verified);
        Assert.Equal(escapes, c.Problems.Count(static x => x.Code.IsLoanConflict()));
    }

    private static string Text(string source, SourceSpan? span) => span is { } value ? source.Substring(value.Start, value.Length) : string.Empty;

    // The text of the block that `head` opens (its first occurrence): through the last following line indented deeper than head's line.
    private static string Block(string source, string head)
    {
        var start = source.IndexOf(head, StringComparison.Ordinal);
        var depth = source.AsSpan(source.LastIndexOf('\n', start) + 1).IndexOfAnyExcept(' ');
        var end = source.IndexOf('\n', start);
        while (source.AsSpan(end + 1).IndexOfAnyExcept(' ') > depth)
        {
            end = source.IndexOf('\n', end + 1);
        }

        return source[start..end];
    }

    // The start and text of `inner` in the first occurrence of `outer`.
    private static (int Start, string Text) Locate(string source, string outer, string inner)
        => (source.IndexOf(outer, StringComparison.Ordinal) + outer.IndexOf(inner, StringComparison.Ordinal), inner);

    private static (int Start, string Text) Located(string source, SourceSpan? span) => (span!.Value.Start, Text(source, span));
}
