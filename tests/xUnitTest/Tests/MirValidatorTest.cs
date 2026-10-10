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

    // An rvalue's operands before its Place, the index locals of a Place before it, a destination last; static accesses, pair cases,
    // intrinsic and Opaque effects as the rows give them; a diverging Call writes no destination.
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
        Assign(result, MirRvalue.Use(Operands(MirOperand.PairAcquire(0, pair))));
        body.Push(entry, MirStatement.Of(MirStatementKind.FakeRead, body.Project(pair, MirProjectionKind.PairLayer, 0), At));
        Assign(exclusive, MirRvalue.Ref(MirRefKind.Uniq, shared, body.TypeOf(exclusive)!));
        Assign(pointer, MirRvalue.Cast(MirCastKind.IntToPointer, Operands(MirOperand.Copy(index)), body.TypeOf(pointer)!));
        Assign(result, MirRvalue.Opaque(BoundType.I32, body.AddOpaque(default, [array.Root])));
        body.Terminate(entry, MirTerminator.Call(MirCallee.Intrinsic(IntrinsicId.MakeObj), Operands(MirOperand.Copy(result)), 1, result, abort, At));
        var text = Operands(MirOperand.Const(body.Const(new(MirConstKind.String, BoundType.String, Value: "abort"))));
        body.Terminate(abort, MirTerminator.Call(MirCallee.Intrinsic(IntrinsicId.Abort), text, 1, result, -1, At, drop));
        body.Terminate(drop, MirTerminator.Drop(array, exit, At));
        body.Terminate(exit, MirTerminator.Return(At));
        Assert.Empty(Validate(body));

        Assert.Equal(["Copy", "1* Reads", "Const", "Abort -1", "0 Writes, Defines"], Uses(body, entry, 0));
        Assert.Equal(["Copy", "4 Reads", "Abort -1", "3* Reads", "0 Writes, Defines"], Uses(body, entry, 1));
        Assert.Equal(["-1 Reads", "StaticRead, RawStaticRead -1"], Uses(body, entry, 2));
        Assert.Equal(["PairAcquire", "2 Reads, CaseDependent", "0 Writes, Defines"], Uses(body, entry, 3));
        Assert.Equal(["2* Reads, CaseDependent"], Uses(body, entry, 4));
        Assert.Equal(["-1 Borrows Uniq", "StaticWrite -1", "5 Writes, Defines"], Uses(body, entry, 5));
        Assert.Equal(["Copy", "4 Reads", "IntToPointer -1", "6 Writes, Defines"], Uses(body, entry, 6));
        Assert.Equal(["Unknown 0", "3 Reads, Writes, Borrows Shared", "0 Writes, Defines"], Uses(body, entry, 7));
        Assert.Equal(["Copy", "0 Reads", "Allocation -1", "0 Writes, Defines"], Uses(body, entry, 8));
        Assert.Equal(["Const", "Abort -1"], Uses(body, abort, 0));
        Assert.Equal(["3 DropUse"], Uses(body, drop, 0));
        Assert.Equal(["0 Moves"], Uses(body, exit, 0));
        Assert.Equal(new[] { 1, 0, 1, 0 }, body.Blocks.ToArray().Select(static x => MirUses.Successors(x.Terminator)));
    }

    // A body with every statement, rvalue, terminator, operand and projection kind, an intrinsic and a Value callee, a static, an Opaque
    // site, a scope, a pair binder and a block that only a check edge reaches.
    [Fact]
    public void ValidBodyHasNoFinding() => Assert.Empty(Validate(Build(null)));

    [Theory]
    [MemberData(nameof(Rules))]
    public void EachRuleRejectsTheBodyThatBreaksIt(string name)
    {
        var rule = Enum.Parse<MirViolation>(name);
        var findings = Validate(Build(rule));
        Assert.NotEmpty(findings);
        Assert.All(findings, x => Assert.Equal(rule, x.Rule));
        Assert.All(findings, x => Assert.Equal(rule is MirViolation.NoBlocks or MirViolation.Unterminated or MirViolation.StatementRange ? default : Broken.Syntax, x.Subject));
    }

    // V11: validating a body again, and enumerating its uses, allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmValidationAllocatesNothing()
    {
        var body = Build(null);
        var validator = new MirValidator();
        var findings = new List<MirFinding>();
        var counter = default(Counter);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            validator.Validate(body, findings);
            for (var block = 0; block < body.Blocks.Length; block++)
            {
                for (var i = 0; i <= body.Blocks[block].Count; i++)
                {
                    MirUses.ForEach(body, new(block, i), ref counter);
                }
            }
        }));
        Assert.Empty(findings);
        Assert.True(counter.Places > 0);
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
        Assign(result, MirRvalue.Use(Operands(MirOperand.PairAcquire(0, pair))));
        Assign(flag, MirRvalue.Opaque(BoundType.Boolean, body.AddOpaque(default, [point.Root])));
        Assign(index, MirRvalue.Discriminant(shape));
        Assign(index, MirRvalue.Len(array));
        Assign(shape, MirRvalue.Aggregate(MirAggregateKind.Adt, Operands(one, one), 2, types.Shape, 1));
        Assign(pairs, MirRvalue.Aggregate(MirAggregateKind.Tuple, Operands(one, Const(MirConstKind.Bool, BoundType.Boolean, 1)), 2, tuple));
        Assign(array, MirRvalue.Aggregate(MirAggregateKind.FixedArray, Operands(one, one, one), 3, types.Array));
        Assign(point, MirRvalue.Aggregate(MirAggregateKind.Adt, Operands(one, one, one), 3, types.Point));
        Assign(result, MirRvalue.Use(Operands(MirOperand.Copy(body.Project(array, MirProjectionKind.Index, index.Root)))));
        Assign(result, MirRvalue.Use(Operands(MirOperand.Copy(body.Project(array, MirProjectionKind.ConstIndex, 2)))));
        Assign(result, MirRvalue.Use(Operands(MirOperand.Copy(body.Project(body.Project(shape, MirProjectionKind.Downcast, 1), MirProjectionKind.Field, 1)))));
        Assign(result, MirRvalue.Use(Operands(MirOperand.Copy(body.Project(body.Project(point, MirProjectionKind.Base, 1), MirProjectionKind.Field, 0)))));
        Assign(result, MirRvalue.Unary(MirUnaryOp.Neg, Operands(MirOperand.Copy(result)), false));
        Assign(index, MirRvalue.Cast(MirCastKind.Integer, Operands(MirOperand.Copy(result)), BoundType.ISize));
        switch (broken)
        {
            case MirViolation.UnknownKind:
                Assign(result, new((MirRvalueKind)9), Broken);
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
                Assign(result, MirRvalue.Use(Operands(MirOperand.PairAcquire(0, pairs))), Broken);
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
        var abortCall = broken == MirViolation.Callee ? MirTerminator.Call(MirCallee.Intrinsic(IntrinsicId.Abort), text, 1, result, call, Broken) : MirTerminator.Call(MirCallee.Intrinsic(IntrinsicId.Abort), text, 1, result, -1, At, call);
        body.Terminate(abort, abortCall);
        body.Terminate(call, MirTerminator.Call(new(MirCalleeKind.Value), Operands(MirOperand.Copy(x)), 1, result, drop, At, value: MirOperand.Copy(function)));
        body.Terminate(drop, broken == MirViolation.CheckTarget ? new(MirTerminatorKind.Drop, Broken, exit, check, Place: point) : MirTerminator.Drop(point, exit, At));
        body.Push(exit, MirStatement.Of(MirStatementKind.StorageDead, point, At));
        body.Terminate(exit, MirTerminator.Goto(done, At, check));
        body.Terminate(done, MirTerminator.Return(At));
        body.Terminate(check, MirTerminator.Unreachable(At));
        switch (broken)
        {
            case MirViolation.Unterminated:
                body.AddBlock();
                break;
            case MirViolation.TargetRange:
                body.Terminate(body.AddBlock(), MirTerminator.Goto(99, Broken));
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
        new MirValidator().Validate(body, findings);
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

    private static string[] Uses(MirBody body, int block, int index)
    {
        var recorder = new Recorder([]);
        MirUses.ForEach(body, new(block, index), ref recorder);
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
