// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Values: a node's own value into a destination (Into), as its consumer takes it (Assign, Operand, Adapt), each by the node's Op, Adjust
// and PlaceShape.
internal sealed partial class MirBuilder
{
    private static MirBinaryOp? BinaryOp(KotoKind kind) => kind switch
    {
        KotoKind.Plus => MirBinaryOp.Add,
        KotoKind.Minus => MirBinaryOp.Sub,
        KotoKind.Asterisk => MirBinaryOp.Mul,
        KotoKind.Slash => MirBinaryOp.Div,
        KotoKind.Percent => MirBinaryOp.Rem,
        KotoKind.LessThanLessThan => MirBinaryOp.Shl,
        KotoKind.GreaterThanGreaterThan => MirBinaryOp.Shr,
        KotoKind.Ampersand => MirBinaryOp.BitAnd,
        KotoKind.Bar => MirBinaryOp.BitOr,
        KotoKind.Caret => MirBinaryOp.BitXor,
        KotoKind.EqualsEquals => MirBinaryOp.Eq,
        KotoKind.ExclamationEquals => MirBinaryOp.Ne,
        KotoKind.LessThan => MirBinaryOp.Lt,
        KotoKind.LessThanEquals => MirBinaryOp.Le,
        KotoKind.GreaterThan => MirBinaryOp.Gt,
        KotoKind.GreaterThanEquals => MirBinaryOp.Ge,
        _ => null,
    };

    // The value a node's own form produces, into the destination.
    private void Into(MirPlace destination, Koto node)
    {
        switch (HirTables.Op(node))
        {
            case HirOp.Literal when this.Literal(node) is var constant and >= 0:
                this.Use(destination, MirOperand.Const(constant), node);
                break;
            case HirOp.Name when HirTables.Symbol(node)?.Kind == BindingSymbolKind.Function: // SPEC 7.6.4: a constant of its Item Type.
                this.Use(destination, MirOperand.Const(this.body.Const(new(MirConstKind.FnItem, TypeOf(node)))), node);
                break;
            case HirOp.Group: // Its operand, which the group acquires as the operand's Adjust says.
                this.Assign(destination, Child(node));
                break;
            case HirOp.Block:
                this.Block(destination, (CodeBlockKoto)node);
                break;
            case HirOp.Call:
                this.Call(destination, node);
                break;
            case HirOp.Jump or HirOp.Continue:
                this.Transfer(destination, (JumpKoto)node);
                break;
            case HirOp.If:
                this.If(destination, (IfKoto)node);
                break;
            case HirOp.Match or HirOp.Try:
                this.Match(destination, (MatchKoto)node);
                break;
            case HirOp.RuntimeTypeTest:
                this.TypeTest(destination, (IsKoto)node);
                break;
            case HirOp.Loop:
                this.Loop(destination, (LoopKoto)node);
                break;
            case HirOp.For:
                this.For(destination, (ForKoto)node);
                break;
            case HirOp.While:
                this.While(destination, (WhileKoto)node);
                break;
            case HirOp.Do:
                this.Do(destination, (DoKoto)node);
                break;
            case HirOp.Require: // A statement: it leaves no value.
                this.Require((RequireKoto)node);
                break;
            case HirOp.TestVerification:
                this.Verification(destination, (TestVerificationKoto)node);
                break;
            case HirOp.Closure:
                this.Closure(destination, (FunctionKoto)node);
                break;
            case HirOp.Unsafe:
                this.Block(destination, ((UnsafeBlockKoto)node).Body, MirScopeKind.Unsafe);
                break;
            case HirOp.Discard: // SPEC 14.2.4: the operand acquired by bare acquisition into a temp, destroyed with the statement's.
                this.Assign(this.Temp(((DiscardKoto)node).Operand), ((DiscardKoto)node).Operand);
                break;
            case HirOp.Defer: // SPEC 16.1: registered here, built where its scope is left.
                this.Register(Cleanup.Defer, -1, node, this.lexical);
                break;
            case HirOp.NoInit: // SPEC 4.3.4: the array constructed from no operand, its elements not stored.
                this.Set(destination, MirRvalue.Aggregate(MirAggregateKind.FixedArray, this.body.Operands.Length, 0, this.body.TypeOf(destination)!), node);
                break;
            case HirOp.PrimitiveOp:
                this.Primitive(destination, node, TypeOf(node));
                break;
            case HirOp.Logical:
                this.Logical(destination, (BinaryKoto)node);
                break;
            case HirOp.Borrow: // SPEC 13.5.5.2, 5.4: the operand's Place borrowed in the mode of the node's Type, or its address.
                var reference = TypeOf(node);
                var kind = (ConversionBinding)HirTables.PlanOf(node).Mode == ConversionBinding.Address ? MirRefKind.Raw
                    : MirTypes.IsExclusive(reference) ? MirRefKind.Uniq : MirRefKind.Shared;
                this.Set(destination, MirRvalue.Ref(kind, this.Place(Child(node)), reference), node);
                break;
            case HirOp.Convert:
                this.Convert(destination, (BinaryKoto)node, TypeOf(node));
                break;
            case HirOp.Aggregate:
                this.Aggregate(destination, node, TypeOf(node));
                break;
            case HirOp.Assign:
                this.Replace(destination, (BinaryKoto)node);
                break;
            case HirOp.CompoundAssign:
                this.Update(destination, node);
                break;
            case HirOp.Index when HirTables.PlaceOf(node).Kind == HirPlaceKind.Value: // A built-in range selection.
                this.Slice(destination, (IndexKoto)node);
                break;
            case HirOp.DictionaryLiteral:
                this.Dictionary(destination, (DictionaryLiteralKoto)node);
                break;
            case HirOp.Interpolation:
                this.Format(destination, node);
                break;
            case HirOp.Error: // Binding reported it; its value is derived.
                this.Opaque(node, destination);
                break;
            default:
                this.Unsupported(node, destination);
                break;
        }
    }

