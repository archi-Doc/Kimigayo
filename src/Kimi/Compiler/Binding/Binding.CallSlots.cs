// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Call slots (SPEC 10.8, 8.1.2): a callee's structural slots and their inference.
// - Responsibility: the declaration owning a callee's slots (CallSlotOwner: a function its own, a constructor its structure's, an enum
//   constructing a Case its own), their count and the binder's Origin count; a member pattern as a call reads it (CallMemberPattern); a
//   candidate's slot bindings fixed before any evidence (FixCallSlots, FixedSlots: a function's explicit Type arguments, a constructor's
//   fixed enclosing slots and fixed own ones) and a constructor's constructed Type (ConstructionType); the structural matching of a
//   pattern over the owner's slots against an actual Type, binding each slot it reaches (Infer), and an s/U whose s is bound, matched as
//   the Type it forms (FormedApplication).
// - Inputs: the callee's declaration and declaring Type; a pattern, the actual Type at its position and one solution's slot and length
//   arrays (a candidate's SlotSolution, a Case construction's, a function reference's or a Callable proof's), with the evidence flags.
// - Outputs: the bindings in those arrays (null while unbound) and whether the actual matches the pattern structurally.
// - Invariants: a slot's earlier binding is kept unless the flags say how two bindings meet (SPEC 10.8): an invariant binding over
//   covariant ones, covariant ones by their common Type; under `relateLater` an Origin never fails a structurally equal binding.
// - Prohibitions: no Origin relation is judged and no applicability decided here (SPEC 15.6.1): the callers judge the solution.
public sealed partial class Binding
{
    // The declaration owning structural inference is independent of the binder owning the Origins: a function owns its slots and a
    // constructor's structure owns them; an enum constructing a Case owns its own (SPEC 10.8.1).
    private static Koto CallSlotOwner(Koto binder)
        => binder is FunctionKoto { IsConstructor: true } constructor ? constructor.BoundSymbol!.Scope.Owner : binder;

    private static IReadOnlyList<TypeKoto> CallOwnSlots(FunctionKoto function)
        => function.IsConstructor ? ((StructKoto)CallSlotOwner(function)).GenericParameterNodes : function.GenericArguments;

    private static int CallSlotCount(Koto binder)
        => binder is FunctionKoto { IsConstructor: false } function
            ? function.GenericArguments.Count : CallSlotOwner(binder).BoundSymbol!.Schema!.GenericSlots.Count;

    // The Origin slots a binder binds: a function's, or a declaration's.
    private static int CallOriginCount(Koto binder) => binder is FunctionKoto function ? function.Origins.Count : binder.BoundSymbol!.Schema!.Origins.Count;

    // SPEC 10.8.1: the slots a constructor's declaring Type fixes: its components, an open own slot as its own parameter; also read before
    // its candidate's frame is seeded (ConstructorType).
    private static FixedSlots ConstructionSlots(FunctionKoto constructor, BoundType declaringType, bool inferred)
    {
        var ownFixed = true;
        for (var i = 0; i < CallOwnSlots(constructor).Count; i++)
        {
            ownFixed &= !OpenConstructionSlot(constructor, declaringType, inferred, i);
        }

        return new(CallSlotOwner(constructor), (BoundType[])declaringType.Components, default, ownFixed);
    }

    // An inferred construction leaves open each own slot that its declaring Type gives as its own parameter; an outer slot is always a fixed
    // binding, even when it is itself a parameter.
    private static bool OpenConstructionSlot(FunctionKoto constructor, BoundType declaringType, bool inferred, int slot)
        => inferred && slot < CallOwnSlots(constructor).Count && declaringType.Components[slot] is { Kind: BoundTypeKind.Parameter } open &&
            ReferenceEquals(open.Symbol?.Scope.Owner, CallSlotOwner(constructor));

