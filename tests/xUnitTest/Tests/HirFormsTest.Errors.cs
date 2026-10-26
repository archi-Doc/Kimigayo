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
    // Declarations whose callers Binding rejects at an input after selection: an Origin relation between fixed Origins it cannot prove
    // (UnprovenOriginRelation_Kd at the input), whatever the input's acquisition or adaptation.
    private const string DerivedInputs = """
        struct Tally
            public var n: i32 = 0
            public var item: i32 = 0
                get(self: ref/Self during static) -> i32 => storage
                set(self: uniq/Self, value: i32) -> () => storage = value
            public var count: i32 = 0
                get() -> i32 => storage
                set(value: i32) -> () => storage = value
            public init() => ()
            public func viewStatic(self: ref/Self during static) -> ref/i32 during static => self.n@ref
        open struct Base
            public var m: i32 = 0
            protected init() => ()
            public func look(self: ref/Self during static) -> ref/i32 during static => self.m@ref
        struct Leaf : Base
            public init() : base()
                ()
        func keepStatic(x: ref/i32 during static) -> ref/i32 during static => x
        func peek(value: ref/i32) => ()

        """;

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

    // SPEC 23.1.2 (DECISIONS R4-M109, amending R4-M88 Rule 2): an Error node a consumer takes, here an input Binding rejected after
    // selection, is published (Temporary, None), its derived value taken as it is, whatever acquisition or adaptation its consumer wrote: a
    // shared borrow of an argument or a receiver, a reference read, a Copy, a base borrow or a payload projection; a resolved input keeps
    // its own (fail-before: each kept its consumer's Adjust).
    [Theory]
    [InlineData("func f(c: ref/Tally)\n    _ = keepStatic(c.n)\n    peek(c.n)\n", "c.n")]
    [InlineData("func f(c: ref/Tally)\n    _ = c@follow.viewStatic()\n", "c@follow")]
    [InlineData("func f(p: uniq/i32)\n    _ = keepStatic(p)\n", "p")]
    [InlineData("func f(p: ref/i32)\n    _ = keepStatic(p)\n", "p")]
    [InlineData("func f(d: ref/Leaf)\n    _ = d.look()\n", "d")]
    [InlineData("func f(h: objref/Tally)\n    _ = h.viewStatic()\n", "h")]
    public void AnErrorInputIsItsConsumersDerivedValue(string body, string text)
    {
        var c = MinimalEmissionTest.Analyze(DerivedInputs + body);
        HirTables.Publish(c);
        var nodes = Reachable(c.Kotonoha).Where(x => x.ToString() == text && HirTables.Op(x) is not HirOp.Syntax).ToArray();
        var error = Assert.Single(nodes, static x => HirTables.Op(x) == HirOp.Error);
        Assert.Equal(BindingFailure.OriginRelation, error.FailureOf());
        Assert.Equal(new HirAdjust(HirAcquire.Temporary, HirAdaptation.None), HirTables.AdjustOf(error));
        Assert.All(nodes.Where(x => !ReferenceEquals(x, error)), static x => Assert.Equal(new HirAdjust(HirAcquire.None, HirAdaptation.SharedBorrow), HirTables.AdjustOf(x)));
    }

    // SPEC 23.1.2 (PLAN G122): a char literal the lexer could not read has no value (the lexer's published absence,
    // CharLiteralKoto.Value): the HIR publishes it Error, derived from the lexer's record, not Broken, while Binding binds and checks it
    // as a `char`, so its consumer's record stays (TypeMismatch_Kd at an `i32` initializer, whose failed declaration replaces it); a
    // char literal with its value is a Literal (fail-before: the malformed one was a Literal without its constant).
    [Theory]
    [InlineData("'\\q'", "InvalidEscape_Kd")]
    [InlineData("''", "InvalidCharLiteral_Kd")]
    [InlineData("'ab'", "InvalidCharLiteral_Kd")]
    [InlineData("'\\u{D800}'", "InvalidUnicodeEscape_Kd")]
    [InlineData("'\\u{110000}'", "InvalidUnicodeEscape_Kd")]
    public void ACharLiteralTheLexerCouldNotReadIsAnError(string literal, string code)
    {
        var c = MinimalEmissionTest.Analyze($"func f() -> char => {literal}\nfunc g()\n    let x: i32 = {literal}\n    let y = 'a'\n");
        HirTables.Publish(c);
        Assert.Equal(
            [$"{literal} Error", $"{literal} Syntax", "'a' Literal"],
            Reachable(c.Kotonoha).OfType<CharLiteralKoto>().Select(static x => $"{x} {HirTables.Op(x)}"));
        Assert.Empty(c.Kotonoha.Syntax.Hir.Broken!);
        Assert.Equal(
            new[] { code, code, "TypeMismatch_Kd" }.Order(StringComparer.Ordinal),
            CompilationTestHelper.BindingErrors(c).Select(static x => x.Code).Order(StringComparer.Ordinal));
    }

    // SPEC 13.7.2, 11.3 (DECISIONS R4-M109): a compound update's getter and setter take its receiver; where they take it by different
    // Adjusts, the column keeps the setter's and the receiver's plan row the getter's, and an input one call takes has none; an Error
    // receiver's both are its derived value (fail-before: no plan-row Adjust, and the Error receiver kept the setter's Reborrow).
    [Fact]
    public void AnUpdateReceiverPublishesEachAccessorsAdjust()
    {
        var c = MinimalEmissionTest.Analyze(DerivedInputs + "func bump(step: i32)\n    var box = Tally.init()\n    box.count += step\n    peek(box.n)\n" +
            "func fail(t: uniq/Tally) => t.item += 1\n");
        HirTables.Publish(c);
        var nodes = Reachable(c.Kotonoha);
        var box = nodes.Single(static x => x.ToString() == "box" && x.Parent?.ToString() == "box.count");
        Assert.Equal(
            (new HirAdjust(HirAcquire.None, HirAdaptation.ExclusiveBorrow), new HirAdjust(HirAcquire.None, HirAdaptation.SharedBorrow)),
            (HirTables.AdjustOf(box), HirTables.PlanOf(box).Adjust));
        Assert.Equal(default, HirTables.PlanOf(nodes.Single(static x => x.ToString() == "box.n")).Adjust);
        var receiver = nodes.Single(static x => x.ToString() == "t" && x.Parent?.ToString() == "t.item");
        var derived = new HirAdjust(HirAcquire.Temporary, HirAdaptation.None);
        Assert.Equal((HirOp.Error, derived, derived), (HirTables.Op(receiver), HirTables.AdjustOf(receiver), HirTables.PlanOf(receiver).Adjust));
    }
}
