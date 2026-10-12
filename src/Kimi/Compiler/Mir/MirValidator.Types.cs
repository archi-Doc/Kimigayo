// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

// The Type tier (SPEC 3.2-3.4, 13.2-13.5, 7.3, 8.9): the Types values flow between agree (MirTypes.Agree, modulo Origins, or as the
// body's premises make them), each operation admits its operands' Types, and every Semantics case view resolves each Adapt.
internal sealed partial class MirValidator
{
    private partial struct Checker
    {
        // SPEC 8.9, 8.10: under each Semantics case view an Adapt selects an operation (MirCases.Adapt), and a borrow or an address takes
        // a Place of its reference's referent Type there; one finding per Adapt, at the first view that breaks either.
        internal void Views(MirCases cases)
        {
            var views = MirCases.Count(body);
            foreach (var (adapt, source) in adapts)
            {
                this.at = source;
                for (var view = 0L; view < views; view++)
                {
                    cases.Select(body, view);
                    var resolved = cases.Adapt(adapt, out var operation);
                    var borrowed = operation is ConversionBinding.Borrow or ConversionBinding.Address && body.TypeOf(resolved.Place) is { } stored
                        ? cases.Type(stored) : null;
                    if (operation == ConversionBinding.None || (borrowed is not null && !this.Agrees(resolved.Type!.Components[0], borrowed)))
                    {
                        this.Report(MirViolation.Operation);
                        break;
                    }
                }
            }
        }

        // An integer Type, or one whose instance decides it (SPEC 8.4.7.3: a PrimitiveInteger T, Wrapping<T>).
        private static bool Integral(BoundType type) => type.HasIntegerArithmetic || type.HasAbstractPart;

        // SPEC 13.5.4, 13.5.7, 5.4, 10.2: whether a Cast of its kind converts a source Type to a target Type.
        private static bool Casts(MirCastKind kind, BoundType source, BoundType target) => kind switch
        {
            MirCastKind.Integer or MirCastKind.Wrap => Integral(source) && Integral(target),
            MirCastKind.Floating => source.IsFloatingPoint && target.IsFloatingPoint,
            MirCastKind.Numeric or MirCastKind.Bits => source.IsFloatingPoint ? Integral(target) : Integral(source) && target.IsFloatingPoint,
            MirCastKind.Pointer => source.Semantics == SemanticsKind.Raw && (target.Semantics == SemanticsKind.Raw || ReferenceEquals(target, BoundType.USize)),
            MirCastKind.IntToPointer => Integral(source) && target.Semantics == SemanticsKind.Raw,
            MirCastKind.Upcast => source.Semantics == target.Semantics && (source.HandleMode is not null || source.IsObjectBorrow),
            MirCastKind.Erasure => source.Kind is BoundTypeKind.Closure or BoundTypeKind.FunctionItem && target.Kind == BoundTypeKind.Function,
            _ => false,
        };

        // The Type a body gives in _0: its function's result (a constructor's object), a default template's parameter's or a static's; none
        // for the top-level statements, which have no symbol.
        private static BoundType? Result(MirDefKey key)
        {
            if (key.Decl is not { } decl)
            {
                return null;
            }

            if (key.Kind == MirBodyKind.StaticInit)
            {
                return decl.Type;
            }

            return TyCtxt.Signature(decl, null, key.Kind == MirBodyKind.DefaultTemplate ? key.Index : -1, out _);
        }

        private readonly BoundType? TypeOf(in MirOperand operand)
        {
            if (operand.Kind == MirOperandKind.Const)
            {
                return (uint)operand.Arg < (uint)body.Consts.Length ? body.Consts[operand.Arg].Type : null;
            }

            return this.IsValid(operand.Place) && !(operand.Place.Root >= 0 && unknown[operand.Place.Root]) ? body.TypeOf(operand.Place) : null;
        }

        // SPEC 8.3: Types agree (MirTypes.Agree) as they are, or as the body's premises make them (TyCtxt.Premised): E and S under `E is S`.
        private readonly bool Agrees(BoundType? expected, BoundType? actual)
            => MirTypes.Agree(expected, actual) || (body.Key.Decl is { } definition && MirTypes.Agree(
                TyCtxt.Premised(definition, expected!),
                TyCtxt.Premised(definition, actual!)));

        private readonly void Agree(BoundType? expected, BoundType? actual)
        {
            if (!this.Agrees(expected, actual))
            {
                this.Report(MirViolation.Type);
            }
        }

        // SPEC 8.9: an Adapt acquires its operand by value, a Place's Copy or a temporary's Move, beside the token of Binding's source Type,
        // into its target Type. A Place or a constant out of range is the walk's finding.
        private readonly bool Adapts(in MirRvalue adapt, ReadOnlySpan<MirOperand> operands)
            => adapt.Type is not null && operands[0].Kind is MirOperandKind.Copy or MirOperandKind.Move &&
                operands[1] is { Kind: MirOperandKind.Const, Arg: var token } &&
                ((uint)token >= (uint)body.Consts.Length || body.Consts[token].Kind == MirConstKind.TypeToken);