    // The destination takes a node's value as its consumer takes it (its Adjust): a Temporary, or a group's operand, is built into the
    // destination; any other acquisition is an operand of the destination's Type.
    private void Assign(MirPlace destination, Koto node)
    {
        if (HirTables.AdjustOf(node) is { Acquire: HirAcquire.Temporary or HirAcquire.None, Adaptation: HirAdaptation.None })
        {
            this.Into(destination, node);
        }
        else
        {
            this.Use(destination, this.Operand(node, this.body.TypeOf(destination)), node);
        }
    }

    // A node's value as its consumer takes it (plan: Adjust to operand): a literal as its constant, another value through a temp, a Place,
    // or a value held in a temp, as Acquire takes it for the expected Type, a call's input as the call's (input).
    private MirOperand Operand(Koto node, BoundType? expected = null, bool input = false)
    {
        var adjust = HirTables.AdjustOf(node);
        if (adjust is { Acquire: HirAcquire.Temporary or HirAcquire.None, Adaptation: HirAdaptation.None })
        {
            return HirTables.Op(node) == HirOp.Literal && this.Literal(node) is var constant and >= 0 ? MirOperand.Const(constant) : MirOperand.Move(this.Value(node));
        }

        if (adjust.Acquire == HirAcquire.Unset || adjust.Adaptation == HirAdaptation.StorageProjection || (adjust.Adaptation == HirAdaptation.BaseBorrow &&
            expected?.Components is not [_]) || (adjust.Adaptation == HirAdaptation.Erasure && expected is null))
        {
            return MirOperand.Move(this.Unsupported(node)); // An Error proof left the acquisition unset; a projected receiver's storage is its Field's (Member).
        }

        return this.Acquire(adjust, adjust.Acquire == HirAcquire.Temporary ? this.Value(node) : this.Place(node), expected, input, node);
    }

