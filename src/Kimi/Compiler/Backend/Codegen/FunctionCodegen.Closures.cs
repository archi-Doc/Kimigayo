// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The value calls of FunctionCodegen (R5 plans C5a, C5b, C5c, C6c and C8b, package C 3.2 I8 and row 14; SPEC 6.4.3, 7.7.3, 7.7.4, 8.10,
// IMPL 21.1.3; DECISIONS R5-M102, R5-M104, R5-M108, R5-M114, R5-M116):
// a Value callee by its value's Type in the item's instance below its reference layer (TyCtxt.Layer, the collector's Callable rule), a closed
// vocabulary. A Closure calls its body, the collector's target, under its signature (Abi's ConcreteClosure: the environment pointer first, the
// context last): the environment is the referent of a borrowed operand, or an owned operand's storage (a Move's Place, a Copy's copy slot), the
// context null. A function Item calls what its call runs (Runs, also an ItemAdapter's entry's callee), decided by its function: a compiler
// function's its direct call's row (SPEC 7.7.4), whatever targets the row needs, a legacy row's by its writer over the Value call's
// operands (FunctionCodegen.Calls' Legacy; DECISIONS R5-M116), an intrinsic's when it is one runtime routine (Routine); any other
// function's its body target, its function's body or a requirement Item's witness (through its slot when it implements one, BodyRun); a
// virtual function's Item, which has no target (X5), its slot (SPEC 6.4.3); a #LibraryImport function's its import (SPEC 22.6); any other
// is a broken contract (an unsafe or internal compiler function has no Item, and a requirement Item whose witness has no MIR body is the
// collector's BodylessWitness gap at check; DECISIONS R5-M146). The slot only makes the callee
// value the call writes (EntryOf), so an Item's call and its adapter's entry dispatch through the one call writer, and the adapter's entry
// is the erased virtual Item's thunk. A common Function value (IMPL 21.1.3: its environment word, then its table) calls its table's entry
// with its environment word and its table's context under the erased signature (ModuleCodegen.Erased). A Value call's operands are its
// Callable signature's inputs in order, so its own key places them (MirCallee.Position without a function, the builder's rule). After an
// owner call whose callee borrows its environment, the call site's next target, the operand Type's glue, destroys the
// operand (SPEC 8.10: "destroys the rest after the call") at the call's location; the collector decides which calls have it. A Closure
// environment's construction is FunctionCodegen.Aggregates', a Closure body's environment parameter FunctionCodegen.Frame's.
internal sealed partial class FunctionCodegen
{
    // IMPL 21.1.3: a common function value's table, its entry, context and drop words at these offsets (FunctionCodegen.Erasures writes it).
    private const int TableEntry = 0;
    private const int TableContext = 8;
    private const int TableDrop = 16;

