// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private bool LowerDictionaryIteration(OwnershipBody body, EmissionFunction function, int id, OwnershipSequence plan, BoundType dictionary, bool borrowed, EmissionOperand address, out string? failure)
    {
        failure = null;
        var operation = body.Operations[id];
        var result = ValueType(body, id);
        if (operation.Source is not ForKoto loop || borrowed != (loop.SharedIterable is not null) || plan.Projection != -1 || plan.End != -1 ||
            !this.TryGetArrayElement(dictionary.Components[0], out var key, allowEmpty: true) ||
            !this.TryGetArrayElement(dictionary.Components[1], out var value, allowEmpty: true))
        {
            return Fail("Dictionary iteration requires its acquired handle and concrete entry layout.", out failure);
        }

        var helper = this.GetDictionaryHelper(DictionaryHelperKind.Clear, key, value);
        if (plan.Kind is SequenceOperation.Start or SequenceOperation.End)
        {
            if (plan.Index != -1 || plan.Element != -1 || !ReferenceEquals(result, BoundType.ISize))
            {
                return Fail("Dictionary cursor endpoints require an isize result.", out failure);
            }

            function.AddScalar(EmissionOpcode.Sequence, id, [address, new(EmissionOperandKind.Integer, 0)], op: plan.Kind == SequenceOperation.Start ? "DictionaryStart" : "DictionaryEnd");
            return true;
        }

        if ((uint)plan.Index >= (uint)id || !ReferenceEquals(ValueType(body, plan.Index), BoundType.ISize) ||
            (body.IsReachable(id) && !this.Dominates(plan.Index, id)))
        {
            return Fail("Dictionary iteration requires its dominating link cursor.", out failure);
        }

        var next = plan.Kind is SequenceOperation.DictionaryNext or SequenceOperation.DictionaryTakeNext;
        if (borrowed != (plan.Kind is SequenceOperation.DictionaryRead or SequenceOperation.DictionaryNext) ||
            (!next && plan.Kind != (borrowed ? SequenceOperation.DictionaryRead : SequenceOperation.DictionaryMoveRead)))
        {
            return Fail("Dictionary iteration acquisition does not match its shared or owning subject.", out failure);
        }

        string code;
        ValueLowering? representation = null;
        var offset = helper.KeyOffset;
        if (next)
        {
            if (plan.Element != -1 || !ReferenceEquals(result, BoundType.ISize))
            {
                return Fail("Dictionary advance must return its next link.", out failure);
            }

            code = borrowed ? "DictionaryNext" : "DictionaryTakeNext";
            this.dictionaryRuntimeUsed |= !borrowed;
            this.arrayRuntimeUsed |= !borrowed;
        }
        else if (plan.Element < 0)
        {
            if (loop.IsTupleBinding || result is not { Kind: BoundTypeKind.Tuple, Components.Count: 2 } ||
                !Matches(result.Components[0], key.Type, false) || !Matches(result.Components[1], value.Type, true) ||
                this.aggregateLayouts.Get(result) is not { } pair ||
                (borrowed ? pair.Offset(0) != 0 || pair.Offset(1) != 8 : pair.Offset(1) != helper.ValueOffset - helper.KeyOffset))
            {
                return Fail("Dictionary pair iteration requires its complete Tuple layout.", out failure);
            }

            representation = pair.Value;
            code = borrowed ? "DictionaryPair" : "DictionaryStorageRead";
        }
        else
        {
            if (!loop.IsTupleBinding || loop.Bindings.Count != 2 || (uint)plan.Element >= 2)
            {
                return Fail("Dictionary component iteration requires two Tuple bindings.", out failure);
            }

            var component = plan.Element == 0 ? key : value;
            if (result is null || !Matches(result, component.Type, plan.Element == 1))
            {
                return Fail("Dictionary component acquisition has a mismatched Type or Origin.", out failure);
            }

            offset = plan.Element == 0 ? helper.KeyOffset : helper.ValueOffset;
            representation = component.Value;
            code = borrowed ? "DictionaryAddress" : component.IsScalar ? "DictionaryRead" : "DictionaryStorageRead";
        }

        ReadOnlySpan<EmissionOperand> operands = [address, this.PhysicalOperand(body, plan.Index), new(EmissionOperandKind.Integer, helper.Stride), new(EmissionOperandKind.Integer, offset), new(EmissionOperandKind.Integer, helper.ValueOffset)];
        function.AddScalar(EmissionOpcode.Sequence, id, operands, place: operation.Place, op: code, representation: representation);
        return true;

        // SPEC 14.6.2: a borrowed key is ref/K; a borrowed value is ref/V for shared and uniq/V for exclusive enumeration.
        // The component is identified by its position, not its Type: K and V may be the same Type.
        bool Matches(BoundType actual, BoundType stored, bool value) => borrowed
            ? actual is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } &&
                (actual.Semantics == SemanticsKind.Ref || (value && body.Places[plan.Receiver].Type.Semantics == SemanticsKind.Uniq)) &&
                ReferenceEquals(actual.Components[0], stored) && ReferenceEquals(actual.Origin, body.Places[plan.Receiver].Type.Origin)
            : ReferenceEquals(actual, stored);
    }
}
