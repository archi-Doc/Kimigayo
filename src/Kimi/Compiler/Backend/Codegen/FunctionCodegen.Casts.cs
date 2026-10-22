// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The Cast rvalue of FunctionCodegen (R5 plan C1b, package C 3.2's one switch per closed MIR vocabulary; DECISIONS R5-M80): a Cast into its
// Place by its closed MirCastKind: an integer, floating-point, numeric, wrapping, bit or raw pointer conversion by the scalar conversion
// row (FunctionCodegen.Conversions); an upcast (C6a) and an erasure (C5b) have no row yet, each one located Unsupported_Kd at the statement.
internal sealed partial class FunctionCodegen
{
    private void Cast(in MirStatement statement)
    {
        switch ((MirCastKind)statement.Rvalue.Op)
        {
            case MirCastKind.Integer or MirCastKind.Floating or MirCastKind.Numeric or MirCastKind.Wrap or MirCastKind.Bits or MirCastKind.Pointer or
                MirCastKind.IntToPointer:
                this.ScalarCast(statement);
                return;
            case MirCastKind.Upcast or MirCastKind.Erasure:
                this.Report(DiagnosticCode.Unsupported_Kd, statement.Source); // C6a's upcast, C5b's erasure.
                return;
            default:
                throw new InvalidOperationException($"The MIR cast kind {(MirCastKind)statement.Rvalue.Op} has no code generation row.");
        }
    }
}
