// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>A5's drop-liveness (DECISIONS R4-M153): a holder kept live only by a Drop past the destruction walk's bound is drop-live, the
/// check sets' second tier, so a conflict only it gives is <c>OwnershipDepthLimit_Kd</c> at that holder's declaration, never a Language
/// record, and it hides no real conflict.</summary>
public sealed partial class MirBorrowsTest
{
    // SPEC 23.1.1, IMPL 21.3.2 (R4-M153): the drop tier beside the other rules, at depth 40 with its twin at 10. A borrowed deep root whose
    // write conflicts only through its drop-liveness has one record, Locations' and the drop tier's at one subject (borrowed); a real use
    // after the write keeps the Language conflict beside the record at d (used); a Loan reached through the closure of d's Loan on x is
    // found from d (reached; at depth 10 d's Drop is shallow, so nothing); err(ended)'s γ kept only by a drop-live holder is the record at
    // that holder (guard), and kept by a real use it stays the Language problem at the candidate, beside Locations' records at the now
    // borrowed holder's two Drops past the bound, its old value's in the guard ({w}) and its own (guardUsed).
    [Theory]
    [InlineData("borrowed", 40, "{d} OwnershipDepthLimit_Kd")]
    [InlineData("used", 40, "value = 99 ComparisonLoanConflict_Kd|{d} OwnershipDepthLimit_Kd")]
    [InlineData("reached", 40, "{d} OwnershipDepthLimit_Kd")]
    [InlineData("reached", 10, "")]
    [InlineData("guard", 40, "{d} OwnershipDepthLimit_Kd")]
    [InlineData("guard", 10, "")]
    [InlineData("guardUsed", 40, "let d ComparisonLoanConflict_Kd|{w} OwnershipDepthLimit_Kd|{d} OwnershipDepthLimit_Kd")]
    public void AConflictOnlyDropLivenessGivesIsTheDepthLimit(string form, int depth, string expected)
    {
        var (declaration, body) = form switch
        {
            "borrowed" => ($"let d = {Nested("value@ref", depth)}", "    let r = d@ref\n    value = 99\n"),
            "used" => ($"let d = {Nested("value@ref", depth)}", "    value = 99\n    inspect(d@ref)\n"),
            "reached" => ($"let d = {Nested("x@ref", depth)}", "    value = 99\n"),
            "guard" => ($"var saved = {Nested("fallback@ref", depth)}", GuardBody(depth)),
            "guardUsed" => ($"var saved = {Nested("fallback@ref", depth)}", GuardBody(depth) + "    inspect(saved@ref)\n"),
            _ => throw new ArgumentOutOfRangeException(nameof(form), form, null),
        };
        var lead = form.StartsWith("guard", StringComparison.Ordinal)
            ? "    let fallback = Data.init()\n    let packet: Packet = .Data(Data.init())\n" : "    var value = 42\n    let x = value@ref\n";
        var c = CompileDepthForm(Prelude + "func f()\n" + lead + $"    {declaration}\n" + body + "    return\n");
        var rows = expected.Replace("{d}", declaration, StringComparison.Ordinal).Replace("{w}", Write(depth), StringComparison.Ordinal);
        Assert.Equal(Rows(rows), DepthRecords(c, Analyze(c), "f").Select(static x => $"{x.Line} {x.Code}"));

        static string Write(int depth) => $"saved = {Nested("d", depth)}";

        static string GuardBody(int depth)
            => $"    match packet\n        .Data(let d) if (label g: do\n            {Write(depth)}\n            exit to g true\n        ) => ()\n        _ => ()\n";
    }

