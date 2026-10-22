// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Raw Origins, written forms (SPEC 5.7 Origins; DECISIONS BU-24; contract in Binding.RawOrigins.cs): the Origin a raw layer's `during`
// writes (WrittenRawOrigin), the slot a written projection names (WrittenProjection) and their one judgment (InvalidRawOrigin), made once
// for every position after the last requirement fixed point (JudgeRawOrigin, from ValidateOriginRequirements).
public sealed partial class Binding
{
    // SPEC 5.7: the Phantom slot a raw layer states, recorded only when the check fails.
    private Dictionary<Koto, OriginParameter>? phantomRawOrigins;

    // SPEC 5.7 Origins (D17, D18; design-final §5; DECISIONS BU-24): the Origin a raw layer's `during` writes, one atom (never a meet or
    // Origin arguments), the atom its name binds whatever position it is written in: `self` names the holding value (Holder) on a layer
    // in inline storage (a Field's or Case payload's Type or a Tuple or fixed-array element of one) and nothing elsewhere in storage; a
    // borrow's name its outer Origin (BindOriginName accepts no other carrier), a slot projection `x.s` the slot's binding, a declared
    // slot, a universal name or `static` itself. JudgeRawOrigin judges it after the last fixed point. Null when the annotation failed.
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
        return origin?.Kind == OriginKind.Intersection ? this.FailOrigin(use) : origin;

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

    // The slot a written projection `x.s` names (SPEC 5.7 Origins: it belongs to that slot's keepers); null for any other written form.
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

    private BoundOrigin? FailOrigin(Koto use)
    {
        this.Fail(use, BindingFailure.InvalidOrigin);
        return null;
    }

    // SPEC 5.7 (DECISIONS BU-24; the re-read's R2 (a)): the one judgment of a written raw Origin, made after the last requirement fixed
    // point for every position (ValidateOriginRequirements), from the written form: the slot it names, through a projection `x.s` too
    // (WrittenProjection over the annotation's scope), whatever binds that slot.
    private void JudgeRawOrigin(TypeSemanticsKoto annotation, BoundOrigin raw)
        => this.InvalidRawOrigin(annotation, raw, this.WrittenProjection(annotation, annotation, this.ConstraintScope(annotation)));

    // SPEC 5.7 (design-final §5, U1): a written raw Origin belongs to a borrow. The slot it names (a projection's, `projected`; an Input
    // atom's target; a declared slot) must not be Phantom (InvalidOrigin with the slot), and a universal name or an input's outer Origin
    // must belong to some borrow among its binder's inputs (RawMembers), else it is InvalidOrigin, or a located Unsupported_Kd when the
    // input whose Origin it is is a pair layer admitting a non-borrow case (U1). A rejected Origin stays published: no step or reader
    // needs it valid. Whether `node` failed.
    private bool InvalidRawOrigin(Koto node, BoundOrigin raw, OriginParameter? projected)
    {
        if ((projected ?? NamedSlot(raw)) is { LoanRequirement: LoanRequirement.None } phantom)
        {
            this.FailExplained(ref this.phantomRawOrigins, node, BindingFailure.InvalidOrigin, phantom);
            return true;
        }

        if (raw is not { Kind: OriginKind.Input or OriginKind.Parameter, Binder: FunctionKoto or PropertyAccessorKoto or FunctionTypeKoto } ||
            NamedSlot(raw) is not null || this.HasMember(raw, raw.Binder))
        {
            return false;
        }

        var pair = raw is { Kind: OriginKind.Parameter, Slot: < 0 };
        for (var i = 0; i < InputCount(raw.Binder) && !pair; i++)
        {
            pair = this.BoundInputType(raw.Binder, i) is { } input && TryPairLayer(input, out _, out _) && ReferenceEquals(this.OuterOrigin(input), raw);
        }

        this.Fail(node, pair ? BindingFailure.Unsupported : BindingFailure.InvalidOrigin);
        return true;
    }

    // Whether `origin` belongs to some borrow among `binder`'s inputs (RawMembers).
    private bool HasMember(BoundOrigin origin, Koto binder)
    {
        var count = InputCount(binder);
        var inputs = this.RentTypes(count);
        try
        {
            var known = 0;
            for (var i = 0; i < count; i++)
            {
                if (this.BoundInputType(binder, i) is { } input)
                {
                    inputs[known++] = input;
                }
            }

            // An incomplete walk reached a slot the Origin binds, which a keeper cycle follows: it belongs to that slot's borrows.
            var start = this.rawMembers.Count;
            var member = !this.RawMembers(origin, inputs.AsSpan(0, known), this.rawMembers) || this.rawMembers.Count > start;
            this.rawMembers.RemoveRange(start, this.rawMembers.Count - start);
            return member;
        }
        finally
        {
            this.typeScratch.Return(inputs, clearArray: true);
        }
    }
}
