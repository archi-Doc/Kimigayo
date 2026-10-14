// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Values: a node's own value into a destination (Into), as its consumer takes it (Assign, Operand, Acquire), each by the node's Op, Adjust
// and PlaceShape.
internal sealed partial class MirBuilder
{
    // The value a node's own form produces, into the destination.
    [CodeRuleException("KR2006", Limit = 67, Reason = "Flat dispatch over the closed HirOp vocabulary; each row invokes its owning lowering operation.")]
    private void Into(MirPlace destination, Koto node)
    {
        switch (HirTables.Op(node))
        {
            case HirOp.Literal when this.Literal(node) is var constant and >= 0:
                this.Use(destination, MirOperand.Const(constant), node);
                break;
            case HirOp.Name when HirTables.Symbol(node)?.Kind == BindingSymbolKind.Function && HirTables.Meaning(node) is BoundType item:
                this.Use(destination, MirOperand.Const(this.body.Const(new(MirConstKind.FnItem, item))), node); // SPEC 7.7.4: a constant of its Item Type.
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
            case HirOp.Require:
                this.Require((RequireKoto)node);
                break;
            case HirOp.Defer:
                this.Register(Cleanup.Defer, -1, node, this.lexical);
                break;
            case HirOp.Discard:
                this.Discard((DiscardKoto)node);
                break;
            case HirOp.Jump or HirOp.Continue:
                this.Transfer((JumpKoto)node);
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
            case HirOp.NoInit: // SPEC 4.3.4: the array constructed from no operand, its elements not stored.
                this.Set(destination, MirRvalue.Aggregate(MirAggregateKind.FixedArray, this.body.Operands.Length, 0, this.body.TypeOf(destination)!), node);
                break;
            case HirOp.PrimitiveOp:
                this.Primitive(destination, node, TypeOf(node));
                break;
            case HirOp.Logical:
                this.Logical(destination, (BinaryKoto)node);
                break;
            case HirOp.Borrow: // SPEC 13.5.6, 5.4: the operand's Place borrowed as the node's Type says, or its address.
                this.Set(destination, this.Ref(this.Place(Child(node)), TypeOf(node), input: false), node);
                break;
            case HirOp.Convert:
                this.Convert(destination, (BinaryKoto)node, TypeOf(node));
                break;
            case HirOp.Aggregate:
                this.Aggregate(destination, node, TypeOf(node));
                break;
            case HirOp.Assign or HirOp.CompoundAssign when Rejected(Child(node)): // An update of a target Binding rejected is derived from its error.
                this.Opaque(node, destination);
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
            case HirOp.Name or HirOp.Field or HirOp.Index or HirOp.Deref when HirTables.PlaceOf(node).Kind is not (HirPlaceKind.Unset or HirPlaceKind.Value):
                // SPEC 3.5, 14.2: a Place's value, discarded or taken as a value, is its Place evaluated (an Index checks its bounds) and read
                // as its Type decides: Copied, a stored exclusive reference Reborrowed, a pair layer by case.
                this.Use(destination, this.Acquire(new(HirAcquire.ByCase, HirAdaptation.None), this.Place(node), null, input: false, node, destination), node);
                break;
            case HirOp.DictionaryLiteral:
                this.Dictionary(destination, (DictionaryLiteralKoto)node);
                break;
            case HirOp.Interpolation:
                this.Format(destination, node);
                break;
            default: // Binding's Error too, whose value is derived (Rejected).
                this.Unsupported(node, destination);
                break;
        }
    }

    // The destination takes a node's value as its consumer takes it (its Adjust): a Temporary, or a group's operand, is built into the
    // destination; any other acquisition is an operand of the destination's Type, a value it forms (a borrow, an erasure) formed there.
    private void Assign(MirPlace destination, Koto node)
    {
        if (HirTables.AdjustOf(node) is { Acquire: HirAcquire.Temporary or HirAcquire.None, Adaptation: HirAdaptation.None })
        {
            this.Into(destination, node);
        }
        else
        {
            this.Use(destination, this.Operand(node, this.body.TypeOf(destination), input: false, destination), node);
        }
    }