    // SPEC 5.2.1 condition 3, 23.1.1 (R4-M153's revision 1, implementation review 1's B1): an own-step departure that only a drop-live
    // holder's Drop past the bound keeps open keeps its Warning beside the depth limit at the holder: d keeps the
    // pointer p formed through b's own step, and `b@follow = …` replaces b's owning value. Whether d's destruction uses p (owndtor: a
    // Reader whose drop releases it) or not (ownpast: a Tuple of the pointer) the walk cannot see past the bound. At depth 10 the walk
    // decides: owndtor's destruction uses p (the Warning alone) and ownpast's does not (nothing), as on dev.
    [Theory]
    [InlineData("owndtor", 40, "b@follow = Buf<i32>.init(1) OwnStepDeparture_Kd|{d} OwnershipDepthLimit_Kd")]
    [InlineData("ownpast", 40, "b@follow = Buf<i32>.init(1) OwnStepDeparture_Kd|{d} OwnershipDepthLimit_Kd")]
    [InlineData("owndtor", 10, "b@follow = Buf<i32>.init(1) OwnStepDeparture_Kd")]
    [InlineData("ownpast", 10, "")]
    public void AnOwnStepDepartureOnlyDropLivenessKeepsOpenKeepsItsWarning(string form, int depth, string expected)
    {
        const string Reader = "struct Reader\n    internal var p: raw/i32\n    public init(p: raw/i32) => self.p = p\n    drop\n" +
            "        unsafe => Raw.release(self.p)\n";
        var (declarations, declaration) = form switch
        {
            "owndtor" => (Reader, $"let d = {Nested("Reader.init(p: p)", depth)}"),
            "ownpast" => (string.Empty, $"let d = {Nested("p", depth)}"),
            _ => throw new ArgumentOutOfRangeException(nameof(form), form, null),
        };
        var c = CompileDepthForm(Prelude + RawShapes + OwnStepShapes + declarations +
            $"func f(b: uniq/Buf<i32>)\n    let p = b.data\n    {declaration}\n    b@follow = Buf<i32>.init(1)\n    return\n");
        Assert.Equal(
            Rows(expected.Replace("{d}", declaration, StringComparison.Ordinal)),
            DepthRecords(c, Analyze(c), "f").Select(static x => $"{x.Line} {x.Code}"));
    }

    // SPEC 5.2.1 condition 3, 23.1.2 (R4-M153's revision 2, implementation review 2's N-b): a location records the departure a counted pass
    // gives before one only the drop tier gives, whatever the order its entries were found in: `t@follow = …` departs t.y's pointer, which
    // the live s keeps (its Warning, anchored at t.y.data and witnessed by s's next use), and t.x's, which only the deep d keeps, so only
    // the first is recorded, with no depth limit, in both orders of the declarations; at depth 10 d's Drop is no use.
    [Theory]
    [InlineData("y first", 40)]
    [InlineData("x first", 40)]
    [InlineData("y first", 10)]
    public void ALocationsDepartureIsTheCountedOneBeforeTheDropTiers(string order, int depth)
    {
        const string Owners = "struct BB\n    public var x: Buf<i32>\n    public var y: Buf<i32>\n    public init()\n" +
            "        self.x = Buf<i32>.init(1)\n        self.y = Buf<i32>.init(1)\n";
        const string Y = "    let q = t.y.data\n    let s = (q, 1)\n";
        var x = $"    let p = t.x.data\n    let d = {Nested("p", depth)}\n";
        var c = CompileDepthForm(Prelude + RawShapes + OwnStepShapes + Owners + "func h(t: uniq/BB)\n" + (order == "y first" ? Y + x : x + Y) +
            "    t@follow = BB.init()\n    let e = s\n    return\n");
        var program = Analyze(c);
        Assert.Equal(["t@follow = BB.init() OwnStepDeparture_Kd"], DepthRecords(c, program, "h").Select(static x => $"{x.Line} {x.Code}"));
        Assert.Equal(["Anchor t.y.data", "Use s"], FactsOf(c, program, "h"));
    }

