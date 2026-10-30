// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>The shared acceptance tests of PLAN G264 U0′ (WORKFLOW Q11; written from the problem statement before either candidate):
/// A5's records at Array <c>truncate</c> and <c>removeAll</c> calls whose elements' destruction stores through a raw layer (SPEC 4.9.6,
/// 15.6.4, 16.3.1). U0′ publishes the two rows and changes no A5 verdict: the referent guard (DECISIONS R4-M143) keeps W at every call
/// whose referent may observe a destruction until U2 (R4-M150), so every row here is a guard that a candidate must keep.</summary>
public sealed partial class MirBorrowsTest
{
    // The problem statement's prelude: a Writer whose drop stores its q through its raw slot p (SPEC 15.6.4's example), a maker of one
    // from an abstract Type, a function item over `ref/Writer`, and a raw-writing helper a Closure's captures reach (the removeAllWrite
    // form, PLAN G266's). No shape Binding reports, so the published records are the forms' own.
    private const string DestructionShapes = """
        func inspect<T>(value: ref/T) => ()
        func usep<T>(p: raw/T) => ()
        func consume<T>(x: T) => ()
        func alwaysW(v: ref/Writer) -> bool => true
        func stash(p: raw/(raw/u8), q: raw/u8) -> bool
            unsafe => *p = q
            return true
        struct Writer
            public var p: raw/(raw/u8)
            public var q: raw/u8
            public init(q: raw/u8)
                self.p = null
                self.q = q
            drop
                unsafe => *self.p = self.q
        contract MakeW
            func make(p: raw/(raw/u8), q: raw/u8) -> Self

        """;

    // The problem statement's forms (its probe p1-forms and p3-plain): a Writer whose drop stores its q, which addresses w, which carries
    // y's Loan, into slot; appended, then the Array's elements destroyed by the operation and the Array consumed; Bad writes `y` before
    // `usep(slot)`, Ok after. truncateFull keeps the element (the row names no count, R4's settlement); Item passes a function item; Cap a
    // Closure capturing `b = y@ref`, used after the call; Gen an abstract element made by `T is MakeW`. The Plain forms hold `Array<i32>`:
    // a borrow of y live across the call, a Closure capturing it, one capturing the Array itself and an element borrow live at the call.
    private const string Forms = """
        func removeAllDrop2Bad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            var a: Array<Writer> = []
            a.append(x@move)
            a.removeAll(matching: func (v) => true)
            consume(a@move)
            y = 5
            usep(slot)
        func removeAllDrop2Ok()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            var a: Array<Writer> = []
            a.append(x@move)
            a.removeAll(matching: func (v) => true)
            consume(a@move)
            usep(slot)
            y = 5
        func truncateDrop3Bad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            var a: Array<Writer> = []
            a.append(x@move)
            a.truncate(0)
            consume(a@move)
            y = 5
            usep(slot)
        func truncateDrop3Ok()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            var a: Array<Writer> = []
            a.append(x@move)
            a.truncate(0)
            consume(a@move)
            usep(slot)
            y = 5
        func truncateFullBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            var a: Array<Writer> = []
            a.append(x@move)
            a.truncate(1)
            consume(a@move)
            y = 5
            usep(slot)
        func truncateFullOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            var a: Array<Writer> = []
            a.append(x@move)
            a.truncate(1)
            consume(a@move)
            usep(slot)
            y = 5
        func clearDrop2Bad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            var a: Array<Writer> = []
            a.append(x@move)
            a.clear()
            consume(a@move)
            y = 5
            usep(slot)
        func clearDrop2Ok()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            var a: Array<Writer> = []
            a.append(x@move)
            a.clear()
            consume(a@move)
            usep(slot)
            y = 5
        func removeAllItemBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            var a: Array<Writer> = []
            a.append(x@move)
            a.removeAll(matching: alwaysW)
            consume(a@move)
            y = 5
            usep(slot)
        func removeAllItemOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            var a: Array<Writer> = []
            a.append(x@move)
            a.removeAll(matching: alwaysW)
            consume(a@move)
            usep(slot)
            y = 5
        func removeAllCapBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            var a: Array<Writer> = []
            a.append(x@move)
            let b = y@ref
            a.removeAll(matching: func [b] (v) => b@follow > 0)
            consume(a@move)
            usep(slot)
            y = 5
            inspect(b)
        func removeAllCapOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            var a: Array<Writer> = []
            a.append(x@move)
            let b = y@ref
            a.removeAll(matching: func [b] (v) => b@follow > 0)
            consume(a@move)
            usep(slot)
            inspect(b)
            y = 5
        func truncateGenBad<T>()
            T is MakeW
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var a: Array<T> = []
            a.append(T.make(p: slot@raw, q: w@raw@raw/u8))
            a.truncate(0)
            consume(a@move)
            y = 5
            usep(slot)
        func truncateGenOk<T>()
            T is MakeW
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var a: Array<T> = []
            a.append(T.make(p: slot@raw, q: w@raw@raw/u8))
            a.truncate(0)
            consume(a@move)
            usep(slot)
            y = 5
        func removeAllGenBad<T>()
            T is MakeW
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var a: Array<T> = []
            a.append(T.make(p: slot@raw, q: w@raw@raw/u8))
            a.removeAll(matching: func (v) => true)
            consume(a@move)
            y = 5
            usep(slot)
        func removeAllGenOk<T>()
            T is MakeW
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var a: Array<T> = []
            a.append(T.make(p: slot@raw, q: w@raw@raw/u8))
            a.removeAll(matching: func (v) => true)
            consume(a@move)
            usep(slot)
            y = 5
        func truncatePlainOk()
            var y: i32 = 1
            var a: Array<i32> = [1]
            let b = y@ref
            a.truncate(0)
            inspect(b)
            y = 5
        func removeAllPlainOk()
            var y: i32 = 1
            var a: Array<i32> = [1]
            let b = y@ref
            a.removeAll(matching: func (v) => true)
            inspect(b)
            y = 5
        func removeAllCapPlainBad()
            var y: i32 = 1
            var a: Array<i32> = [1]
            let b = y@ref
            a.removeAll(matching: func [b] (v) => b@follow > 0)
            y = 5
            inspect(b)
        func removeAllCapPlainOk()
            var y: i32 = 1
            var a: Array<i32> = [1]
            let b = y@ref
            a.removeAll(matching: func [b] (v) => b@follow > 0)
            inspect(b)
            y = 5
        func removeAllCapArrBad()
            var a: Array<i32> = [1]
            let v = a@ref
            a.removeAll(matching: func [v] (e) => v@follow.length > 0)
            inspect(v)
        func removeAllElemBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            var x = Writer.init(q: w@raw@raw/u8)
            x.p = slot@raw
            var a: Array<Writer> = []
            a.append(x@move)
            let e = a[0]@ref
            a.removeAll(matching: func (v) => true)
            inspect(e)
            usep(slot)
            y = 5

        """;

