// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Raw Origins, written forms (SPEC 5.7 Origins; DECISIONS BU-24; contract in Binding.RawOrigins.cs): the Origin a raw layer's `during`
// writes (WrittenRawOrigin), the slot a written projection names (WrittenProjection) and their one judgment (InvalidRawOrigin) where the
// slots' requirements exist: a declaration's right after the first fixed point (RejectRawOrigins, which publishes a rejected layer
// unstated: Unpublish), a body's at formation.
public sealed partial class Binding
{
    // SPEC 5.7: the Phantom slot a raw layer states, recorded only when the check fails.
    private Dictionary<Koto, OriginParameter>? phantomRawOrigins;

    // The pass (storageVersion) whose declarations' written raw Origins RejectRawOrigins judged; later formations are judged at once.
    private ulong rawJudgmentVersion;

    // SPEC 5.7 Origins, Authority (D17, D18; design-final §5; DECISIONS BU-24): the Origin a raw layer's `during` writes, one atom (never
    // a meet or Origin arguments), formed with the step its written form gives, whatever kind of atom the name binds: `self` names the
    // holding value (Holder) on a layer in inline storage (a Field's or Case payload's Type or a Tuple or fixed-array element of one)
    // and nothing elsewhere in storage; a borrow's name steps by that borrow's mode (BindOriginName accepts no other carrier); a slot
    // projection `x.s` by the projected slot's requirement (WrittenProjection); a declared slot, a universal name or `static` by its own
    // (StepOf). It is judged by InvalidRawOrigin where the slots' requirements exist: a declaration's by RejectRawOrigins after the
    // first fixed point (its projection binds an Input atom naming the slot, which StepOf reads), a body's here, a rejected one
    // published unstated (Unstated, which the caller forms as none). Null when the annotation failed.
    private BoundOrigin? WrittenRawOrigin(TypeSemanticsKoto annotation, Koto use, BindingScope scope, TypePosition position)
    {
        var name = annotation.OriginExpression is null ? annotation.OriginName : (annotation.OriginExpression as IdentifierNameKoto)?.IdentifierName;
        if (name == "self" && (position == TypePosition.InstanceStorage || OriginOwner(annotation) is { Akind: KotoKind.EnumCase }))
        {
            if (!InlineStorage(annotation))
            {
                return this.FailOrigin(use);
            }

            if (annotation.OriginExpression is { } self)
            {
                self.BindingState = BindingState.Resolved;
                self.BoundOrigin = BoundOrigin.Holder;
            }

            return BoundOrigin.Holder;
        }

        if (annotation.OriginArguments is not null)
        {
            return this.FailOrigin(use);
        }

        var origin = annotation.OriginExpression is { } expression ? this.BindOrigin(expression, scope) : this.BindOriginName(name!, use, scope);
        if (origin is null || origin.Kind == OriginKind.Intersection)
        {
            return origin is null ? null : this.FailOrigin(use);
        }

        var mode = name is not null && this.OriginCandidate(name, use, scope, out _, out var carrier, out _) ? this.LayerStep(carrier) : null;
        if (this.rawJudgmentVersion != this.storageVersion)
        {
            return mode is { } borrowed ? this.Formed(origin, borrowed) : origin;
        }

        var projected = mode is null ? this.WrittenProjection(annotation, use, scope) : null;
        var step = mode ?? (projected is null ? this.StepOf(origin) : RequirementStep(projected.LoanRequirement));
        var raw = step is { } formed ? this.Formed(origin, formed) : origin;
        if (this.InvalidRawOrigin(annotation, raw, projected))
        {
            return BoundOrigin.Unstated;
        }

        // Formed's invariant, on which StepOf's Exclusive labels rest: the published Origin reads as the step its form gives.
        return this.StepOf(raw) == step ? raw
            : throw new InvalidOperationException($"The written raw Origin `{raw}` does not read as {step} (InternalInvariant).");

        static bool InlineStorage(Koto node)
        {
            while (node.Parent is ParenthesizedTypeKoto or TupleTypeKoto ||
                (node.Parent is FixedArrayTypeKoto array && ReferenceEquals(array.ElementType, node)))
            {
                node = node.Parent;
            }

            var field = node.Parent is VariableKoto variable && ReferenceEquals(variable.TypeKoto, node);
            return node.Parent is SyntaxFormKoto { Akind: KotoKind.EnumCase } || field;
        }
    }

