// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private readonly Dictionary<BoundFormatting, FormattingEstimate> formattingEstimates = new(ReferenceEqualityComparer.Instance);
    private readonly List<FormattingEstimate> formattingEstimateStorage = new();

    private bool LowerFormatting(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, int id, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        var value = body.Values[id];
        if (operation.Kind != OwnershipOperationKind.Produce || operation.Source is not FormattingKoto { Plan: { } plan } syntax ||
            !ReferenceEquals(plan.Root.Formatting, plan) || !ReferenceEquals(ValueType(body, id), body.Resolve(syntax.BoundType, body.ContextAt(id))))
        {
            return Fail("Formatting requires its bound root plan and matching result storage.", out failure);
        }

        this.formattingRuntimeUsed = true;
        switch (syntax.Operation)
        {
            case FormattingOperation.Status when value.Count == 1 && ReferenceEquals(ValueType(body, id), BoundType.Boolean):
                function.AddCall(id, LegacyRuntimeHelpers.FormatHealthy, [this.PhysicalOperand(body, Input(body, id, 0))]);
                return true;
            case FormattingOperation.Capacity when value.Count == 0 && ReferenceEquals(ValueType(body, id), BoundType.ISize):
                function.AddScalar(EmissionOpcode.Scalar, id, [new(EmissionOperandKind.Integer, 0), new(EmissionOperandKind.Integer, this.EstimateFormatting(plan).Capacity)], type: "i64", op: "add");
                return true;
            case FormattingOperation.Hint when value.Count == 1 && ReferenceEquals(ValueType(body, id), BoundType.Unit):
            case FormattingOperation.Check when value.Count == 1 && ReferenceEquals(ValueType(body, id), BoundType.Unit):
                var input = Input(body, id, 0);
                if (ValueType(body, input) is not { Semantics: SemanticsKind.Uniq, Components.Count: 1 } reference ||
                    reference.Components[0].Symbol?.LibraryDeclaration != KimiDeclarationId.Utf8Writer ||
                    (body.IsReachable(id) && !this.Dominates(input, id)) || !this.TryGetLocation(plan.Root, constants, out var location))
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

                    function.AddCall(id, LegacyRuntimeHelpers.FormatHint, [this.PhysicalOperand(body, input), new(EmissionOperandKind.Integer, hints[(int)value.Constant])]);
                }
                else
                {
                    function.AddCall(id, LegacyRuntimeHelpers.FormatCheck, [this.PhysicalOperand(body, input), new(EmissionOperandKind.ConstantAddress, location), new(EmissionOperandKind.ConstantLength, location)]);
                }

                return true;
            case FormattingOperation.Finish when value.Count == 1 && ReferenceEquals(ValueType(body, id), BoundType.String):
                var buffer = (int)value.Constant;
                if ((uint)buffer >= (uint)body.Places.Count || body.Places[buffer].Type.Symbol?.LibraryDeclaration != KimiDeclarationId.HeapBuffer ||
                    !ReferenceEquals(body.Places[buffer].Source, plan.Heap) ||
                    (body.IsReachable(id) && (body.GetInputState(id, buffer) & PlaceState.MustInit) == 0) ||
                    !this.TryGetLocation(plan.Root, constants, out var finishLocation))
                {
                    return Fail("Formatting completion requires its initialized private buffer.", out failure);
                }

                function.AddCall(id, this.EstimateFormatting(plan).Stack ? LegacyRuntimeHelpers.FormatStackFinish : LegacyRuntimeHelpers.FormatFinish, [new(EmissionOperandKind.SlotAddress, operation.Place), new(EmissionOperandKind.SlotAddress, buffer), this.PhysicalOperand(body, Input(body, id, 0)), new(EmissionOperandKind.ConstantAddress, finishLocation), new(EmissionOperandKind.ConstantLength, finishLocation)]);
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
        var sum = FormatEstimate.Empty; // The promoted rule (LegacyRuntimeHelpers), over the later writes.
        for (var i = hints.Length - 1; i >= 0; i--)
        {
            hints[i] = sum.Hint;
            var write = plan.Writes[i];
            sum = sum.Add(write.ArgumentNodes[1] is StringLiteralKoto text ? Encoding.UTF8.GetByteCount(text.Literal)
                : LegacyRuntimeHelpers.FormatBound(this.Resolve(write.BoundCall!.TypeArguments[0], InterpretationContext.Root)));
        }

        estimate.Capacity = sum.Capacity;
        estimate.Stack = sum.Stack(plan.Root is InterpolatedStringKoto &&
            plan.Root.Parent is InvocationKoto { BoundCall.Target.CompilerFunction: CompilerFunctionKind.WriteLine } console &&
            console.ArgumentNodes.Count == 1 && ReferenceEquals(console.ArgumentNodes[0], plan.Root));
        this.formattingEstimates.Add(plan, estimate);
        return estimate;
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