    // A Place as an acquisition and an adaptation take it: Copied, moved, acquired by its pair layer's case (SPEC 8.9; where its Type
    // decides the case, as in a default's copy, Copied or Reborrowed as that Type is) or, a stored exclusive reference, Reborrowed in its
    // own mode (SPEC 15.1.5). SPEC 10.2: adapted, the Scalar its reference layers end in, Copied; a Closure or
    // function item erased to its Function Type; else a reference, of the expected Type or the one its mode forms, to the Place itself (a
    // shared or exclusive borrow), to the referent its layers reach (ReferenceRead), to an object handle's payload or to the base the
    // expected Type names. An implicit exclusive borrow of a call's input is two-phase: reserved here, activated at the Call (SPEC 15.6.7).
    private MirOperand Acquire(HirAdjust adjust, MirPlace place, BoundType? expected, bool input, Koto node)
    {
        var (adaptation, type) = (adjust.Adaptation, this.body.TypeOf(place) ?? BoundType.Unit);
        var target = expected?.Components is [var referent] ? referent : null;
        switch (adaptation)
        {
            case HirAdaptation.None:
                return adjust.Acquire switch
                {
                    HirAcquire.ByCase when this.Binder(type) is var binder and >= 0 => MirOperand.PairAcquire(binder, place),
                    HirAcquire.ByCase when !MirTypes.IsExclusive(type) => MirOperand.Copy(place),
                    HirAcquire.Reborrow or HirAcquire.ByCase => this.Temporary(type, MirRvalue.Ref(MirRefKind.Uniq, this.body.Project(place, MirProjectionKind.Deref, 0), type), node),
                    HirAcquire.Copy => MirOperand.Copy(place),
                    _ => MirOperand.Move(place),
                };
            case HirAdaptation.ReferentRead:
                return MirOperand.Copy(this.Follow(place, 16, expected));
            case HirAdaptation.Erasure: // SPEC 7.6.4: of the value its ordinary acquisition gives.
                var source = this.Acquire(adjust with { Adaptation = HirAdaptation.None }, place, null, input, node);
                return this.Temporary(expected!, MirRvalue.Cast(MirCastKind.Erasure, this.body.AddOperand(source), expected!) with { Proven = true }, node);
            case HirAdaptation.ReferenceRead:
                place = this.Follow(place, 16, target);
                break;
            case HirAdaptation.PayloadProjection:
                place = this.body.Project(place, MirProjectionKind.Deref, 0);
                break;
            case HirAdaptation.BaseBorrow:
                place = this.Follow(place, 16);
                var depth = 0;
                for (var layer = this.body.TypeOf(place); layer is not null && !ReferenceEquals(layer, target); layer = AdtDef.Base(layer))
                {
                    depth++;
                }

                place = depth == 0 ? place : this.body.Project(place, MirProjectionKind.Base, depth);
                break;
        }

        var exclusive = adaptation == HirAdaptation.ExclusiveBorrow || (adaptation is HirAdaptation.ReferenceRead or HirAdaptation.BaseBorrow or HirAdaptation.PayloadProjection &&
            MirTypes.IsExclusive(expected));
        var reference = expected ?? compilation.Types.Reference(exclusive ? SemanticsKind.Uniq : SemanticsKind.Ref, this.body.TypeOf(place) ?? BoundType.Unit);
        return this.Temporary(reference, MirRvalue.Ref(!exclusive ? MirRefKind.Shared : input ? MirRefKind.TwoPhaseUniq : MirRefKind.Uniq, place, reference), node);
    }

    // An rvalue in a new temp of its Type, moved.
    private MirOperand Temporary(BoundType type, in MirRvalue rvalue, Koto node)
    {
        var temp = this.Temp(type, node);
        this.Set(temp, rvalue, node);
        return MirOperand.Move(temp);
    }

    // A borrow of a Place in a new temp of the reference a Semantics kind forms to it (uniq: exclusive; ref, objref: shared), moved. A Ref
    // has no operand that could diverge, so it is made where it is built even when the node it serves does not complete (a call whose
    // default diverges, an interpolation with a transfer).
    private MirOperand Borrow(SemanticsKind semantics, MirPlace place, Koto node)
    {
        var view = compilation.Types.Reference(semantics, this.body.TypeOf(place) ?? BoundType.Unit);
        var temp = this.Temp(view, node);
        this.body.Push(this.current, MirStatement.Assign(temp, MirRvalue.Ref(semantics == SemanticsKind.Uniq ? MirRefKind.Uniq : MirRefKind.Shared, place, view), this.Info(node)));
        return MirOperand.Move(temp);
    }