    // A node's value as its consumer takes it (plan: Adjust to operand): a literal as its constant, another value through a temp, a Place,
    // or a value held in a temp, as Acquire takes it for the expected Type, a call's input as the call's (input), a value the acquisition
    // forms in the place given (into) where it has that Type.
    private MirOperand Operand(Koto node, BoundType? expected, bool input = false, MirPlace? into = null)
    {
        var adjust = HirTables.AdjustOf(node);
        if (adjust is { Acquire: HirAcquire.Temporary or HirAcquire.None, Adaptation: HirAdaptation.None })
        {
            return HirTables.Op(node) == HirOp.Literal && this.Literal(node) is var constant and >= 0 ? MirOperand.Const(constant)
                : MirOperand.Move(this.Value(node, into));
        }

        if (adjust.Acquire == HirAcquire.Unset || adjust.Adaptation == HirAdaptation.StorageProjection || (adjust.Adaptation == HirAdaptation.BaseBorrow &&
            expected?.Components is not [_]) || (adjust.Adaptation == HirAdaptation.Erasure && expected is null))
        {
            // An Error proof left the acquisition unset; a projected receiver's storage is its Field's (Member).
            return MirOperand.Move(this.Unsupported(node, derived: false, expected));
        }

        return this.Acquire(adjust, adjust.Acquire == HirAcquire.Temporary ? this.Value(node) : this.Place(node), expected, input, node, into);
    }

    // A Place as an acquisition and an adaptation take it: Copied, moved, acquired by its pair layer's case (SPEC 8.14; where its Type
    // decides the case, as in a default's copy, Copied or Reborrowed as that Type is) or, a stored exclusive reference, Reborrowed in its
    // own mode (SPEC 15.1.5). SPEC 10.2: adapted, the Scalar its reference layers end in, Copied; a Closure or
    // function item erased to its Function Type; else a reference, of the expected Type or the one its mode forms, to the Place itself (a
    // shared or exclusive borrow), to the referent its layers reach (ReferenceRead), to an object handle's payload or to the base the
    // expected Type names. An implicit exclusive borrow of a call's input is two-phase: reserved here, activated at the Call (SPEC 15.6.7).
    private MirOperand Acquire(HirAdjust adjust, MirPlace place, BoundType? expected, bool input, Koto node, MirPlace? into = null)
    {
        var adaptation = adjust.Adaptation;
        var type = this.body.TypeOf(place) ?? BoundType.Unit;
        var target = expected?.Components is [var referent] ? referent : null;
        switch (adaptation)
        {
            case HirAdaptation.None:
                return adjust.Acquire switch
                {
                    HirAcquire.ByCase when this.Binder(type) is var binder and >= 0 => MirOperand.PairAcquire(binder, place),
                    HirAcquire.ByCase when !MirTypes.IsExclusive(type) => MirOperand.Copy(place),
                    HirAcquire.Reborrow or HirAcquire.ByCase => this.Temporary(type, this.Ref(place, type, input: false), node, into),
                    HirAcquire.Copy => MirOperand.Copy(place),
                    _ => MirOperand.Move(place),
                };
            case HirAdaptation.ReferentRead:
                return MirOperand.Copy(this.Follow(place, 16, expected));
            case HirAdaptation.Erasure: // SPEC 7.7.4: of the value its ordinary acquisition gives.
                var source = this.Acquire(adjust with { Adaptation = HirAdaptation.None }, place, null, input, node);
                return this.Temporary(
                    expected!,
                    MirRvalue.Cast(MirCastKind.Erasure, this.body.AddOperand(source), expected!) with { Proven = true },
                    node,
                    into);
            case HirAdaptation.ReferenceRead:
                place = this.Follow(place, 16, target);
                break;
            case HirAdaptation.PayloadProjection:
                place = this.body.Project(place, MirProjectionKind.Deref, 0);
                break;
        }

        var reference = expected ?? compilation.Types.Reference(
            adaptation == HirAdaptation.ExclusiveBorrow ? SemanticsKind.Uniq : SemanticsKind.Ref,
            this.body.TypeOf(place) ?? BoundType.Unit);
        return this.Temporary(reference, this.Ref(place, reference, input), node, into);
    }

