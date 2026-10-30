// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Backend;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // Physical aggregate descriptors and their reusable pool.

/// <summary>A syntax-free aggregate representation. Fields remain in logical acquisition/destruction order.</summary>
internal sealed record AggregateLayout(int Id, ValueLowering Value, ValueLowering[] Fields, AggregateLayout?[] Children, int Count, bool IsArray, bool NeedsDestruction, int Destructor = -1, AggregateLayout[]? Cases = null, int PayloadOffset = 0, bool FunctionHandle = false, ObjectHandleMode? ObjectHandle = null, bool CLayout = false, AggregateLayout? Base = null, string? CollectionDrop = null, string? GenericDestructor = null, int? ObjectPayloadDrop = null)
{
    internal int Offset(int index) => this.IsArray ? checked(index * this.Fields[0].Layout.Stride) : this.Value.Layout.FieldOffsets.Span[index];

    // Generated code addresses a Kimi library record Field by its declared name, at the offset of this layout of `type`.
    internal int Offset(BoundType type, string name) => this.Offset(AdtDef.IndexOf(type, name));

    internal int StorageCount { get; } = Count + (Base?.StorageCount ?? 0);

    internal AggregateLayout StorageLayer(ref int index)
    {
        var layer = this;
        while (layer.Base is { } parent)
        {
            var count = parent.StorageCount;
            if (index >= count)
            {
                index -= count;
                break;
            }

            layer = parent;
        }

        return layer;
    }

    internal int StorageOffset(int index) => this.StorageLayer(ref index).Offset(index);
}

/// <summary>Interns physical shapes independently of bound Type and Origin identities.</summary>
internal sealed class AggregateLayoutPool
{
    // SPEC 21.3.2: the mandatory bound on inline nesting depth; a self-containing Type is rejected by Binding before this.
    private const int DepthLimit = 64;

    private readonly Layout layout;
    private readonly List<AggregateLayout> pool = new();
    private readonly Dictionary<BoundType, AggregateLayout?> resolved = new(ReferenceEqualityComparer.Instance);
    private readonly List<ValueLowering> fields = new();
    private readonly List<AggregateLayout?> children = new();
    private readonly List<AggregateLayout> enumCases = new();
    private readonly Dictionary<FunctionKoto, int> destructors = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, AggregateLayout> collectionFields = new(StringComparer.Ordinal);
    private readonly List<AggregateLayout> usedCollectionFields = new();
    private readonly Dictionary<(ObjectHandleMode Mode, int Drop), AggregateLayout> sealedObjectHandles = new();
    private readonly HashSet<BoundType> activeLayouts = new(ReferenceEqualityComparer.Instance);
    private AggregateLayout? functionHandle;
    private AggregateLayout? objectHandle;
    private AggregateLayout? rcHandle;
    private AggregateLayout? arcHandle;

    /// <summary>Initializes a new instance of the <see cref="AggregateLayoutPool"/> class.</summary>
    /// <param name="layout">The compilation's <see cref="Layout"/>, which places every record this pool interns.</param>
    internal AggregateLayoutPool(Layout layout) => this.layout = layout;

    /// <summary>Gets the compilation's <see cref="Backend.Layout"/>, which the old callers pass to <see cref="Abi"/>.</summary>
    internal Layout Layout => this.layout;

    /// <summary>Gets or sets the source of the element-specific drop helper that stored collection destruction calls; it
    /// registers the helper for the current body and returns its name, or null for an unsupported element.</summary>
    internal Func<BoundType, string?>? CollectionDrop { get; set; }

    /// <summary>Gets or sets the current semantic proof of a complete Sealed object target.</summary>
    internal Func<BoundType, bool>? IsSealedObjectTarget { get; set; }

    /// <summary>Gets the stored collection layouts of the current body, whose destructors release the buffers.</summary>
    internal List<AggregateLayout> UsedCollectionFields => this.usedCollectionFields;

    internal void RegisterDestructor(FunctionKoto function, int ordinal) => this.destructors[function] = ordinal;

    internal void ClearDestructors() => this.destructors.Clear();

    internal Dictionary<BoundType, AggregateLayout?>.ValueCollection Used => this.resolved.Values;

    /// <summary>Gets the exhausted implementation limit, independently of unsupported representations and invalid source.</summary>
    internal string? ResourceLimitFailure { get; private set; }

    internal void Clear()
    {
        this.resolved.Clear();
        this.usedCollectionFields.Clear();
        this.ResourceLimitFailure = null;
    }

