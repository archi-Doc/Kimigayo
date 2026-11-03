// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Raw Origins, `during self` layers (SPEC 5.7 Reading and Writing a Field; DECISIONS BU-24, R4-M105; contract in Binding.RawOrigins.cs):
// a Type holding one in inline storage (HoldsHolder), the layer read at a Place with the Origin its path is reached through
// (ReadHolder, ReplaceHolder), and the allowlist of its Place reached through no dereference (HolderPlaceFailure, HolderWriteFailure).
public sealed partial class Binding
{
    // Whether a Type holds a `during self` layer in its inline storage: the layer, or a Tuple or fixed array with a component holding one
    // (its Origin summary's bit, Binding.OriginSummaries).
    private bool HoldsHolder(BoundType type) => this.SummaryOf(type).HoldsHolder;

    // SPEC 5.1, 5.7 (R4-M105, OQ-R1 (a)): what a write of `source` puts in a `during self` layer that `stored` is or holds inline, other
    // than the null literal: `source` itself at the layer, or the first such element at a layer's position of a Tuple or array literal,
    // recursively; any other source reaches the layers as a whole. Null when every layer receives null.
    private Koto? NonNullAtLayers(BoundType stored, Koto source)
    {
        source = KotoHelper.UnwrapParentheses(source);
        if (stored.RawOrigin?.Kind == OriginKind.Holder)
        {
            return source is NullLiteralKoto ? null : source;
        }

        var elements = (stored.Kind, source) switch
        {
            (BoundTypeKind.Tuple, TupleLiteralKoto tuple) => tuple.Elements,
            (BoundTypeKind.FixedArray, ArrayLiteralKoto array) => array.Elements,
            _ => null,
        };
        for (var i = 0; elements is not null && i < elements.Count; i++)
        {
            var part = stored.Components[stored.Kind == BoundTypeKind.FixedArray ? 0 : i];
            if (this.HoldsHolder(part) && this.NonNullAtLayers(part, elements[i]) is { } written)
            {
                return written;
            }
        }

        return elements is null ? source : null;
    }

    // SPEC 5.7, Reading a Field: a `during self` layer read at `place` (a Field, or a Subject a payload is selected from in place)
    // states the Origin of the last borrow or raw pointer the path dereferences (PathOrigin), the special receiver's in a constructor or
    // drop, or none through no dereference; null after a located Unsupported_Kd at `node` (U2).
    private BoundType? ReadHolder(BoundType type, Koto place, Koto node)
    {
        if (!type.CarriesRawOrigin || !this.HoldsHolder(type))
        {
            return type;
        }

        var path = this.PathOrigin(place);
        if (path.Dereferences is null)
        {
            this.Fail(node, BindingFailure.Unsupported);
            return null;
        }

        return this.ReplaceHolder(type, path.Origin);
    }

    // A part holding no `during self` layer is its own image (HoldsHolder, its Origin summary's bit), so only the parts holding one are
    // rebuilt, each a Tuple or fixed array a declaration writes inline: no Type argument or projection states `during self` (SPEC 5.7;
    // PLAN G309).
    private BoundType ReplaceHolder(BoundType type, BoundOrigin? image)
    {
        if (type.RawOrigin?.Kind == OriginKind.Holder)
        {
            return this.types.Pointer(type.Components[0], image);
        }

        if (!this.HoldsHolder(type))
        {
            return type;
        }

        var components = this.RentTypes(type.Components.Count);
        try
        {
            for (var i = 0; i < type.Components.Count; i++)
            {
                components[i] = this.ReplaceHolder(type.Components[i], image);
            }

            return this.types.Rebuild(type, components.AsSpan(0, type.Components.Count), type.Origin, (BoundOrigin[])type.OriginArguments);
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
        }
    }

    // SPEC 5.7 (DECISIONS R4-M105, OQ-R1 (a); R4-M110): through no dereference, except through `self` in the constructors and drop of
    // the Type whose inline storage holds the layer, a `during self` layer's Place admits reads (a Move included), shared borrows and
    // writes of null only; an enclosing Tuple or fixed-array Place is written and borrowed as the layer's Place is. Whether `access` to
    // `place`, writing `source`, failed at `at` (HolderWriteFailure), for a write, an update, a written exclusive borrow, a receiver's
    // implicit one (JudgeCallFit) and a Move; PathOrigin publishes the stored Field and the Place's stored Type.
    private bool HolderPlaceFailure(Koto place, HolderAccess access, Koto? source, Koto at)
        => this.PathOrigin(place) is { Dereferences: false, Field: { } field, Stored: { } stored } && this.HoldsHolder(stored) && Receiver(field.Left) is null &&
            this.HolderWriteFailure(stored, access, source, at);

    // `access` to storage whose Type `stored` is, or encloses, a `during self` layer reached through no dereference. Admitted: a write
    // whose value puts null in every layer (NonNullAtLayers) and a Move of the layer's Place, a read whose restoring write is a write.
    // A Move of an enclosing Place is a located Unsupported_Kd (PLAN G212: SPEC does not say what it gives). Any other write, an update
    // and an exclusive borrow, of the layer's Place or an enclosing one, record an Origin relation failure at `at` and form no
    // relation: the holder's storage, which no Origin expression names, outlives no Origin. Its longer end is the Origin of the value
    // reaching the layer, none for an update or a borrow, which admit the Place's read Type.
    private bool HolderWriteFailure(BoundType stored, HolderAccess access, Koto? source, Koto at)
    {
        var written = access == HolderAccess.Write && source is not null ? this.NonNullAtLayers(stored, source) : null;
        if ((access == HolderAccess.Write && written is null) || (access == HolderAccess.Move && stored.Kind == BoundTypeKind.Semantics))
        {
            return false; // A Semantics `stored` is the layer itself; an enclosing one is a Tuple or fixed array (HoldsHolder).
        }

        if (access == HolderAccess.Move)
        {
            this.Fail(at, BindingFailure.Unsupported);
            return true;
        }

        var longer = written?.BoundType?.RawOrigin ?? BoundOrigin.Unstated;
        var relation = new OriginRelationFact(at, longer, this.SuppliedAtUse(longer, at), BoundOrigin.Holder, Equality: false, stored, Refuted: true);
        this.FailExplained(ref this.originRelations, at, BindingFailure.OriginRelation, relation);
        return true;
    }

    // The accesses of a `during self` layer's Place the allowlist decides (HolderPlaceFailure).
    private enum HolderAccess : byte
    {
        Write,
        Update,
        ExclusiveBorrow,
        Move,
    }
}
