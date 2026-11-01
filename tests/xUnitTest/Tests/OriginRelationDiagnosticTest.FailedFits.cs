// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// G216 (a) Part 1's U1.6h rows (DECISIONS BU-77): FailedFit, the candidate's pattern at each unbound failed part its evidence walk
// meets, and its readers. The programs are R4's probes, copied as written.
public partial class OriginRelationDiagnosticTest
{
    private const string HTx = "func h<T>(x: T, y: i32) -> i32 => 0\n";

    private const string FOptionTuples = "func f(x: (Option<i64>, i32)) -> i32 => 0\nfunc f(x: (Option<bool>, i32)) -> i32 => 1\n";

    private const string GOptionPairs = "func g(x: i64, y: Option<i64>) -> i32 => 0\nfunc g(x: i64, y: Option<bool>) -> i32 => 1\n";

    private const string GSlotPairs = "func g<T>(x: T, y: Option<i64>) -> i32 => 0\nfunc g<U>(x: U, y: Option<bool>) -> i32 => 1\n";

    // U1.6h-1 (SPEC 23.1.2, 10.8; R4's C15, C22): the selected candidate's unsolved slots rest on a failed part only when every one occurs
    // in the candidate's pattern at a failed part, whose Type could have bound it. So `T` at the name's position is no record (g028, and
    // the first call of s1, s2 and s4), while a slot no failed part sits at keeps it: `T` at `Option<T>` beside the name at i64 (h4b; h4,
    // with a literal, rests at the join already), and `U` beside the name at `T` (h7); their twins without the name give the same record
    // (h4b_t, h7_t). FailedFit lives in its call's evaluation: another call of `h` keeps its independent record, in the same function
    // (s1), in another (s2) and with `T` bound by its other argument in the failing call (s4), and a call nested in the failing call's
    // outer call keeps the outer's (s3).
    [Theory]
    [InlineData("U1.6h-1", "g028", IdentityT + "func g<T>(x: (T, i32)) -> i32 => 0\n" + KD + "    return g((if c => .None else if d => undefinedName else => identity(.Some(3)), 3))\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-1", "h4", "func h<T>(x: i64, y: Option<T>) -> i32 => 0\n" + KHead + "    return h(if c => undefinedName else => 3, .None)\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-1", "h4b", IdentityT + "func h<T>(x: i64, y: Option<T>) -> i32 => 0\n" + KHead + "    return h(if c => undefinedName else => identity(3), .None)\n", "UnboundTypeArgument_Kd h(if c => undefinedName else => identity(3), .None); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-1", "h4b_t", IdentityT + "func h<T>(x: i64, y: Option<T>) -> i32 => 0\n" + KHead + "    return h(if c => identity(4) else => identity(3), .None)\n", "UnboundTypeArgument_Kd h(if c => identity(4) else => identity(3), .None)")]
    [InlineData("U1.6h-1", "h7", IdentityT + "func h<T, U>(x: T, y: Option<U>) -> i32 => 0\n" + KHead + "    return h(if c => undefinedName else => identity(3), .None)\n", "UnboundTypeArgument_Kd h(if c => undefinedName else => identity(3), .None); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-1", "h7_t", IdentityT + "func h<T, U>(x: T, y: Option<U>) -> i32 => 0\n" + KHead + "    return h(if c => identity(4) else => identity(3), .None)\n", "UnboundTypeArgument_Kd h(if c => identity(4) else => identity(3), .None)")]
    [InlineData("U1.6h-1", "s1", IdentityT + HTx + KHead + "    let a = h(if c => undefinedName else => identity(3), 1)\n    return h(identity(4), 2)\n", "UnboundTypeArgument_Kd h(identity(4), 2); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-1", "s2", IdentityT + HTx + KHead + "    let a = h(if c => undefinedName else => identity(3), 1)\n    return 0\nfunc m() -> i32\n    return h(identity(4), 2)\n", "UnboundTypeArgument_Kd h(identity(4), 2); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-1", "s3", IdentityT + HTx + "func g<U>(x: i32, y: Option<U>) -> i32 => 0\n" + KHead + "    return g(h(if c => undefinedName else => identity(3), 1), .None)\n", "UnboundTypeArgument_Kd g(h(if c => undefinedName else => identity(3), 1), .None); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-1", "s4", IdentityT + "func h<T>(x: T, y: Option<T>) -> i32 => 0\n" + KHead + "    let a = h(if c => undefinedName else => identity(3), .None)\n    let b = h(identity(4), .None)\n    return a\n", "UnboundTypeArgument_Kd h(identity(4), .None); UnresolvedBinding_Kd undefinedName")]
    public void AnUnsolvedSlotRestsOnlyWhereEveryOneIsAtAFailedPart(string unit, string form, string body, string records)
        => AssertRecords($"{form} [{unit}]", body, records, null);

