// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

// The values of FunctionCodegen (R5 package C 3.2 I4, I5 and I7): an operand's value and a constant's, a Type's representation in the
// item's instance, bool's widening and narrowing and a site's location (SPEC 22.5.1); their spelling is FunctionCodegen.Spelling's. A
// string constant has no value: it is written whole where it is put (FunctionCodegen.Stores' Text) and its bytes are read where it is
// compared (FunctionCodegen.Strings; DECISIONS R5-M118). A TypeToken constant has no value either: its call's writer reads it (C6a's
// TypeTest, C9c's format estimates).
internal sealed partial class FunctionCodegen
{
    // An integer's spelling at its width: the signed value of its low bits (two's complement, IMPL 21.1.3), so an unsigned value above the
    // signed range is the negative number with the same bits.
    private static Int128 Signed(Int128 bits, int width) => width >= 128 ? bits : (bits << (128 - width)) >> (128 - width);

    // An operand's value in its Type's representation (Read; a value in memory is its address); false after a broken contract's record (the
    // place writer's or Constant's; DECISIONS R5-M146). A zero-sized operand has no value. A PairAcquire Copies its Place or Reborrows it,
    // as the item's view decides (SPEC 8.14), and both write the Place's bits in its case Type: a Reborrow of an exclusive reference is the
    // pointer the Place holds (IMPL 21.1.3), so it needs no case decision here (DECISIONS R5-M87).
    private bool Operand(in MirOperand operand, out Value value)
    {
        value = default;
        switch (operand.Kind)
        {
            case MirOperandKind.Copy or MirOperandKind.Move or MirOperandKind.PairAcquire:
                return this.Read(operand.Place, out value);
            case MirOperandKind.Const:
                return this.Constant(this.body.Consts[operand.Arg], out value);
            default:
                throw new InvalidOperationException($"The MIR operand kind {operand.Kind} has no code generation row.");
        }
    }

