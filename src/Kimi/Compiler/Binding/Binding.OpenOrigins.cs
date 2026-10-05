// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// SPEC 10.8, 15.3.6: a known call signature as evidence for an outer candidate's slots. Binder quantifies the signature's own Origins
// (the Function Item or closure declaration, or the Function Type's own syntax); Source is the argument that supplied it.
internal readonly record struct SignatureEvidence(Koto? Binder, Koto Source);

public sealed partial class Binding
{
    // The key of a call's own open region for the callee's i-th Origin, apart from the keys of argument evidence.
    private const int ResultOnlyKey = int.MinValue / 2;

    // One open region per argument and per Origin the argument's known call signature quantifies (Input slot, or -1 - Parameter slot).
    private readonly Dictionary<(Koto Source, int Key), BoundOrigin> openOrigins = new();

    // SPEC 10.8: an Origin that the evidence's own binder quantifies, such as a per-call input, never becomes the solution of an Origin
    // inside a slot. The slot keeps the structure and Semantics, and each such Origin is replaced by an open region of the call (SPEC
    // 15.3.6): a local region that holds no Loans, is never displayed, and that other evidence for the slot fills.
    internal static bool HasOpenOrigin(BoundType type)
    {
        if (!type.CarriesOrigin)
        {
            return false;
        }

        if (type.Origin is { } origin && IsOpen(origin))
        {
            return true;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (IsOpen(type.OriginArguments[i]))
            {
                return true;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (HasOpenOrigin(type.Components[i]))
            {
                return true;
            }
        }

        return false;

        static bool IsOpen(BoundOrigin origin)
        {
            if (origin.Open)
            {
                return true;
            }

            for (var i = 0; i < origin.Operands.Count; i++)
            {
                if (origin.Operands[i].Open)
                {
                    return true;
                }
            }

            return false;
        }
    }

    // SPEC 15.3.7, IMPL 21.3: Origin differences alone never duplicate generated code, so two Types that differ only by open regions
    // name one instance.
    internal static bool SameModuloOpenOrigins(BoundType? left, BoundType? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Kind != right.Kind || left.Semantics != right.Semantics || !ReferenceEquals(left.Symbol, right.Symbol) ||
            left.Length != right.Length || !ReferenceEquals(left.LengthExpression, right.LengthExpression) || !ReferenceEquals(left.ClosureContext, right.ClosureContext) ||
            left.Components.Count != right.Components.Count || left.OriginArguments.Count != right.OriginArguments.Count ||
            !SameOrigin(left.Origin, right.Origin) || !(HasOpenOrigin(left) || HasOpenOrigin(right)))
        {
            return false;
        }

        for (var i = 0; i < left.OriginArguments.Count; i++)
        {
            if (!SameOrigin(left.OriginArguments[i], right.OriginArguments[i]))
            {
                return false;
            }
        }

        for (var i = 0; i < left.Components.Count; i++)
        {
            if (!SameModuloOpenOrigins(left.Components[i], right.Components[i]))
            {
                return false;
            }
        }

        return true;

        static bool SameOrigin(BoundOrigin? a, BoundOrigin? b) => ReferenceEquals(a, b) || (a is { Open: true } && b is { Open: true });
    }