    // SPEC 13.2, 13.3: a built-in operator on its operands, its check Proven where IMPL 21.5.3 omits it; or a sequence's metadata. The left
    // operand is pending while the right one is evaluated, so a transfer there ends a comparison's inspection Loan (SPEC 13.4, 14.10.3).
    private void Primitive(MirPlace destination, Koto node, BoundType type)
    {
        switch (node)
        {
            case MemberAccessKoto member:
                this.Metadata(destination, member);
                break;
            case BinaryKoto binary when BinaryOp(binary.Akind) is { } op:
                var start = this.arguments.Count;
                this.arguments.Add(this.Operand(binary.Left));
                this.arguments.Add(this.Operand(binary.Right));
                var proven = this.Proven(op, type, this.arguments[^1]);
                this.Set(destination, MirRvalue.Binary(op, this.Flush(start), proven), node);
                break;
            case UnaryKoto { Akind: KotoKind.PrefixPlus } plus:
                this.Assign(destination, plus.Operand);
                break;
            case UnaryKoto { Akind: KotoKind.PrefixMinus or KotoKind.Not } unary:
                var negate = unary.Akind == KotoKind.PrefixMinus;
                this.Set(destination, MirRvalue.Unary(negate ? MirUnaryOp.Neg : MirUnaryOp.Not, this.body.AddOperand(this.Operand(unary.Operand)), !negate || type.IsFloatingPoint || type.IsWrappingInteger), node);
                break;
            default:
                this.Unsupported(node, destination);
                break;
        }
    }

    // SPEC 4.6.1, 4.6.3: a sequence's length, whether it is empty (its length equals 0) and its indices (the ResolvedRange of start 0 and
    // end its length), from Len of the receiver; of a Utf8Slice, the one struct Binding serves as a sequence (SequenceMember), Len of its
    // byte Slice. SPEC 4.7.4: an Array's or a Dictionary's capacity is the temporary intrinsic Capacity of a shared view of the handle
    // (owner decision 2026-10-11) until R8's representation structs make it a Field read.
    private void Metadata(MirPlace destination, MemberAccessKoto member)
    {
        var form = IntrinsicDisposition.FormOf(member);
        if (form is not (BuiltinForm.Length or BuiltinForm.IsEmpty or BuiltinForm.Indices or BuiltinForm.Capacity))
        {
            this.Unsupported(member, destination);
            return;
        }

        var sequence = this.Place(member.Left);
        if (form == BuiltinForm.Capacity)
        {
            var start = this.arguments.Count;
            this.arguments.Add(this.Borrow(SemanticsKind.Ref, sequence, member));
            this.Invoke(destination, MirCallee.Intrinsic(IntrinsicId.Capacity), start, member);
            return;
        }

        if (AdtDef.IsStruct(this.body.TypeOf(sequence)))
        {
            sequence = this.body.Project(sequence, MirProjectionKind.Field, AdtDef.IndexOf(this.body.TypeOf(sequence)!, "value"));
        }

        var length = form == BuiltinForm.Length ? destination : this.Temp(BoundType.ISize, member);
        this.Set(length, MirRvalue.Len(sequence), member);
        if (form != BuiltinForm.Length)
        {
            var start = this.body.AddOperand(MirOperand.Const(this.body.Const(new(MirConstKind.Integer, BoundType.ISize))));
            this.body.AddOperand(MirOperand.Move(length));
            this.Set(destination, form == BuiltinForm.IsEmpty ? MirRvalue.Binary(MirBinaryOp.Eq, start, true) : MirRvalue.Aggregate(MirAggregateKind.Adt, start, 2, TypeOf(member)), member);
        }
    }

    // SPEC 13.3: `and` and `or` evaluate the right operand only where the left does not decide the result: the left into the destination,
    // then the branch on it to the right operand or past it.
    private void Logical(MirPlace destination, BinaryKoto logical)
    {
        this.Assign(destination, logical.Left);
        var (right, join, and) = (this.body.AddBlock(), this.body.AddBlock(), logical.Akind == KotoKind.And);
        this.Branch(destination, logical.Left, and ? join : right, and ? right : join);
        this.current = right;
        this.Assign(destination, logical.Right);
        this.Join(join, logical.Right);
        this.current = join;
    }

