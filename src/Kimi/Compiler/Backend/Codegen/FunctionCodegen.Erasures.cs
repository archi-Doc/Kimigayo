// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The common Function values of FunctionCodegen (R5 plan C5b, package C 3.2 and 3.6, PD3, PD19; SPEC 7.7.4, 16.4, 22.5.1, IMPL 21.1.3;
// DECISIONS R5-M104): erasure, the adapter items' functions and a Function value's destruction, every call through the one call writer.
// A common Function value is IMPL 21.1.3's 16 bytes, its environment word at 0 and its table's address at 8 (Layout's FunctionHandle). An
// adapter item's table is a private constant {entry, context, drop} at the offsets FunctionCodegen.Closures reads: its entry, a null
// context and its drop entry, which takes a glue function's parameters at the Function value's address, the environment word's: the
// environment's own glue for an inline environment that may need a drop, the adapter's release for a heap one (its glue, then the heap
// release), else null.
// Erasure (a Cast of the Erasure kind): the source as the builder acquired it; an environment of 8 bytes or less and alignment 8 or less
// is put in the zero-filled word, also one that needs destruction (PD19); a larger one is allocated when the value is formed, at the
// conversion's location (SPEC 7.7.4, 22.5.1), the heap's 16-byte alignment covering every Layout alignment (IMPL 21.4.3; DECISIONS
// R5-M92), and its address put in the word; a function Item has no environment. Then the table of the collector's adapter item at the site.
// An adapter's entry (WriteAdapter) has the erased signature of its source's own Callable signature (ModuleCodegen.Adapter) and calls the
// source's body with its own parameters and the location it received (Forward): a ClosureErasure's Shared Closure body with its
// environment (the word spilled for an inline one, the heap pointer) and a null context; an ItemAdapter's function or requirement witness.
// Temporary until C5c (DECISIONS R5-M104), which keys an adapter by its target Function Type: an erasure whose target's erased signature
// is not its adapter's entry's (a Never-result source erased into another result) and an adapter without a body target (a compiler
// function's Item, a Builtin or descriptor target) are one located Unsupported_Kd each, at the conversion and at the adapter's site.
// A Function value's glue row (DestroyErased): its table's drop entry, when present, at its address.
internal sealed partial class FunctionCodegen
{
    // A drop entry's signature, a glue function's (DropGlue.Parameters), called through a table's pointer.
    private static readonly FunctionAbi DropEntry = new(string.Empty, "void", DropGlue.Parameters);

    private static readonly TypeLayout WordLayout = Layout.Row(BoundType.ISize).Layout; // The environment word, an i64.

    private static readonly TypeLayout PointerLayout = RuntimeAbi.StringReference.Layout;

    /// <summary>Writes the table and functions of a ClosureErasure or ItemAdapter item, or records why not.</summary>
    /// <param name="collector">The collector of the checked set.</param>
    /// <param name="item">The adapter item.</param>
    /// <param name="output">The module's buffer.</param>
    internal void WriteAdapter(InstanceCollector collector, int item, TextWriter output)
    {
        (this.collector, this.item, this.output, this.next) = (collector, item, output, 0);
        if (this.module.Adapter(item) is not { } entry)
        {
            this.Report(DiagnosticCode.Unsupported_Kd, default); // C5c: an adapter without a body target (above).
            return;
        }

        var targets = collector.Set.Targets(item);
        var closure = collector.Set.Items[item].Key.Kind == MonoKind.ClosureErasure;
        if (this.module.Signature(targets[0].Item) is not { } callee || this.Laid(collector.Set.Items[item].RepresentativeType) is not { } source)
        {
            return; // A broken contract, recorded.
        }

        var heap = Heap(source.Layout);
        var glue = closure && targets.Length > 1 ? targets[1].Item : -1;
        this.Table(item, entry, heap ? this.module.Release(item) : glue >= 0 ? this.module.Glue(glue) : null);
        this.abi = entry;
        output.Write(entry.GetDefinition(exported: false));
        output.Write("entry:\n");
        var forwarded = closure ? this.Forward(callee, [this.Environment(heap), new('n', 0)], out var result) : this.Forward(callee, default, out result);
        if (!forwarded)
        {
            return; // A broken contract, recorded.
        }

        this.End(callee, result);
        if (heap)
        {
            this.Release(item, glue);
        }
    }

    // An environment of more than 8 bytes or of an alignment above 8 is allocated (IMPL 21.1.3).
    private static bool Heap(TypeLayout environment) => environment.Size > 8 || environment.Alignment > 8;

