// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The Call terminator of FunctionCodegen (R5 package C 3.2 I8, plan C3a, C3b, C8a, C5a, C6c, C8b, C8d, C8e, C9a, C9b; DECISIONS R5-M89,
// R5-M92, R5-M93, R5-M102, R5-M114, R5-M121, R5-M124, R5-M128, R5-M130): a callee by its kind's row (a Value callee's in
// FunctionCodegen.Closures), the collector's targets read by one forward cursor, each once in its site's order, and the one call
// writer, which the intrinsic rows, the glue items and the module frame's root calls call too, through the frame-function writers (a
// function the frame begins, BeginFrameFunction, its slots, WriteTextSlot, then WriteEntryCall with the frame's arguments, and an
// adapter's end after its call, End, which an adapter item's entry (WriteAdapter) writes too); each argument is FunctionCodegen.Arguments'
// (its parameter's kind, a copy slot). A call that dispatches (SPEC 6.4.3, 6.4.6) runs through a virtual slot, which only makes the callee
// value it writes (EntryOf): a Virtual callee, without a target (X5), under its slot's signature (ModuleCodegen.Slot); a body target
// that implements a slot, a requirement's virtual witness (BodyRun); a Builtin or PropertyWitness target its shim's function
// (FunctionCodegen.Shims; DECISIONS R5-M126, R5-M130, R5-M141); an Import target, a #LibraryImport function's direct call or a
// requirement's whose witness it is, the import by its C signature (DECISIONS R8-M19).
internal sealed partial class FunctionCodegen
{
    private int cursor; // The item's first target not passed yet (I8).

    /// <summary>Begins a function the module frame writes (IMPL 21.4.2, 21.6): the spelling context of what FunctionCodegen writes into
    /// it next (WriteTextSlot, WriteEntryCall), its parameters and its values numbered from 0, as a body's are. Every frame-written
    /// function that calls a root begins here first.</summary>
    /// <param name="output">The module's writer.</param>
    /// <param name="caller">The signature of the function the frame is writing, whose parameters the arguments may name.</param>
    internal void BeginFrameFunction(TextWriter output, FunctionAbi caller) => (this.output, this.abi, this.next) = (output, caller, 0);

    /// <summary>Writes a slot of <c>Text.Utf8Slice</c> in the function the frame writes (IMPL 21.6.3: a platform adapter's text), its
    /// alloca by the Type's layout and its <c>value</c> Field's Slice words <c>data</c> and <c>length</c> (Storage.SliceRepr, through
    /// Word), never a literal record. The caller gives each word a value of its Type: a <c>ptr</c> and an <c>i64</c>.</summary>
    /// <param name="text">The slot's Type, <c>Text.Utf8Slice</c>.</param>
    /// <param name="data">The bytes' address, a <c>ptr</c>.</param>
    /// <param name="length">The byte count, an <c>i64</c>.</param>
    /// <returns>The slot's address.</returns>
    internal Value WriteTextSlot(BoundType text, Value data, Value length)
    {
        var record = this.module.Layout.Of(text).Value!; // A witness's parameter Type, laid out by the collector.
        var slot = new Value('t', this.next);
        this.WriteSlot("  %t", this.next++, record.Layout);
        var field = AdtDef.IndexOf(text, "value");
        var slice = AdtDef.FieldType(this.typeContext, text, field)!;
        var start = this.Offset(slot, record.Layout.FieldOffsets.Span[field]);
        var words = this.module.Layout.Of(slice).Value!;
        this.StoreWord(slice, words, start, "data", data);
        this.StoreWord(slice, words, start, "length", length);
        return slot;
    }

    /// <summary>Writes a call of a root from the function the frame began (BeginFrameFunction; IMPL 21.4.2; DECISIONS R5-M89), the one
    /// member through which the frame calls a root (the module entry's call of its startup root, a test entry's of a case's root, a
    /// platform adapter's of its witness; an interrupt thunk's call of its handler is to reuse it): through the one call writer, in that
    /// function's spelling context.</summary>
    /// <param name="routine">The root's signature.</param>
    /// <param name="arguments">The call's arguments, one per parameter of the root's signature.</param>
    /// <returns>The call's value, or null for a void result.</returns>
    internal Value? WriteEntryCall(FunctionAbi routine, ReadOnlySpan<Value> arguments) => this.WriteCall(routine, arguments);

    /// <summary>Writes the end of an adapter's function after its one call, of a platform adapter the frame began (BeginFrameFunction)
    /// or of an adapter item's entry (WriteAdapter): <c>unreachable</c> after a callee that never returns, else the call's value
    /// returned as the written function's result, or <c>ret void</c> without one (SPEC 10.7).</summary>
    /// <param name="callee">The called signature.</param>
    /// <param name="result">The call's value, or null for a void result.</param>
    internal void End(FunctionAbi callee, Value? result)
    {
        var output = this.output;
        if (callee.NoReturn)
        {
            output.Write("  unreachable\n}\n");
            return;
        }

        if (result is not { } value)
        {
            output.Write("  ret void\n}\n");
            return;
        }

        output.Write("  ret ");
        output.Write(this.abi.Result);
        output.Write(' ');
        this.WriteValue(value);
        output.Write("\n}\n");
    }

