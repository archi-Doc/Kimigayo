// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The Call terminator of FunctionCodegen (R5 package C 3.2 I8, plan C3a, C3b, C8a, C5a, C6c, C8b; DECISIONS R5-M89, R5-M92, R5-M93,
// R5-M102, R5-M114, R5-M116): a callee by its kind's row (a Value callee's in FunctionCodegen.Closures, a compiler function's of the Body
// disposition by its legacy row's writer, Legacy), the collector's targets read by one forward cursor, each once in its site's order, and
// the one call writer, which the legacy rows, the intrinsic rows, the glue items and the module's entry call too; each argument is
// FunctionCodegen.Arguments' (its parameter's kind, a copy slot). A call that dispatches (SPEC 6.4.3, 6.4.6) runs through a virtual slot,
// which only makes the callee value it writes (EntryOf): a Virtual callee, without a target (X5), under its slot's signature
// (ModuleCodegen.Slot); a body target that implements a slot, a requirement's virtual witness (BodyRun). Rows not written yet: a Builtin
// shim (C8f, C9b), a property witness (C3c), a requirement whose witness has no body (PLAN G157 (10)), the other compiler functions' legacy
// rows (C8d, C8e, C9a; LegacyRuntimeHelpers.Row).
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
    // legacy row, any other Fn callee and a Requirement by its target (Function), an intrinsic by its row's writer
    // (FunctionCodegen.Intrinsics); the other rows are not written yet, each at the call.
    private void Call(in MirTerminator call)
    {
        var callee = call.Callee;
        switch (callee.Kind)
        {
            case MirCalleeKind.Fn when LegacyRuntimeHelpers.Row(callee.Symbol!.CompilerFunction) is { } row:
                this.Legacy(call, row, this.Target(new(this.at, this.body.Blocks[this.at].Count)));
                return;
            case MirCalleeKind.Fn or MirCalleeKind.Requirement:
                this.Function(call);
                return;
            case MirCalleeKind.Value:
                this.ValueCall(call);
                return;
            case MirCalleeKind.Virtual:
                this.Virtual(call);
                return;
            case MirCalleeKind.Intrinsic:
                this.Intrinsic(call, IntrinsicRows.Of((IntrinsicId)callee.Arg));
                return;
            default:
                throw new InvalidOperationException($"The MIR callee kind {callee.Kind} has no code generation row.");
        }
    }

    // A Fn or Requirement callee by the collector's target at the call (I8; a requirement's is the witness K6 resolved, SPEC 8.6.3-8.6.4,
    // whose signature is the call's, since compatibility inserts no conversion): a Fn callee's body item is called by its signature
    // (ModuleCodegen.Signature), a caller-location one given the call's location (Invoke), whatever it implements (a direct base call, SPEC
    // 6.4.4); a Requirement callee's runs as a witness does (BodyRun); a Builtin shim is C8f's and C9b's, a property witness C3c's. Without a
    // target, a #LibraryImport function is called by its C signature (SPEC 22.6), a requirement's witness has no body (R5-M67; a row not
    // written yet, PLAN G157 (10)), and any other callee is a broken contract.
    private void Function(in MirTerminator call)
    {
        var target = this.Target(new(this.at, this.body.Blocks[this.at].Count));
        if (target < 0)
        {
            if (call.Callee.Kind == MirCalleeKind.Requirement)
            {
                this.Report(DiagnosticCode.Unsupported_Kd, call.Source);
            }
            else if (this.module.Import(call.Callee.Symbol!) is { } import)
            {
                this.Invoke(call, import, new(call.Callee.Symbol, MirBodyKind.Fn));
            }
            else
            {
                this.Report(DiagnosticCode.InternalInvariant_Kd, call.Source);
            }

            return;
        }

        switch (this.collector.Set.Items[target].Key.Kind)
        {
            case MonoKind.Body when this.BodyRun(target) is { } run:
                var key = this.program.Bodies[this.collector.Set.Items[target].BodyOrdinal].Key;
                this.Invoke(call, run.Signature, key, default, call.Callee.Kind == MirCalleeKind.Requirement ? run.Slot : null);
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

    // SPEC 6.4.6, 8.6.3 (DECISIONS R5-M114): what a call of a body target runs, the one rule of a requirement's witness: the body by its
    // signature, through the virtual slot its definition runs when it runs one (VTableDef.SlotOf; a virtual witness maps to its slot). A
    // Requirement call and a function Item's call and adapter (Runs) take it; a Fn callee calls the function it names (above). A virtual
    // function's own call and Item have no body target (X5). Null after a record.
    private Run? BodyRun(int target)
    {
        if (this.module.Signature(target) is not { } signature)
        {
            return null; // Recorded.
        }

        var function = this.program.Bodies[this.collector.Set.Items[target].BodyOrdinal].Key.Decl;
        return new Run(signature, function is not null && VTableDef.SlotOf(function) >= 0 ? function : null);
    }

    // SPEC 6.4.3 (X5; DECISIONS R5-M114): a Virtual callee, which has no target: the original's slot under the call's substitution in the
    // item's instance (ModuleCodegen.Slot), its operands the original's (the receiver first), called through the original's slot.
    private void Virtual(in MirTerminator call)
    {
        var original = call.Callee.Symbol!;
        if (this.module.Slot(original, call.Callee.Subst, this.item) is { } signature)
        {
            this.Invoke(call, signature, new(original, MirBodyKind.Fn), default, original);
        }
    }

    // A call by its signature (IMPL 21.3.1, SPEC 22.6): its arguments (Pass), then the call of its callee value, which the slot it runs
    // through, if any, makes (EntryOf), and its end (Finish).
    private void Invoke(in MirTerminator call, FunctionAbi signature, MirDefKey callee, ReadOnlySpan<Value> given = default, BindingSymbol? slot = null)
    {
        if (this.Pass(call, signature, callee, given))
        {
            var arguments = this.arguments.AsSpan(0, signature.Parameters.Length);
            this.Finish(call, this.WriteCall(signature, arguments, this.EntryOf(signature, slot)));
        }
    }

    // A call's arguments in its signature's parameter order, into `arguments` (IMPL 21.3.1, SPEC 22.6): each parameter's argument (Argument),
    // the destination's address as the result slot, the call's location for the location parameters (SPEC 22.5.1, Location; DECISIONS
    // R5-M92, R5-M93), a parameter without an operand (Given: a runtime routine's stride or reason, a Closure's environment and context)
    // the next of the given values. The operands are in the order the callee's key gives (ArgumentPosition). A result slot is the destination, a fresh
    // temp or local, never a pointer argument: a Move's Place is moved out and a Copy's a copy slot (DECISIONS R5-M89). False after a record.
    private bool Pass(in MirTerminator call, FunctionAbi signature, MirDefKey callee, ReadOnlySpan<Value> given)
    {
        var parameters = signature.Parameters;
        this.arguments = this.arguments.Length < parameters.Length ? new Value[parameters.Length] : this.arguments;
        var (located, site, next) = (false, default(Site), 0);
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (Given(parameter))
            {
                this.arguments[i] = given[next++];
                continue;
            }

            if (parameter.Kind is AbiParameterKind.Location or AbiParameterKind.LocationLength)
            {
                if (!located && !(located = this.Location(call.Source, out site)))
                {
                    return false; // Recorded.
                }

                this.arguments[i] = parameter.Kind == AbiParameterKind.Location ? site.Text : site.Length;
                continue;
            }

            var index = call.Start + ArgumentPosition(callee, parameter.LogicalIndex);
            var found = parameter.Kind == AbiParameterKind.ResultSlot ? this.Address(call.Place, out this.arguments[i])
                : this.Argument(this.body.Operands[index], this.copyOf[index], parameter.Kind, out this.arguments[i]);
            if (!found)
            {
                this.Report(DiagnosticCode.Unsupported_Kd, call.Source);
                return false;
            }
        }

        return true;
    }

    // A call's end: its value, if any, put into the destination in its storage representation, then the call's target (Branch, after its
    // flag points), or unreachable after a call without one (SPEC 14.10.3), which has no runtime successor and so writes no points.
    private void Finish(in MirTerminator call, Value? result)
    {
        if (result is { } value && this.LoweringOf(call.Place) is { } lowering)
        {
            this.Store(MirStatement.Assign(call.Place, default, call.Source), this.Widen(lowering, value));
        }

        if (call.Target < 0)
        {
            this.output.Write("  unreachable\n");
            return;
        }

        this.Branch(call.Target);
    }

    // A compiler function the backend serves from its legacy row until R8 writes its body (the Body disposition; LegacyRuntimeHelpers.Row,
    // DECISIONS R5-M116), by the row's writer, at its direct call and at its Item's Value call (SPEC 7.7.4), with the target the call site
    // carries: a runtime routine through the one call writer (Invoke), the call's operands at its parameters' slots placed by the call's own
    // key and the call site's location; a whole-value update (FunctionCodegen.Updates); a fixed array's storage (FunctionCodegen.Storage);
    // a row not written yet, its unit's located Unsupported_Kd.
    private void Legacy(in MirTerminator call, LegacyRow row, int target)
    {
        if (row.Updates)
        {
            this.Update(call, row.Writer, target);
            return;
        }

        switch (row.Writer)
        {
            case LegacyWriter.Routine:
                this.Invoke(call, row.Routine!, new(call.Callee.Symbol, MirBodyKind.Fn));
                return;
            case LegacyWriter.FixedStorage:
                this.FixedStorage(call);
                return;
            case LegacyWriter.Unwritten:
                this.Report(DiagnosticCode.Unsupported_Kd, call.Source); // Its row's unit (LegacyRuntimeHelpers.Row).
                return;
            default:
                throw new InvalidOperationException($"The legacy writer {row.Writer} has no code generation row.");
        }
    }

    // A call of a routine, each argument with its parameter's Type and attributes (the O2 parity list's `noundef nonnull ...` at every call,
    // value calls included, package C row 14); its value, or null for a void result. Its callee is one value: the routine's symbol, or an
    // entry pointer (an erased value's table entry, FunctionCodegen.Closures; a virtual slot's, EntryOf); nothing else decides how it calls.
    private Value? WriteCall(FunctionAbi routine, ReadOnlySpan<Value> arguments, Value? entry = null)
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
        output.Write(' ');
        if (entry is { } pointer)
        {
            this.WriteValue(pointer);
        }
        else
        {
            output.Write('@');
            output.Write(routine.Name);
        }

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

    // The next item the collector targets at a site of the item's body, or -1 (I8): its targets are in location order (K package 4.3), a
    // site's in the order its effects run (an owner Value call's body, then its operand's glue; DECISIONS R5-M102), and the frame writes
    // the sites in that order, so one forward cursor reads each once, passing the sites of blocks no runtime path reaches.
    private int Target(MirLocation site)
    {
        var targets = this.collector.Set.Targets(this.item);
        while (this.cursor < targets.Length && targets[this.cursor].At is var at &&
            (at.Block < site.Block || (at.Block == site.Block && at.Index < site.Index)))
        {
            this.cursor++;
        }

        return this.cursor < targets.Length && targets[this.cursor].At == site ? targets[this.cursor++].Item : -1;
    }
}
