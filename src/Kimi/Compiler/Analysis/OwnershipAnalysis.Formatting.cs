// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private readonly Dictionary<FormattingKoto, int> formattingPlaces = new(ReferenceEqualityComparer.Instance);

    private int TryWrite(BoundFormatting plan)
    {
        var acquisition = plan.Acquisition!;
        var depth = this.comparisonDepth++;
        var reference = this.PrepareCallArgument(acquisition, acquisition.ArgumentNodes[0], acquisition.BoundCall!.ArgumentOperations[0], immediate: true);
        this.formattingPlaces[plan.Writer] = reference;
        var region = this.checkingRegion;
        var mark = this.terminalSeeds.Count;
        var join = this.New(OwnershipOperationKind.Branch, plan.Root);
        var completes = reference >= 0;
        foreach (var write in plan.Writes)
        {
            if (!completes)
            {
                var partDepth = this.comparisonDepth++;
                this.PrepareCallArgument(write, write.ArgumentNodes[1], write.BoundCall!.ArgumentOperations[1]);
                this.EndComparisonLoans(partDepth, write);
                this.comparisonDepth = partDepth;
                continue;
            }

            var status = this.Temporary(plan.Check);
            this.SetValue(this.Value(status), OwnershipValueKind.Formatting, [this.Value(reference)]);
            var branch = this.Emit(OwnershipOperationKind.Branch, plan.Check);
            this.SetValue(branch, OwnershipValueKind.Alias, [this.Value(status)]);
            var next = this.New(OwnershipOperationKind.Branch, write);
            this.Connect(branch, join, OwnershipEdgeKind.False);
            this.Connect(branch, next, OwnershipEdgeKind.True);
            this.current = next;
            completes = this.Call(write) >= 0;
        }

        if (completes)
        {
            this.Connect(this.current, join);
        }

        this.current = reference >= 0 ? join : -1;
        this.checkingRegion = region;
        this.FilterTerminalSeeds(plan.Root, mark);
        var result = reference >= 0 ? this.Call(plan.Outcome) : -1;
        this.EndComparisonLoans(depth, plan.Root);
        this.comparisonDepth = depth;
        return result;
    }

    private int Formatting(BoundFormatting plan)
    {
        var buffer = this.Call(plan.Heap);
        this.formattingPlaces[plan.BufferOwner] = buffer;
        this.formattingPlaces[plan.Buffer] = this.BorrowStruct(plan.BufferOwner, plan.Buffer.BoundType!);
        var writer = this.Call(plan.Adapter);
        this.formattingPlaces[plan.WriterOwner] = writer;
        var reference = this.BorrowStruct(plan.WriterOwner, plan.Writer.BoundType!);
        this.formattingPlaces[plan.Writer] = reference;
        var completes = true;
        for (var i = 0; i < plan.Writes.Count; i++)
        {
            var write = plan.Writes[i];
            if (!completes)
            {
                // Retain ownership diagnostics in dead source, without accessing
                // the private adapter already destroyed by an escaping transfer.
                var depth = this.comparisonDepth++;
                this.PrepareCallArgument(write, write.ArgumentNodes[1], write.BoundCall!.ArgumentOperations[1]);
                this.EndComparisonLoans(depth, write);
                this.comparisonDepth = depth;
                continue;
            }

            var hint = this.Temporary(plan.Hint);
            this.SetValue(this.Value(hint), OwnershipValueKind.Formatting, [this.Value(reference)], constant: i);
            completes = this.Call(write) >= 0;
            if (!completes)
            {
                continue;
            }

            var check = this.Temporary(plan.Check);
            this.SetValue(this.Value(check), OwnershipValueKind.Formatting, [this.Value(reference)]);
        }

        if (!completes)
        {
            return -1;
        }

        var result = this.Temporary(plan.Finish);
        this.body.PlaceStorage[result] = this.body.Places[result] with { Source = plan.Root };
        this.SetValue(this.Value(result), OwnershipValueKind.Formatting, [this.Value(reference)], constant: buffer);
        return result;
    }

    private int FormattingValue(FormattingKoto node)
    {
        if (node.Operation == FormattingOperation.Storage && this.formattingPlaces.TryGetValue(node, out var place))
        {
            return place;
        }

        var result = this.Temporary(node);
        this.SetValue(this.Value(result), OwnershipValueKind.Formatting, []);
        return result;
    }
}