    // A Call by its callee's row: a Fn callee and a Requirement by its target (Function), an intrinsic by its row's writer
    // (FunctionCodegen.Intrinsics), a Value or Virtual callee by its own row.
    private void Call(in MirTerminator call)
    {
        var callee = call.Callee;
        switch (callee.Kind)
        {
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
    // whose signature is the call's, since compatibility inserts no conversion): what the target runs (Witness), a body item by its
    // signature (ModuleCodegen.Signature) with its operands in its body's order, a caller-location one given the call's location (Invoke); a
    // Fn callee calls the function it names whatever it implements (a direct base call, SPEC 6.4.4), a Requirement callee through the
    // virtual slot its witness runs. A call without a target is a broken contract: the collector gives every accepted call one, a
    // #LibraryImport function's its Import item (SPEC 22.6; DECISIONS R8-M19).
    private void Function(in MirTerminator call)
    {
        var target = this.Target(new(this.at, this.body.Blocks[this.at].Count));
        if (target < 0)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, call.Source);
            return;
        }

        if (this.Witness(target, call.Source) is { } run)
        {
            var ordinal = this.collector.Set.Items[target].BodyOrdinal; // A shim's operands are its requirement's, in their own order.
            var key = ordinal >= 0 ? this.program.Body(ordinal).Key : new MirDefKey(call.Callee.Symbol, MirBodyKind.Fn);
            this.Invoke(call, run.Signature, key, default, call.Callee.Kind == MirCalleeKind.Requirement ? run.Slot : null);
        }
    }

    // SPEC 8.6.3-8.6.4, 6.4.6 (K package 4.5 rows 1, 3 and 5): what a call of a function's or a requirement's target item runs, the one rule
    // of a Requirement call and of a function Item's call and adapter (FunctionCodegen.Closures' Runs): a body item its body (BodyRun); a
    // shim item, Builtin or PropertyWitness, its shim's function (FunctionCodegen.Shims; DECISIONS R5-M126, R5-M130, R5-M141); an Import
    // item its import by its C signature (ModuleCodegen.Import; SPEC 22.6, DECISIONS R8-M19); any other item a broken contract. Null after a
    // record at the source.
    private Run? Witness(int target, in SourceInfo source)
    {
        switch (this.collector.Set.Items[target].Key.Kind)
        {
            case MonoKind.Body:
                return this.BodyRun(target); // Null after a record.
            case MonoKind.Builtin or MonoKind.PropertyWitness:
                return this.module.Shim(target) is { } shim ? new Run(shim, null) : null; // Null after a record.
            case MonoKind.Import when this.module.Import(this.collector.Set.Items[target].Key.Symbol!) is { } import:
                return new Run(import, null); // Every import of a complete compilation is validated (Binding.LibraryImports).
            default:
                this.Report(DiagnosticCode.InternalInvariant_Kd, source); // A call targets a body, a shim or a witness (K package 4.5).
                return null;
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

        var function = this.program.Body(this.collector.Set.Items[target].BodyOrdinal).Key.Decl;
        return new Run(signature, function is not null && VTableDef.SlotOf(function) >= 0 ? function : null);
    }

    // SPEC 6.4.6, 8.6.3: the call of a witness's body a function writes itself around (BodyRun's run: a comparison shim's Witness leaf),
    // each parameter by its kind: the result slot the slot given, a location parameter the site given, any other the input at its logical
    // index; the call of the callee value its slot, if any, makes (EntryOf); its value into `result` (none for a void callee). False, with
    // InternalInvariant_Kd, for a parameter the caller does not supply (a witness's signature is its requirement's).
    private bool CallWitness(Run run, ReadOnlySpan<Value> inputs, Value? slot, Site? site, out Value? result)
    {
        result = null;
        var parameters = run.Signature.Parameters;
        this.arguments = this.arguments.Length < parameters.Length ? new Value[parameters.Length] : this.arguments;
        for (var i = 0; i < parameters.Length; i++)
        {
            Value? argument = parameters[i].Kind switch
            {
                AbiParameterKind.ResultSlot => slot,
                AbiParameterKind.Location => site?.Text,
                AbiParameterKind.LocationLength => site?.Length,
                _ => (uint)parameters[i].LogicalIndex < (uint)inputs.Length ? inputs[parameters[i].LogicalIndex] : null,
            };
            if (argument is not { } value)
            {
                this.Report(DiagnosticCode.InternalInvariant_Kd, default);
                return false;
            }

            this.arguments[i] = value;
        }

        result = this.WriteCall(run.Signature, this.arguments.AsSpan(0, parameters.Length), this.EntryOf(run.Signature, run.Slot));
        return true;
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

    // A call's arguments in its signature's parameter order, into `arguments` (IMPL 21.3.1, SPEC 22.6), a Call terminator's or a creating
    // adaptation's in that form (FunctionCodegen.Objects' Creation, without a callee or a target), of which it reads the Place, the
    // operands' Start and the Source only: each parameter's argument (Argument), the destination's address as the result slot, the call's
    // location for the location parameters (SPEC 22.5.1, Location; DECISIONS R5-M92, R5-M93), a parameter without an operand (Given: a
    // runtime routine's stride or reason, Text.writer's kind and dispatch word, a Closure's environment and context) the next of the
    // given values. The operands are in the order the callee's key gives (ArgumentPosition). A result slot is the destination, a fresh
    // temp or local, never a pointer argument: a Move's Place is moved out and a Copy's a copy slot (DECISIONS R5-M89). False after a
    // record.
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
                return false; // A broken contract, recorded.
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

    // A call of a routine, its result's extension and each argument with its parameter's Type and attributes (the O2 parity list's
    // `noundef nonnull ...` at every call, value calls included, package C row 14; an import's System V extensions, its own at each call,
    // IMPL 21.2); its value, or null for a void result. Its callee is one value: the routine's symbol, or an
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

        output.Write("call");
        output.Write(routine.ResultExtension);
        output.Write(' ');
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
