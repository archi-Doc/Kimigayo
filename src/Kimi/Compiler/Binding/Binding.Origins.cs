// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Origin expressions (SPEC 15.3.1, 15.3.6): atoms interned per binder, kind and slot (OriginAtom), meets interned in one canonical
// operand order (Meet, CompareOrigins), the context-free outlives facts (OriginOutlives), and the record of an Origin obligation
// (AddObligation). Declaration schemas are Binding.Origins.Schemas.cs's; a Type position's written and omitted Origins
// Binding.Origins.Annotations.cs's.
public sealed partial class Binding
{
    internal static bool IsBorrow(SemanticsKind semantics) => semantics is SemanticsKind.Ref or SemanticsKind.Uniq or SemanticsKind.ObjRef or SemanticsKind.ObjUniq;

    private static bool IsExclusive(SemanticsKind semantics) => semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq;

    private static int CompareOrigins(BoundOrigin a, BoundOrigin b)
    {
        if (ReferenceEquals(a, b))
        {
            return 0;
        }

        var order = ((int)a.Kind).CompareTo((int)b.Kind);
        if (order != 0)
        {
            return order;
        }

        if (a.Binder is { } left && b.Binder is { } right)
        {
            order = StringComparer.Ordinal.Compare(left.CodeContext.Kotonoha.Name, right.CodeContext.Kotonoha.Name);
            if (order == 0)
            {
                order = StringComparer.Ordinal.Compare(left.CodeContext.SourceDocument?.Path, right.CodeContext.SourceDocument?.Path);
            }

            if (order == 0)
            {
                order = left.Span.Start.CompareTo(right.Span.Start);
            }

            if (order == 0)
            {
                order = left.Span.End.CompareTo(right.Span.End);
            }

            // Source-less generated fragments still have immutable source snapshots. Never order by addresses.
            if (order == 0)
            {
                order = StringComparer.Ordinal.Compare(left.CodeContext.SourceDocument?.SourceText, right.CodeContext.SourceDocument?.SourceText);
            }

            if (order != 0)
            {
                return order;
            }
        }

        order = a.Slot.CompareTo(b.Slot);
        if (order != 0)
        {
            return order;
        }

        order = a.Operands.Count.CompareTo(b.Operands.Count);
        for (var i = 0; order == 0 && i < a.Operands.Count; i++)
        {
            order = CompareOrigins(a.Operands[i], b.Operands[i]);
        }

        return order;
    }

    /// <summary>Proves only context-independent outlives facts; lack of proof is not equality.</summary>
    private static bool OriginOutlives(BoundOrigin a, BoundOrigin b)
    {
        if (ReferenceEquals(a, b) || a.Kind is OriginKind.Static or OriginKind.Anchor)
        {
            return true;
        }

        if (UnstatedRelation(a, b) is { } decided)
        {
            return decided;
        }

        if (a.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < a.Operands.Count; i++)
            {
                if (!OriginOutlives(a.Operands[i], b))
                {
                    return false;
                }
            }

            return true;
        }