    // SPEC 13.5.6, 5.4, 6.2.2: a reference of a Type to a Place, the one rule of every borrow: of the Place's layer whose Type is the
    // reference's referent (an object borrow's payload, a reborrow's referent) or its base that is (a base view), in its Type's mode
    // (MirTypes.RefKind; an exclusive borrow of a call's input is two-phase).
    private MirRvalue Ref(MirPlace place, BoundType reference, bool input)
    {
        var referent = reference.Components is [var target] ? target : null;
        var depth = 0;
        place = referent is null ? place : this.Follow(place, 16, referent);
        for (var layer = this.body.TypeOf(place); referent is not null && layer is not null && !MirTypes.Agree(layer, referent); layer = AdtDef.Base(layer))
        {
            depth = AdtDef.Base(layer) is null ? 0 : depth + 1;
        }

        return MirRvalue.Ref(MirTypes.RefKind(reference, input), depth == 0 ? place : this.body.Project(place, MirProjectionKind.Base, depth), reference);
    }

    // An rvalue in a new temp of its Type, or in the place given where that has its Type, moved.
    private MirOperand Temporary(BoundType type, in MirRvalue rvalue, Koto node, MirPlace? into = null)
    {
        var temp = this.Temp(type, node, into);
        this.Set(temp, rvalue, node);
        return MirOperand.Move(temp);
    }

    // A borrow of a Place in a new temp of the reference a Semantics kind forms to it (uniq: exclusive; ref, objref: shared), moved; a part
    // made where it is built (Make).
    private MirOperand Borrow(SemanticsKind semantics, MirPlace place, Koto node)
    {
        var view = compilation.Types.Reference(semantics, this.body.TypeOf(place) ?? BoundType.Unit);
        var temp = this.Temp(view, node);
        this.Make(temp, this.Ref(place, view, input: false), node);
        return MirOperand.Move(temp);
    }

    // SPEC 3.2, 4.3.2, 6.6: a Tuple, a fixed array or an enum Case value from its operands in order, an enum's payloads as its construction
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
            ArrayLiteralKoto array when type is { Kind: BoundTypeKind.FixedArray } && (array.FillLength is null || type.LengthExpression is null) =>
                array.Elements,
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

        MirAggregateKind kind;
        if (construction is not null)
        {
            kind = MirAggregateKind.Adt;
        }
        else
        {
            kind = node is TupleLiteralKoto ? MirAggregateKind.Tuple : MirAggregateKind.FixedArray;
        }

