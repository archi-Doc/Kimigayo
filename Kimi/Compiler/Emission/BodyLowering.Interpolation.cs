// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private const int FormattingStackLimit = 1024;
    private static readonly FunctionAbi CheckFormatting = new("__kimi_format_check", "void", [new("ptr", "writer"), new("ptr", "location"), new("i64", "location_length")]);
    private static readonly FunctionAbi HintFormatting = new("__kimi_format_hint", "void", [new("ptr", "writer"), new("i64", "hint")]);
    private static readonly FunctionAbi StatusFormatting = new("__kimi_format_healthy", "i1", [new("ptr", "writer")]);
    private static readonly FunctionAbi FinishFormatting = new("__kimi_format_finish", "void", [new("ptr", "ret"), new("ptr", "buffer"), new("ptr", "writer"), new("ptr", "location"), new("i64", "location_length")]);
    private static readonly FunctionAbi FinishStackFormatting = new("__kimi_format_stack_finish", "void", FinishFormatting.Parameters);
    private static readonly FunctionAbi LiteralFormatting = new("__kimi_format_literal", "void", WindowsLowering.GetCompilerFunction(CompilerFunctionKind.WriterWrite)!.Parameters, resultSlot: true);
    private readonly Dictionary<BoundFormatting, FormattingEstimate> formattingEstimates = new(ReferenceEqualityComparer.Instance);
    private readonly List<FormattingEstimate> formattingEstimateStorage = new();

    private bool LowerFormatting(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        var value = body.Values[id];
        if (operation.Kind != OwnershipOperationKind.Produce || operation.Source is not FormattingKoto { Plan: { } plan } syntax ||
            !ReferenceEquals(plan.Root.Formatting, plan) || !ReferenceEquals(ValueType(body, id), SignatureType(this, syntax.BoundType)))
        {
            return Fail("Formatting requires its bound root plan and matching result storage.", out failure);
        }

        this.formattingRuntimeUsed = true;
        switch (syntax.Operation)
        {
            case FormattingOperation.Status when value.Count == 1 && ReferenceEquals(ValueType(body, id), BoundType.Boolean):
                function.AddCall(id, StatusFormatting, [this.PhysicalOperand(body, Input(body, id, 0))]);
                return true;
            case FormattingOperation.Capacity when value.Count == 0 && ReferenceEquals(ValueType(body, id), BoundType.ISize):
                function.AddScalar(EmissionOpcode.Scalar, id, [new(EmissionOperandKind.Integer, 0), new(EmissionOperandKind.Integer, this.EstimateFormatting(plan).Capacity)], type: "i64", op: "add");
                return true;
            case FormattingOperation.Hint when value.Count == 1 && ReferenceEquals(ValueType(body, id), BoundType.Unit):
            case FormattingOperation.Check when value.Count == 1 && ReferenceEquals(ValueType(body, id), BoundType.Unit):
                var input = Input(body, id, 0);
                if (ValueType(body, input) is not { Semantics: SemanticsKind.Uniq, Components.Count: 1 } reference ||
                    reference.Components[0].Symbol?.LibraryDeclaration != KimiDeclarationId.Utf8Writer ||
                    (body.IsReachable(id) && !this.Dominates(input, id)) || !this.TryGetLocation(plan.Root, directory, constants, out var location))
                {
                    return Fail("Formatting check requires its live exclusive adapter.", out failure);
                }

                if (syntax.Operation == FormattingOperation.Hint)
                {
                    var hints = this.EstimateFormatting(plan).Hints;
                    if ((uint)value.Constant >= (uint)hints.Length)
                    {
                        return Fail("Formatting hint has no matching write.", out failure);
                    }

                    function.AddCall(id, HintFormatting, [this.PhysicalOperand(body, input), new(EmissionOperandKind.Integer, hints[(int)value.Constant])]);
                }
                else
                {
                    function.AddCall(id, CheckFormatting, [this.PhysicalOperand(body, input), new(EmissionOperandKind.ConstantAddress, location), new(EmissionOperandKind.ConstantLength, location)]);
                }

                return true;
            case FormattingOperation.Finish when value.Count == 1 && ReferenceEquals(ValueType(body, id), BoundType.String):
                var buffer = (int)value.Constant;
                if ((uint)buffer >= (uint)body.Places.Count || body.Places[buffer].Type.Symbol?.LibraryDeclaration != KimiDeclarationId.HeapBuffer ||
                    !ReferenceEquals(body.Places[buffer].Source, plan.Heap) ||
                    (body.IsReachable(id) && (body.GetInputState(id, buffer) & PlaceState.MustInit) == 0) ||
                    !this.TryGetLocation(plan.Root, directory, constants, out var finishLocation))
                {
                    return Fail("Formatting completion requires its initialized private buffer.", out failure);
                }

                function.AddCall(id, this.EstimateFormatting(plan).Stack ? FinishStackFormatting : FinishFormatting, [new(EmissionOperandKind.SlotAddress, operation.Place), new(EmissionOperandKind.SlotAddress, buffer), this.PhysicalOperand(body, Input(body, id, 0)), new(EmissionOperandKind.ConstantAddress, finishLocation), new(EmissionOperandKind.ConstantLength, finishLocation)]);
                return true;
            default:
                return Fail("Unsupported formatting root operation.", out failure);
        }
    }

    private FormattingEstimate EstimateFormatting(BoundFormatting plan)
    {
        if (this.formattingEstimates.TryGetValue(plan, out var estimate))
        {
            return estimate;
        }

        var index = this.formattingEstimates.Count;
        if (index == this.formattingEstimateStorage.Count)
        {
            this.formattingEstimateStorage.Add(new());
        }

        estimate = this.formattingEstimateStorage[index];
        if (estimate.Hints.Length != plan.Writes.Count)
        {
            estimate.Hints = new long[plan.Writes.Count];
        }

        var hints = estimate.Hints;
        long total = 0;
        var bounded = true;
        for (var i = hints.Length - 1; i >= 0; i--)
        {
            hints[i] = total < 0 ? 0 : total;
            var write = plan.Writes[i];
            var amount = write.ArgumentNodes[1] is StringLiteralKoto text ? Encoding.UTF8.GetByteCount(text.Literal)
                : this.FormattingBound(SignatureType(this, write.BoundCall!.TypeArguments[0]));
            bounded &= amount >= 0;
            amount = Math.Max(0, amount);
            if (total < 0 || total > long.MaxValue - amount)
            {
                total = -1;
                continue;
            }

            total += amount;
        }

        var stack = bounded && total is >= 0 and <= FormattingStackLimit && plan.Root is InterpolatedStringKoto &&
            plan.Root.Parent is InvocationKoto { BoundCall.Target.CompilerFunction: CompilerFunctionKind.WriteLine } console &&
            console.ArgumentNodes.Count == 1 && ReferenceEquals(console.ArgumentNodes[0], plan.Root);
        estimate.Capacity = bounded && total >= 0 ? total : 0;
        estimate.Stack = stack;
        this.formattingEstimates.Add(plan, estimate);
        return estimate;
    }

    private int FormattingBound(BoundType? type)
    {
        if (type is null)
        {
            return -1;
        }

        var width = ScalarTypes.Width(type, this.pointerWidth);
        return width != 0 ? (width, ScalarTypes.Signed(type)) switch
        {
            (8, true) => 4, (8, false) => 3, (16, true) => 6, (16, false) => 5,
            (32, true) => 11, (32, false) => 10, (64, _) => 20, (128, true) => 40, (128, false) => 39,
            _ => -1,
        } : ReferenceEquals(type, BoundType.F32) ? 17 : ReferenceEquals(type, BoundType.F64) ? 24 :
            ReferenceEquals(type, BoundType.Char) ? 4 : ReferenceEquals(type, BoundType.Boolean) ? 5 : ReferenceEquals(type, BoundType.Unit) ? 2 : -1;
    }

    private bool IsStackFormattingBuffer(OwnershipPlace place)
        => place.Source is InvocationKoto { Parent.Formatting: { } plan } call && ReferenceEquals(call, plan.Heap) && this.EstimateFormatting(plan).Stack;

    private sealed class FormattingEstimate
    {
        internal long Capacity { get; set; }

        internal long[] Hints { get; set; } = [];

        internal bool Stack { get; set; }
    }
}