    internal static bool SameModuloOpenOrigins(ReadOnlySpan<BoundType?> left, ReadOnlySpan<BoundType?> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (!SameModuloOpenOrigins(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    // An Origin that a known call signature's binder quantifies, or that a nested Function Type's own syntax binds per call.
    private static bool QuantifiedBy(BoundOrigin origin, Koto binder)
    {
        if (origin.Kind is OriginKind.Input or OriginKind.Parameter)
        {
            return ReferenceEquals(origin.Binder, binder) || origin.Binder is FunctionTypeKoto;
        }

        for (var i = 0; i < origin.Operands.Count; i++)
        {
            if (QuantifiedBy(origin.Operands[i], binder))
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 15.3.6: a callee's own Origin that no parameter Type and no relation clause mentions, such as the result-only `s` of
    // `constant() -> ref/i32 during s`, has no bound at a call that no expected result fixes. It is a local region of the call, solved
    // with the body's other local regions by its uses; it needs no annotation and is never replaced by static. No input supplies it,
    // so the result holds no Loan through it: the call's open region (keyed apart from argument evidence by ResultOnlyKey).
    private bool OpenResultOnlyOrigins(Koto call, FunctionKoto function, BoundOrigin[] origins)
    {
        var opened = false;
        var relations = this.originDeclarations.GetValueOrDefault(function)?.Relations;
        for (var i = 0; i < function.Origins.Count; i++)
        {
            if (origins[i] is not null)
            {
                continue;
            }

            var mentioned = false;
            for (var p = 0; p < function.Parameters.Count && !mentioned; p++)
            {
                mentioned = function.Parameters[p].Type.BoundType is not { } parameter || MentionsOriginSlot(parameter, function, i);
            }

            for (var r = 0; relations is not null && r < relations.Count && !mentioned; r++)
            {
                mentioned = NamesSlot(relations[r].Longer, function, i) || NamesSlot(relations[r].Shorter, function, i);
            }

            if (!mentioned)
            {
                origins[i] = this.OpenOrigin(call, ResultOnlyKey + i);
                opened = true;
            }
        }

        return opened;

        static bool MentionsOriginSlot(BoundType type, Koto binder, int slot)
        {
            if (!type.CarriesOrigin)
            {
                return false;
            }

            if (NamesSlot(type.Origin, binder, slot))
            {
                return true;
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                if (NamesSlot(type.OriginArguments[i], binder, slot))
                {
                    return true;
                }
            }

            for (var i = 0; i < type.Components.Count; i++)
            {
                if (MentionsOriginSlot(type.Components[i], binder, slot))
                {
                    return true;
                }
            }

            return false;
        }

        static bool NamesSlot(BoundOrigin? origin, Koto binder, int slot)
        {
            if (origin is null)
            {
                return false;
            }

            for (var i = 0; i < origin.Operands.Count; i++)
            {
                if (NamesSlot(origin.Operands[i], binder, slot))
                {
                    return true;
                }
            }

            return origin.Kind == OriginKind.Parameter && ReferenceEquals(origin.Binder, binder) && origin.Slot == slot;
        }
    }

    private BoundOrigin OpenOrigin(Koto source, int key)
    {
        if (!this.openOrigins.TryGetValue((source, key), out var origin))
        {
            this.openOrigins.Add((source, key), origin = new(OriginKind.Inference, source, key) { Open = true, InputIndex = -1 });
        }

        return origin;
    }

    // The evidence Type with each Origin that `binder` quantifies replaced by the open region of `source`.
    private BoundType OpenKnownOrigins(BoundType type, Koto binder, Koto source)
    {
        if (!type.CarriesOrigin)
        {
            return type;
        }

        var origin = type.Origin is { } outer ? this.OpenKnownOrigin(outer, binder, source) : null;
        var components = this.RentTypes(type.Components.Count);
        var origins = this.originScratch.Rent(type.OriginArguments.Count);
        try
        {
            var changed = !ReferenceEquals(origin, type.Origin);
            for (var i = 0; i < type.Components.Count; i++)
            {
                components[i] = this.OpenKnownOrigins(type.Components[i], binder, source);
                changed |= !ReferenceEquals(components[i], type.Components[i]);
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                origins[i] = this.OpenKnownOrigin(type.OriginArguments[i], binder, source);
                changed |= !ReferenceEquals(origins[i], type.OriginArguments[i]);
            }

            return changed ? this.InternType(type.Kind, type.Symbol, type.Semantics, components.AsSpan(0, type.Components.Count), type.Length, origin, origins.AsSpan(0, type.OriginArguments.Count), type.LengthExpression, type.ClosureContext) : type;
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
        }
    }

    private BoundOrigin OpenKnownOrigin(BoundOrigin origin, Koto binder, Koto source)
    {
        if (origin.Kind is OriginKind.Input or OriginKind.Parameter && ReferenceEquals(origin.Binder, binder))
        {
            return this.OpenOrigin(source, origin.Kind == OriginKind.Input ? origin.Slot : -1 - origin.Slot);
        }

        if (origin.Kind == OriginKind.Intersection && origin.Operands.Count != 0)
        {
            var result = this.OpenKnownOrigin(origin.Operands[0], binder, source);
            for (var i = 1; i < origin.Operands.Count; i++)
            {
                result = this.Meet(result, this.OpenKnownOrigin(origin.Operands[i], binder, source));
            }

            return result;
        }

        return origin;
    }

    // `type` with each open region replaced by the Origin that `other` has at the same position, when that one is an ordinary Origin,
    // or also an open region of `other` (`takeOpen`), so that two open positions keep the earlier region. Unequal structure is left to
    // the caller's ordinary comparison.
    private BoundType FillOpenOrigins(BoundType type, BoundType other, bool takeOpen)
    {
        if (ReferenceEquals(type, other) || !type.CarriesOrigin || !other.CarriesOrigin || type.Kind != other.Kind || type.Components.Count != other.Components.Count ||
            type.OriginArguments.Count != other.OriginArguments.Count)
        {
            return type;
        }

        var origin = Fill(type.Origin, other.Origin, takeOpen);
        var components = this.RentTypes(type.Components.Count);
        var origins = this.originScratch.Rent(type.OriginArguments.Count);
        try
        {
            var changed = !ReferenceEquals(origin, type.Origin);
            for (var i = 0; i < type.Components.Count; i++)
            {
                components[i] = this.FillOpenOrigins(type.Components[i], other.Components[i], takeOpen);
                changed |= !ReferenceEquals(components[i], type.Components[i]);
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                origins[i] = Fill(type.OriginArguments[i], other.OriginArguments[i], takeOpen)!;
                changed |= !ReferenceEquals(origins[i], type.OriginArguments[i]);
            }

            return changed ? this.InternType(type.Kind, type.Symbol, type.Semantics, components.AsSpan(0, type.Components.Count), type.Length, origin, origins.AsSpan(0, type.OriginArguments.Count), type.LengthExpression, type.ClosureContext) : type;
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
        }

        static BoundOrigin? Fill(BoundOrigin? origin, BoundOrigin? supplied, bool takeOpen)
            => origin is { Open: true } && supplied is not null && (supplied.Kind != OriginKind.Inference || (takeOpen && supplied.Open)) ? supplied : origin;
    }
}
