// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

internal sealed partial class BodyLowering
{
    private readonly List<ConversionPlan> conversions = new();
    private int[] conversionIndices = [];
    private int[] physicalValues = [];

    // SPEC 4.6.8: positions resolved at compile time. A folded value is an isize constant that its consumers read in place of
    // the operation, which emits nothing.
    private bool[] folded = [];
    private Int128[] foldedValues = [];

    // SPEC 13.5.4.3: a wrapping conversion keeps the low bits or extends by the operand's signedness, without a check.
    internal static ConversionPlan PlanWrap(BoundType source, BoundType target, int pointerWidth)
    {
        var sourceWidth = ScalarTypes.Width(source, pointerWidth);
        var targetWidth = ScalarTypes.Width(target, pointerWidth);
        return new(sourceWidth == targetWidth ? null : sourceWidth > targetWidth ? "trunc" : ScalarTypes.Signed(source) ? "sext" : "zext", null, null, 0, 0);
    }

    internal static ConversionPlan PlanConversion(BoundType source, BoundType target, int pointerWidth)
    {
        if (ReferenceTypes.IsPointer(source) || ReferenceTypes.IsPointer(target))
        {
            // SPEC 5.4-5.5: one address space and a usize-wide address, so pointer casts, and the address of a borrow, keep the value.
            var address = ReferenceTypes.IsPointer(source) || ReferenceTypes.IsReference(source);
            return address == ReferenceTypes.IsPointer(target) ? default : new(address ? "ptrtoint" : "inttoptr", null, null, 0, 0);
        }

        if (FloatingTypes.Supports(source))
        {
            if (FloatingTypes.Supports(target))
            {
                // Halfway between the largest f32 and 2^128 rounds to infinity.
                const double Overflow = 3.40282356779733661637539395458142568448e38;
                return ReferenceEquals(source, target) ? default : ReferenceEquals(source, BoundType.F32)
                    ? new("fpext", null, null, 0, 0)
                    : new("fptrunc", "ole", "oge", BitConverter.DoubleToInt64Bits(-Overflow), BitConverter.DoubleToInt64Bits(Overflow));
            }

            var width = ScalarTypes.Width(target, pointerWidth);
            var signed = ScalarTypes.Signed(target);
            var exponent = width - (signed ? 1 : 0);
            var upper = Math.ScaleB(1.0, exponent);
            var lower = signed ? -upper : 0.0;
            var exactPredecessor = !signed || exponent < (ReferenceEquals(source, BoundType.F32) ? 24 : 53);
            // Check the mathematical truncated value before fptosi/fptoui. For
            // small targets (min-1, min) is legal; for large targets no source
            // float lies in that interval. Unordered predicates also reject NaN.
            if (exactPredecessor)
            {
                lower -= 1.0;
            }

            return new(signed ? "fptosi" : "fptoui", exactPredecessor ? "ule" : "ult", "uge", FloatingBits(source, lower), FloatingBits(source, upper));
        }

        if (FloatingTypes.Supports(target))
        {
            return new(ScalarTypes.Signed(source) ? "sitofp" : "uitofp", null, null, 0, 0);
        }

        var sourceWidth = ScalarTypes.Width(source, pointerWidth);
        var targetWidth = ScalarTypes.Width(target, pointerWidth);
        var sourceSigned = ScalarTypes.Signed(source);
        var targetSigned = ScalarTypes.Signed(target);
        var sourceMin = sourceSigned ? -((Int128)1 << (sourceWidth - 1)) : 0;
        var targetMin = targetSigned ? -((Int128)1 << (targetWidth - 1)) : 0;
        var sourceMax = UInt128.MaxValue >> (128 - sourceWidth + (sourceSigned ? 1 : 0));
        var targetMax = UInt128.MaxValue >> (128 - targetWidth + (targetSigned ? 1 : 0));
        return new(
            sourceWidth == targetWidth ? null : sourceWidth > targetWidth ? "trunc" : sourceSigned ? "sext" : "zext",
            sourceMin < targetMin ? "slt" : null,
            sourceMax > targetMax ? sourceSigned ? "sgt" : "ugt" : null,
            ScalarTypes.Normalize(targetMin, sourceWidth),
            ScalarTypes.Normalize(unchecked((Int128)targetMax), sourceWidth));
    }

    private static long FloatingBits(BoundType type, double value)
        => ReferenceEquals(type, BoundType.F32) ? BitConverter.SingleToUInt32Bits((float)value) : BitConverter.DoubleToInt64Bits(value);

    // Semantic aliases remain separate: a conversion's Type and dominance identity
    // are checked before resolving the physical bits used by every IR consumer.
    private EmissionOperand PhysicalOperand(OwnershipBody body, int id)
        => this.folded[id] ? new(EmissionOperandKind.Integer, this.foldedValues[id]) :
            this.referenceRoots[id] >= 0 && ReferenceTypes.IsString(ValueType(body, id)) ? this.ReferenceOperand(body, id) : Operand(body, this.physicalValues[id]);

    // Records that the value `id` is the isize constant `value`.
    private void Fold(int id, Int128 value)
    {
        this.folded[id] = true;
        this.foldedValues[id] = value;
    }

