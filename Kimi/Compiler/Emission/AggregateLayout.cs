// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // Physical aggregate descriptors and their reusable pool.

/// <summary>A syntax-free aggregate representation. Fields remain in logical acquisition/destruction order.</summary>
internal sealed record AggregateLayout(int Id, ValueLowering Value, ValueLowering[] Fields, AggregateLayout?[] Children, int Count, bool IsArray, bool NeedsDestruction, int Destructor = -1, AggregateLayout[]? Cases = null, int PayloadOffset = 0, bool FunctionHandle = false, bool ObjectHandle = false, bool CLayout = false)
{
    internal int Offset(int index) => this.IsArray ? checked(index * this.Fields[0].Layout.Stride) : this.Value.Layout.FieldOffsets.Span[index];
}

/// <summary>Interns physical shapes independently of bound Type and Origin identities.</summary>
internal sealed class AggregateLayoutPool
{
    private readonly List<AggregateLayout> pool = new();
    private readonly Dictionary<BoundType, AggregateLayout?> resolved = new(ReferenceEqualityComparer.Instance);
    private readonly List<ValueLowering> fields = new();
    private readonly List<AggregateLayout?> children = new();
    private readonly List<AggregateLayout> enumCases = new();
    private readonly Dictionary<FunctionKoto, int> destructors = new(ReferenceEqualityComparer.Instance);
    private AggregateLayout? functionHandle;
    private AggregateLayout? objectHandle;

    internal void RegisterDestructor(FunctionKoto function, int ordinal) => this.destructors[function] = ordinal;

    internal void ClearDestructors() => this.destructors.Clear();

    internal Dictionary<BoundType, AggregateLayout?>.ValueCollection Used => this.resolved.Values;

    internal void Clear() => this.resolved.Clear();

    internal AggregateLayout? Get(BoundType type) => this.Get(type, 0);

    private static long Align(long size, int alignment) => (size + alignment - 1) & -(long)alignment;

