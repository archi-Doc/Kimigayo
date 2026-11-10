// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

// The Type tier (SPEC 3.2-3.4, 8.2, 8.14, 8.15, 13.2-13.5).
// - Responsibility: the Types values flow between agree (Agree, by MirTypes.Agrees: MirTypes.Agree, modulo Origins, or as the body's
//   premises make them); an rvalue's Type agrees with its destination's and each operation admits its operands' Types (Types, Casts, Component,
//   Adapts; an operator takes its operands in the operator table's domain, Operates; a keyed Storage's key is one whose start is
//   open by the one rule of the start, MirTypes.StartsAtStorage: Keyed); every Semantics case view resolves each
//   Adapt and PairAcquire (Views), whose exclusive borrow or Reborrow there is W1 rule 1's (MirValidator.SharedPaths); and the temps whose
//   Type the tier reads as unknown (Unknown).
// - Inputs: an rvalue whose arity holds, with its destination's Type, and an aggregate's shape (MirValidator.Rvalues' Shape); operands
//   and their Places; the body's case views (MirCases).
// - Outputs: Type, Operation and SharedPath findings; the case sites for Views (caseSites: the Adapts whose Places and token are in range
//   and the PairAcquire Uses whose Place is); the unknown mask Unknown gives the checker (a flag per local).
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
        // MirValidator.SharedPaths); a PairAcquire's Place, where the view makes it Reborrow (MirCases.Reborrows; SPEC 3.5's rows, the owner
        // case's target included, DECISIONS R5-M139's (b2) line), passes none either; one finding per Adapt or PairAcquire, at the first
        // view that breaks one of them.
        internal void Views(MirCases cases)
        {
            var views = MirCases.Count(body);
            foreach (var (site, source) in caseSites)
            {
                this.at = source;
                for (var view = 0L; view < views; view++)
                {
                    var assignment = cases.Select(body, view);
                    if (site.Kind == MirRvalueKind.Use)
                    {
                        ref readonly var acquire = ref body.Operands[site.Start];
                        if (cases.Reborrows(acquire) && this.IsSharedPath(acquire.Place, assignment))
                        {
                            this.Report(MirViolation.SharedPath);
                            break;
                        }

                        continue;
                    }

                    var resolved = cases.Adapt(site, out var operation);
                    var borrowed = operation is ConversionBinding.Borrow or ConversionBinding.Address && body.TypeOf(resolved.Place) is { } stored
                        ? cases.Type(stored) : null;
                    if (operation == ConversionBinding.None || (borrowed is not null && !MirTypes.Agrees(body, resolved.Type!.Components[0], borrowed)))
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

        // SPEC 13.5.4, 13.6.1, 5.4, 10.2: whether a Cast of its kind converts a source Type to a target Type; an integer one converts Types
        // with the integer operators, concrete or generic (MirTypes.IsArithmeticInteger).
        private static bool Casts(MirCastKind kind, BoundType source, BoundType target) => kind switch
        {
            MirCastKind.Integer or MirCastKind.Wrap => MirTypes.IsArithmeticInteger(source) && MirTypes.IsArithmeticInteger(target),
            MirCastKind.Floating => source.IsFloatingPoint && target.IsFloatingPoint,
            MirCastKind.Numeric or MirCastKind.Bits => source.IsFloatingPoint
                ? MirTypes.IsArithmeticInteger(target) : MirTypes.IsArithmeticInteger(source) && target.IsFloatingPoint,
            MirCastKind.Pointer => source.Semantics == SemanticsKind.Raw && (target.Semantics == SemanticsKind.Raw || ReferenceEquals(target, BoundType.USize)),
            MirCastKind.IntToPointer => MirTypes.IsArithmeticInteger(source) && target.Semantics == SemanticsKind.Raw,
            MirCastKind.Upcast => source.Semantics == target.Semantics && (source.HandleMode is not null || source.IsObjectBorrow),
            MirCastKind.Erasure => source.Kind is BoundTypeKind.Closure or BoundTypeKind.FunctionItem && target.Kind == BoundTypeKind.Function,
            _ => false,
        };

        // A Type below its reference and pair layers (TyCtxt.Layer), which a comparison compares (SPEC 13.4).
        private static BoundType? Terminal(BoundType? type)
            => TyCtxt.Layer(type, out var below, out _) is var _ && !ReferenceEquals(below, type) ? Terminal(below) : type;

        // SPEC 13.4, 8.2.3: what a comparison BinaryOp's operand compares: its Type as the body's premises make it (MirTypes.Premised, the
        // premising Binding.OperandTerminal applies too), before the walk, so a layer a premise gives is walked too, down to a raw layer,
        // which stops the walk; unknown (null) for the Never value. A check over Binding's decision, never a reader of it.
        private readonly BoundType? PremisedTerminal(BoundType? type) => type is null ? null : Terminal(MirTypes.Premised(body, type));

        // SPEC 13.2-13.4, 5.3, 8.2.3: an operator rvalue's operation takes its operand at its premised terminal (PremisedTerminal; the
        // Backend's column reads the instance's terminal too), the right one's where the left is the Never value, whose unknown Type takes
        // it: the operator table's domain (MirTypes.Operates, a Never terminal's comparison included; a string's comparison is its library
        // witness's, R8-S1e-b), an abstract operand's or Wrapping over one's on Binding's proof per group.
        private readonly bool Operates(in MirRvalue rvalue, BoundType? first, BoundType? second)
            => (this.PremisedTerminal(first) ?? this.PremisedTerminal(second)) is not { } type || MirTypes.Operates(rvalue.Kind, rvalue.Op, type);

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

        private readonly void Agree(BoundType? expected, BoundType? actual)
        {
            if (!MirTypes.Agrees(body, expected, actual))
            {
                this.Report(MirViolation.Type);
            }
        }

        // The Type of an aggregate's operand, as its shape (Shape) gives it: its element, or the base, then the Fields of its parts.
        private readonly BoundType? Component(in AggregateShape shape, int index) => shape.Element ?? (shape.Base is null
            ? body.ProjectedType(shape.Parts, MirProjectionKind.Field, index)
            : index == 0 ? shape.Base : body.ProjectedType(shape.Parts, MirProjectionKind.Field, index - 1));

        // SPEC 8.14: an Adapt acquires its operand by value, a Place's Copy or a temporary's Move, beside the token of Binding's source Type,
        // into its target Type. A Place or a constant out of range is the walk's finding.
        private readonly bool Adapts(in MirRvalue adapt, ReadOnlySpan<MirOperand> operands)
            => adapt.Type is not null && operands[0].Kind is MirOperandKind.Copy or MirOperandKind.Move &&
                operands[1] is { Kind: MirOperandKind.Const, Arg: var token } &&
                ((uint)token >= (uint)body.Consts.Length || body.Consts[token].Kind == MirConstKind.TypeToken);

        // SPEC 4.8.1, 8.15.3 (G597): a Storage has no operand, or a key selection's two: its start position, an isize, and the token of its
        // key's Type, whose start is open in the body (MirTypes.StartsAtStorage: a key that decides it takes the unkeyed form, displaced
        // by a proven Add at a position; one form for each answer). A constant out of range is the walk's finding.
        private readonly bool Keyed(ReadOnlySpan<MirOperand> operands, BoundType? start)
            => operands.Length == 0 || ((start is null || ReferenceEquals(start, BoundType.ISize)) &&
                operands[1] is { Kind: MirOperandKind.Const, Arg: var token } &&
                ((uint)token >= (uint)body.Consts.Length ||
                (body.Consts[token] is { Kind: MirConstKind.TypeToken, Type: var key } && MirTypes.StartsAtStorage(key) is null)));

        // SPEC 3.2-3.4, 13.2-13.5: the Type an rvalue gives agrees with its destination's: a Use's or a unary operator's operand's, another
        // operator's left operand's, a comparison's bool, a Discriminant's or Len's isize, else its own Type. An operator takes its operands
        // in the operator table's domain (Operates); they agree (a comparison's at their premised terminals) but for a shift count, any
        // integer, and a raw pointer's displacement, an isize (SPEC 5.3); a Cast converts what its kind admits; a Ref's Type is its kind's
        // reference to its Place; an aggregate's operands are its components; an Adapt gives its target Type.
        private void Types(in MirRvalue rvalue, BoundType? destination)
        {
            var operands = body.Operands.Slice(rvalue.Start, rvalue.Count);
            var op = (MirBinaryOp)rvalue.Op;
            var first = operands.Length > 0 ? this.TypeOf(operands[0]) : null;
            var second = operands.Length > 1 ? this.TypeOf(operands[1]) : null;
            var place = rvalue.Kind is MirRvalueKind.Ref or MirRvalueKind.Storage && this.IsValid(rvalue.Place) ? body.TypeOf(rvalue.Place) : null;
            var (type, admitted) = rvalue.Kind switch
            {
                MirRvalueKind.Use => (first, true),
                MirRvalueKind.UnaryOp => (first, this.Operates(rvalue, first, null)),
                MirRvalueKind.BinaryOp => (op >= MirBinaryOp.Eq ? BoundType.Boolean : first, this.Operates(rvalue, first, second) && op switch
                {
                    MirBinaryOp.Shl or MirBinaryOp.Shr => second is null || second.IsInteger || second.IsAbstract,
                    >= MirBinaryOp.Eq => MirTypes.Agree(this.PremisedTerminal(first), this.PremisedTerminal(second)),
                    MirBinaryOp.Add or MirBinaryOp.Sub when first?.Semantics == SemanticsKind.Raw => second is null || ReferenceEquals(second, BoundType.ISize),
                    _ => MirTypes.Agrees(body, first, second),
                }),
                MirRvalueKind.Discriminant or MirRvalueKind.Len => (BoundType.ISize, true),
                // O-92 (a): exactly the built-in range-selection receivers, never through a reference; a raw pointer to their element.
                MirRvalueKind.Storage => (rvalue.Type, place is null ||
                    (place.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Array or BoundTypeKind.Slice &&
                    rvalue.Type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw, Components: [var element] } &&
                    MirTypes.Agrees(body, element, place.Components[0]) && this.Keyed(operands, first))),
                MirRvalueKind.Cast => (rvalue.Type, first is null || rvalue.Type is null || Casts((MirCastKind)rvalue.Op, first, rvalue.Type)),
                MirRvalueKind.Adapt => (rvalue.Type, this.Adapts(rvalue, operands)),
                MirRvalueKind.Ref => (rvalue.Type, place is null ||
                    (rvalue.Type is { Kind: BoundTypeKind.Semantics, Components: [var referent] } reference && MirTypes.Agrees(body, referent, place) &&
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
                    caseSites.Add((rvalue, this.at)); // Its case views resolve it: its Places and token are in range.
                }
            }
            else if (rvalue is { Kind: MirRvalueKind.Use, Op: not 0 } && operands[0] is { Kind: MirOperandKind.PairAcquire } acquire &&
                this.IsValid(acquire.Place))
            {
                caseSites.Add((rvalue, this.at)); // A PairAcquire's Use (A3 E13 (4)): its case views resolve it, its Place in range.
            }

            if (rvalue.Kind == MirRvalueKind.Aggregate)
            {
                var shape = Shape(rvalue); // Its Count is the operands' (Rvalue's Arity).
                for (var i = 0; i < operands.Length; i++)
                {
                    this.Agree(this.Component(shape, i), this.TypeOf(operands[i]));
                }
            }
        }
    }
}
