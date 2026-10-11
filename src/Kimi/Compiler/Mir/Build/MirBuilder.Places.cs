// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Places (SPEC 3.4, 3.5): the Place a node designates, formed from its PlaceShape's Kind, its Res and its direct children, then followed
// through the Layers its consumer follows.
internal sealed partial class MirBuilder
{
    // The one operand of a group, a borrow, a conversion or a Deref.
    private static Koto Child(Koto node) => node switch { LabeledKoto label => label.Target, BinaryKoto binary => binary.Left, _ => ((UnaryKoto)node).Operand };

    // The Place a node designates: one an expansion located already (Located), a parenthesized Place its operand's; a value its consumer needs
    // as a Place is held in a temp.
    private MirPlace Place(Koto node)
    {
        if (this.Located(node) is { } located)
        {
            return located;
        }

        var shape = HirTables.PlaceOf(node);
        var place = shape.Kind switch
        {
            _ when node is ParenthesizedKoto group && shape.Kind != HirPlaceKind.Value => this.Place(group.Operand),
            HirPlaceKind.Field when node is MemberAccessKoto member => this.Member(member),
            HirPlaceKind.Local or HirPlaceKind.Static or HirPlaceKind.Field => this.Named(node),
            HirPlaceKind.Deref or HirPlaceKind.PairLayer => this.Layer(this.Place(Child(node)), shape.Kind),
            HirPlaceKind.Index or HirPlaceKind.ConstIndex => this.Element((IndexKoto)node, shape.Kind),
            HirPlaceKind.CallResult => this.Referent(node),
            HirPlaceKind.Value => this.Value(node),
            _ => this.Unsupported(node),
        };

        return this.Follow(place, shape.Layers);
    }

    // The Place an expansion located a node at, which its later calls read instead of locating it again (SPEC 13.7.2).
    private MirPlace? Located(Koto node)
    {
        for (var i = this.located.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(this.located[i].Node, node))
            {
                return this.located[i].Place;
            }
        }

