// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private readonly Dictionary<FormattingKoto, int> formattingPlaces = new(ReferenceEqualityComparer.Instance);

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
