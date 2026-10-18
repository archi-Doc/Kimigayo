// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The capability check of operations (K package 4.7; DECISIONS R4-M8, R5-M53): the capability table read for the operations of a body.
internal sealed partial class InstanceCollector
{
    // A3b: an operation of a root-module body whose Types are closed, checked once per definition and location in whichever block it lies
    // (unused and check-only ones included); an open one is checked per instance. An operation Binding refused is an Opaque value, which
    // has no row, so Binding's gate and this check never both report.
    private void CheckOperation(MirBody body, in MirStatement statement)
    {
        if (statement is { Kind: MirStatementKind.Assign, Rvalue: { Kind: MirRvalueKind.BinaryOp or MirRvalueKind.Cast } rvalue } &&
            OperandType(body, body.Operands[rvalue.Start]) is { ContainsParameter: false } operand && rvalue.Type is not { ContainsParameter: true } &&
            Capability.Of(rvalue, operand) is { } gap)
        {
            this.Set.Report(Capability.Code(gap), this.Located(body, statement.Source));
        }
    }
}