    internal AggregateLayout? Get(BoundType type) => this.Get(type, 0);

    internal AggregateLayout? GetStored(BoundType type)
        => type.Kind is BoundTypeKind.Array or BoundTypeKind.Dictionary ? this.CollectionField(type, 0) : this.Get(type, 0);

    // IMPL 21.1.3: Sealed identifies the complete payload, but a direct call also needs its concrete cleanup.
    // Recursive handle edges retain dynamic cleanup; they do not expand an inline layout indefinitely.
    private AggregateLayout? SealedObjectHandle(BoundType type, ObjectHandleMode mode, int depth)
    {
        var payload = type.Components[0];
        if (depth >= DepthLimit || this.activeLayouts.Contains(payload) || this.IsSealedObjectTarget?.Invoke(payload) != true)
        {
            return null;
        }

        var drop = -1;
        if (ReferenceEquals(payload, BoundType.String))
        {
            drop = -2;
        }
        else
        {
            var layout = payload.Kind is BoundTypeKind.Array or BoundTypeKind.Dictionary
                ? this.CollectionField(payload, depth + 1) : this.Get(payload, depth + 1);
            if (layout is not null)
            {
                drop = layout.NeedsDestruction ? layout.Id : -1;
            }
            else if (WindowsLowering.GetValue(payload) is null)
            {
                // A recursive payload can meet an enum/aggregate currently being laid out. This optional
                // optimization must not cache that temporary refusal as a failed independent payload layout.
                foreach (var entry in this.resolved)
                {
                    if (entry.Value is null && !this.activeLayouts.Contains(entry.Key))
                    {
                        this.resolved.Remove(entry.Key);
                    }
                }

                return null;
            }
        }

        if (!this.sealedObjectHandles.TryGetValue((mode, drop), out var handle))
        {
            handle = new(this.pool.Count, this.layout.Of(type).Value!, [], [], 0, false, true, ObjectHandle: mode, ObjectPayloadDrop: drop);
            this.pool.Add(handle);
            this.sealedObjectHandles.Add((mode, drop), handle);
        }

        return handle;
    }

    // A stored collection: its handle layout, destroyed through the element-specific drop helper.
    private AggregateLayout? CollectionField(BoundType array, int depth)
    {
        if (this.CollectionDrop?.Invoke(array) is not { } drop || this.Get(array, depth + 1) is not { } handle)
        {
            return null;
        }

        if (!this.collectionFields.TryGetValue(drop, out var field))
        {
            field = new(this.pool.Count, handle.Value, [], [], 0, false, true, CollectionDrop: drop);
            this.pool.Add(field);
            this.collectionFields.Add(drop, field);
        }

        if (!this.usedCollectionFields.Contains(field))
        {
            this.usedCollectionFields.Add(field);
        }

        return field;
    }

    private AggregateLayout? ExceedLimit(BoundType type, bool depth = false)
    {
        this.ResourceLimitFailure ??= depth ? "Inline layout exceeds the generation depth limit of 64 levels."
            : "Inline layout exceeds the generation limit of 2147483647 bytes or array elements.";
        return this.resolved[type] = null;
    }

    // Layout's record of a Type whose parts this pool accepted; null after mapping a size, count or depth limit to the resource limit
    // and CLayout (a C-layout instance storing a zero-sized Field) to a refusal. Generation follows a complete Binding, so Rejected
    // storage is a broken contract, and so is Oversized: this pool accepted every part (at most 2^31 - 1 bytes, fewer than 2^31 of them)
    // and checks counts itself, so no Type it asks about exceeds 2^63 - 1 bytes (DECISIONS R5-M52).
    private ValueLowering? Represent(BoundType type)
    {
        var layout = this.layout.Of(type);
        if (layout.Outcome is Layout.Outcome.Rejected or Layout.Outcome.Oversized)
        {
            throw new InvalidOperationException($"Generation reached {type.Name}, whose layout is {layout.Outcome}.");
        }

        if (layout.Outcome == Layout.Outcome.CLayout)
        {
            this.resolved[type] = null;
        }
        else if (layout.IsLimit)
        {
            this.ExceedLimit(type, depth: layout.Outcome == Layout.Outcome.DepthLimit);
        }

        return layout.Value;
    }

    private AggregateLayout? Get(BoundType type, int depth)
    {
        if (this.resolved.TryGetValue(type, out var existing))
        {
            return existing;
        }

        if (!this.activeLayouts.Add(type))
        {
            return null;
        }

        try
        {
            return this.GetCore(type, depth);
        }
        finally
        {
            this.activeLayouts.Remove(type);
        }
    }

