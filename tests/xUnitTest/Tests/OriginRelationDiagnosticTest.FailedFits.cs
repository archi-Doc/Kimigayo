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

    // U1.6h-1: a warm rebind publishes and releases FailedFit in reused storage (two calls that rest on their failed parts).
    [Fact]
    [Trait("Purpose", "Allocation")]
    public void AWarmRebindOfFailedFitsAllocatesNothing()
    {
        var c = MirBuildTest.Compile(IdentityT + HTx + "func g<T>(x: (T, i32)) -> i32 => 0\n" + KD +
            "    let a = h(if c => undefinedName else => identity(3), 1)\n" +
            "    return g((if c => .None else if d => undefinedName else => identity(.Some(3)), 3))\n" + Main);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }
}
