// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<Koto, OriginRequirementWork> originRequirementNodes = new(ReferenceEqualityComparer.Instance);
    private readonly List<OriginRequirementWork> activeOriginRequirements = new();
    private readonly Queue<OriginRequirementWork> originRequirementQueue = new();
    private uint originRequirementPass;
    private uint originRequirementConsumer;

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
                schema.Origins[0].LoanRequirement = LoanRequirement.Ref;
                schema.GenericSlots[0].OriginVariance = OriginVariance.Covariant;
            }

            if (node.BoundSymbol?.LibraryDeclaration is KimiDeclarationId.FixedBuffer or KimiDeclarationId.WriteWindow or KimiDeclarationId.Utf8Writer && schema.Origins.Count == 1)
            {
                schema.Origins[0].Variance = OriginVariance.Covariant;
                schema.Origins[0].LoanRequirement = LoanRequirement.Uniq;
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
        // invariant and propagate that fact through all consumers using the same queue.
        for (var phase = 0; phase < 2; phase++)
        {
            if (phase == 1)
            {
                foreach (var item in this.activeOriginRequirements)
                {
                    for (var i = 0; i < item.Schema.Origins.Count; i++)
                    {
                        var origin = item.Schema.Origins[i];
                        if (origin.Variance == OriginVariance.Unused)
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
        else if (work.Owner is DeclarationContainerKoto container && this.storageShapes.TryGetValue(container, out var shape))
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
