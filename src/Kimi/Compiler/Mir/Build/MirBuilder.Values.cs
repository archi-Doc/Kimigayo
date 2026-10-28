// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Values: a node's own value into a destination (Into), as its consumer takes it (Assign, Operand; its acquisition in
// MirBuilder.Acquisitions.cs), each by the node's Op, Adjust and PlaceShape.
internal sealed partial class MirBuilder
{
    // SPEC 12.2: whether a node's operand builds code where it is evaluated, before which a Place operand evaluated earlier in its
    // operation is secured, so that it is read where it is evaluated (Secure): all but a literal's constant and a Place read as it is
    // (Copied, Moved or through its referent) whose path from a local is stored Fields, Derefs and constant positions (Operand, Acquire,
    // Place).
    private static bool Builds(Koto node)
    {
        var adjust = HirTables.AdjustOf(node);
        if (InPlace(adjust))
        {
            return HirTables.Op(node) != HirOp.Literal;
        }

        var read = Reads(adjust);
        while (read && HirTables.Op(node) != HirOp.Name && HirTables.PlaceOf(node).Kind is HirPlaceKind.Field or HirPlaceKind.Deref or
            HirPlaceKind.PairLayer or HirPlaceKind.ConstIndex)
        {
            node = Child(node);
        }

        return !read || HirTables.PlaceOf(node).Kind != HirPlaceKind.Local;
    }

    // Whether an acquisition reads its Place as it is (Copied, Moved or through its referent), so it builds no code of its own (Builds).
    private static bool Reads(HirAdjust adjust) => adjust.Adaptation == HirAdaptation.ReferentRead
        ? adjust.Acquire is not (HirAcquire.Unset or HirAcquire.Temporary)
        : adjust is { Adaptation: HirAdaptation.None, Acquire: HirAcquire.Copy or HirAcquire.Move };

    // Whether a consumer takes a node's value as the node builds it (its Adjust): a Temporary or a group's operand, not adapted (Assign).
    private static bool InPlace(HirAdjust adjust) => adjust is { Acquire: HirAcquire.Temporary or HirAcquire.None, Adaptation: HirAdaptation.None };

