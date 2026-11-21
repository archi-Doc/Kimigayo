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
    // The callees of the Keeps rows (ARejectedNodeKeepsWhatItsFormEvaluatesOnEveryPath).
    private const string K = "func k(a: i32) -> i32 => a\nfunc k2(a: i32, b: i32) -> i32 => a\n";

    // Z17's twin r-weak-sibling (valid; SPEC 3.2.2, 13.6.3, 15.6.2): a Weak Field's borrow beside a live `uniq` on a sibling Field, which
    // Binding rejects only through the Weak limit (Unsupported_Kd).
    private const string WeakSibling = "struct Node\n    public let w: Weak<rc/Node>\n    public var v: i32\n    public init(w: Weak<rc/Node>, v: i32)\n" +
        "        self.w = w@move\n        self.v = v\nfunc f(w: Weak<rc/Node>) -> i32\n    var n: Node = Node.init(w@move, 1)\n    let r = n.v@uniq\n" +
        "    let k = n.w@ref\n    r@follow = 2\n    return 0\n";

    // Declarations whose callers' inputs need an Origin relation between fixed Origins the premises do not prove, whatever the input's
    // acquisition or adaptation: since R7-O1 b1 a row A5 judges (UnprovenOriginRelation_Kd at the input), the input itself resolved. State's
    // static Places, which a call's input borrows, are the inputs Binding still rejects after selection: the located limit until P42
    // (FailStaticBorrow; DECISIONS BU-25).
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
        group State
            public var tally: Tally = Tally.init()
            public var leaf: Leaf = Leaf.init()
            public var handle: obj/Tally = Kimi.Intrinsics.makeObj<Tally>(Tally.init())

        """;

    // SPEC 23.1.2 (DECISIONS R4-M88, Z16): an operation Binding left unresolved (a built-in operator, `and`, a runtime Type test, a built-in
    // selection) is an Error node that keeps every operand its form evaluates on every path, one Binding did not resolve or an Error too,
    // but `and`'s right operand, which it evaluates only when the left holds (SPEC 13.4), and a construct without a Type (a selection's
    // `if` index), which stay replaced; a construct, an assignment and a labeled do Binding left unresolved keep their Ops (fail-before:
    // the first five kept their forms' Ops).
    [Theory]
    [InlineData("func f(x: i32) -> i32\n    return x + missing\n", "x + missing", "Error", "x missing")]
    [InlineData("func f(c: bool) -> bool\n    return c and missing\n", "c and missing", "Error", "c")]
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
        var keeps = node.Children().Where(static x => HirTables.AdjustOf(x) == new HirAdjust(HirAcquire.None, HirAdaptation.None) && HirTables.Op(x) != HirOp.Syntax);
        Assert.True(kept is null || kept == string.Join(" ", keeps.Select(static x => x.ToString())), string.Join(" ", keeps.Select(static x => x.ToString())));
    }

    // SPEC 23.1.2 (DECISIONS BU-106, Z16): a rejected do keeps its body Evaluated, written in place (None, None), with its parts' own Ops; a
    // labeled do that a jump published without failure goes to keeps nothing, and a rejected if keeps only its condition, which it
    // evaluates on every path, its bodies replaced (fail-before: the do's body was Syntax; the if's condition was Syntax until Z16).
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
        var condition = rejectedIf.Branches[0].Condition;
        Assert.Equal((HirOp.Name, new HirAdjust(HirAcquire.None, HirAdaptation.None)), (HirTables.Op(condition), HirTables.AdjustOf(condition)));
        var replaced = Reachable(c.Kotonoha).Where(x => x.Parent is not null && !ReferenceEquals(x, condition) && (IsWithin(x, dos[1]) || IsWithin(x, rejectedIf)));
        Assert.All(replaced.Where(x => x is not DoKoto and not IfKoto), static x => Assert.Equal(HirOp.Syntax, HirTables.Op(x)));
    }

    // DECISIONS BU-106: the jump walk of a rejected labeled do visits its subtree once per enclosing rejected labeled do: linear in the
    // number of the do's body statements at a fixed depth, and at most depth times the program's nodes for nesting. The depth rows reach
    // G414's 100, since the phases run on a compiler thread (DECISIONS R4-M178); a 1.5 MB Debug host overflowed from about 63, PLAN G444.
    [Fact]
    public void TheJumpWalkIsBoundedByItsSubtree()
    {
        long[] statements = [Work(1, 8).Work, Work(1, 16).Work, Work(1, 32).Work];
        Assert.True(MirBorrowsTest.Grows(statements, 2), $"body statements {string.Join(", ", statements)}");
        foreach (var depth in (int[])[12, 25, 50, 100])
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

    // SPEC 23.1.2 (DECISIONS R5-M63, Z16): a failed Field, Index, follow or `*p` keeps the operands Binding resolved as a value or a Place
    // at their positions, written in place (None, None) with their own Ops, and an operand Binding did not resolve as an Error, kept by the
    // same rule (Z16); a member name, a follow target, a Type name and a container name Binding rejected (both paths of a name), which
    // are no values, stay replaced, so the MIR records nothing. A group the parser recovered (a parameter default) keeps its operand; a
    // group around a failed call is not the failed node, and the call keeps its arguments (Z16; they stayed replaced until then, PLAN
    // G121), its callee covered.
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
            "take(s@move, s@move) Call None/None", "follow Syntax Unset/None", "missing Error None/None", "0 Syntax Unset/None",
            "P Syntax Unset/None", "missing Syntax Unset/None", "take(s@move, s@move) Call None/None", "take Syntax Unset/None",
            "s@move Group None/None", "s@move Group None/None", "1 Error None/None",
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

    // SPEC 15.6.1, 15.6.5, 23.1.2 (R7-O1 b1, superseding DECISIONS R4-M109 for call positions): an input whose call-position relation the
    // premises do not prove is no rejected input: the relation is a row A5 judges, so Binding records nothing and the input is published
    // with its own Op and the Adjust its consumer wrote, as a valid input is: a shared borrow of an argument or a receiver, a reference
    // read, a Copy, a base borrow or a payload projection (fail-before: an Error node, failed OriginRelation, its derived value
    // (Temporary, None)).
    [Theory]
    [InlineData("func f(c: ref/Tally)\n    _ = keepStatic(c.n)\n    peek(c.n)\n", "c.n", "None", "SharedBorrow")]
    [InlineData("func f(c: ref/Tally)\n    _ = c@follow.viewStatic()\n", "c@follow", "None", "SharedBorrow")]
    [InlineData("func f(p: uniq/i32)\n    _ = keepStatic(p)\n", "p", "None", "ReferenceRead")]
    [InlineData("func f(p: ref/i32)\n    _ = keepStatic(p)\n", "p", "Copy", "None")]
    [InlineData("func f(d: ref/Leaf)\n    _ = d.look()\n", "d", "None", "BaseBorrow")]
    [InlineData("func f(h: objref/Tally)\n    _ = h.viewStatic()\n", "h", "None", "PayloadProjection")]
    public void ACallPositionsInputKeepsItsConsumersAdjust(string body, string text, string acquire, string adaptation)
    {
        var c = MinimalEmissionTest.Analyze(DerivedInputs + body);
        HirTables.Publish(c);
        Assert.DoesNotContain(c.Binding.Issues, static x => x.Failure == BindingFailure.OriginRelation);
        var nodes = Reachable(c.Kotonoha).Where(x => x.ToString() == text && HirTables.Op(x) is not HirOp.Syntax).ToArray();
        Assert.NotEmpty(nodes);
        Assert.All(nodes, x => Assert.Equal((false, BindingFailure.None, new HirAdjust(Enum.Parse<HirAcquire>(acquire), Enum.Parse<HirAdaptation>(adaptation))), (HirTables.Op(x) == HirOp.Error, x.FailureOf(), HirTables.AdjustOf(x))));
    }

    // SPEC 23.1.2 (DECISIONS R4-M109, amending R4-M88 Rule 2; superseded for call positions only, ACallPositionsInputKeepsItsConsumersAdjust):
    // an Error node a consumer takes, here an input Binding rejects after selection (a static Place's Borrow, the located limit), is
    // published (Temporary, None), its derived value taken as it is, whatever adaptation its consumer wrote: a shared borrow of an argument
    // or a receiver, a base borrow or a payload projection.
    [Theory]
    [InlineData("func f()\n    peek(State.tally.n)\n", "State.tally.n")]
    [InlineData("func f()\n    _ = State.tally.viewStatic()\n", "State.tally")]
    [InlineData("func f()\n    _ = State.leaf.look()\n", "State.leaf")]
    [InlineData("func f()\n    _ = State.handle.viewStatic()\n", "State.handle")]
    public void AnErrorInputIsItsConsumersDerivedValue(string body, string text)
    {
        var c = MinimalEmissionTest.Analyze(DerivedInputs + body);
        HirTables.Publish(c);
        var nodes = Reachable(c.Kotonoha).Where(x => x.ToString() == text && HirTables.Op(x) is not HirOp.Syntax).ToArray();
        var error = Assert.Single(nodes, static x => HirTables.Op(x) == HirOp.Error);
        Assert.Equal((BindingFailure.Unsupported, new HirAdjust(HirAcquire.Temporary, HirAdaptation.None)), (error.FailureOf(), HirTables.AdjustOf(error)));
    }

    // SPEC 13.7.1, 23.1.2 (DECISIONS R5-M195): a plain write Binding rejected for its value's fit after admitting its destination keeps
    // Assign, its target its own Op (a local's Name, a setter's Call) and its value, which Binding resolved, the Error taken as its derived
    // value (Temporary, None); a write its destination check refuses (a Field of a computed Property's value, SPEC 3.4) and a compound
    // update Binding rejected stay Error, keeping their value in place, which a write evaluates on every path, and replacing their target,
    // which a write locates, not a value (SPEC 13.7.1, 13.7.2; DECISIONS Z16, G147 ruling 5) (fail-before: the local's and the setter's
    // writes were Error too; the two Errors' values were Syntax until Z16).
    [Theory]
    [InlineData("func f()\n    var x: i32 = 0\n    x = \"a\"\n", "x = \"a\"", "Assign Name Error Temporary/None")]
    [InlineData(
        "struct C\n    public var count: i32 = 0\n    public computed doubled: i32\n        get() -> i32 => self.count * 2\n" +
        "        set(value: i32) -> () => self.count = value / 2\n    public init() => ()\nfunc h(c: uniq/C)\n    c.doubled = \"a\"\n",
        "c.doubled = \"a\"",
        "Assign Call Error Temporary/None")]
    [InlineData(
        "struct In\n    public var v: i32 = 0\n    public init() => ()\nstruct B\n    public computed inner: In\n        get() -> In => In.init()\n" +
        "    public init() => ()\nfunc h(b: uniq/B)\n    b.inner.v = \"a\"\n",
        "b.inner.v = \"a\"",
        "Error Syntax Literal None/None")]
    [InlineData("func f()\n    var x: i32 = 0\n    x += \"a\"\n", "x += \"a\"", "Error Syntax Literal None/None")]
    public void ARejectedWriteKeepsItsWriteWithADerivedValue(string source, string text, string rows)
    {
        var c = MinimalEmissionTest.Analyze(source);
        HirTables.Publish(c);
        var write = Reachable(c.Kotonoha).OfType<BinaryKoto>().First(x => x.ToString() == text);
        Assert.NotEqual(BindingFailure.None, write.FailureOf());
        var value = HirTables.AdjustOf(write.Right);
        Assert.Equal(rows, $"{HirTables.Op(write)} {HirTables.Op(write.Left)} {HirTables.Op(write.Right)} {value.Acquire}/{value.Adaptation}");
    }

    // SPEC 23.1.2 (PLAN G122; DECISIONS Z16's gap row): a char literal the lexer could not read has no value (the lexer's published
    // absence, CharLiteralKoto.Value): the HIR publishes it Error, which rests on the lexer's own record (Binding.RestingCauses, the record's
    // key CharLiteralKoto.Cause), not Broken, while Binding binds and checks it as a `char`, so its consumer's record stays (TypeMismatch_Kd
    // at an `i32` initializer, whose failed declaration keeps it as an Error part); a char literal with its value is a Literal (fail-before:
    // the malformed one was a Literal without its constant; it rested on no cause until Z16).
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
        var literals = Reachable(c.Kotonoha).OfType<CharLiteralKoto>().ToArray();
        Assert.Equal([$"{literal} Error", $"{literal} Error", "'a' Literal"], literals.Select(static x => $"{x} {HirTables.Op(x)}"));
        Assert.All(literals[..2], x => Assert.Equal([x.Cause!.Value], c.Binding.RestingCauses(x)!));
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
    // constructs were Unresolved or `()` and Completes, with a TypeMismatch_Kd at each operand that is not Unit. SPEC 10.5.4 (DECISIONS
    // BU-109): an anonymous function's single-item body keeps the context fixed before it, so a result inferred as Unit leaves it in
    // Value Context (r21, r23, t5: valid; fail-before: row 10 at each yield beside its non-null row, and r23's `if` Diverges), while Unit
    // written (t2) or fixed by the expected Type (t1) or the parameter (t3) discards it.
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
    [InlineData("func r21(c: bool)\n    let g = func [c] () => if c => yield () else => ()\n    g()\n", "if c => yield (): () Completes", "")]
    [InlineData("func r23(c: bool)\n    let g = func [c] () => if c => yield () else => yield ()\n    g()\n", "if c => yield (): () Completes", "")]
    [InlineData("func t5(c: bool)\n    let g = func [c] () => match c\n        true => yield ()\n        false => ()\n    g()\n", "match c: () Completes", "")]
    [InlineData("func t1(c: bool)\n    let g: () -> () = func [c] () => if c => yield () else => ()\n    g()\n", "if c => yield (): () Completes", "UnlabeledYieldTarget_Kd 2:46")]
    [InlineData("func t2(c: bool)\n    let g = func [c] () -> () => if c => yield () else => ()\n    g()\n", "if c => yield (): () Completes", "UnlabeledYieldTarget_Kd 2:42")]
    [InlineData("func run(f: () -> ()) => f()\nfunc t3(c: bool) => run(func [c] () => if c => yield () else => ())\n", "if c => yield (): () Completes", "UnlabeledYieldTarget_Kd 2:48")]
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
    // Adjusts, the column keeps the setter's and the receiver's plan row the getter's, and an input one call takes has none (fail-before:
    // no plan-row Adjust). `fail`'s receiver, whose getter's `during static` the premises do not prove, is no rejected input since R7-O1 b1
    // (the relation is A5's row; R4-M109 superseded for call positions), so it takes the two accessors' own Adjusts as `box` does: the
    // setter's exclusive Reborrow in the column, the getter's reference read in the plan row (fail-before: an Error, both its derived value).
    // `limit`'s receiver, a static Place both accessors borrow, Binding still rejects after selection (the located limit): an Error, whose
    // column and plan row are both its derived value (R4-M109).
    [Fact]
    public void AnUpdateReceiverPublishesEachAccessorsAdjust()
    {
        var c = MinimalEmissionTest.Analyze(DerivedInputs + "func bump(step: i32)\n    var box = Tally.init()\n    box.count += step\n    peek(box.n)\n" +
            "func fail(t: uniq/Tally) => t.item += 1\nfunc limit() => State.tally.item += 1\n");
        HirTables.Publish(c);
        var nodes = Reachable(c.Kotonoha);
        var box = nodes.Single(static x => x.ToString() == "box" && x.Parent?.ToString() == "box.count");
        Assert.Equal(
            (new HirAdjust(HirAcquire.None, HirAdaptation.ExclusiveBorrow, ExclusiveMode: true), new HirAdjust(HirAcquire.None, HirAdaptation.SharedBorrow)),
            (HirTables.AdjustOf(box), HirTables.PlanOf(box).Adjust));
        Assert.Equal(default, HirTables.PlanOf(nodes.Single(static x => x.ToString() == "box.n")).Adjust);
        var receiver = nodes.Single(static x => x.ToString() == "t" && x.Parent?.ToString() == "t.item");
        Assert.Equal(
            (HirOp.Name, new HirAdjust(HirAcquire.Reborrow, HirAdaptation.None, ExclusiveMode: true), new HirAdjust(HirAcquire.None, HirAdaptation.ReferenceRead)),
            (HirTables.Op(receiver), HirTables.AdjustOf(receiver), HirTables.PlanOf(receiver).Adjust));
        var limited = nodes.Single(static x => x.ToString() == "State.tally" && x.Parent?.ToString() == "State.tally.item");
        var derived = new HirAdjust(HirAcquire.Temporary, HirAdaptation.None);
        Assert.Equal((HirOp.Error, derived, derived), (HirTables.Op(limited), HirTables.AdjustOf(limited), HirTables.PlanOf(limited).Adjust));
    }

    // SPEC 23.1.2 (DECISIONS BU-103, BU-110): a consumed Place or a Pattern binding whose acquisition failed is published Failed: a Param
    // whose Move rests on an Error Copy proof, a group of a typeless alias, and a binding whose Copy proof is Error (Deferred), a match arm's
    // or a for loop's item, also a Tuple item's slot; an Error node's folds to its derived value (Temporary, None; R4-M109), a name of a
    // typeless local among them, which is an Error since Z16 (HasFacts' Name row: a name needs its Type), discarded or not. Fail-before:
    // each was the column's default (Unset, None), and a for item whose Copy proof is Error was (Move, None). A valid Copy item stays
    // Copied and a Non-Copy one Moved.
    [Fact]
    public void AFailedAcquisitionIsPublishedFailed()
    {
        const string Source = "struct Bad\n    Self is Copy\n    public var s: string = \"\"\n    public init() => ()\nstruct Gen\n    Self is Iterator\n" +
            "    associate Iterator.Item is Bad\n    public init() => ()\n    public func next(self: uniq/Self) -> Option<Bad> => .None\n" +
            "func a(b: Bad) -> Bad => b\nfunc f(t: (i32, i32)) -> i32 => t.5\nfunc k(c: bool) -> i32\n    let x = missing\n    let z = x\n" +
            "    let y: i32 = if c => 1 else => x\n    let w: i32 = if c => 1 else => (z)\n    return y + w\nfunc g() -> i32\n    let q = missing\n    q\n" +
            "func m(it: uniq/Gen)\n    match it.next()\n        .Some(let v) => ()\n        .None => ()\n" +
            "func n(xs: Array<Bad>, ys: Array<(Bad, i32)>)\n    for e in xs@move\n        _ = 1\n    for (p, j) in ys@move\n        _ = j\n" +
            "struct Good\n    Self is Copy\n    public var n: i32 = 0\n    public init() => ()\nstruct Owned\n    public var s: string = \"\"\n" +
            "    public init() => ()\nfunc h(gs: Array<Good>, os: Array<Owned>)\n    for o in gs@move\n        _ = o\n    for u in os@move\n" +
            "        _ = u@move\n";
        var c = MinimalEmissionTest.Analyze(Source);
        HirTables.Publish(c);
        var rows = Reachable(c.Kotonoha)
            .Where(static x => x.ToString() == "t.5" || HirTables.AdjustOf(x).Acquire == HirAcquire.Failed ||
                HirTables.PlanOf(x).Adjust.Acquire == HirAcquire.Failed)
            .Select(static x => $"{x} {HirTables.Op(x)} {HirTables.AdjustOf(x).Acquire} {HirTables.PlanOf(x).Adjust.Acquire}");
        string[] expected =
        [
            "b Name Failed Unset", "t.5 Error Temporary Unset", "(z) Group Failed Unset", "let v Syntax Failed Unset", "e Syntax Failed Unset",
            "p Syntax Failed Unset",
        ];
        Assert.Equal(expected, rows);
        var items = Reachable(c.Kotonoha).Where(static x => x.ToString() is "j" or "o" or "u" && HirTables.Op(x) == HirOp.Syntax)
            .Select(static x => $"{x} {HirTables.AdjustOf(x).Acquire}");
        Assert.Equal(["j Copy", "o Copy", "u Move"], items);
    }

    // SPEC 12, 13.4, 14, 23.1.2 (DECISIONS Z16, the one Keeps rule; Z15 2g-D's B5 rows): a node Binding rejected or left unresolved keeps,
    // in place (None, None), every child its form evaluates on every path, a part Binding resolved or an Error, which keeps its own by the
    // same rule: a failed call's arguments, a let's initializer, an operator's operands (`and`'s left one only), a Tuple's, an array's,
    // a Case construction's and an interpolation's elements (its text segments too), a match's Subject, a for's Iterable and a rejected
    // if's first condition; `or`'s right operand, a construct without a Type (PLAN G121) and a closure Binding left unbound for the failed
    // call (an Error without causes of its own), also in parentheses, and a part without a Type that is no Error (a noinit under a failed
    // declaration Type) and a function's name, whose value its consumer's erasure forms, stay replaced (G147 ruling 5's rows 1, 2 and 4:
    // kept, the first two were InternalInvariant_Kd, the last Unsupported_Kd).
    // Fail-before: every row but `x + missing2`'s and `y + missing`'s kept nothing.
    [Theory]
    [InlineData(K + "func f() -> i32\n    var x: i32\n    let z = k2(x, missing)\n    return 1\n", "let z", "k2(x, missing)")]
    [InlineData(K + "func f() -> i32\n    var x: i32\n    let z = k2(x, missing)\n    return 1\n", "k2(x, missing)", "x missing")]
    [InlineData("func f() -> i32\n    var x: i32\n    missing(x + missing2)\n    return 1\n", "missing(", "x + missing2")]
    [InlineData("func f() -> i32\n    var x: i32\n    missing(x + missing2)\n    return 1\n", "x + missing2", "x missing2")]
    [InlineData("func f() -> i32\n    var x: i32\n    let t = (x, missing)\n    return 1\n", "(x, missing)", "x missing")]
    [InlineData("func f() -> i32\n    var x: i32\n    let a = [x, missing]\n    return 1\n", "[x, missing]", "x missing")]
    [InlineData("enum E\n    case A(i32, i32)\nfunc f() -> i32\n    var x: i32\n    let e = E.A(x, missing)\n    return 1\n", "E.A(x, missing)", "x missing")]
    [InlineData("func f() -> string\n    var x: i32\n    return \"\\(x) \\(missing)\"\n", "\"", "\"\" x \" \" missing \"\"")]
    [InlineData(
        "func f()\n    var y: Option<i32> = .Some(1)\n    let r = y@uniq\n    let z = match y\n        .Nope => 1\n        _ => 2\n    r@follow = .None\n",
        "match y",
        "y")]
    [InlineData("func f() -> i32\n    var x: i32\n    for i in missing(x)\n        _ = i\n    return 1\n", "for i", "missing(x)")]
    [InlineData(K + "func f(c: bool) -> i32\n    var x: i32\n    let z: i32 = if k(x) == 1\n        require c else => ()\n    else\n        ()\n    return 1\n", "if k(x)", "k(x) == 1")]
    [InlineData(K + "func f() -> bool\n    var x: i32\n    let b = k(x) == 1 and missing\n    return true\n", "k(x) == 1 and", "k(x) == 1")]
    [InlineData(K + "func f() -> bool\n    var x: i32\n    let b = missing or k(x) == 1\n    return true\n", "missing or", "missing")]
    [InlineData("func f(c: bool) -> i32\n    var x: i32\n    let v = if c => missing else => x\n    return 1\n", "let v", "")]
    [InlineData("func f()\n    missing(func (v: i32) -> i32 => v)\n", "missing(", "")]
    [InlineData("func k(g: (i32) -> i32) -> i32\n    return 0\nfunc f() -> i32\n    return k((func [] (o: bool) => 0))\n", "k((", "")]
    [InlineData(
        "func need<T>(x: T) -> i32\n    T is Owned\n    return 0\nfunc h() -> i32\n    if true\n        func q<U>(u: U) -> i32\n            if true\n" +
        "                func pk() -> i32 => 0\n                return need(pk)\n            return 0\n        return q(1)\n    return 0\n",
        "need(pk)",
        "")]
    [InlineData("func f()\n    unsafe\n        var values: [2 of Missing] = noinit\n", "var values", "")]
    public void ARejectedNodeKeepsWhatItsFormEvaluatesOnEveryPath(string source, string text, string kept)
    {
        var c = MinimalEmissionTest.Analyze(source, output: OutputKind.Library);
        HirTables.Publish(c);
        var node = Reachable(c.Kotonoha).First(x => x.ToString()!.StartsWith(text, StringComparison.Ordinal) && HirTables.Op(x) == HirOp.Error);
        var keeps = node.Children().Where(static x => HirTables.AdjustOf(x) == new HirAdjust(HirAcquire.None, HirAdaptation.None) && HirTables.Op(x) != HirOp.Syntax);
        Assert.Equal(kept, string.Join(" ", keeps.Select(static x => x.ToString())));
        Assert.Empty(c.Kotonoha.Syntax.Hir.Broken!);
    }

    // DECISIONS D-986 (for Z17's commit 3): a kept operand is marked read (PlanRow.KeptRead) where the Adjust rule, run for its Error's
    // form, reads its whole Place in a use not in place (HirAdjust.Reads: an acquisition or a shared borrow, SPEC 13.4, 15.1.5, 15.1.6), as
    // it takes a valid node's child of that form: an operator's Copy operand, a non-Copy operand it inspects through a shared Loan and an
    // Error value, a Tuple's elements, `and`'s left operand, a built-in index's index, a match's Place Subject, which its plan borrows
    // shared, a for's Iterable, and the operand of a conversion Binding left unclassified (KindOp's Convert arm: a same-Type acquisition
    // and G839's `h@raw/Missing`); not a selection's receiver or a built-in index's base (in place: r-weak-sibling's `n.w`, valid beside
    // its limits, an unresolved member, and a value base, which the rule takes as a Temporary in place: `mk()[missing]`, the row that
    // fails without FormReads' in-place guard), a borrow's operand, nor an argument of a call Binding did not resolve, whose operation it
    // did not decide (CallPlan.Of reads only a resolved call's plan), whether it selected a callee (`k2`) or none (`k`, two arguments for
    // one parameter). Only a kept operand is marked. Fail-before: no kept operand was marked.
    [Theory]
    [InlineData(K + "func f() -> i32\n    var x: i32\n    let z = k2(x, missing)\n    return 1\n", "k2(x, missing)", "")]
    [InlineData(K + "func f() -> i32\n    var x: i32\n    let z = k(x, missing)\n    return 1\n", "k(x, missing)", "")]
    [InlineData("func f() -> i32\n    var x: i32\n    missing(x + missing2)\n    return 1\n", "x + missing2", "x missing2")]
    [InlineData("func f(s: string) -> bool\n    return s == missing\n", "s == missing", "s missing")]
    [InlineData("func f() -> i32\n    var x: i32\n    let t = (x, missing)\n    return 1\n", "(x, missing)", "x missing")]
    [InlineData("func f(c: bool) -> bool\n    return c and missing\n", "c and missing", "c")]
    [InlineData("func f(v: [3 of i32]) -> i32\n    return v[missing]\n", "v[missing]", "missing")]
    [InlineData(
        "func f()\n    var y: Option<i32> = .Some(1)\n    let r = y@uniq\n    let z = match y\n        .Nope => 1\n        _ => 2\n    r@follow = .None\n",
        "match y",
        "y")]
    [InlineData("func f() -> i32\n    var x: i32\n    for i in missing(x)\n        _ = i\n    return 1\n", "for i in missing(x)", "missing(x)")]
    [InlineData("func f(s: string) -> string\n    return s@string\n", "s@string", "s")]
    [InlineData("func f() -> i32\n    var h = 1\n    let r = h@uniq\n    let p = h@raw/Missing\n    r@follow = 2\n    return 0\n", "h@raw/Missing", "h")]
    [InlineData(WeakSibling, "n.w", "")]
    [InlineData(WeakSibling, "n.w@ref", "")]
    [InlineData("func mk() -> [3 of i32] => [1, 2, 3]\nfunc f() -> i32\n    return mk()[missing]\n", "mk()[missing]", "missing")]
    [InlineData("struct P\n    public var a: i32 = 1\n    public init() => ()\nfunc f(p: P) -> i32\n    return p.missing\n", "p.missing", "")]
    public void AKeptOperandIsReadWhereItsErrorsFormReadsItsPlace(string source, string text, string read)
    {
        var kept = new HirAdjust(HirAcquire.None, HirAdaptation.None);
        var c = MinimalEmissionTest.Analyze(source, output: OutputKind.Library);
        HirTables.Publish(c);
        var node = Reachable(c.Kotonoha).First(x => x.ToString() is { } written && (written == text || written.StartsWith(text + "\n", StringComparison.Ordinal)) &&
            HirTables.Op(x) == HirOp.Error);
        var marked = node.Children().Where(x => HirTables.AdjustOf(x) == kept && HirTables.KeptRead(x));
        Assert.Equal(read, string.Join(" ", marked.Select(static x => x.ToString())));
        Assert.DoesNotContain(Reachable(c.Kotonoha), x => HirTables.KeptRead(x) && !(x.Parent is { } parent && HirTables.Op(parent) == HirOp.Error && HirTables.AdjustOf(x) == kept));
        Assert.Empty(c.Kotonoha.Syntax.Hir.Broken!);
    }

    // DECISIONS D-986 (G147 ruling 53): a kept operand's read is its publication's. Check publishes again in its pass (after H1's
    // failures; CheckBound reruns it), so a read an earlier publication of the pass recorded is not a later one's: with the recorded read
    // of `v[missing]`'s index placed at its kept base `v`, which the form uses in place, CheckBound's republication marks the index alone.
    // Fail-before (a pass-lived row): `v` was marked read too.
    [Fact]
    public void AKeptOperandsReadIsItsPublications()
    {
        var c = MinimalEmissionTest.Analyze("func f(v: [3 of i32]) -> i32\n    return v[missing]\n", output: OutputKind.Library);
        HirTables.Publish(c);
        var node = Reachable(c.Kotonoha).First(static x => x.ToString() == "v[missing]" && HirTables.Op(x) == HirOp.Error);
        var (unread, read) = (node.Children().First(static x => x.ToString() == "v"), node.Children().First(static x => x.ToString() == "missing"));
        ref var rows = ref HirTables.Of(read).Families.FormReads;
        var recorded = rows.Get(read.SyntaxId, c.Binding);
        Assert.NotEqual(default, recorded);
        rows.Ref(unread.SyntaxId, c.Binding) = recorded;
        c.Binding.CheckBound();
        Assert.Equal("missing", string.Join(" ", node.Children().Where(HirTables.KeptRead).Select(static x => x.ToString())));
    }

    // DECISIONS Z16 (Z15 2g-D D2; B1, B4, B6): every failure fact the HIR publishes (Binding.FailureFact: an Error, a failed acquisition at
    // the acquired node, a stored Property's declaration not published Body: a static with no initializer, or one whose parts are
    // unverified or whose slots are unstated) rests on its causes (Binding.RestingCauses), so the publisher's one list of broken contracts
    // (HirTables.Broken) is empty: an unreadable char literal on its own lexical record and an admitted misfit's value on its write's fit
    // failure (the gap rows), a Place whose Move rests on an Error Copy proof on the unresolved mark, a name of a Type-less binding (an
    // Error by HasFacts' Name row) and a match whose plan failed on Binding's causes, a static without an initializer on its record (PLAN
    // G1113), and a valid computed static has none. A static's or a Field's declaration whose own Type failed is Body with its failed slot,
    // the failure's one published fact (PLAN G1073, DECISIONS D-834; G147 ruling 27): the static's use is the fact (`G.s`, an Error by the
    // Name row), the Field gives none; a static's initializer Binding failed is its own fact. Fail-before: the first three facts rested on
    // no cause.
    [Theory]
    [InlineData("func f() -> char\n    return 'ab'\n", "'ab'")]
    [InlineData("func f() -> i32\n    var x: i32\n    x = \"a\"\n    return x\n", "\"a\"")]
    [InlineData("struct Bad\n    Self is Copy\n    public var s: string = \"\"\n    public init() => ()\nfunc a(b: Bad) -> Bad => b\n", "b")]
    [InlineData("func use(x: string) => ()\nfunc f()\n    let q = missing\n    use(q@move)\n    use(q@move)\n", "q")]
    [InlineData("func f(v: Option<i32>) -> i32\n    return match v\n        .Some(let n) if missing(n) => 1\n        _ => 0\n", "match v")]
    [InlineData("group G\n    public let s: Missing = 1\nfunc f() -> i32\n    return G.s\n", "G.s")]
    [InlineData("struct Q\n    public var n: Missing = 1\n    public var m: i32\n    public init(m: i32)\n        self.m = m\n", "")]
    [InlineData("group G\n    public let s: i32 = missing\nfunc f() -> i32\n    return G.s\n", "missing")]
    [InlineData("group G\n    public let s: i32\nfunc f() -> i32\n    return G.s\n", "Error")]
    [InlineData("group G\n    public computed x: i32\n        get() -> i32 => 1\nfunc f() -> i32\n    return G.x\n", "")]
    public void EveryFailureFactRestsOnItsCauses(string source, string fact)
    {
        var c = MinimalEmissionTest.Analyze(source, output: OutputKind.Library);
        HirTables.Publish(c);
        var facts = Reachable(c.Kotonoha).Where(static x => x.SyntaxOwner is { Hir.Ops: { } ops } && x.SyntaxId < ops.Length && Binding.FailureFact(x, HirTables.Op(x))).ToArray();
        Assert.All(facts, x => Assert.NotNull(c.Binding.RestingCauses(x)));
        Assert.Empty(c.Kotonoha.Syntax.Hir.Broken!);
        Assert.True(fact.Length == 0 ? facts.Length == 0 : facts.Any(x => x.ToString()!.StartsWith(fact, StringComparison.Ordinal)), string.Join(", ", facts.Select(static x => x.ToString())));
    }

    // The 1-based line:column of an offset in a source.
    private static string At(string source, int offset)
    {
        var start = offset == 0 ? 0 : source.LastIndexOf('\n', offset - 1) + 1;
        return $"{source.AsSpan(0, offset).Count('\n') + 1}:{offset - start + 1}";
    }
}
