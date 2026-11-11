// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>The descriptor table and the MIR validator (plan 5.4, 14-5): every kind has a row; <see cref="MirUses"/> reports what a
/// location uses and its effects by the rows; a body that every rule accepts has no finding, and each rule rejects a body that breaks only
/// it, at the syntax of what breaks it; a warm validation allocates nothing.</summary>
[Collection(nameof(MirValidatorTest))] // Its fixture's TyCtxt, which MirDumpTest shares, is not thread-safe.
public sealed class MirValidatorTest(ITestOutputHelper output)
{
    // An owning interpolation of a literal, a value and a literal (FormatBuffer, three FormatSegments, a write, FormatFinish).
    private const string Interpolation = "func f(n: i32) -> string => \"n=\\(n)!\"\n";

    // A static, as the static tests write it, and a body reading it on two branches, each after its own staticEnsure (G191).
    private const string Registry = "group Registry\n    public let total: i32 = 5\n";
    private const string Branches = "func use(c: bool) -> i32 => if c => Registry.total else => Registry.total\n";

    private static readonly SourceInfo At = default;

    // The source of what a rule's body changes.
    private static readonly SourceInfo Broken = new(new(0, 77), 0, true);

    private static readonly Lazy<(Compilation Compilation, MirModelTest.Types Types)> Fixture = new(MirModelTest.Fixture);

    public static TheoryData<string> Rules => new(Enum.GetNames<MirViolation>());

    // G620's rows, each integer Type's payload bits: a signed Type's ends and one past each; an unsigned Type's 0, its maximum (-1), the
    // value 2^(N-1) (-2^(N-1)) and 2^(N-1) - 1, and outside: the value form of 2^(N-1) and of the maximum, 2^N, one below -2^(N-1); isize
    // and usize at 64 bits; 128-bit payloads; dev's no-target bound tests (C-142 N3).
    public static TheoryData<string, string, bool> IntegerConstants
    {
        get
        {
            var rows = new List<(string Type, Int128 Bits, bool Holds)>
            {
                ("i128", Int128.MinValue, true), ("i128", Int128.MaxValue, true), ("u128", Int128.MinValue, true), ("u128", Int128.NegativeOne, true),
                ("i32", -(Int128.One << 127), false), ("u8", (Int128.One << 127) - 1, false),
            };
            foreach (var (suffix, width) in new[] { ("8", 8), ("16", 16), ("32", 32), ("64", 64), ("size", 64) })
            {
                var (half, full) = (Int128.One << (width - 1), Int128.One << width);
                foreach (var sign in new[] { "i", "u" })
                {
                    rows.AddRange([(sign + suffix, -half, true), (sign + suffix, half - 1, true), (sign + suffix, -half - 1, false), (sign + suffix, half, false)]);
                }

                rows.AddRange([("u" + suffix, 0, true), ("u" + suffix, -1, true), ("u" + suffix, full - 1, false), ("u" + suffix, full, false)]);
            }

            return [.. rows.Select(static x => (x.Type, x.Bits.ToString(CultureInfo.InvariantCulture), x.Holds))];
        }
    }

    // Each closed vocabulary, the aggregate kinds (DECISIONS R5-M129) and the intrinsic table have one overload of MirDescriptors.Of, and each
    // member a row; a value outside the enumeration and IntrinsicId.None have none. Only the Opaque row's access, its roots', is an Opaque
    // root's (MirDescriptors.IsOpaqueRoot, G191 N1).
    [Fact]
    public void EveryKindHasADescriptorRow()
    {
        var overloads = typeof(MirDescriptors).GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Where(static x => x.Name == nameof(MirDescriptors.Of)).ToArray();
        Assert.Equal(
            new[]
            {
                typeof(IntrinsicId), typeof(MirAggregateKind), typeof(MirOperandKind), typeof(MirProjectionKind), typeof(MirRvalueKind),
                typeof(MirStatementKind), typeof(MirTerminatorKind),
            },
            overloads.Select(static x => x.GetParameters()[0].ParameterType).OrderBy(static x => x.Name, StringComparer.Ordinal));
        foreach (var overload in overloads)
        {
            var kind = overload.GetParameters()[0].ParameterType;
            foreach (var value in Enum.GetValues(kind))
            {
                var row = (MirDescriptor)overload.Invoke(null, [value])!;
                Assert.True(row.Defined != value is IntrinsicId.None, $"{kind.Name}.{value}");
                Assert.True(MirDescriptors.IsOpaqueRoot(row.Access) == value is MirRvalueKind.Opaque, $"{kind.Name}.{value}");
            }

            Assert.False(((MirDescriptor)overload.Invoke(null, [Enum.ToObject(kind, 200)])!).Defined);
        }

        Assert.Equal(new[] { IntrinsicId.Abort, IntrinsicId.Fail, IntrinsicId.TestRequireAbort }, Enum.GetValues<IntrinsicId>().Where(static x => MirDescriptors.Of(x).Diverges));
    }

    // A3 E14: an intrinsic serving a library declaration takes its parameters first, so its row's arity covers them, and the operand its
    // row says the result retains is one the builder adds beyond them (RawSlice's backing).
    [Fact]
    public void AnIntrinsicsRowCoversItsDeclaration()
    {
        var c = Fixture.Value.Compilation;
        var declared = Enum.GetValues<CompilerFunctionKind>().Where(static x => IntrinsicDisposition.Of(x).Kind == DispositionKind.Intrinsic)
            .Select(x => (Row: MirDescriptors.Of(IntrinsicDisposition.Of(x).Id), Declaration: c.Library.Function(x)))
            .Where(static x => x.Declaration is not null).ToArray();
        Assert.NotEmpty(declared);
        foreach (var (row, declaration) in declared)
        {
            _ = TyCtxt.Signature(declaration!, null, -1, out var count);
            Assert.True(count <= row.Arity && (row.Retained < 0 || (row.Retained >= count && row.Retained < row.Arity)), declaration!.Name);
        }

        Assert.Equal([IntrinsicId.RawSlice], Enum.GetValues<IntrinsicId>().Where(static x => MirDescriptors.Of(x) is { Defined: true, Retained: >= 0 }));
    }

    // An rvalue's operands before its Place, the index locals of a Place before it, a destination last; static accesses, pair cases,
    // intrinsic and Opaque effects as the rows give them; a diverging Call writes no destination. An Adapt's acquired Place and payload are
    // case-dependent and its creation possible; under a Semantics case view it is the operation its case selects: of a T to s/T (s is
    // owner or uniq), the owner case's Identity reads its operand and the uniq case's Borrow borrows it exclusively.
    [Fact]
    public void UsesFollowTheDescriptorRows()
    {
        var (c, types) = Fixture.Value;
        var body = MirModelTest.NewBody(types);
        var pairs = MirModelTest.Local(body, c.Types.Tuple([BoundType.I32, BoundType.Boolean]), MirLocalKind.Arg);
        var pair = MirModelTest.Local(body, types.Pair, MirLocalKind.Arg);
        var array = MirModelTest.Local(body, types.Array);
        var index = MirModelTest.Local(body, BoundType.ISize, MirLocalKind.Temp);
        var exclusive = MirModelTest.Local(body, c.Types.Reference(SemanticsKind.Uniq, BoundType.I32));
        var pointer = MirModelTest.Local(body, c.Types.Pointer(BoundType.I32));
        _ = TyCtxt.Layer(types.Pair, out var payload, out _);
        var value = MirModelTest.Local(body, payload!);
        PairBinder.Collect(types.Generic, body.Binders);
        var shared = body.Static(new(types.Function, BoundType.I32, false, true));
        var (ensure, entry, abort, drop, exit) = (body.AddBlock(), body.AddBlock(), body.AddBlock(), body.AddBlock(), body.AddBlock()); // The ensure first.
        var result = new MirPlace(0);
        void Assign(MirPlace place, MirRvalue rvalue) => body.Push(entry, MirStatement.Assign(place, rvalue, At));
        int Operands(params MirOperand[] operands) => AddOperands(body, operands);
        var one = MirOperand.Const(body.Const(new(MirConstKind.Integer, BoundType.I32, 1)));
        Assign(result, MirRvalue.Binary(MirBinaryOp.Add, Operands(MirOperand.Copy(body.Project(pairs, MirProjectionKind.Field, 0)), one), false));
        Assign(result, MirRvalue.Use(Operands(MirOperand.Copy(body.Project(array, MirProjectionKind.Index, index.Root)))));
        body.Push(entry, MirStatement.Of(MirStatementKind.FakeRead, shared, At));
        Assign(pair, MirRvalue.Acquire(Operands(MirOperand.PairAcquire(0, pair)), MirRefKind.Uniq));
        body.Push(entry, MirStatement.Of(MirStatementKind.FakeRead, body.Project(pair, MirProjectionKind.PairLayer, 0), At));
        Assign(exclusive, MirRvalue.Ref(MirRefKind.Uniq, shared, body.TypeOf(exclusive)!));
        Assign(pointer, MirRvalue.Cast(MirCastKind.IntToPointer, Operands(MirOperand.Copy(index)), body.TypeOf(pointer)!));
        Assign(result, MirRvalue.Opaque(BoundType.I32, body.AddOpaque(default, [array.Root])));
        var token = MirOperand.Const(body.Const(new(MirConstKind.TypeToken, payload!)));
        Assign(pair, MirRvalue.Adapt(Operands(MirOperand.Copy(value), token), value, types.Pair, shorthand: true, input: false));
        body.Terminate(entry, MirTerminator.Call(MirCallee.Intrinsic(IntrinsicId.MakeObj), Operands(MirOperand.Copy(result)), 1, result, abort, At));
        var text = Operands(MirOperand.Const(body.Const(new(MirConstKind.String, BoundType.String, Value: "abort"))));
        body.Terminate(abort, MirTerminator.Call(MirCallee.Intrinsic(IntrinsicId.Abort), text, 1, result, -1, At) with { CheckTarget = drop });
        body.Terminate(drop, MirTerminator.Drop(array, exit, At));
        body.Terminate(exit, MirTerminator.Return(At));
        var ensured = MirModelTest.Local(body, BoundType.Unit, MirLocalKind.Temp);
        var slot = MirOperand.Const(body.Const(new(MirConstKind.Integer, BoundType.ISize, 0)));
        body.Terminate(ensure, MirTerminator.Call(MirCallee.Intrinsic(IntrinsicId.StaticEnsure), Operands(slot), 1, ensured, entry, At));
        Assert.Empty(Validate(body));

        Assert.Equal(["Copy", "1* Reads", "Const", "Abort -1", "0 Writes, Defines"], Uses(body, entry, 0));
        Assert.Equal(["Copy", "4 Reads", "Abort -1", "3* Reads", "0 Writes, Defines"], Uses(body, entry, 1));
        Assert.Equal(["-1 Reads", "StaticRead, RawStaticRead 0"], Uses(body, entry, 2));
        Assert.Equal(["PairAcquire", "2 Reads, CaseDependent", "2 Writes, Defines"], Uses(body, entry, 3));
        Assert.Equal(["2* Reads, CaseDependent"], Uses(body, entry, 4));
        Assert.Equal(["-1 Borrows Uniq", "StaticWrite 0", "5 Writes, Defines"], Uses(body, entry, 5));
        Assert.Equal(["Copy", "4 Reads", "IntToPointer -1", "6 Writes, Defines"], Uses(body, entry, 6));
        Assert.Equal(["Unknown 0", "3 Reads, Writes, Borrows Shared", "0 Writes, Defines"], Uses(body, entry, 7));
        Assert.Equal(["Copy", "7 Reads, CaseDependent", "Const", "7 CaseDependent", "Allocation -1", "2 Writes, Defines"], Uses(body, entry, 8));
        Assert.Equal(["Copy", "0 Reads", "Allocation -1", "0 Writes, Defines"], Uses(body, entry, 9));
        Assert.Equal(["Const", "Abort -1"], Uses(body, abort, 0));
        Assert.Equal(["3 DropUse"], Uses(body, drop, 0));
        Assert.Equal(["0 Moves"], Uses(body, exit, 0));
        Assert.Equal(["Const", "StaticInit, Abort -1", "8 Writes, Defines"], Uses(body, ensure, 0));
        Assert.Equal(new[] { 1, 1, 0, 1, 0 }, body.Blocks.ToArray().Select(static x => MirUses.Successors(x.Terminator)));

        var cases = new MirCases(c.Types);
        cases.Select(body, 0);
        Assert.Equal(["Copy", "7 Reads", "2 Writes, Defines"], Uses(body, entry, 8, cases));
        Assert.Equal(Uses(body, entry, 7), Uses(body, entry, 7, cases));
        cases.Select(body, 1);
        Assert.Equal(["7 Borrows Uniq", "2 Writes, Defines"], Uses(body, entry, 8, cases));
    }

    // A body with every statement, rvalue, terminator, operand and projection kind, an intrinsic and a Value callee, a static, an Opaque
    // site, a scope, a pair binder, a block that only a check edge reaches and a MustNotComplete row with a guessed site; and the Type
    // tier's body, every form it checks in a valid use.
    [Fact]
    public void ValidBodyHasNoFinding()
    {
        Assert.Empty(Validate(Build(null)));
        Assert.Empty(Validate(Typed(null)));
    }

    [Theory]
    [MemberData(nameof(Rules))]
    public void EachRuleRejectsTheBodyThatBreaksIt(string name)
    {
        var rule = Enum.Parse<MirViolation>(name);
        var findings = Validate(rule switch { MirViolation.Type => Typed("assign"), MirViolation.Operation => Typed("operands"), _ => Build(rule) });
        Assert.NotEmpty(findings);
        Assert.All(findings, x => Assert.Equal(rule, x.Rule));
        Assert.All(findings, x => Assert.Equal(rule is MirViolation.NoBlocks or MirViolation.Unterminated or MirViolation.StatementRange ? default : Broken.Syntax, x.Subject));
    }

    // The Type tier (MirTypes.Agree, modulo Origins): each form it checks, broken alone, is a finding of its rule at the syntax that breaks
    // it: an Assign's rvalue, an aggregate's operand (a Tuple's element, a struct's base, a Case's payload element, a Closure's capture:
    // each row of the aggregate's shape), a call's argument (the receiver first, a default template's shared references) and
    // result, a Value callee's argument and _0 at a Return disagreeing with their Types; an operator's operands, a shift count, a raw
    // pointer's displacement, a comparison's operands below their layers, a Cast, a Ref, an object upcast and a branch's operand that their
    // kind does not admit; an Adapt whose operand is no by-value acquisition, whose source token is not its operand's Type, that a case
    // view selects no operation for (i32 to f64), or whose object borrow there takes a Place other than the payload, and one whose Op has a
    // bit besides its shorthand and input flags, of an unknown kind; an intrinsic callee whose function is not the library declaration of
    // a compiler function it serves (A3 E14); a Value callee whose signature is no TypeToken constant (C8), and a Closure callee given more
    // arguments than its declared signature has inputs; a testRecordValue whose value is no Scalar (a ref/i32: no layer is followed; SPEC
    // 3.1, F8b), which code generation never sees (fail-before: no row), and one short of its row's arity 2, an Arity finding (fail-before:
    // arity -1 admitted any count). A local an Opaque value defines has an unknown Type (the replaced syntax may have none), which any use
    // admits; the same use of a local a Unit constant defines is a finding. So is the Never value's at every operand position (a Use, an
    // operator, an aggregate; SPEC 3.8, DECISIONS R4-M62), but a ref/Never is no ref/i32. A returning Value call of a `(i32) -> Never`
    // callable into a Temp of Type Never agrees in Type but is no writer R5-M38 admits.
    [Theory]
    [InlineData("assign", "Type")]
    [InlineData("component", "Type")]
    [InlineData("base", "Type")]
    [InlineData("case", "Type")]
    [InlineData("capture", "Type")]
    [InlineData("argument", "Type")]
    [InlineData("receiver", "Type")]
    [InlineData("result", "Type")]
    [InlineData("template", "Type")]
    [InlineData("value", "Type")]
    [InlineData("signature", "Callee")]
    [InlineData("declaration", "Callee")]
    [InlineData("arity", "Arity")]
    [InlineData("return", "Type")]
    [InlineData("operands", "Operation")]
    [InlineData("shift", "Operation")]
    [InlineData("displacement", "Operation")]
    [InlineData("comparison", "Operation")]
    [InlineData("cast", "Operation")]
    [InlineData("ref", "Operation")]
    [InlineData("storage", "Operation")] // O-92 (a), N9: exactly a fixed array, an Array or a Slice, never through a reference.
    [InlineData("upcast", "Operation")]
    [InlineData("switch", "Operation")]
    [InlineData("opaque", "Type")]
    [InlineData("never", "Type")]
    [InlineData("acquisition", "Operation")]
    [InlineData("token", "Type")]
    [InlineData("adapt", "Operation")]
    [InlineData("payload", "Operation")]
    [InlineData("shorthand", "UnknownKind")]
    [InlineData("storage-op", "UnknownKind")] // A Storage is a raw borrow: any other Op is of an unknown kind (N12).
    [InlineData("returning", "NeverWrite")]
    [InlineData("record", "Type")]
    [InlineData("short", "Arity")]
    public void TypeTierRejectsEachForm(string form, string rule)
    {
        var findings = Validate(Typed(form));
        Assert.NotEmpty(findings);
        Assert.All(findings, x => Assert.Equal((Enum.Parse<MirViolation>(rule), Broken.Syntax), (x.Rule, x.Subject)));
    }