    // Constructor slots already include their fixed enclosing environment. Apply structural
    // bindings once, to declaration patterns; never substitute inside an already-bound argument.
    private BoundType? CallMemberPattern(BoundType type, Koto binder, BoundType? declaringType)
        => !ReferenceEquals(CallSlotOwner(binder), binder) && declaringType is not null
            ? this.SubstituteStoredOrigins(type, CallSlotOwner(binder), (BoundOrigin[])declaringType.OriginArguments)
            : this.MemberType(type, declaringType);

    private BoundType? ConstructionType(FunctionKoto function, BoundType template, ReadOnlySpan<BoundType?> slots)
    {
        var count = CallSlotCount(function);
        for (var i = 0; i < count; i++)
        {
            if (slots[i] is null)
            {
                return null;
            }
        }

        return this.types.Rebuild(template, slots[..count]!, template.Origin, (BoundOrigin[])template.OriginArguments);
    }

    // SPEC 10.8, 10.8.1, 8.1.3: seeds a candidate's frame with the slot bindings fixed before any evidence and returns them (FixedSlots):
    // a function's explicit Type arguments and lengths, which supply every own slot, or a constructor's declaring Type, whose enclosing
    // slots are always fixed and whose own slots are open only where an inferred construction leaves them its own parameters.
    private FixedSlots FixCallSlots(
        InvocationKoto call,
        FunctionKoto function,
        BoundType? declaringType,
        ReadOnlySpan<BoundType?> explicitTypes,
        ReadOnlySpan<BoundLength?> explicitLengths,
        SlotFrame slots,
        bool fixedConstruction = false)
    {
        var count = CallSlotCount(function);
        slots.ClearTypes(count);
        slots.ClearLengths(count);
        if (!function.IsConstructor)
        {
            for (var i = 0; i < Math.Min(count, explicitTypes.Length); i++)
            {
                if (explicitLengths[i] is { } length)
                {
                    slots.SetLength(i, length);
                }
                else
                {
                    slots.SetType(i, explicitTypes[i]);
                }
            }

            return new(function, explicitTypes, explicitLengths, !explicitTypes.IsEmpty);
        }

        if (declaringType is null)
        {
            return default;
        }

        var inferred = !fixedConstruction && this.InfersConstruction(call);
        for (var i = 0; i < count; i++)
        {
            if (!OpenConstructionSlot(function, declaringType, inferred, i))
            {
                slots.SetType(i, declaringType.Components[i]);
            }
        }

        return ConstructionSlots(function, declaringType, inferred);
    }

    // Whether a constructor call infers its construction's Type (SPEC 10.8.1): its qualifier names the Type without its arguments.
    private bool InfersConstruction(InvocationKoto call)
        => call.Method is MemberAccessKoto member && this.inferredConstructorTargets.Contains(member.Left);

    // SPEC 8.1.2: an s/U whose s is already inferred is matched as the Type it forms: U for owner, one reference layer with
    // the occurrence's outer-Origin slot for ref or uniq. An application whose s is still open is returned as it is.
    private BoundType FormedApplication(BoundType pattern, Koto function, ReadOnlySpan<BoundType?> arguments)
    {
        for (var depth = 0; depth < 16; depth++)
        {
            if (pattern is not { Kind: BoundTypeKind.SemanticsApplication, Symbol: { } selector, Components: [var target] } ||
                ContainerSlot(function, selector) is not (>= 0 and var slot) || slot >= arguments.Length || arguments[slot] is not { } whole)
            {
                break;
            }

            if (whole.Semantics == SemanticsKind.Owner)
            {
                pattern = target;
            }
            else if (whole.Semantics is SemanticsKind.Ref or SemanticsKind.Uniq)
            {
                pattern = this.types.Intern(BoundTypeKind.Semantics, null, whole.Semantics, [target], origin: pattern.Origin);
                break;
            }
            else
            {
                break;
            }
        }

        return pattern;
    }

