// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

public sealed partial class OwnershipBody
{
    private readonly Dictionary<int, ReferentSet> referentCache = new();
    private readonly List<int> referentTargets = new();
    private readonly List<(int Operation, int Place)> referentWork = new();
    private readonly HashSet<(int Operation, int Place)> referentVisited = new();

    private readonly record struct ReferentSet(int Start, int Count, bool Unknown);

    // Resolve current reference values against the shared CFG predecessor index. Earlier overwritten values do not
    // contribute targets. Branches and back edges keep every reaching target; unknown external storage stays conservative.
    // Cache each queried value for this analysis pass and retain the scratch capacity across passes.
    private ReferentSet CurrentReferents(int pointer)
    {
        if (this.referentCache.TryGetValue(pointer, out var cached))
        {
            return cached;
        }

        var start = this.referentTargets.Count;
        var unknown = false;
        this.referentWork.Clear();
        this.referentVisited.Clear();
        Add(pointer, -1);
        while (this.referentWork.Count != 0)
        {
            var (id, place) = this.referentWork[^1];
            this.referentWork.RemoveAt(this.referentWork.Count - 1);
            if ((uint)id >= (uint)this.Operations.Count)
            {
                unknown = true;
                continue;
            }

            if (place >= 0)
            {
                if (this.contentHeads[id] < 0)
                {
                    unknown = true;
                }

                for (var edge = this.contentHeads[id]; edge >= 0; edge = this.contentPredecessors[edge].Next)
                {
                    var from = this.contentPredecessors[edge].From;
                    Add(from, this.ReferenceDefinition(from) == place ? -1 : place);
                }

                continue;
            }

            var operation = this.Operations[id];
            var node = this.Values[id];
            if (operation.Kind == OwnershipOperationKind.Borrow && node.Kind == OwnershipValueKind.Address &&
                operation.Place >= 0 && operation.Input >= 0 && this.Places[operation.Input].Type.Components is [var addressed] &&
                ReferenceTypes.StorageMatches(addressed, this.Places[operation.Place].Type))
            {
                var target = operation.Place;
                if (this.referentTargets.IndexOf(target, start) < 0)
                {
                    this.referentTargets.Add(target);
                }
            }
            else if (node.Kind is OwnershipValueKind.Alias or OwnershipValueKind.Address && node.Count == 1)
            {
                Add(this.ValueOperands[node.Start], -1);
            }
            else if (node.Kind == OwnershipValueKind.Phi)
            {
                for (var i = 0; i < node.Count; i++)
                {
                    Add(this.PhiInputs[node.Start + i].Value, -1);
                }
            }
            else if (operation.Kind is OwnershipOperationKind.Read or OwnershipOperationKind.CallEntry or OwnershipOperationKind.Consume && operation.Place >= 0)
            {
                Add(id, operation.Place);
            }
            else if (operation.Kind is OwnershipOperationKind.Write or OwnershipOperationKind.InitializeSubject or OwnershipOperationKind.PayloadPlacement && operation.Input >= 0)
            {
                Add(id, operation.Input);
            }
            else
            {
                // A call's input-derived result establishes ancestry, not identity with the complete input storage.
                unknown = true;
            }
        }

        var result = new ReferentSet(start, this.referentTargets.Count - start, unknown);
        this.referentCache.Add(pointer, result);
        return result;

        void Add(int operation, int place)
        {
            if (this.referentVisited.Add((operation, place)))
            {
                this.referentWork.Add((operation, place));
            }
        }
    }

    private int ReferenceDefinition(int id) => this.Operations[id] switch
    {
        { Kind: OwnershipOperationKind.Write, Place: >= 0 } write => write.Place,
        { IsWholeUpdate: true, Place: >= 0 } update => update.Place,
        { Kind: OwnershipOperationKind.Consume, Input: >= 0, Acquisition: AcquisitionKind.Move } moved when ReferenceTypes.IsBorrow(this.Places[moved.Input].Type) => moved.Input,
        { Kind: OwnershipOperationKind.Borrow, Input: >= 0 } borrow => borrow.Input,
        { Kind: OwnershipOperationKind.Produce, Place: >= 0 } produce when this.Values[id] is { Kind: OwnershipValueKind.Alias, Count: 1 } => produce.Place,
        { Kind: OwnershipOperationKind.InitializeSubject, Place: >= 0 } subject => subject.Place,
        { Kind: OwnershipOperationKind.AcquirePattern, Input: >= 0 } binding => binding.Input,
        { Kind: OwnershipOperationKind.Produce, Place: >= 0 } item when this.Values[id].Kind == OwnershipValueKind.Sequence => item.Place,
        { Kind: OwnershipOperationKind.PayloadPlacement, Place: >= 0 } placement => placement.Place,
        _ => -1,
    };
}
