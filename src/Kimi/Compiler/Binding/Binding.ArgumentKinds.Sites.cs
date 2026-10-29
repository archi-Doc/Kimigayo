// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Argument sites (SPEC 10.5.1, 15.6.4 step 3, D-1; DECISIONS R4-M155): where a part that takes its Type from its call records its
// relations, which the completing call publishes.
// - Responsibility: the one entry (PublishPartSite) through which a named call's completion, a Case construction's payloads and a
//   value call's acquisition publish a part's site (ArgumentFact.Site, its well-formedness) and opened site (ArgumentFact.Opened, a
//   contextual payload's fit, D-1).
// - Inputs: the completing call's own published site, its parameter or payload pattern and the slots it took from its own expected
//   Type (EvaluatedCandidate.FromExpected for a call, a construction's slots fixed by its expected Type).
// - Outputs: the facts JudgedAt and OpenedAt read (Binding.CallRelations.cs).
// - Invariants: a part's site is the argument of the call whose solution determines its Type, followed outward only through a call
//   that takes the position from its own expected Type and is itself a part of an outer argument; a Tuple or array literal's elements
//   share their literal's site.
// - Prohibitions: no location is found by walking the syntax (Parent); no consumer decides a site.
public sealed partial class Binding
{
    // Whether `type`, a parameter or payload pattern of `binder`, names a slot of `fromExpected`: a Type parameter, or an Origin it binds.
    private static bool NamesExpectedSlot(BoundType type, Koto binder, in SlotSet fromExpected, int depth)
    {
        if (fromExpected.IsEmpty || depth > 64)
        {
            return false;
        }

        if (type.Kind == BoundTypeKind.Parameter && type.Symbol is { } symbol && ContainerSlot(CallSlotOwner(binder), symbol) is >= 0 and var slot)
        {
            return fromExpected.Contains(slot);
        }

        if (type.CarriesOrigin && (Names(type.Origin, binder, fromExpected) || AnyNamed(type.OriginArguments, binder, fromExpected)))
        {
            return true;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (NamesExpectedSlot(type.Components[i], binder, fromExpected, depth + 1))
            {
                return true;
            }
        }

        return false;

        static bool AnyNamed(IReadOnlyList<BoundOrigin> origins, Koto binder, in SlotSet fromExpected)
        {
            for (var i = 0; i < origins.Count; i++)
            {
                if (Names(origins[i], binder, fromExpected))
                {
                    return true;
                }
            }

            return false;
        }

        static bool Names(BoundOrigin? origin, Koto binder, in SlotSet fromExpected)
            => origin is not null &&
                (ExpectedSlot(origin, binder) is >= 0 and var slot ? fromExpected.Contains(slot) : AnyNamed(origin.Operands, binder, fromExpected));
    }

    // The index of an Origin `binder` binds among its expected slots: after its Type slots, its Origins, then its per-call inputs; -1 for
    // any other Origin.
    private static int ExpectedSlot(BoundOrigin origin, Koto binder)
        => origin.Kind is OriginKind.Parameter or OriginKind.Input && ReferenceEquals(origin.Binder, binder)
            ? CallSlotCount(binder) + (origin.Kind == OriginKind.Input ? CallOriginCount(binder) : 0) + origin.Slot : -1;

    // The slots of `binder` an expected Type can fix: its Type slots, Origins and per-call inputs.
    private static int ExpectedSlotCount(Koto binder)
        => CallSlotCount(binder) + CallOriginCount(binder) + (binder is FunctionKoto ? InputOriginCount(binder) : 0);

    // SPEC 15.6.4 step 3, D-1 (DECISIONS R4-M155): the site of `part`, which `call` (a selected call, or a Case construction, of `binder`)
    // completes at the parameter Type `pattern`. Where the call takes the Type at that position from its own expected Type (the pattern
    // names a slot of `fromExpected`) and is itself a part of an outer call's argument, the part's relations are that call's own:
    // its site and opened site, followed outward. Otherwise they lie at the part, a payload's fit there too unless the call opens the
    // position (`opened`, the part or null). A Tuple or array literal's elements share their literal's site.
    private void PublishPartSite(Koto call, Koto binder, Koto part, BoundType pattern, in SlotSet fromExpected, Koto? opened)
    {
        var outer = this.RecordedFact(call);
        if (outer.Site is { } site && NamesExpectedSlot(pattern, binder, fromExpected, 0))
        {
            this.PublishArgumentSite(part, site, outer.Opened);
        }
        else
        {
            this.PublishArgumentSite(part, part, opened);
        }
    }

    private void PublishArgumentSite(Koto part, Koto site, Koto? opened)
    {
        var node = KotoHelper.UnwrapParentheses(part);
        var key = SourceRef.Of(node);
        this.argumentKinds[key] = this.argumentKinds.GetValueOrDefault(key) with { Site = site, Opened = opened };
        var elements = node is TupleLiteralKoto tuple ? tuple.Elements : node is ArrayLiteralKoto array ? array.Elements : null;
        for (var i = 0; elements is not null && i < elements.Count; i++)
        {
            this.PublishArgumentSite(elements[i], site, opened);
        }
    }
}