    private AggregateLayout? GetCore(BoundType type, int depth)
    {
        if (AdtDef.IsEnum(type))
        {
            return this.GetEnum(type, depth);
        }

        if (type.Kind == BoundTypeKind.Function)
        {
            if (this.functionHandle is null)
            {
                this.functionHandle = new(this.pool.Count, this.layout.Of(type).Value!, [], [], 0, false, true, FunctionHandle: true);
                this.pool.Add(this.functionHandle);
            }

            return this.resolved[type] = this.functionHandle;
        }

        if (type.HandleMode is { } mode)
        {
            if (this.SealedObjectHandle(type, mode, depth) is { } sealedHandle)
            {
                return this.resolved[type] = sealedHandle;
            }

            var handle = mode.Counting switch
            {
                ObjectCountingStep.None => this.objectHandle,
                ObjectCountingStep.NonAtomic => this.rcHandle,
                ObjectCountingStep.Atomic => this.arcHandle,
                _ => throw new InvalidOperationException("Unknown object counting step."),
            };
            if (handle is null)
            {
                handle = new(this.pool.Count, this.layout.Of(type).Value!, [], [], 0, false, true, ObjectHandle: mode);
                this.pool.Add(handle);
                switch (mode.Counting)
                {
                    case ObjectCountingStep.None:
                        this.objectHandle = handle;
                        break;
                    case ObjectCountingStep.NonAtomic:
                        this.rcHandle = handle;
                        break;
                    case ObjectCountingStep.Atomic:
                        this.arcHandle = handle;
                        break;
                    default:
                        throw new InvalidOperationException("Unknown object counting step.");
                }
            }

            return this.resolved[type] = handle;
        }

        var structure = AdtDef.IsStruct(type);
        if (depth == DepthLimit)
        {
            return this.ExceedLimit(type, depth: true);
        }

        // SPEC 4.8.6, 4.9.5: a Slice is {buffer, length}; an Array handle is {buffer, length, capacity}.
        var sequence = type.Kind is BoundTypeKind.Slice or BoundTypeKind.Array or BoundTypeKind.Dictionary;
        if ((!structure && !sequence && type.Kind is not (BoundTypeKind.Tuple or BoundTypeKind.FixedArray or BoundTypeKind.Closure or BoundTypeKind.FunctionItem)) ||
            type.Semantics != SemanticsKind.Owner || (!sequence && type.Origin is not null) || (!structure && type.OriginArguments.Count != 0) ||
            (type.Kind == BoundTypeKind.FixedArray && (type.Length < 0 || type.Components.Count != 1)))
        {
            return null;
        }

        if (type.Kind == BoundTypeKind.FixedArray && type.Length > int.MaxValue)
        {
            return this.ExceedLimit(type);
        }

        var parent = structure ? AdtDef.Base(type) : null;
        var baseLayout = parent is null ? null : this.Get(parent, depth + 1);
        if (parent is not null && baseLayout is null)
        {
            return this.resolved[type] = null;
        }

        var start = this.fields.Count;
        var array = type.Kind == BoundTypeKind.FixedArray;
        // A sequence handle's words are Layout's (IMPL 21.1.3); this pool adds their destruction facts.
        var representation = sequence ? this.layout.Of(type).Value : null;
        // A Function Item's Components are its bound generic arguments, not stored fields.
        var fieldCount = representation?.Layout.FieldOffsets.Length ??
            (structure ? AdtDef.Count(type) : type.Kind == BoundTypeKind.FunctionItem ? 0 : type.Components.Count);
        var count = array ? (int)type.Length : fieldCount;
        // Shapes stay apart by layout mode (Binding's AdtDef.IsCLayout); Layout places both.
        var cLayout = structure && type.Symbol!.Adt is { IsCLayout: true };
        var body = AdtDef.Destructor(type);
        var destructor = body is not null ? this.destructors.GetValueOrDefault(body, -1) : -1;
        if (destructor < 0 && body is not null)
        {
            return this.resolved[type] = null; // An unprepared destructor must never become trivial cleanup.
        }

        try
        {
            for (var i = 0; i < fieldCount; i++)
            {
                var component = sequence ? BoundType.ISize : structure ? AdtDef.FieldType(type, i)! : type.Components[i];
                AggregateLayout? child;
                if (component.Kind is BoundTypeKind.Array or BoundTypeKind.Dictionary)
                {
                    // SPEC 4.5, 16.3.2: destruction follows the stored Type in every containing aggregate.
                    if (this.CollectionField(component, depth) is not { } collection)
                    {
                        this.resolved[type] = null;
                        return null;
                    }

                    child = collection;
                }
                else
                {
                    child = this.Get(component, depth + 1);
                }

                // The scalar-value gate gives Never no record, so a Never Field keeps this pool's null (STATUS) though Layout lays it out.
                var value = sequence && i == 0 ? RuntimeAbi.StringReference : child?.Value ?? WindowsLowering.GetValue(component);
                if (value is null)
                {
                    this.resolved[type] = null;
                    return null;
                }

                this.fields.Add(value);
                this.children.Add(child);
            }

            // PLAN G33: the compiler never destroys an InlineStorage; its owner destroys the contents it still holds.
            var destroy = destructor >= 0 || baseLayout?.NeedsDestruction == true;
            for (var i = 0; i < fieldCount; i++)
            {
                destroy |= ReferenceEquals(this.fields[start + i], RuntimeAbi.String) || this.children[start + i]?.NeedsDestruction == true;
            }

            destroy &= (!array || count != 0) && !(structure && type.Symbol?.LibraryDeclaration == KimiDeclarationId.InlineStorage);
            foreach (var candidate in this.pool)
            {
                if (candidate.FunctionHandle || candidate.ObjectHandle is not null || candidate.Cases is not null || candidate.CLayout != cLayout || candidate.IsArray != array || candidate.Count != count || candidate.Fields.Length != fieldCount || candidate.Destructor != destructor || !ReferenceEquals(candidate.Base, baseLayout) || candidate.NeedsDestruction != destroy)
                {
                    continue;
                }

                var equal = true;
                for (var i = 0; i < candidate.Fields.Length; i++)
                {
                    equal &= ReferenceEquals(candidate.Fields[i], this.fields[start + i]) && ReferenceEquals(candidate.Children[i], this.children[start + i]);
                }

                if (equal)
                {
                    this.resolved[type] = candidate;
                    return candidate;
                }
            }

            representation ??= this.Represent(type);
            if (representation is null)
            {
                return null;
            }

            var result = new AggregateLayout(this.pool.Count, representation, CollectionsMarshal.AsSpan(this.fields).Slice(start, fieldCount).ToArray(), CollectionsMarshal.AsSpan(this.children).Slice(start, fieldCount).ToArray(), count, array, destroy, destructor, CLayout: cLayout, Base: baseLayout);
            this.pool.Add(result);
            this.resolved[type] = result;
            return result;
        }
        finally
        {
            this.fields.RemoveRange(start, this.fields.Count - start);
            this.children.RemoveRange(start, this.children.Count - start);
        }
    }