    // SPEC 4.3.1, 4.3.3, 4.3.4, 12.4.1 (DECISIONS R4-M81, R5-M129): a fixed array's aggregate has one operand per element or none (noinit,
    // the only count at a generic length, where no element literal binds), and a Fill one, at a fixed-array Type; an Array's any number, none
    // for `[]`, at an Array Type, which no FixedArray aggregate builds any more; any other count or Type is an Arity finding (fail-before: a
    // generic length admitted any count, and no Fill was valid; an Array was a FixedArray aggregate of no operand).
    [Theory]
    [InlineData("FixedArray", 3, "b", false)]
    [InlineData("FixedArray", 0, "b", false)]
    [InlineData("FixedArray", 0, "a", false)]
    [InlineData("Fill", 1, "b", false)]
    [InlineData("Fill", 1, "a", false)]
    [InlineData("Array", 2, "d", false)]
    [InlineData("Array", 0, "d", false)]
    [InlineData("FixedArray", 1, "b", true)]
    [InlineData("FixedArray", 1, "a", true)]
    [InlineData("Fill", 2, "b", true)]
    [InlineData("Fill", 1, "d", true)]
    [InlineData("FixedArray", 0, "d", true)]
    [InlineData("Array", 3, "b", true)]
    public void AnArraysAggregateHasTheOperandsItsKindSays(string kind, int count, string parameter, bool broken)
    {
        var c = MirBuildTest.Compile("func w<length N>(a: [N of i32], b: [3 of i32], d: Array<i32>) -> i32 => 1\n");
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "w");
        var body = new MirBody();
        body.Reset(new(function.SymbolOf()!, MirBodyKind.Fn), default);
        body.AddLocal(new(BoundType.I32, MirLocalKind.Return, At));
        var target = MirModelTest.Local(body, function.Parameters.Single(x => x.InternalName == parameter).Type.TypeOf()!);
        var one = MirOperand.Const(body.Const(new(MirConstKind.Integer, BoundType.I32, 1)));
        var block = body.AddBlock();
        var aggregate = MirRvalue.Aggregate(Enum.Parse<MirAggregateKind>(kind), AddOperands(body, [.. Enumerable.Repeat(one, count)]), count, body.TypeOf(target)!);
        body.Push(block, MirStatement.Assign(target, aggregate, Broken));
        body.Terminate(block, MirTerminator.Return(At));
        var findings = new List<MirFinding>();
        new MirValidator(c.Types).Validate(body, findings);
        Assert.Equal(broken ? [MirViolation.Arity] : [], findings.Select(static x => x.Rule));
    }

    // SPEC 4.8.1, 8.15.3 (PLAN G597): a keyed Storage has two operands, its start position, an isize, and the token of its key's Type,
    // whose start is open in the body (R; the S of Range<S, End> and Range<S, E>, keys whose own IsAbstract is false): no finding. A key
    // that decides its start takes the unkeyed form, so its token is an Operation finding either way: one that starts at the storage
    // (Range<Start, End>, Range<Start, E>) and one that displaces (Range<i32, E>, which holds a parameter; Range<FromEnd<i32>, End>;
    // ResolvedRange). So are a start that is no isize (flag) and a second operand that is no Type token (value); one operand or three is
    // an Arity finding (fail-before: a Storage with any operand was an Arity finding).
    [Theory]
    [InlineData("r", 2, null)]
    [InlineData("from", 2, null)]
    [InlineData("both", 2, null)]
    [InlineData("full", 2, "Operation")]
    [InlineData("to", 2, "Operation")]
    [InlineData("at", 2, "Operation")]
    [InlineData("back", 2, "Operation")]
    [InlineData("resolved", 2, "Operation")]
    [InlineData("flag", 2, "Operation")]
    [InlineData("value", 2, "Operation")]
    [InlineData("r", 1, "Arity")]
    [InlineData("r", 3, "Arity")]
    public void AKeyedStorageTakesAStartAndAnOpenKey(string form, int count, string? rule)
    {
        var (body, types) = KeyedStorage(form, count);
        MirViolation[] expected = rule is null ? [] : [Enum.Parse<MirViolation>(rule)];
        Assert.Equal(expected.Select(static x => (x, Broken.Syntax)), Validate(body, types).Select(static x => (x.Rule, x.Subject)));
    }

    // SPEC 4.9.5, 4.9.6, 22.5.1 (DECISIONS R5-M129): an Array aggregate of elements publishes its buffer's allocation and its Abort through
    // its kind's row, the effects the reserve and append calls it replaces had; `[]` allocates nothing and reports none, and a fixed array is
    // placed in its own storage (fail-before: no aggregate kind had a row).
    [Fact]
    public void AnArrayAggregateReportsItsAllocationAndAbort()
    {
        var c = MirBuildTest.Compile("func w(b: [3 of i32], d: Array<i32>) -> i32 => 1\n");
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "w");
        var body = new MirBody();
        body.Reset(new(function.SymbolOf()!, MirBodyKind.Fn), default);
        body.AddLocal(new(BoundType.I32, MirLocalKind.Return, At));
        var fixedArray = MirModelTest.Local(body, function.Parameters[0].Type.TypeOf()!);
        var array = MirModelTest.Local(body, function.Parameters[1].Type.TypeOf()!);
        var one = MirOperand.Const(body.Const(new(MirConstKind.Integer, BoundType.I32, 1)));
        var block = body.AddBlock();
        void Assign(MirPlace place, MirAggregateKind kind, MirOperand[] operands)
            => body.Push(block, MirStatement.Assign(place, MirRvalue.Aggregate(kind, AddOperands(body, operands), operands.Length, body.TypeOf(place)!), At));
        Assign(array, MirAggregateKind.Array, [one, one]);
        Assign(array, MirAggregateKind.Array, []);
        Assign(fixedArray, MirAggregateKind.FixedArray, [one, one, one]);
        body.Terminate(block, MirTerminator.Return(At));
        var findings = new List<MirFinding>();
        new MirValidator(c.Types).Validate(body, findings);
        Assert.Empty(findings);
        Assert.Equal(["Const", "Const", "Abort, Allocation -1", "2 Writes, Defines"], Uses(body, block, 0));
        Assert.Equal(["2 Writes, Defines"], Uses(body, block, 1));
        Assert.Equal(["Const", "Const", "Const", "1 Writes, Defines"], Uses(body, block, 2));
    }

    // SPEC 8.2, 15.3: Types that differ only by their Origins agree, and so do Types a body's Type-identity premise equates (E and S under
    // `E is S`), which the corpus's generic bodies rely on; other Types disagree.
    [Fact]
    public void TypesAgreeModuloOriginsAndPremises()
    {
        var c = MirBuildTest.Compile("func pick(x: ref/i32) -> ref/i32 during x => x\nfunc same<S, E>(a: S, b: E) -> S\n    E is S\n    return b@move\n");
        Assert.Empty(c.BuildMir().Findings);
        BindingSymbol Function(string name) => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == name).SymbolOf()!;
        var held = TyCtxt.Signature(Function("pick"), null, -1, out _)!;
        Assert.True(held.CarriesOrigin);
        Assert.True(MirTypes.Agree(held, c.Types.Reference(SemanticsKind.Ref, BoundType.I32)));
        Assert.False(MirTypes.Agree(held, c.Types.Reference(SemanticsKind.Ref, BoundType.Boolean)));
        Assert.False(MirTypes.Agree(held, c.Types.Reference(SemanticsKind.Uniq, BoundType.I32)));
        var same = Function("same");
        var (s, e) = (TyCtxt.Signature(same, null, 0, out _)!, TyCtxt.Signature(same, null, 1, out _)!);
        Assert.False(MirTypes.Agree(s, e));
        Assert.Same(TyCtxt.Premised(same, s), TyCtxt.Premised(same, e));
    }

    // G131 (DECISIONS R5-M33): only check-only blocks read a Temp whose only writes are diverging Calls' destinations, an argument's here;
    // a Never parameter and a destination an if's other arm writes are read on runtime paths, validly (SPEC 3.1.3). A read of a Never
    // payload binding (SPEC 14.9.3) is valid too, but Binding rejects it today, so no row has one. EachRuleRejectsTheBodyThatBreaksIt has
    // the invalid read.
    [Theory]
    [InlineData("func absurd(x: Never) -> i32 => x\n")]
    [InlineData("func stop() -> Never => $abort(\"stop\")\nfunc pick(c: bool) -> i32\n    let x: i32 = if c => stop() else => 1\n    return x\n")]
    [InlineData("func stop() -> Never => $abort(\"stop\")\nfunc take(a: i32, b: i32) -> i32 => a + b\nfunc f(n: i32) -> i32 => take(n, stop())\n")]
    public void OnlyCheckOnlyBlocksReadWhatOnlyADivergingCallWrites(string source)
    {
        var c = MirBuildTest.Compile(source);
        Assert.Empty(c.Binding.Issues);
        Assert.Empty(c.BuildMir().Findings);
    }

    // G131 (DECISIONS R5-M33): a Drop, like a StorageDead, reads nothing, so a runtime Drop of a Temp only a diverging Call writes, storage
    // nothing initialized, is valid: the validator's body here drops the Abort's destination where it dropped point.
    [Fact]
    public void ARuntimeDropOfWhatOnlyADivergingCallWritesIsValid()
    {
        var body = Build(null);
        var stopped = new MirPlace(Enumerable.Range(0, body.Locals.Length).Single(x => body.Locals[x].Type == BoundType.Never));
        var drop = Enumerable.Range(0, body.Blocks.Length).Single(x => body.Blocks[x].Terminator.Kind == MirTerminatorKind.Drop);
        body.Terminate(drop, body.Blocks[drop].Terminator with { Place = stopped });
        Assert.Empty(Validate(body));
    }

    // Plan 5.6.1: a PairAcquire and a PairLayer name the binder of their Place's pair layer, so each resolves under every Semantics case view
    // (MirCases). In a body with two binders, naming the other one is a finding: Binder for the operand, Projection for the step, whose Type
    // ProjectedType does not give. An adaptation family `p@r` (SPEC 13.5.5; PLAN G755) names its binder too (TyCtxt.PairBinder).
    [Fact]
    public void PairFormsNameTheirLayersBinder()
    {
        var c = MirBuildTest.Compile(
            "struct Node\n    public var n: i32\n    public init(n: i32) => self.n = n\n" +
            "func both<s/T, t/U, r/V>(a: s/T, b: t/U, c: r/V, o: obj/Node) -> ()\n    s is owner or uniq\n    t is owner or ref\n    r is objref or objuniq\n" +
            "    T is Copy\n    U is Copy\n    _ = a\n    _ = b\n    var p = o@move\n    let y = p@r\n    _ = y\n");
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "both");
        var body = new MirBody();
        body.Reset(new(function.SymbolOf()!, MirBodyKind.Fn), default);
        body.AddLocal(new(BoundType.Unit, MirLocalKind.Return, At));
        var (pa, pb) = (function.Parameters[0].Type.TypeOf()!, function.Parameters[1].Type.TypeOf()!);
        var (a, b) = (MirModelTest.Local(body, pa, MirLocalKind.Arg), MirModelTest.Local(body, pb, MirLocalKind.Arg));
        _ = (TyCtxt.Layer(pa, out var t, out _), TyCtxt.Layer(pb, out var u, out _));
        var py = KotoTree.Walk(function).OfType<VariableKoto>().Single(static x => x.NameKoto.IdentifierName == "y").SymbolOf()!.Type!;
        Assert.Equal(BoundTypeKind.SemanticsAdaptation, py.Kind);
        var y = MirModelTest.Local(body, py);
        PairBinder.Collect(function, body.Binders);
        var block = body.AddBlock();
        void Read(MirOperand operand, BoundType type, SourceInfo source) => body.Push(block, MirStatement.Assign(
            MirModelTest.Local(body, type),
            operand.Kind == MirOperandKind.PairAcquire ? MirRvalue.Acquire(body.AddOperand(operand), MirRefKind.Uniq) : MirRvalue.Use(body.AddOperand(operand)),
            source));
        Read(MirOperand.PairAcquire(0, a), pa, At);
        Read(MirOperand.PairAcquire(1, b), pb, At);
        Read(MirOperand.PairAcquire(2, y), py, At);
        Read(MirOperand.Copy(body.Project(a, MirProjectionKind.PairLayer, 0)), t!, At);
        Read(MirOperand.Copy(body.Project(b, MirProjectionKind.PairLayer, 1)), u!, At);
        body.Terminate(block, MirTerminator.Return(At));
        Assert.Empty(Validate(body, c.Types));

        // A family is never followed as one layer (it has no single target), so a PairLayer step of it is a finding as well.
        Read(MirOperand.PairAcquire(1, a), pa, Broken);
        Read(MirOperand.Copy(body.Project(b, MirProjectionKind.PairLayer, 0)), u!, Broken);
        Read(MirOperand.PairAcquire(0, y), py, Broken);
        Read(MirOperand.Copy(body.Project(y, MirProjectionKind.PairLayer, 2)), py, Broken);
        Assert.Equal(
            [(MirViolation.Binder, Broken.Syntax), (MirViolation.Projection, Broken.Syntax), (MirViolation.Binder, Broken.Syntax), (MirViolation.Projection, Broken.Syntax)],
            Validate(body, c.Types).Select(static x => (x.Rule, x.Subject)));
    }

    // A3 E13 (4): a PairAcquire is always the whole rvalue of an Assign to a local's whole Place, its Use marked with the Reborrow its
    // exclusive case makes (Uniq, or TwoPhaseUniq for an invocation's input), and only such a Use is marked. A PairAcquire passed to a Call,
    // gathered into an aggregate or assigned to a projection, one unmarked or marked Raw, and a marked Use of a constant are each an
    // Operation finding at their syntax.
    [Theory]
    [InlineData(null)]
    [InlineData("argument")]
    [InlineData("aggregate")]
    [InlineData("projection")]
    [InlineData("unmarked")]
    [InlineData("raw")]
    [InlineData("constant")]
    public void APairAcquireIsTheWholeRvalueOfAnAssignToALocal(string? broken)
    {
        var c = MirBuildTest.Compile(
            "func both<s/T, t/U>(a: s/T, b: t/U) -> ()\n    s is owner or uniq\n    t is owner or ref\n    T is Copy\n    U is Copy\n    _ = a\n    _ = b\n");
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "both");
        var body = new MirBody();
        body.Reset(new(function.SymbolOf()!, MirBodyKind.Fn), default);
        body.AddLocal(new(BoundType.Unit, MirLocalKind.Return, At));
        var (pa, pb) = (function.Parameters[0].Type.TypeOf()!, function.Parameters[1].Type.TypeOf()!);
        var (a, b) = (MirModelTest.Local(body, pa, MirLocalKind.Arg), MirModelTest.Local(body, pb, MirLocalKind.Arg));
        PairBinder.Collect(function, body.Binders);
        var (first, second) = (MirModelTest.Local(body, pa, MirLocalKind.Temp), MirModelTest.Local(body, pb, MirLocalKind.Temp));
        var (tuple, number) = (MirModelTest.Local(body, c.Types.Tuple([pa, BoundType.I32])), MirModelTest.Local(body, BoundType.I32));
        var (block, next) = (body.AddBlock(), body.AddBlock());
        SourceInfo Where(string form) => broken == form ? Broken : At;
        void Assign(MirPlace place, MirRvalue rvalue, string form) => body.Push(block, MirStatement.Assign(place, rvalue, Where(form)));
        var (acquire, one) = (MirOperand.PairAcquire(0, a), MirOperand.Const(body.Const(new(MirConstKind.Integer, BoundType.I32, 1))));
        var mark = broken switch { "unmarked" => MirRefKind.Shared, "raw" => MirRefKind.Raw, _ => MirRefKind.Uniq };
        Assign(first, MirRvalue.Acquire(body.AddOperand(acquire), mark), broken is "unmarked" or "raw" ? broken : string.Empty);
        Assign(second, MirRvalue.Acquire(body.AddOperand(MirOperand.PairAcquire(1, b)), MirRefKind.TwoPhaseUniq), string.Empty);
        var projected = broken == "projection" ? body.Project(tuple, MirProjectionKind.Field, 0) : first;
        Assign(projected, MirRvalue.Acquire(body.AddOperand(acquire), MirRefKind.Uniq), "projection");
        var elements = AddOperands(body, [broken == "aggregate" ? acquire : MirOperand.Move(first), one]);
        Assign(tuple, MirRvalue.Aggregate(MirAggregateKind.Tuple, elements, 2, body.TypeOf(tuple)!), "aggregate");
        Assign(number, MirRvalue.Use(body.AddOperand(one)) with { Op = (byte)(broken == "constant" ? MirRefKind.Uniq : MirRefKind.Shared) }, "constant");
        var passed = AddOperands(body, [broken == "argument" ? acquire : MirOperand.Move(first), MirOperand.Move(second)]);
        body.Terminate(block, MirTerminator.Call(new(MirCalleeKind.Fn, function.SymbolOf()), passed, 2, new(0), next, Where("argument")));
        body.Terminate(next, MirTerminator.Return(At));
        Assert.Equal(broken is null ? [] : [(MirViolation.Operation, Broken.Syntax)], Validate(body, c.Types).Select(static x => (x.Rule, x.Subject)));
    }

    // G154 (K 4.8): MIR holds a function item constant only as the operand of an Assign's Use, where the collector finds its edge
    // (InstanceCollector.IsSite); the built body is valid, and the constant moved into the Value call that calls the item or into the Tuple
    // that holds it is an Operation finding there.
    [Theory]
    [InlineData(null)]
    [InlineData("callee")]
    [InlineData("aggregate")]
    public void AFunctionItemConstantIsTheOperandOfAUse(string? broken)
    {
        var c = MirBuildTest.Compile("func g(a: i32) -> i32 => a\nfunc f() -> i32\n    let pair = (g, 1)\n    let item = g\n    return item(1)\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var body = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "f");
        var item = MirOperand.Const(Array.FindIndex(body.Consts.ToArray(), static x => x.Kind == MirConstKind.FnItem));
        var call = Array.FindIndex(body.Blocks.ToArray(), static x => x.Terminator is { Kind: MirTerminatorKind.Call, Callee.Kind: MirCalleeKind.Value });
        var tuple = body.Statements.ToArray().Single(static x => x.Rvalue.Kind == MirRvalueKind.Aggregate);
        if (broken == "callee")
        {
            Unsafe.AsRef(in body.Blocks[call]).Terminator = body.Blocks[call].Terminator with { Operand = item };
        }
        else if (broken == "aggregate")
        {
            Unsafe.AsRef(in body.Operands[tuple.Rvalue.Start]) = item;
        }

        var subject = broken == "callee" ? body.Blocks[call].Terminator.Source.Syntax : tuple.Source.Syntax;
        Assert.Equal(broken is null ? [] : [(MirViolation.Operation, subject)], Validate(body, c.Types).Select(static x => (x.Rule, x.Subject)));
    }

    // DECISIONS R5-M166: a tuple operator's call names its Contract's own requirement under a ComparisonMode, through that Contract, on a
    // composite Self whose comparison composes; the built call is valid, and a mode on a function that is no such requirement, a Reversed
    // equals, a Contract reference, a scalar Self or a mode past the vocabulary is a Callee finding at the call, not an exception.
    [Theory]
    [InlineData(null)]
    [InlineData("function")]
    [InlineData("reversed")]
    [InlineData("contract")]
    [InlineData("scalar")]
    [InlineData("mode")]
    public void AComparisonModeNamesItsContractsOwnRequirement(string? broken)
    {
        var c = MirBuildTest.Compile("func f(a: (i32, i32), b: (i32, i32)) -> bool => a == b\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var body = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "f");
        var call = Array.FindIndex(body.Blocks.ToArray(), static x => x.Terminator is { Kind: MirTerminatorKind.Call, Callee.Arg: (int)ComparisonMode.Operator });
        var callee = body.Blocks[call].Terminator.Callee;
        var equatable = c.Library.GetSymbol(KimiDeclarationId.Equatable)!;
        callee = broken switch
        {
            "function" => callee with { Symbol = body.Key.Decl },
            "reversed" => callee with { Arg = (int)ComparisonMode.Reversed },
            "contract" => callee with { Contract = equatable },
            "scalar" => callee with { Subst = c.Types.Synthesized(callee.Symbol!, BoundType.I32, equatable.Type!) },
            "mode" => callee with { Arg = 9 },
            _ => callee,
        };
        Unsafe.AsRef(in body.Blocks[call]).Terminator = body.Blocks[call].Terminator with { Callee = callee };
        var findings = new List<MirFinding>();
        new MirValidator(c.Types).Validate(body, findings);
        Assert.Equal(
            broken is null ? [] : [(MirViolation.Callee, body.Blocks[call].Terminator.Source.Syntax)],
            findings.Select(static x => (x.Rule, x.Subject)));
    }

    // F10 (plan 5.6.6, DECISIONS R5-M71): a staticEnsure names its static by an Integer constant indexing the calling body's statics, the
    // form MirProgram.StaticInit decodes; the built body is valid, and a slot past the statics, a Bool constant or a local's Copy as the
    // operand is a StaticSlot finding at the access, which then names no static, so the static's read is an Unensured one (G191).
    [Theory]
    [InlineData(null)]
    [InlineData("range")]
    [InlineData("kind")]
    [InlineData("place")]
    public void AStaticEnsureNamesItsStaticsSlot(string? broken)
    {
        var c = MirBuildTest.Compile("group Registry\n    public let total: i32 = 5\nfunc use() -> i32 => Registry.total\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var body = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "use");
        var block = Enumerable.Range(0, body.Blocks.Length).Single(b => body.Blocks[b].Terminator is
            { Kind: MirTerminatorKind.Call, Callee: { Kind: MirCalleeKind.Intrinsic, Arg: (int)IntrinsicId.StaticEnsure } });
        var ensure = body.Blocks[block].Terminator;
        Assert.NotNull(program.StaticInit(body, body.Operands[ensure.Start]));
        var operand = broken switch
        {
            "range" => MirOperand.Const(body.Const(new(MirConstKind.Integer, BoundType.ISize, body.Statics.Length))),
            "kind" => MirOperand.Const(body.Const(new(MirConstKind.Bool, BoundType.Boolean, 0))),
            "place" => MirOperand.Copy(ensure.Place),
            _ => body.Operands[ensure.Start],
        };
        body.Terminate(block, ensure with { Start = body.AddOperand(operand) });
        var read = body.Subject(body.Statements.ToArray().Single(static x => x.Kind == MirStatementKind.Assign && x.Place.Root == 0).Source);
        Assert.Equal(
            broken is null ? [] : [(MirViolation.StaticSlot, body.Subject(ensure.Source)), (MirViolation.Unensured, read)],
            Validate(body, c.Types).Select(static x => (x.Rule, x.Subject)));
    }

    // G191 (K package 4.5 row 7, DECISIONS R4-M121): the builder builds a static's Place after its staticEnsure (MirBuilder.Places), so each
    // static root of a Place a location accesses is named by a staticEnsure of the body: the built body is valid, and with its ensure turned
    // into a Goto its read is an Unensured finding at the access. An Opaque site's root names a static the replaced syntax may access, with
    // no staticEnsure (here a call Binding rejects), and is valid.
    [Theory]
    [InlineData(null)]
    [InlineData("goto")]
    [InlineData("opaque")]
    public void AStaticPlaceIsEnsuredByItsBody(string? form)
    {
        var c = MirBuildTest.Compile("group Registry\n    public let total: i32 = 5\nfunc use() -> i32 => " + (form == "opaque" ? "missing(Registry.total)\n" : "Registry.total\n"));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var body = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "use");
        var block = Array.FindIndex(body.Blocks.ToArray(), static x => x.Terminator is
            { Kind: MirTerminatorKind.Call, Callee: { Kind: MirCalleeKind.Intrinsic, Arg: (int)IntrinsicId.StaticEnsure } });
        Assert.Equal(form == "opaque", block < 0);
        Assert.Equal(form == "opaque", body.OpaqueRoots.ToArray().Contains(-1));
        if (form == "goto")
        {
            var ensure = body.Blocks[block].Terminator;
            body.Terminate(block, MirTerminator.Goto(ensure.Target, ensure.Source));
        }

        var read = body.Statements.ToArray().Where(static x => x.Kind == MirStatementKind.Assign && x.Place.Root == 0).Select(x => body.Subject(x.Source));
        Assert.Equal(form == "goto" ? read.Select(static x => (MirViolation.Unensured, x)) : [], Validate(body, c.Types).Select(static x => (x.Rule, x.Subject)));
    }

    // G191 rule (3) (SPEC 22.4.3, DECISIONS R4-M121 as amended by U-C): on every runtime path from the entry a staticEnsure of its static
    // precedes each access. The built bodies are valid, and so is a loop's access whose only ensure is before the loop ("loop": the in-loop
    // ensure turned into a Goto). With one branch's ensure turned into a Goto ("branch", the other branch's still naming the static, rule
    // (2)), or a loop's first ensure, whose access the loop's second ensure reaches only along the back edge ("latch"), the access it
    // served is an Unensured finding at that access (fail-before: none).
    [Theory]
    [InlineData(null)]
    [InlineData("branch")]
    [InlineData("loop")]
    [InlineData("latch")]
    public void AStaticEnsurePrecedesEachAccessOnEveryPath(string? form)
    {
        var source = form switch
        {
            "loop" => "func use(n: i32) -> i32\n    var s = Registry.total\n    while s < n\n        s = s + Registry.total\n    return s\n",
            "latch" => "func use(n: i32) -> i32\n    var s = 0\n    while s < n\n        s = s + Registry.total\n" +
                "        s = s + Registry.total\n    return s\n",
            _ => Branches,
        };
        var c = MirBuildTest.Compile(Registry + source);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var body = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "use");
        var ensures = Enumerable.Range(0, body.Blocks.Length).Where(b => body.Blocks[b].Terminator is
            { Kind: MirTerminatorKind.Call, Callee: { Kind: MirCalleeKind.Intrinsic, Arg: (int)IntrinsicId.StaticEnsure } }).ToArray();
        Assert.Equal(2, ensures.Length);
        var expected = new List<(MirViolation, SourceRef)>();
        if (form is not null)
        {
            var (turned, served) = (ensures[form == "loop" ? 1 : 0], body.Blocks[ensures[form == "loop" ? 1 : 0]].Terminator.Target);
            body.Terminate(turned, MirTerminator.Goto(served, body.Blocks[turned].Terminator.Source));
            var block = body.Blocks[served]; // The access the turned ensure served, the first of its target to read the static.
            var at = Enumerable.Range(0, block.Count + 1).First(i => Uses(body, served, i).Any(static x => x.StartsWith("-1 ", StringComparison.Ordinal)));
            var access = at < block.Count ? body.Statements[block.Start + at].Source : block.Terminator.Source;
            expected.AddRange(form == "loop" ? [] : [(MirViolation.Unensured, body.Subject(access))]);
        }

        Assert.Equal(expected, Validate(body, c.Types).Select(static x => (x.Rule, x.Subject)));
    }

    // G191 rule (3)'s cost: a body of N groups, each a branch and a nested loop reading two statics, grows its blocks linearly with N, and
    // the ensure solve's work (MirValidator.Work: the bit words its runtime edges meet, over passes the loop depth bounds) with them.
    [Fact]
    public void TheEnsureSolveScalesLinearly()
    {
        var (sizes, blocks, work) = (new[] { 16, 32, 64 }, new long[3], new long[3]);
        for (var i = 0; i < sizes.Length; i++)
        {
            var text = new StringBuilder("group Registry\n    public let a: i32 = 5\n    public let b: i32 = 7\n");
            text.Append("func use(c: bool, n: i32) -> i32\n    var s = 0\n");
            for (var k = 0; k < sizes[i]; k++)
            {
                text.Append("    if c => s += Registry.a\n    while s < n\n        while s < n\n            s += Registry.b\n");
            }

            var c = MirBuildTest.Compile(text.Append("    return s\n").ToString());
            var program = c.BuildMir();
            Assert.Empty(program.Findings);
            var (validator, findings) = (new MirValidator(c.Types), new List<MirFinding>());
            var body = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "use");
            validator.Validate(body, findings);
            Assert.Empty(findings);
            (blocks[i], work[i]) = (body.Blocks.Length, validator.Work);
        }

        output.WriteLine($"N {string.Join(", ", sizes)}: blocks {string.Join(", ", blocks)}, solve work {string.Join(", ", work)}");
        Assert.True(blocks[2] - blocks[1] <= (2 * (blocks[1] - blocks[0])) + 8, $"blocks {string.Join(", ", blocks)}");
        Assert.True(work[0] > 0 && work[2] - work[1] <= (2 * (work[1] - work[0])) + 8, $"solve work {string.Join(", ", work)}");
    }

    // G191 (K package 4.5 row 7, DECISIONS R4-M121): the builder ensures only the statics whose initializers it builds, so over the program
    // each staticEnsure names a static whose StaticInit body the program has: the built program is valid, and with the ensure pointed at a
    // function's symbol (Binding never gives one) the program has a StaticSlot finding at the access, which marks the ensuring body
    // (MirBody.HasFinding; fail-before: no verdict).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnEnsuredStaticHasItsInitializer(bool broken)
    {
        var c = MirBuildTest.Compile("group Registry\n    public let total: i32 = 5\nfunc plain() -> i32 => 1\nfunc use() -> i32 => Registry.total\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var use = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "use");
        var plain = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "plain").Key.Decl!;
        var block = Enumerable.Range(0, use.Blocks.Length).Single(b => use.Blocks[b].Terminator is
            { Kind: MirTerminatorKind.Call, Callee: { Kind: MirCalleeKind.Intrinsic, Arg: (int)IntrinsicId.StaticEnsure } });
        if (broken)
        {
            var root = use.Static(new(plain, BoundType.Unit, false, false)).Root;
            var operand = use.AddOperand(MirOperand.Const(use.Const(new(MirConstKind.Integer, BoundType.ISize, ~root))));
            use.Terminate(block, use.Blocks[block].Terminator with { Start = operand });
        }

        new MirValidator(c.Types).Validate(program);
        Assert.Equal(broken ? [(MirViolation.StaticSlot, "Registry.total")] : [], program.Findings.Select(x => (x.Rule, c.SyntaxOf(x.Subject).ToString())));
        Assert.Equal(broken, use.HasFinding); // The ensuring body's verdict (DECISIONS R5-M183).
    }

    // G191 N2 (DECISIONS R4-M121 as amended): over the program, each default template's call and each created Closure has its body, which
    // the instance collector reads unchecked: the built program is valid, and a call of a function without defaults with its Arg set to
    // 1 (a template call) or an Assign of a Closure aggregate over a symbol without a Closure body is a Callee finding at the call or the
    // statement, which marks its body (fail-before: no finding).
    [Theory]
    [InlineData(null)]
    [InlineData("template")]
    [InlineData("closure")]
    public void ANamedBodyIsInItsProgram(string? broken)
    {
        var c = MirBuildTest.Compile("func plain() -> i32 => 1\nfunc use() -> i32 => plain() + 1\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var use = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "use");
        var plain = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "plain").Key.Decl!;
        var call = Array.FindIndex(use.Blocks.ToArray(), static x => x.Terminator is { Kind: MirTerminatorKind.Call, Callee.Kind: MirCalleeKind.Fn });
        var assign = Array.FindIndex(use.Statements.ToArray(), static x => x.Kind == MirStatementKind.Assign);
        SourceInfo at = default;
        if (broken == "template")
        {
            at = use.Blocks[call].Terminator.Source;
            Unsafe.AsRef(in use.Blocks[call]).Terminator = use.Blocks[call].Terminator with { Callee = use.Blocks[call].Terminator.Callee with { Arg = 1 } };
        }
        else if (broken == "closure")
        {
            at = use.Statements[assign].Source;
            var closure = c.Types.Intern(BoundTypeKind.Closure, plain, SemanticsKind.Owner, []);
            Unsafe.AsRef(in use.Statements[assign]) = use.Statements[assign] with { Rvalue = new(MirRvalueKind.Aggregate, (byte)MirAggregateKind.Closure, Type: closure) };
        }

        new MirValidator(c.Types).Validate(program);
        Assert.Equal(broken is null ? [] : [(MirViolation.Callee, use.Subject(at))], program.Findings.Select(static x => (x.Rule, x.Subject)));
        Assert.Equal(broken is not null, use.HasFinding);
    }

    // R3-10 (PLAN G157 (37)): an owning interpolation's steps carry their constants, which code generation reads unchecked, each of its
    // position's kind (the literal bytes an Integer, the values' Types a token of their Tuple, a segment's text a string): the built bodies
    // are valid, a nested interpolation's included; a step's constant of another kind or an i32 token is a Type finding at the step
    // (fail-before: none for another kind). FormatFinish takes only its adapter since R8-W2 a2 (R5-M131's pairing went with the stack form).
    [Theory]
    [InlineData(null, false)]
    [InlineData(null, true)]
    [InlineData("text", false)]
    [InlineData("later", false)]
    [InlineData("segment", false)]
    [InlineData("buffer", false)]
    public void AnInterpolationsStepsCarryItsEstimate(string? broken, bool nested)
    {
        var c = MirBuildTest.Compile(nested ? "func f(n: i32) -> string => \"a\\(\"b\\(n)\")c\"\n" : Interpolation);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var body = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "f");
        var (id, at) = broken switch
        {
            "text" => (IntrinsicId.FormatSegment, 1),
            "later" => (IntrinsicId.FormatSegment, 2),
            "segment" => (IntrinsicId.FormatSegment, 3),
            _ => (IntrinsicId.FormatBuffer, 1),
        };
        MirTerminator[] Steps(IntrinsicId step) => [.. body.Blocks.ToArray().Select(static x => x.Terminator)
            .Where(x => x.Callee is { Kind: MirCalleeKind.Intrinsic } callee && callee.Arg == (int)step)];
        var (steps, finishes) = (Steps(id), Steps(IntrinsicId.FormatFinish));
        Assert.Equal(nested ? 2 : 1, finishes.Length);
        Assert.Equal(id == IntrinsicId.FormatSegment ? 3 : finishes.Length, steps.Length);
        var step = body.Subject(steps[0].Source);
        if (broken is not null)
        {
            MirConst constant = broken switch
            {
                "text" => new(MirConstKind.Integer, BoundType.ISize, 0),
                "later" => new(MirConstKind.Bool, BoundType.Boolean, 1),
                _ => new(MirConstKind.TypeToken, BoundType.I32),
            };
            Unsafe.AsRef(in body.Operands[steps[0].Start + at]) = MirOperand.Const(body.Const(constant));
        }

        (MirViolation, SourceRef)[] expected = broken is null ? [] : [(MirViolation.Type, step)];
        var findings = new List<MirFinding>();
        new MirValidator(c.Types).Validate(body, findings);
        Assert.Equal(expected, findings.Select(static x => (x.Rule, x.Subject)));
    }

    // SPEC 22.7, rule L (R8-W2 a2): a generic direct line's region is a fixed array whose length is the interpolation-bound term, which its
    // instance decides; the body is valid, its Len a sequence's.
    [Fact]
    public void ALenOverAnInterpolationBoundRegionIsASequences()
    {
        var c = MirBuildTest.Compile("func f<T>(v: ref/T)\n    T is Utf8Format\n    Console.writeLine(\"v=\\(v)\")\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var body = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "f");
        var len = Assert.Single(body.Statements.ToArray(), static x => x is { Kind: MirStatementKind.Assign, Rvalue.Kind: MirRvalueKind.Len });
        var region = body.TypeOf(len.Rvalue.Place)!;
        Assert.Equal((BoundTypeKind.FixedArray, LengthOperation.InterpolationBound), (region.Kind, region.LengthExpression!.Operation));
        var findings = new List<MirFinding>();
        new MirValidator(c.Types).Validate(body, findings);
        Assert.Empty(findings);
    }

    // G136 (DECISIONS R5-M72): A5 reads the Place a Ref lends into a Call's input temp flow-insensitively (MirBorrows.lent), so each body
    // of an if, a match or try and a do builds its value for an input that reserves (MirCases.Reserves) in a temp of its own, moved into
    // the input temp, as an exit's secured value is: the golden's program is valid (a match, an if and a destroying replace; a do of exits
    // and a do of an if; a labeled if's yield; a try; a receiver; a Value callee), and with each move into chosen's input temps replaced by
    // the Ref it moves (the earlier shape) each input temp is a Lent finding at it. A shared input does not reserve, so its two Refs are
    // not checked.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnInputThatReservesHasOneWriteWhereARefWritesIt(bool direct)
    {
        var c = MirBuildTest.Compile(File.ReadAllText(DiagnosticCorpus.RepositoryPath("tests", "mir", "calls.kimi")));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var bodies = program.Bodies.ToArray();
        var shared = bodies.Single(static x => x.Key.Decl?.Name == "shared");
        Assert.Equal(2, shared.Statements.ToArray().Count(static x => x.Rvalue is { Kind: MirRvalueKind.Ref, Op: (byte)MirRefKind.Shared }));
        var body = bodies.Single(static x => x.Key.Decl?.Name == "chosen");
        var statements = body.Statements.ToArray();
        var refs = statements.Where(static x => x.Rvalue.Kind == MirRvalueKind.Ref).ToDictionary(static x => x.Place.Root, static x => x.Rvalue);
        var inputs = new List<SourceRef>();
        for (var i = 0; i < statements.Length; i++)
        {
            if (statements[i] is { Kind: MirStatementKind.Assign, Rvalue: { Kind: MirRvalueKind.Use } use } statement &&
                body.Operands[use.Start] is { Kind: MirOperandKind.Move, Place.Projection: 0 } moved && refs.TryGetValue(moved.Place.Root, out var lent))
            {
                inputs.Add(body.Subject(body.Locals[statement.Place.Root].Source));
                Unsafe.AsRef(in body.Statements[i]) = direct ? statement with { Rvalue = lent } : statement;
            }
        }

        Assert.Equal(6, inputs.Count); // Two bodies for each of three calls.
        Assert.Equal(direct ? inputs.Distinct().Select(static x => (MirViolation.Lent, x)) : [], Validate(body, c.Types).Select(static x => (x.Rule, x.Subject)));
    }

    // R5-M72 (its first review's B1): an input that does not reserve, an obj/Base or an objref/Base, is not checked, though a conditional's
    // Adapts of a pair layer write it twice; the objref/Base program's only records are Binding's.
    [Theory]
    [InlineData("obj")]
    [InlineData("objref")]
    public void AnInputThatDoesNotReserveIsNotChecked(string semantics)
    {
        var c = MirBuildTest.Compile("open struct Base\n    public let value: i32 = 9\nstruct Derived: Base\n    public init() => ()\n" +
            $"func own(x: {semantics}/Base) -> () => ()\nfunc probe<s/T>(flag: bool, a: s/Derived, b: s/Derived) -> ()\n    s is obj\n" +
            $"    own(if flag => a@move@{semantics}/Base else => b@move@{semantics}/Base)\n");
        Assert.Equal(semantics == "obj" ? [] : [Kimi.DiagnosticCode.MissingOriginBinding_Kd], c.Binding.Issues.Select(static x => x.Code).Distinct());
        var program = c.BuildMir();
        var body = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "probe");
        Assert.Equal(2, body.Statements.ToArray().Count(static x => x.Rvalue.Kind == MirRvalueKind.Adapt));
        Assert.Empty(program.Findings);
    }

    // R5-M72: a default whose template Binding rejected (here a call beyond the case bound, OwnershipCaseLimit_Kd) is an Opaque value over
    // the prepared slots before it, whose writes are derived, so the slot an exclusive Ref writes and the call takes is not checked.
    [Fact]
    public void AnOpaqueSitesRootIsNotALentFinding()
    {
        var binders = string.Concat("abcdegh".Select(static x => $"    {x} is owner or uniq\n"));
        var c = MirBuildTest.Compile("func f<a/A, b/B, c/C, d/D, e/E, g/G, h/H>(pa: a/A, pb: b/B, pc: c/C, pd: d/D, pe: e/E, pg: g/G, ph: h/H, " +
            "k: i32 = 1) -> i32\n" + binders + "    return k\nfunc main()\n    var n: i32 = 1\n    _ = f(n, n@uniq, n, n, n, n, n)\n");
        Assert.Contains(c.Binding.Issues, static x => x.Code == Kimi.DiagnosticCode.OwnershipCaseLimit_Kd);
        var program = c.BuildMir();
        var body = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "main");
        var slot = body.Statements.ToArray().Single(static x => x.Rvalue is { Kind: MirRvalueKind.Ref, Op: (byte)MirRefKind.TwoPhaseUniq }).Place.Root;
        Assert.Contains(slot, body.OpaqueRoots.ToArray());
        Assert.Empty(program.Findings);
    }

    // W1 rule 1 (DECISIONS R5-M123; SPEC 3.3.2, 4.8.1, 13.5.5): a projected Place that a location writes or destroys in place passes no shared
    // layer after its last raw Deref (a Deref of a ref, objref, rc or arc Type, an Index of a Slice, or a PairLayer whose binder admits a
    // shared Semantics): else a SharedPath finding at the location, here at Broken. None for an exclusive or owning layer, a fixed array's
    // element, a raw Place (5.2.1 (3)), a Move (A1's Move Path rule), a shared borrow or an address, the Deref directly on a PreparedInput
    // (the caller's slot itself; a shared layer past it is a finding), a location in an inline default copy (its template's check), and a
    // location whose scope cannot be walked (its Scope finding alone).
    [Theory]
    [InlineData("ref", true)]
    [InlineData("ref uniq", true)]
    [InlineData("uniq ref", true)]
    [InlineData("raw ref", true)]
    [InlineData("objref", true)]
    [InlineData("rc", true)]
    [InlineData("arc", true)]
    [InlineData("slice", true)]
    [InlineData("slice drop", true)]
    [InlineData("ref drop", true)]
    [InlineData("call", true)]
    [InlineData("pair ref", true)]
    [InlineData("slot ref", true)]
    [InlineData("uniq", false)]
    [InlineData("objuniq", false)]
    [InlineData("obj", false)]
    [InlineData("array", false)]
    [InlineData("ref raw", false)]
    [InlineData("pair uniq", false)]
    [InlineData("move", false)]
    [InlineData("shared borrow", false)]
    [InlineData("address", false)]
    [InlineData("slot", false)]
    [InlineData("inlined", false)]
    [InlineData("unscoped", false)]
    public void AWriteOrDestructionPastASharedLayerIsAFinding(string form, bool finding)
    {
        var (c, types) = Fixture.Value;
        var body = MirModelTest.NewBody(types);
        BoundType Layer(SemanticsKind semantics, BoundType referent) => semantics is SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc
            ? c.Types.Intern(BoundTypeKind.Semantics, null, semantics, [referent]) : c.Types.Reference(semantics, referent);
        var tuple = c.Types.Tuple([BoundType.I32, BoundType.Boolean]);
        var prepared = new MirPlace(body.AddLocal(new(
            Layer(SemanticsKind.Ref, form == "slot ref" ? Layer(SemanticsKind.Ref, tuple) : BoundType.I32), MirLocalKind.Arg, At, Role: MirLocalRole.PreparedInput)));
        PairBinder.Collect(types.Generic, body.Binders); // Its binder admits owner or uniq.
        if (form == "pair ref")
        {
            body.Binders[0] = body.Binders[0] with { Admitted = SemanticsMask.Ref | SemanticsMask.Uniq };
        }

        MirPlace Local(BoundType type) => MirModelTest.Local(body, type);
        MirPlace Deref(MirPlace place) => body.Project(place, MirProjectionKind.Deref, 0);
        var index = MirModelTest.Local(body, BoundType.ISize, MirLocalKind.Temp);
        var reference = Local(Layer(SemanticsKind.Ref, BoundType.I32));
        var one = MirOperand.Const(body.Const(new(MirConstKind.Integer, BoundType.I32, 1)));
        var (entry, exit) = (body.AddBlock(), body.AddBlock());
        var at = form switch
        {
            "inlined" => Broken with { Scope = (ushort)body.AddScope(0, MirScopeKind.Inlined, default) },
            "unscoped" => Broken with { Scope = 9 },
            _ => Broken,
        };
        MirPlace Payload(SemanticsKind semantics) => body.Project(Deref(Local(Layer(semantics, types.Point))), MirProjectionKind.Field, 0); // x
        var place = form switch
        {
            "ref" or "ref drop" or "call" or "move" or "shared borrow" or "address" or "inlined" or "unscoped" => Deref(reference),
            "ref uniq" => Deref(Deref(Local(Layer(SemanticsKind.Ref, Layer(SemanticsKind.Uniq, BoundType.I32))))),
            "uniq ref" => Deref(Deref(Local(Layer(SemanticsKind.Uniq, Layer(SemanticsKind.Ref, BoundType.I32))))),
            "raw ref" => Deref(Deref(Local(c.Types.Pointer(Layer(SemanticsKind.Ref, BoundType.I32))))),
            "objref" => Payload(SemanticsKind.ObjRef),
            "rc" => Payload(SemanticsKind.Rc),
            "arc" => Payload(SemanticsKind.Arc),
            "objuniq" => Payload(SemanticsKind.ObjUniq),
            "obj" => Payload(SemanticsKind.Obj),
            "slice" or "slice drop" => body.Project(Local(c.Types.Intern(BoundTypeKind.Slice, null, SemanticsKind.Owner, [BoundType.I32])), MirProjectionKind.Index, index.Root),
            "array" => body.Project(Local(types.Array), MirProjectionKind.Index, index.Root),
            "uniq" => Deref(Local(Layer(SemanticsKind.Uniq, BoundType.I32))),
            "ref raw" => Deref(body.Project(Deref(Local(Layer(SemanticsKind.Ref, c.Types.Tuple([c.Types.Pointer(BoundType.I32), BoundType.Boolean])))), MirProjectionKind.Field, 0)),
            "pair ref" or "pair uniq" => body.Project(Local(types.Pair), MirProjectionKind.PairLayer, 0),
            "slot" => Deref(prepared),
            _ => body.Project(Deref(Deref(prepared)), MirProjectionKind.Field, 0), // slot ref: the parameter's own shared layer past the slot.
        };
        var value = form.StartsWith("pair", StringComparison.Ordinal) ? MirOperand.Move(Local(body.TypeOf(place)!)) : one; // A payload f's premises do not make Copy (BU-88).
        var rvalue = form switch
        {
            "shared borrow" => MirRvalue.Ref(MirRefKind.Shared, place, body.TypeOf(reference)!),
            "address" => MirRvalue.Ref(MirRefKind.Raw, place, c.Types.Pointer(BoundType.I32)),
            "move" => MirRvalue.Use(AddOperands(body, [MirOperand.Move(place)])),
            _ => MirRvalue.Use(AddOperands(body, [value])),
        };
        if (form.EndsWith("drop", StringComparison.Ordinal))
        {
            body.Terminate(entry, MirTerminator.Drop(place, exit, at));
        }
        else if (form == "call")
        {
            // A returning Call's destination: f() -> i32.
            body.Terminate(entry, MirTerminator.Call(new(MirCalleeKind.Fn, types.Function), body.Operands.Length, 0, place, exit, at));
        }
        else
        {
            var destination = form is "move" or "shared borrow" or "address" ? Local(rvalue.Kind == MirRvalueKind.Use ? BoundType.I32 : rvalue.Type!) : place;
            body.Push(entry, MirStatement.Assign(destination, rvalue, form is "move" or "shared borrow" or "address" ? Broken : at));
            body.Terminate(entry, MirTerminator.Goto(exit, At));
        }

        body.Terminate(exit, MirTerminator.Return(At));
        MirViolation[] expected = finding ? [MirViolation.SharedPath] : form == "unscoped" ? [MirViolation.Scope] : [];
        Assert.Equal(expected.Select(static x => (x, Broken.Syntax)), Validate(body).Select(static x => (x.Rule, x.Subject)));
    }

    // DECISIONS BU-88 (Q8-3): a Copy operand's Type is Copy by Binding's proof under the body definition's premises (TyCtxt.Copies): a Use's
    // Copy of a Point, which is not Copy, is an Operation finding at its syntax (fail-before: none), its twin's of an i32 none; the case
    // sites' operands are exempt: a PairAcquire's and a per-view Adapt's Copy of the pair's payload, which f's premises do not prove Copy.
    [Theory]
    [InlineData("owned")]
    [InlineData("copied")]
    [InlineData("acquired")]
    [InlineData("adapted")]
    public void ACopyOperandsTypeIsCopy(string form)
    {
        var (c, types) = Fixture.Value;
        var body = MirModelTest.NewBody(types);
        var pair = MirModelTest.Local(body, types.Pair, MirLocalKind.Arg);
        PairBinder.Collect(types.Generic, body.Binders);
        var layer = body.Project(pair, MirProjectionKind.PairLayer, 0);
        var copied = form == "owned" ? MirModelTest.Local(body, types.Point) : MirModelTest.Local(body, BoundType.I32);
        var (entry, exit) = (body.AddBlock(), body.AddBlock());
        var token = MirOperand.Const(body.Const(new(MirConstKind.TypeToken, body.TypeOf(layer)!)));
        var (place, rvalue) = form switch
        {
            "acquired" => (pair, MirRvalue.Acquire(AddOperands(body, [MirOperand.PairAcquire(0, pair)]), MirRefKind.Uniq)),
            "adapted" => (pair, MirRvalue.Adapt(AddOperands(body, [MirOperand.Copy(layer), token]), layer, types.Pair, shorthand: true, input: true)),
            _ => (MirModelTest.Local(body, body.TypeOf(copied)!), MirRvalue.Use(AddOperands(body, [MirOperand.Copy(copied)]))),
        };
        body.Push(entry, MirStatement.Assign(place, rvalue, Broken));
        body.Terminate(entry, MirTerminator.Goto(exit, At));
        body.Terminate(exit, MirTerminator.Return(At));
        Assert.False(c.Types.Copies(body.Key.Decl).Copy(types.Point) || c.Types.Copies(body.Key.Decl).Copy(body.TypeOf(layer)!)); // Under f's premises.
        MirViolation[] expected = form == "owned" ? [MirViolation.Operation] : [];
        Assert.Equal(expected.Select(static x => (x, Broken.Syntax)), Validate(body).Select(static x => (x.Rule, x.Subject)));
    }

    // W1 rule 1: a UniqSlice's element is reached past its exclusive view, so its write is no SharedPath finding (SPEC 4.8.2).
    [Fact]
    public void AUniqSlicesElementIsWrittenPastNoSharedLayer()
    {
        var program = MirBuildTest.Compile("func set(values: uniq/[2 of i32])\n    var view = values.sliceUniq()\n    view[0] = 5\n").BuildMir();
        var body = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "set");
        Assert.Contains(body.Statements.ToArray(), x => x.Kind == MirStatementKind.Assign && body.Projections[x.Place.Projection].Kind == MirProjectionKind.Deref);
        Assert.Empty(program.Findings);
    }

    // W1 rule 1, stage B (DECISIONS R5-M139 with its (b2) line; SPEC 3.5, 3.5.1, 8.15.2, 13.5.5): a Place a Ref borrows exclusively (Uniq
    // or TwoPhaseUniq), a PairAcquire's Place under each view in which it Reborrows (MirCases.Reborrows: its case Type is exclusive), and
    // the Place of an exclusive borrow an Adapt resolves to under a view pass no shared layer after their last raw Deref: else one
    // SharedPath finding at the location, here at Broken, whatever the layer (a ref, objref, rc or arc Deref, a Slice's Index, a pair layer
    // in a shared case). A PairAcquire's and an Adapt's pair layers are read in the view's cases, so the own pair layer is no shared layer;
    // a PairAcquire whose case Types are no exclusive reference only Copies, and one over `uniq/i32` with `s is owner or ref` Reborrows in
    // the owner case. None for a Ref through a raw pointer past a shared layer (SPEC 5.2.1 (3)); a shared
    // borrow and an address are the write theory's rows.
    [Theory]
    [InlineData("uniq ref", true)]
    [InlineData("two-phase ref", true)]
    [InlineData("uniq objref", true)]
    [InlineData("uniq rc", true)]
    [InlineData("uniq arc", true)]
    [InlineData("uniq slice", true)]
    [InlineData("uniq pair", true)]
    [InlineData("acquire ref", true)]
    [InlineData("acquire target past ref", true)]
    [InlineData("adapt ref", true)]
    [InlineData("uniq raw", false)]
    [InlineData("acquire own", false)]
    [InlineData("acquire copy", false)]
    [InlineData("acquire target", false)]
    [InlineData("adapt own", false)]
    public void AnExclusiveAcquisitionPastASharedLayerIsAFinding(string form, bool finding)
    {
        var (c, types) = Fixture.Value;
        var body = MirModelTest.NewBody(types);
        PairBinder.Collect(types.Generic, body.Binders);
        var admitted = form switch
        {
            "acquire copy" => SemanticsMask.Ref,
            "acquire target" or "acquire target past ref" => SemanticsMask.Owner | SemanticsMask.Ref,
            _ => SemanticsMask.Ref | SemanticsMask.Uniq,
        };
        body.Binders[0] = body.Binders[0] with { Admitted = admitted };
        _ = TyCtxt.Layer(types.Pair, out var payload, out var binder); // s/T: T and s.
        var exclusive = c.Types.Reference(SemanticsKind.Uniq, BoundType.I32);
        var target = c.Types.Intern(BoundTypeKind.SemanticsApplication, binder, SemanticsKind.Parameter, [exclusive]); // s/(uniq/i32)
        MirPlace Local(BoundType type) => MirModelTest.Local(body, type);
        MirPlace Deref(MirPlace place) => body.Project(place, MirProjectionKind.Deref, 0);
        MirPlace Payload(SemanticsKind semantics) => body.Project(
            Deref(Local(semantics == SemanticsKind.ObjRef ? c.Types.Reference(semantics, types.Point) : c.Types.Intern(BoundTypeKind.Semantics, null, semantics, [types.Point]))),
            MirProjectionKind.Field,
            0);
        var index = MirModelTest.Local(body, BoundType.ISize, MirLocalKind.Temp);
        var place = form switch
        {
            "uniq ref" or "two-phase ref" => Deref(Local(c.Types.Reference(SemanticsKind.Ref, BoundType.I32))),
            "uniq objref" => Payload(SemanticsKind.ObjRef),
            "uniq rc" => Payload(SemanticsKind.Rc),
            "uniq arc" => Payload(SemanticsKind.Arc),
            "uniq slice" => body.Project(Local(c.Types.Intern(BoundTypeKind.Slice, null, SemanticsKind.Owner, [BoundType.I32])), MirProjectionKind.Index, index.Root),
            "uniq pair" or "adapt own" => body.Project(Local(types.Pair), MirProjectionKind.PairLayer, 0),
            "uniq raw" => Deref(body.Project(Deref(Local(c.Types.Reference(SemanticsKind.Ref, c.Types.Tuple([c.Types.Pointer(BoundType.I32), BoundType.Boolean])))), MirProjectionKind.Field, 0)),
            "acquire ref" or "acquire copy" => Deref(Local(c.Types.Reference(SemanticsKind.Ref, types.Pair))),
            "acquire target past ref" => Deref(Local(c.Types.Reference(SemanticsKind.Ref, target))),
            "acquire target" => Local(target),
            "acquire own" => body.Project(Local(c.Types.Intern(BoundTypeKind.SemanticsApplication, binder, SemanticsKind.Parameter, [types.Pair])), MirProjectionKind.PairLayer, 0),
            _ => Deref(Local(c.Types.Reference(SemanticsKind.Ref, payload!))), // adapt ref: T of s/T, past ref.
        };
        var type = body.TypeOf(place)!;
        var rvalue = form[..form.IndexOf(' ')] switch
        {
            "acquire" => MirRvalue.Acquire(body.AddOperand(MirOperand.PairAcquire(0, place)), MirRefKind.Uniq),
            "adapt" => MirRvalue.Adapt(AddOperands(body, [MirOperand.Copy(place), MirOperand.Const(body.Const(new(MirConstKind.TypeToken, type)))]), place, types.Pair, shorthand: false, input: false),
            var kind => MirRvalue.Ref(kind == "uniq" ? MirRefKind.Uniq : MirRefKind.TwoPhaseUniq, place, c.Types.Reference(SemanticsKind.Uniq, type)),
        };
        var (entry, exit) = (body.AddBlock(), body.AddBlock());
        body.Push(entry, MirStatement.Assign(Local(rvalue.Type ?? type), rvalue, Broken));
        body.Terminate(entry, MirTerminator.Goto(exit, At));
        body.Terminate(exit, MirTerminator.Return(At));
        MirViolation[] expected = finding ? [MirViolation.SharedPath] : [];
        Assert.Equal(expected.Select(static x => (x, Broken.Syntax)), Validate(body).Select(static x => (x.Rule, x.Subject)));
    }

    // SPEC 13.4, 14.8.1: a comparison's literal has no Place and no destruction, so a string constant at ref/string reaches no other position
    // than an input of a Call that keeps nothing of it.
    [Fact]
    public void AStringConstantAtARefIsNoValue()
    {
        var body = new MirBody();
        body.Reset(new(null, MirBodyKind.Fn), default);
        body.AddLocal(new(BoundType.Unit, MirLocalKind.Return, At));
        var view = Fixture.Value.Compilation.Types.Reference(SemanticsKind.Ref, BoundType.String);
        var (held, block) = (MirModelTest.Local(body, view), body.AddBlock());
        var literal = MirOperand.Const(body.Const(new(MirConstKind.String, view, Value: "a")));
        body.Push(block, MirStatement.Assign(held, MirRvalue.Use(AddOperands(body, [literal])), At));
        body.Terminate(block, MirTerminator.Return(At));
        Assert.Contains((MirViolation.Operation, body.Source), Validate(body).Select(static x => (x.Rule, x.Subject)));
    }

    // SPEC 13.2-13.4, 13.4.1, 5.1, 5.3, 3.1.3, 8.7.4 (PLAN G662; R8-M15's family): an operator rvalue stands only in the operator table's
    // domain (MirTypes.Operates, the whitelist the Backend's Column reads) at its operands' terminals: every comparison at an integer, a
    // wrapping integer, a floating-point Type, char and a Never terminal (below ref or uniq: it has no value), equality alone at bool, Unit
    // and a raw pointer (SPEC 5.1: its address; to a string too, directly or below a pair layer, since the walk stops at a raw layer); + - * /
    // at a numeric Type (+ and - a raw pointer displaced by an isize too), % & | ^ and a shift's left operand at an integer or wrapping
    // integer, unary - at a numeric Type but an unsigned integer (Wrapping<u32>'s stands), not at bool; at an abstract operand, a Type
    // parameter here, each binary operator, on Binding's PrimitiveInteger proof (MIR reads the shape), and unary - at Wrapping over one,
    // but neither unary operator at the parameter itself (a Negatable T's negation is a requirement call; no T is bool by proof). A
    // string's comparison is its library witness's call (R8-S1e-b), a struct's, an enum's and a Tuple's a Contract call, so a BinaryOp at
    // one, as it is, below ref, uniq or pair layers (s/string) or as the right operand after a Never left, is a broken contract, as is an
    // ordering at bool, Unit or a raw pointer, arithmetic at bool, char, Unit, a struct, a Tuple, a string or a raw pointer's product, a
    // remainder, bitwise operator, shift or not at f64, a bitwise operator or shift at bool or char, and unary - at usize, bool or char;
    // one at i32 is not, after a Never left either.
    [Theory]
    [InlineData("string", "==", true)]
    [InlineData("ref", "==", true)]
    [InlineData("uniq", "==", true)]
    [InlineData("pair", "==", true)]
    [InlineData("never ref", "==", true)]
    [InlineData("struct", "==", true)]
    [InlineData("never struct", "==", true)]
    [InlineData("enum", "==", true)]
    [InlineData("tuple", "==", true)]
    [InlineData("unit", "<", true)]
    [InlineData("bool", "<=", true)]
    [InlineData("raw", ">", true)]
    [InlineData("i32", "==", false)]
    [InlineData("never i32", "==", false)]
    [InlineData("raw", "==", false)]
    [InlineData("pair raw", "!=", false)]
    [InlineData("unit", "==", false)]
    [InlineData("bool", "!=", false)]
    [InlineData("char", "<", false)]
    [InlineData("f64", ">=", false)]
    [InlineData("wrapping", ">", false)]
    [InlineData("parameter", "<=", false)]
    [InlineData("refNever", "==", false)]
    [InlineData("refNever", "<", false)]
    [InlineData("uniqNever", "!=", false)]
    [InlineData("bool", "+", true)]
    [InlineData("char", "*", true)]
    [InlineData("unit", "-", true)]
    [InlineData("struct", "+", true)]
    [InlineData("never struct", "/", true)]
    [InlineData("tuple", "+", true)]
    [InlineData("string", "+", true)]
    [InlineData("raw", "*", true)]
    [InlineData("f64", "%", true)]
    [InlineData("bool", "&", true)]
    [InlineData("f64", "|", true)]
    [InlineData("char", "^", true)]
    [InlineData("bool", "<<", true)]
    [InlineData("f64", ">>", true)]
    [InlineData("usize", "neg", true)]
    [InlineData("bool", "neg", true)]
    [InlineData("char", "neg", true)]
    [InlineData("i32", "not", true)]
    [InlineData("f64", "not", true)]
    [InlineData("i32", "+", false)]
    [InlineData("never i32", "*", false)]
    [InlineData("f64", "/", false)]
    [InlineData("wrapping", "%", false)]
    [InlineData("usize", "&", false)]
    [InlineData("wrapping", "<<", false)]
    [InlineData("i32", "neg", false)]
    [InlineData("f64", "neg", false)]
    [InlineData("wrapping", "neg", false)]
    [InlineData("bool", "not", false)]
    [InlineData("parameter", "+", false)]
    [InlineData("parameter", "neg", true)]
    [InlineData("parameter", "not", true)]
    [InlineData("wrapping parameter", "neg", false)]
    [InlineData("wrapping u32", "neg", false)]
    [InlineData("raw", "+", false)]
    [InlineData("raw", "-", false)]
    public void AnOperatorRvalueIsInTheOperatorTablesDomain(string operand, string op, bool reported)
    {
        var body = new MirBody();
        body.Reset(new(null, MirBodyKind.Fn), default);
        body.AddLocal(new(BoundType.Unit, MirLocalKind.Return, At));
        var (c, types) = Fixture.Value;
        _ = TyCtxt.Layer(types.Pair, out var parameter, out var binder); // T and s of s/T.
        var type = operand.Replace("never ", string.Empty, StringComparison.Ordinal) switch
        {
            "string" => BoundType.String,
            "ref" => c.Types.Reference(SemanticsKind.Ref, BoundType.String),
            "uniq" => c.Types.Reference(SemanticsKind.Uniq, BoundType.String),
            "pair" => c.Types.Intern(BoundTypeKind.SemanticsApplication, binder, SemanticsKind.Parameter, [BoundType.String]),
            "raw" => c.Types.Pointer(BoundType.String),
            "pair raw" => c.Types.Intern(BoundTypeKind.SemanticsApplication, binder, SemanticsKind.Parameter, [c.Types.Pointer(BoundType.String)]),
            "struct" => types.Point,
            "enum" => types.Shape,
            "tuple" => c.Types.Tuple([BoundType.I32, BoundType.I32]),
            "unit" => BoundType.Unit,
            "bool" => BoundType.Boolean,
            "char" => BoundType.Char,
            "f64" => BoundType.F64,
            "usize" => BoundType.USize,
            "wrapping" => BoundType.WrappingOf(BoundType.I32),
            "wrapping u32" => BoundType.WrappingOf(BoundType.Primitives["u32"]),
            "wrapping parameter" => c.Types.Intern(
                BoundTypeKind.Constructed, c.Library.GetSymbol(KimiDeclarationId.Wrapping)!, SemanticsKind.Owner, [parameter!]),
            "parameter" => parameter!,
            "refNever" => c.Types.Reference(SemanticsKind.Ref, BoundType.Never),
            "uniqNever" => c.Types.Reference(SemanticsKind.Uniq, BoundType.Never),
            _ => BoundType.I32,
        };
        var left = MirModelTest.Local(body, operand.StartsWith("never", StringComparison.Ordinal) ? BoundType.Never : type);
        var (unary, code) = Op(op);
        var counterpart = code is MirBinaryOp.Shl or MirBinaryOp.Shr ? BoundType.I32 // A shift's count, a raw pointer's displacement.
            : operand == "raw" && code is MirBinaryOp.Add or MirBinaryOp.Sub ? BoundType.ISize : type;
        var right = MirModelTest.Local(body, counterpart);
        var (result, block) = (MirModelTest.Local(body, code >= MirBinaryOp.Eq ? BoundType.Boolean : type), body.AddBlock());
        var rvalue = unary is { } negation ? MirRvalue.Unary(negation, AddOperands(body, [Read(left)]), proven: true)
            : MirRvalue.Binary(code, AddOperands(body, [Read(left), Read(right)]), proven: true);
        body.Push(block, MirStatement.Assign(result, rvalue, At));
        body.Terminate(block, MirTerminator.Return(At));
        Assert.Equal(reported, Validate(body).Any(static x => x.Rule == MirViolation.Operation));

        // A value of a Type that is not Copy here (string, uniq/string, a pair layer, a struct or enum without the Contract, a parameter in a
        // body without its definition's premises) is moved, since its Copy is an Operation finding of its own (DECISIONS BU-88).
        MirOperand Read(MirPlace place)
            => operand is "string" or "uniq" or "pair" or "pair raw" or "struct" or "never struct" or "enum" or "parameter" or "wrapping parameter" or
                "uniqNever" ? MirOperand.Move(place) : MirOperand.Copy(place);

        // A unary operator, or a binary one (Add for a unary row: neither a comparison nor a shift).
        static (MirUnaryOp? Unary, MirBinaryOp Binary) Op(string op) => op switch
        {
            "neg" => (MirUnaryOp.Neg, MirBinaryOp.Add),
            "not" => (MirUnaryOp.Not, MirBinaryOp.Add),
            "+" => (null, MirBinaryOp.Add),
            "-" => (null, MirBinaryOp.Sub),
            "*" => (null, MirBinaryOp.Mul),
            "/" => (null, MirBinaryOp.Div),
            "%" => (null, MirBinaryOp.Rem),
            "&" => (null, MirBinaryOp.BitAnd),
            "|" => (null, MirBinaryOp.BitOr),
            "^" => (null, MirBinaryOp.BitXor),
            "<<" => (null, MirBinaryOp.Shl),
            ">>" => (null, MirBinaryOp.Shr),
            "==" => (null, MirBinaryOp.Eq),
            "!=" => (null, MirBinaryOp.Ne),
            "<" => (null, MirBinaryOp.Lt),
            "<=" => (null, MirBinaryOp.Le),
            ">" => (null, MirBinaryOp.Gt),
            _ => (null, MirBinaryOp.Ge),
        };
    }

    // SPEC 3.1.3, 13.4 (PLAN G662, design review 1's B1): Binding builds a comparison at a Never terminal below ref or uniq layers as a
    // BinaryOp (ComparisonApplicability admits every comparison at Never, a Primitive), and the whitelist's Never row admits it: each body
    // validates and holds the BinaryOp.
    [Theory]
    [InlineData("func g(a: ref/Never, b: ref/Never) -> bool\n    return a == b\n")]
    [InlineData("func g(a: uniq/Never, b: ref/Never) -> bool\n    return a == b\n")]
    [InlineData("func g(a: ref/Never, b: ref/Never) -> bool\n    return a < b\n")]
    public void ANeverTerminalComparisonIsABinaryOpThatValidates(string source)
    {
        var program = MirBuildTest.Compile(source).BuildMir();
        Assert.Empty(program.Findings);
        var built = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn, Decl.Name: "g" });
        Assert.Contains(built.Statements.ToArray(), static x => x.Rvalue is { Kind: MirRvalueKind.BinaryOp, Op: >= (byte)MirBinaryOp.Eq });
    }

    // SPEC 8.2.3, 13.4 (PLAN G646; C-158, C-159): the validator reads an operand at its premised terminal (MirTypes.Premised, the premising
    // Binding.OperandTerminal applies, before the layer walk), so a BinaryOp at a Type a premise makes string, through a layer or two the
    // premise gives (T is ref/string, uniq/string, ref/(ref/string)), an associated Type (H.Item is string) or a chain through one (T is
    // H.Item), is reported; under T is i32, and under T is ref/i32 against a written ref/i32 (both compare i32), it stands. The body built
    // for each definition holds a comparison BinaryOp only where the hand-made one stands, and validates. The hand-made body moves both
    // operands, so a Copy of a Type that is not Copy (uniq/string) is no Operation finding of its own and the row reads the rule alone.
    [Theory]
    [InlineData("func same<T>(a: ref/T, b: ref/T) -> bool\n    T is string\n    return a == b\n", true)]
    [InlineData("func same<T>(x: ref/string during o, a: T, b: T) -> bool\n    T is ref/string during o\n    return a == b\n", true)]
    [InlineData("func same<T>(x: uniq/string during o, a: T, b: T) -> bool\n    T is uniq/string during o\n    return a == b\n", true)]
    [InlineData("func same<T>(x: ref/string during o, a: T, b: T) -> bool\n    T is ref/(ref/string during o) during o\n    return a == b\n", true)]
    [InlineData("contract Holder\n    associate Item\nfunc same<H>(a: ref/H.Item, b: ref/H.Item) -> bool\n    H is Holder\n    H.Item is string\n    return a == b\n", true)]
    [InlineData("contract Holder\n    associate Item\nfunc same<H, T>(a: ref/T, b: ref/T) -> bool\n    H is Holder\n    H.Item is string\n    T is H.Item\n    return a == b\n", true)]
    [InlineData("func same<T>(a: ref/T, b: ref/T) -> bool\n    T is i32\n    return a == b\n", false)]
    [InlineData("func same<T>(x: ref/i32 during o, a: T, b: ref/i32) -> bool\n    T is ref/i32 during o\n    return a == b\n", false)]
    public void APremisedStringComparisonIsNoBinaryOp(string source, bool reported)
    {
        var c = MirBuildTest.Compile(source);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var built = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn, Decl.Name: "same" });
        Assert.Equal(!reported, built.Statements.ToArray().Any(static x => x.Rvalue is { Kind: MirRvalueKind.BinaryOp, Op: (byte)MirBinaryOp.Eq }));
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "same");
        var body = new MirBody();
        body.Reset(new(function.SymbolOf()!, MirBodyKind.Fn), default);
        body.AddLocal(new(BoundType.Boolean, MirLocalKind.Return, At));
        var arguments = function.Parameters.Select(x => MirModelTest.Local(body, x.Type.TypeOf()!, MirLocalKind.Arg)).ToArray();
        var block = body.AddBlock();
        var operands = AddOperands(body, [MirOperand.Move(arguments[^2]), MirOperand.Move(arguments[^1])]);
        body.Push(block, MirStatement.Assign(MirModelTest.Local(body, BoundType.Boolean), MirRvalue.Binary(MirBinaryOp.Eq, operands, proven: true), At));
        body.Terminate(block, MirTerminator.Return(At));
        Assert.Equal(reported, Validate(body, c.Types).Any(static x => x.Rule == MirViolation.Operation));
    }

    // SPEC 8.2.3, 13.4 (C-159, the Front's refuting form; G157 (29)): a Field whose Type is a projection bound in its struct's scope compares
    // as the premises of the comparison's scope make it, string under H.Item is string, also as ref/string under a layer-adding premise and
    // inside a Closure: each comparison is the witness's call, so every body validates and holds no equality BinaryOp. Binding.OperandTerminal
    // premises by CallableContractType (OT2); before, it substituted identity premises only, and each comparison was a BinaryOp at ref/H.Item.
    [Theory]
    [InlineData("    H.Item is string\n    return p.v == q.v\n")]
    [InlineData("    H.Item is ref/string during o\n    return p.v == q.v\n")]
    [InlineData("    H.Item is string\n    let f = func [p, q] () => p.v == q.v\n    return f()\n")]
    public void AProjectionFieldComparesItsPremisedString(string body)
    {
        var c = MirBuildTest.Compile("contract Holder\n    associate Item\nstruct P<H>\n    H is Holder\n    public let v: H.Item\n" +
            "func same<H>(x: ref/string during o, p: ref/P<H>, q: ref/P<H>) -> bool\n    H is Holder\n" + body);
        Assert.Empty(c.Binding.Issues);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.All(program.Bodies.ToArray().Where(static x => x.Source.Module == 0), static x => Assert.DoesNotContain(x.Statements.ToArray(), static y =>
            y.Rvalue is { Kind: MirRvalueKind.BinaryOp, Op: (byte)MirBinaryOp.Eq or (byte)MirBinaryOp.Ne }));
    }

    // G620: an Integer constant holds a value of its integer Type as its canonical N-bit payload, sign-extended for every Type (u8's 255
    // is -1), isize and usize at the profile's 64 bits, so every Int128 is a 128-bit payload. A payload one past either end is a Const
    // finding at its use, and so are an unsigned value's other encoding (255 for u8's 255) and dev's no-target bound test (2^127 in i32
    // and u8; C-142 N3), which validated before (fail-before: no range row).
    [Theory]
    [MemberData(nameof(IntegerConstants))]
    public void AnIntegerConstantHoldsItsValueInItsTypesRange(string type, string bits, bool holds)
        => AssertIntegerConstant(BoundType.Primitives[type], Int128.Parse(bits, CultureInfo.InvariantCulture), holds);

    // G620, G644: an Integer constant of a Type without a width is one of the closed family code generation also lowers
    // (MirTypes.HoldsIntegerConstant), with a value of it: a char's Unicode scalar value (SPEC 3.1: U+0000-U+D7FF and U+E000-U+10FFFF), a
    // raw pointer's null, 0 (SPEC 5.1), and a Type parameter's generic literal, 0 through 127 (SPEC 8.7.4); a value outside them is a Const
    // finding (before: not ranged, F4's rows held), and so is a constant of any other Type, bool's or a struct's (before G620: no Type row).
    [Theory]
    [InlineData("char", 0x10FFFF, true)]
    [InlineData("char", 0xD7FF, true)]
    [InlineData("char", 0xE000, true)]
    [InlineData("char", 0xD800, false)]
    [InlineData("char", 0xDFFF, false)]
    [InlineData("char", 0x110000, false)]
    [InlineData("char", -1, false)]
    [InlineData("pointer", 0, true)]
    [InlineData("pointer", 1, false)]
    [InlineData("parameter", 0, true)]
    [InlineData("parameter", 127, true)]
    [InlineData("parameter", 128, false)]
    [InlineData("parameter", -1, false)]
    [InlineData("bool", 1, false)]
    [InlineData("struct", 0, false)]
    public void AnIntegerConstantOfAWidthlessTypeIsOneCodeGenerationLowers(string type, int bits, bool holds)
    {
        var (c, types) = Fixture.Value;
        var held = type switch
        {
            "char" => BoundType.Char,
            "pointer" => c.Types.Pointer(BoundType.I32),
            "parameter" => types.Pair,
            "bool" => BoundType.Boolean,
            _ => types.Point,
        };
        Assert.True(type != "parameter" || held.Kind == BoundTypeKind.Parameter);
        AssertIntegerConstant(held, bits, holds);
    }

    // G643: a Type with the integer operators, concrete or generic (MirTypes.IsArithmeticInteger, SPEC 13.3.1), has integer arithmetic or
    // an instance that decides it: a PrimitiveInteger T or projection, or Wrapping over one, which hold their generic literals (SPEC 8.7.4,
    // 12.3.3); no abstract Type of another kind, Option<T>, ref/T or Wrapping<Option<T>>, whose instance is never one: its constant is a
    // Const finding (before: any Type with an abstract part held).
    [Theory]
    [InlineData(0, 127, true)]
    [InlineData(0, 128, false)]
    [InlineData(3, 127, true)]
    [InlineData(3, 128, false)]
    [InlineData(4, 100, true)]
    [InlineData(4, 128, false)]
    [InlineData(5, 7, true)]
    [InlineData(5, 128, false)]
    [InlineData(1, 0, false)]
    [InlineData(2, 0, false)]
    [InlineData(6, 0, false)]
    public void AGenericIntegerConstantHasAnArithmeticIntegerType(int parameter, int bits, bool holds)
    {
        var (c, types) = GenericIntegers();
        AssertIntegerConstant(types[parameter], bits, holds, c.Types);
    }

    // G644: the builder's generic literals are Binding's fitted ones, 0 through 127 (SPEC 8.7.4, 12.3.3): a literal that fits no instance,
    // 200 or -1 at T or 128 at Wrapping<T>, is Binding's error and never a Const finding; 127 is valid and holds.
    [Theory]
    [InlineData("    return x + 200\n", false)]
    [InlineData("    return x + -1\n", false)]
    [InlineData("    let w: Wrapping<T> = 128\n    return x\n", false)]
    [InlineData("    let w: Wrapping<T> = 127\n    return x + 127\n", true)]
    public void AGenericLiteralOutsideItsRangeIsBindingsError(string body, bool valid)
    {
        var c = MirBuildTest.Compile("func f<T>(x: T) -> T\n    T is PrimitiveInteger\n" + body);
        var findings = c.BuildMir().Findings;
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.DoesNotContain(findings, static x => x.Rule == MirViolation.Const);
    }

    // G644 (BU-72, SPEC 8.2.3, 12.3.1, 14.8.1): Binding types a literal it fits through an identity premise at the complete Type, while
    // MIR keeps the premised parameter: `T is i32`'s 200 and -1, as values and as Literal Patterns, are i32 constants, and `T is u128`'s
    // maximum a u128 one, which hold by their width, not as generic literals (0 through 127), so the bodies validate.
    [Theory]
    [InlineData("func f<T>(x: T) -> T\n    T is i32\n    let y: T = 200\n    let z: T = -1\n    return y + z + x\n", "i32", 200)]
    [InlineData("func f<T>(x: T) -> i32\n    T is i32\n    return match x\n        200 => 1\n        -1 => 2\n        _ => 0\n", "i32", -1)]
    [InlineData("func f<T>(x: T) -> T\n    T is u128\n    let y: T = 340282366920938463463374607431768211455\n    return y\n", "u128", -1)]
    public void ALiteralFittedThroughAnIdentityPremiseHasTheCompleteType(string source, string type, int bits)
    {
        var c = MirBuildTest.Compile(source);
        Assert.True(c.Binding.Result.IsComplete);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var f = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == "f");
        Assert.Equal(BoundTypeKind.Parameter, f.Locals[1].Type.Kind);
        Assert.Contains(f.Consts.ToArray(), x => x.Kind == MirConstKind.Integer && ReferenceEquals(x.Type, BoundType.Primitives[type]) && x.Bits == bits);
    }

    // G643: an Integer, wrapping, numeric, bit or integer-to-pointer Cast converts Types with the integer operators, concrete or generic
    // (MirTypes.IsArithmeticInteger): T to and from Wrapping<T> and to usize, a projection to T, usize to raw/T (SPEC 8.7.4, 13.5.4, 5.4);
    // from Option<T> or ref/T it is an Operation finding (before: held).
    [Theory]
    [InlineData("Integer", 0, 3, true)]
    [InlineData("Integer", 3, 0, true)]
    [InlineData("Integer", 0, -1, true)]
    [InlineData("Integer", 1, 0, false)]
    [InlineData("Integer", 2, -1, false)]
    [InlineData("Wrap", 4, 0, true)]
    [InlineData("Wrap", 1, 0, false)]
    [InlineData("IntToPointer", -1, 7, true)]
    [InlineData("IntToPointer", 2, 7, false)]
    public void AGenericIntegerCastConvertsArithmeticIntegerTypes(string kind, int source, int target, bool holds)
    {
        var (c, types) = GenericIntegers();
        var body = new MirBody();
        body.Reset(new(null, MirBodyKind.Fn), default);
        body.AddLocal(new(BoundType.Unit, MirLocalKind.Return, At));
        var (from, to) = (source < 0 ? BoundType.USize : types[source], target < 0 ? BoundType.USize : types[target]);
        var (read, held, block) = (MirModelTest.Local(body, from), MirModelTest.Local(body, to), body.AddBlock());
        var cast = MirRvalue.Cast(Enum.Parse<MirCastKind>(kind), AddOperands(body, [MirOperand.Move(read)]), to);
        body.Push(block, MirStatement.Assign(held, cast, Broken));
        body.Terminate(block, MirTerminator.Return(At));
        MirViolation[] expected = holds ? [] : [MirViolation.Operation];
        Assert.Equal(expected.Select(static x => (x, Broken.Syntax)), Validate(body, c.Types).Select(static x => (x.Rule, x.Subject)));
    }

    // G658 (C-177): the builder's form of a folded wrap to an abstract integer Type, a Wrap Cast of an i128 constant (the payload holds by
    // its width, outside 0 through 127 too) to T or Wrapping<T>, validates; the same payload as a constant of T is a Const finding (above).
    [Theory]
    [InlineData(0, 200)]
    [InlineData(0, -1)]
    [InlineData(3, 128)]
    public void AWrapCastOfAnI128ConstantToAGenericIntegerHolds(int target, int bits)
    {
        var (c, types) = GenericIntegers();
        var body = new MirBody();
        body.Reset(new(null, MirBodyKind.Fn), default);
        body.AddLocal(new(BoundType.Unit, MirLocalKind.Return, At));
        var (held, block) = (MirModelTest.Local(body, types[target]), body.AddBlock());
        var payload = MirOperand.Const(body.Const(new(MirConstKind.Integer, BoundType.Primitives["i128"], bits)));
        body.Push(block, MirStatement.Assign(held, MirRvalue.Cast(MirCastKind.Wrap, AddOperands(body, [payload]), types[target]) with { Proven = true }, Broken));
        body.Terminate(block, MirTerminator.Return(At));
        Assert.Empty(Validate(body, c.Types));
    }

    // The Call positions of the same rule, by the facts A5 reads and not by the result's Type: `put` returns a scalar and keeps its input through
    // `h`, `pick` keeps it in its result, `pinned` and `bound` relate it to static, which no output states; a Value callee has no declaration to
    // read, a default template's call no contract, and RawSlice's result retains its backing beyond its declaration (MirDescriptors.Retained);
    // only `size` keeps nothing.
    [Theory]
    [InlineData("size", false)]
    [InlineData("put", true)]
    [InlineData("pick", true)]
    [InlineData("pinned", true)]
    [InlineData("bound", true)]
    [InlineData("value", true)]
    [InlineData("template", true)]
    [InlineData("backing", true)]
    public void AStringConstantAtARefIsAnInputItsCallKeepsNothingOf(string position, bool reported)
    {
        const string Source = """
            func put(h: uniq/Array<ref/string during a>, s: ref/string during a) -> i32
                h.insert(0, s)
                return 1
            func size(s: ref/string) -> i32 => 1
            func pick(s: ref/string) -> ref/string during s => s
            func pinned(s: ref/string during static) -> i32 => 1
            func bound(s: ref/string during b) -> i32
                origin b outlives static
                return 1
            func first(s: ref/string, tail: i32 = if s.isEmpty => 1 else => second(s)) -> i32 => tail
            func second(s: ref/string, tail: i32 = if s.isEmpty => 2 else => first(s)) -> i32 => tail
            unsafe func backed(p: raw/i32, s: ref/Array<i32> during b) -> Slice<i32> during b => Raw.slice(p, 1)
            func run(h: uniq/Array<ref/string during a>, s: ref/string during a, t: ref/string during static, f: (ref/string) -> i32) -> i32
                let stored = put(h, s)
                let measured = size(s)
                let picked = pick(s)
                let fixed = pinned(t)
                let called = f(s)
                let held = bound(t)
                return stored + measured + fixed + called + held + first(s)

            """;
        var c = MirBuildTest.Compile(Source);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var name = position == "backing" ? "backed" : "run";
        var body = program.Bodies.ToArray().Single(x => x.Key.Decl?.Name == name);
        var call = body.Blocks.ToArray().Select(static x => x.Terminator).Single(x => x.Kind == MirTerminatorKind.Call && position switch
        {
            "value" => x.Callee.Kind == MirCalleeKind.Value,
            "template" => x.Callee is { Kind: MirCalleeKind.Fn, Arg: > 0 } && x.Callee.Symbol?.Name == "first",
            "backing" => x.Callee is { Kind: MirCalleeKind.Intrinsic, Arg: (int)IntrinsicId.RawSlice },
            _ => x.Callee.Symbol?.Name == position,
        });
        var view = c.Types.Reference(SemanticsKind.Ref, BoundType.String);
        Unsafe.AsRef(in body.Operands[call.Start + call.Count - 1]) = MirOperand.Const(body.Const(new(MirConstKind.String, view, Value: "a")));
        var findings = Validate(body, c.Types).Select(static x => x.Rule).ToArray();
        Assert.Equal(reported, findings.Contains(MirViolation.Operation));
        Assert.True(reported || findings.Length == 0);
    }

    // SPEC 8.15, 23.1.1: Binding publishes no part of a definition beyond PairBinder.CaseBound views (Binding.CaseLimits), so a body with more
    // views is a Binder finding at the body, which is not checked further: six binders of two cases, 64 views, are valid but for an Adapt no
    // view resolves (T to bool); a seventh gives the one Binder finding, the Adapt's views unresolved.
    [Fact]
    public void ViewsBeyondTheCaseBoundAreAFinding()
    {
        var binders = new List<PairBinder>();
        PairBinder.Collect(Fixture.Value.Types.Generic, binders);
        var binder = binders[0] with { Admitted = SemanticsMask.Owner | SemanticsMask.Uniq };
        var body = new MirBody();
        body.Reset(new(null, MirBodyKind.Fn), default);
        body.AddLocal(new(BoundType.Unit, MirLocalKind.Return, At));
        _ = TyCtxt.Layer(Fixture.Value.Types.Pair, out var payload, out _);
        var (value, adapted, block) = (MirModelTest.Local(body, payload!), MirModelTest.Local(body, BoundType.Boolean), body.AddBlock());
        var token = MirOperand.Const(body.Const(new(MirConstKind.TypeToken, payload!)));
        var adapt = MirRvalue.Adapt(AddOperands(body, [MirOperand.Copy(value), token]), value, BoundType.Boolean, shorthand: false, input: false);
        body.Push(block, MirStatement.Assign(adapted, adapt, At));
        body.Terminate(block, MirTerminator.Return(At));
        body.Binders.AddRange(Enumerable.Repeat(binder, 6));
        Assert.Equal([(MirViolation.Operation, body.Source)], Validate(body).Select(static x => (x.Rule, x.Subject)));
        body.Binders.Add(binder);
        Assert.Equal([(MirViolation.Binder, body.Source)], Validate(body).Select(static x => (x.Rule, x.Subject)));
    }

    // DECISIONS R5-M183: the validator's verdict (MirBody.HasFinding) is whether it added a finding, at each of its exits: the walk's end, a
    // projection chain out of order that Tables cannot walk, and the case bound's; a valid body validated after another body's findings has
    // none, and a pooled body reset and rebuilt valid (MirProgram.Begin's reset) clears it. Fail-before: no verdict.
    [Fact]
    public void TheVerdictIsWhetherTheValidatorAddedAFinding()
    {
        var validator = new MirValidator(Fixture.Value.Compilation.Types);
        var findings = new List<MirFinding>();
        var binders = new List<PairBinder>();
        PairBinder.Collect(Fixture.Value.Types.Generic, binders);
        var binder = binders[0] with { Admitted = SemanticsMask.Owner | SemanticsMask.Uniq };
        Assert.True(Verdict(Typed("assign")));
        var unordered = Build(null);
        Unsafe.AsRef(in unordered.Projections[1]) = unordered.Projections[1] with { Prefix = 1 };
        Assert.True(Verdict(unordered));
        Assert.False(Verdict(Build(null)));
        var pooled = new MirBody();
        Rebuild(pooled, 7); // 128 views (ViewsBeyondTheCaseBoundAreAFinding's).
        Assert.True(Verdict(pooled));
        Rebuild(pooled, 0);
        Assert.False(pooled.HasFinding);
        Assert.False(Verdict(pooled));

        bool Verdict(MirBody body)
        {
            validator.Validate(body, findings);
            return body.HasFinding;
        }

        // The body started over as an empty body of a unit result with a number of two-case binders.
        void Rebuild(MirBody body, int count)
        {
            body.Reset(new(null, MirBodyKind.Fn), default);
            body.AddLocal(new(BoundType.Unit, MirLocalKind.Return, At));
            body.Terminate(body.AddBlock(), MirTerminator.Return(At));
            body.Binders.AddRange(Enumerable.Repeat(binder, count));
        }
    }

    // DECISIONS R5-M90 (2e-D 5.7): a region, the body or an Inlined scope's locations (a default's copy), is entered only at its first
    // location, and no edge from inside it targets the block that location lies in. Valid: a copy at (0, 0), which the body's entry enters;
    // a copy the caller's statement before it enters mid-block, with a nested copy that shares its first location and a loop inside it;
    // and a copy at a block's start that two caller edges enter (a loop's). Each broken form is an Entry finding at the edge: a caller jump
    // into the second copy's second block, a check edge there from the third copy, the caller's statement between two parts of the second
    // copy, its edge back into the block it began in, and the third copy's edge to its first location. An edge to block 0, the body's
    // first location, is EachRuleRejectsTheBodyThatBreaksIt's.
    [Theory]
    [InlineData(null)]
    [InlineData("jump")]
    [InlineData("check")]
    [InlineData("fall")]
    [InlineData("back")]
    [InlineData("again")]
    public void ARegionIsEnteredOnlyAtItsFirstLocation(string? broken)
    {
        var body = MirModelTest.NewBody(Fixture.Value.Types);
        var flag = MirModelTest.Local(body, BoundType.Boolean, MirLocalKind.Temp);
        var first = (ushort)body.AddScope(0, MirScopeKind.Inlined, default);
        var copy = (ushort)body.AddScope(0, MirScopeKind.Inlined, default);
        var inner = (ushort)body.AddScope(copy, MirScopeKind.Inlined, default);
        var loop = (ushort)body.AddScope(0, MirScopeKind.Inlined, default);
        var (entry, call, rest, after) = (body.AddBlock(), body.AddBlock(), body.AddBlock(), body.AddBlock());
        var (head, next, tail, done) = (body.AddBlock(), body.AddBlock(), body.AddBlock(), body.AddBlock());
        SourceInfo In(ushort scope, string form = "") => (broken == form ? Broken : At) with { Scope = scope };
        void Read(int block, SourceInfo source) => body.Push(block, MirStatement.Of(MirStatementKind.FakeRead, flag, source));
        MirTerminator Branch(int to, int otherwise, SourceInfo source)
        {
            body.AddCase(0, to);
            return MirTerminator.SwitchInt(MirOperand.Copy(flag), body.Cases.Length - 1, 1, otherwise, source);
        }

        Read(entry, In(first));
        Read(entry, At);
        body.Terminate(entry, MirTerminator.Goto(call, At));
        Read(call, At); // The caller's statement before the copy, its slot's StorageLive.
        Read(call, In(inner));
        Read(call, In(copy));
        if (broken == "fall")
        {
            Read(call, Broken);
            Read(call, In(copy));
        }

        body.Terminate(call, MirTerminator.Goto(rest, In(copy)));
        Read(rest, In(copy));
        body.Terminate(rest, Branch(broken == "back" ? call : rest, after, In(copy, "back")));
        Read(after, At);
        body.Terminate(after, Branch(broken == "jump" ? rest : head, head, In(0, "jump")));
        Read(head, In(loop));
        body.Terminate(head, MirTerminator.Goto(next, In(loop, "check"), broken == "check" ? rest : -1));
        Read(next, In(loop));
        body.Terminate(next, broken == "again" ? Branch(head, tail, In(loop, "again")) : MirTerminator.Goto(tail, In(loop)));
        Read(tail, At);
        body.Terminate(tail, Branch(head, done, At));
        body.Terminate(done, MirTerminator.Return(At));
        Assert.Equal(broken is null ? [] : [(MirViolation.Entry, Broken.Syntax)], Validate(body).Select(static x => (x.Rule, x.Subject)));
    }

    // R5-M90's guard: the region walk follows the scope table and each location's scope, so a scope chain that does not lead out (an
    // Inlined scope its own outer scope) or a location outside the scopes stops that walk alone: the Scope finding and the other rules'
    // findings (an unreachable block) are reported, and the edge to block 0 is not walked.
    [Theory]
    [InlineData("outer")]
    [InlineData("location")]
    public void AScopeTheRegionWalkCannotFollowStopsOnlyIt(string broken)
    {
        var body = MirModelTest.NewBody(Fixture.Value.Types);
        var flag = MirModelTest.Local(body, BoundType.Boolean, MirLocalKind.Temp);
        var scope = body.AddScope(broken == "outer" ? body.Scopes.Length : 0, MirScopeKind.Inlined, default);
        var (entry, loop) = (body.AddBlock(), body.AddBlock());
        body.Push(entry, MirStatement.Of(MirStatementKind.FakeRead, flag, At with { Scope = (ushort)(broken == "outer" ? scope : 9) }));
        body.Terminate(entry, MirTerminator.Goto(loop, At));
        body.Terminate(loop, MirTerminator.Goto(entry, At));
        body.Terminate(body.AddBlock(), MirTerminator.Return(Broken));
        Assert.Equal([MirViolation.Scope, MirViolation.Unreachable], Validate(body).Select(static x => x.Rule));
    }

    // DECISIONS R4-M132: a Result local, the body's result in transit, is a Temp of local 0's Type read only by the whole Move into _0 that
    // delivers it: the builder's is valid; the role on a Var or on a temp of another Type, or a delivery that copies it, is a Local finding.
    [Theory]
    [InlineData(null)]
    [InlineData("var")]
    [InlineData("bool")]
    [InlineData("copy")]
    public void AResultLocalIsReadOnlyByItsDelivery(string? broken)
    {
        var c = MirBuildTest.Compile("func leave(x: ref/i32, c: bool) -> ref/i32 during x\n    if c\n        let local = 1\n" +
            "        var r = local@ref\n        return r\n    return x\n");
        var body = c.BuildMir().Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "leave");
        var locals = body.Locals.ToArray();
        var result = Array.FindIndex(locals, static x => x.Role == MirLocalRole.Result);
        var delivery = Array.FindIndex(body.Statements.ToArray(), x => x is { Kind: MirStatementKind.Assign, Place.Root: 0, Rvalue.Kind: MirRvalueKind.Use } &&
            body.Operands[x.Rvalue.Start].Place.Root == result);
        Assert.True(delivery >= 0);
        var other = broken switch
        {
            "var" => Array.FindIndex(locals, static x => x.Kind == MirLocalKind.Var),
            "bool" => Array.FindIndex(locals, static x => x.Kind == MirLocalKind.Temp && ReferenceEquals(x.Type, BoundType.Boolean)),
            _ => -1,
        };
        if (other >= 0)
        {
            Unsafe.AsRef(in body.Locals[other]) = locals[other] with { Role = MirLocalRole.Result };
        }
        else if (broken == "copy")
        {
            var operand = body.Statements[delivery].Rvalue.Start;
            Unsafe.AsRef(in body.Operands[operand]) = body.Operands[operand] with { Kind = MirOperandKind.Copy };
        }

        Assert.Equal(broken is null ? [] : [MirViolation.Local], Validate(body, c.Types).Select(static x => x.Rule).Distinct());
    }

    // PLAN G442 (the owner's O-97 (a)): Places marks a Dictionary literal entry's placeEntry Call, the construction's insertion: a Fn callee
    // each of whose operands is a Move of a whole Temp. The builder's is valid; a copied operand, an operand moving a Var, and the attribute
    // on a Goto are Operation findings.
    [Theory]
    [InlineData(null)]
    [InlineData("copy")]
    [InlineData("var")]
    [InlineData("goto")]
    public void APlacesCallMovesWholeTemps(string? broken)
    {
        var c = MirBuildTest.Compile("func f(a: i32) -> Dictionary<i32, i32>\n    var v = a\n    v = 2\n    return [1: a, 2: v]\n");
        var body = c.BuildMir().Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "f");
        var blocks = body.Blocks.ToArray();
        var call = Array.FindIndex(blocks, static x => x.Terminator.Places);
        Assert.True(call >= 0);
        var value = blocks[call].Terminator.Start + 2;
        switch (broken)
        {
            case "copy":
                Unsafe.AsRef(in body.Operands[value]) = body.Operands[value] with { Kind = MirOperandKind.Copy };
                break;
            case "var":
                var local = Array.FindIndex(body.Locals.ToArray(), static x => x.Kind == MirLocalKind.Var);
                Unsafe.AsRef(in body.Operands[value]) = body.Operands[value] with { Place = new(local) };
                break;
            case "goto":
                var other = Array.FindIndex(blocks, static x => x.Terminator.Kind == MirTerminatorKind.Goto);
                body.Terminate(other, blocks[other].Terminator with { Places = true });
                break;
        }

        Assert.Equal(broken is null ? [] : [MirViolation.Operation], Validate(body, c.Types).Select(static x => x.Rule).Distinct());
    }

    // DECISIONS BU-48 (amended by G283, SPEC 16.3.1): MirLocalRole.Receiver marks exactly a constructor's _0, its construction receiver, and
    // a destructor's _1, its destruction receiver: the builder's are valid; the role dropped from either, set on another local of the
    // constructor or on a destructor's _0, set on _0 of a body that is neither, or set on _1 of a by-value Self method (`consume`, `take`)
    // or of a function local to the drop (`helper`), whose _1 is a value of the struct with the drop, is a Local finding.
    [Theory]
    [InlineData(null)]
    [InlineData("dropped")]
    [InlineData("other")]
    [InlineData("function")]
    [InlineData("destructor")]
    [InlineData("destructor dropped")]
    [InlineData("destructor result")]
    [InlineData("consume")]
    [InlineData("take")]
    [InlineData("helper")]
    public void AReceiverIsExactlyAConstructorsResultOrADestructorsSelf(string? broken)
    {
        var c = MirBuildTest.Compile(
            "struct S\n    public var n: i32\n    public init(n: i32) => self.n = n\n    drop\n        func helper(e: S) => ()\n" +
            "    public func consume(self: Self) => ()\n    public func take(self: Self, k: i32) => ()\nfunc f(n: i32) -> i32 => n\n");
        var bodies = c.BuildMir().Bodies.ToArray();
        var destructor = broken?.StartsWith("destructor", StringComparison.Ordinal) == true;
        var name = broken switch { "function" => "f", "consume" or "take" or "helper" => broken, _ => null };
        var body = bodies.Single(x => x.Source.Module == 0 && (name is not null ? x.Key.Decl?.Name == name
            : destructor ? x.Key.Decl?.Declaration is FunctionKoto { IsDestructor: true } : AdtDef.IsConstructor(x.Key.Decl)));
        var locals = body.Locals.ToArray();
        Assert.Equal(name is not null ? [] : [destructor ? 1 : 0], Enumerable.Range(0, locals.Length).Where(x => locals[x].Role == MirLocalRole.Receiver));
        var (at, role) = broken switch
        {
            "dropped" => (0, MirLocalRole.None),
            "other" or "consume" or "take" or "helper" => (1, MirLocalRole.Receiver),
            "function" => (0, MirLocalRole.Receiver),
            "destructor dropped" => (1, MirLocalRole.None),
            "destructor result" => (0, MirLocalRole.Receiver),
            _ => (-1, MirLocalRole.None),
        };
        if (at >= 0)
        {
            Unsafe.AsRef(in body.Locals[at]) = locals[at] with { Role = role };
        }

        Assert.Equal(broken is null or "destructor" ? [] : [MirViolation.Local], Validate(body, c.Types).Select(static x => x.Rule).Distinct());
    }

    // DECISIONS BU-46: Unstated marks only a kept part's raw layer its declaration writes without `during`, so a local whose Type states it,
    // as a raw layer or inside a Tuple, is a Local finding; the builder's locals over unstated pointers are valid.
    [Theory]
    [InlineData(null)]
    [InlineData("layer")]
    [InlineData("component")]
    public void ALocalsTypeStatesNoUnstatedOrigin(string? broken)
    {
        var c = MirBuildTest.Compile("func f()\n    var p: raw/i32 = null\n    var t: (raw/i32, i32) = (null, 0)\n");
        var body = c.BuildMir().Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "f");
        var locals = body.Locals.ToArray();
        var at = Array.FindIndex(locals, x => x.Name == (broken == "component" ? "t" : "p"));
        if (broken is not null)
        {
            var marked = c.Types.Pointer(BoundType.I32, BoundOrigin.Unstated);
            Unsafe.AsRef(in body.Locals[at]) = locals[at] with { Type = broken == "layer" ? marked : c.Types.Tuple([marked, BoundType.I32]) };
        }

        Assert.Equal(broken is null ? [] : [MirViolation.Local], Validate(body, c.Types).Select(static x => x.Rule).Distinct());
    }

    // V11: validating a body again, the Type tier's and its Adapt's case views included, an owning interpolation's steps and its FormatFinish's
    // pairing (DECISIONS R5-M131) included, a static's reads on two branches with G191 rule (3)'s solve included, and enumerating its uses,
    // of the body and under each case view, allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmValidationAllocatesNothing()
    {
        var (body, typed) = (Build(null), Typed(null));
        var c = MirBuildTest.Compile(Interpolation + Registry + Branches);
        var bodies = c.BuildMir().Bodies.ToArray();
        var (formatted, statics) = (bodies.Single(static x => x.Key.Decl?.Name == "f"), bodies.Single(static x => x.Key.Decl?.Name == "use"));
        var (validator, formats) = (new MirValidator(Fixture.Value.Compilation.Types), new MirValidator(c.Types));
        var cases = new MirCases(Fixture.Value.Compilation.Types);
        var findings = new List<MirFinding>();
        var counter = default(Counter);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            validator.Validate(body, findings);
            validator.Validate(typed, findings);
            formats.Validate(formatted, findings);
            formats.Validate(statics, findings);
            for (var block = 0; block < body.Blocks.Length; block++)
            {
                for (var i = 0; i <= body.Blocks[block].Count; i++)
                {
                    MirUses.ForEach(body, new(block, i), ref counter);
                    for (var view = 0L; view < MirCases.Count(body); view++)
                    {
                        cases.Select(body, view);
                        MirUses.ForEach(cases, new(block, i), ref counter);
                    }
                }
            }
        }));
        Assert.Empty(findings);
        Assert.True(counter.Places > 0);
    }

    // The Type tier's body: each form it checks in a valid use, or the named form broken at Broken.
    internal static MirBody Typed(string? broken)
    {
        var (c, types) = Fixture.Value;
        BindingSymbol Function(string name) => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == name).SymbolOf()!;
        var body = new MirBody();
        body.Reset(new(broken == "return" ? Function("h") : types.Function, MirBodyKind.Fn), default);
        body.AddLocal(new(BoundType.I32, MirLocalKind.Return, At));
        MirPlace Local(BoundType type) => MirModelTest.Local(body, type);
        var (a, flag, index, count, real) = (Local(BoundType.I32), Local(BoundType.Boolean), Local(BoundType.ISize), Local(BoundType.Primitives["u8"]), Local(BoundType.F64));
        var (pointer, view, exclusive) = (Local(c.Types.Pointer(BoundType.I32)), Local(c.Types.Reference(SemanticsKind.Ref, BoundType.I32)), Local(c.Types.Reference(SemanticsKind.Uniq, BoundType.I32)));
        var (tuple, meter) = (c.Types.Tuple([BoundType.I32, BoundType.Boolean]), c.Semantics.SelfType(c.Kotonoha.RootKoto.NestedContainers.Single(static x => x.Name == "Meter").SymbolOf()!));
        var (pair, parent, point, shape, array, gauge) = (Local(tuple), Local(types.Base), Local(types.Point), Local(types.Shape), Local(types.Array), Local(c.Types.Reference(SemanticsKind.Ref, meter)));
        var (handle, upcast) = (Local(c.Types.Intern(BoundTypeKind.Semantics, null, SemanticsKind.Obj, [types.Point])), Local(c.Types.Intern(BoundTypeKind.Semantics, null, SemanticsKind.Obj, [types.Base])));
        var result = broken == "returning" ? BoundType.Never : BoundType.Boolean;
        var callable = Local(c.Types.Reference(SemanticsKind.Ref, c.Types.Intern(BoundTypeKind.Function, null, SemanticsKind.Owner, [c.Types.Tuple([BoundType.I32]), result]))); // Borrowed (BU-88).
        var (adapted, borrowed) = (broken == "adapt" ? real : a, Local(c.Types.Reference(SemanticsKind.ObjRef, types.Point)));
        var blocks = new[] { body.AddBlock(), body.AddBlock(), body.AddBlock(), body.AddBlock(), body.AddBlock(), body.AddBlock(), body.AddBlock() };
        int Operands(params MirOperand[] operands) => AddOperands(body, operands);
        SourceInfo Where(string form) => broken == form ? Broken : default;
        void Assign(MirPlace place, MirRvalue rvalue, string form = "") => body.Push(blocks[0], MirStatement.Assign(place, rvalue, Where(form)));
        var one = MirOperand.Const(body.Const(new(MirConstKind.Integer, BoundType.I32, 1)));
        var yes = MirOperand.Const(body.Const(new(MirConstKind.Bool, BoundType.Boolean, 1)));
        Assign(a, MirRvalue.Binary(MirBinaryOp.Add, Operands(MirOperand.Copy(a), broken == "operands" ? MirOperand.Copy(flag) : one), false), "operands");
        Assign(flag, MirRvalue.Binary(MirBinaryOp.Lt, Operands(MirOperand.Copy(a), one), true));
        Assign(flag, MirRvalue.Binary(MirBinaryOp.Eq, Operands(MirOperand.Copy(view), broken == "comparison" ? MirOperand.Copy(flag) : one), true), "comparison");
        Assign(a, MirRvalue.Binary(MirBinaryOp.Shl, Operands(MirOperand.Copy(a), MirOperand.Copy(broken == "shift" ? flag : count)), false), "shift");
        Assign(pointer, MirRvalue.Binary(MirBinaryOp.Add, Operands(MirOperand.Copy(pointer), MirOperand.Copy(broken == "displacement" ? a : index)), true), "displacement");
        Assign(a, MirRvalue.Unary(MirUnaryOp.Neg, Operands(MirOperand.Copy(a)), false));
        Assign(index, MirRvalue.Cast(MirCastKind.Integer, Operands(MirOperand.Copy(a)), BoundType.ISize));
        Assign(real, MirRvalue.Cast(broken == "cast" ? MirCastKind.Floating : MirCastKind.Numeric, Operands(MirOperand.Copy(a)), BoundType.F64), "cast");
        Assign(upcast, MirRvalue.Cast(MirCastKind.Upcast, Operands(broken == "upcast" ? MirOperand.Copy(a) : MirOperand.Move(handle)), body.TypeOf(upcast)!), "upcast");
        Assign(broken == "ref" ? exclusive : view, MirRvalue.Ref(MirRefKind.Shared, a, body.TypeOf(broken == "ref" ? exclusive : view)!), "ref");
        Assign(exclusive, MirRvalue.Ref(MirRefKind.Uniq, a, body.TypeOf(exclusive)!));
        Assign(pointer, MirRvalue.Ref(MirRefKind.Raw, a, body.TypeOf(pointer)!));
        Assign(pair, MirRvalue.Aggregate(MirAggregateKind.Tuple, Operands(one, broken == "component" ? one : yes), 2, tuple), "component");
        var (never, nowhere) = (Local(BoundType.Never), Local(c.Types.Reference(SemanticsKind.Ref, BoundType.Never)));
        Assign(a, MirRvalue.Use(Operands(MirOperand.Move(never))));
        Assign(a, MirRvalue.Binary(MirBinaryOp.Add, Operands(MirOperand.Move(never), one), false));
        Assign(pair, MirRvalue.Aggregate(MirAggregateKind.Tuple, Operands(MirOperand.Move(never), yes), 2, tuple));
        Assign(view, MirRvalue.Use(Operands(MirOperand.Copy(broken == "never" ? nowhere : view))), "never");
        Assign(point, MirRvalue.Aggregate(MirAggregateKind.Adt, Operands(broken == "base" ? one : MirOperand.Move(parent), one, yes), 3, types.Point), "base");
        Assign(shape, MirRvalue.Aggregate(MirAggregateKind.Adt, Operands(one, broken == "case" ? yes : one), 2, types.Shape, 1), "case");
        var closure = c.Types.Intern(BoundTypeKind.Closure, types.Function, SemanticsKind.Owner, [BoundType.I32]);
        Assign(Local(closure), MirRvalue.Aggregate(MirAggregateKind.Closure, Operands(broken == "capture" ? yes : one), 1, closure), "capture");
        Assign(array, MirRvalue.Aggregate(MirAggregateKind.FixedArray, Operands(one, one, one), 3, types.Array));
        Assign(index, MirRvalue.Len(array));
        var lent = Local(c.Types.Reference(SemanticsKind.Ref, types.Array));
        var storage = MirRvalue.Storage(broken == "storage" ? lent : array, body.TypeOf(pointer)!);
        Assign(pointer, broken == "storage-op" ? storage with { Op = (byte)MirRefKind.Shared } : storage, broken == "storage-op" ? broken : "storage");
        Assign(index, MirRvalue.Discriminant(shape));
        Assign(a, MirRvalue.Use(Operands(broken == "assign" ? MirOperand.Copy(flag) : MirOperand.Copy(body.Project(pair, MirProjectionKind.Field, 0)))), "assign");
        var lost = MirModelTest.Local(body, BoundType.Unit, MirLocalKind.Temp);
        Assign(lost, broken == "opaque" ? MirRvalue.Use(Operands(MirOperand.Const(body.Const(new(MirConstKind.Unit, BoundType.Unit))))) : MirRvalue.Opaque(BoundType.Unit, body.AddOpaque(default, [])));
        Assign(a, MirRvalue.Use(Operands(MirOperand.Move(lost))), "opaque");
        MirOperand Token(BoundType type) => MirOperand.Const(body.Const(new(MirConstKind.TypeToken, type)));
        var (acquired, payload) = (broken == "token" ? flag : a, body.Project(handle, MirProjectionKind.Deref, 0));
        var adapt = MirRvalue.Adapt(Operands(broken == "acquisition" ? one : MirOperand.Copy(acquired), Token(BoundType.I32)), acquired, body.TypeOf(adapted)!, shorthand: false, input: false);
        Assign(adapted, broken == "shorthand" ? adapt with { Op = 4 } : adapt, broken is "acquisition" or "token" or "shorthand" ? broken : "adapt");
        var borrow = MirRvalue.Adapt(Operands(MirOperand.Copy(handle), Token(body.TypeOf(handle)!)), broken == "payload" ? handle : payload, body.TypeOf(borrowed)!, shorthand: false, input: false);
        Assign(borrowed, borrow, "payload");

        body.AddCase(0, blocks[1]);
        body.Terminate(blocks[0], MirTerminator.SwitchInt(MirOperand.Copy(broken == "switch" ? real : flag), 0, 1, blocks[1], Where("switch")));
        var (first, second) = broken == "argument" ? (flag, flag) : (a, flag);
        var h = broken == "declaration" ? MirCallee.Intrinsic(IntrinsicId.RawInitialize, Function("h")) : new(MirCalleeKind.Fn, Function("h"));
        var site = Where(broken is "argument" or "declaration" ? broken : "result");
        var passed = Operands(MirOperand.Copy(first), MirOperand.Copy(second));
        body.Terminate(blocks[1], MirTerminator.Call(h, passed, 2, broken == "result" ? a : flag, blocks[2], site));
        var signature = broken == "signature" ? one.Arg : Token(body.TypeOf(callable)!.Components[0]).Arg;
        var (callee, arguments) = broken == "arity"
            ? (Local(c.Types.Intern(BoundTypeKind.Closure, types.Function, SemanticsKind.Owner, [])), Operands(MirOperand.Copy(a), MirOperand.Copy(a)))
            : (callable, Operands(MirOperand.Copy(broken == "value" ? flag : a)));
        var at = Where(broken is "signature" or "arity" or "returning" ? broken : "value");
        var into = broken == "returning" ? MirModelTest.Local(body, BoundType.Never, MirLocalKind.Temp) : flag;
        var value = MirTerminator.Call(new(MirCalleeKind.Value, Arg: signature), arguments, broken == "arity" ? 2 : 1, into, blocks[3], at);
        body.Terminate(blocks[2], value with { Operand = MirOperand.Copy(callee) });
        body.Terminate(blocks[3], MirTerminator.Call(new(MirCalleeKind.Fn, Function("d"), Arg: 2), Operands(broken == "template" ? MirOperand.Copy(a) : MirOperand.Copy(view)), 1, flag, blocks[4], Where("template")));
        var (receiver, by) = (MirOperand.Copy(gauge), MirOperand.Copy(a));
        body.Terminate(blocks[4], MirTerminator.Call(new(MirCalleeKind.Fn, Function("scaled")), broken == "receiver" ? Operands(by, receiver) : Operands(receiver, by), 2, a, blocks[5], Where("receiver")));
        var held = MirModelTest.Local(body, BoundType.Unit, MirLocalKind.Temp);
        var recorded = MirOperand.Copy(broken == "record" ? view : a);
        var records = broken == "short" // Its side, then the value; a short call its side alone (its row's arity is 2).
            ? MirTerminator.Call(MirCallee.Intrinsic(IntrinsicId.TestRecordValue), Operands(one), 1, held, blocks[6], Broken)
            : MirTerminator.Call(MirCallee.Intrinsic(IntrinsicId.TestRecordValue), Operands(one, recorded), 2, held, blocks[6], Where("record"));
        body.Terminate(blocks[5], records);
        body.Terminate(blocks[6], MirTerminator.Return(Where("return")));
        return body;
    }

    // The valid body, or the body that breaks one rule: an added statement, local, block or terminator at Broken, or a changed one.
    internal static MirBody Build(MirViolation? broken)
    {
        var (c, types) = Fixture.Value;
        var tuple = c.Types.Tuple([BoundType.I32, BoundType.Boolean]);
        var body = MirModelTest.NewBody(types);
        var pairs = MirModelTest.Local(body, tuple, MirLocalKind.Arg);
        var pair = MirModelTest.Local(body, types.Pair, MirLocalKind.Arg);
        var point = MirModelTest.Local(body, types.Point);
        var index = MirModelTest.Local(body, BoundType.ISize, MirLocalKind.Temp);
        var shape = MirModelTest.Local(body, types.Shape);
        var array = MirModelTest.Local(body, types.Array);
        var reference = MirModelTest.Local(body, c.Types.Reference(SemanticsKind.Ref, BoundType.I32));
        var exclusive = c.Types.Reference(SemanticsKind.Uniq, BoundType.I32);
        var parameter = broken == MirViolation.Lent ? exclusive : BoundType.I32;
        var function = MirModelTest.Local(body, c.Types.Reference(SemanticsKind.Ref, c.Types.Intern(BoundTypeKind.Function, null, SemanticsKind.Owner, [c.Types.Tuple([parameter]), BoundType.I32]))); // The callee, borrowed (BU-88).
        var flag = MirModelTest.Local(body, BoundType.Boolean, MirLocalKind.Temp);
        if (broken == MirViolation.Local)
        {
            body.AddLocal(new(BoundType.I32, MirLocalKind.Return, Broken));
        }

        var parent = MirModelTest.Local(body, types.Base);

        PairBinder.Collect(types.Generic, body.Binders);
        var scope = (ushort)body.AddScope(0, MirScopeKind.Unsafe, default);
        var shared = body.Static(new(types.Function, BoundType.I32, false, false));
        if (broken == MirViolation.NoBlocks)
        {
            return body;
        }

        var (entry, abort, call, drop, exit, ensure, done) = (body.AddBlock(), body.AddBlock(), body.AddBlock(), body.AddBlock(), body.AddBlock(), body.AddBlock(), body.AddBlock());
        var check = body.AddBlock();
        var result = new MirPlace(0);
        var x = body.Project(pairs, MirProjectionKind.Field, 0);
        void Push(MirStatementKind kind, MirPlace place, SourceInfo? source = null) => body.Push(entry, MirStatement.Of(kind, place, source ?? At));
        void Assign(MirPlace place, MirRvalue rvalue, SourceInfo? source = null) => body.Push(entry, MirStatement.Assign(place, rvalue, source ?? At));
        int Operands(params MirOperand[] operands) => AddOperands(body, operands);
        MirOperand Const(MirConstKind kind, BoundType type, Int128 bits = default, object? value = null) => MirOperand.Const(body.Const(new(kind, type, bits, value)));
        var one = Const(MirConstKind.Integer, BoundType.I32, 1);
        var input = MirOperand.Copy(x); // The Value call's argument.

        Push(MirStatementKind.StorageLive, point, new(default, scope, true));
        Assign(index, MirRvalue.Use(Operands(Const(MirConstKind.Integer, BoundType.ISize, 2))));
        Assign(result, MirRvalue.Binary(MirBinaryOp.Add, Operands(MirOperand.Copy(x), one), false));
        Assign(reference, MirRvalue.Ref(MirRefKind.Shared, x, body.TypeOf(reference)!));
        Assign(result, MirRvalue.Use(Operands(MirOperand.Copy(body.Project(reference, MirProjectionKind.Deref, 0)))));
        Push(MirStatementKind.EndBorrow, reference);
        Push(MirStatementKind.FakeRead, body.Project(pair, MirProjectionKind.PairLayer, 0));
        Assign(pair, MirRvalue.Acquire(Operands(MirOperand.PairAcquire(0, pair)), MirRefKind.Uniq));
        Assign(pair, MirRvalue.Acquire(Operands(MirOperand.PairAcquire(0, pair)), MirRefKind.TwoPhaseUniq));
        var layer = body.Project(pair, MirProjectionKind.PairLayer, 0);
        var token = Const(MirConstKind.TypeToken, body.TypeOf(layer)!);
        Assign(pair, MirRvalue.Adapt(Operands(MirOperand.Copy(layer), token), layer, types.Pair, shorthand: true, input: true));
        Assign(flag, MirRvalue.Opaque(BoundType.Boolean, body.AddOpaque(default, [point.Root])));
        Assign(index, MirRvalue.Discriminant(shape));
        Assign(index, MirRvalue.Len(array));
        Assign(shape, MirRvalue.Aggregate(MirAggregateKind.Adt, Operands(one, one), 2, types.Shape, 1));
        Assign(pairs, MirRvalue.Aggregate(MirAggregateKind.Tuple, Operands(one, Const(MirConstKind.Bool, BoundType.Boolean, 1)), 2, tuple));
        Assign(array, MirRvalue.Aggregate(MirAggregateKind.FixedArray, Operands(one, one, one), 3, types.Array));
        Assign(array, MirRvalue.Aggregate(MirAggregateKind.FixedArray, Operands(), 0, types.Array)); // SPEC 4.3.4 noinit: no element stored.
        Assign(array, MirRvalue.Aggregate(MirAggregateKind.Fill, Operands(one), 1, types.Array)); // SPEC 4.3.3: every element a copy of one.
        Assign(point, MirRvalue.Aggregate(MirAggregateKind.Adt, Operands(MirOperand.Move(parent), one, Const(MirConstKind.Bool, BoundType.Boolean, 1)), 3, types.Point));
        Assign(result, MirRvalue.Use(Operands(MirOperand.Copy(body.Project(array, MirProjectionKind.Index, index.Root)))));
        Assign(result, MirRvalue.Use(Operands(MirOperand.Copy(body.Project(array, MirProjectionKind.ConstIndex, 2)))));
        Assign(result, MirRvalue.Use(Operands(MirOperand.Copy(body.Project(body.Project(shape, MirProjectionKind.Downcast, 1), MirProjectionKind.Field, 1)))));
        Assign(result, MirRvalue.Use(Operands(MirOperand.Copy(body.Project(body.Project(point, MirProjectionKind.Base, 1), MirProjectionKind.Field, 0)))));
        Assign(result, MirRvalue.Unary(MirUnaryOp.Neg, Operands(MirOperand.Copy(result)), false));
        Assign(index, MirRvalue.Cast(MirCastKind.Integer, Operands(MirOperand.Copy(result)), BoundType.ISize));
        switch (broken)
        {
            case MirViolation.UnknownKind:
                Assign(result, new((MirRvalueKind)200), Broken);
                break;
            case MirViolation.Scope:
                Push(MirStatementKind.FakeRead, index, Broken with { Scope = 9 });
                break;
            case MirViolation.StorageLocal:
                Push(MirStatementKind.StorageLive, pairs, Broken);
                break;
            case MirViolation.EndBorrowLocal:
                Push(MirStatementKind.EndBorrow, index, Broken);
                break;
            case MirViolation.Place:
                Push(MirStatementKind.FakeRead, new(99), Broken);
                break;
            case MirViolation.Projection:
                Push(MirStatementKind.FakeRead, body.Project(index, MirProjectionKind.Field, 0), Broken);
                break;
            case MirViolation.Const:
                Assign(result, MirRvalue.Use(Operands(MirOperand.Const(99))), Broken);
                break;
            case MirViolation.Binder:
                Assign(pairs, MirRvalue.Acquire(Operands(MirOperand.PairAcquire(0, pairs)), MirRefKind.Uniq), Broken);
                break;
            case MirViolation.OperandRange:
                Assign(result, MirRvalue.Use(999), Broken);
                break;
            case MirViolation.Arity:
                Assign(pairs, MirRvalue.Aggregate(MirAggregateKind.Tuple, Operands(one), 1, tuple), Broken);
                break;
            case MirViolation.Discriminant:
                Assign(index, MirRvalue.Discriminant(pairs), Broken);
                break;
            case MirViolation.Len:
                Assign(index, MirRvalue.Len(index), Broken);
                break;
            case MirViolation.Opaque:
                Assign(flag, MirRvalue.Opaque(BoundType.Boolean, 5), Broken);
                break;
            case MirViolation.NeverWrite: // R5-M38: an operator's value, which no whitelisted writer gives, into a Temp of Type Never.
                var never = MirModelTest.Local(body, BoundType.Never, MirLocalKind.Temp);
                Assign(never, MirRvalue.Unary(MirUnaryOp.Neg, Operands(MirOperand.Copy(never)), false), Broken);
                break;
            case MirViolation.Lent: // G136: the Value call's input, a Temp of an exclusive Type that a Ref writes, written again.
                input = MirOperand.Move(new(body.AddLocal(new(exclusive, MirLocalKind.Temp, Broken))));
                Assign(input.Place, MirRvalue.Ref(MirRefKind.Uniq, x, exclusive));
                Assign(input.Place, MirRvalue.Ref(MirRefKind.Uniq, x, exclusive));
                break;
            case MirViolation.Unensured: // G191: a static no staticEnsure of the body names.
                Push(MirStatementKind.FakeRead, body.Static(new(types.Generic.SymbolOf()!, BoundType.I32, false, false)), Broken);
                break;
            case MirViolation.SharedPath: // W1 rule 1: a write through the shared reference.
                Assign(body.Project(reference, MirProjectionKind.Deref, 0), MirRvalue.Use(Operands(one)), Broken);
                break;
        }

        body.AddCase(0, abort);
        body.Terminate(entry, MirTerminator.SwitchInt(MirOperand.Copy(flag), 0, 1, call, At));
        var text = Operands(Const(MirConstKind.String, BoundType.String, value: "abort"));
        var stopped = MirModelTest.Local(body, BoundType.Never, MirLocalKind.Temp); // Only the diverging Abort writes it (G131).
        var abortCall = broken == MirViolation.Callee
            ? MirTerminator.Call(MirCallee.Intrinsic(IntrinsicId.Abort), text, 1, result, call, Broken)
            : MirTerminator.Call(MirCallee.Intrinsic(IntrinsicId.Abort), text, 1, stopped, -1, At) with { CheckTarget = call };
        body.Terminate(abort, abortCall);
        var signature = new MirCallee(MirCalleeKind.Value, Arg: body.Const(new(MirConstKind.TypeToken, body.TypeOf(function)!.Components[0])));
        var called = broken == MirViolation.StaticSlot // F10: a staticEnsure of a slot past the body's one static.
            ? MirTerminator.Call(MirCallee.Intrinsic(IntrinsicId.StaticEnsure), Operands(Const(MirConstKind.Integer, BoundType.ISize, 1)), 1, result, drop, Broken)
            : MirTerminator.Call(signature, Operands(input), 1, result, drop, At) with { Operand = MirOperand.Copy(function) };
        body.Terminate(call, called);
        body.Terminate(drop, broken == MirViolation.CheckTarget ? new(MirTerminatorKind.Drop, Broken, exit, check, Place: point) : MirTerminator.Drop(point, exit, At));
        body.Push(exit, MirStatement.Of(MirStatementKind.StorageDead, point, At));
        if (broken == MirViolation.Diverged)
        {
            body.Push(exit, MirStatement.Of(MirStatementKind.FakeRead, stopped, Broken)); // A runtime edge reaches exit.
        }

        body.Terminate(exit, MirTerminator.Goto(ensure, At, check));
        var ensured = MirModelTest.Local(body, BoundType.Unit, MirLocalKind.Temp); // G191: the static's staticEnsure.
        body.Terminate(ensure, MirTerminator.Call(MirCallee.Intrinsic(IntrinsicId.StaticEnsure), Operands(Const(MirConstKind.Integer, BoundType.ISize, 0)), 1, ensured, done, At));
        body.Push(done, MirStatement.Of(MirStatementKind.FakeRead, shared, At)); // After its staticEnsure on every runtime path (G191 rule (3)).
        body.Terminate(done, MirTerminator.Return(At));
        body.Push(check, MirStatement.Of(MirStatementKind.FakeRead, stopped, At)); // Only a check edge reaches check.
        body.Terminate(check, MirTerminator.Unreachable(At));
        body.AddGuessedSite(At.Syntax);
        body.AddMustNotComplete(new(abort, check, At.Syntax, 0, 1));
        switch (broken)
        {
            case MirViolation.Unterminated:
                body.AddBlock();
                break;
            case MirViolation.TargetRange:
                body.Terminate(body.AddBlock(), MirTerminator.Goto(99, Broken));
                body.AddMustNotComplete(new(99, check, Broken.Syntax, 0, 0));
                body.AddMustNotComplete(new(entry, 99, Broken.Syntax, 0, 0));
                body.AddMustNotComplete(new(entry, done, Broken.Syntax, 0, 0)); // An end that is a Return.
                body.AddMustNotComplete(new(abort, check, Broken.Syntax, 1, 1)); // A guessed-site range past the body's sites (R5-M186).
                var checkedEnd = body.AddBlock();
                body.Terminate(checkedEnd, MirTerminator.Unreachable(Broken, check));
                body.AddMustNotComplete(new(entry, checkedEnd, Broken.Syntax, 0, 0)); // An end with a check target.
                break;
            case MirViolation.Unreachable:
                body.Terminate(body.AddBlock(), MirTerminator.Return(Broken));
                break;
            case MirViolation.Entry: // R5-M90: an edge to block 0, the body's first location.
                body.Terminate(done, MirTerminator.Goto(entry, Broken));
                break;
            case MirViolation.StatementRange:
                Unsafe.AsRef(in body.Blocks[exit]).Count = 99;
                break;
        }

        return body;
    }

    // A body of one keyed Storage at Broken, `_2 = storage(_1, copy _3, const type K)` over an Array<i32> (PLAN G597): its key the named
    // parameter's Type of a generic k; "flag" a bool start under the key R and "value" an Integer constant for the token; of its three
    // operands (the token twice) the count given.
    internal static (MirBody Body, TyCtxt Types) KeyedStorage(string form, int count)
    {
        var c = MirBuildTest.Compile(
            "func k<R, S, E>(v: Array<i32>, r: R, from: Range<S, End>, both: Range<S, E>, full: Range<Start, End>, to: Range<Start, E>, " +
            "at: Range<i32, E>, back: Range<FromEnd<i32>, End>, resolved: ResolvedRange) -> ()\n" +
            "    R is PositionRange\n    S is Position\n    E is Position\n    return ()\n");
        Assert.True(c.Binding.Result.IsComplete);
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "k");
        BoundType Of(string name) => function.Parameters.Single(x => x.InternalName == name).Type.TypeOf()!;
        var body = new MirBody();
        body.Reset(new(function.SymbolOf()!, MirBodyKind.Fn), default);
        body.AddLocal(new(BoundType.Unit, MirLocalKind.Return, At));
        var (array, pointer) = (MirModelTest.Local(body, Of("v")), MirModelTest.Local(body, c.Types.Pointer(BoundType.I32)));
        var start = MirOperand.Copy(MirModelTest.Local(body, form == "flag" ? BoundType.Boolean : BoundType.ISize));
        MirConst key = form == "value" ? new(MirConstKind.Integer, BoundType.ISize, 0) : new(MirConstKind.TypeToken, Of(form == "flag" ? "r" : form));
        var token = MirOperand.Const(body.Const(key));
        var block = body.AddBlock();
        var keyed = MirRvalue.Storage(array, body.TypeOf(pointer)!, AddOperands(body, [start, token, token])) with { Count = count };
        body.Push(block, MirStatement.Assign(pointer, keyed, Broken));
        body.Terminate(block, MirTerminator.Return(At));
        return (body, c.Types);
    }

    // The compilation of a PrimitiveInteger T and a PrimitiveInteger projection U.(HasItem).Item, and eight Types: its parameters' T,
    // Option<T>, ref/T, Wrapping<T>, U.(HasItem).Item and Wrapping<U.(HasItem).Item> (0-5), then Wrapping over Option<T>, a non-abstract
    // Type with an abstract part (6), and raw/T (7).
    private static (Compilation Compilation, BoundType[] Types) GenericIntegers()
    {
        var c = MirBuildTest.Compile("contract HasItem\n    associate Item\n" +
            "func w<T, U>(a: T, b: Option<T>, c: ref/T, d: Wrapping<T>, e: U.(HasItem).Item, f: Wrapping<U.(HasItem).Item>) -> ()\n" +
            "    T is PrimitiveInteger\n    U is HasItem\n    U.(HasItem).Item is PrimitiveInteger\n    return ()\n");
        Assert.True(c.Binding.Result.IsComplete);
        var w = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "w");
        var types = w.Parameters.Select(static x => x.Type.TypeOf()!).ToList();
        Assert.Equal(
            [BoundTypeKind.Parameter, BoundTypeKind.Constructed, BoundTypeKind.Semantics, BoundTypeKind.Constructed, BoundTypeKind.AssociatedProjection,
                BoundTypeKind.Constructed],
            types.Select(static x => x.Kind));
        types.Add(c.Types.Intern(BoundTypeKind.Constructed, types[3].Symbol, SemanticsKind.Owner, [types[1]]));
        types.Add(c.Types.Pointer(types[0]));
        Assert.Equal(BoundTypeKind.Constructed, types[6].Kind);
        return (c, [.. types]);
    }

    // A body assigning one Integer constant of a Type to a local of it: no finding when it holds, else one Const finding at the Assign.
    private static void AssertIntegerConstant(BoundType type, Int128 bits, bool holds, TyCtxt? types = null)
    {
        var body = new MirBody();
        body.Reset(new(null, MirBodyKind.Fn), default);
        body.AddLocal(new(BoundType.Unit, MirLocalKind.Return, At));
        var (held, block) = (MirModelTest.Local(body, type), body.AddBlock());
        var constant = MirOperand.Const(body.Const(new(MirConstKind.Integer, type, bits)));
        body.Push(block, MirStatement.Assign(held, MirRvalue.Use(AddOperands(body, [constant])), Broken));
        body.Terminate(block, MirTerminator.Return(At));
        MirViolation[] expected = holds ? [] : [MirViolation.Const];
        Assert.Equal(expected.Select(static x => (x, Broken.Syntax)), Validate(body, types).Select(static x => (x.Rule, x.Subject)));
    }

    // A compiled body is validated with its own compilation's Type context, whose Binding the Copy rule reads (DECISIONS BU-88).
    private static List<MirFinding> Validate(MirBody body, TyCtxt? types = null)
    {
        var findings = new List<MirFinding>();
        new MirValidator(types ?? Fixture.Value.Compilation.Types).Validate(body, findings);
        return findings;
    }

    // Appends operands together and gives the first one's index.
    private static int AddOperands(MirBody body, MirOperand[] operands)
    {
        var start = body.Operands.Length;
        foreach (var operand in operands)
        {
            body.AddOperand(operand);
        }

        return start;
    }

    // What a location of a body uses, or under the selected view of it.
    private static string[] Uses(MirBody body, int block, int index, MirCases? view = null)
    {
        var recorder = new Recorder([]);
        if (view is null)
        {
            MirUses.ForEach(body, new(block, index), ref recorder);
        }
        else
        {
            MirUses.ForEach(view, new(block, index), ref recorder);
        }

        return recorder.Log.ToArray();
    }

    private readonly struct Recorder(List<string> log) : IMirUses
    {
        internal List<string> Log => log;

        public void Operand(in MirOperand operand) => log.Add(operand.Kind.ToString());

        public void Place(MirPlace place, MirAccess access, MirRefKind kind)
            => log.Add($"{place.Root}{(place.Projection == 0 ? string.Empty : "*")} {access}{((access & MirAccess.Borrows) != 0 ? " " + kind : string.Empty)}");

        public void Effect(MirEffect effects, int site) => log.Add($"{effects} {site}");
    }

    private struct Counter : IMirUses
    {
        internal int Places { get; private set; }

        public void Operand(in MirOperand operand)
        {
        }

        public void Place(MirPlace place, MirAccess access, MirRefKind kind) => this.Places++;

        public void Effect(MirEffect effects, int site)
        {
        }
    }
}