    // With `evidence`, the actual is a known call signature (SPEC 10.5, 10.8): an argument's, or the parts an anonymous header writes.
    // Its structure and Semantics are evidence, and its Origins are not compared: applicability judges no Origin part (SPEC 15.6.1),
    // and the argument fits the substituted parameter after selection. An Origin the signature itself quantifies, such as a per-call
    // input, lies beyond the call and never solves an Origin in a slot: it becomes an open region that other evidence fills.
    // With `relateLater`, the actual is a value's Type whose Origin relations to the solution are judged after selection (SPEC 15.6.1),
    // so Origin evidence never fails a structurally equal slot binding; `invariant` marks a position below an exclusive layer or an
    // invariant schema slot.
    private bool Infer(BoundType pattern, BoundType actual, Koto function, SlotFrame frame, bool inferOrigins = false, bool lengths = false, bool commonOrigins = false, bool structural = false, SignatureEvidence? evidence = null, bool relateLater = false, bool invariant = false)
    {
        var arguments = frame.Types;
        if (pattern.Kind == BoundTypeKind.AssociatedProjection)
        {
            // An associated Type is not an injective constructor: infer its receiver from
            // other inputs, then check the substituted complete parameter in candidate fitting.
            return true;
        }

        if (pattern.Kind == BoundTypeKind.TargetProjection && pattern.Symbol is { } projected &&
            ContainerSlot(function, projected) is >= 0 and var projectionSlot)
        {
            // The target alone does not determine a pair's Semantics. Once another input, an explicit argument or the
            // result fixes the whole Type, match its direct target; final candidate fitting checks a still-open projection.
            return projectionSlot < arguments.Length && (arguments[projectionSlot] is not { } whole ||
                this.Infer(this.DirectTarget(whole), actual, function, frame, inferOrigins, lengths, commonOrigins, structural, evidence, relateLater, invariant));
        }

        if (pattern.Kind == BoundTypeKind.SemanticsApplication && pattern.Symbol is { } selector &&
            ContainerSlot(function, selector) is var selectorSlot && selectorSlot >= 0 && selectorSlot < arguments.Length)
        {
            if (arguments[selectorSlot] is not { } whole)
            {
                return true; // SPEC 10.2: solved together; the substituted parameter is checked once another argument fixes s/T.
            }

            if (whole.Semantics == SemanticsKind.Owner)
            {
                return this.Infer(pattern.Components[0], actual, function, frame, inferOrigins, lengths, commonOrigins, structural, evidence, relateLater, invariant);
            }

            return actual.Kind == BoundTypeKind.Semantics && actual.Semantics == whole.Semantics &&
                this.Infer(pattern.Components[0], actual.Components[0], function, frame, inferOrigins, lengths, commonOrigins, structural, evidence, relateLater, invariant || whole.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq or SemanticsKind.Raw);
        }

        // SPEC 3.1.1: the wrapping Scalar Wrapping<u8> is the instance of the pattern Wrapping<T>, so T is inferred from
        // the Scalar's integer argument; the substituted parameter then normalizes to the same Scalar.
        if (actual.IsWrappingInteger && pattern is { Kind: BoundTypeKind.Constructed, Components.Count: 1 } && pattern.Symbol?.LibraryDeclaration == KimiDeclarationId.Wrapping)
        {
            return this.Infer(pattern.Components[0], actual.Underlying, function, frame, inferOrigins, lengths, commonOrigins, structural, evidence, relateLater, invariant);
        }

        if (pattern.Kind == BoundTypeKind.Parameter && ContainerSlot(function, pattern.Symbol!) is var slot && slot >= 0)
        {
            if ((uint)slot >= (uint)arguments.Length)
            {
                return false;
            }

            if (HasArrayLengthHole(actual))
            {
                // An incomplete annotation cannot become a generic argument. Established argument evidence is checked
                // against it at the ordinary result fit; without such evidence the slot remains unsolved.
                return arguments[slot] is not { } establishedArray || FitsStructuralPart(establishedArray, actual);
            }

            // SPEC 10.2.2: a parameter constrained to Position, PositionRange or PrimitiveInteger binds the referent; the
            // argument is then value-read.
            if (!structural && actual is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } &&
                ComparisonReferent(actual) is var referent && !ReferenceEquals(referent, actual) && this.InfersReadReferent(pattern, this.activeInferenceFunction is { } inference && ReferenceEquals(CallSlotOwner(inference), function) ? inference : function))
            {
                actual = referent;
            }

            if (evidence is { Binder: FunctionKoto { ClosureDefinition: not null } closure } && this.HasEnvironmentOrigin(actual, closure))
            {
                // SPEC 8.10, 7.7.4: a result over the closure's hidden environment receiver satisfies no Callable signature and no
                // erasure, so that part is no evidence for the slot; the Callable proof or the conversion judges it against the rest.
                if (this.activeInferenceFunction is { } trial && ContainerSlot(CallSlotOwner(trial), pattern.Symbol!) is >= 0 and var own &&
                    own < this.environmentJudgments.Length)
                {
                    this.environmentJudgments[own] ??= new(PendingJudgmentKind.EnvironmentResult, evidence.Value.Source, pattern.Symbol);
                }

                return true;
            }

            if (evidence is { Binder: { } binder } known)
            {
                actual = this.OpenKnownOrigins(actual, binder, known.Source);
            }

            if (arguments[slot] is { } previous)
            {
                if (!ReferenceEquals(previous, actual) && (HasOpenOrigin(previous) || HasOpenOrigin(actual)))
                {
                    // SPEC 10.8: no argument fixes a slot first by traversal order. Another argument's Origin at an open position
                    // solves it, whichever comes first; two open positions keep the earlier region.
                    previous = this.FillOpenOrigins(previous, actual, false);
                    actual = this.FillOpenOrigins(actual, previous, true);
                }

                if (relateLater && !structural && !ReferenceEquals(previous, actual) &&
                    FitsStructuralPart(actual, previous) && FitsStructuralPart(previous, actual))
                {
                    // SPEC 10.8, 15.3.6: Origin evidence for a slot never makes the candidate inapplicable; each argument's relation to
                    // the solution is judged after selection (SPEC 15.6.1). An explicit Type argument is kept, an invariant binding is
                    // kept over covariant ones and the first invariant one over later ones, whatever the argument order, and covariant
                    // bindings meet.
                    if (!commonOrigins || this.invariantSlots.Contains(slot))
                    {
                        frame.SetType(slot, previous);
                    }
                    else if (invariant)
                    {
                        frame.SetType(slot, actual);
                        this.invariantSlots.Add(slot);
                    }
                    else
                    {
                        frame.SetType(slot, IsSubtype(actual, previous) ? previous : this.CommonOriginType(previous, actual, out _) ?? previous);
                    }

                    return true;
                }

                // SPEC 10.8: structural matching compares normalized Types, so two spellings of one Function Type, whose per-call
                // inputs have distinct binders, are one binding.
                if (ReferenceEquals(previous, actual) || (!structural && inferOrigins && IsSubtype(actual, previous)) ||
                    (structural && previous.Kind == BoundTypeKind.Function && actual.Kind == BoundTypeKind.Function &&
                    IsSubtype(actual, previous) && IsSubtype(previous, actual)))
                {
                    frame.SetType(slot, previous);
                    return true;
                }

                if (commonOrigins && this.CommonOriginType(previous, actual, out _) is { } common)
                {
                    frame.SetType(slot, common);
                    return true;
                }

                return false;
            }

            frame.SetType(slot, actual);
            if (relateLater && invariant)
            {
                this.invariantSlots.Add(slot);
            }

            return true;
        }

