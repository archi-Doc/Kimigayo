// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<Koto, OriginRequirementWork> originRequirementNodes = new(ReferenceEqualityComparer.Instance);
    private readonly List<OriginRequirementWork> activeOriginRequirements = new();
    private readonly Queue<OriginRequirementWork> originRequirementQueue = new();
    private readonly HashSet<(OriginRequirementWork Source, OriginRequirementWork Target)> originRequirementEdges = new();

    private void ComputeOriginRequirements()
    {
        this.activeOriginRequirements.Clear();
        this.originRequirementQueue.Clear();
        this.originRequirementEdges.Clear();
        for (var i = 0; i < this.nodes.Count; i++)
        {
            var node = this.nodes[i];
            if (node is not (FunctionKoto or DeclarationContainerKoto) || node.BoundSymbol?.Schema is not { } schema)
            {
                continue;
            }

            if (schema.Origins.Count == 0 && schema.GenericSlots.Count == 0)
            {
                continue;
            }

            if (!this.originRequirementNodes.TryGetValue(node, out var work))
            {
                this.originRequirementNodes.Add(node, work = new(node, schema));
            }

            work.Schema = schema;
            work.Dependents.Clear();
            work.Queued = true;
            this.activeOriginRequirements.Add(work);
            this.originRequirementQueue.Enqueue(work);
        }

        for (var i = 0; i < this.activeOriginRequirements.Count; i++)
        {
            this.VisitRequirementTypes(this.activeOriginRequirements[i], collectEdges: true);
        }

        // Only changed summaries wake their consumers; long declaration chains do not rescan the tree.
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
        if (type.Symbol?.Declaration is { } declaration && this.originRequirementNodes.TryGetValue(declaration, out var source) && this.originRequirementEdges.Add((source, consumer)))
        {
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

        internal bool Queued { get; set; }
    }
}