    private AggregateLayout? Get(BoundType type, int depth)
    {
        if (this.resolved.TryGetValue(type, out var existing))
        {
            return existing;
        }

        if (EnumStorage.IsEnum(type))
        {
            return this.GetEnum(type, depth);
        }

        if (type.Kind == BoundTypeKind.Function)
        {
            if (this.functionHandle is null)
            {
                this.functionHandle = new(this.pool.Count, new(new("[16 x i8]", 16, 8, 16, new[] { 0, 8 }), "[16 x i8]", "ptr"), [], [], 0, false, true, FunctionHandle: true);
                this.pool.Add(this.functionHandle);
            }

            return this.resolved[type] = this.functionHandle;
        }

        if (ObjectTypes.IsOwner(type))
        {
            if (this.objectHandle is null)
            {
                this.objectHandle = new(this.pool.Count, new(new("[8 x i8]", 8, 8, 8, ReadOnlyMemory<int>.Empty), "[8 x i8]", "ptr"), [], [], 0, false, true, ObjectHandle: true);
                this.pool.Add(this.objectHandle);
            }

            return this.resolved[type] = this.objectHandle;
        }

        var structure = StructStorage.IsStruct(type);
        var cLayout = false;
        if (structure)
        {
            for (var attribute = StructStorage.Declaration(type)!.AttributeChain; attribute is not null; attribute = attribute.AttributeChain)
            {
                if (attribute.LayoutMode == "C")
                {
                    cLayout = true;
                }
            }
        }

        var sequence = type.Kind is BoundTypeKind.ResolvedRange or BoundTypeKind.Slice;
        if (depth == 64 || (!structure && !sequence && type.Kind is not (BoundTypeKind.Tuple or BoundTypeKind.FixedArray or BoundTypeKind.Closure)) ||
            type.Semantics != SemanticsKind.Owner || (!sequence && type.Origin is not null) || (!structure && type.OriginArguments.Count != 0) ||
            (type.Kind == BoundTypeKind.FixedArray && (type.Length < 0 || type.Length > int.MaxValue || type.Components.Count != 1)))
        {
            return null;
        }

        var start = this.fields.Count;
        var array = type.Kind == BoundTypeKind.FixedArray;
        var fieldCount = sequence ? 2 : structure ? StructStorage.Count(type) : type.Components.Count;
        var count = array ? (int)type.Length : fieldCount;
        if (cLayout && (fieldCount == 0 || StructStorage.Declaration(type) is not { Bases.Count: 0 } declaration || (declaration.Modifier & ModifierKind.Open) != 0))
        {
            return this.resolved[type] = null;
        }

        var destructor = StructStorage.Destructor(type) is { } body ? this.destructors.GetValueOrDefault(body, -1) : -1;
        try
        {
            for (var i = 0; i < fieldCount; i++)
            {
                var component = sequence ? BoundType.ISize : structure ? StructStorage.FieldType(type, i)! : type.Components[i];
                var child = this.Get(component, depth + 1);
                var value = type.Kind == BoundTypeKind.Slice && i == 0 ? WindowsLowering.StringReference : child?.Value ?? (ReferenceTypes.IsValue(component) || ReferenceEquals(component, BoundType.Unit) || ReferenceEquals(component, BoundType.String) ? WindowsLowering.GetValue(component) : null);
                if (value is null || (cLayout && (value.Layout.Size == 0 || value.Layout.Alignment > 16)))
                {
                    this.resolved[type] = null;
                    return null;
                }

                this.fields.Add(value);
                this.children.Add(child);
            }

            foreach (var candidate in this.pool)
            {
                if (candidate.FunctionHandle || candidate.ObjectHandle || candidate.Cases is not null || candidate.CLayout != cLayout || candidate.IsArray != array || candidate.Count != count || candidate.Fields.Length != fieldCount || candidate.Destructor != destructor)
                {
                    continue;
                }

                var equal = true;
                for (var i = 0; i < candidate.Fields.Length; i++)
                {
                    equal &= ReferenceEquals(candidate.Fields[i], this.fields[start + i]);
                }

                if (equal)
                {
                    this.resolved[type] = candidate;
                    return candidate;
                }
            }

            var alignment = 1;
            var destroy = destructor >= 0;
            for (var i = 0; i < fieldCount; i++)
            {
                alignment = Math.Max(alignment, this.fields[start + i].Layout.Alignment);
                destroy |= ReferenceEquals(this.fields[start + i], WindowsLowering.String) || this.children[start + i]?.NeedsDestruction == true;
            }

            // The existing physical plan uses signed 32-bit byte offsets. Reject its
            // limit explicitly before any LLVM parser can truncate a size or offset.
            long size = 0;
            var offsets = array ? Array.Empty<int>() : new int[fieldCount];
            if (array)
            {
                size = (long)count * this.fields[start].Layout.Stride;
                destroy &= count != 0;
            }
            else if (cLayout)
            {
                // Windows x64 /Zp16: natural alignment (at most 16), written field order.
                for (var i = 0; i < fieldCount; i++)
                {
                    var layout = this.fields[start + i].Layout;
                    size = Align(size, layout.Alignment);
                    if (size > int.MaxValue)
                    {
                        return this.resolved[type] = null;
                    }

                    offsets[i] = (int)size;
                    size += layout.Size;
                }

                size = Align(size, alignment);
            }
            else
            {
                for (var a = alignment; a != 0; a >>= 1)
                {
                    for (var i = 0; i < fieldCount; i++)
                    {
                        var layout = this.fields[start + i].Layout;
                        if (layout.Alignment != a)
                        {
                            continue;
                        }

                        size = Align(size, a);
                        if (size > int.MaxValue)
                        {
                            this.resolved[type] = null;
                            return null;
                        }

                        offsets[i] = (int)size;
                        size += layout.Size;
                    }
                }

                size = Align(size, alignment);
            }

            if (size > int.MaxValue)
            {
                this.resolved[type] = null;
                return null;
            }

            // A zero-length integer carrier preserves natural alignment without
            // making padding into a typed source-language value.
            string storage;
            if (cLayout)
            {
                // Ordinary LLVM structs derive the same ABI padding; never use packed structs.
                var builder = new StringBuilder("{ ");
                for (var i = 0; i < fieldCount; i++)
                {
                    if (i != 0)
                    {
                        builder.Append(", ");
                    }

                    builder.Append(this.fields[start + i].Layout.StorageType);
                }

                storage = builder.Append(" }").ToString();
            }
            else
            {
                storage = "{ [0 x i" + (alignment * 8).ToString(CultureInfo.InvariantCulture) + "], [" + size.ToString(CultureInfo.InvariantCulture) + " x i8] }";
            }

            var representation = new ValueLowering(new(storage, (int)size, alignment, (int)size, offsets), storage, "ptr");
            var result = new AggregateLayout(this.pool.Count, representation, CollectionsMarshal.AsSpan(this.fields).Slice(start, fieldCount).ToArray(), CollectionsMarshal.AsSpan(this.children).Slice(start, fieldCount).ToArray(), count, array, destroy, destructor, CLayout: cLayout);
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
        if (depth == 64 || type.Origin is not null ||
            type.OriginArguments.Count != (type.Symbol?.Schema?.Origins.Count ?? 0) || type.StoredCases is not { Length: > 0 } types)
        {
            return null;
        }

        this.resolved[type] = null; // Reject recursive inline representations.
        var start = this.enumCases.Count;
        try
        {
            var alignment = 1;
            long payloadSize = 0;
            var destroy = false;
            for (var i = 0; i < types.Length; i++)
            {
                if (this.Get(types[i], depth + 1) is not { } payload)
                {
                    return null;
                }

                this.enumCases.Add(payload);
                alignment = Math.Max(alignment, payload.Value.Layout.Alignment);
                payloadSize = Math.Max(payloadSize, payload.Value.Layout.Size);
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

            var offset = (int)Align(4, alignment);
            var size = Align(offset + Align(payloadSize, alignment), Math.Max(4, alignment));
            if (size > int.MaxValue)
            {
                return null;
            }

            var storage = "{ i32, { [0 x i" + (alignment * 8).ToString(CultureInfo.InvariantCulture) + "], [" + Align(payloadSize, alignment).ToString(CultureInfo.InvariantCulture) + " x i8] } }";
            var value = new ValueLowering(new(storage, (int)size, Math.Max(4, alignment), (int)size, new[] { 0, offset }), storage, "ptr");
            var result = new AggregateLayout(this.pool.Count, value, [], [], 0, false, destroy, Cases: cases.ToArray(), PayloadOffset: offset);
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
