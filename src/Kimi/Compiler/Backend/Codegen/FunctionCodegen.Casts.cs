// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The Cast rvalue of FunctionCodegen (R5 plans C1b and C5b, package C 3.2's one switch per closed MIR vocabulary; DECISIONS R5-M80,
// R5-M104): a Cast into its Place by its closed MirCastKind: an integer, floating-point, numeric, wrapping, bit or raw pointer conversion
// by the scalar conversion row (FunctionCodegen.Conversions), an erasure by the common Function value's (FunctionCodegen.Erasures), an
// upcast by the Use row (SPEC 13.6.1: the same object, a handle's header or a borrow's payload address, the complete base being a prefix
// of the derived payload, X1; DECISIONS R5-M110).
internal sealed partial class FunctionCodegen
{
    private void Cast(in MirStatement statement)
    {
        switch ((MirCastKind)statement.Rvalue.Op)
        {
            case MirCastKind.Integer or MirCastKind.Floating or MirCastKind.Numeric: // SPEC 13.5.4's numeric conversions.
            case MirCastKind.Wrap or MirCastKind.Bits: // Its wrapping and bit conversions.
            case MirCastKind.Pointer or MirCastKind.IntToPointer: // The raw pointer conversions (SPEC 5.4, 8.11.2).
                this.ScalarCast(statement);
                return;
            case MirCastKind.Erasure:
                this.Erase(statement);
                return;
            case MirCastKind.Upcast:
                this.Use(statement);
                return;
            default:
                throw new InvalidOperationException($"The MIR cast kind {(MirCastKind)statement.Rvalue.Op} has no code generation row.");
        }
    }
}
