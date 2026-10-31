// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Call slots (SPEC 10.8, 8.1.2): a callee's structural slots and their inference.
// - Responsibility: the declaration owning a callee's slots (CallSlotOwner: a function its own, a constructor its structure's, an enum
//   constructing a Case its own), their count and the binder's Origin count; a member pattern as a call reads it (CallMemberPattern); a
//   constructor's fixed enclosing slots (InitializeCallSlots) and its constructed Type (ConstructionType); the structural matching of a
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

    // Constructor slots already include their fixed enclosing environment. Apply structural
    // bindings once, to declaration patterns; never substitute inside an already-bound argument.
    private BoundType? CallMemberPattern(BoundType type, Koto binder, BoundType? declaringType)
        => !ReferenceEquals(CallSlotOwner(binder), binder) && declaringType is not null
            ? this.SubstituteStoredOrigins(type, CallSlotOwner(binder), (BoundOrigin[])declaringType.OriginArguments)
            : this.MemberType(type, declaringType);

    private BoundType? ConstructionType(FunctionKoto function, BoundType template, BoundType?[] slots)
    {
        var count = CallSlotCount(function);
        for (var i = 0; i < count; i++)
        {
            if (slots[i] is null)
            {
                return null;
            }
        }

        return this.types.Rebuild(template, ((BoundType[])(object)slots).AsSpan(0, count), template.Origin, (BoundOrigin[])template.OriginArguments);
    }

    private void InitializeCallSlots(InvocationKoto call, FunctionKoto function, BoundType? declaringType, BoundType?[] slots, bool fixedConstruction = false)
    {
        var count = CallSlotCount(function);
        slots.AsSpan(0, count).Clear();
        if (!function.IsConstructor || declaringType is null)
        {
            return;
        }

        var infer = !fixedConstruction && call.Method is MemberAccessKoto member && this.inferredConstructorTargets.Contains(member.Left);
        var own = CallOwnSlots(function).Count;
        var owner = CallSlotOwner(function);
        for (var i = 0; i < count; i++)
        {
            var fixedType = declaringType.Components[i];
            // An outer slot is always a fixed binding, even when it is itself a parameter.
            if (!infer || i >= own || fixedType.Kind != BoundTypeKind.Parameter || !ReferenceEquals(fixedType.Symbol?.Scope.Owner, owner))
            {
                slots[i] = fixedType;
            }
        }
    }

    // SPEC 8.1.2: an s/U whose s is already inferred is matched as the Type it forms: U for owner, one reference layer with
    // the occurrence's outer-Origin slot for ref or uniq. An application whose s is still open is returned as it is.
    private BoundType FormedApplication(BoundType pattern, Koto function, BoundType?[] arguments)
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
    private bool Infer(BoundType pattern, BoundType actual, Koto function, BoundType?[] arguments, bool inferOrigins = false, BoundLength?[]? lengths = null, bool commonOrigins = false, bool structural = false, SignatureEvidence? evidence = null, bool relateLater = false, bool invariant = false)
    {
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
                this.Infer(this.DirectTarget(whole), actual, function, arguments, inferOrigins, lengths, commonOrigins, structural, evidence, relateLater, invariant));
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
                return this.Infer(pattern.Components[0], actual, function, arguments, inferOrigins, lengths, commonOrigins, structural, evidence, relateLater, invariant);
            }

            return actual.Kind == BoundTypeKind.Semantics && actual.Semantics == whole.Semantics &&
                this.Infer(pattern.Components[0], actual.Components[0], function, arguments, inferOrigins, lengths, commonOrigins, structural, evidence, relateLater, invariant || whole.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq or SemanticsKind.Raw);
        }

        // SPEC 3.1.1: the wrapping Scalar Wrapping<u8> is the instance of the pattern Wrapping<T>, so T is inferred from
        // the Scalar's integer argument; the substituted parameter then normalizes to the same Scalar.
        if (actual.IsWrappingInteger && pattern is { Kind: BoundTypeKind.Constructed, Components.Count: 1 } && pattern.Symbol?.LibraryDeclaration == KimiDeclarationId.Wrapping)
        {
            return this.Infer(pattern.Components[0], actual.Underlying, function, arguments, inferOrigins, lengths, commonOrigins, structural, evidence, relateLater, invariant);
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
                return arguments[slot] is not { } establishedArray || IsStructuralSubtype(establishedArray, actual);
            }

            // SPEC 10.2.2: a parameter constrained to Position, PositionRange or PrimitiveInteger binds the referent; the
            // argument is then value-read.
            if (!structural && actual is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } &&
                ComparisonReferent(actual) is var referent && !ReferenceEquals(referent, actual) && this.InfersReadReferent(pattern, this.activeInferenceFunction is { } inference && ReferenceEquals(CallSlotOwner(inference), function) ? inference : function))
            {
                actual = referent;
            }

            if (evidence is { Binder: FunctionKoto { ClosureDefinition: not null } closure } && HasEnvironmentOrigin(actual, closure))
            {
                // SPEC 8.10, 7.7.4: a result over the closure's hidden environment receiver satisfies no Callable signature and no
                // erasure, so that part is no evidence for the slot; the Callable proof or the conversion judges it against the rest.
                this.environmentEvidence = true;
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
                    IsStructuralSubtype(actual, previous) && IsStructuralSubtype(previous, actual))
                {
                    // SPEC 10.8, 15.3.6: Origin evidence for a slot never makes the candidate inapplicable; each argument's relation to
                    // the solution is judged after selection (SPEC 15.6.1). An explicit Type argument is kept, an invariant binding is
                    // kept over covariant ones and the first invariant one over later ones, whatever the argument order, and covariant
                    // bindings meet.
                    if (!commonOrigins || this.invariantSlots.Contains(slot))
                    {
                        arguments[slot] = previous;
                    }
                    else if (invariant)
                    {
                        arguments[slot] = actual;
                        this.invariantSlots.Add(slot);
                    }
                    else
                    {
                        arguments[slot] = IsSubtype(actual, previous) ? previous : this.CommonOriginType(previous, actual, out _) ?? previous;
                    }

                    return true;
                }

                // SPEC 10.8: structural matching compares normalized Types, so two spellings of one Function Type, whose per-call
                // inputs have distinct binders, are one binding.
                if (ReferenceEquals(previous, actual) || (!structural && inferOrigins && IsSubtype(actual, previous)) ||
                    (structural && previous.Kind == BoundTypeKind.Function && actual.Kind == BoundTypeKind.Function &&
                    IsSubtype(actual, previous) && IsSubtype(previous, actual)))
                {
                    arguments[slot] = previous;
                    return true;
                }

                if (commonOrigins && this.CommonOriginType(previous, actual, out _) is { } common)
                {
                    arguments[slot] = common;
                    return true;
                }

                return false;
            }

            arguments[slot] = actual;
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
            !(lengths is not null && pattern.Kind == BoundTypeKind.FixedArray ? this.InferLength(pattern, actual, function, lengths) : pattern.Length == actual.Length && ReferenceEquals(pattern.LengthExpression, actual.LengthExpression)) ||
            (!inferOrigins && evidence is null && !ReferenceEquals(pattern.Origin, actual.Origin)) || pattern.OriginArguments.Count != actual.OriginArguments.Count || pattern.Components.Count != actual.Components.Count || pattern.LengthArguments.Length != actual.LengthArguments.Length || (pattern.Components.Count == 0 && pattern.LengthArguments.Length == 0 && pattern.OriginArguments.Count == 0))
        {
            return false;
        }

        for (var i = 0; i < pattern.LengthArguments.Length; i++)
        {
            var required = pattern.LengthArguments[i];
            var supplied = actual.LengthArguments[i];
            if (!ReferenceEquals(required, supplied) && (required is null || supplied is null || lengths is null || !this.InferLength(required, supplied, function, lengths)))
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
                arguments,
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
}
