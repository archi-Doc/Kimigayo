// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;

namespace Kimi.Compiler.Backend;

// The object descriptors of ModuleCodegen (IMPL 21.1.3, SPEC 3.3.1, 13.6, 16.3.3; R5 plan C6a; DECISIONS R5-M74, R5-M110): the module's
// runtime identities, numbered once per Write before the items (Number): the erased payload of each Generated ObjectDescriptor item in
// admission order, then each base on its chain (AdtDef.Base), each erased by TyCtxt.Erase as MonoKey.Type is, so equal Types share one
// number, from 1 at its first occurrence, a base no creation makes included; a descriptor's token and base tokens and a type test's target
// read them (Token). A descriptor item's row (ObjectCreation: its payload's representation, Copy by TyCtxt.Copy on the representative, its
// factory's signature, ObjectRuntime.Factory, its token, its payload glue's symbol, the descriptor's first target when the payload may need a
// drop, and its chain's tokens nearest first) is computed once per Write and the previous one reused while it matches, so a warm module
// allocates nothing; ObjectRuntime writes it into the function buffer, and the frame writes the object runtime once on request
// (Needs.Objects). A descriptor whose payload's struct has a slot, its VTable target, is C6c's: one located Unsupported_Kd at its site, where
// the VTable item's own record lies, and no partial table.
internal sealed partial class ModuleCodegen
{
    private readonly Dictionary<BoundType, int> numbers = new(); // By erased Type, its number (Number).
    private readonly List<int> chain = []; // A row's base tokens, nearest first.
    private ObjectCreation?[] objects = []; // By descriptor item, its row, kept across Writes.
    private int[] built = []; // By descriptor item, the Write whose row objects holds (0: none).

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
    // copies by memcpy, the memory intrinsics; a VTable target is C6c's.
    private void Descriptor(int item)
    {
        foreach (var target in this.set.Targets(item))
        {
            if (this.set.Items[target.Item].Key.Kind == MonoKind.VTable)
            {
                this.Report(DiagnosticCode.Unsupported_Kd, this.set.Items[item].Site); // C6c writes the descriptor with its table (R5-M74).
                return;
            }
        }

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
        this.chain.Clear();
        for (var layer = AdtDef.Base(entry.Key.Type!); layer is not null; layer = AdtDef.Base(layer))
        {
            this.chain.Add(this.Token(layer));
        }

        if (this.objects[item] is { } previous && previous.Payload == value && previous.Copy == copy && previous.TypeToken == token &&
            previous.Destroy == destroy && (value.Layout.Size == 0 || previous.Abi.Parameters[1].Kind == kind) &&
            previous.BaseTokens.AsSpan().SequenceEqual(CollectionsMarshal.AsSpan(this.chain)))
        {
            return previous;
        }

        return this.objects[item] = new(item, value, copy, ObjectRuntime.Factory(item, value, kind), token)
        {
            Destroy = destroy,
            BaseTokens = [.. this.chain],
        };
    }
}
