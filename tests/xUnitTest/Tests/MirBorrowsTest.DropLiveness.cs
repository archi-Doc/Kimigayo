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
    // holder's Drop past the bound keeps open keeps its Warning, witnessed by that Drop, beside the depth limit at the holder: d keeps the
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
}
