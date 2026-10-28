// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Places (SPEC 3.4, 3.5): the Place a node designates, formed from its PlaceShape's Kind, its Res and its direct children, then followed
// through the Layers its consumer follows.
internal sealed partial class MirBuilder
{
    // The one operand of a group, a borrow, a conversion or a Deref.
    private static Koto Child(Koto node) => node switch { LabeledKoto label => label.Target, BinaryKoto binary => binary.Left, _ => ((UnaryKoto)node).Operand };

    // The written exclusive borrow a node's value is, also in parentheses; null for another node.
    private static Koto? Lending(Koto node) => HirTables.Op(node) switch
    {
        HirOp.Group => Lending(Child(node)),
        HirOp.Borrow when MirTypes.IsExclusive(TypeOf(node)) => node,
        _ => null,
    };

    // The Place a node designates: one an expansion located already (Located), a parenthesized Place its operand's; a value its consumer needs
    // as a Place is held in a temp. A reserved input's path (input) is its preparation path down to a value or a call result (SPEC
    // 15.6.7): a written exclusive borrow it continues through is not formed, the path going on from the Place it lends, so the input has
    // one reservation, at its lending point, as the path without it has (DECISIONS R4-M54).
    private MirPlace Place(Koto node, bool input = false)
    {
        if (this.Located(node) is { } located)
        {
            return located;
        }

        var shape = HirTables.PlaceOf(node);
        if (input && shape is { Kind: HirPlaceKind.Value, Layers: > 0 } && Lending(node) is { } borrow)
        {
            return this.Follow(this.Lent(this.Place(Child(borrow), input), TypeOf(borrow)), shape.Layers - 1);
        }

        var place = shape.Kind switch
        {
            _ when HirTables.Op(node) == HirOp.Group && shape.Kind != HirPlaceKind.Value => this.Place(Child(node), input),
            HirPlaceKind.Field when node is MemberAccessKoto member => this.Member(member, input),
            HirPlaceKind.Local or HirPlaceKind.Static or HirPlaceKind.Field => this.Named(node),
            HirPlaceKind.Deref or HirPlaceKind.PairLayer => this.Layer(this.Place(Child(node), input), shape.Kind),
            HirPlaceKind.Index => this.Element((IndexKoto)node, input),
            HirPlaceKind.ConstIndex => this.ConstElement((IndexKoto)node, input),
            HirPlaceKind.CallResult => this.Referent(node),
            HirPlaceKind.Value => this.Value(node, input: input),
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
        => kind == HirPlaceKind.PairLayer ? this.body.Project(place, MirProjectionKind.PairLayer, MirCases.Binder(this.body, this.body.TypeOf(place)))
            : this.body.Project(place, MirProjectionKind.Deref, 0);

    // SPEC 3.4.3, 13.5.5: up to count of the layers its Type's selection follows implicitly, stopping at a Place of the target Type.
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
    // storage of the receiver's Property (an accessor's `storage`, SPEC 11.3).
    private MirPlace Named(Koto node)
    {
        var symbol = HirTables.Symbol(node);
        switch (HirTables.ResOf(node))
        {
            case HirRes.Local or HirRes.Param or HirRes.PreparedParam or HirRes.Capture when symbol is not null && this.Bound(symbol) is { } place:
                return place;
            case HirRes.Static when symbol is not null:
                return this.Static(symbol, node);
            case HirRes.Field: // A Property, or an accessor's storage of its Property.
                var property = symbol!.Kind == BindingSymbolKind.Storage ? HirTables.Symbol(symbol.Declaration!)! : symbol;
                return this.Storage(property, this.receiver < 0 ? null : new MirPlace(this.receiver), node);
            default:
                return this.Unsupported(node);
        }
    }

    // SPEC 11.2, 11.3, 11.8: a stored Property's storage, which its standard accessors and its accessors' `storage` read and write: a static
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
    // Closure's body of an environment binding: its capture's Field of the environment the receiver reaches (SPEC 7.7.2); null for another
    // symbol.
    private MirPlace? Bound(BindingSymbol symbol)
    {
        if (this.locals.TryGetValue(symbol, out var local))
        {
            var prepared = this.body.Locals[local].Role == MirLocalRole.PreparedInput;
            this.prepared |= prepared ? 1UL << Math.Min(local - 1, 63) : 0;
            return prepared ? this.body.Project(new(local), MirProjectionKind.Deref, 0) : new(local);
        }

        var captures = this.receiver > 0 && this.body.Key.Kind == MirBodyKind.Closure ? ((FunctionKoto)this.declaration).ClosureDefinition?.Captures : null;
        var index = (captures?.Count ?? 0) - 1;
        while (index >= 0 && !ReferenceEquals(captures![index].Environment, symbol))
        {
            index--;
        }

        return index < 0 ? null : this.body.Project(this.Follow(new(this.receiver), 16), MirProjectionKind.Field, index);
    }

    // SPEC 22.4.3, plan 5.6.6: a static's Place, after the check of its state that initializes it at its first access, staticEnsure of its
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
        return this.body.Static(new(symbol, type, Binding.Reassignable(symbol), MirTypes.HoldsRaw(type, this.walked)));
    }

    // SPEC 6.2.2, 3.2: a stored Field or a Tuple element of the receiver's Place, which the receiver's Layers follow; a Tuple element at
    // the position its own plan row keeps (Folded).
    private MirPlace Member(MemberAccessKoto member, bool input)
    {
        var receiver = this.Place(member.Left, input);
        return HirTables.PlanOf(member).Folded is { } element
            ? this.body.Project(receiver, MirProjectionKind.Field, (int)element) : this.Field(receiver, HirTables.Symbol(member), member);
    }

    // The stored Field of a symbol in a struct Place, in the layer that stores it (AdtDef.FieldOwner), or the value derived from the error
    // of one that has no Place (Stored).
    private MirPlace Field(MirPlace receiver, BindingSymbol? field, Koto node)
    {
        var type = this.body.TypeOf(receiver);
        var owner = type is null ? null : AdtDef.FieldOwner(type, field);
        return owner is { } found && this.Stored(receiver, found.Layer, found.Depth, found.Index) is { } place
            ? place : this.Unsupported(node, owner is not null);
    }

    // The Place of the stored Field at an own index of the layer `depth` Base steps up from a struct Place (the receiver's
    // StorageProjection); none for a Field Binding formed no stored Type for, a value derived from that error (SPEC 23.1.2).
    private MirPlace? Stored(MirPlace receiver, BoundType layer, int depth, int index)
        => AdtDef.FieldType(layer, index) is null ? null : this.body.Project(
            depth == 0 ? receiver : this.body.Project(receiver, MirProjectionKind.Base, depth),
            MirProjectionKind.Field,
            index);

    // SPEC 4.7.1: the referent of the reference that a subscript's selected call returns, which the selection's published expansion names.
    private MirPlace Referent(Koto node)
    {
        var call = ExpansionPlan.Of(node)!.Value.Second!;
        var result = this.Hold(call.ReturnType, node);
        this.Call(result, call, node, first: null);
        return this.body.Project(this.Made(result), MirProjectionKind.Deref, 0);
    }

    // A value held in a temp of its Type where its consumer needs a Place, or in the place given where that has its Type, unless an expansion
    // holds it already; a call's input as the call's (input). One Binding gave no Type, a construct it left unresolved or a derived value,
    // is held at the Type its consumer expects (expected), as Unsupported holds a derived one: SPEC 14.9.2 makes a construct's Type the
    // fixed expectation, which Binding does not publish for one it left unresolved (DECISIONS R4-M88; PLAN G103 R1). An Error node's value is
    // held there also where Binding gave the node a Type, and it is never taken from where an expansion located the node: each consumer
    // receives its own derived value, whose site names the located root (SiteRoots; DECISIONS R4-M109). The temp an Error node's value is
    // held in is an Opaque temp, which nothing lends (Unlent; DECISIONS R4-M149).
    private MirPlace Value(Koto node, MirPlace? into = null, bool input = false, BoundType? expected = null)
    {
        var derived = HirTables.Op(node) == HirOp.Error;
        if (!derived && this.Located(node) is { } located)
        {
            return located;
        }

        var type = (derived ? expected : null) ?? HirTables.Meaning(node) as BoundType ?? expected ?? BoundType.Unit;
        if (this.Given(into, type) is { } place)
        {
            this.Into(place, node, input);
            return place;
        }

        var temp = this.Hold(type, node);
        if (derived)
        {
            this.opaqueTemps.Add(temp.Root);
        }

        this.Into(temp, node, input);
        return this.Made(temp);
    }
}