    // A constant of a covered kind: Bool (stored 0 or 1), Integer of an integer or char Type or a raw pointer's null (SPEC 5.1), Float (its
    // IEEE bits), Unit, a function item (zero-sized: a Function Item stores none of its Components, SPEC 7.7.4; DECISIONS R5-M102) and a
    // Location, a verification site's number in the test catalog (TestCatalog.SiteOf, SPEC 20.6.2; DECISIONS R5-M136), which discovery
    // gives every verification of the accepted test build, so an unnumbered site is a broken contract. Any other constant read as a value,
    // a String or TypeToken one (its writer reads it) or an Integer one of a Type without integer arithmetic, char or raw pointer (Binding
    // fits literals), is a broken contract too, recorded at the body's subject; false after it (DECISIONS R5-M146).
    private bool Constant(in MirConst constant, out Value value)
    {
        value = default;
        if (constant.Kind == MirConstKind.Location)
        {
            if (this.module.Tests.SiteOf(constant.Site) is not (>= 0 and var site))
            {
                this.Report(DiagnosticCode.InternalInvariant_Kd, default);
                return true;
            }

            value = new('\0', site);
            return true;
        }

        var scalar = constant.Kind is MirConstKind.Bool or MirConstKind.Integer or MirConstKind.Float;
        if (!scalar && constant.Kind is not (MirConstKind.Unit or MirConstKind.FnItem))
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, default); // String, which has no value (above), and TypeToken (C6a, C9c).
            return false;
        }

        if (this.Representation(constant.Type) is not { } lowering)
        {
            return true; // A broken contract, recorded.
        }

        if (constant.Kind == MirConstKind.Float)
        {
            value = new('x', Hexadecimal(constant.Bits, lowering.Layout.Size));
            return true;
        }

        var type = this.collector.TypeOf(this.item, constant.Type)!;
        if (constant.Kind == MirConstKind.Integer && type.Semantics == SemanticsKind.Raw)
        {
            value = new('n', 0); // SPEC 5.1: null, the one raw pointer constant.
            return true;
        }

        if (constant.Kind == MirConstKind.Integer && !type.HasIntegerArithmetic && !ReferenceEquals(type, BoundType.Char))
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, default);
            return false;
        }

        value = lowering.Layout.Size == 0 ? default : new('\0', Signed(constant.Bits, 8 * lowering.Layout.Size));
        return true;
    }

    // The representation of a Type of the body in the item's instance (I7): Laid of its instance Type.
    private ValueLowering? Representation(BoundType type) => this.Laid(this.collector.TypeOf(this.item, type));

    // The layout a form needs past the item's by-value set (LayoutNeeds; DECISIONS R5-M149), the one path code generation reads such a Type
    // by: the Type the form's row names (LayoutNeeds.TypeOf: a projection step's, a raw pointer displacement's, a compared Tuple's, a call's
    // Type argument at an index, that Type given too) laid out in the item's instance; null after a record (Laid).
    private ValueLowering? Needed(MirPlace step) => this.Representation(LayoutNeeds.TypeOf(this.body, step));

    private ValueLowering? Needed(in MirRvalue operation) => this.Representation(LayoutNeeds.TypeOf(this.body, operation));

    private ValueLowering? Needed(ComparisonNode node) => this.Laid(LayoutNeeds.TypeOf(node)); // A shim's tree is of its closed Self.

    private ValueLowering? Needed(Subst subst, int index, out BoundType? type)
    {
        type = LayoutNeeds.TypeOf(subst)[index] is { } argument ? this.collector.TypeOf(this.item, argument) : null;
        return this.Laid(type);
    }

    // The representation of a Type in the item's instance (I7); null, with InternalInvariant_Kd at the body, where TypeOf made none or Layout
    // has none: the collector laid out every Type of an admitted item's by-value set and every layout need its forms have (K 4.8), so that is
    // a broken contract.
    private ValueLowering? Laid(BoundType? closed)
    {
        if (closed is not null && this.module.Layout.Of(closed).Value is { } lowering)
        {
            return lowering;
        }

        this.Report(DiagnosticCode.InternalInvariant_Kd, default);
        return null;
    }

    // The representation of a covered operand's Type: its Place's, or a constant's in the item's instance (null after a record).
    private ValueLowering? LoweringOf(in MirOperand operand)
        => operand.Kind == MirOperandKind.Const ? this.Representation(this.body.Consts[operand.Arg].Type) : this.LoweringOf(operand.Place);

    // SPEC 22.5.1 (I5; DECISIONS R5-M93): the location a callee, a runtime routine or an Abort at a site of the body reports, IMPL 21.3.1's
    // pair of a text and its byte length: in a caller-location body the location it received, its last two parameters (Abi.Of), which it
    // forwards, so a failure inside it reports the user's call; elsewhere the site's text as a pooled Location constant. False after a
    // record at a site without its source document, a broken contract.
    private bool Location(in SourceInfo source, out Site site)
    {
        var count = this.abi.Parameters.Length;
        if (this.abi.CallerLocation)
        {
            site = new(new('p', count - 2), new('p', count - 1));
            return true;
        }

        site = default;
        if (this.module.Locations.Text(this.body.Subject(source)) is not { } text)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, source); // A site of a body has its source document.
            return false;
        }

        var constant = this.module.Constants.Intern(text, LlvmConstantKind.Location);
        site = new(new('@', constant), new('\0', this.module.Constants[constant].ByteLength));
        return true;
    }

    // IMPL 21.1.3: bool, the one Type whose computation (i1) and storage (i8) representations differ, widens to be stored and narrows to
    // be computed; every other value is the same in both.
    private Value Widen(ValueLowering lowering, Value value) => this.Convert("zext ", lowering.ComputationType, value, lowering.Layout.StorageType);

    private Value Narrow(ValueLowering lowering, Value value) => this.Convert("trunc ", lowering.Layout.StorageType, value, lowering.ComputationType);

    private Value Convert(string operation, string from, Value value, string to)
    {
        if (from == to)
        {
            return value;
        }

        var converted = new Value('t', this.next++);
        this.output.Write("  ");
        this.WriteValue(converted);
        this.output.Write(" = ");
        this.output.Write(operation);
        this.output.Write(from);
        this.output.Write(' ');
        this.WriteValue(value);
        this.output.Write(" to ");
        this.output.Write(to);
        this.output.Write('\n');
        return converted;
    }

    // A location as IMPL 21.3.1 passes it (Location): its text's address and its byte length.
    private readonly record struct Site(Value Text, Value Length);
}
