// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Mir;
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

    // SPEC 23.1.2 (DECISIONS BU-106): a rejected do keeps its body Evaluated, written in place (None, None), with its parts' own Ops; a
    // labeled do that a jump published without failure goes to, and a rejected if, keep nothing (fail-before: the do's body was Syntax).
    [Fact]
    public void ARejectedDoKeepsItsBody()
    {
        const string Source = "func f(c: bool) -> i32\n    let x: i32 = do\n        require c else => ()\n        7\n" +
            "    let y: i32 = label done: do\n        if c => exit to done 2\n        require c else => ()\n" +
            "    let z: i32 = if c\n        require c else => ()\n    else\n        ()\n    return 1\n";
        var c = MinimalEmissionTest.Analyze(Source);
        HirTables.Publish(c);
        var dos = Reachable(c.Kotonoha).OfType<DoKoto>().ToArray();
        Assert.All(dos, static x => Assert.Equal(HirOp.Error, HirTables.Op(x)));
        Assert.Equal((HirOp.Block, new HirAdjust(HirAcquire.None, HirAdaptation.None)), (HirTables.Op(dos[0].Body), HirTables.AdjustOf(dos[0].Body)));
        Assert.Equal(HirOp.Require, HirTables.Op(Reachable(c.Kotonoha).OfType<RequireKoto>().First()));
        var rejectedIf = Reachable(c.Kotonoha).OfType<IfKoto>().Last();
        var replaced = Reachable(c.Kotonoha).Where(x => x.Parent is not null && (IsWithin(x, dos[1]) || IsWithin(x, rejectedIf)));
        Assert.All(replaced.Where(x => x is not DoKoto and not IfKoto), static x => Assert.Equal(HirOp.Syntax, HirTables.Op(x)));
    }

    // DECISIONS BU-106: the jump walk of a rejected labeled do visits its subtree once per enclosing rejected labeled do: linear in the
    // size at a fixed depth, and at most depth times the program's nodes for nesting (the depth is the syntax's own bound, PLAN G444).
    [Fact]
    public void TheJumpWalkIsBoundedByItsSubtree()
    {
        long[] siblings = [Work(1, 8).Work, Work(1, 16).Work, Work(1, 32).Work];
        Assert.True(siblings[2] - siblings[1] <= (2 * (siblings[1] - siblings[0])) + 8, $"siblings {string.Join(", ", siblings)}");
        foreach (var depth in (int[])[25, 50, 100])
        {
            var (work, nodes) = Work(depth, 1);
            Assert.True(work <= (long)depth * nodes, $"depth {depth}: {work} visits, {nodes} nodes");
        }

        static (long Work, long Nodes) Work(int depth, int statements)
        {
            var source = new System.Text.StringBuilder("func f(c: bool) -> i32\n");
            for (var i = 0; i < depth; i++)
            {
                source.Append(' ', 4 * (i + 1)).Append($"let y{i}: i32 = label l{i}: do\n");
            }

            for (var i = 0; i < statements; i++)
            {
                source.Append(' ', 4 * (depth + 1)).Append("require c else => ()\n");
            }

            source.Append("    return 1\n");
            var c = MinimalEmissionTest.Analyze(source.ToString());
            return (c.Binding.KeptTargetWork, Reachable(c.Kotonoha).LongCount());
        }
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
        Assert.Empty(c.BuildMir().Records(MirRecordClass.Construction));
        var ambiguous = MinimalEmissionTest.Analyze("group Config\n    public var count: i32 = 3\nstruct Settings\n    public var count: i32 = 0\n" +
            "func read(Config: ref/Settings) -> i32\n    return Config.count\n");
        Assert.Empty(ambiguous.BuildMir().Records(MirRecordClass.Construction));
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

    // SPEC 14.5.2, 14.9.1-14.9.3, 23.1.2 (DECISIONS BU-101): an invalid transfer goes to no target (Binding.TransferTarget). Its row is
    // null and Binding records row 9 or 10 at it; it supplies no result source and completes no construct, and its operand is bound with
    // no expected Type. A selection whose every transfer is invalid is a Never form (Never, Diverges). One with another source takes that
    // source's Type: an omitted or written `else`'s Unit, a valid labeled yield's, or the enclosing join's sources beside an inner Never
    // form. No record rests on the target an invalid transfer lacks: the operand's TypeMismatch_Kd, the discarded selection's own (r15) and
    // an enclosing join's at another source (r4's `2`) go. The operand's own records stay (TransferRequired_Kd at `yield xs`, as row 9
    // gives it), and so does the fallthrough the Dependent construct hid (FunctionFallthrough_Kd, SPEC 14.9.3's example). A valid labeled
    // yield to a discarded selection keeps its TypeMismatch_Kd (SPEC 14.7), and its Unit twin is valid. Fail-before: the first six rows'
    // constructs were Unresolved or `()` and Completes, with a TypeMismatch_Kd at each operand that is not Unit.
    [Theory]
    [InlineData("func f(a: i32)\n    if a > 0\n        yield 1\n    else\n        yield 2\n", "if a > 0: Never Diverges", "UnlabeledYieldTarget_Kd 3:9, UnlabeledYieldTarget_Kd 5:9")]
    [InlineData("func f(a: i32) -> i32\n    if a > 0\n        yield 1\n    else\n        yield 2\n", "if a > 0: Never Diverges", "UnlabeledYieldTarget_Kd 3:9, UnlabeledYieldTarget_Kd 5:9")]
    [InlineData("func f(a: i32) -> i32\n    if a > 0\n        yield 1\n    else\n        yield 2\n    return 0\n", "if a > 0: Never Diverges", "UnlabeledYieldTarget_Kd 3:9, UnlabeledYieldTarget_Kd 5:9")]
    [InlineData("func f(a: bool)\n    if a\n        yield ()\n    else\n        yield\n    ()\n", "if a: Never Diverges", "UnlabeledYieldTarget_Kd 3:9, UnlabeledYieldTarget_Kd 5:9")]
    [InlineData("func f(a: i32)\n    match a\n        0 => yield 1\n        _ => yield 2\n", "match a: Never Diverges", "UnlabeledYieldTarget_Kd 3:14, UnlabeledYieldTarget_Kd 4:14")]
    [InlineData("func f(a: i32) -> i32\n    var x: i32\n    if a > 0\n        yield 1\n    else\n        yield 2\n    return x\n", "if a > 0: Never Diverges", "UnlabeledYieldTarget_Kd 4:9, UnlabeledYieldTarget_Kd 6:9")]
    [InlineData("func r1(a: i32)\n    label s: if a > 0\n        yield to s ()\n    else\n        yield 1\n", "if a > 0: () Completes", "UnlabeledYieldTarget_Kd 5:9")]
    [InlineData("func r2(a: i32) -> i32\n    let v = if a > 0\n        if a > 1\n            yield 5\n        yield 1\n    else => 2\n    return v\n", "if a > 0: i32 Completes; if a > 1: () Completes", "UnlabeledYieldTarget_Kd 4:13")]
    [InlineData("func r4(a: i32) -> i32\n    let v = if a > 0\n        if a > 1\n            yield 5\n        else\n            yield 6\n        ()\n    else => 2\n    return v\n", "if a > 0: i32 Completes; if a > 1: Never Diverges", "UnlabeledYieldTarget_Kd 4:13, UnlabeledYieldTarget_Kd 6:13")]
    [InlineData("func r15(a: i32) -> i32\n    let v = if a > 0\n        if a > 1\n            yield \"s\"\n        yield 1\n    else => 2\n    return v\n", "if a > 0: i32 Completes; if a > 1: () Completes", "UnlabeledYieldTarget_Kd 4:13")]
    [InlineData("func r19(a: i32) -> Array<i32>\n    let xs: Array<i32> = [1, 2]\n    if a > 0\n        yield xs\n    else\n        ()\n    return xs\n", "if a > 0: () Completes", "TransferRequired_Kd 4:15, TransferRequired_Kd 7:12, UnlabeledYieldTarget_Kd 4:9")]
    [InlineData("func r19c(a: i32) -> Array<i32>\n    let xs: Array<i32> = [1, 2]\n    loop\n        yield xs\n", "loop: Never Diverges", "InvalidJumpTarget_Kd 4:9, TransferRequired_Kd 4:15")]
    [InlineData("func p12(a: i32) -> i32\n    if a > 0\n        yield 1\n    else\n        ()\n", "if a > 0: () Completes", "FunctionFallthrough_Kd 2:5, UnlabeledYieldTarget_Kd 3:9")]
    [InlineData("func p4(a: i32)\n    label s: if a > 0\n        yield to s 1\n    else\n        ()\n", "if a > 0: null Completes", "TypeMismatch_Kd 3:20")]
    [InlineData("func p5(a: i32)\n    label s: if a > 0\n        yield to s ()\n    else\n        ()\n", "if a > 0: () Completes", "")]
    public void AnInvalidTransferSuppliesNoSource(string source, string constructs, string records)
    {
        var c = MinimalEmissionTest.Analyze(source);
        HirTables.Publish(c);
        var nodes = Reachable(c.Kotonoha);
        Assert.All(nodes.OfType<JumpKoto>(), static x => Assert.True(HoldsItsTarget(x)));
        Assert.Equal(constructs, string.Join("; ", nodes.Where(static x => x is IfKoto or MatchKoto or LoopKoto)
            .Select(static x => $"{x.ToString()!.Split('\n')[0]}: {x.TypeOf()?.Name ?? "null"} {HirTables.CompletionOf(x)}")));
        Assert.Equal(records, string.Join(", ", CompilationTestHelper.BindingErrors(c).Select(x => $"{x.Code} {At(source, x.Span.Start)}").Order(StringComparer.Ordinal)));
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

    // SPEC 23.1.2 (DECISIONS BU-103): a consumed Place or a Pattern binding whose acquisition failed is published Failed: a Param whose Move
    // rests on an Error Copy proof, a typeless local and a group of a typeless alias, and a binding whose Copy proof is Error (Deferred); an
    // Error node's folds to its derived value (Temporary, None; R4-M109), and a discarded one's plan row is Failed, its column unset (no
    // consumer takes it). Fail-before: each was the column's default (Unset, None).
    [Fact]
    public void AFailedAcquisitionIsPublishedFailed()
    {
        const string Source = "struct Bad\n    Self is Copy\n    public var s: string = \"\"\n    public init() => ()\nstruct Gen\n    Self is Iterator\n" +
            "    associate Iterator.Item is Bad\n    public init() => ()\n    public func next(self: uniq/Self) -> Option<Bad> => .None\n" +
            "func a(b: Bad) -> Bad => b\nfunc f(t: (i32, i32)) -> i32 => t.5\nfunc k(c: bool) -> i32\n    let x = missing\n    let z = x\n" +
            "    let y: i32 = if c => 1 else => x\n    let w: i32 = if c => 1 else => (z)\n    return y + w\nfunc g() -> i32\n    let q = missing\n    q\n" +
            "func m(it: uniq/Gen)\n    match it.next()\n        .Some(let v) => ()\n        .None => ()\n";
        var c = MinimalEmissionTest.Analyze(Source);
        HirTables.Publish(c);
        var rows = Reachable(c.Kotonoha)
            .Where(static x => x.ToString() == "t.5" || HirTables.AdjustOf(x).Acquire == HirAcquire.Failed ||
                HirTables.PlanOf(x).Adjust.Acquire == HirAcquire.Failed)
            .Select(static x => $"{x} {HirTables.Op(x)} {HirTables.AdjustOf(x).Acquire} {HirTables.PlanOf(x).Adjust.Acquire}");
        string[] expected =
        [
            "b Name Failed Unset", "t.5 Error Temporary Unset", "x Name Failed Unset", "(z) Group Failed Unset", "q Name Unset Failed",
            "let v Syntax Failed Unset",
        ];
        Assert.Equal(expected, rows);
    }

    // The 1-based line:column of an offset in a source.
    private static string At(string source, int offset)
    {
        var start = offset == 0 ? 0 : source.LastIndexOf('\n', offset - 1) + 1;
        return $"{source.AsSpan(0, offset).Count('\n') + 1}:{offset - start + 1}";
    }
}
