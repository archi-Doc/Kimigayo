// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;

namespace Kimi.Compiler.Backend;

// The object descriptors of ModuleCodegen (IMPL 21.1.3, SPEC 3.3.1, 6.4, 13.6, 16.3.3; R5 plans C6a, C6c; DECISIONS R5-M74, R5-M110,
// R5-M114): the module's runtime identities, numbered once per Write before the items (Number): the erased payload of each Generated
// ObjectDescriptor item in admission order, then each base on its chain (AdtDef.Base), each erased by TyCtxt.Erase as MonoKey.Type is, so
// equal Types share one number, from 1 at its first occurrence, a base no creation makes included; a descriptor's token and base tokens and a
// type test's target read them (Token). A descriptor item's row (ObjectCreation: its payload's representation, Copy by TyCtxt.Copy on the
// representative, its factory's signature, ObjectRuntime.Factory, its token, its payload glue's symbol, the descriptor's first target when
// the payload may need a drop, its chain's tokens nearest first, and its VTable target's entries, Table) is computed once per Write and the
// previous one reused while it matches, so a warm module allocates nothing; ObjectRuntime writes it into the function buffer, and the frame
// writes the object runtime once on request (Needs.Objects). A VTable item's text is its descriptor's virtual table (one per descriptor:
// both are keyed by the erased payload, and the descriptor is the VTable's one admitter), so it writes nothing of its own. Its entries, in
// slot order (VTableDef): slot s runs the next of its targets whose body's definition is Implementations[s] (DECISIONS R5-M74: the
// collector's targets are the applying slots' bodies in slot order), called by that body's own signature, which SPEC 6.4.1 makes the slot's;
// a slot without one has none, a refuted slot (R4-M28), which no call reaches (SPEC 6.4.3: a call and an Item acquisition need its
// premise), or a refused admission, whose collector record fails the check. A target left unpaired is a broken contract.
internal sealed partial class ModuleCodegen
{
    private readonly Dictionary<BoundType, int> numbers = new(); // By erased Type, its number (Number).
    private readonly List<int> chain = []; // A row's base tokens, nearest first.
    private readonly List<FunctionAbi?> entries = []; // A table's entries in slot order (Table).
    private ObjectCreation?[] objects = []; // By descriptor item, its row, kept across Writes.
    private int[] built = []; // By descriptor item, the Write whose row objects holds (0: none).
    private FunctionAbi?[]?[] tables = []; // By VTable item, its entries, kept across Writes.
    private int[] tabled = []; // By VTable item, the Write whose entries tables holds (0: none).

    /// <summary>Gets the number of a closed Type's runtime identity in the module (SPEC 3.3.1: its erasure; DECISIONS R5-M74, R5-M110), which
    /// a type test compares an object's descriptor with.</summary>
    /// <param name="type">A closed Type.</param>
    /// <returns>The number, or -1 for a Type on no Generated descriptor's chain, which no runtime object supports.</returns>
    internal int Token(BoundType type) => this.numbers.TryGetValue(this.types.Erase(type), out var number) ? number : -1;