    // The G266 forms, kept apart: a Closure capturing `p = slot@raw` and `q`, which carries y's Loan, stores q through p when called.
    private const string WriteForms = """
        func removeAllWriteBad()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            let p = slot@raw
            let q = w@raw@raw/u8
            var a: Array<i32> = [1]
            a.removeAll(matching: func [p, q] (v) => stash(p, q))
            consume(a@move)
            y = 5
            usep(slot)
        func removeAllWriteOk()
            var y: i32 = 1
            var w: ref/i32 = y@ref
            var slot: raw/u8 = null
            let p = slot@raw
            let q = w@raw@raw/u8
            var a: Array<i32> = [1]
            a.removeAll(matching: func [p, q] (v) => stash(p, q))
            consume(a@move)
            usep(slot)
            y = 5

        """;

    // SPEC 4.9.6, 15.6.4, 16.3.1 (PLAN G264 U0′; DECISIONS R4-M143, R4-M147): at a `truncate` or `removeAll` call whose referent may
    // observe a destruction, A5's records stay today's until U2 composes the destruction's retention (R4-M150): each Bad form errs at
    // `y = 5` (SPEC's verdict: slot may retain q, which addresses w, which carries y's Loan), and each Ok twin errs there too, whichever
    // argument form, a Closure, a function item (Item) or a capturing Closure (Cap, whose Bad is b's Loan), and whatever the length
    // argument (truncateFull: the row names no count); `clear`'s twins are the comparison. Over an abstract `T` not proven Copy (Gen), the
    // call is the located Unsupported_Kd and Bad keeps its true `y = 5` (R4-M147's abstract-referent rule, as clearGen's).
    // Every Ok twin's `y = 5` (removeAllDrop2Ok, truncateDrop3Ok, truncateFullOk, clearDrop2Ok, removeAllItemOk, removeAllCapOk), once
    // R4-M143's known false errors, is a correct error under SPEC 15.6's whole-holder text (DECISIONS BU-67, R4-M143's note; the Array
    // holding x is consumed, as consumeDrop2Ok's x). Pinned, not SPEC's verdict: removeAllElemBad's second record, its MIR-only `y = 5`
    // beside the activation conflict, is C3's destruction guard keeping W, pinned here until G264 U2. Guards: U0′ changes no A5 verdict,
    // so each row holds today and must hold after.
    [Fact]
    public void ATruncateOrRemoveAllWhoseReferentMayObserveKeepsW()
    {
        var c = MirBuildTest.Compile(DestructionShapes + Forms);
        Assert.Equal(
            [
                "removeAllDrop2Bad: y = 5", "removeAllDrop2Ok: y = 5", "truncateDrop3Bad: y = 5", "truncateDrop3Ok: y = 5",
                "truncateFullBad: y = 5", "truncateFullOk: y = 5", "clearDrop2Bad: y = 5", "clearDrop2Ok: y = 5",
                "removeAllItemBad: y = 5", "removeAllItemOk: y = 5", "removeAllCapBad: y = 5", "removeAllCapOk: y = 5",
                "truncateGenBad: a.truncate(0) Unsupported_Kd", "truncateGenBad: y = 5", "truncateGenOk: a.truncate(0) Unsupported_Kd",
                "removeAllGenBad: a.removeAll(matching: func (v) => true) Unsupported_Kd", "removeAllGenBad: y = 5",
                "removeAllGenOk: a.removeAll(matching: func (v) => true) Unsupported_Kd",
                "removeAllCapPlainBad: y = 5", "removeAllCapArrBad: a.removeAll(matching: func [v](e) => v@follow.length > 0) CallActivationConflict_Kd",
                "removeAllElemBad: a.removeAll(matching: func (v) => true) CallActivationConflict_Kd", "removeAllElemBad: y = 5",
            ],
            Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]));
    }

    // SPEC 15.6.4, 15.6.7, 4.9.6 (PLAN G264 U0′, G160): the records each form publishes, the same on every pipeline (DECISIONS R5-M156;
    // the default and `-Pipeline mir` runs cover both). A Writer form publishes none, since A5's records stay unpublished (G160; SPEC:
    // Bad errs at `y = 5`); the Plain forms are published as SPEC says: a borrow of y live across the call is valid (truncatePlainOk,
    // removeAllPlainOk), a Closure capturing it keeps b's Loan (removeAllCapPlainBad errs at `y = 5`, its twin valid; removeAllCapBad errs
    // there, by b's Loan), and a Closure capturing the Array or an element borrow live at the call is the activation conflict at the call
    // (removeAllCapArrBad, removeAllElemBad: callbacks cannot observe the Array). Guards: each row holds today and must hold after.
    [Theory]
    [InlineData("removeAllDrop2Bad", "")]
    [InlineData("removeAllDrop2Ok", "")]
    [InlineData("truncateDrop3Bad", "")]
    [InlineData("truncateDrop3Ok", "")]
    [InlineData("truncateFullBad", "")]
    [InlineData("truncateFullOk", "")]
    [InlineData("clearDrop2Bad", "")]
    [InlineData("clearDrop2Ok", "")]
    [InlineData("removeAllItemBad", "")]
    [InlineData("removeAllItemOk", "")]
    [InlineData("removeAllCapBad", "ComparisonLoanConflict_Kd y = 5")]
    [InlineData("removeAllCapOk", "")]
    [InlineData("truncateGenBad", "")]
    [InlineData("truncateGenOk", "")]
    [InlineData("removeAllGenBad", "")]
    [InlineData("removeAllGenOk", "")]
    [InlineData("truncatePlainOk", "")]
    [InlineData("removeAllPlainOk", "")]
    [InlineData("removeAllCapPlainBad", "ComparisonLoanConflict_Kd y = 5")]
    [InlineData("removeAllCapPlainOk", "")]
    [InlineData("removeAllCapArrBad", "CallActivationConflict_Kd a.removeAll(matching: func [v] (e) => v@follow.length > 0)")]
    [InlineData("removeAllElemBad", "CallActivationConflict_Kd a.removeAll(matching: func (v) => true)")]
    public void ATruncateOrRemoveAllCallPublishesItsRecordsOnBothPipelines(string form, string records)
    {
        var source = DestructionShapes + Form(Forms, form) + "public func main() => ()\n";
        Assert.Equal($"{form}: {records}", $"{form}: {Records(source)}");
    }

    // Kept apart (scored apart; PLAN G266's cause, not a row's): SPEC 15.6.4, storage reached through a Closure's captures may retain an
    // argument by the instantiated contract: `removeAll`'s callback stores q, which addresses w, which carries y's Loan, into slot through
    // its captured p, so removeAllWriteBad errs at `y = 5` and its Ok twin, which uses slot before, is valid. Today both are accepted: the
    // Closure is a by-value input whose raw layer the callee writes through (consumeDrop2Bad's class). The Bad row fails on dev by design.
    [Theory]
    [InlineData("removeAllWriteBad", "removeAllWriteBad: y = 5")]
    [InlineData("removeAllWriteOk", "")]
    public void ARemoveAllCallbackStoringThroughItsCapturesRetainsAsTheCallDoesPendingG266(string form, string expected)
    {
        var c = MirBuildTest.Compile(DestructionShapes + Form(WriteForms, form));
        Assert.Equal(expected.Length == 0 ? [] : [expected], Problems(c, Analyze(c), LoanCodes));
    }

    // The published Error records of a program: each code and the first line of its subject, in order.
    internal static string Records(string source)
        => string.Join("; ", ContextualCaptureTest.PublishedRecords(source).Where(static x => x.Severity == DiagnosticSeverity.Error)
            .Select(x => $"{x.Code} {source.Substring(x.Span.Start, x.Span.Length).Split('\n')[0].Trim()}"));

    // One form's function in a forms text: from its header to the next top-level declaration or the end.
    private static string Form(string forms, string name)
    {
        var start = forms.IndexOf($"func {name}(", StringComparison.Ordinal) is >= 0 and var found ? found : forms.IndexOf($"func {name}<", StringComparison.Ordinal);
        Assert.True(start >= 0, name);
        var end = forms.IndexOf("\nfunc ", start + 1, StringComparison.Ordinal);
        return end < 0 ? forms[start..] : forms[start..(end + 1)];
    }
}