    // SPEC 23.1.1, 23.1.2 (R4-M153's revision 2, implementation review 2's B2): where an unsupported part and a part past the bound meet at
    // one call's verdict, it gets one record, MirOutcome.Join's answer, the located Unsupported_Kd: putTwo's W reaches h, a Limit holder
    // (R4-M147's stored raw layer a Type argument supplies), and dd, a Deep holder whose step would reach k. Its twins: each alone (the
    // depth limit at putAny), and at depth 10 the Limit alone. dd's own depth limit, at its declaration, is another verdict's: z's write
    // conflicts with the Loan the store put in dd only through dd's Drop past the bound.
    [Theory]
    [InlineData("limit", 40, "h.put(z@ref) Unsupported_Kd")]
    [InlineData("deep", 40, "putAny(dd@uniq, z@ref) OwnershipDepthLimit_Kd|{dd} OwnershipDepthLimit_Kd")]
    [InlineData("limit+deep", 40, "putTwo(h@uniq, dd@uniq, z@ref) Unsupported_Kd|{dd} OwnershipDepthLimit_Kd")]
    [InlineData("limit+deep", 10, "putTwo(h@uniq, dd@uniq, z@ref) Unsupported_Kd")]
    public void OneVerdictAnUnsupportedPartAndAPartPastTheBoundMeetAtGetsOneRecord(string form, int depth, string expected)
    {
        const string Wrap = "struct Wrap<E>\n    var value: E\n    public init(value: E) => self.value = value\n    public func put<U>(self: uniq/Self, v: U) => ()\n" +
            "func putAny<U, V>(t: uniq/U, v: V) => ()\nfunc putTwo<U, V, T>(a: uniq/U, b: uniq/V, v: T) => ()\n";
        const string Lead = "    var x = 1\n    var y = 2\n    var z = 3\n    var w: ref/i32 = x@ref\n    var r = w@uniq\n" +
            "    var h = Wrap.init(r@follow@raw@raw/u8)\n    var k: ref/i32 = y@ref\n";
        var dd = $"var dd = {Nested("k@ref", depth)}";
        var call = form switch
        {
            "limit" => "h.put(z@ref)",
            "deep" => "putAny(dd@uniq, z@ref)",
            "limit+deep" => "putTwo(h@uniq, dd@uniq, z@ref)",
            _ => throw new ArgumentOutOfRangeException(nameof(form), form, null),
        };
        var c = CompileDepthForm(Prelude + Wrap + "func f()\n" + Lead + $"    {dd}\n    {call}\n    z = 5\n    inspect(w)\n    inspect(k)\n");
        Assert.Equal(
            Rows(expected.Replace("{dd}", dd, StringComparison.Ordinal)),
            DepthRecords(c, Analyze(c), "f").Select(static x => $"{x.Line} {x.Code}"));
    }

    // SPEC 23.1.1 (R4-M153's revision 3, implementation review 3's B3 (i)): a destruction known to observe decides its check whatever lies
    // past the bound beside it, so the walk's answer is the Join of its parts', the same in either order: d, a Tuple of R (whose drop
    // observes the Loan of value) and a part past the bound, gives `value = 99`'s Language conflict and no depth limit, the observer first
    // or last; at depth 10 the same.
    [Theory]
    [InlineData("first", 40)]
    [InlineData("last", 40)]
    [InlineData("first", 10)]
    [InlineData("last", 10)]
    public void AnObserverDecidesBesideAPartPastTheBoundInEitherOrder(string observer, int depth)
    {
        var parts = observer == "first" ? $"R.init(r: value@ref), {Nested("0", depth)}" : $"{Nested("0", depth)}, R.init(r: value@ref)";
        var c = CompileDepthForm(Prelude + $"func f()\n    var value = 42\n    let d = ({parts})\n    value = 99\n    return\n");
        Assert.Equal(["value = 99 ComparisonLoanConflict_Kd"], DepthRecords(c, Analyze(c), "f").Select(static x => $"{x.Line} {x.Code}"));
    }