        if (ReferenceEquals(pattern, actual))
        {
            return true;
        }

        if (pattern.Kind != actual.Kind || pattern.Symbol != actual.Symbol || pattern.Semantics != actual.Semantics ||
            !(lengths && pattern.Kind == BoundTypeKind.FixedArray ? this.InferLength(pattern, actual, function, frame) : pattern.Length == actual.Length && ReferenceEquals(pattern.LengthExpression, actual.LengthExpression)) ||
            (!inferOrigins && evidence is null && !ReferenceEquals(pattern.Origin, actual.Origin)) || pattern.OriginArguments.Count != actual.OriginArguments.Count || pattern.Components.Count != actual.Components.Count || pattern.LengthArguments.Length != actual.LengthArguments.Length || (pattern.Components.Count == 0 && pattern.LengthArguments.Length == 0 && pattern.OriginArguments.Count == 0))
        {
            return false;
        }

        for (var i = 0; i < pattern.LengthArguments.Length; i++)
        {
            var required = pattern.LengthArguments[i];
            var supplied = actual.LengthArguments[i];
            if (!ReferenceEquals(required, supplied) && (required is null || supplied is null || !lengths || !this.InferLength(required, supplied, function, frame)))
            {
                return false;
            }
        }

        for (var i = 0; i < pattern.OriginArguments.Count; i++)
        {
            if (!inferOrigins && evidence is null && !ReferenceEquals(pattern.OriginArguments[i], actual.OriginArguments[i]))
            {
                return false;
            }
        }