    // A Value call by its callee value's Type (above).
    private void ValueCall(in MirTerminator call)
    {
        if (this.InstanceType(call.Operand) is not { } type)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, call.Source); // The collector made every Type of an admitted item.
            return;
        }

        var layer = TyCtxt.Layer(type, out var callable, out _);
        if (layer == HirPlaceKind.PairLayer)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, call.Source); // Binding calls no value through a pair layer (NotCallable_Kd).
            return;
        }

        var owned = layer == HirPlaceKind.Value; // By value (an owner call), else through a reference.
        switch (callable!.Kind)
        {
            case BoundTypeKind.Closure:
                this.Closure(call, owned);
                return;
            case BoundTypeKind.FunctionItem:
                this.FunctionItem(call, callable);
                return;
            case BoundTypeKind.Function:
                this.Erased(call, owned);
                return;
            default:
                this.Report(DiagnosticCode.InternalInvariant_Kd, call.Source); // Binding admits only these as callable values (SPEC 3.2.1).
                return;
        }
    }

    // SPEC 7.7.3: a Closure's body, the collector's target, with its environment: the referent of a borrowed operand, or an owned
    // operand's storage, which a Consuming body destroys, and else the call's next target after the call (Rest).
    private void Closure(in MirTerminator call, bool owned)
    {
        var target = this.Target(new(this.at, this.body.Blocks[this.at].Count));
        if (target < 0 || this.collector.Set.Items[target].Key.Kind != MonoKind.Body)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, call.Source); // A Closure's call targets its body (K package 4.5 row 5).
            return;
        }

        if (this.module.Signature(target) is not { } signature)
        {
            return; // A broken contract, recorded.
        }

        if (!this.Argument(call.Operand, this.CalleeCopy(), owned ? AbiParameterKind.OwnedSlot : AbiParameterKind.Value, out var environment))
        {
            return; // A broken contract, recorded.
        }

        if (this.Pass(call, signature, new(call.Callee.Symbol, MirBodyKind.Fn), [environment, new('n', 0)]))
        {
            var result = this.WriteCall(signature, this.arguments.AsSpan(0, signature.Parameters.Length));
            if (!owned || this.Rest(call, environment))
            {
                this.Finish(call, result);
            }
        }
    }

    // SPEC 7.7.4: a function Item's call, what it runs (Runs) over the Value call's operands placed by its own key; a compiler function's of
    // the Body disposition its direct call's legacy row (Legacy; DECISIONS R5-M116); the zero-sized callee value writes nothing.
    private void FunctionItem(in MirTerminator call, BoundType item)
    {
        var target = this.Target(new(this.at, this.body.Blocks[this.at].Count));
        if (LegacyRuntimeHelpers.Row(item.Symbol?.CompilerFunction ?? CompilerFunctionKind.None) is { } row)
        {
            this.Legacy(call, row, target, this.typeContext.SubstOfItem(item));
            return;
        }

        if (this.Runs(item, target, this.InstanceType(call.Place), call.Source, out var given) is { } run)
        {
            this.Invoke(call, run.Signature, new(call.Callee.Symbol, MirBodyKind.Fn), given, run.Slot);
        }
    }

    // SPEC 7.7.4 (DECISIONS R5-M108): what a call of a function Item runs, decided by its function, for its Value call and its adapter's
    // entry: a compiler function's call is its direct call's row whatever targets the row needs (a format witness, a descriptor), written
    // when it is one runtime routine (Routine, its slot-less values from the call's result Type, its Type argument or its target into
    // `given`, a formatting row's capacity measured by the adapter's entry, Forward; DECISIONS R5-M128); any other function's is what
    // its target runs (the collector's: its function's body or a requirement Item's witness; FunctionCodegen.Calls' Witness, a virtual
    // witness through its slot); a virtual function's Item, which has no target (X5), runs its slot (SPEC 6.4.3) under the Item's own
    // substitution (ModuleCodegen.Slot); a #LibraryImport function's Item, which has no target either, its import by its C signature, the
    // rule of its direct call (ModuleCodegen.Import; SPEC 22.6, DECISIONS R5-M146).
    // Null after a record at the source (an adapter's: its site): a broken contract (above).
    private Run? Runs(BoundType item, int target, BoundType? result, SourceInfo source, out ReadOnlySpan<Value> given)
    {
        given = default;
        var kind = item.Symbol?.CompilerFunction ?? CompilerFunctionKind.None;
        if (kind == CompilerFunctionKind.None && target >= 0)
        {
            return this.Witness(target, source); // Null after a record.
        }

        if (kind == CompilerFunctionKind.None && item.Symbol is { } function && VTableDef.SlotOf(function) >= 0)
        {
            return this.module.Slot(item, this.item) is { } slot ? new Run(slot, function) : null; // Null after a record.
        }

        if (kind == CompilerFunctionKind.None && item.Symbol is { } foreign && this.module.Import(foreign) is { } import)
        {
            return new Run(import, null); // SPEC 22.6: its C signature, as its direct call (Function).
        }

        if (kind != CompilerFunctionKind.None && this.Routine(item, result, target, source, out given) is { } routine)
        {
            return new Run(routine, null);
        }

        this.Report(DiagnosticCode.InternalInvariant_Kd, source); // A broken contract (above).
        return null;
    }

    // IMPL 21.1.3: a common Function value's call, through its table's entry with its environment word and its table's context, under the
    // erased signature of the call's Callable signature in the item's instance; an owned (by-value) value is destroyed after the call by
    // the call's target (Rest).
    private void Erased(in MirTerminator call, bool owned)
    {
        if (this.collector.TypeOf(this.item, this.body.Consts[call.Callee.Arg].Type) is not { } declared)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, call.Source);
            return;
        }

        if (!this.Argument(call.Operand, -1, owned ? AbiParameterKind.OwnedSlot : AbiParameterKind.Value, out var value))
        {
            return; // A broken contract, recorded.
        }

        var handle = Layout.Row(declared).Layout.FieldOffsets.Span; // A Function Type's row: its environment word, then its table.
        var environment = this.LoadAt(this.Offset(value, handle[0]), WordLayout);
        var table = this.LoadAt(this.Offset(value, handle[1]), PointerLayout);
        var entry = this.LoadAt(this.Offset(table, TableEntry), PointerLayout);
        var context = this.LoadAt(this.Offset(table, TableContext), PointerLayout);
        var signature = this.module.Erased(declared);
        if (this.Pass(call, signature, new(call.Callee.Symbol, MirBodyKind.Fn), [environment, context]))
        {
            var result = this.WriteCall(signature, this.arguments.AsSpan(0, signature.Parameters.Length), entry);
            if (!owned || this.Rest(call, value))
            {
                this.Finish(call, result);
            }
        }
    }

    // SPEC 8.10: after an owner call whose callee borrows its environment, the operand at the address passed is destroyed by the call
    // site's next target, its Type's glue, at the call's location; no next target, nothing (a Consuming body destroys its environment, or
    // the Type needs no drop). False after a record.
    private bool Rest(in MirTerminator call, Value address)
    {
        var rest = this.Target(new(this.at, this.body.Blocks[this.at].Count));
        if (rest < 0)
        {
            return true;
        }

        if (this.collector.Set.Items[rest].Key.Kind != MonoKind.DropGlue)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, call.Source); // A call's second target is its operand's glue (DECISIONS R5-M102).
            return false;
        }

        if (!this.Location(call.Source, out var site))
        {
            return false;
        }

        this.Destroy(this.module.Glue(rest), address, site);
        return true;
    }

    // What a call runs (Runs): its signature, and the function whose virtual slot it runs through (null: none), which only makes the callee
    // value the call writes (EntryOf), never how it writes the call.
    private readonly record struct Run(FunctionAbi Signature, BindingSymbol? Slot);
}