    // SPEC 23.1.1 (R4-M153's revision 3): the destruction walk enters a Type at the shallowest depth it meets it, so one Type at two depths
    // answers alike in either order: d holds R 41 levels down a Tuple and 9 levels down beside it, the deep one's tail being the shallow
    // one's Type; R's drop decides `value = 99`'s conflict, with no depth limit, the deep component first or last.
    [Theory]
    [InlineData("deep first")]
    [InlineData("shallow first")]
    public void AnObserversTypeMetPastAndWithinTheBoundDecidesInEitherOrder(string order)
    {
        var (deep, shallow) = (Nested("R.init(r: value@ref)", 40), Nested("R.init(r: value@ref)", 8));
        var parts = order == "deep first" ? $"{deep}, {shallow}" : $"{shallow}, {deep}";
        var c = CompileDepthForm(Prelude + $"func f()\n    var value = 42\n    let d = ({parts})\n    value = 99\n    return\n");
        Assert.Equal(["value = 99 ComparisonLoanConflict_Kd"], DepthRecords(c, Analyze(c), "f").Select(static x => $"{x.Line} {x.Code}"));
    }

    // SPEC 23.1.1, 23.1.2 (R4-M147, R4-M153's revision 3, implementation review 3's B3 (ii)): at a referent store whose referent's
    // destruction holds an abstract part not proven Copy (Limit) and a part past the bound (Deep), the one answer is Limit, so `a.clear()`
    // is the located Unsupported_Kd, as at depth 10 and for clearGenOk (the abstract part alone; ADestructionAReferentMayObserveKeepsW).
    [Theory]
    [InlineData("deep", 40)]
    [InlineData("deep", 10)]
    [InlineData("alone", 0)]
    public void AnAbstractPartBesideAPartPastTheBoundIsUnsupportedAtAReferentStore(string form, int depth)
    {
        var (element, value) = form == "deep"
            ? ($"(T, {NestedType(depth)})", $"(T.make(p: slot@raw, q: w@raw@raw/u8), {Nested("0", depth)})")
            : ("T", "T.make(p: slot@raw, q: w@raw@raw/u8)");
        var c = CompileDepthForm(Prelude + RawShapes + ReferentShapes + "func clearGen<T>()\n    T is MakeW\n    var y: i32 = 1\n" +
            $"    var w: ref/i32 = y@ref\n    var slot: raw/u8 = null\n    var a: Array<{element}> = []\n    a.append({value})\n    a.clear()\n" +
            "    consume(a@move)\n    usep(slot)\n    y = 5\n");
        Assert.Equal(["a.clear() Unsupported_Kd"], DepthRecords(c, Analyze(c), "clearGen").Select(static x => $"{x.Line} {x.Code}"));
    }

    // SPEC 23.1.2 (R4-M153's revision 3, implementation review 3's N1): a limit record is one failure's, so independent checks at one
    // subject keep their own records: `replace` destroys s's old value in place, whose destruction passes the bound (err(destroy): the depth
    // limit), and its W store reaches s, whose guard a stored raw layer a Type argument supplies leaves undecided (the located
    // Unsupported_Kd); s's own depth limit, at its declaration, is a third verdict's (the drop tier). At depth 10 the store's alone.
    [Theory]
    [InlineData(40, "{call} Unsupported_Kd|{call} OwnershipDepthLimit_Kd|{s} OwnershipDepthLimit_Kd")]
    [InlineData(10, "{call} Unsupported_Kd")]
    public void IndependentChecksAtOneSubjectKeepTheirOwnRecords(int depth, string expected)
    {
        const string Wrap = "struct Wrap<E>\n    var value: E\n    public init(value: E) => self.value = value\n";
        var value = $"(Wrap.init(r@follow@raw@raw/u8), {Nested("z@ref", depth)})";
        var (s, call) = ($"var s = {value}", $"Kimi.Intrinsics.replace(s@uniq, with: {value})");
        var c = CompileDepthForm(Prelude + Wrap + "func f()\n    var x = 1\n    var z = 3\n    var w: ref/i32 = x@ref\n    var r = w@uniq\n" +
            $"    {s}\n    {call}\n    inspect(w)\n");
        Assert.Equal(
            Rows(expected.Replace("{call}", call, StringComparison.Ordinal).Replace("{s}", s, StringComparison.Ordinal)),
            DepthRecords(c, Analyze(c), "f").Select(static x => $"{x.Line} {x.Code}"));
    }
}