    // SPEC 13.5.4, 13.5.7, 5.4: a numeric, wrapping, bit, pointer or object conversion, a Cast of its operand, Proven where it checks
    // nothing; a folded direct literal is its constant and evaluates nothing, a fitted literal is its operand, and a Never operand only
    // diverges. A case adaptation is not built yet.
    private void Convert(MirPlace destination, BinaryKoto conversion, BoundType type)
    {
        var binding = (ConversionBinding)HirTables.PlanOf(conversion).Mode;
        if (HirTables.PlanOf(conversion).Folded is { } folded)
        {
            this.Use(destination, MirOperand.Const(this.body.Const(new(type.IsFloatingPoint ? MirConstKind.Float : MirConstKind.Integer, type, folded))), conversion);
        }
        else if (binding is ConversionBinding.Literal or ConversionBinding.Abrupt)
        {
            this.Assign(binding == ConversionBinding.Literal ? destination : this.Temp(conversion.Left), conversion.Left);
        }
        else if (binding == ConversionBinding.CaseAdaptation)
        {
            this.Unsupported(conversion, destination);
        }
        else
        {
            var operand = this.Operand(conversion.Left);
            var source = this.TypeOf(operand) ?? type;
            var kind = binding switch
            {
                ConversionBinding.Wrap => MirCastKind.Wrap,
                ConversionBinding.Bits => MirCastKind.Bits,
                ConversionBinding.ObjectUpcast => MirCastKind.Upcast,
                ConversionBinding.Pointer => source.Semantics == SemanticsKind.Raw ? MirCastKind.Pointer : MirCastKind.IntToPointer,
                _ => source.IsFloatingPoint == type.IsFloatingPoint ? source.IsFloatingPoint ? MirCastKind.Floating : MirCastKind.Integer : MirCastKind.Numeric,
            };

            // IMPL 21.5.3: an integer conversion whose source range lies within its target's checks nothing (T and Wrapping<T>, u8@u32,
            // i8@i64), nor does a widening floating-point one or one from an integer to a floating-point Type.
            var proven = kind switch
            {
                MirCastKind.Integer => this.Width(source, out var signed) is var from and > 0 && this.Width(type, out var target) is var to and > 0 &&
                    (target || !signed) && from - (signed ? 1 : 0) <= to - (target ? 1 : 0),
                MirCastKind.Floating => ReferenceEquals(type, BoundType.F64),
                MirCastKind.Numeric => type.IsFloatingPoint,
                _ => true,
            };

            this.Set(destination, MirRvalue.Cast(kind, this.body.AddOperand(operand), type) with { Proven = proven }, conversion);
        }
    }

    // SPEC 3.2, 4.3.2, 6.4: a Tuple, a fixed array or an enum Case value from its operands in order, an enum's payloads as its construction
    // acquires them; a dynamic Array literal the calls of its built-in form (Collection). A fill of a length its instance decides is not built.
    private void Aggregate(MirPlace destination, Koto node, BoundType type)
    {
        if (node is ArrayLiteralKoto { FillLength: null } literal && type.Kind == BoundTypeKind.Array)
        {
            this.Collection(destination, literal, type);
            return;
        }

        var construction = BoundEnumConstruction.Of(node);
        var elements = node switch
        {
            TupleLiteralKoto tuple => tuple.Elements,
            ArrayLiteralKoto array when type is { Kind: BoundTypeKind.FixedArray } && (array.FillLength is null || type.LengthExpression is null) => array.Elements,
            _ => null,
        };
        if (construction is null && elements is null)
        {
            this.Unsupported(node, destination);
            return;
        }

        var start = this.arguments.Count;
        for (var i = 0; i < elements?.Count; i++)
        {
            var element = type.Kind == BoundTypeKind.Tuple ? i : 0; // The element's Type, which a Type Binding rejected may not have.
            this.arguments.Add(this.Operand(elements![i], element < type.Components.Count ? type.Components[element] : null));
        }

        // SPEC 4.3.3: a fill construction acquires its value once, even for length 0, and copies it into each element.
        if (node is ArrayLiteralKoto { FillLength: not null })
        {
            var value = this.arguments[start];
            if (value.Kind != MirOperandKind.Const)
            {
                var once = this.Temp(type.Components[0], node);
                this.Use(once, value, node);
                value = MirOperand.Copy(once);
            }

            this.arguments.RemoveAt(start);
            for (var i = 0; i < type.Length; i++)
            {
                this.arguments.Add(value);
            }
        }

        foreach (ref readonly var payload in construction is null ? default : construction.PayloadOperations)
        {
            this.arguments.Add(this.Operand(payload.Source!, payload.AdaptedType ?? payload.ParameterType));
        }

        var (kind, count) = (construction is not null ? MirAggregateKind.Adt : node is TupleLiteralKoto ? MirAggregateKind.Tuple : MirAggregateKind.FixedArray, this.arguments.Count - start);
        this.Set(destination, MirRvalue.Aggregate(kind, this.Flush(start), count, type, construction?.Case.Ordinal ?? 0), node);
    }