    // The value a node's own form produces, into the destination; as a call's input that reserves (input), a written exclusive borrow,
    // also inside parentheses, is two-phase (SPEC 15.6.7), and a selection's or a do's bodies give their values as Result places them.
    [CodeRuleException("KR2006", Limit = 67, Reason = "Flat dispatch over the closed HirOp vocabulary; each row invokes its owning lowering operation.")]
    private void Into(MirPlace destination, Koto node, bool input = false)
    {
        switch (HirTables.Op(node))
        {
            case HirOp.Literal:
                this.Use(destination, this.Literal(node), node);
                break;
            case HirOp.Name when HirTables.Symbol(node)?.Kind == BindingSymbolKind.Function && HirTables.Meaning(node) is BoundType item:
                this.Use(destination, MirOperand.Const(this.body.Const(new(MirConstKind.FnItem, item))), node); // SPEC 7.7.4: a constant of its Item Type.
                break;
            case HirOp.Group: // Its operand, which the group acquires as the operand's Adjust says.
                this.Assign(destination, Child(node), input);
                break;
            case HirOp.Block:
                this.Block(destination, (CodeBlockKoto)node);
                break;
            case HirOp.Call when HirTables.PlaceOf(node).Kind != HirPlaceKind.CallResult: // A subscript's element is read below as its Place.
                this.Call(destination, node);
                break;
            case HirOp.If:
                this.If(destination, (IfKoto)node, input);
                break;
            case HirOp.Match or HirOp.Try:
                this.Match(destination, (MatchKoto)node, input);
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
                this.Do(destination, (DoKoto)node, input);
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
                this.Set(destination, this.Ref(this.Place(Child(node), input), TypeOf(node), input), node);
                break;
            case HirOp.Convert:
                this.Convert(destination, (BinaryKoto)node, TypeOf(node), input);
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
            case HirOp.Name or HirOp.Field or HirOp.Index or HirOp.Deref or HirOp.Call when HirTables.PlaceOf(node).Kind is not (HirPlaceKind.Unset or HirPlaceKind.Value):
                // SPEC 3.5, 14.2.2: a discarded Place's value is its Place evaluated (an Index checks its bounds) and acquired by its bare
                // acquisition, its plan row's Adjust (DECISIONS R4-M145), as any consumed child's Adjust is (Operand).
                this.Use(destination, this.Operand(node, HirTables.PlanOf(node).Adjust, this.body.TypeOf(destination), input, destination), node);
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
    // destination; any other acquisition is an operand of the destination's Type, a value it forms (a borrow, an erasure) formed there,
    // as a call's input where the destination holds one (input).
    private void Assign(MirPlace destination, Koto node, bool input = false)
    {
        if (InPlace(HirTables.AdjustOf(node)))
        {
            this.Into(destination, node, input);
        }
        else
        {
            this.Use(destination, this.Operand(node, this.body.TypeOf(destination), input, destination), node);
        }
    }

    // A node's value as its consumer takes it, by the node's Adjust.
    private MirOperand Operand(Koto node, BoundType? expected, bool input = false, MirPlace? into = null)
        => this.Operand(node, HirTables.AdjustOf(node), expected, input, into);

    // A node's value as an Adjust takes it (plan: Adjust to operand; the column's, or an update getter's plan row, DECISIONS R4-M109): a
    // literal as its constant, another value through a temp, a Place, or a value held in a temp, as Acquire takes it for the expected Type,
    // a call's input as the call's (input), a value the acquisition forms in the place given (into) where it has that Type. SPEC 15.6.7:
    // only an input the call takes as an exclusive reference (its expected Type, or without one, a value call's callee, the reference it is
    // or its acquisition forms) starts a reservation, so only its path is a preparation path (Place); on another input's path a written
    // exclusive borrow is live, as SPEC 7.3.3's is. An input the call takes as a pair layer, exclusive only by case, reserves where it is
    // acquired (reserves, MirCases.Reserves) and has no preparation path (A3 E13 (4)).
    private MirOperand Operand(Koto node, HirAdjust adjust, BoundType? expected, bool input = false, MirPlace? into = null)
    {
        var reserves = input && MirCases.Reserves(this.body, expected ?? TypeOf(node));
        input &= MirTypes.IsExclusive(expected) ||
            (expected is null && (adjust.Adaptation == HirAdaptation.ExclusiveBorrow || MirTypes.IsExclusive(TypeOf(node))));
        reserves |= input;
        if (InPlace(adjust))
        {
            return HirTables.Op(node) == HirOp.Literal ? this.Literal(node) : MirOperand.Move(this.Value(node, into, reserves, expected));
        }

        if (adjust.Acquire == HirAcquire.Unset || adjust.Adaptation == HirAdaptation.StorageProjection || (adjust.Adaptation == HirAdaptation.BaseBorrow &&
            expected?.Components is not [_]) || (adjust.Adaptation == HirAdaptation.Erasure && expected is null))
        {
            // An Error proof or a Place without a Type (Bare) leaves a consumed child's acquisition unset, so its value is derived from
            // that error (SPEC 23.1.2); a projected receiver's storage is its Field's (Member), and the other adaptations are not built yet.
            return MirOperand.Move(this.Unsupported(node, adjust.Acquire == HirAcquire.Unset, expected));
        }

        var place = adjust.Acquire == HirAcquire.Temporary ? this.Value(node, input: input) : this.Place(node, input);
        return this.Acquire(adjust, place, expected, reserves, node, into);
    }

    // SPEC 3.2, 4.3.1, 4.3.3, 6.6, 12.4.1: a Tuple, a fixed array, an Array or an enum Case value from its operands in order, an enum's
    // payloads as its construction acquires them, a fill from its value acquired once, which every element of its Type's length copies
    // (Fill, DECISIONS R4-M81); an Array's elements moved into the one buffer its aggregate allocates (DECISIONS R5-M129).
    private void Aggregate(MirPlace destination, Koto node, BoundType type)
    {
        var construction = BoundEnumConstruction.Of(node);
        var elements = node switch
        {
            TupleLiteralKoto tuple => tuple.Elements,
            ArrayLiteralKoto array when type.Kind == BoundTypeKind.FixedArray => array.Elements,
            ArrayLiteralKoto { FillLength: null } array when type.Kind == BoundTypeKind.Array => array.Elements,
            _ => null,
        };
        if (construction is null && elements is null)
        {
            this.Unsupported(node, destination);
            return;
        }

        var start = this.arguments.Count;
        var written = elements?.Count ?? 0;
        var payloads = construction is null ? default : construction.PayloadOperations;
        var last = -1; // SPEC 12.2: the last operand that builds code (Builds); one before it is secured where it is evaluated.
        for (var i = 0; i < written + payloads.Length; i++)
        {
            if (Builds(i < written ? elements![i] : payloads[i - written].Source!))
            {
                last = i;
            }
        }

        for (var i = 0; i < written; i++)
        {
            var element = type.Kind == BoundTypeKind.Tuple ? i : 0; // The element's Type, which a Type Binding rejected may not have.
            var operand = this.Operand(elements![i], element < type.Components.Count ? type.Components[element] : null);
            this.arguments.Add(i < last ? this.Secure(operand, elements[i]) : operand);
        }

        for (var i = 0; i < payloads.Length; i++)
        {
            var operand = this.Operand(payloads[i].Source!, payloads[i].AdaptedType ?? payloads[i].ParameterType);
            this.arguments.Add(written + i < last ? this.Secure(operand, payloads[i].Source!) : operand);
        }

        MirAggregateKind kind;
        if (construction is not null)
        {
            kind = MirAggregateKind.Adt;
        }
        else if (node is ArrayLiteralKoto { FillLength: not null })
        {
            kind = MirAggregateKind.Fill; // SPEC 4.3.3: its one operand, the value acquired once even for length 0, in every element.
        }
        else
        {
            kind = type.Kind switch
            {
                BoundTypeKind.FixedArray => MirAggregateKind.FixedArray,
                BoundTypeKind.Array => MirAggregateKind.Array, // SPEC 4.9.5: one allocation for its elements, none for `[]`.
                _ => MirAggregateKind.Tuple, // A Tuple's may be Unit.
            };
        }

        var count = this.arguments.Count - start;
        this.Set(destination, MirRvalue.Aggregate(kind, this.Flush(start), count, type, construction?.Case.Ordinal ?? 0), node);
    }

    // SPEC 7.7.1, 7.7.2: creating a Closure evaluates its captures, not its body: each environment binding, in entry order, from the Place
    // of its source binding (Bound) as its entry acquires it, the Closure value their aggregate. A source not bound here is unsupported.
    // SPEC 12.2 (A3 E13 (2)): a capture before a later one whose acquisition builds code is secured where it is evaluated.
    private void Closure(MirPlace destination, FunctionKoto closure)
    {
        var captures = closure.ClosureDefinition!.Captures;
        var start = this.arguments.Count;
        var last = -1;
        for (var i = 0; i < captures.Count; i++)
        {
            last = Reads(captures[i].Adjust) ? last : i;
        }

        for (var i = 0; i < captures.Count; i++)
        {
            var type = captures[i].Environment.Type ?? BoundType.Unit;
            if (this.Bound(captures[i].Source) is not { } place)
            {
                place = this.Unsupported(closure, type: type);
            }

            var operand = this.Acquire(captures[i].Adjust, place, type, input: false, closure);
            this.arguments.Add(i < last ? this.Secure(operand, closure) : operand);
        }

        this.Set(destination, MirRvalue.Aggregate(MirAggregateKind.Closure, this.Flush(start), captures.Count, TypeOf(closure)), closure);
    }

    // SPEC 13.7.1: the value secured first, then the target located, its old value dropped where its Type may need it, and the value placed;
    // a target that is not a Place is a Property's, designated through its parentheses, whose setter (its last call, after any getter)
    // takes the value, evaluated first.
    private void Replace(MirPlace destination, BinaryKoto assignment)
    {
        if (HirTables.PlaceOf(assignment.Left).Kind is HirPlaceKind.Unset or HirPlaceKind.Value)
        {
            var property = KotoHelper.UnwrapParentheses(assignment.Left);
            if ((CallPlan.Of(property, 1) ?? CallPlan.Of(property)) is { } setter)
            {
                this.Call(destination, setter, assignment, assignment.Right);
            }
            else
            {
                this.Unsupported(assignment, destination);
            }

            return;
        }

        // Of the Type it is placed at, or the Never value of a right side of Type Never, which is not placed (Overwrite).
        var source = ReferenceEquals(TypeOf(assignment.Right), BoundType.Never) ? assignment.Right : assignment.Left;
        var value = this.Hold(TypeOf(source), source);
        this.Assign(value, assignment.Right);
        this.Made(value);
        this.Overwrite(this.Place(assignment.Left), value, assignment);
        this.Unit(destination, assignment);
    }

    // SPEC 13.7.1: a target's old value dropped where its Type may need it (A2 elaborates a moved one away), then the value placed; the
    // Never value is neither, so the target keeps its old value (DECISIONS R4-M62).
    private void Overwrite(MirPlace target, MirPlace value, Koto node)
    {
        if (!this.IsNever(value) && MirTypes.MayNeedDrop(this.body.TypeOf(target), this.walked))
        {
            this.DropAt(target, this.Info(node));
        }

        this.Use(target, MirOperand.Move(value), node);
    }

    // SPEC 3.1: a published Literal's constant, a number's being the value Binding fitted (its plan row's Folded); a literal the lexer
    // could not read is published Error (Binding.Rejected). A published Literal without its constant is a broken contract, the one check
    // of that guarantee: InternalInvariant_Kd, the value a derived Opaque.
    private MirOperand Literal(Koto node)
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

        if (constant is { } known)
        {
            return MirOperand.Const(this.body.Const(known));
        }

        this.program.Report(SourceRef.Of(node), DiagnosticCode.InternalInvariant_Kd);
        return MirOperand.Move(this.Unsupported(node, derived: true));
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

    // The one assignment: a node's own value or a part of it, made where it is built from the operands evaluated before it, also where
    // only the checks continue (assumed completion, SPEC 14.10.3; DECISIONS R4-M62). Nothing is built that has no value: an rvalue that
    // has an input (an operand, or the Place a Ref, Len or Discriminant reads) whose every input is the Never value, or a value assigned
    // to the Never value, since a node of Type Never makes none (but its Opaque value, which stands for its syntax, of its Type).
    private void Set(MirPlace destination, in MirRvalue rvalue, Koto node)
    {
        var never = rvalue.Kind is MirRvalueKind.Ref or MirRvalueKind.Len or MirRvalueKind.Discriminant ? this.IsNeverValue(rvalue.Place) : rvalue.Count > 0;
        for (var i = 0; never && i < rvalue.Count; i++)
        {
            never = this.body.Operands[rvalue.Start + i] is { Kind: not MirOperandKind.Const } operand && this.IsNeverValue(operand.Place);
        }

        if (!never && (rvalue.Kind == MirRvalueKind.Opaque || !this.IsNeverValue(destination)))
        {
            this.body.Push(this.current, MirStatement.Assign(destination, rvalue, this.Info(node)));
        }
    }

    // SPEC 3.1.3, 3.8: whether a Place is the Never value, a temp of Type Never, which carries no Loan and fits any operand position; only
    // an unadapted Copy or Move of a Never Place (Acquire), an Opaque value of Type Never and a diverging Call write it (DECISIONS R4-M62,
    // R5-M38, R5-M33; MirValidator).
    private bool IsNeverValue(MirPlace place) => this.IsTemp(place) && this.IsNever(place);

    // Whether a Place is of Type Never: the Never value or a named Place, neither of which has a referent to borrow or view.
    private bool IsNever(MirPlace place) => ReferenceEquals(this.body.TypeOf(place), BoundType.Never);

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
        => this.Made(this.Hold(type, node, role, name));

    // The place given where it has the Type, else a new temp of it.
    private MirPlace Temp(BoundType type, Koto node, MirPlace? into) => this.Given(into, type) ?? this.Temp(type, node);

    // The place given where it has the Type, which its holder makes (Made), else none.
    private MirPlace? Given(MirPlace? into, BoundType type) => into is { } place && ReferenceEquals(this.body.TypeOf(place), type) ? place : null;

    // An Unsupported form, or a derived one, in a temp of the Type its consumer expects, else of its own: an Opaque temp, which nothing
    // lends (Unlent; DECISIONS R4-M149).
    private MirPlace Unsupported(Koto node, bool derived = false, BoundType? type = null)
    {
        var temp = this.Temp(type ?? TypeOf(node), node);
        this.opaqueTemps.Add(temp.Root);
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
