// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Places (SPEC 3.4, 3.5): the Place a node designates, formed from its PlaceShape's Kind, its Res and its direct children, then followed
// through the Layers its consumer follows.
internal sealed partial class MirBuilder
{
    // Whether the body has an argument, its receiver where a Field of the implicit self or a capture reaches one (local 1).
    private bool Receiving => this.body.Locals.Length > 1 && this.body.Locals[1].Kind == MirLocalKind.Arg;

    // The one operand of a group, a borrow, a conversion or a Deref.
    private static Koto Child(Koto node) => node switch { LabeledKoto label => label.Target, BinaryKoto binary => binary.Left, _ => ((UnaryKoto)node).Operand };

    // The Place a node designates: a parenthesized Place is its operand's; a value its consumer needs as a Place is held in a temp.
    private MirPlace Place(Koto node)
    {
        var shape = HirTables.PlaceOf(node);
        var place = shape.Kind switch
        {
            _ when node is ParenthesizedKoto group && shape.Kind != HirPlaceKind.Value => this.Place(group.Operand),
            HirPlaceKind.Field when node is MemberAccessKoto member => this.Member(member),
            HirPlaceKind.Local or HirPlaceKind.Static or HirPlaceKind.Field => this.Named(node),
            HirPlaceKind.Deref or HirPlaceKind.PairLayer => this.Layer(this.Place(Child(node)), shape.Kind),
            HirPlaceKind.Index or HirPlaceKind.ConstIndex when CallPlan.Of(node) is null => this.Element((IndexKoto)node, shape.Kind),
            HirPlaceKind.CallResult => this.Referent(node),
            HirPlaceKind.Value => this.Value(node),
            _ => this.Unsupported(node), // A Pattern binding's payload (U11), an element a key resolves (U12).
        };

        return this.Follow(place, shape.Layers);
    }

    // One layer of a Place: a Deref (of a reference, an object handle or, explicitly, a raw pointer), or the PairLayer of the binder its
    // pair layer names (TyCtxt.Layer).
    private MirPlace Layer(MirPlace place, HirPlaceKind kind)
        => kind == HirPlaceKind.PairLayer ? this.body.Project(place, MirProjectionKind.PairLayer, this.Binder(this.body.TypeOf(place))) : this.body.Project(place, MirProjectionKind.Deref, 0);

    // The index in the body's binders of the binder a pair layer Type names; -1 for another Type.
    private int Binder(BoundType? type)
    {
        _ = TyCtxt.Layer(type, out _, out var target);
        var binder = this.body.Binders.Count - 1;
        while (binder >= 0 && !ReferenceEquals(this.body.Binders[binder].Target, target))
        {
            binder--;
        }

        return binder;
    }

    // SPEC 3.4.1, 13.5.5.1: up to count of the layers its Type's selection follows implicitly, stopping at a Place of the target Type.
    private MirPlace Follow(MirPlace place, int count, BoundType? target = null)
    {
        for (; count > 0 && !ReferenceEquals(this.body.TypeOf(place), target) && TyCtxt.Layer(this.body.TypeOf(place), out _, out _) is var kind and not HirPlaceKind.Value; count--)
        {
            place = this.Layer(place, kind);
        }

        return place;
    }

    // A name by its Res: a local or parameter, a static after its first-access check, a capture in the environment the Closure's receiver
    // reaches, or a Field of the receiver argument (an accessor's storage, SPEC 11.2).
    private MirPlace Named(Koto node)
    {
        var symbol = HirTables.Symbol(node);
        switch (HirTables.ResOf(node))
        {
            case HirRes.Local or HirRes.Param when symbol is not null && this.locals.TryGetValue(symbol, out var local):
                return new(local);
            case HirRes.Static when symbol is not null:
                return this.Static(symbol, node);
            case HirRes.Capture when this.Receiving && this.declaration is FunctionKoto { IsAnonymous: true } closure && BoundClosure.Of(closure) is { } captures:
                var index = captures.Captures.Count - 1;
                while (index >= 0 && !ReferenceEquals(captures.Captures[index].Environment, symbol))
                {
                    index--;
                }

                return index < 0 ? this.Unsupported(node) : this.body.Project(this.Follow(new(1), 16), MirProjectionKind.Field, index);
            case HirRes.Field when symbol is not null && this.Receiving:
                return this.Field(this.Follow(new(1), 16), symbol.Kind == BindingSymbolKind.Storage ? HirTables.Symbol(symbol.Declaration) : symbol, node);
            default:
                return this.Unsupported(node); // A prepared parameter of a default (U14).
        }
    }

