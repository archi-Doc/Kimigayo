// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

// The common Function values of FunctionCodegen (R5 plans C5b and C5c, package C 3.2 and 3.6, PD3, PD19; SPEC 7.7.4, 10.7, 16.4, 22.5.1,
// IMPL 21.1.3; DECISIONS R5-M104, R5-M108): erasure, the adapter items' functions and a Function value's destruction, every call through the one call writer.
// A common Function value is IMPL 21.1.3's 16 bytes, its environment word at 0 and its table's address at 8 (Layout's FunctionHandle). An
// adapter item's table is a private constant {entry, context, drop} at the offsets FunctionCodegen.Closures reads: its entry, a null
// context and its drop entry, which takes a glue function's parameters at the address of a copy of the environment word (DestroyErased):
// the environment's own glue for an inline environment that may need a drop, the adapter's release for a heap one (its glue, then the
// heap release), else null.
// Erasure (a Cast of the Erasure kind): the source as the builder acquired it; an environment of 8 bytes or less and alignment 8 or less
// is put in the zero-filled word, also one that needs destruction (PD19); a larger one is allocated when the value is formed, at the
// conversion's location (SPEC 7.7.4, 22.5.1), the heap's 16-byte alignment covering every Layout alignment (IMPL 21.4.3; DECISIONS
// R5-M92), and its address put in the word; a function Item has no environment. Then the table of the collector's adapter item at the site.
// An adapter item is keyed by its source and its target Function Type, so its entry (WriteAdapter) has the erased signature every erased
// call of that Type writes (ModuleCodegen.Adapter) and calls what the source's call runs with its own parameters and the location it
// received (Forward): a ClosureErasure's Shared Closure body with its environment (the word spilled for an inline one, the heap pointer) and
// a null context; an ItemAdapter's what its Item's call runs (FunctionCodegen.Closures' Runs: a function's body, a requirement Item's
// witness, a virtual function's slot through the receiver parameter's object, a compiler function's routine row with its slot-less values,
// any other a broken contract at the adapter's site; an update Item's entry writes the update over its own parameters,
// FunctionCodegen.Updates, DECISIONS R5-M116); a Never-result callee leaves the entry's result unused and ends it unreachable (SPEC 10.7),
// also where the entry's own result is another (a slot's signature is its Item's own, ModuleCodegen.Slot).
// A Function value's glue row (DestroyErased, only in its glue function): its table's drop entry, when present, at the address of a copy
// of its environment word in a slot of the glue's own, so the value's address does not escape into the call and an optimizer can drop
// the value's table store and with it an entry no call reaches (G182).
internal sealed partial class FunctionCodegen
{
    // A drop entry's signature, a glue function's (DropGlue.Parameters), called through a table's pointer.
    private static readonly FunctionAbi DropEntry = new(string.Empty, "void", DropGlue.Parameters);

    private static readonly TypeLayout WordLayout = Layout.Row(BoundType.ISize).Layout; // The environment word, an i64.

    private static readonly TypeLayout PointerLayout = RuntimeAbi.StringReference.Layout;

    /// <summary>Gets whether an adapter's own parameter passes what a callee's parameter of the same logical index takes (Forward; DECISIONS
    /// R5-M121): one of the same kind, or a SharedReference and a Value of the same Type when that Type is a pointer, since a SharedReference
    /// is a Value with attributes (<see cref="AbiParameter.Attributes"/>), so both lower to the same pointer (a string borrow at a legacy
    /// helper's by-value pointer input). One rule for every adapter; a Value of another Type or an OwnedSlot is not passed so.</summary>
    /// <param name="own">The adapter's parameter.</param>
    /// <param name="parameter">The callee's parameter.</param>
    /// <returns>Whether the one passes the other.</returns>
    internal static bool Passes(in AbiParameter own, in AbiParameter parameter)
        => own.Kind == parameter.Kind || (own.Type == parameter.Type && own.Type == PointerLayout.StorageType && ByValue(own.Kind) && ByValue(parameter.Kind));