        if (b.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < b.Operands.Count; i++)
            {
                if (OriginOutlives(a, b.Operands[i]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasAnchor(BoundOrigin origin)
    {
        if (origin.Kind == OriginKind.Anchor)
        {
            return true;
        }

        for (var i = 0; i < origin.Operands.Count; i++)
        {
            if (origin.Operands[i].Kind == OriginKind.Anchor)
            {
                return true;
            }
        }

        return false;
    }

    private BoundOrigin OriginAtom(Koto binder, OriginKind kind, int slot, string? name = null)
    {
        var key = (binder, kind, slot);
        if (!this.originAtoms.TryGetValue(key, out var origin) || (name is not null && origin.Name != name))
        {
            // An edited declaration may reuse a slot with a different name. Keep old expressions immutable while
            // rebuilding the current signature; unchanged passes retain the same interned atom.
            this.originAtoms[key] = origin = new(kind, binder, slot, name) { Body = this.formingBody };
        }

        return origin;
    }

    private BoundOrigin Meet(BoundOrigin a, BoundOrigin b)
    {
        // An anchor outlives every Origin but is no lifetime bound: it names the Loan of a raw Place borrow, so a meet keeps it.
        if (ReferenceEquals(a, b))
        {
            return a;
        }

        if (a.Kind == OriginKind.Static || (OriginOutlives(a, b) && !HasAnchor(a)))
        {
            return b;
        }

        if (b.Kind == OriginKind.Static || (OriginOutlives(b, a) && !HasAnchor(b)))
        {
            return a;
        }

        var capacity = (a.Kind == OriginKind.Intersection ? a.Operands.Count : 1) + (b.Kind == OriginKind.Intersection ? b.Operands.Count : 1);
        var scratch = this.originScratch.Rent(capacity);
        try
        {
            var count = 0;
            Add(a);
            Add(b);
            // Small Origin sets: insertion sort avoids a comparison delegate or boxed enumerator.
            for (var i = 1; i < count; i++)
            {
                var item = scratch[i];
                var j = i;
                while (j > 0 && CompareOrigins(item, scratch[j - 1]) < 0)
                {
                    scratch[j] = scratch[j - 1];
                    j--;
                }

                scratch[j] = item;
            }

            var hash = default(HashCode);
            for (var i = 0; i < count; i++)
            {
                hash.Add(RuntimeHelpers.GetHashCode(scratch[i]));
            }

            var key = hash.ToHashCode();
            if (!this.originExpressions.TryGetValue(key, out var bucket))
            {
                this.originExpressions.Add(key, bucket = new(1));
            }

            for (var i = 0; i < bucket.Count; i++)
            {
                var candidate = bucket[i];
                if (candidate.Operands.Count != count)
                {
                    continue;
                }

                var equal = true;
                for (var j = 0; j < count; j++)
                {
                    equal &= ReferenceEquals(candidate.Operands[j], scratch[j]);
                }

                if (equal)
                {
                    return candidate;
                }
            }

            var created = new BoundOrigin(OriginKind.Intersection, operands: scratch.AsSpan(0, count).ToArray());
            bucket.Add(created);
            return created;

            void Add(BoundOrigin origin)
            {
                if (origin.Kind == OriginKind.Intersection)
                {
                    for (var i = 0; i < origin.Operands.Count; i++)
                    {
                        Add(origin.Operands[i]);
                    }

                    return;
                }

                for (var i = 0; i < count; i++)
                {
                    if (ReferenceEquals(scratch[i], origin))
                    {
                        return;
                    }
                }

                scratch[count++] = origin;
            }
        }
        finally
        {
            this.originScratch.Return(scratch, clearArray: true);
        }
    }

    private void AddObligation(BindingObligation obligation)
    {
        // Rulings S4, design-final §13 A2: a relation with an Unstated end forms no obligation; a Refuted one is recorded at its use,
        // its Unstated end first.
        if (obligation is { Longer: { } longer, Shorter: { } shorter } && UnstatedRelation(longer, shorter) is { } holds)
        {
            if (!holds || (obligation.Equality && UnstatedRelation(shorter, longer) == false))
            {
                // As ownership reports a fit's record: at the value that supplies the longer end, a local's initializer.
                var at = IsFitObligation(obligation) && obligation.Use is VariableKoto { InitializerKoto: { } initializer } ? initializer : obligation.Use;
                var fact = holds ? new OriginRelationFact(at, shorter, longer, Equality: true, obligation.Type, Refuted: true)
                    : new(at, longer, shorter, obligation.Equality, obligation.Type, Refuted: true);
                this.FailExplained(ref this.originRelations, at, BindingFailure.OriginRelation, fact);
            }

            return;
        }

        obligation = obligation with { Body = this.formingBody }; // The body Binding formed it in (DECISIONS R4-M101 E1).
        this.AddPendingRelation(obligation); // Judged when its Closure is classified, if it names a pending atom (R4-M126 §2.5).
        if (this.obligationSet.Add(obligation))
        {
            this.obligations.Add(obligation);
            this.originStateVersion++;
            this.RecordLocalRegionBound(obligation);
        }
    }
}