    // U1.6h-2 (SPEC 23.1.2, 10.4; R4's C16): an ambiguity rests on a failed part only where the kept candidates' patterns there differ,
    // structurally and modulo their own slots, so that a Type of the part could have separated them: i64 against bool at the join's
    // position (n1_ov), as an element (f2_ov) and at a child position (e3_ov), Option<i64> against Option<bool> in a nested join (g238,
    // and R4's g238g). Where they agree, no Type of the part separates them and the ambiguity is independent: i64 against i64 (h3e, and
    // its twin without the name h3e_t), T against U, each its candidate's own slot (h8, h8_t), and literal-only joins (b3f). A literal
    // beside the name already rests at the join (h3, h3b, h3c).
    [Theory]
    [InlineData("U1.6h-2", "n1_ov", IdentityT + FScalars + KHead + "    return f(if c => undefinedName else => identity(1))\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-2", "f2_ov", IdentityT + FTuples + KHead + "    return f((if c => identity(1) else => undefinedName, 3))\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-2", "e3_ov", IdentityT + FTuples + KHead + "    return f(if c => (identity(1), 2) else => (undefinedName, 3))\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-2", "g238", IdentityT + FOptionTuples + KD + "    return f((if c => (if d => .None else => identity(.Some(4))) else => undefinedName, 3))\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-2", "g238g", IdentityT + FOptionTuples + "func k(c1: bool, c2: bool, c3: bool, c4: bool, c5: bool, c6: bool) -> i32\n    return f((if c5 => (if c3 => .Some(6) else if c4 => (if c1 => .None else if c2 => identity(.Some(4)) else => undefinedName) else => .None) else if c6 => .Some(8) else => .None, 3))\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-2", "h3e", IdentityT + GOptionPairs + KHead + "    return g(if c => undefinedName else => identity(3), .None)\n", "AmbiguousBinding_Kd g(if c => undefinedName else => identity(3), .None); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-2", "h3e_t", IdentityT + GOptionPairs + KHead + "    return g(if c => identity(4) else => identity(3), .None)\n", "AmbiguousBinding_Kd g(if c => identity(4) else => identity(3), .None)")]
    [InlineData("U1.6h-2", "h8", IdentityT + GSlotPairs + KHead + "    return g(if c => undefinedName else => identity(3), .None)\n", "AmbiguousBinding_Kd g(if c => undefinedName else => identity(3), .None); UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-2", "h8_t", IdentityT + GSlotPairs + KHead + "    return g(if c => identity(4) else => identity(3), .None)\n", "AmbiguousBinding_Kd g(if c => identity(4) else => identity(3), .None)")]
    [InlineData("U1.6h-2", "b3f", "func g(x: i64) -> i32 => 0\nfunc g(x: i32) -> i32 => 1\n" + KHead + "    return g(if c => (if c => 5 else => 6) else => 7)\n", "AmbiguousBinding_Kd g(if c => (if c => 5 else => 6) else => 7)")]
    [InlineData("U1.6h-2", "h3", GOptionPairs + KHead + "    return g(if c => undefinedName else => 3, .None)\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-2", "h3b", "func g(x: (i64, Option<i64>)) -> i32 => 0\nfunc g(x: (i64, Option<bool>)) -> i32 => 1\n" + KHead + "    return g((if c => undefinedName else => 3, .None))\n", "UnresolvedBinding_Kd undefinedName")]
    [InlineData("U1.6h-2", "h3c", "func g(x: i64, y: Option<i64>) -> i32 => 0\nfunc g(x: bool, y: Option<bool>) -> i32 => 1\n" + KHead + "    return g(if c => undefinedName else => 3, .None)\n", "UnresolvedBinding_Kd undefinedName")]
    public void AnAmbiguityRestsOnlyWhereTheKeptCandidatesDifferAtAFailedPart(string unit, string form, string body, string records)
        => AssertRecords($"{form} [{unit}]", body, records, null);

    // U1.6h-1 and U1.6h-2: a warm rebind publishes and releases FailedFit and compares the kept candidates' patterns in reused storage
    // (two calls whose slots rest on their failed parts, and an ambiguity that rests on its failed part).
    [Fact]
    [Trait("Purpose", "Allocation")]
    public void AWarmRebindOfFailedFitsAllocatesNothing()
    {
        var c = MirBuildTest.Compile(IdentityT + HTx + FTuples + "func g<T>(x: (T, i32)) -> i32 => 0\n" + KD +
            "    let a = h(if c => undefinedName else => identity(3), 1)\n" +
            "    let b = f(if c => (identity(1), 2) else => (undefinedName, 3))\n" +
            "    return g((if c => .None else if d => undefinedName else => identity(.Some(3)), 3))\n" + Main);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }
}