    /// <summary>Writes the table and functions of a ClosureErasure or ItemAdapter item, or records why not.</summary>
    /// <param name="collector">The collector of the checked set.</param>
    /// <param name="item">The adapter item.</param>
    /// <param name="output">The module's buffer.</param>
    internal void WriteAdapter(InstanceCollector collector, int item, TextWriter output)
    {
        (this.collector, this.item, this.output, this.next) = (collector, item, output, 0);
        var entry = this.module.Adapter(item);
        var targets = collector.Set.Targets(item);
        var key = collector.Set.Items[item].Key;
        var represented = collector.Set.Items[item].RepresentativeType!;
        var closure = key.Kind == MonoKind.ClosureErasure;
        if (!closure && LegacyRuntimeHelpers.Row(represented.Symbol?.CompilerFunction ?? CompilerFunctionKind.None) is { Updates: true } row)
        {
            this.WriteUpdateEntry(item, entry, row.Writer, targets.Length > 0 ? targets[0].Item : -1); // Its direct call's row (FunctionCodegen.Updates).
            return;
        }

        var given = default(ReadOnlySpan<Value>);
        Run? run;
        if (closure)
        {
            run = this.module.Signature(targets[0].Item) is { } body ? new Run(body, null) : null;
        }
        else
        {
            run = this.Runs(represented, targets.Length > 0 ? targets[0].Item : -1, key.Function!.Components[1], default, out given);
        }

        if (run is not { Signature: var callee, Slot: var slot } || this.Laid(represented) is not { } source)
        {
            return; // A broken contract, recorded.
        }

        var heap = Heap(source.Layout);
        var glue = closure && targets.Length > 1 ? targets[1].Item : -1;
        var drop = (heap, glue) switch
        {
            (true, _) => this.module.Release(item),
            (false, >= 0) => this.module.Glue(glue),
            _ => null,
        };
        this.Table(item, entry, drop);
        this.abi = entry;
        output.Write(entry.GetDefinition(exported: false));
        output.Write("entry:\n");
        var forwarded = closure
            ? this.Forward(callee, [this.Environment(heap), new('n', 0)], null, out var result)
            : this.Forward(callee, given, slot, out result);
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

    // Whether a parameter of a kind passes its value itself, a Value or a SharedReference (a Value with attributes).
    private static bool ByValue(AbiParameterKind kind) => kind is AbiParameterKind.Value or AbiParameterKind.SharedReference;

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

        var function = this.InstanceType(statement.Place);
        var operand = this.body.Operands[statement.Rvalue.Start];
        var type = this.InstanceType(operand);
        if (this.Laid(type) is not { } source || this.Laid(function) is not { } handle)
        {
            return; // A broken contract, recorded.
        }

        if (!this.Operand(operand, out var value))
        {
            return; // A broken contract, recorded.
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
    // result slot, the location the erased call passed, SPEC 22.5.1), a capacity Context the length of the fixed array its parameter
    // receives, measured from the entry's own input Type, its target Function Type's at that index (Measure; DECISIONS R5-M128; read only
    // there, so an item without a Function Type forwards its parameters too when its callee measures nothing), a parameter
    // without an operand (Given: an environment, a context or a formatting row's kind) the next given value, then the call of its callee
    // value, which the slot it runs through, if any, makes (EntryOf); its value (none for a void callee). False, with InternalInvariant_Kd,
    // where the adapter has no such parameter.
    private bool Forward(FunctionAbi callee, ReadOnlySpan<Value> given, BindingSymbol? slot, out Value? result)
    {
        result = null;
        var parameters = callee.Parameters;
        this.arguments = this.arguments.Length < parameters.Length ? new Value[parameters.Length] : this.arguments;
        var next = 0;
        for (var i = 0; i < parameters.Length; i++)
        {
            if (Given(parameters[i]))
            {
                this.arguments[i] = given[next++];
                continue;
            }

            if (parameters[i].Kind == AbiParameterKind.Context)
            {
                if (this.Measure(this.collector.Set.Items[this.item].Key.Function!.Components[0].Components[parameters[i].LogicalIndex]) is not { } length)
                {
                    return false; // Recorded.
                }

                this.arguments[i] = length;
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

        var arguments = this.arguments.AsSpan(0, parameters.Length);
        result = this.WriteCall(callee, arguments, this.EntryOf(callee, slot));
        return true;
    }

    // The adapter's parameter of a callee parameter's logical index that passes what it takes (Passes), or -1.
    private int Own(in AbiParameter parameter)
    {
        var own = this.abi.Parameters;
        for (var i = 0; i < own.Length; i++)
        {
            if (own[i].LogicalIndex == parameter.LogicalIndex && Passes(own[i], parameter))
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

    // SPEC 16.4 (above): a Function value's destruction at its address, written first in its glue's entry block: the word's slot, then its
    // table's drop entry called when present, at the site, on the word moved to the slot (the value is dead after its drop, and an
    // adapter entry already passes an inline word so, Environment).
    private void DestroyErased(BoundType type, Value address, Site site)
    {
        if (this.Laid(type) is not { } handle)
        {
            return; // A broken contract, recorded.
        }

        var slot = this.next++;
        this.WriteSlot("  %t", slot, WordLayout);
        var word = new Value('t', slot);
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
        this.StoreAt(word, WordLayout, this.LoadAt(this.Offset(address, handle.Layout.FieldOffsets.Span[0]), WordLayout));
        this.Destroy(DropEntry, word, site, drop);
        this.Join(id);
    }
}
