// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // Physical aggregate descriptors and their reusable pool.

/// <summary>A syntax-free aggregate representation. Fields remain in logical acquisition/destruction order.</summary>
internal sealed record AggregateLayout(int Id, ValueLowering Value, ValueLowering[] Fields, AggregateLayout?[] Children, int Count, bool IsArray, bool NeedsDestruction, int Destructor = -1)
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
    private readonly Dictionary<FunctionKoto, int> destructors = new(ReferenceEqualityComparer.Instance);

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

        var structure = StructStorage.IsStruct(type);
        if (depth == 64 || (!structure && type.Kind is not (BoundTypeKind.Tuple or BoundTypeKind.FixedArray)) ||
            type.Semantics != SemanticsKind.Owner || type.Origin is not null || (!structure && type.OriginArguments.Count != 0) ||
            (type.Kind == BoundTypeKind.FixedArray && (type.Length < 0 || type.Length > int.MaxValue || type.Components.Count != 1)))
        {
            return null;
        }

        var start = this.fields.Count;
        var array = type.Kind == BoundTypeKind.FixedArray;
        var fieldCount = structure ? StructStorage.Count(type) : type.Components.Count;
        var count = array ? (int)type.Length : fieldCount;
        var destructor = StructStorage.Destructor(type) is { } body ? this.destructors.GetValueOrDefault(body, -1) : -1;
        try
        {
            for (var i = 0; i < fieldCount; i++)
            {
                var component = structure ? StructStorage.Field(type, i).BoundType! : type.Components[i];
                var child = this.Get(component, depth + 1);
                var value = child?.Value ?? (ReferenceTypes.IsValue(component) || ReferenceEquals(component, BoundType.Unit) || ReferenceEquals(component, BoundType.String) ? WindowsLowering.GetValue(component) : null);
                if (value is null)
                {
                    this.resolved[type] = null;
                    return null;
                }

                this.fields.Add(value);
                this.children.Add(child);
            }

            foreach (var candidate in this.pool)
            {
                if (candidate.IsArray != array || candidate.Count != count || candidate.Fields.Length != fieldCount || candidate.Destructor != destructor)
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
            var storage = "{ [0 x i" + (alignment * 8).ToString(CultureInfo.InvariantCulture) + "], [" + size.ToString(CultureInfo.InvariantCulture) + " x i8] }";
            var representation = new ValueLowering(new(storage, (int)size, alignment, (int)size, offsets), storage, "ptr");
            var result = new AggregateLayout(this.pool.Count, representation, CollectionsMarshal.AsSpan(this.fields).Slice(start, fieldCount).ToArray(), CollectionsMarshal.AsSpan(this.children).Slice(start, fieldCount).ToArray(), count, array, destroy, destructor);
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
}