    private void PrepareConversions(OwnershipBody body)
    {
        var count = body.Values.Count;
        Grow(ref this.physicalValues, count);
        Grow(ref this.folded, count);
        Grow(ref this.foldedValues, count);
        this.folded.AsSpan(0, count).Clear();
        this.conversions.Clear();

        for (var id = 0; id < count; id++)
        {
            var kind = body.Values[id].Kind;
            var plan = kind != OwnershipValueKind.Convert ? default
                : body.Values[id].Constant == OwnershipValue.WrapConversion ? PlanWrap(ValueType(body, Input(body, id, 0))!, ValueType(body, id)!, this.pointerWidth)
                : body.Values[id].Constant == OwnershipValue.BitConversion ? new("bitcast", null, null, 0, 0) // SPEC 13.5.4.4: the same bits, no check.
                : body.Values[id].Constant == OwnershipValue.RawPlaceBorrow ? default // SPEC 5.2.2: the address itself.
                : PlanConversion(ValueType(body, Input(body, id, 0))!, ValueType(body, id)!, this.pointerWidth);
            if (kind == OwnershipValueKind.Convert && body.Values[id].Constant == OwnershipValue.PositionConversion && plan.Operator is null)
            {
                // SPEC 4.6.9: an unsigned position of the same width reinterpreted as isize is negative exactly when isize
                // cannot hold it, so the bounds check of its use rejects it without a conversion check.
                plan = default;
            }

            if (kind == OwnershipValueKind.Convert)
            {
                Grow(ref this.conversionIndices, count);
                this.conversionIndices[id] = this.conversions.Count;
                this.conversions.Add(plan);
            }

            this.checks[id] = this.LiteralOperandCheck(body, id, ClassifyCheck(body.Values[id], ValueType(body, id), plan));
            var input = kind is OwnershipValueKind.Alias or OwnershipValueKind.Convert ? Input(body, id, 0) : -1;
            if (kind == OwnershipValueKind.Convert && body.Values[id].Constant == OwnershipValue.PositionConversion && (uint)input < (uint)id &&
                (this.folded[input] ? new EmissionOperand(EmissionOperandKind.Integer, this.foldedValues[input]) : Operand(body, this.physicalValues[input])) is { Kind: EmissionOperandKind.Integer } position)
            {
                // SPEC 4.6.9: a literal position is converted at compile time; a value isize cannot hold becomes -1.
                var limit = this.pointerWidth == 32 ? (Int128)int.MaxValue : long.MaxValue;
                this.Fold(id, position.Value >= -limit - 1 && position.Value <= limit ? position.Value : -1);
                this.checks[id] = ArithmeticCheckKind.None;
                this.physicalValues[id] = id;
                continue;
            }

            this.physicalValues[id] = (uint)input < (uint)id && ((kind == OwnershipValueKind.Alias && IsScalar(ValueType(body, input))) || (kind == OwnershipValueKind.Convert && plan.Operator is null))
                ? this.physicalValues[input] : id;
        }
    }

    // IMPL 21.5.3: a check whose success follows from a literal right operand is not generated even at O0: the zero check of a
    // literal nonzero divisor, the minimum / -1 check of a quotient by a literal other than -1, and the count check of a literal
    // in-range shift count. An integer quotient by a literal -1 keeps its minimum check; the writer negates instead of dividing.
    private ArithmeticCheckKind LiteralOperandCheck(OwnershipBody body, int id, ArithmeticCheckKind check)
    {
        if (check is not (ArithmeticCheckKind.Division or ArithmeticCheckKind.DivisionZero or ArithmeticCheckKind.WrappingDivision or ArithmeticCheckKind.Shift))
        {
            return check;
        }

        var input = Input(body, id, 1);
        if ((uint)input >= (uint)body.Values.Count || body.Values[Definition(body, input)].Kind != OwnershipValueKind.Constant)
        {
            return check;
        }

        var constant = body.Values[Definition(body, input)].Constant;
        if (check == ArithmeticCheckKind.Shift)
        {
            return constant >= 0 && constant < ScalarTypes.Width(ValueType(body, Input(body, id, 0)), this.pointerWidth) ? ArithmeticCheckKind.None : check;
        }

        return constant == 0 || (constant == -1 && check == ArithmeticCheckKind.Division) ? check : ArithmeticCheckKind.None;
    }

    private bool LowerConversion(OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string directory, int id, out string? failure)
    {
        failure = null;
        var plan = this.conversions[this.conversionIndices[id]];
        var check = this.checks[id];
        if (this.folded[id] || (plan.Operator is null && check == ArithmeticCheckKind.None))
        {
            return true;
        }

        var location = -1;
        if (check != ArithmeticCheckKind.None && !this.TryGetLocation(body.Operations[id].Source, directory, constants, out location))
        {
            return Fail("Numeric conversion check has no source location.", out failure);
        }

        var input = Input(body, id, 0);
        var source = ValueType(body, input)!;
        var boundKind = ReferenceEquals(source, BoundType.F32) ? EmissionOperandKind.Float32 :
            ReferenceEquals(source, BoundType.F64) ? EmissionOperandKind.Float64 : EmissionOperandKind.Integer;
        var start = function.Operands.Count;
        function.Operands.Add(this.PhysicalOperand(body, input));
        function.Operands.Add(new(boundKind, plan.Lower));
        function.Operands.Add(new(boundKind, plan.Upper));
        function.Instructions.Add(new(EmissionOpcode.Convert, id, Place: body.Operations.Count + id, Constant: location, OperandStart: start, OperandCount: 3, ScalarType: WindowsLowering.GetValue(ValueType(body, id)!)!.ComputationType, ScalarOperator: plan.Operator, Check: check, Representation: WindowsLowering.GetValue(ValueType(body, input)!), LowerPredicate: plan.LowerPredicate, UpperPredicate: plan.UpperPredicate));
        return true;
    }

    internal readonly record struct ConversionPlan(string? Operator, string? LowerPredicate, string? UpperPredicate, Int128 Lower, Int128 Upper)
    {
        internal bool Checked => this.LowerPredicate is not null || this.UpperPredicate is not null;
    }
}
