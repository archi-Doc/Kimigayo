// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;
using static XunitTest.HirColumnsTest;

namespace XunitTest;

/// <summary>The HIR rows of the nodes Binding rejected or left unresolved (SPEC 23.1.2; docs/dev/DECISIONS.md R5-M63, R4-M88): an Error
/// node, the operands it keeps and the children it replaces.</summary>
public sealed partial class HirFormsTest
{
    // SPEC 23.1.2 (DECISIONS R4-M88): an operation Binding left unresolved (a built-in operator, `and`, a runtime Type test, a built-in
    // selection) is an Error node that keeps every operand its form evaluates, one Binding did not resolve or an Error too, but a construct
    // without a Type (a selection's `if` index), which stays replaced; a construct, an assignment and a labeled do Binding left unresolved
    // keep their Ops (fail-before: the first five kept their forms' Ops).
    [Theory]
    [InlineData("func f(x: i32) -> i32\n    return x + missing\n", "x + missing", "Error", "x missing")]
    [InlineData("func f(c: bool) -> bool\n    return c and missing\n", "c and missing", "Error", "c missing")]
    [InlineData("func f(x: i32) -> bool\n    return x is Missing\n", "x is Missing", "Error", "x")]
    [InlineData("func f(values: [3 of i32], r: Range<isize>) -> i32 => values[r]\n", "values[r]", "Error", "values r")]
    [InlineData("func f(c: bool, v: [3 of i32], i: isize) -> i32\n    return v[if c => i else => missing]\n", "v[if", "Error", "v")]
    [InlineData("func f(c: bool) -> i32\n    return if c => 1 else => missing\n", "if c => 1", "If", null)]
    [InlineData("func f()\n    var y: i32 = 0\n    y = missing\n", "y = missing", "Assign", null)]
    [InlineData("func f() -> i32\n    return label l: do\n        exit to l missing\n", "label l: do", "Group", null)]
    public void AnOperationBindingDidNotResolveIsAnError(string source, string text, string op, string? kept)
    {
        var c = MinimalEmissionTest.Analyze(source);
        HirTables.Publish(c);
        var node = Reachable(c.Kotonoha).First(x => x.ToString()!.StartsWith(text, StringComparison.Ordinal));
        Assert.NotEqual(BindingState.Resolved, node.StateOf());
        Assert.Equal(BindingFailure.None, node.FailureOf());
        Assert.Equal(op, HirTables.Op(node).ToString());
        var keeps = node.ChildNodes.Where(static x => HirTables.AdjustOf(x) == new HirAdjust(HirAcquire.None, HirAdaptation.None) && HirTables.Op(x) != HirOp.Syntax);
        Assert.True(kept is null || kept == string.Join(" ", keeps.Select(static x => x.ToString())), string.Join(" ", keeps.Select(static x => x.ToString())));
    }

    // SPEC 23.1.2 (DECISIONS R5-M63): a failed Field, Index, follow or `*p` keeps the operands Binding resolved as a value or a Place at
    // their positions, written in place (None, None) with their own Ops; a member name, a follow target, a Type name and an operand Binding
    // did not resolve, or resolved without the facts of an evaluated name (both paths of a name), stay replaced, so the MIR records
    // nothing. A group the parser recovered (a parameter default) keeps its operand; a group around a failed call is not the failed node,
    // and the call's children stay replaced (PLAN G121).
    [Fact]
    public void AFailedPlaceKeepsTheOperandsBindingResolved()
    {
        const string Source = "struct P\n    public var a: i32 = 1\n    public init() => ()\nfunc make(a: string, b: string) -> P => P.init()\n" +
            "func take(a: string, b: string) -> i32 => 1\nfunc f(n: i32)\n    let s = \"x\"\n    _ = n[take(s@move, s@move)]\n" +
            "    _ = make(s@move, s@move).missing\n    _ = take(s@move, s@move)@follow\n    _ = missing.0\n    _ = P.missing\n" +
            "    unsafe\n        _ = *take(s@move, s@move)\n    _ = (take(s@move, s@move, 1))\n";
        var c = MinimalEmissionTest.Analyze(Source);
        HirTables.Publish(c);
        var rows = Reachable(c.Kotonoha).Where(static x => x.Parent is { } parent && HirTables.Op(parent) == HirOp.Error)
            .Select(static x => (Node: x, Adjust: HirTables.AdjustOf(x)))
            .Select(x => $"{Source.Substring(x.Node.Span.Start, x.Node.Span.Length)} {HirTables.Op(x.Node)} {x.Adjust.Acquire}/{x.Adjust.Adaptation}");
        string[] expected =
        [
            "n Name None/None", "take(s@move, s@move) Call None/None", "make(s@move, s@move) Call None/None", "missing Syntax Unset/None",
            "take(s@move, s@move) Call None/None", "follow Syntax Unset/None", "missing Syntax Unset/None", "0 Syntax Unset/None",
            "P Syntax Unset/None", "missing Syntax Unset/None", "take(s@move, s@move) Call None/None", "take Syntax Unset/None",
            "s@move Syntax Unset/None", "s@move Syntax Unset/None", "1 Syntax Unset/None",
        ];
        Assert.Equal(expected, rows);
        Assert.Equal(HirOp.Group, HirTables.Op(Reachable(c.Kotonoha).OfType<ParenthesizedKoto>().Single()));
        Assert.Empty(c.BuildMir().Records);
        var ambiguous = MinimalEmissionTest.Analyze("group Config\n    public var count: i32 = 3\nstruct Settings\n    public var count: i32 = 0\n" +
            "func read(Config: ref/Settings) -> i32\n    return Config.count\n");
        Assert.Empty(ambiguous.BuildMir().Records);
        var recovered = MinimalEmissionTest.Analyze(
            "func take(a: string, b: string) -> i32 => 1\nfunc g(s: string, x: i32 = (take(s@move, s@move)) y: i32 = 0) -> i32 => x\n");
        HirTables.Publish(recovered);
        var group = Reachable(recovered.Kotonoha).OfType<ParenthesizedKoto>().Single();
        Assert.Equal(
            (HirOp.Error, HirOp.Call, new HirAdjust(HirAcquire.None, HirAdaptation.None)),
            (HirTables.Op(group), HirTables.Op(group.Operand), HirTables.AdjustOf(group.Operand)));
    }
}
