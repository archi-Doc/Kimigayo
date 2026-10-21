// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The Call terminator of FunctionCodegen (R5 package C 3.2 I8, plan C3a; DECISIONS R5-M89): a callee by its kind's row, the collector's
// targets read by one forward cursor, and the one call writer, which the legacy rows, the glue items and the module's entry call too.
internal sealed partial class FunctionCodegen
{
    private int cursor; // The item's first target not passed yet (I8).

    /// <summary>Writes the module entry's call of its startup root, which takes no argument (IMPL 21.4.2), through the one call writer.</summary>
    /// <param name="output">The module's writer.</param>
    /// <param name="startup">The startup root's signature.</param>
    internal void WriteEntryCall(TextWriter output, FunctionAbi startup)
    {
        this.output = output;
        _ = this.WriteCall(startup, default);
    }

    // A Call by its callee's row: a compiler function of the Body disposition (KimiLibraryKinds' published IntrinsicDisposition) by its
    // legacy row, any other Fn callee by its target (Function); the other rows are not written yet, each at the call.
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
                this.Function(call);
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

    // A Fn callee by the collector's target at the call (I8): a body item is called by its signature (ModuleCodegen.Signature), one that
    // receives its caller's location is C3b's row; a Builtin shim is C8f's and C9b's, a property witness C3c's; without a target, a
    // #LibraryImport function is called by its C signature (SPEC 22.6), and any other callee is a broken contract.
    private void Function(in MirTerminator call)
    {
        var target = this.Target(new(this.at, this.body.Blocks[this.at].Count));
        if (target < 0)
        {
            if (this.module.Import(call.Callee.Symbol!) is { } import)
            {
                this.Invoke(call, import, new(call.Callee.Symbol, MirBodyKind.Fn));
                return;
            }

            this.Report(DiagnosticCode.InternalInvariant_Kd, call.Source);
            return;
        }

        switch (this.collector.Set.Items[target].Key.Kind)
        {
            case MonoKind.Body when this.module.Signature(target) is { } signature:
                if (signature.CallerLocation)
                {
                    this.Report(DiagnosticCode.Unsupported_Kd, call.Source); // C3b passes the caller's location.
                    return;
                }

                this.Invoke(call, signature, this.program.Bodies[this.collector.Set.Items[target].BodyOrdinal].Key);
                return;
            case MonoKind.Body:
                return; // A broken contract, recorded.
            case MonoKind.Builtin or MonoKind.PropertyWitness:
                this.Report(DiagnosticCode.Unsupported_Kd, call.Source); // C8f and C9b write the Builtin shims, C3c the property witnesses.
                return;
            default:
                this.Report(DiagnosticCode.InternalInvariant_Kd, call.Source); // A Fn call targets a body, a shim or a witness (K package 4.5).
                return;
        }
    }

    // A call by its signature (IMPL 21.3.1, SPEC 22.6): each parameter's argument (Argument), the destination's address as the result slot,
    // the call, its value put into the destination, then the call's target, or unreachable after a call without one (SPEC 14.10.3). The
    // operands are in the callee's Arg-local order (ArgumentPosition). A result slot is the destination, a fresh temp or local, never a
    // pointer argument: a Move's Place is moved out and a Copy's a copy slot (DECISIONS R5-M89).
    private void Invoke(in MirTerminator call, FunctionAbi signature, MirDefKey callee)
    {
        var parameters = signature.Parameters;
        this.arguments = this.arguments.Length < parameters.Length ? new Value[parameters.Length] : this.arguments;
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            var found = parameter.Kind == AbiParameterKind.ResultSlot ? this.Address(call.Place, out this.arguments[i])
                : this.Argument(call.Start + ArgumentPosition(callee, parameter.LogicalIndex), parameter, out this.arguments[i]);
            if (!found)
            {
                this.Report(DiagnosticCode.Unsupported_Kd, call.Source);
                return;
            }
        }

        if (this.WriteCall(signature, this.arguments.AsSpan(0, parameters.Length)) is { } result && this.LoweringOf(call.Place) is { } lowering)
        {
            this.Store(MirStatement.Assign(call.Place, default, call.Source), this.Widen(lowering, result));
        }

        this.output.Write(call.Target < 0 ? "  unreachable\n" : "  br ");
        if (call.Target >= 0)
        {
            this.WriteLabel(call.Target);
            this.output.Write('\n');
        }
    }

    // An argument for its parameter (IMPL 21.3.1): a Value or a string borrow its operand in its computation representation; an OwnedSlot
    // the address of storage the callee owns from the call on, a Move's Place itself (a string's too, whose Read is its record), or a
    // Copy's copy or a string constant's record in its copy slot (Copies). False for a form not covered yet.
    private bool Argument(int index, in AbiParameter parameter, out Value value)
    {
        var operand = this.body.Operands[index];
        value = default;
        switch (parameter.Kind)
        {
            case AbiParameterKind.Value or AbiParameterKind.SharedReference:
                if (!this.Operand(operand, out value))
                {
                    return false;
                }

                if (this.LoweringOf(operand) is { } lowering)
                {
                    value = this.Narrow(lowering, value);
                }

                return true;
            case AbiParameterKind.OwnedSlot when this.copyOf[index] >= 0 && operand.Kind == MirOperandKind.Const:
                value = new('c', this.copyOf[index]);
                this.Text(value, (string)this.body.Consts[operand.Arg].Value!);
                return true;
            case AbiParameterKind.OwnedSlot when this.copyOf[index] >= 0:
                if (!this.Read(operand.Place, out var source) || this.LoweringOf(operand.Place) is not { } copied)
                {
                    return false;
                }

                value = new('c', this.copyOf[index]);
                this.Put(value, this.InstanceType(operand.Place)!, copied, source);
                return true;
            case AbiParameterKind.OwnedSlot:
                return operand.Kind is MirOperandKind.Copy or MirOperandKind.Move && this.Address(operand.Place, out value);
            default:
                throw new InvalidOperationException($"The parameter kind {parameter.Kind} has no argument row.");
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
                    var operand = this.body.Operands[call.Start + ArgumentPosition(new(symbol, MirBodyKind.Fn), parameters[i].LogicalIndex)];
                    if (!this.Operand(operand, out this.arguments[i]))
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

        _ = this.WriteCall(routine, this.arguments.AsSpan(0, parameters.Length));
        this.output.Write("  br ");
        this.WriteLabel(call.Target);
        this.output.Write('\n');
    }

    // A call of a routine, each argument with its parameter's Type and attributes (the O2 parity list's `noundef nonnull ...` at every
    // call); its value, or null for a void result.
    private Value? WriteCall(FunctionAbi routine, ReadOnlySpan<Value> arguments)
    {
        var output = this.output;
        Value? result = routine.Result == "void" ? null : new Value('t', this.next++);
        output.Write("  ");
        if (result is { } value)
        {
            this.WriteValue(value);
            output.Write(" = ");
        }

        output.Write("call ");
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
        return result;
    }

    // The item the collector targets at a site of the item's body, or -1 (I8): its targets are in location order (K package 4.3) and the
    // frame writes the sites in that order, so one forward cursor reads them, passing the sites of blocks no runtime path reaches.
    private int Target(MirLocation site)
    {
        var targets = this.collector.Set.Targets(this.item);
        while (this.cursor < targets.Length && targets[this.cursor].At is var at &&
            (at.Block < site.Block || (at.Block == site.Block && at.Index < site.Index)))
        {
            this.cursor++;
        }

        return this.cursor < targets.Length && targets[this.cursor].At == site ? targets[this.cursor].Item : -1;
    }
}
