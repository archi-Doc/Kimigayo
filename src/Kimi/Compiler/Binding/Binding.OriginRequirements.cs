// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Numerics;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<Koto, OriginRequirementWork> originRequirementNodes = new(ReferenceEqualityComparer.Instance);
    private readonly List<OriginRequirementWork> activeOriginRequirements = new();
    private readonly Queue<OriginRequirementWork> originRequirementQueue = new();
    private uint originRequirementPass;
    private uint originRequirementConsumer;

    // SPEC 15.3.5, 8.15.2 (DECISIONS BU-44): a declared slot's weakest and strongest Loan requirement over the Semantics cases `instance`
    // may select, or with no instance its declaration's admitted cases: Weakest `none` is Phantom in some case, Strongest `uniq` exclusive
    // in some case. Its bits index the GenericSlots of the schema listing it, the instance's, or its binder's with no instance (an inherited
    // slot is read through an instance of the nested declaration). A slot without binders reads its unconditional part at once.
    internal (LoanRequirement Weakest, LoanRequirement Strongest) RequirementAt(OriginParameter slot, BoundType? instance)
        => slot.Requirement.Binders == 0 ? (slot.Requirement.Unconditional, slot.Requirement.Unconditional)
            : this.RequirementOf(slot.Requirement, (instance?.Symbol ?? slot.Origin.Binder!.BoundSymbol)!.Schema!, instance);

    // A pair binder's bit among a schema's GenericSlots; 0 when the schema lists it at none of the first 64.
    private static ulong PairBit(DeclarationSchema schema, BoundType whole)
    {
        for (var k = 0; k < schema.GenericSlots.Count && k < 64; k++)
        {
            if (schema.GenericSlots[k].Semantics is not null && ReferenceEquals(schema.GenericSlots[k].Symbol.WholeType, whole))
            {
                return 1UL << k;
            }
        }

        return 0;
    }

    // The argument selecting binder k's cases: the instance's, or the declaring binder's own pair W with no instance (or before a Type is
    // complete), which selects every admitted case.
    private static BoundType Selecting(DeclarationSchema schema, int k, BoundType? instance)
        => instance is not null && k < instance.Components.Count ? instance.Components[k] : schema.GenericSlots[k].Symbol.WholeType!;

    // The Semantics a concrete Type takes, a layer's or owner.
    private static SemanticsMask CaseSemantics(BoundType type) => type.Kind == BoundTypeKind.Semantics ? type.Semantics.ToMask() : SemanticsMask.Owner;

    // The strongest and weakest Loan class of a set of Semantics (SPEC 15.3.5: `ref` for ref and objref, `uniq` for uniq and objuniq,
    // `none` otherwise); over no Semantics, `none` and `uniq`.
    private static LoanRequirement Strongest(SemanticsMask mask) => mask switch
    {
        _ when (mask & (SemanticsMask.Uniq | SemanticsMask.ObjUniq)) != 0 => LoanRequirement.Uniq,
        _ when (mask & (SemanticsMask.Ref | SemanticsMask.ObjRef)) != 0 => LoanRequirement.Ref,
        _ => LoanRequirement.None,
    };

    private static LoanRequirement Weakest(SemanticsMask mask) => mask switch
    {
        _ when (mask & ~SemanticsMask.Borrow) != 0 => LoanRequirement.None,
        _ when (mask & (SemanticsMask.Ref | SemanticsMask.ObjRef)) != 0 => LoanRequirement.Ref,
        _ => LoanRequirement.Uniq,
    };

    private static LoanRequirement Max(LoanRequirement a, LoanRequirement b) => a < b ? b : a;

    private void ComputeOriginRequirements()
    {
        this.activeOriginRequirements.Clear();
        this.originRequirementQueue.Clear();
        // Retained work survives between passes so summaries and lists are reused. A pass
        // stamp keeps declarations that dropped out from accumulating dependents forever.
        this.originRequirementPass++;
        for (var i = 0; i < this.nodes.Count; i++)
        {
            var node = this.nodes[i];
            if (node is not (FunctionKoto or DeclarationContainerKoto or PropertyAccessorKoto) || node.BoundSymbol?.Schema is not { } schema)
            {
                continue;
            }

            if (schema.Origins.Count == 0 && schema.GenericSlots.Count == 0)
            {
                continue;
            }

            if (ReferenceEquals(node, this.Library.Slice.Declaration) && schema.Origins.Count == 1 && schema.GenericSlots.Count == 1)
            {
                // The validated intrinsic has compiler-managed shared storage, not a
                // user Phantom Origin; preserve its published dependency metadata.
                schema.Origins[0].Variance = OriginVariance.Covariant;
                schema.Origins[0].Requirement = new(LoanRequirement.Ref, 0);
                schema.GenericSlots[0].OriginVariance = OriginVariance.Covariant;
            }

            // SPEC 15.3.5: a Loan<T> Field is analyzed as storing its borrow Type T.
            if (node.BoundSymbol?.LibraryDeclaration == KimiDeclarationId.Loan && schema.GenericSlots.Count == 1)
            {
                schema.GenericSlots[0].OriginVariance = OriginVariance.Covariant;
            }

            if (!this.originRequirementNodes.TryGetValue(node, out var work))
            {
                this.originRequirementNodes.Add(node, work = new(node, schema));
            }

            work.Schema = schema;
            work.Dependents.Clear();
            work.Pass = this.originRequirementPass;
            work.Queued = true;
            this.activeOriginRequirements.Add(work);
            this.originRequirementQueue.Enqueue(work);
        }

        for (var i = 0; i < this.activeOriginRequirements.Count; i++)
        {
            // One consumer at a time, so a monotonic stamp on the producer replaces an edge set.
            this.originRequirementConsumer++;
            this.VisitRequirementTypes(this.activeOriginRequirements[i], collectEdges: true);
        }

        // First discover structural variance. Then close unproven phantom positions as
        // invariant and propagate that fact through all consumers using the same queue:
        // a Type's slot Phantom in every admitted case too (SPEC 15.3.5; DECISIONS BU-44).
        for (var phase = 0; phase < 2; phase++)
        {
            if (phase == 1)
            {
                foreach (var item in this.activeOriginRequirements)
                {
                    for (var i = 0; i < item.Schema.Origins.Count; i++)
                    {
                        var origin = item.Schema.Origins[i];
                        if (origin.Variance == OriginVariance.Unused || (item.Owner is DeclarationContainerKoto && origin.Requirement == default))
                        {
                            origin.Variance = OriginVariance.Invariant;
                        }
                    }

                    for (var i = 0; i < item.Schema.GenericSlots.Count; i++)
                    {
                        var slot = item.Schema.GenericSlots[i];
                        if (slot.OriginVariance == OriginVariance.Unused)
                        {
                            slot.OriginVariance = OriginVariance.Invariant;
                        }
                    }

                    item.Queued = true;
                    this.originRequirementQueue.Enqueue(item);
                }
            }

            while (this.originRequirementQueue.TryDequeue(out var work))
            {
                work.Queued = false;
                if (!this.VisitRequirementTypes(work, collectEdges: false))
                {
                    continue;
                }

                for (var i = 0; i < work.Dependents.Count; i++)
                {
                    var dependent = work.Dependents[i];
                    if (dependent.Queued)
                    {
                        continue;
                    }

                    dependent.Queued = true;
                    this.originRequirementQueue.Enqueue(dependent);
                }
            }
        }
    }

    private bool VisitRequirementTypes(OriginRequirementWork work, bool collectEdges)
    {
        var changed = false;
        if (work.Owner is FunctionKoto function)
        {
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                if (function.Parameters[i].Type.BoundType is { } input)
                {
                    Visit(input, -1);
                }
            }

            if (function.ReturnType?.BoundType is { } result)
            {
                Visit(result, 1);
            }
        }
        else if (work.Owner is PropertyAccessorKoto accessor)
        {
            var operation = Accessor(accessor);
            if (operation.Receiver is { } receiver)
            {
                Visit(receiver, -1);
            }

            if (operation.Input is { } input)
            {
                Visit(input, -1);
            }

            if (operation.Result is { } result)
            {
                Visit(result, 1);
            }
        }
        else if (work.Owner is DeclarationContainerKoto { BoundSymbol.Adt: { } shape })
        {
            for (var i = 0; i < shape.Types.Count; i++)
            {
                if (shape.Types[i].BoundType is { } type)
                {
                    Visit(type, 1);
                }
            }
        }

        return changed;

        void Visit(BoundType type, int polarity)
        {
            if (collectEdges)
            {
                this.CollectRequirementEdges(type, work);
            }
            else
            {
                this.AccumulateRequirements(type, work.Schema, polarity, ref changed);
            }
        }
    }

    private void CollectRequirementEdges(BoundType type, OriginRequirementWork consumer)
    {
        if (type.Symbol?.Declaration is { } declaration && this.originRequirementNodes.TryGetValue(declaration, out var source) &&
            source.Pass == this.originRequirementPass && source.Consumer != this.originRequirementConsumer)
        {
            source.Consumer = this.originRequirementConsumer;
            source.Dependents.Add(consumer);
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            this.CollectRequirementEdges(type.Components[i], consumer);
        }
    }

    // SPEC 15.3.5 (DECISIONS BU-44): a conditional slot, the outer Origin of a pair layer of `whole`'s binder, is a use only in that
    // binder's borrow cases: its bit when the binder admits a borrow and `schema` lists it, its strongest class in every case where the
    // schema lists no such binder (a function's or accessor's use of its Container's), and no use where it admits no borrow.
    private SlotRequirement ConditionalUse(DeclarationSchema schema, BoundType whole)
    {
        var borrows = this.LayerAdmitted(whole) & SemanticsMask.Borrow;
        var bit = borrows == SemanticsMask.None ? 0 : PairBit(schema, whole);
        return new(bit == 0 ? Strongest(borrows) : LoanRequirement.None, bit);
    }

    // A nested slot's requirement as a use in `outer` (SPEC 15.3.5): its unconditional part, and per binder of it the instance's argument:
    // a pair layer is a conditional use of its own binder (ConditionalUse), any other its strongest class (none for a Type parameter or
    // projection, which selects no case).
    private SlotRequirement NestedUse(SlotRequirement nested, BoundType instance, DeclarationSchema outer)
    {
        var use = new SlotRequirement(nested.Unconditional, 0);
        for (var bits = nested.Binders; bits != 0; bits &= bits - 1)
        {
            var argument = Selecting(instance.Symbol!.Schema!, BitOperations.TrailingZeroCount(bits), instance);
            use = use.Join(TryPairLayer(argument, out var whole, out _)
                ? this.ConditionalUse(outer, whole) : new(Strongest(this.SelectedSemantics(argument)), 0));
        }

        return use;
    }

    // The requirement over the cases `instance` selects of the binders in `requirement`, bits of `schema` (RequirementAt): the unconditional
    // part joined per binder with the classes of its selected Semantics; an argument selecting none leaves no case, which claims nothing,
    // neither Phantom nor `uniq` (Uniq, None).
    private (LoanRequirement Weakest, LoanRequirement Strongest) RequirementOf(SlotRequirement requirement, DeclarationSchema schema, BoundType? instance)
    {
        var (weakest, strongest) = (requirement.Unconditional, requirement.Unconditional);
        for (var bits = requirement.Binders; bits != 0; bits &= bits - 1)
        {
            var selected = this.SelectedSemantics(Selecting(schema, BitOperations.TrailingZeroCount(bits), instance));
            if (selected == SemanticsMask.None)
            {
                return (LoanRequirement.Uniq, LoanRequirement.None);
            }

            (weakest, strongest) = (Max(weakest, Weakest(selected)), Max(strongest, Strongest(selected)));
        }

        return (weakest, strongest);
    }

    // The Semantics an argument of a pair slot takes over the cases it selects (SPEC 8.15.2): a pair layer's, its binder's admitted set; a
    // family's, its case Types'; a concrete Type's own; none for a Type parameter or projection, which never passes formation.
    private SemanticsMask SelectedSemantics(BoundType argument)
    {
        if (TryPairLayer(argument, out _, out _))
        {
            return this.LayerAdmitted(argument);
        }

        if (argument.Kind != BoundTypeKind.SemanticsAdaptation)
        {
            return argument.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.AssociatedProjection
                ? SemanticsMask.None : CaseSemantics(argument);
        }

        var selected = SemanticsMask.None;
        for (var i = 0; i < argument.Components.Count; i++)
        {
            selected |= CaseSemantics(argument.Components[i]);
        }

        return selected;
    }

    // SPEC 23.1.2, 15.3.5 (DECISIONS BU-44): the Semantics cases a slot's requirement is `failing` in at an instance, its failure's typed
    // fact: per binder of it that the instance leaves open (a pair layer's or a family's, or the declaring binder with no instance), its
    // cases of that class where they are some but not all of its cases; none when every case fails, as the concrete twin's record names
    // none. A Phantom failure (`none`) holds in the product of the named cases, a `uniq` one in their union. Formed only on failure.
    private PairCase[] FailingCase(OriginParameter slot, BoundType? instance, LoanRequirement failing)
    {
        var (weakest, strongest) = this.RequirementAt(slot, instance);
        var cases = new List<PairCase>();
        for (var bits = weakest == strongest ? 0 : slot.Requirement.Binders; bits != 0; bits &= bits - 1)
        {
            var argument = Selecting((instance?.Symbol ?? slot.Origin.Binder!.BoundSymbol)!.Schema!, BitOperations.TrailingZeroCount(bits), instance);
            var family = argument.Kind == BoundTypeKind.SemanticsAdaptation;
            if (!family && !TryPairLayer(argument, out _, out _))
            {
                continue; // A concrete argument fails in its one case or in none.
            }

            var start = cases.Count;
            var admitted = family ? (SemanticsMask)argument.Length : this.LayerAdmitted(argument);
            foreach (var semantics in SemanticsOrder)
            {
                var taken = family && FamilyCase(argument, semantics) is { } child ? CaseSemantics(child) : semantics.ToMask();
                if ((admitted & semantics.ToMask()) != 0 && Strongest(taken) == failing)
                {
                    cases.Add(new(argument.Symbol!, semantics));
                }
            }

            if (cases.Count - start == BitOperations.PopCount((uint)admitted))
            {
                cases.RemoveRange(start, cases.Count - start); // Each case of this binder fails: the record names none of it.
            }
        }

        return [.. cases];
    }

    private sealed class OriginRequirementWork(Koto owner, DeclarationSchema schema)
    {
        internal Koto Owner { get; } = owner;

        internal DeclarationSchema Schema { get; set; } = schema;

        internal List<OriginRequirementWork> Dependents { get; } = new();

        internal uint Pass { get; set; }

        internal uint Consumer { get; set; }

        internal bool Queued { get; set; }
    }
}
