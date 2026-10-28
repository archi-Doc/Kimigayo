// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

// The Type tier (SPEC 3.2-3.4, 8.2, 8.14, 8.15, 13.2-13.5).
// - Responsibility: the Types values flow between agree (Agree, Agrees: MirTypes.Agree, modulo Origins, or as the body's premises
//   make them); an rvalue's Type agrees with its destination's and each operation admits its operands' Types (Types, Casts, Component,
//   Adapts); every Semantics case view resolves each Adapt (Views), whose exclusive borrow there is W1 rule 1's
//   (MirValidator.SharedPaths); and the temps whose Type the tier reads as unknown (Unknown).
// - Inputs: an rvalue whose arity holds, with its destination's Type; operands and their Places; the body's case views (MirCases).
// - Outputs: Type, Operation and SharedPath findings; the Adapts whose Places and token are in range, for Views; the unknown mask Unknown
//   gives the checker (a flag per local).
// - Invariants: an operand of unknown Type (a Place or constant out of range, a temp Unknown names, the Never value) agrees with
//   every Type.
// - Prohibitions: no range or kind finding (the walk's: MirValidator.Tables, .Rvalues); a Call's Types are MirValidator.Signatures', but
//   an intrinsic row's, which MirValidator.Calls reads through TypeOf (testRecordValue's Scalar).
internal sealed partial class MirValidator
{
    private bool[] unknown = [];

    // The temps an Opaque value defines, whose Type is the replaced syntax's, which Binding may not know (an Error node has none), and those a
    // Use, a unary operator or an arithmetic, bit or shift operator of such a temp defines, whose Type is that operand's: the Type tier reads
    // them as of an unknown Type.
    private bool[] Unknown(MirBody body)
    {
        if (this.unknown.Length < body.Locals.Length)
        {
            this.unknown = new bool[Math.Max(body.Locals.Length, this.unknown.Length * 2)];
        }

        var locals = body.Locals;
        var operands = body.Operands;
        Array.Clear(this.unknown, 0, locals.Length);
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (ref readonly var statement in body.Statements)
            {
                if (statement is not { Kind: MirStatementKind.Assign, Place: { Projection: 0, Root: >= 0 and var root }, Rvalue: var rvalue } ||
                    root >= locals.Length || locals[root].Kind != MirLocalKind.Temp || this.unknown[root])
                {
                    continue;
                }

                if (rvalue.Kind != MirRvalueKind.Opaque)
                {
                    if (rvalue is not { Kind: MirRvalueKind.Use or MirRvalueKind.UnaryOp } and not
                        { Kind: MirRvalueKind.BinaryOp, Op: < (byte)MirBinaryOp.Eq } ||
                        (uint)rvalue.Start >= (uint)operands.Length)
                    {
                        continue;
                    }

                    if (operands[rvalue.Start] is not { Kind: not MirOperandKind.Const, Place.Root: >= 0 and var read } ||
                        read >= locals.Length || !this.unknown[read])
                    {
                        continue;
                    }
                }

                this.unknown[root] = changed = true;
            }
        }

        return this.unknown;
    }

    private partial struct Checker
    {
        // SPEC 8.14, 8.15: under each Semantics case view an Adapt selects an operation (MirCases.Adapt), a borrow or an address takes a
        // Place of its reference's referent Type there, and an exclusive borrow passes no shared layer under the view's cases (W1 rule 1,
        // MirValidator.SharedPaths); one finding per Adapt, at the first view that breaks one of them.
        internal void Views(MirCases cases)
        {
            var views = MirCases.Count(body);
            foreach (var (adapt, source) in adapts)
            {
                this.at = source;
                for (var view = 0L; view < views; view++)
                {
                    var assignment = cases.Select(body, view);
                    var resolved = cases.Adapt(adapt, out var operation);
                    var borrowed = operation is ConversionBinding.Borrow or ConversionBinding.Address && body.TypeOf(resolved.Place) is { } stored
                        ? cases.Type(stored) : null;
                    if (operation == ConversionBinding.None || (borrowed is not null && !this.Agrees(resolved.Type!.Components[0], borrowed)))
                    {
                        this.Report(MirViolation.Operation);
                        break;
                    }

                    if (resolved is { Kind: MirRvalueKind.Ref, Op: (byte)MirRefKind.Uniq or (byte)MirRefKind.TwoPhaseUniq } &&
                        this.IsSharedPath(resolved.Place, assignment))
                    {
                        this.Report(MirViolation.SharedPath);
                        break;
                    }
                }
            }
        }

        // An integer Type, or one whose instance decides it (SPEC 8.7.4: a PrimitiveInteger T, Wrapping<T>).
        private static bool Integral(BoundType type) => type.HasIntegerArithmetic || type.HasAbstractPart;

        // SPEC 13.5.4, 13.6.1, 5.4, 10.2: whether a Cast of its kind converts a source Type to a target Type.
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

        // A Type below its reference and pair layers (TyCtxt.Layer), which a comparison compares (SPEC 13.4).
        private static BoundType? Terminal(BoundType? type)
            => TyCtxt.Layer(type, out var below, out _) is var _ && !ReferenceEquals(below, type) ? Terminal(below) : type;

        // An operand's Type, unknown (null) for a Place out of range or a local an Opaque value defines, and for the Never value, which
        // fits any operand position (SPEC 3.8; DECISIONS R4-M62).
        private readonly BoundType? TypeOf(in MirOperand operand)
        {
            if (operand.Kind == MirOperandKind.Const)
            {
                return (uint)operand.Arg < (uint)body.Consts.Length ? body.Consts[operand.Arg].Type : null;
            }

            var type = this.IsValid(operand.Place) && !(operand.Place.Root >= 0 && unknown[operand.Place.Root]) ? body.TypeOf(operand.Place) : null;
            return ReferenceEquals(type, BoundType.Never) ? null : type;
        }

        // SPEC 8.2: Types agree (MirTypes.Agree) as they are, or as the body's premises make them (TyCtxt.Premised): E and S under `E is S`.
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

        // The Type of an aggregate's operand, as the Place it initializes projects it: a Tuple's element, a Closure's capture, a struct's base
        // then its own Fields, a Case's payload, a fixed array's, a Fill's or an Array's element.
        private readonly BoundType? Component(in MirRvalue rvalue, int index) => (MirAggregateKind)rvalue.Op switch
        {
            MirAggregateKind.FixedArray or MirAggregateKind.Fill or MirAggregateKind.Array => rvalue.Type!.Components[0],
            MirAggregateKind.Adt when AdtDef.IsEnum(rvalue.Type) => body.ProjectedType(
                body.ProjectedType(
                    rvalue.Type,
                    MirProjectionKind.Downcast,
                    rvalue.Arg),
                MirProjectionKind.Field,
                index),
            MirAggregateKind.Adt when AdtDef.Base(rvalue.Type!) is { } parent => index == 0 ? parent : body.ProjectedType(
                rvalue.Type,
                MirProjectionKind.Field,
                index - 1),
            _ => body.ProjectedType(rvalue.Type, MirProjectionKind.Field, index),
        };

        // SPEC 8.14: an Adapt acquires its operand by value, a Place's Copy or a temporary's Move, beside the token of Binding's source Type,
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
                    >= MirBinaryOp.Eq => this.Agrees(Terminal(first), Terminal(second)),
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
                this.Agree(this.Component(rvalue, i), this.TypeOf(operands[i]));
            }
        }
    }
}