        var count = this.arguments.Count - start;
        this.Set(destination, MirRvalue.Aggregate(kind, this.Flush(start), count, type, construction?.Case.Ordinal ?? 0), node);
    }

    // SPEC 7.7.1, 7.7.2: creating a Closure evaluates its captures, not its body: each environment binding, in entry order, from the Place
    // of its source binding (Bound) as its entry acquires it, the Closure value their aggregate. A source not bound here is unsupported.
    private void Closure(MirPlace destination, FunctionKoto closure)
    {
        var captures = BoundClosure.Of(closure)!.Captures;
        var start = this.arguments.Count;
        for (var i = 0; i < captures.Count; i++)
        {
            var type = captures[i].Environment.Type ?? BoundType.Unit;
            if (this.Bound(captures[i].Source) is not { } place)
            {
                this.Unsupported(closure, place = this.Temp(type, closure));
            }

            this.arguments.Add(this.Acquire(captures[i].Adjust, place, type, input: false, closure));
        }

        this.Set(destination, MirRvalue.Aggregate(MirAggregateKind.Closure, this.Flush(start), captures.Count, TypeOf(closure)), closure);
    }

    // SPEC 13.7.1: the value secured first, then the target located, its old value dropped where its Type may need it, and the value placed;
    // a target that is not a Place is a Property's, whose setter (its last call, after any getter) takes the value, evaluated first.
    private void Replace(MirPlace destination, BinaryKoto assignment)
    {
        if (HirTables.PlaceOf(assignment.Left).Kind is HirPlaceKind.Unset or HirPlaceKind.Value)
        {
            if ((CallPlan.Of(assignment.Left, 1) ?? CallPlan.Of(assignment.Left)) is { } setter)
            {
                this.Call(destination, setter, assignment, assignment.Right);
            }
            else
            {
                this.Unsupported(assignment, destination);
            }

            return;
        }

        var value = this.Temp(Completes(assignment.Left) ? assignment.Left : assignment.Right); // Of the Type it is placed at, if it is placed.
        this.Assign(value, assignment.Right);
        this.Overwrite(this.Place(assignment.Left), MirOperand.Move(value), assignment);
        this.Unit(destination, assignment);
    }

    // SPEC 13.7.1: a target's old value dropped where its Type may need it (A2 elaborates a moved one away), then the value placed.
    private void Overwrite(MirPlace target, in MirOperand value, Koto node)
    {
        if (Completes(node) && MirTypes.MayNeedDrop(this.body.TypeOf(target)))
        {
            this.DropAt(target, this.Info(node));
        }

        this.Use(target, value, node);
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
            _ when HirTables.PlanOf(node).Folded is { } value => new(type.IsFloatingPoint ? MirConstKind.Float : MirConstKind.Integer, type, value),
            _ => null,
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

    private BoundType? TypeOf(in MirOperand operand) => operand.Kind == MirOperandKind.Const ? this.body.Consts[operand.Arg].Type
        : this.body.TypeOf(operand.Place);

    // The node's own value, made at its end from the operands the node evaluated, assigned only where the node completes: a node that does
    // not complete has no value to assign (SPEC 15.6.7: its operand diverged).
    private void Set(MirPlace destination, in MirRvalue rvalue, Koto node)
    {
        if (Completes(node))
        {
            this.Make(destination, rvalue, node);
        }
    }

    // A part of a node made where it is built, before later parts that may not complete, so it exists on every path that reaches it: it
    // reads only Places already located or values made before it (a Ref, a Len, an empty collection the parts fill, an Opaque value
    // standing for its node, a result's discriminant; SPEC 12.4.3, 4.8.1). The one unguarded assignment beside Set.
    private void Make(MirPlace destination, in MirRvalue rvalue, Koto node)
        => this.body.Push(this.current, MirStatement.Assign(destination, rvalue, this.Info(node)));

    // The destination takes an operand, unless the operand is the destination's own value moved (an acquisition formed it there).
    private void Use(MirPlace destination, in MirOperand operand, Koto node)
    {
        if (operand.Kind != MirOperandKind.Move || operand.Place != destination)
        {
            this.Set(destination, MirRvalue.Use(this.body.AddOperand(operand)), node);
        }
    }

    private void Unit(MirPlace destination, Koto node) => this.Use(destination, this.UnitOperand(), node);

    private MirOperand UnitOperand() => MirOperand.Const(this.body.Const(new(MirConstKind.Unit, BoundType.Unit)));

    private MirPlace Temp(Koto node) => this.Temp(TypeOf(node), node);

    private MirPlace Temp(BoundType type, Koto node, MirLocalRole role = MirLocalRole.None, string? name = null)
        => new(this.Live(this.body.AddLocal(new(type, MirLocalKind.Temp, this.Info(node, local: true), Name: name, Role: role)), this.temporaries));

    // The place given where it has the Type, else a new temp of it.
    private MirPlace Temp(BoundType type, Koto node, MirPlace? into) => into is { } place && ReferenceEquals(this.body.TypeOf(place), type) ? place
        : this.Temp(type, node);

    // An Unsupported form, or a derived one, in a temp of the Type its consumer expects, else of its own.
    private MirPlace Unsupported(Koto node, bool derived = false, BoundType? type = null)
    {
        var temp = this.Temp(type ?? TypeOf(node), node);
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
