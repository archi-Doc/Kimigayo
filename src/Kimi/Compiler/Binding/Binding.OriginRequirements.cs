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

    // The words of one binder ordinal beyond 63 (SingleBinder), one array per ordinal, and the buffer NestedUse builds a use's binders
    // into, both reused across passes (BinderSet.Union).
    private ulong[]?[] binderSingles = [];
    private ulong[]? binderScratch;

    // SPEC 15.3.5, 8.15.2 (DECISIONS BU-44): a declared slot's weakest and strongest Loan requirement over the Semantics cases `instance`
    // may select, or with no instance its declaration's admitted cases: Weakest `none` is Phantom in some case, Strongest `uniq` exclusive
    // in some case. A slot without binders reads its unconditional part at once.
    internal (LoanRequirement Weakest, LoanRequirement Strongest) RequirementAt(OriginParameter slot, BoundType? instance)
        => slot.Requirement.Binders.IsEmpty ? (slot.Requirement.Unconditional, slot.Requirement.Unconditional)
            : this.RequirementOf(slot.Requirement, ListingSchema(slot, instance), instance);

    // The schema whose pair slots a slot's binders number (SlotRequirement): the one listing it, its declaring schema for its own slot and
    // the instance's for an inherited copy, which records the nested declaration's own uses (SPEC 6.1.3).
    private static DeclarationSchema ListingSchema(OriginParameter slot, BoundType? instance)
    {
        var declaring = slot.Origin.Binder!.BoundSymbol!.Schema!;
        return ReferenceEquals(declaring.Origins[slot.Slot], slot) || instance?.Symbol?.Schema is not { } listing ? declaring : listing;
    }

    // A pair binder's ordinal among the schema's pair slots, own then inherited (Type slots take none); -1 when the schema lists no such
    // binder.
    private static int PairOrdinal(DeclarationSchema schema, BoundType whole)
    {
        for (int k = 0, ordinal = 0; k < schema.GenericSlots.Count; k++)
        {
            if (schema.GenericSlots[k].Semantics is null)
            {
                continue;
            }

            if (ReferenceEquals(schema.GenericSlots[k].Symbol.WholeType, whole))
            {
                return ordinal;
            }

            ordinal++;
        }

        return -1;
    }

    // The argument selecting the cases of the pair binder at `ordinal` of `schema` (PairOrdinal): the instance's, in its own-then-inherited
    // order, where an enclosing declaration's slots follow a nested declaration's own (SPEC 6.1.3), or with no instance (or before a Type
    // is complete) the binder's own pair W, which selects every admitted case.
    private static BoundType Selecting(DeclarationSchema schema, int ordinal, BoundType? instance)
    {
        var k = -1;
        for (var seen = -1; seen < ordinal;)
        {
            seen += schema.GenericSlots[++k].Semantics is null ? 0 : 1;
        }

        var at = (instance?.Symbol?.Schema?.GenericSlots.Count ?? 0) - schema.GenericSlots.Count + k;
        return instance is not null && (uint)at < (uint)instance.Components.Count ? instance.Components[at] : schema.GenericSlots[k].Symbol.WholeType!;
    }

    // The Semantics a case Type takes, a layer's or owner.
    private static SemanticsMask CaseSemantics(BoundType type) => type.Kind == BoundTypeKind.Semantics ? type.Semantics.ToMask() : SemanticsMask.Owner;

    // The Semantics an argument takes in its case `mode` (SelectedCases): a family's case Type's, any other's the case's own.
    private static SemanticsMask Taken(BoundType argument, SemanticsKind mode)
        => argument.Kind == BoundTypeKind.SemanticsAdaptation && FamilyCase(argument, mode) is { } child ? CaseSemantics(child) : mode.ToMask();

    // The Loan class of a Semantics (SPEC 15.3.5): `uniq` for uniq and objuniq, `ref` for ref and objref, `none` otherwise.
    private static LoanRequirement ClassOf(SemanticsMask semantics) => semantics switch
    {
        SemanticsMask.Uniq or SemanticsMask.ObjUniq => LoanRequirement.Uniq,
        SemanticsMask.Ref or SemanticsMask.ObjRef => LoanRequirement.Ref,
        _ => LoanRequirement.None,
    };

    private static LoanRequirement Max(LoanRequirement a, LoanRequirement b) => a < b ? b : a;

    private static LoanRequirement Min(LoanRequirement a, LoanRequirement b) => a < b ? a : b;

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
                schema.Origins[0].Requirement = new(LoanRequirement.Ref, default);
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
                this.CollectRequirementEdges(type, work, this.visitedParts.Begin(this.storageVersion));
            }
            else
            {
                this.AccumulateRequirements(type, work.Schema, polarity, ref changed);
            }
        }
    }

    // The edges from the declarations a Type names to their consumer, each distinct part visited once per call: a part's edge is added
    // once per consumer (its Consumer mark), so a part met again adds nothing (DECISIONS BU-55, BU-58).
    private void CollectRequirementEdges(BoundType type, OriginRequirementWork consumer, uint query)
    {
        if (type.Symbol?.Declaration is { } declaration && this.originRequirementNodes.TryGetValue(declaration, out var source) &&
            source.Pass == this.originRequirementPass && source.Consumer != this.originRequirementConsumer)
        {
            source.Consumer = this.originRequirementConsumer;
            source.Dependents.Add(consumer);
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (this.FirstVisit(type.Components[i], query))
            {
                this.CollectRequirementEdges(type.Components[i], consumer, query);
            }
        }
    }

    // SPEC 15.3.5 (DECISIONS BU-44): a conditional slot, the outer Origin of a pair layer of `whole`'s binder, is a use only in that
    // binder's borrow cases: that binder when it admits a borrow and `schema` lists it, its strongest class in every case where the schema
    // lists no such binder (a function's or accessor's use of its Container's), and no use where it admits no borrow.
    private SlotRequirement ConditionalUse(DeclarationSchema schema, BoundType whole)
    {
        var strongest = this.ClassRange(whole).Strongest;
        var ordinal = strongest == LoanRequirement.None ? -1 : PairOrdinal(schema, whole);
        return ordinal < 0 ? new(strongest, default) : new(LoanRequirement.None, this.SingleBinder(ordinal));
    }

    // The binder set of one ordinal (BinderSet.Of), its words beyond 63 one array per ordinal for this Binding.
    private BinderSet SingleBinder(int ordinal)
    {
        var index = ordinal - 64;
        if (index >= this.binderSingles.Length)
        {
            Array.Resize(ref this.binderSingles, Math.Max(index + 1, 2 * this.binderSingles.Length));
        }

        return BinderSet.Of(ordinal, index < 0 ? null : (this.binderSingles[index] ??= new ulong[(index / 64) + 1]));
    }

    // A nested slot's requirement as a use in `outer` (SPEC 15.3.5): its unconditional part, and per binder of it the instance's argument:
    // a pair layer is a conditional use of its own binder (ConditionalUse), any other its strongest class (none for a Type parameter or
    // projection, which selects no case).
    private SlotRequirement NestedUse(SlotRequirement nested, BoundType instance, DeclarationSchema outer)
    {
        var use = new SlotRequirement(nested.Unconditional, default);
        for (var ordinal = nested.Binders.Next(0); ordinal >= 0; ordinal = nested.Binders.Next(ordinal + 1))
        {
            var argument = Selecting(instance.Symbol!.Schema!, ordinal, instance);
            var part = TryPairLayer(argument, out var whole, out _) ? this.ConditionalUse(outer, whole) : new(this.ClassRange(argument).Strongest, default);
            use = new(Max(use.Unconditional, part.Unconditional), use.Binders.Union(part.Binders, ref this.binderScratch, out _));
        }

        return use; // Its binders may live in binderScratch until the next NestedUse: AccumulateRequirements reads them first.
    }

    // The requirement over the cases `instance` selects of the binders in `requirement`, ordinals of `schema` (RequirementAt): the unconditional
    // part joined per binder with the classes of its selected cases; an argument selecting none leaves no case, which claims nothing,
    // neither Phantom nor `uniq` (Uniq, None).
    private (LoanRequirement Weakest, LoanRequirement Strongest) RequirementOf(SlotRequirement requirement, DeclarationSchema schema, BoundType? instance)
    {
        var weakest = requirement.Unconditional;
        var strongest = weakest;
        for (var ordinal = requirement.Binders.Next(0); ordinal >= 0; ordinal = requirement.Binders.Next(ordinal + 1))
        {
            var (least, most) = this.ClassRange(Selecting(schema, ordinal, instance));
            if (least > most)
            {
                return (LoanRequirement.Uniq, LoanRequirement.None);
            }

            (weakest, strongest) = (Max(weakest, least), Max(strongest, most));
        }

        return (weakest, strongest);
    }

    // The Semantics cases an argument of a pair slot selects (SPEC 8.15.2), by the Semantics of the binder naming each (`binder`): a pair
    // layer's admitted set and a family's, by their pair binder; a concrete Type's own Semantics, by none; no case for a Type parameter or
    // projection, which never passes formation. Each case takes the Semantics Taken gives.
    private SemanticsMask SelectedCases(BoundType argument, out BindingSymbol? binder)
    {
        binder = argument.Symbol;
        if (argument.Kind == BoundTypeKind.SemanticsAdaptation)
        {
            return (SemanticsMask)argument.Length;
        }

        if (TryPairLayer(argument, out _, out _))
        {
            return this.LayerAdmitted(argument);
        }

        binder = null;
        return argument.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.AssociatedProjection
            ? SemanticsMask.None : CaseSemantics(argument);
    }

    // The weakest and strongest Loan class over the cases an argument selects (SelectedCases, Taken); over no case (uniq, none).
    private (LoanRequirement Weakest, LoanRequirement Strongest) ClassRange(BoundType argument)
    {
        var cases = this.SelectedCases(argument, out _);
        var weakest = LoanRequirement.Uniq;
        var strongest = LoanRequirement.None;
        foreach (var mode in SemanticsOrder)
        {
            if ((cases & mode.ToMask()) != 0)
            {
                var loan = ClassOf(Taken(argument, mode));
                (weakest, strongest) = (Min(weakest, loan), Max(strongest, loan));
            }
        }

        return (weakest, strongest);
    }

    // SPEC 23.1.2, 15.3.5 (DECISIONS BU-44): the Semantics cases a slot's requirement is `failing` in at an instance, its failure's typed
    // fact: per binder of it that the instance leaves open (SelectedCases: a pair layer's or a family's, or the declaring binder with no
    // instance), its cases of that class where they are some but not all of its cases; none when every case fails, as the concrete twin's
    // record names none. Formed only on failure.
    private PairCase[] FailingCase(OriginParameter slot, BoundType? instance, LoanRequirement failing)
    {
        var (weakest, strongest) = this.RequirementAt(slot, instance);
        var schema = ListingSchema(slot, instance);
        var cases = new List<PairCase>();
        var binders = weakest == strongest ? default : slot.Requirement.Binders;
        for (var ordinal = binders.Next(0); ordinal >= 0; ordinal = binders.Next(ordinal + 1))
        {
            var argument = Selecting(schema, ordinal, instance);
            var modes = this.SelectedCases(argument, out var binder);
            var start = cases.Count;
            foreach (var mode in SemanticsOrder)
            {
                if (binder is not null && (modes & mode.ToMask()) != 0 && ClassOf(Taken(argument, mode)) == failing)
                {
                    cases.Add(new(binder, mode));
                }
            }

            if (cases.Count - start == BitOperations.PopCount((uint)modes))
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