    private AggregateLayout? GetEnum(BoundType type, int depth)
    {
        if (depth == DepthLimit)
        {
            return this.ExceedLimit(type, depth: true);
        }

        if (type.Origin is not null ||
            type.OriginArguments.Count != (type.Symbol?.Schema?.Origins.Count ?? 0) || AdtDef.CaseTypes(type) is not { Length: > 0 } types)
        {
            return null;
        }

        this.resolved[type] = null; // Reject recursive inline representations.
        var start = this.enumCases.Count;
        try
        {
            var destroy = false;
            for (var i = 0; i < types.Length; i++)
            {
                if (types[i] is not { } stored || this.Get(stored, depth + 1) is not { } payload)
                {
                    return null;
                }

                this.enumCases.Add(payload);
                destroy |= payload.NeedsDestruction;
            }

            var cases = CollectionsMarshal.AsSpan(this.enumCases).Slice(start, types.Length);
            foreach (var candidate in this.pool)
            {
                if (candidate.Cases is { } previous && previous.AsSpan().SequenceEqual(cases))
                {
                    this.resolved[type] = candidate;
                    return candidate;
                }
            }

            if (this.Represent(type) is not { } value)
            {
                return null;
            }

            // Layout places the tag at offset 0 and the payload union at the second offset.
            var payloadOffset = value.Layout.FieldOffsets.Span[1];
            var result = new AggregateLayout(this.pool.Count, value, [], [], 0, false, destroy, Cases: cases.ToArray(), PayloadOffset: payloadOffset);
            this.pool.Add(result);
            this.resolved[type] = result;
            return result;
        }
        finally
        {
            this.enumCases.RemoveRange(start, this.enumCases.Count - start);
        }
    }
}
