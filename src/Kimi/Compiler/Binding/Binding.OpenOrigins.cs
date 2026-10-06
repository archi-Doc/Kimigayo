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

    // SPEC 15.3.6: a selected call whose slot only an argument's own per-call Origin would satisfy, with that argument and the Reason.
    private Dictionary<Koto, (Koto Argument, string Reason)>? perCallOrigins;

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
            left.Length != right.Length || !ReferenceEquals(left.LengthExpression, right.LengthExpression) || !ReferenceEquals(left.ClosureContext, right.ClosureContext) || !left.LengthArguments.AsSpan().SequenceEqual(right.LengthArguments) ||
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

    // Whether `type` holds an open region that the known call signature of `source` supplied.
    private static bool HasOpenOriginFrom(BoundType type, Koto source)
    {
        if (!type.CarriesOrigin)
        {
            return false;
        }

        if (From(type.Origin, source))
        {
            return true;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (From(type.OriginArguments[i], source))
            {
                return true;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (HasOpenOriginFrom(type.Components[i], source))
            {
                return true;
            }
        }

        return false;

        static bool From(BoundOrigin? origin, Koto source)
        {
            if (origin is null)
            {
                return false;
            }

            for (var i = 0; i < origin.Operands.Count; i++)
            {
                if (From(origin.Operands[i], source))
                {
                    return true;
                }
            }

            return origin.Open && ReferenceEquals(origin.Binder, source);
        }
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

            return changed ? this.InternType(type.Kind, type.Symbol, type.Semantics, components.AsSpan(0, type.Components.Count), type.Length, origin, origins.AsSpan(0, type.OriginArguments.Count), type.LengthExpression, type.ClosureContext, type.LengthArguments) : type;
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

    // SPEC 15.3.6, 10.8: after selection, a slot that only the argument's own per-call Origin would satisfy is MissingOriginBinding_Kd
    // at the call, whose Reason names the argument and the slot, with the argument related.
    private BoundType? FailPerCallOrigin(InvocationKoto call, FunctionKoto selected, BoundType?[] slots, Koto argument)
    {
        var slot = "the Type argument";
        for (var g = 0; g < selected.GenericArguments.Count; g++)
        {
            if (slots[g] is { } solution && HasOpenOriginFrom(solution, argument))
            {
                slot = selected.GenericArguments[g].Identifier;
                break;
            }
        }

        return this.FailExplained(ref this.perCallOrigins, call, BindingFailure.MissingOrigin, (argument, $"only a per-call Origin of {argument} would satisfy {slot}"));
    }

    // SPEC 15.3.6, 10.8: `type` with each open region that the known call signature of `source` put in place of its k-th per-call input
    // replaced by the outer Origin of the k-th input of `signature`, the required call signature at that parameter: the per-call Origin
    // that alone would satisfy the slot. Null when `type` holds no such open region. With `fixedOnly`, only a required input over a
    // fixed Origin, such as the callee's `x` of `(ref/i32 during x) -> T`, replaces its open region: SPEC 10.7 instantiates the
    // argument's per-call input to that Origin, which is then a solution of the slot, not a per-call Origin.
    private BoundType? PerCallStandIn(BoundType type, Koto source, BoundType signature, bool fixedOnly = false)
    {
        var replaced = this.ReplaceOpenOrigins(type, source, signature, fixedOnly);
        return ReferenceEquals(replaced, type) ? null : replaced;
    }

    // SPEC 10.7, 10.8: the slot solutions with each open region of `source` at a required input over a fixed Origin instantiated to that
    // Origin, once the fit holds with it (PerCallStandIn with `fixedOnly`).
    private void InstantiateOpenOrigins(Span<BoundType?> slots, Koto source, BoundType signature)
    {
        for (var g = 0; g < slots.Length; g++)
        {
            if (slots[g] is { } solution)
            {
                slots[g] = this.ReplaceOpenOrigins(solution, source, signature, true);
            }
        }
    }

    private BoundType ReplaceOpenOrigins(BoundType type, Koto source, BoundType signature, bool fixedOnly = false)
    {
        if (!type.CarriesOrigin)
        {
            return type;
        }

        var origin = type.Origin is { } outer ? this.ReplaceOpenOrigin(outer, source, signature, fixedOnly) : null;
        var components = this.RentTypes(type.Components.Count);
        var origins = this.originScratch.Rent(type.OriginArguments.Count);
        try
        {
            var changed = !ReferenceEquals(origin, type.Origin);
            for (var i = 0; i < type.Components.Count; i++)
            {
                components[i] = this.ReplaceOpenOrigins(type.Components[i], source, signature, fixedOnly);
                changed |= !ReferenceEquals(components[i], type.Components[i]);
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                origins[i] = this.ReplaceOpenOrigin(type.OriginArguments[i], source, signature, fixedOnly);
                changed |= !ReferenceEquals(origins[i], type.OriginArguments[i]);
            }

            return changed ? this.InternType(type.Kind, type.Symbol, type.Semantics, components.AsSpan(0, type.Components.Count), type.Length, origin, origins.AsSpan(0, type.OriginArguments.Count), type.LengthExpression, type.ClosureContext, type.LengthArguments) : type;
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
        }
    }

    private BoundOrigin ReplaceOpenOrigin(BoundOrigin origin, Koto source, BoundType signature, bool fixedOnly)
    {
        if (origin.Open && ReferenceEquals(origin.Binder, source))
        {
            // A required input's own per-call Origin is the outer Origin of a direct borrow that the signature's Function Type binds at
            // that position (SPEC 8.6); any other Origin there is fixed.
            var inputs = signature.Components[0];
            return origin.Slot >= 0 && origin.Slot < inputs.Components.Count && inputs.Components[origin.Slot].Origin is { Open: false } perCall &&
                !(fixedOnly && perCall is { Kind: OriginKind.Input, Occurrence: null, Binder: FunctionTypeKoto } && perCall.Slot == origin.Slot) ? perCall : origin;
        }

        if (origin.Kind == OriginKind.Intersection && origin.Operands.Count != 0)
        {
            var result = this.ReplaceOpenOrigin(origin.Operands[0], source, signature, fixedOnly);
            for (var i = 1; i < origin.Operands.Count; i++)
            {
                result = this.Meet(result, this.ReplaceOpenOrigin(origin.Operands[i], source, signature, fixedOnly));
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

            return changed ? this.InternType(type.Kind, type.Symbol, type.Semantics, components.AsSpan(0, type.Components.Count), type.Length, origin, origins.AsSpan(0, type.OriginArguments.Count), type.LengthExpression, type.ClosureContext, type.LengthArguments) : type;
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