    // SPEC 7.7.4, IMPL 21.1.3 (above): an erasure into its Place, the Function value.
    private void Erase(in MirStatement statement)
    {
        var adapter = this.Target(new(this.at, this.position));
        if (adapter < 0 || this.collector.Set.Items[adapter].Key.Kind is not (MonoKind.ClosureErasure or MonoKind.ItemAdapter))
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, statement.Source); // An erasure targets its adapter item (K package 4.5 row 13).
            return;
        }

        if (this.InstanceType(statement.Place) is not { } function || this.module.Adapter(adapter) is not { } entry || !this.module.Fits(entry, function))
        {
            this.Report(DiagnosticCode.Unsupported_Kd, statement.Source); // C5c (above).
            return;
        }

        var operand = this.body.Operands[statement.Rvalue.Start];
        var type = operand.Kind == MirOperandKind.Const
            ? this.collector.TypeOf(this.item, this.body.Consts[operand.Arg].Type) : this.InstanceType(operand.Place);
        if (this.Laid(type) is not { } source || this.Laid(function) is not { } handle)
        {
            return; // A broken contract, recorded.
        }

        if (!this.Operand(operand, out var value))
        {
            this.Report(DiagnosticCode.Unsupported_Kd, statement.Source);
            return;
        }

        var storage = default(Value?);
        if (Heap(source.Layout))
        {
            if (!this.Location(statement.Source, out var site))
            {
                return; // Recorded.
            }

            storage = this.WriteCall(RuntimeAbi.Allocate, [new('\0', source.Layout.Size), site.Text, site.Length]);
        }

        if (!this.Destination(statement, out var address))
        {
            return; // Recorded.
        }

        var offsets = handle.Layout.FieldOffsets.Span;
        var word = this.Offset(address, offsets[0]);
        if (storage is { } allocated)
        {
            this.Put(allocated, type!, source, value);
            this.StoreAt(word, PointerLayout, allocated);
        }
        else
        {
            this.StoreAt(word, WordLayout, new('\0', 0));
            if (source.Layout.Size > 0)
            {
                this.Put(word, type!, source, value);
            }
        }

        this.StoreAt(this.Offset(address, offsets[1]), PointerLayout, new('g', adapter));
    }

    // An adapter's table (above), before its entry.
    private void Table(int item, FunctionAbi entry, FunctionAbi? drop)
    {
        var output = this.output;
        this.WriteValue(new('g', item));
        output.Write(" = private constant { ptr, ptr, ptr } { ptr @");
        output.Write(entry.Name);
        output.Write(", ptr null, ptr ");
        if (drop is null)
        {
            output.Write("null");
        }
        else
        {
            output.Write('@');
            output.Write(drop.Name);
        }

        output.Write(" }, align 8\n");
    }

    // A ClosureErasure entry's environment pointer: the received word (Abi's i64 [environment]) as the heap address it holds, or spilled
    // to an aligned slot of its own for an inline one, which the Shared body only reads.
    private Value Environment(bool heap)
    {
        var word = new Value('p', 0);
        if (heap)
        {
            return this.Convert("inttoptr ", WordLayout.StorageType, word, PointerLayout.StorageType);
        }

        var slot = this.next++;
        this.WriteSlot("  %t", slot, WordLayout);
        this.StoreAt(new('t', slot), WordLayout, word);
        return new('t', slot);
    }

    // An adapter's call of its callee (PD3): each callee parameter takes the adapter's own of its kind and logical index (a value, the
    // result slot, the location the erased call passed, SPEC 22.5.1), an environment or a context the next given value; its value (none
    // for a void callee). False, with InternalInvariant_Kd, where the adapter has no such parameter.
    private bool Forward(FunctionAbi callee, ReadOnlySpan<Value> given, out Value? result)
    {
        result = null;
        var parameters = callee.Parameters;
        this.arguments = this.arguments.Length < parameters.Length ? new Value[parameters.Length] : this.arguments;
        var next = 0;
        for (var i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].Kind is AbiParameterKind.Environment or AbiParameterKind.Context)
            {
                this.arguments[i] = given[next++];
                continue;
            }

            var own = this.Own(parameters[i]);
            if (own < 0)
            {
                this.Report(DiagnosticCode.InternalInvariant_Kd, default); // The entry's parameters are its source's (ModuleCodegen.Adapter).
                return false;
            }

            this.arguments[i] = new('p', own);
        }

        result = this.WriteCall(callee, this.arguments.AsSpan(0, parameters.Length));
        return true;
    }

    // The adapter's parameter of a callee parameter's kind and logical index, or -1.
    private int Own(in AbiParameter parameter)
    {
        var own = this.abi.Parameters;
        for (var i = 0; i < own.Length; i++)
        {
            if (own[i].Kind == parameter.Kind && own[i].LogicalIndex == parameter.LogicalIndex)
            {
                return i;
            }
        }

        return -1;
    }

    // An adapter entry's end: unreachable after a callee that never returns, else its value or nothing returned.
    private void End(FunctionAbi callee, Value? result)
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

    // A heap environment's release (the table's drop entry, a glue function's parameters): its glue at the address the word holds, then
    // the heap release, each at the location received.
    private void Release(int item, int glue)
    {
        (this.abi, this.next) = (this.module.Release(item), 0);
        this.output.Write(this.abi.GetDefinition(exported: false));
        this.output.Write("entry:\n");
        var environment = this.LoadAt(new('p', 0), PointerLayout);
        if (glue >= 0)
        {
            this.Destroy(this.module.Glue(glue), environment, Received);
        }

        this.Destroy(RuntimeAbi.StorageRelease, environment, Received);
        this.output.Write("  ret void\n}\n");
    }

    // SPEC 16.4 (above): a Function value's destruction at its address, its table's drop entry called when present, at the site.
    private void DestroyErased(BoundType type, Value address, Site site)
    {
        if (this.Laid(type) is not { } handle)
        {
            return; // A broken contract, recorded.
        }

        var id = this.next++;
        var table = this.LoadAt(this.Offset(address, handle.Layout.FieldOffsets.Span[1]), PointerLayout);
        var drop = this.LoadAt(this.Offset(table, TableDrop), PointerLayout);
        var present = new Value('t', this.next++);
        this.output.Write("  ");
        this.WriteValue(present);
        this.output.Write(" = icmp ne ptr ");
        this.WriteValue(drop);
        this.output.Write(", null\n");
        this.Guard(id, present);
        this.Destroy(DropEntry, address, site, drop);
        this.Join(id);
    }
}
