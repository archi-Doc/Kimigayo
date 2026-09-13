// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private readonly List<ConversionPlan> conversions = new();
    private int[] conversionIndices = [];
    private int[] physicalValues = [];

    private static ConversionPlan PlanConversion(BoundType source, BoundType target, int pointerWidth)
    {
        var sourceWidth = ScalarTypes.Width(source, pointerWidth);
        var targetWidth = ScalarTypes.Width(target, pointerWidth);
        var sourceSigned = ScalarTypes.Signed(source);
        var targetSigned = ScalarTypes.Signed(target);
        var sourceMin = sourceSigned ? -((Int128)1 << (sourceWidth - 1)) : 0;
        var targetMin = targetSigned ? -((Int128)1 << (targetWidth - 1)) : 0;
        var sourceMax = ((Int128)1 << (sourceWidth - (sourceSigned ? 1 : 0))) - 1;
        var targetMax = ((Int128)1 << (targetWidth - (targetSigned ? 1 : 0))) - 1;
        return new(
            sourceWidth == targetWidth ? null : sourceWidth > targetWidth ? "trunc" : sourceSigned ? "sext" : "zext",
            sourceMin < targetMin ? "slt" : null,
            sourceMax > targetMax ? sourceSigned ? "sgt" : "ugt" : null,
            ScalarTypes.Normalize(unchecked((long)targetMin), sourceWidth),
            ScalarTypes.Normalize(unchecked((long)targetMax), sourceWidth));
    }

    // Semantic aliases remain separate: a conversion's Type and dominance identity
    // are checked before resolving the physical bits used by every IR consumer.
    private EmissionOperand PhysicalOperand(OwnershipBody body, int id) => Operand(body, this.physicalValues[id]);

    private void PrepareConversions(OwnershipBody body)
    {
        var count = body.Values.Count;
        Grow(ref this.physicalValues, count);
        this.conversions.Clear();

        for (var id = 0; id < count; id++)
        {
            var kind = body.Values[id].Kind;
            var plan = kind == OwnershipValueKind.Convert
                ? PlanConversion(ValueType(body, Input(body, id, 0))!, ValueType(body, id)!, this.pointerWidth) : default;
            if (kind == OwnershipValueKind.Convert)
            {
                Grow(ref this.conversionIndices, count);
                this.conversionIndices[id] = this.conversions.Count;
                this.conversions.Add(plan);
            }

            this.checks[id] = ClassifyCheck(body.Values[id], ValueType(body, id), plan);
            var input = kind is OwnershipValueKind.Alias or OwnershipValueKind.Convert ? Input(body, id, 0) : -1;
            this.physicalValues[id] = (uint)input < (uint)id && ((kind == OwnershipValueKind.Alias && IsScalar(ValueType(body, input))) || (kind == OwnershipValueKind.Convert && plan.Operator is null))
                ? this.physicalValues[input] : id;
        }
    }

    private bool LowerConversion(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, out string? failure)
    {
        failure = null;
        var plan = this.conversions[this.conversionIndices[id]];
        var check = this.checks[id];
        if (plan.Operator is null && check == ArithmeticCheckKind.None)
        {
            return true;
        }

        var location = -1;
        if (check != ArithmeticCheckKind.None && !this.TryGetLocation(body.Operations[id].Source, directory, constants, out location))
        {
            return Fail("Integer conversion check has no source location.", out failure);
        }

        var input = Input(body, id, 0);
        var start = function.Operands.Count;
        function.Operands.Add(this.PhysicalOperand(body, input));
        function.Operands.Add(new(EmissionOperandKind.Integer, plan.Lower));
        function.Operands.Add(new(EmissionOperandKind.Integer, plan.Upper));
        function.Instructions.Add(new(EmissionOpcode.Convert, id, Place: body.Operations.Count + id, Constant: location, OperandStart: start, OperandCount: 3, ScalarType: WindowsLowering.GetValue(ValueType(body, id)!)!.ComputationType, ScalarOperator: plan.Operator, Check: check, Representation: WindowsLowering.GetValue(ValueType(body, input)!), LowerPredicate: plan.LowerPredicate, UpperPredicate: plan.UpperPredicate));
        return true;
    }

    private readonly record struct ConversionPlan(string? Operator, string? LowerPredicate, string? UpperPredicate, long Lower, long Upper)
    {
        internal bool Checked => this.LowerPredicate is not null || this.UpperPredicate is not null;
    }
}