        return null;
    }

    // One layer of a Place: a Deref (of a reference, an object handle or, explicitly, a raw pointer), or the PairLayer of the binder its
    // pair layer names (TyCtxt.Layer).
    private MirPlace Layer(MirPlace place, HirPlaceKind kind)
        => kind == HirPlaceKind.PairLayer ? this.body.Project(place, MirProjectionKind.PairLayer, this.Binder(this.body.TypeOf(place)))
            : this.body.Project(place, MirProjectionKind.Deref, 0);

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
        for (; count > 0 && !(target is not null && MirTypes.Agree(this.body.TypeOf(place), target)) && TyCtxt.Layer(
            this.body.TypeOf(place),
            out _,
            out _) is var kind and not HirPlaceKind.Value; count--)
        {
            place = this.Layer(place, kind);
        }

        return place;
    }

    // A name by its Res: a local, parameter, prepared parameter or environment binding, a static after its first-access check, or the
    // storage of the receiver's Property (an accessor's `storage`, SPEC 11.2).
    private MirPlace Named(Koto node)
    {
        var symbol = HirTables.Symbol(node);
        switch (HirTables.ResOf(node))
        {
            case HirRes.Local or HirRes.Param or HirRes.PreparedParam or HirRes.Capture when symbol is not null && this.Bound(symbol) is { } place:
                return place;
            case HirRes.Static when symbol is not null:
                return this.Static(symbol, node);
            case HirRes.Field when (symbol?.Kind == BindingSymbolKind.Storage ? HirTables.Symbol(symbol.Declaration!) : symbol) is { } property:
                return this.Storage(property, this.receiver < 0 ? null : new MirPlace(this.receiver), node);
            default:
                return this.Unsupported(node);
        }
    }

    // SPEC 11.1, 11.2, 11.3.2: a stored Property's storage, which its standard accessors and its accessors' `storage` read and write: a static
    // Property's slot, or the Property's Field of its receiver's Place.
    private MirPlace Storage(BindingSymbol property, MirPlace? receiver, Koto node)
    {
        if (HirTables.ResOf(property.Declaration!) == HirRes.Static)
        {
            return this.Static(property, node);
        }

        return receiver is { } place ? this.Field(this.Follow(place, 16), property, node) : this.Unsupported(node);
    }

    // The Place of a local or a parameter, in a default template a prepared parameter's slot its p_i references (SPEC 7.2.3), or in a
    // Closure's body of an environment binding: its capture's Field of the environment the receiver reaches (SPEC 7.6.2); null for another
    // symbol.
    private MirPlace? Bound(BindingSymbol symbol)
    {
        if (this.locals.TryGetValue(symbol, out var local))
        {
            var prepared = this.body.Locals[local].Role == MirLocalRole.PreparedInput;
            this.prepared |= prepared ? 1UL << Math.Min(local - 1, 63) : 0;
            return prepared ? this.body.Project(new(local), MirProjectionKind.Deref, 0) : new(local);
        }

        var captures = this.receiver > 0 && this.declaration is FunctionKoto { IsAnonymous: true } closure ? BoundClosure.Of(closure)?.Captures : null;
        var index = (captures?.Count ?? 0) - 1;
        while (index >= 0 && !ReferenceEquals(captures![index].Environment, symbol))
        {
            index--;
        }

        return index < 0 ? null : this.body.Project(this.Follow(new(this.receiver), 16), MirProjectionKind.Field, index);
    }

    // SPEC 22.2.3, plan 5.6.6: a static's Place, after the check of its state that initializes it at its first access, staticEnsure of its
    // slot in the body's statics, which names its initializer's body; a static whose declaration Binding rejected has none, and its use is
    // derived from that error.
    private MirPlace Static(BindingSymbol symbol, Koto node)
    {
        if (HirTables.Op(symbol.Declaration!) != HirOp.Body)
        {
            return this.Unsupported(node, derived: true);
        }

        var place = this.StaticRoot(symbol);
        var start = this.arguments.Count;
        this.arguments.Add(MirOperand.Const(this.body.Const(new(MirConstKind.Integer, BoundType.ISize, ~place.Root))));
        this.Invoke(this.Temp(BoundType.Unit, node), MirCallee.Intrinsic(IntrinsicId.StaticEnsure), start, node);
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
        var type = this.body.TypeOf(receiver);
        var position = -1;
        if (type is null || !AdtDef.FindField(type, field, out _, out position))
        {
            return this.Unsupported(node, position >= 0); // A Field whose stored Type Binding could not substitute is derived from that error.
        }

        var depth = 0;
        for (; position < Inherited(type); depth++)
        {
            type = AdtDef.Base(type)!;
        }

        return this.body.Project(
            depth == 0 ? receiver : this.body.Project(receiver, MirProjectionKind.Base, depth),
            MirProjectionKind.Field,
            position - Inherited(type));

        static int Inherited(BoundType type) => AdtDef.Base(type) is { } parent ? AdtDef.StorageCount(parent) : 0;
    }

    // SPEC 4.6.1, 4.6.9, 15.1.3: a built-in element at an integer position, a ConstIndex where the position is a constant (in range: its
    // PlaceShape), at a written ^x the length minus x, or at another key's resolution; SPEC 5.3: a raw pointer's p[n] is *(p + n).
    private MirPlace Element(IndexKoto index, HirPlaceKind kind)
    {
        var sequence = this.Place(index.Left);
        var key = ExpansionPlan.Of(index)?.First;
        if (key is not null || HirTables.Op(index.Right) == HirOp.Range)
        {
            var body = this.body;
            var resolved = key is null ? this.Boundary(index.Right, this.Length(sequence, index)) : this.Resolve(key, index, sequence);
            return body.Project(sequence, MirProjectionKind.Index, resolved.Root);
        }

        var position = this.Operand(index.Right, expected: null);
        if (this.body.TypeOf(sequence) is { Semantics: SemanticsKind.Raw } pointer)
        {
            var offset = this.Temp(pointer, index);
            var start = this.body.AddOperand(MirOperand.Copy(sequence));
            this.body.AddOperand(position);
            this.Set(offset, MirRvalue.Binary(MirBinaryOp.Add, start, proven: true), index);
            return this.body.Project(offset, MirProjectionKind.Deref, 0);
        }

        if (kind == HirPlaceKind.ConstIndex && position.Kind == MirOperandKind.Const)
        {
            return this.body.Project(sequence, MirProjectionKind.ConstIndex, (int)this.body.Consts[position.Arg].Bits);
        }

        return this.body.Project(sequence, MirProjectionKind.Index, this.Position(position, index.Right).Root);
    }

    // SPEC 4.6.2: a position as the isize a selection reads, its value compared mathematically: a Type no wider than isize wraps (a same-width
    // unsigned value isize cannot hold reads as negative, which the bounds check rejects), a wider one converts checked.
    private MirPlace Position(in MirOperand position, Koto node)
    {
        var type = this.TypeOf(position) ?? BoundType.ISize;
        var local = this.Temp(BoundType.ISize, node);
        var operand = this.body.AddOperand(position);
        var wraps = this.Width(type, out _) is var width and > 0 && width <= compilation.PointerWidth;
        MirRvalue value;
        if (ReferenceEquals(type, BoundType.ISize))
        {
            value = MirRvalue.Use(operand);
        }
        else
        {
            value = MirRvalue.Cast(wraps ? MirCastKind.Wrap : MirCastKind.Integer, operand, BoundType.ISize) with { Proven = wraps };
        }

        this.Set(local, value, node);
        return local;
    }

    // SPEC 7.1.1: the referent of the reference a place call, a user index or a getter publishes: its selection's call, else the node's.
    private MirPlace Referent(Koto node)
    {
        var result = this.Temp((ExpansionPlan.Of(node)?.Second ?? CallPlan.Of(node))!.ReturnType, node);
        this.Into(result, node);
        return this.body.Project(result, MirProjectionKind.Deref, 0);
    }

    // A value held in a temp where its consumer needs a Place, or in the place given where that has its Type, unless an expansion holds it
    // already.
    private MirPlace Value(Koto node, MirPlace? into = null)
    {
        if (this.Located(node) is { } located)
        {
            return located;
        }

        var temp = this.Temp(TypeOf(node), node, into);
        this.Into(temp, node);
        return temp;
    }
}