    // SPEC 7.6.1, 7.6.2: creating a Closure evaluates its captures, not its body: each environment binding, in entry order, from the Place
    // of its source binding (Bound) as its entry acquires it, the Closure value their aggregate. A source not bound here is unsupported.
    private void Closure(MirPlace destination, FunctionKoto closure)
    {
        var (captures, start) = (BoundClosure.Of(closure)!.Captures, this.arguments.Count);
        for (var i = 0; i < captures.Count; i++)
        {
            var type = captures[i].Environment.Type ?? BoundType.Unit;
            if (this.Bound(captures[i].Source) is not { } place)
            {
                this.Unsupported(closure, place = this.Temp(type, closure));
            }

            this.arguments.Add(this.Acquire(captures[i].Adjust, place, type, false, closure));
        }

        this.Set(destination, MirRvalue.Aggregate(MirAggregateKind.Closure, this.Flush(start), captures.Count, TypeOf(closure)), closure);
    }

    // SPEC 13.7.1: the value secured first, then the target located, its old value dropped where its Type may need it, and the value placed;
    // a target that is not a Place is a Property's, whose setter (its last call, after any getter) takes the value, evaluated first. An
    // assignment to a target Binding rejected is derived from that error.
    private void Replace(MirPlace destination, BinaryKoto assignment)
    {
        if (HirTables.PlaceOf(assignment.Left).Kind is HirPlaceKind.Unset or HirPlaceKind.Value)
        {
            if ((CallPlan.Of(assignment.Left, 1) ?? CallPlan.Of(assignment.Left)) is { } setter)
            {
                this.Call(destination, setter, assignment, assignment.Right);
            }
            else if (HirTables.Op(assignment.Left) == HirOp.Error)
            {
                this.Opaque(assignment, destination);
            }
            else
            {
                this.Unsupported(assignment, destination);
            }

            return;
        }

        var value = this.Temp(assignment.Left);
        this.Assign(value, assignment.Right);
        this.Overwrite(this.Place(assignment.Left), MirOperand.Move(value), assignment);
        this.Unit(destination, assignment);
    }

    // SPEC 13.7.1: a target's old value dropped where its Type may need it (A2 elaborates a moved one away), then the value placed.
    private void Overwrite(MirPlace target, in MirOperand value, Koto node)
    {
        if (Completes(node) && MirTypes.MayNeedDrop(this.body.TypeOf(target)))
        {
            var next = this.body.AddBlock();
            this.body.Terminate(this.current, MirTerminator.Drop(target, next, this.Info(node)));
            this.current = next;
        }

        this.Use(target, value, node);
    }