    /// <summary>Gets the factory of an ObjectDescriptor item (SHARED_OBJECT_RUNTIME, Creation; <see cref="ObjectRuntime.Factory"/>), which
    /// a creation of its payload calls, named <c>__kimi_make_object</c> with its index.</summary>
    /// <param name="item">The descriptor item.</param>
    /// <returns>The signature.</returns>
    internal FunctionAbi Factory(int item)
    {
        if (!this.set.IsGenerated(item))
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, this.set.Items[item].Site); // Only a Generated item is requested (K11).
        }

        return this.Row(item).Abi;
    }

    // The module's runtime identities (above), before the items are written.
    private void Number()
    {
        this.numbers.Clear();
        var items = this.set.Items;
        for (var item = 0; item < items.Length; item++)
        {
            if (items[item].Key is { Kind: MonoKind.ObjectDescriptor, Type: { } payload } && this.set.IsGenerated(item))
            {
                for (var layer = (BoundType?)payload; layer is not null; layer = AdtDef.Base(layer))
                {
                    _ = this.numbers.TryAdd(this.types.Erase(layer), this.numbers.Count + 1);
                }
            }
        }
    }

    // A descriptor item (above): its row written through ObjectRuntime, which requests the object runtime and, for a payload its factory
    // copies by memcpy, the memory intrinsics.
    private void Descriptor(int item)
    {
        var row = this.Row(item);
        this.Request(ObjectRuntime.Copies(row.Payload) ? Needs.Objects | Needs.Memory : Needs.Objects);
        ObjectRuntime.WriteObject(this.functions, row);
    }

    // A descriptor item's row in this Write (above), the previous Write's while it matches.
    private ObjectCreation Row(int item)
    {
        if (this.Stamped(ref this.objects, ref this.built, item) is { } current)
        {
            return current;
        }

        this.built[item] = this.writes;
        var entry = this.set.Items[item];
        var targets = this.set.Targets(item);
        var payload = entry.RepresentativeType!;
        var value = this.Layout.Of(payload).Value!; // The collector admits a type-driven item with an Ok verdict (K package 4.8).
        var copy = this.types.Copy(null, payload);
        var kind = Abi.Kind(Abi.Convention.Internal, payload);
        var destroy = targets.Length > 0 && this.set.Items[targets[0].Item].Key.Kind == MonoKind.DropGlue ? this.Glue(targets[0].Item).Name : null;
        var token = this.Token(entry.Key.Type!);
        var slots = targets.Length > 0 && this.set.Items[targets[^1].Item].Key.Kind == MonoKind.VTable ? this.Table(targets[^1].Item) : [];
        this.chain.Clear();
        for (var layer = AdtDef.Base(entry.Key.Type!); layer is not null; layer = AdtDef.Base(layer))
        {
            this.chain.Add(this.Token(layer));
        }

        if (this.objects[item] is { } previous &&
            (previous.Payload, previous.Copy, previous.TypeToken, previous.Destroy, previous.VirtualSlots) == (value, copy, token, destroy, slots) &&
            (value.Layout.Size == 0 || previous.Abi.Parameters[1].Kind == kind) &&
            previous.BaseTokens.AsSpan().SequenceEqual(CollectionsMarshal.AsSpan(this.chain)))
        {
            return previous;
        }

        return this.objects[item] = new(item, value, copy, ObjectRuntime.Factory(item, value, kind), token)
        {
            Destroy = destroy,
            BaseTokens = [.. this.chain],
            VirtualSlots = slots,
        };
    }

    // A VTable item's entries in slot order in this Write (above), the previous Write's array while they are equal.
    private FunctionAbi?[] Table(int vtable)
    {
        if (this.Stamped(ref this.tables, ref this.tabled, vtable) is { } current)
        {
            return current;
        }

        this.tabled[vtable] = this.writes;
        var implementations = VTableDef.Of(this.set.Items[vtable].RepresentativeType!.Symbol!)!.Implementations;
        var targets = this.set.Targets(vtable);
        var next = 0;
        this.entries.Clear();
        foreach (var implementation in implementations)
        {
            var paired = next < targets.Length &&
                ReferenceEquals(this.program.Bodies[this.set.Items[targets[next].Item].BodyOrdinal].Key.Decl, implementation);
            this.entries.Add(paired ? this.Signature(targets[next++].Item) : null);
        }

        if (next != targets.Length)
        {
            this.Report(DiagnosticCode.InternalInvariant_Kd, this.set.Items[vtable].Site); // A VTable targets its slots' bodies (R5-M74).
        }

        if (this.tables[vtable] is { } previous && previous.AsSpan().SequenceEqual(CollectionsMarshal.AsSpan(this.entries)))
        {
            return previous;
        }

        return this.tables[vtable] = [.. this.entries];
    }
}
