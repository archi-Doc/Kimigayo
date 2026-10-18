// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using System.Runtime.CompilerServices;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>The descriptor table and the MIR validator (plan 5.4, 14-5): every kind has a row; <see cref="MirUses"/> reports what a
/// location uses and its effects by the rows; a body that every rule accepts has no finding, and each rule rejects a body that breaks only
/// it, at the syntax of what breaks it; a warm validation allocates nothing.</summary>
[Collection(nameof(MirValidatorTest))] // Its fixture's TyCtxt, which MirDumpTest shares, is not thread-safe.
public sealed class MirValidatorTest
{
    private static readonly SourceInfo At = default;

    // The source of what a rule's body changes.
    private static readonly SourceInfo Broken = new(new(0, 77), 0, true);

    private static readonly Lazy<(Compilation Compilation, MirModelTest.Types Types)> Fixture = new(MirModelTest.Fixture);

    public static TheoryData<string> Rules => new(Enum.GetNames<MirViolation>());

    // Each closed vocabulary and the intrinsic table have one overload of MirDescriptors.Of, and each member a row; a value outside the
    // enumeration and IntrinsicId.None have none.
    [Fact]
    public void EveryKindHasADescriptorRow()
    {
        var overloads = typeof(MirDescriptors).GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Where(static x => x.Name == nameof(MirDescriptors.Of)).ToArray();
        Assert.Equal(
            new[] { typeof(IntrinsicId), typeof(MirOperandKind), typeof(MirProjectionKind), typeof(MirRvalueKind), typeof(MirStatementKind), typeof(MirTerminatorKind) },
            overloads.Select(static x => x.GetParameters()[0].ParameterType).OrderBy(static x => x.Name, StringComparer.Ordinal));
        foreach (var overload in overloads)
        {
            var kind = overload.GetParameters()[0].ParameterType;
            foreach (var value in Enum.GetValues(kind))
            {
                Assert.True(((MirDescriptor)overload.Invoke(null, [value])!).Defined != value is IntrinsicId.None, $"{kind.Name}.{value}");
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
        var (entry, abort, drop, exit) = (body.AddBlock(), body.AddBlock(), body.AddBlock(), body.AddBlock());
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
        Assert.Equal(new[] { 1, 0, 1, 0 }, body.Blocks.ToArray().Select(static x => MirUses.Successors(x.Terminator)));

        var cases = new MirCases(c.Types);
        cases.Select(body, 0);
        Assert.Equal(["Copy", "7 Reads", "2 Writes, Defines"], Uses(body, entry, 8, cases));
        Assert.Equal(Uses(body, entry, 7), Uses(body, entry, 7, cases));
        cases.Select(body, 1);
        Assert.Equal(["7 Borrows Uniq", "2 Writes, Defines"], Uses(body, entry, 8, cases));
    }

    // A body with every statement, rvalue, terminator, operand and projection kind, an intrinsic and a Value callee, a static, an Opaque
    // site, a scope, a pair binder, a block that only a check edge reaches and a MustNotComplete row; and the Type tier's body, every form it
    // checks in a valid use.
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
    // it: an Assign's rvalue, an aggregate's operand, a call's argument (the receiver first, a default template's shared references) and
    // result, a Value callee's argument and _0 at a Return disagreeing with their Types; an operator's operands, a shift count, a raw
    // pointer's displacement, a comparison's operands below their layers, a Cast, a Ref, an object upcast and a branch's operand that their
    // kind does not admit; an Adapt whose operand is no by-value acquisition, whose source token is not its operand's Type, that a case
    // view selects no operation for (i32 to f64), or whose object borrow there takes a Place other than the payload, and one whose Op has a
    // bit besides its shorthand and input flags, of an unknown kind; an intrinsic callee whose function is not the library declaration of a compiler function it
    // serves (A3 E14); a Value callee whose signature is no TypeToken constant (C8), and a Closure callee given more arguments than its
    // declared signature has inputs. A local an Opaque value defines has an unknown Type (the replaced syntax may have
    // none), which any use admits; the same use of a local a Unit constant defines is a finding. So is the Never value's at every operand
    // position (a Use, an operator, an aggregate; SPEC 3.8, DECISIONS R4-M62), but a ref/Never is no ref/i32.
    [Theory]
    [InlineData("assign", "Type")]
    [InlineData("component", "Type")]
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
    [InlineData("upcast", "Operation")]
    [InlineData("switch", "Operation")]
    [InlineData("opaque", "Type")]
    [InlineData("never", "Type")]
    [InlineData("acquisition", "Operation")]
    [InlineData("token", "Type")]
    [InlineData("adapt", "Operation")]
    [InlineData("payload", "Operation")]
    [InlineData("shorthand", "UnknownKind")]
    public void TypeTierRejectsEachForm(string form, string rule)
    {
        var findings = Validate(Typed(form));
        Assert.NotEmpty(findings);
        Assert.All(findings, x => Assert.Equal((Enum.Parse<MirViolation>(rule), Broken.Syntax), (x.Rule, x.Subject)));
    }

    // SPEC 8.2, 15.3: Types that differ only by their Origins agree, and so do Types a body's Type-identity premise equates (E and S under
    // `E is S`), which the corpus's generic bodies rely on; other Types disagree.
    [Fact]
    public void TypesAgreeModuloOriginsAndPremises()
    {
        var c = MirBuildTest.Compile("func pick(x: ref/i32) -> ref/i32 during x => x\nfunc same<S, E>(a: S, b: E) -> S\n    E is S\n    return b\n");
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
    // ProjectedType does not give.
    [Fact]
    public void PairFormsNameTheirLayersBinder()
    {
        var c = MirBuildTest.Compile("func both<s/T, t/U>(a: s/T, b: t/U) -> ()\n    s is owner or uniq\n    t is owner or ref\n    T is Copy\n    U is Copy\n    _ = a\n    _ = b\n");
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(static x => x.Name == "both");
        var body = new MirBody();
        body.Reset(new(function.SymbolOf()!, MirBodyKind.Fn), default);
        body.AddLocal(new(BoundType.Unit, MirLocalKind.Return, At));
        var (pa, pb) = (function.Parameters[0].Type.TypeOf()!, function.Parameters[1].Type.TypeOf()!);
        var (a, b) = (MirModelTest.Local(body, pa, MirLocalKind.Arg), MirModelTest.Local(body, pb, MirLocalKind.Arg));
        _ = (TyCtxt.Layer(pa, out var t, out _), TyCtxt.Layer(pb, out var u, out _));
        PairBinder.Collect(function, body.Binders);
        var block = body.AddBlock();
        void Read(MirOperand operand, BoundType type, SourceInfo source) => body.Push(block, MirStatement.Assign(
            MirModelTest.Local(body, type),
            operand.Kind == MirOperandKind.PairAcquire ? MirRvalue.Acquire(body.AddOperand(operand), MirRefKind.Uniq) : MirRvalue.Use(body.AddOperand(operand)),
            source));
        Read(MirOperand.PairAcquire(0, a), pa, At);
        Read(MirOperand.PairAcquire(1, b), pb, At);
        Read(MirOperand.Copy(body.Project(a, MirProjectionKind.PairLayer, 0)), t!, At);
        Read(MirOperand.Copy(body.Project(b, MirProjectionKind.PairLayer, 1)), u!, At);
        body.Terminate(block, MirTerminator.Return(At));
        Assert.Empty(Validate(body));

        Read(MirOperand.PairAcquire(1, a), pa, Broken);
        Read(MirOperand.Copy(body.Project(b, MirProjectionKind.PairLayer, 0)), u!, Broken);
        Assert.Equal([(MirViolation.Binder, Broken.Syntax), (MirViolation.Projection, Broken.Syntax)], Validate(body).Select(static x => (x.Rule, x.Subject)));
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
        var c = MirBuildTest.Compile("func both<s/T, t/U>(a: s/T, b: t/U) -> ()\n    s is owner or uniq\n    t is owner or ref\n    T is Copy\n    U is Copy\n    _ = a\n    _ = b\n");
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
        Assign(broken == "projection" ? body.Project(tuple, MirProjectionKind.Field, 0) : first, MirRvalue.Acquire(body.AddOperand(acquire), MirRefKind.Uniq), "projection");
        Assign(tuple, MirRvalue.Aggregate(MirAggregateKind.Tuple, AddOperands(body, [broken == "aggregate" ? acquire : MirOperand.Move(first), one]), 2, body.TypeOf(tuple)!), "aggregate");
        Assign(number, MirRvalue.Use(body.AddOperand(one)) with { Op = (byte)(broken == "constant" ? MirRefKind.Uniq : MirRefKind.Shared) }, "constant");
        var passed = AddOperands(body, [broken == "argument" ? acquire : MirOperand.Move(first), MirOperand.Move(second)]);
        body.Terminate(block, MirTerminator.Call(new(MirCalleeKind.Fn, function.SymbolOf()), passed, 2, new(0), next, Where("argument")));
        body.Terminate(next, MirTerminator.Return(At));
        Assert.Equal(broken is null ? [] : [(MirViolation.Operation, Broken.Syntax)], Validate(body).Select(static x => (x.Rule, x.Subject)));
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

    // V11: validating a body again, the Type tier's and its Adapt's case views included, and enumerating its uses, of the body and under
    // each case view, allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmValidationAllocatesNothing()
    {
        var (body, typed) = (Build(null), Typed(null));
        var validator = new MirValidator(Fixture.Value.Compilation.Types);
        var cases = new MirCases(Fixture.Value.Compilation.Types);
        var findings = new List<MirFinding>();
        var counter = default(Counter);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            validator.Validate(body, findings);
            validator.Validate(typed, findings);
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
        var callable = Local(c.Types.Intern(BoundTypeKind.Function, null, SemanticsKind.Owner, [c.Types.Tuple([BoundType.I32]), BoundType.Boolean]));
        var (adapted, borrowed) = (broken == "adapt" ? real : a, Local(c.Types.Reference(SemanticsKind.ObjRef, types.Point)));
        var blocks = new[] { body.AddBlock(), body.AddBlock(), body.AddBlock(), body.AddBlock(), body.AddBlock(), body.AddBlock() };
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
        Assign(point, MirRvalue.Aggregate(MirAggregateKind.Adt, Operands(MirOperand.Move(parent), one, yes), 3, types.Point));
        Assign(shape, MirRvalue.Aggregate(MirAggregateKind.Adt, Operands(one, one), 2, types.Shape, 1));
        Assign(array, MirRvalue.Aggregate(MirAggregateKind.FixedArray, Operands(one, one, one), 3, types.Array));
        Assign(index, MirRvalue.Len(array));
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
        var signature = broken == "signature" ? one.Arg : Token(body.TypeOf(callable)!).Arg;
        var (callee, arguments) = broken == "arity"
            ? (Local(c.Types.Intern(BoundTypeKind.Closure, types.Function, SemanticsKind.Owner, [])), Operands(MirOperand.Copy(a), MirOperand.Copy(a)))
            : (callable, Operands(MirOperand.Copy(broken == "value" ? flag : a)));
        var at = Where(broken is "signature" or "arity" ? broken : "value");
        var value = MirTerminator.Call(new(MirCalleeKind.Value, Arg: signature), arguments, broken == "arity" ? 2 : 1, flag, blocks[3], at);
        body.Terminate(blocks[2], value with { Operand = MirOperand.Copy(callee) });
        body.Terminate(blocks[3], MirTerminator.Call(new(MirCalleeKind.Fn, Function("d"), Arg: 2), Operands(broken == "template" ? MirOperand.Copy(a) : MirOperand.Copy(view)), 1, flag, blocks[4], Where("template")));
        var (receiver, by) = (MirOperand.Copy(gauge), MirOperand.Copy(a));
        body.Terminate(blocks[4], MirTerminator.Call(new(MirCalleeKind.Fn, Function("scaled")), broken == "receiver" ? Operands(by, receiver) : Operands(receiver, by), 2, a, blocks[5], Where("receiver")));
        body.Terminate(blocks[5], MirTerminator.Return(Where("return")));
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
        var function = MirModelTest.Local(body, c.Types.Intern(BoundTypeKind.Function, null, SemanticsKind.Owner, [c.Types.Tuple([BoundType.I32]), BoundType.I32]));
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

        var (entry, abort, call, drop, exit, done, check) = (body.AddBlock(), body.AddBlock(), body.AddBlock(), body.AddBlock(), body.AddBlock(), body.AddBlock(), body.AddBlock());
        var result = new MirPlace(0);
        var x = body.Project(pairs, MirProjectionKind.Field, 0);
        void Push(MirStatementKind kind, MirPlace place, SourceInfo? source = null) => body.Push(entry, MirStatement.Of(kind, place, source ?? At));
        void Assign(MirPlace place, MirRvalue rvalue, SourceInfo? source = null) => body.Push(entry, MirStatement.Assign(place, rvalue, source ?? At));
        int Operands(params MirOperand[] operands) => AddOperands(body, operands);
        MirOperand Const(MirConstKind kind, BoundType type, Int128 bits = default, object? value = null) => MirOperand.Const(body.Const(new(kind, type, bits, value)));
        var one = Const(MirConstKind.Integer, BoundType.I32, 1);

        Push(MirStatementKind.StorageLive, point, new(default, scope, true));
        Assign(index, MirRvalue.Use(Operands(Const(MirConstKind.Integer, BoundType.ISize, 2))));
        Assign(result, MirRvalue.Binary(MirBinaryOp.Add, Operands(MirOperand.Copy(x), one), false));
        Assign(reference, MirRvalue.Ref(MirRefKind.Shared, x, body.TypeOf(reference)!));
        Assign(result, MirRvalue.Use(Operands(MirOperand.Copy(body.Project(reference, MirProjectionKind.Deref, 0)))));
        Push(MirStatementKind.EndBorrow, reference);
        Push(MirStatementKind.FakeRead, shared);
        Push(MirStatementKind.FakeRead, body.Project(pair, MirProjectionKind.PairLayer, 0));
        Assign(pair, MirRvalue.Acquire(Operands(MirOperand.PairAcquire(0, pair)), MirRefKind.Uniq));
        Assign(pair, MirRvalue.Acquire(Operands(MirOperand.PairAcquire(0, pair)), MirRefKind.TwoPhaseUniq));
        var layer = body.Project(pair, MirProjectionKind.PairLayer, 0);
        Assign(pair, MirRvalue.Adapt(Operands(MirOperand.Copy(layer), Const(MirConstKind.TypeToken, body.TypeOf(layer)!)), layer, types.Pair, shorthand: true, input: true));
        Assign(flag, MirRvalue.Opaque(BoundType.Boolean, body.AddOpaque(default, [point.Root])));
        Assign(index, MirRvalue.Discriminant(shape));
        Assign(index, MirRvalue.Len(array));
        Assign(shape, MirRvalue.Aggregate(MirAggregateKind.Adt, Operands(one, one), 2, types.Shape, 1));
        Assign(pairs, MirRvalue.Aggregate(MirAggregateKind.Tuple, Operands(one, Const(MirConstKind.Bool, BoundType.Boolean, 1)), 2, tuple));
        Assign(array, MirRvalue.Aggregate(MirAggregateKind.FixedArray, Operands(one, one, one), 3, types.Array));
        Assign(array, MirRvalue.Aggregate(MirAggregateKind.FixedArray, Operands(), 0, types.Array)); // SPEC 4.3.4 noinit: no element stored.
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
        }

        body.AddCase(0, abort);
        body.Terminate(entry, MirTerminator.SwitchInt(MirOperand.Copy(flag), 0, 1, call, At));
        var text = Operands(Const(MirConstKind.String, BoundType.String, value: "abort"));
        var stopped = MirModelTest.Local(body, BoundType.Never, MirLocalKind.Temp); // Only the diverging Abort writes it (G131).
        var abortCall = broken == MirViolation.Callee
            ? MirTerminator.Call(MirCallee.Intrinsic(IntrinsicId.Abort), text, 1, result, call, Broken)
            : MirTerminator.Call(MirCallee.Intrinsic(IntrinsicId.Abort), text, 1, stopped, -1, At) with { CheckTarget = call };
        body.Terminate(abort, abortCall);
        var signature = new MirCallee(MirCalleeKind.Value, Arg: body.Const(new(MirConstKind.TypeToken, body.TypeOf(function)!)));
        body.Terminate(call, MirTerminator.Call(signature, Operands(MirOperand.Copy(x)), 1, result, drop, At) with { Operand = MirOperand.Copy(function) });
        body.Terminate(drop, broken == MirViolation.CheckTarget ? new(MirTerminatorKind.Drop, Broken, exit, check, Place: point) : MirTerminator.Drop(point, exit, At));
        body.Push(exit, MirStatement.Of(MirStatementKind.StorageDead, point, At));
        if (broken == MirViolation.Diverged)
        {
            body.Push(exit, MirStatement.Of(MirStatementKind.FakeRead, stopped, Broken)); // A runtime edge reaches exit.
        }

        body.Terminate(exit, MirTerminator.Goto(done, At, check));
        body.Terminate(done, MirTerminator.Return(At));
        body.Push(check, MirStatement.Of(MirStatementKind.FakeRead, stopped, At)); // Only a check edge reaches check.
        body.Terminate(check, MirTerminator.Unreachable(At));
        body.AddMustNotComplete(new(abort, check, At.Syntax));
        switch (broken)
        {
            case MirViolation.Unterminated:
                body.AddBlock();
                break;
            case MirViolation.TargetRange:
                body.Terminate(body.AddBlock(), MirTerminator.Goto(99, Broken));
                body.AddMustNotComplete(new(99, check, Broken.Syntax));
                body.AddMustNotComplete(new(entry, 99, Broken.Syntax));
                body.AddMustNotComplete(new(entry, done, Broken.Syntax)); // An end that is a Return.
                var checkedEnd = body.AddBlock();
                body.Terminate(checkedEnd, MirTerminator.Unreachable(Broken, check));
                body.AddMustNotComplete(new(entry, checkedEnd, Broken.Syntax)); // An end with a check target.
                break;
            case MirViolation.Unreachable:
                body.Terminate(body.AddBlock(), MirTerminator.Return(Broken));
                break;
            case MirViolation.StatementRange:
                Unsafe.AsRef(in body.Blocks[exit]).Count = 99;
                break;
        }

        return body;
    }

    private static List<MirFinding> Validate(MirBody body)
    {
        var findings = new List<MirFinding>();
        new MirValidator(Fixture.Value.Compilation.Types).Validate(body, findings);
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