        for (var i = 0; i < pattern.Components.Count; i++)
        {
            var below = invariant || IsInvariantLayer(pattern, this) ||
                (pattern.Kind == BoundTypeKind.Constructed && pattern.Symbol?.Schema is { } schema && i < schema.GenericSlots.Count && schema.GenericSlots[i].OriginVariance is OriginVariance.Invariant or OriginVariance.Unused);
            if (!this.Infer(
                pattern.Components[i],
                actual.Components[i],
                function,
                frame,
                inferOrigins,
                lengths,
                commonOrigins,
                structural,
                evidence,
                relateLater,
                below))
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 10.8, 10.8.1, 6.5.2 step 2 (DECISIONS: the fixed bindings): a solution's slot bindings fixed before any evidence, over the slot
    // owner's slots (FixCallSlots for a candidate, ConstructionSlots for a construction Type, a Case construction's seeding): each slot's
    // binding, an open slot as its own parameter (only an inferred construction leaves one beside fixed ones), and the lengths, null where
    // none is fixed; no Types when no slot is fixed. Own tells whether every own slot is fixed: evidence then keeps every binding (Infer's
    // commonOrigins off); otherwise the open slots' covariant bindings meet. Its spans live as long as the call's explicit Types, the
    // declaring Type or the Case's scratch.
    private readonly ref struct FixedSlots(Koto owner, ReadOnlySpan<BoundType?> types, ReadOnlySpan<BoundLength?> lengths, bool own)
    {
        internal Koto? Owner { get; } = owner;

        internal ReadOnlySpan<BoundType?> Types { get; } = types;

        internal ReadOnlySpan<BoundLength?> Lengths { get; } = lengths;

        internal bool Own { get; } = own;
    }

    // SPEC 10.2.2 step 1, 10.8, 6.5.2 step 2: the only input of the adaptation entry (AdaptInput): a pattern under the slot bindings fixed
    // before any evidence. Of substitutes a solution's FixedSlots (a slot still open stays a slot); Open states that its reader fixes no
    // slot: a completed parameter Type, or a conversion's, a property's, a receiver's or a value call's pattern, which holds none.
    private readonly struct FixedPattern
    {
        private FixedPattern(BoundType type) => this.Type = type;

        internal BoundType Type { get; }

        internal static FixedPattern Of(Binding binding, BoundType pattern, in FixedSlots fixedSlots)
            => new(fixedSlots.Types.IsEmpty ? pattern
                : binding.SubstituteType(pattern, fixedSlots.Owner!, fixedSlots.Types, fixedSlots.Lengths) ?? pattern);

        internal static FixedPattern Open(BoundType pattern) => new(pattern);
    }
}
