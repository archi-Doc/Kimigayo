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
            _ when node is ParenthesizedKoto group && shape.Kind != HirPlaceKind.Value => this.Place(group.Operand, input),
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

        var captures = this.receiver > 0 && this.declaration is FunctionKoto { IsAnonymous: true } closure ? BoundClosure.Of(closure)?.Captures : null;
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
        return this.body.Static(new(symbol, type, symbol.Declaration is PropertyKoto { VariableKind: VariableKind.Var }, MirTypes.HoldsRaw(type, this.walked)));
    }

    // SPEC 6.2.2, 3.2: a stored Field or a Tuple element of the receiver's Place, which the receiver's Layers follow.
    private MirPlace Member(MemberAccessKoto member, bool input)
    {
        var receiver = this.Place(member.Left, input);
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

    // SPEC 15.1.3: a fixed-array element at a literal position in range (its PlaceShape): at the position the index's own plan row keeps
    // (Folded); the position evaluates nothing.
    private MirPlace ConstElement(IndexKoto index, bool input)
        => this.body.Project(this.Place(index.Left, input), MirProjectionKind.ConstIndex, (int)HirTables.PlanOf(index).Folded!.Value);

    // SPEC 4.7, 4.7.1: a built-in element at a runtime position: an integer, at a written ^x the length minus x, or at another key's
    // resolution; SPEC 5.3: a raw pointer's p[n] is *(p + n). SPEC 4.7.4: the receiver is located once, before the index: a handle the
    // selection reads by value is secured there where the index builds code (Handle), and storage is lent there by a guard, a shared
    // borrow ended once the index is evaluated, before the element's own access is formed. The guard is the receiver's check at the
    // selection (DECISIONS R4-M56), and the position local is located there, so A1 reports an access through the Index under the same
    // subject: one problem unless the receiver changed in between.
    private MirPlace Element(IndexKoto index, bool input)
    {
        var sequence = this.Place(index.Left, input);
        sequence = Builds(index.Right) ? this.Handle(sequence, index.Left) : sequence;
        var guard = MirTypes.SelectsByValue(this.body.TypeOf(sequence)) ? (MirPlace?)null : this.Borrow(SemanticsKind.Ref, sequence, index).Place;
        var key = ExpansionPlan.Of(index)?.First;
        MirPlace position;
        if (key is not null || HirTables.Op(index.Right) == HirOp.Range)
        {
            position = key is null ? this.Boundary(index.Right, this.Length(sequence, index), index) : this.Resolve(key, index, sequence);
        }
        else if (this.body.TypeOf(sequence) is { Semantics: SemanticsKind.Raw } pointer)
        {
            var count = this.Operand(index.Right, expected: null);
            var offset = this.Temp(pointer, index);
            var start = this.body.AddOperand(MirOperand.Copy(sequence));
            this.body.AddOperand(count);
            this.Set(offset, MirRvalue.Binary(MirBinaryOp.Add, start, proven: true), index);
            return this.body.Project(offset, MirProjectionKind.Deref, 0);
        }
        else
        {
            position = this.Position(this.Operand(index.Right, expected: null), index.Right, index);
        }

        if (guard is { } lent)
        {
            this.body.Push(this.current, MirStatement.Of(MirStatementKind.EndBorrow, lent, this.Info(index)));
        }

        return this.body.Project(sequence, MirProjectionKind.Index, position.Root);
    }

    // SPEC 4.7.4, 12.2: a selection's receiver whose value it reads, a Slice handle or a raw pointer, secured where it is located (before an
    // index or a boundary that builds code), so that the selection reads the value it located; another receiver is its storage.
    private MirPlace Handle(MirPlace sequence, Koto node) => MirTypes.SelectsByValue(this.body.TypeOf(sequence))
        ? this.Secure(MirOperand.Copy(sequence), node).Place : sequence;

    // SPEC 4.6.1: a position as the isize a selection reads, its value compared mathematically: a Type no wider than isize wraps (a same-width
    // unsigned value isize cannot hold reads as negative, which the bounds check rejects), a wider one converts checked; in a local located
    // at its selection, whose subject A1 reports an access through its Index under (DECISIONS R4-M56).
    private MirPlace Position(in MirOperand position, Koto node, Koto selection)
    {
        var type = this.TypeOf(position) ?? BoundType.ISize;
        var local = this.Temp(BoundType.ISize, selection);
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

    // SPEC 4.7.1: the referent of the reference that a subscript's selected call returns, which the selection's published expansion names.
    private MirPlace Referent(Koto node)
    {
        var call = ExpansionPlan.Of(node)!.Value.Second!;
        var result = this.Temp(call.ReturnType, node);
        this.Call(result, call, node, first: null);
        return this.body.Project(result, MirProjectionKind.Deref, 0);
    }

    // A value held in a temp where its consumer needs a Place, or in the place given where that has its Type, unless an expansion holds it
    // already; a call's input as the call's (input).
    private MirPlace Value(Koto node, MirPlace? into = null, bool input = false)
    {
        if (this.Located(node) is { } located)
        {
            return located;
        }

        var temp = this.Temp(TypeOf(node), node, into);
        this.Into(temp, node, input);
        return temp;
    }
}