    // The slot a written projection `x.s` names (SPEC 5.7 Origins: it belongs to that slot's keepers), the one whose requirement steps
    // it; null for any other written form.
    private OriginParameter? WrittenProjection(TypeSemanticsKoto annotation, Koto use, BindingScope scope)
    {
        if (annotation.OriginExpression is not MemberAccessKoto { Left: IdentifierNameKoto input, Right: IdentifierNameKoto target } ||
            !this.OriginCandidate(input.IdentifierName, use, scope, out _, out var type, out _))
        {
            return null;
        }

        var slot = ProjectedSlot(ref type, target.IdentifierName);
        return slot < 0 ? null : type!.Symbol!.Schema!.Origins[slot];
    }

    // SPEC 5.7 (design-final §7.3): right after the slots' requirements exist, a declaration's written raw Origin that StepOf cannot
    // classify (a Phantom slot, U1's case-dependent pair input, an atom no borrow holds) is rejected at its annotation (InvalidRawOrigin)
    // and never published: its layer is unstated in every Type written around it, up to the declared Type its symbol holds (Unpublish),
    // so no substitution or dereference re-steps it. From then on in the pass a written raw Origin is judged where it is formed
    // (WrittenRawOrigin), and no requirement may change (AccumulateOrigin).
    private void RejectRawOrigins()
    {
        for (var i = 0; i < this.nodes.Count; i++)
        {
            if (this.nodes[i] is TypeSemanticsKoto { BoundType: { RawOrigin: { } raw } layer } annotation && this.InvalidRawOrigin(annotation, raw, null))
            {
                this.Unpublish(annotation, layer, this.types.Pointer(layer.Components[0]));
            }
        }

        this.rawJudgmentVersion = this.storageVersion;
        this.RejectPolymorphicKeeperCycles();
    }

    // Replaces `from`, the Type of `node`, with `to` there and in each Type written around it (TyCtxt.Rebuild), up to the declaration
    // (a Field, parameter, function result or Case payload) whose symbol holds the declared Type.
    private void Unpublish(Koto node, BoundType from, BoundType to)
    {
        for (Koto? at = node; at is not null; at = at.Parent)
        {
            if (at.BoundSymbol is { } symbol && ReferenceEquals(symbol.Declaration, at) && ReferenceEquals(symbol.Type, from))
            {
                symbol.Type = to;
            }

            if (ReferenceEquals(at.BoundType, from))
            {
                at.BoundType = to;
            }
            else if (at.BoundType is { } enclosing && this.Replaced(enclosing, from, to) is { } rebuilt)
            {
                at.BoundType = rebuilt;
                (from, to) = (enclosing, rebuilt);
            }

            if (at is not (TypeKoto or GenericsKoto))
            {
                return;
            }
        }
    }

    // `type` with each component that is `from` replaced by `to` (TyCtxt.Rebuild); null when none is.
    private BoundType? Replaced(BoundType type, BoundType from, BoundType to)
    {
        var count = type.Components.Count;
        if (!type.Components.Contains(from))
        {
            return null;
        }

        var components = this.RentTypes(count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                components[i] = ReferenceEquals(type.Components[i], from) ? to : type.Components[i];
            }

            return this.types.Rebuild(type, components.AsSpan(0, count), type.Origin, (BoundOrigin[])type.OriginArguments);
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
        }
    }

    private BoundOrigin? FailOrigin(Koto use)
    {
        this.Fail(use, BindingFailure.InvalidOrigin);
        return null;
    }

    // SPEC 5.7 (design-final §5, U1): once the requirement fixed point is complete, a written raw Origin belongs to a borrow. The slot
    // whose requirement steps it (a projection's, `projected` when its atom does not name it; an Input atom's target; a declared slot)
    // must not be Phantom (InvalidOrigin with the slot); an Origin without a step, one that no input borrow or slot holds, is invalid,
    // and one whose admitted pair cases mix modes is a located Unsupported_Kd. Whether `node` failed.
    private bool InvalidRawOrigin(Koto node, BoundOrigin raw, OriginParameter? projected)
    {
        if ((projected ?? SteppingSlot(RegionOf(raw))) is { LoanRequirement: LoanRequirement.None } phantom)
        {
            this.FailExplained(ref this.phantomRawOrigins, node, BindingFailure.InvalidOrigin, phantom);
            return true;
        }

        if (this.StepOf(raw) is not null)
        {
            return false;
        }

        var pair = raw is { Kind: OriginKind.Parameter, Slot: < 0 };
        for (var i = 0; raw.Binder is { } binder && i < InputCount(binder) && !pair; i++)
        {
            pair = this.BoundInputType(binder, i) is { } input && TryPairLayer(input, out _, out _) && ReferenceEquals(this.OuterOrigin(input), raw);
        }

        this.Fail(node, pair ? BindingFailure.Unsupported : BindingFailure.InvalidOrigin);
        return true;
    }
}