    // IMPL 21.5.3: whether an operation cannot Abort: it checks nothing (a comparison, a bit operation, or an operation of a floating-point,
    // raw pointer or wrapping Type), or a literal right operand proves its check: a divisor other than 0 and, for a checked signed
    // quotient, -1; a shift count below the left operand's width.
    private bool Proven(MirBinaryOp op, BoundType type, in MirOperand right)
    {
        var literal = right.Kind == MirOperandKind.Const && this.body.Consts[right.Arg].Kind == MirConstKind.Integer ? this.body.Consts[right.Arg].Bits : (Int128?)null;
        return op switch
        {
            MirBinaryOp.Add or MirBinaryOp.Sub or MirBinaryOp.Mul => type.IsFloatingPoint || type.IsWrappingInteger || type.Semantics == SemanticsKind.Raw,
            MirBinaryOp.Div or MirBinaryOp.Rem => type.IsFloatingPoint || (literal is { } divisor && divisor != 0 && (divisor != -1 || op == MirBinaryOp.Rem || !type.IsInteger || type.IsUnsignedInteger)),
            MirBinaryOp.Shl or MirBinaryOp.Shr => literal is { } count && count >= 0 && count < this.Width(type, out _),
            _ => true,
        };
    }

    // SPEC 3.1: the bit width of an integer Type's representation and its signedness, isize and usize the target's pointer width; 0 for
    // another Type, such as a Type parameter, whose instance decides it.
    private int Width(BoundType type, out bool signed)
    {
        var integer = type.Underlying;
        signed = !integer.IsUnsignedInteger;
        return integer is not { IsInteger: true, Kind: BoundTypeKind.Primitive } ? 0 : integer.Name is "isize" or "usize" ? compilation.PointerWidth
            : int.TryParse(integer.Name.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var width) ? width : 0;
    }

    // SPEC 3.1: a literal's constant, a number's being the value Binding fitted (its plan row's Folded); -1 without one.
    private int Literal(Koto node)
    {
        var type = TypeOf(node);
        MirConst? constant = node switch
        {
            BoolLiteralKoto boolean => new(MirConstKind.Bool, type, boolean.Value ? 1 : 0),
            CharLiteralKoto { Value: { } character } => new(MirConstKind.Integer, type, character.Value),
            StringLiteralKoto text => new(MirConstKind.String, type, Value: text.Literal),
            UnitLiteralKoto => new(MirConstKind.Unit, type),
            NullLiteralKoto => new(MirConstKind.Integer, type),
            _ => HirTables.PlanOf(node).Folded is { } value ? new(type.IsFloatingPoint ? MirConstKind.Float : MirConstKind.Integer, type, value) : null,
        };

        return constant is { } known ? this.body.Const(known) : -1;
    }

    // Appends the operands evaluated onto the argument stack from start to the body's operands together, and pops them.
    private int Flush(int start)
    {
        var first = this.body.Operands.Length;
        for (var i = start; i < this.arguments.Count; i++)
        {
            this.body.AddOperand(this.arguments[i]);
        }

        this.arguments.RemoveRange(start, this.arguments.Count - start);
        return first;
    }

    private BoundType? TypeOf(in MirOperand operand) => operand.Kind == MirOperandKind.Const ? this.body.Consts[operand.Arg].Type : this.body.TypeOf(operand.Place);

    // An rvalue assigned at a node; a node that does not complete has no value to assign (SPEC 15.6.7: its operand diverged).
    private void Set(MirPlace destination, in MirRvalue rvalue, Koto node)
    {
        if (Completes(node))
        {
            this.body.Push(this.current, MirStatement.Assign(destination, rvalue, this.Info(node)));
        }
    }

    private void Use(MirPlace destination, in MirOperand operand, Koto node) => this.Set(destination, MirRvalue.Use(this.body.AddOperand(operand)), node);

    private void Unit(MirPlace destination, Koto node) => this.Use(destination, this.UnitOperand(), node);

    private MirOperand UnitOperand() => MirOperand.Const(this.body.Const(new(MirConstKind.Unit, BoundType.Unit)));

    private MirPlace Temp(Koto node) => this.Temp(TypeOf(node), node);

    private MirPlace Temp(BoundType type, Koto node, MirLocalRole role = MirLocalRole.None, string? name = null)
        => new(this.Live(this.body.AddLocal(new(type, MirLocalKind.Temp, this.Info(node, true), Name: name, Role: role)), this.temporaries));

    // An Unsupported form, or a derived one, in a temp.
    private MirPlace Unsupported(Koto node, bool derived = false)
    {
        var temp = this.Temp(node);
        if (derived)
        {
            this.Opaque(node, temp);
        }
        else
        {
            this.Unsupported(node, temp);
        }

        return temp;
    }
}