        // SPEC 3.2-3.4, 13.2-13.5: the Type an rvalue gives agrees with its destination's: a Use's or a unary operator's operand's, another
        // operator's left operand's, a comparison's bool, a Discriminant's or Len's isize, else its own Type. An operator's operands agree (a
        // comparison's below their layers) but for a shift count, any integer, and a raw pointer's displacement, an isize (SPEC 5.3); a Cast
        // converts what its kind admits; a Ref's Type is its kind's reference to its Place; an aggregate's operands are its components; an
        // Adapt gives its target Type.
        private void Types(in MirRvalue rvalue, BoundType? destination)
        {
            var operands = body.Operands.Slice(rvalue.Start, rvalue.Count);
            var op = (MirBinaryOp)rvalue.Op;
            var first = operands.Length > 0 ? this.TypeOf(operands[0]) : null;
            var second = operands.Length > 1 ? this.TypeOf(operands[1]) : null;
            var place = rvalue.Kind == MirRvalueKind.Ref && this.IsValid(rvalue.Place) ? body.TypeOf(rvalue.Place) : null;
            var (type, admitted) = rvalue.Kind switch
            {
                MirRvalueKind.Use or MirRvalueKind.UnaryOp => (first, true),
                MirRvalueKind.BinaryOp => (op >= MirBinaryOp.Eq ? BoundType.Boolean : first, op switch
                {
                    MirBinaryOp.Shl or MirBinaryOp.Shr => second is null || second.IsInteger || second.IsAbstract,
                    >= MirBinaryOp.Eq => this.Agrees(MirTypes.Terminal(first), MirTypes.Terminal(second)),
                    MirBinaryOp.Add or MirBinaryOp.Sub when first?.Semantics == SemanticsKind.Raw => second is null || ReferenceEquals(second, BoundType.ISize),
                    _ => this.Agrees(first, second),
                }),
                MirRvalueKind.Discriminant or MirRvalueKind.Len => (BoundType.ISize, true),
                MirRvalueKind.Cast => (rvalue.Type, first is null || rvalue.Type is null || Casts((MirCastKind)rvalue.Op, first, rvalue.Type)),
                MirRvalueKind.Adapt => (rvalue.Type, this.Adapts(rvalue, operands)),
                MirRvalueKind.Ref => (rvalue.Type, place is null ||
                    (rvalue.Type is { Kind: BoundTypeKind.Semantics, Components: [var referent] } reference && this.Agrees(referent, place) &&
                    (MirRefKind)rvalue.Op switch
                    {
                        MirRefKind.Shared => reference.Semantics is SemanticsKind.Ref or SemanticsKind.ObjRef,
                        MirRefKind.Raw => reference.Semantics == SemanticsKind.Raw,
                        _ => MirTypes.IsExclusive(reference),
                    })),
                _ => (rvalue.Type, true),
            };

            this.Agree(destination, type);
            if (!admitted)
            {
                this.Report(MirViolation.Operation);
            }
            else if (rvalue.Kind == MirRvalueKind.Adapt)
            {
                this.Agree(second, first); // Binding's source Type, the token, is the operand's.
                if (second is not null && this.IsValid(operands[0].Place) && this.IsValid(rvalue.Place))
                {
                    adapts.Add((rvalue, this.at)); // Its case views resolve it: its Places and token are in range.
                }
            }

            for (var i = 0; rvalue.Kind == MirRvalueKind.Aggregate && i < operands.Length; i++)
            {
                this.Agree(MirTypes.Component(body, rvalue, rvalue.Type!, i), this.TypeOf(operands[i]));
            }
        }

        // SPEC 7.3: a call passes and gets the Types its callee takes and gives (MirTypes.Parameter), a default template's arguments as
        // shared references. A Closure's or a function item's signature is its declaration's. A call that diverges writes no destination.
        private void Signature(in MirTerminator call)
        {
            var template = call.Callee.Kind == MirCalleeKind.Fn && call.Callee.Arg > 0;
            var value = call.Callee.Symbol is null ? this.TypeOf(call.Operand) : null;
            var result = MirTypes.Parameter(call.Callee, value, -1, out var count);
            if (count < 0)
            {
                return;
            }

            if (count != call.Count)
            {
                this.Report(MirViolation.Arity);
                return;
            }

            for (var i = 0; i < count; i++)
            {
                var actual = this.TypeOf(body.Operands[call.Start + i]);
                var expected = MirTypes.Parameter(call.Callee, value, i, out _);
                if (!template || actual is null || actual is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Components: [_] })
                {
                    this.Agree(expected, template ? actual?.Components[0] : actual);
                }
                else
                {
                    this.Report(MirViolation.Type); // A template's argument is no shared reference.
                }
            }

            if (call.Target >= 0 && this.IsValid(call.Place))
            {
                this.Agree(body.TypeOf(call.Place), result);
            }
        }
    }
}