    // SPEC 22.2.3, plan 5.6.6: a static's Place, after the check of its state that initializes it at its first access, staticEnsure of its
    // slot in the body's statics.
    private MirPlace Static(BindingSymbol symbol, Koto node)
    {
        var place = this.StaticRoot(symbol);
        var slot = this.body.AddOperand(MirOperand.Const(this.body.Const(new(MirConstKind.Integer, BoundType.ISize, ~place.Root))));
        var next = this.body.AddBlock();
        this.body.Terminate(this.current, MirTerminator.Call(MirCallee.Intrinsic(IntrinsicId.StaticEnsure), slot, 1, this.Temp(BoundType.Unit, node), next, this.Info(node)));
        this.current = next;
        return place;
    }

    private MirPlace StaticRoot(BindingSymbol symbol)
    {
        var type = symbol.Type ?? BoundType.Unit;
        return this.body.Static(new(symbol, type, symbol.Declaration is PropertyKoto { VariableKind: VariableKind.Var }, MirTypes.HoldsRaw(type)));
    }

    // SPEC 6.2.2, 3.2: a stored Field or a Tuple element of the receiver's Place, which the receiver's Layers follow.
    private MirPlace Member(MemberAccessKoto member)
    {
        var receiver = this.Place(member.Left);
        return member.Right is NumberLiteralKoto number && number.TryGetIntegerMagnitude(out var element)
            ? this.body.Project(receiver, MirProjectionKind.Field, (int)element) : this.Field(receiver, HirTables.Symbol(member), member);
    }

    // The stored Field of a symbol in a struct Place: AdtDef.FindField's position, the bases' Fields first, as the Base that declares it
    // (the receiver's StorageProjection) and the Field's own index there.
    private MirPlace Field(MirPlace receiver, BindingSymbol? field, Koto node)
    {
        var (type, position) = (this.body.TypeOf(receiver), -1);
        if (type is null || !AdtDef.FindField(type, field, out _, out position))
        {
            return this.Unsupported(node, position >= 0); // A Field whose stored Type Binding could not substitute is derived from that error.
        }

        var depth = 0;
        for (; position < Inherited(type); depth++)
        {
            type = AdtDef.Base(type)!;
        }

        return this.body.Project(depth == 0 ? receiver : this.body.Project(receiver, MirProjectionKind.Base, depth), MirProjectionKind.Field, position - Inherited(type));

        static int Inherited(BoundType type) => AdtDef.Base(type) is { } parent ? AdtDef.StorageCount(parent) : 0;
    }

    // SPEC 4.6.1, 15.1.3: a built-in element at an integer position, a ConstIndex where the position is a constant (in range: its PlaceShape);
    // SPEC 5.3: a raw pointer's p[n] is *(p + n).
    private MirPlace Element(IndexKoto index, HirPlaceKind kind)
    {
        var sequence = this.Place(index.Left);
        var position = this.Operand(index.Right);
        if (this.body.TypeOf(sequence) is { Semantics: SemanticsKind.Raw } pointer)
        {
            var (offset, start) = (this.Temp(pointer, index), this.body.AddOperand(MirOperand.Copy(sequence)));
            this.body.AddOperand(position);
            this.Set(offset, MirRvalue.Binary(MirBinaryOp.Add, start, true), index);
            return this.body.Project(offset, MirProjectionKind.Deref, 0);
        }

        if (kind == HirPlaceKind.ConstIndex && position.Kind == MirOperandKind.Const)
        {
            return this.body.Project(sequence, MirProjectionKind.ConstIndex, (int)this.body.Consts[position.Arg].Bits);
        }

        // SPEC 4.6.2: the position as the isize the Index reads, its value compared mathematically: a Type no wider than isize wraps (a
        // same-width unsigned value isize cannot hold reads as negative, which the bounds check rejects), a wider one converts checked.
        var type = this.TypeOf(position) ?? BoundType.ISize;
        var local = this.Temp(BoundType.ISize, index.Right);
        var operand = this.body.AddOperand(position);
        var wraps = this.Width(type, out _) is var width and > 0 && width <= compilation.PointerWidth;
        this.Set(local, ReferenceEquals(type, BoundType.ISize) ? MirRvalue.Use(operand) : MirRvalue.Cast(wraps ? MirCastKind.Wrap : MirCastKind.Integer, operand, BoundType.ISize) with { Proven = wraps }, index.Right);
        return this.body.Project(sequence, MirProjectionKind.Index, local.Root);
    }

    // SPEC 7.1.1: the referent of the reference a place call, a user index or a getter publishes: its selection's call, else the node's.
    private MirPlace Referent(Koto node)
    {
        var result = this.Temp((ExpansionPlan.Of(node)?.Second ?? CallPlan.Of(node))!.ReturnType, node);
        this.Into(result, node);
        return this.body.Project(result, MirProjectionKind.Deref, 0);
    }

    // A value held in a temp where its consumer needs a Place.
    private MirPlace Value(Koto node)
    {
        var temp = this.Temp(node);
        this.Into(temp, node);
        return temp;
    }
}
