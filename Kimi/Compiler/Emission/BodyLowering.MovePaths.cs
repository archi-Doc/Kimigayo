// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private readonly List<PartDestruction> partDestructions = new();
    private int[] partStarts = [];
    private int[] partFlags = [];

    internal static bool ValidatePathFlags(OwnershipBody body, EmissionFunction function, Span<int> seen, ReadOnlySpan<byte> execution = default)
    {
        if (seen.Length < body.Operations.Count)
        {
            return false;
        }

        foreach (var instruction in function.Instructions)
        {
            if (instruction.Opcode == EmissionOpcode.DestroyPart && instruction.Place >= 0 && !function.PathFlags.Contains(instruction.Place))
            {
                return false;
            }

            if (instruction.Opcode == EmissionOpcode.StorePathFlag &&
                ((uint)instruction.Operation >= (uint)body.Operations.Count || !function.PathFlags.Contains(instruction.Place)))
            {
                return false;
            }
        }

        for (var i = 0; i < function.PathFlags.Count; i++)
        {
            var path = function.PathFlags[i];
            if ((uint)path >= (uint)body.MovePathCount || function.PathFlags.IndexOf(path) != i || !body.GetMovePath(path).HasRemainder)
            {
                return false;
            }

            seen.Clear();
            foreach (var instruction in function.Instructions)
            {
                if (instruction.Opcode != EmissionOpcode.StorePathFlag || instruction.Place != path)
                {
                    continue;
                }

                var id = instruction.Operation;
                if (seen[id] != 0 || instruction.Constant < 0 || instruction.Constant != PathFlagTransition(body, body.Operations[id], path) ||
                    (!execution.IsEmpty && (execution[id] & NormalMark) == 0))
                {
                    return false;
                }

                seen[id] = 1;
            }

            for (var id = 0; id < body.Operations.Count; id++)
            {
                if ((execution.IsEmpty || (execution[id] & NormalMark) != 0) && PathFlagTransition(body, body.Operations[id], path) >= 0 && seen[id] == 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static int PathFlagTransition(OwnershipBody body, OwnershipOperation operation, int path)
    {
        if (operation.Projection >= 0 && operation.Kind is OwnershipOperationKind.Produce or OwnershipOperationKind.WriteElement &&
            body.Projections[operation.Projection].Path == operation.Projection && body.IsPathWithin(path, body.ProjectionPath(operation.Projection)))
        {
            return operation.Kind == OwnershipOperationKind.WriteElement ? 1 : operation.Acquisition == AcquisitionKind.Move ? 0 : -1;
        }

        return operation.Place != body.GetMovePath(path).Root ? -1 : operation.Kind switch
        {
            OwnershipOperationKind.Write => 1,
            // PrepareSlotFunctions separately verifies the signature's entry Produce.
            OwnershipOperationKind.Produce when operation.Projection == -1 && body.Places[operation.Place].Kind == OwnershipPlaceKind.Parameter &&
                ReferenceEquals(operation.Source, body.Places[operation.Place].Source) => 1,
            OwnershipOperationKind.Declare or OwnershipOperationKind.Cleanup or OwnershipOperationKind.Deliver or OwnershipOperationKind.CallEntry or OwnershipOperationKind.StorePointer => 0,
            OwnershipOperationKind.Consume or OwnershipOperationKind.Read or OwnershipOperationKind.Borrow when operation.Acquisition == AcquisitionKind.Move => 0,
            _ => -1,
        };
    }

    private void PreparePartDestruction(OwnershipBody body, EmissionFunction function)
    {
        this.partDestructions.Clear();
        Grow(ref this.partStarts, body.Operations.Count + 1);
        Grow(ref this.partFlags, body.MovePathCount);
        this.partFlags.AsSpan(0, body.MovePathCount).Clear();
        for (var id = 0; id < body.Operations.Count; id++)
        {
            this.partStarts[id] = this.partDestructions.Count;
            var operation = body.Operations[id];
            if (operation.Kind == OwnershipOperationKind.Cleanup && operation.Place >= 0 && this.IsStackFormattingBuffer(body.Places[operation.Place]))
            {
                this.continuations[id] = -1;
                continue;
            }

            var path = this.DestructionPath(body, operation);
            if (path >= 0 && body.IsReachable(id))
            {
                body.LoadPathInput(id);
                this.CollectPartDestruction(body, path, this.PathOffset(body, path));
                if (this.partDestructions.Count != this.partStarts[id] &&
                    !(this.partDestructions.Count == this.partStarts[id] + 1 && this.partDestructions[this.partStarts[id]] is { Conditional: false, Offset: 0, Count: 1 }))
                {
                    this.continuations[id] = body.Operations.Count + id;
                }
                else
                {
                    this.continuations[id] = -1;
                }
            }
        }

        this.partStarts[body.Operations.Count] = this.partDestructions.Count;
        for (var path = 0; path < body.MovePathCount; path++)
        {
            if (this.partFlags[path] != 0)
            {
                function.PathFlags.Add(path);
            }
        }
    }

    private int DestructionPath(OwnershipBody body, OwnershipOperation operation)
        => operation.Kind is OwnershipOperationKind.Cleanup or OwnershipOperationKind.Write && operation.Place >= 0
            ? body.MoveRoot(operation.Place)
            : operation.Kind == OwnershipOperationKind.WriteElement && body.Projections[operation.Projection].Path == operation.Projection
                ? body.ProjectionPath(operation.Projection) : -1;

    private int PathOffset(OwnershipBody body, int path)
    {
        var offset = 0;
        for (var node = body.GetMovePath(path); node.Parent >= 0; node = body.GetMovePath(node.Parent))
        {
            offset = checked(offset + this.aggregateLayouts.Get(body.GetMovePath(node.Parent).Type)!.Offset(node.Selector));
        }

        return offset;
    }

    private void CollectPartDestruction(OwnershipBody body, int path, int offset)
    {
        var node = body.GetMovePath(path);
        var layout = this.aggregateLayouts.Get(node.Type);
        if (node.Child < 0 || (body.CurrentPathState(path) & PlaceState.MustInit) != 0)
        {
            this.AddPartDestruction(body, path, offset, 1, layout?.Value ?? WindowsLowering.GetValue(node.Type)!, layout);
            return;
        }

        var end = node.Count;
        for (var child = node.Child; ; child = body.GetMovePath(child).Next)
        {
            var selector = child < 0 ? -1 : body.GetMovePath(child).Selector;
            if (layout!.IsArray)
            {
                if (end > selector + 1)
                {
                    this.AddPartDestruction(body, path, offset + layout.Offset(selector + 1), end - selector - 1, layout.Fields[0], layout.Children[0]);
                }
            }
            else
            {
                for (var field = end - 1; field > selector; field--)
                {
                    this.AddPartDestruction(body, path, offset + layout.Offset(field), 1, layout.Fields[field], layout.Children[field]);
                }
            }

            if (child < 0)
            {
                break;
            }

            this.CollectPartDestruction(body, child, offset + layout.Offset(selector));
            end = selector;
        }

        if (layout?.Base is { } parent)
        {
            this.AddPartDestruction(body, path, offset, 1, parent.Value, parent);
        }
    }

    private void AddPartDestruction(OwnershipBody body, int path, int offset, int count, ValueLowering value, AggregateLayout? aggregate)
    {
        if (!ReferenceEquals(value, WindowsLowering.String) && aggregate?.NeedsDestruction != true)
        {
            return;
        }

        var state = body.CurrentRemainderState(path);
        if ((state & PlaceState.MayInit) == 0)
        {
            return;
        }

        var conditional = (state & PlaceState.MustInit) == 0;
        this.partFlags[path] |= conditional ? 1 : 0;
        this.partDestructions.Add(new(path, offset, count, value.Layout.Stride, aggregate, conditional));
    }

    private bool LowerPartDestruction(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, out string? failure)
    {
        failure = null;
        var start = this.partStarts[id];
        var end = this.partStarts[id + 1];
        if (start == end)
        {
            return true;
        }

        if (!this.TryGetLocation(body.Operations[id].Source, directory, constants, out var location))
        {
            return Fail("Partial destruction requires a source location.", out failure);
        }

        for (var i = start; i < end; i++)
        {
            var part = this.partDestructions[i];
            if (end == start + 1 && part is { Conditional: false, Offset: 0, Count: 1 })
            {
                AddOwnedDestruction(function, id, new(EmissionOperandKind.SlotAddress, body.GetMovePath(part.Path).Root), location, part.Aggregate);
                return true;
            }

            var operands = function.Operands.Count;
            function.Operands.Add(new(EmissionOperandKind.SlotAddress, body.GetMovePath(part.Path).Root));
            function.Operands.Add(new(EmissionOperandKind.Integer, part.Offset));
            function.Operands.Add(new(EmissionOperandKind.Integer, part.Count));
            function.Operands.Add(new(EmissionOperandKind.Integer, part.Stride));
            function.Instructions.Add(new(EmissionOpcode.DestroyPart, id, Place: part.Conditional ? part.Path : -1, Constant: location, OperandStart: operands, OperandCount: 4, Aggregate: part.Aggregate, Continuation: i));
        }

        function.Add(EmissionOpcode.EndPartDestruction, id, constant: this.continuations[id]);
        return true;
    }

    private void AddPathFlags(OwnershipBody body, EmissionFunction function, int id)
    {
        foreach (var path in function.PathFlags)
        {
            var value = PathFlagTransition(body, body.Operations[id], path);
            if (value >= 0)
            {
                function.Add(EmissionOpcode.StorePathFlag, id, path, value);
            }
        }
    }

    private readonly record struct PartDestruction(int Path, int Offset, int Count, int Stride, AggregateLayout? Aggregate, bool Conditional);
}
