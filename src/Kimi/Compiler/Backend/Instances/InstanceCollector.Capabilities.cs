// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The capability check of operations and declarations (K package 4.7; DECISIONS R4-M8, R5-M53, R5-M54, R5-M82, R5-M126): the capability
// table read for the operations of a body, at the definition stage (A3b) and in each instance, for an arithmetic shim's operation at each
// site that requests it (InstanceCollector.Witnesses' Requirement) and for a root-module static's declaration.
internal sealed partial class InstanceCollector
{
    // Whether an operation's Types depend on slots: its first operand's or its result's.
    private static bool IsOpen(MirBody body, in MirRvalue rvalue)
        => OperandType(body, body.Operands[rvalue.Start]).ContainsParameter || rvalue.Type is { ContainsParameter: true };

    // IMPL 21.3.3 (DECISIONS R5-M126): the gap of a Builtin witness's operation, an arithmetic shim's Contract's MIR operator
    // (BuiltinArithmetic, SPEC 13.3.3) on the target's closed Self, which Requirement records at each site that requests the shim, as a
    // statement's operation is at its location; none for any other compiler function.
    private static CapabilityGap? ArithmeticGap(in RequirementTarget target)
    {
        if (target.Target!.CompilerFunction != CompilerFunctionKind.BuiltinArithmetic)
        {
            return null;
        }

        var (kind, op) = BuiltinArithmetic.Operation(TyCtxt.Contract(target.Target).LibraryDeclaration);
        return Capability.Of(kind, op, target.Subst!.Self!, null);
    }

    // A3b: an operation of a root-module body whose Types are closed, checked once per definition and location in whichever block it lies
    // (unused and check-only ones included); an open one is checked per instance. An operation Binding refused is an Opaque value, which
    // has no row, so Binding's gate and this check never both report.
    private void CheckOperation(MirBody body, in MirStatement statement)
    {
        if (statement is { Kind: MirStatementKind.Assign, Rvalue: { Kind: MirRvalueKind.BinaryOp or MirRvalueKind.Cast } rvalue } &&
            OperandType(body, body.Operands[rvalue.Start]) is { ContainsParameter: false } operand && rvalue.Type is not { ContainsParameter: true } &&
            Capability.Of(rvalue, operand) is { } gap)
        {
            this.Report(Capability.Code(gap), this.Located(body, statement.Source));
        }
    }

    // IMPL 21.3.3: an operation whose Types depend on slots, checked in an item's instance with its Types there and reported at its location
    // for every instance of the checked set, unreachable blocks included, once per location (K 4.9: in a generic body of a root module its
    // own operation; in a library one the user's request).
    private void CheckOperation(int item, SourceRef subject, MirBody body, in MirRvalue rvalue)
    {
        var operand = this.In(item, subject, OperandType(body, body.Operands[rvalue.Start]));
        var result = rvalue.Type is { } type ? this.In(item, subject, type) : null;
        if (operand is not null && (rvalue.Type is null || result is not null) && Capability.Of(rvalue with { Type = result }, operand) is { } gap)
        {
            this.Report(Capability.Code(gap), subject);
        }
    }

    // A3b's declaration row (DECISIONS R5-M82): a verified root-module static that can be set (its setter present), whose StaticInit
    // body's result Type is not a scalar (ScalarTypes) or whose definition instantiates (a generic container's group, TyCtxt.Instantiates)
    // is the static-storage gap at its declaration, its StaticInit body's source, whether or not anything reads it; an unverified one is
    // Binding's error.
    private void CheckStatic(MirBody body)
    {
        if (body.Key is { Kind: MirBodyKind.StaticInit, Decl: { Property: { IsVerified: true } property } definition } &&
            (property.Setter.IsPresent || !ScalarTypes.Supports(body.Locals[0].Type) || this.types.Instantiates(definition)))
        {
            this.Report(Capability.Code(CapabilityGap.StaticStorage), body.Source);
        }
    }
}
