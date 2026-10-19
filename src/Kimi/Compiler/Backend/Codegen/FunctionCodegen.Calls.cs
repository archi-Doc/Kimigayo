// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The Call terminator of FunctionCodegen (R5 package C 3.2, plan C3a): a callee by its kind's row and the one call writer.
internal sealed partial class FunctionCodegen
{
    // A Call by its callee's row: a compiler function of the Body disposition (KimiLibraryKinds' published IntrinsicDisposition) by its
    // legacy row; the other rows are not written yet, each at the call.
    private void Call(in MirTerminator call)
    {
        var callee = call.Callee;
        switch (callee.Kind)
        {
            case MirCalleeKind.Fn when callee.Symbol!.CompilerFunction is not CompilerFunctionKind.None and var kind &&
                IntrinsicDisposition.Of(kind).Kind == DispositionKind.Body:
                this.Legacy(call);
                return;
            case MirCalleeKind.Fn:
                this.Report(DiagnosticCode.Unsupported_Kd, call.Source); // C3a calls the collector's target; C8f and C9b the Shim dispositions.
                return;
            case MirCalleeKind.Requirement:
                this.Report(DiagnosticCode.Unsupported_Kd, call.Source); // C3b calls the requirement's target.
                return;
            case MirCalleeKind.Value:
                this.Report(DiagnosticCode.Unsupported_Kd, call.Source); // C5a calls a Closure or function value.
                return;
            case MirCalleeKind.Virtual:
                this.Report(DiagnosticCode.Unsupported_Kd, call.Source); // C6c loads the virtual slot.
                return;
            case MirCalleeKind.Intrinsic:
                this.Report(DiagnosticCode.Unsupported_Kd, call.Source); // C8a and C10a write the intrinsic rows.
                return;
            default:
                throw new InvalidOperationException($"The MIR callee kind {callee.Kind} has no code generation row.");
        }
    }

    // A compiler function the backend serves from a legacy runtime row until R8 writes its body (the Body disposition): the console
    // writeLine (RuntimeAbi.WriteLine, SPEC 22.1-22.5.5: it borrows its string); C8b-C8e add theirs, and C9a's formatting rows come from one
    // Backend table beside LegacyRuntimeHelpers.FormattingRow, not from more arms here. The routine takes the call's operands at its
    // parameters' slots and the call site's location (Location); its result is Unit, so the destination takes nothing.
    private void Legacy(in MirTerminator call)
    {
        var symbol = call.Callee.Symbol!;
        var routine = symbol.CompilerFunction switch
        {
            CompilerFunctionKind.WriteLine => RuntimeAbi.WriteLine,
            _ => null,
        };
        if (routine is null)
        {
            this.Report(DiagnosticCode.Unsupported_Kd, call.Source);
            return;
        }

        var constant = this.Location(call.Source);
        if (constant < 0)
        {
            return; // Recorded.
        }

        var parameters = routine.Parameters;
        if (this.arguments.Length < parameters.Length)
        {
            this.arguments = new Value[parameters.Length];
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            switch (parameters[i].Kind)
            {
                case AbiParameterKind.Value or AbiParameterKind.SharedReference:
                    if (!this.Operand(this.body.Operands[call.Start + MirCallee.Position(symbol, parameters[i].LogicalIndex)], out this.arguments[i]))
                    {
                        this.Report(DiagnosticCode.Unsupported_Kd, call.Source);
                        return;
                    }

                    break;
                case AbiParameterKind.Location:
                    this.arguments[i] = new('@', constant);
                    break;
                case AbiParameterKind.LocationLength:
                    this.arguments[i] = new('\0', this.module.Constants[constant].ByteLength);
                    break;
                default:
                    throw new InvalidOperationException($"The runtime parameter kind {parameters[i].Kind} has no argument row.");
            }
        }

        this.WriteCall(routine, this.arguments.AsSpan(0, parameters.Length));
        this.output.Write("  br ");
        this.WriteLabel(call.Target);
        this.output.Write('\n');
    }

    // A call of a routine without a result value, each argument with its parameter's Type and attributes.
    private void WriteCall(FunctionAbi routine, ReadOnlySpan<Value> arguments)
    {
        var output = this.output;
        output.Write("  call ");
        output.Write(routine.Result);
        output.Write(" @");
        output.Write(routine.Name);
        output.Write('(');
        for (var i = 0; i < arguments.Length; i++)
        {
            output.Write(i == 0 ? string.Empty : ", ");
            output.Write(routine.Parameters[i].Type);
            output.Write(routine.Parameters[i].Attributes);
            output.Write(' ');
            this.WriteValue(arguments[i]);
        }

        output.Write(")\n");
    }
}
