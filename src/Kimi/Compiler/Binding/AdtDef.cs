// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>The stored shape of one struct or enum declaration, kept on its symbol: stored Fields, Cases by ordinal, the
/// destructor and the Type syntax of its inline storage. Binding refills it once per pass, before any body is bound.</summary>
internal sealed class AdtDef
{
    /// <summary>Gets the inline storage Type syntax in declaration order: direct bases, then stored Field Types and Case payloads.</summary>
    internal List<Koto> Types { get; } = [];

    /// <summary>Gets the stored Fields in declaration order; a Field's logical index is its position here.</summary>
    internal PropertyKoto[] Fields { get; private set; } = [];

    /// <summary>Gets the Cases by ordinal; a Case form without a name keeps its ordinal and has no Case.</summary>
    internal BoundEnumCase?[] Cases { get; private set; } = [];

    internal FunctionKoto? DestructorKoto { get; set; }

    /// <summary>Gets or sets the storage version of the pass whose PrepareStorage refilled this shape.</summary>
    internal ulong Version { get; set; }

    internal static bool IsStruct(BoundType? type) => type is { Kind: BoundTypeKind.Nominal or BoundTypeKind.Constructed, Semantics: SemanticsKind.Owner, Symbol.Declaration: StructKoto };

    internal static bool IsEnum(BoundType? type) => type?.Semantics == SemanticsKind.Owner && type.Symbol?.Declaration is EnumKoto;

    internal static StructKoto? Declaration(BoundType? type) => IsStruct(type) ? (StructKoto)type!.Symbol!.Declaration : null;

    // An instance's stored Types are its substitution of the declared ones (SPEC 8.10): Type, Length and Origin arguments.
    internal static BoundType? FieldType(BoundType type, int index) => Instance(type).Fields[index];

    internal static BoundType? Base(BoundType type) => IsStruct(type) ? Instance(type).Base : null;

    // Each Case's payload Types as one Tuple, by ordinal; null when a payload does not substitute.
    internal static BoundType[]? CaseTypes(BoundType type) => IsEnum(type) ? Instance(type).Cases : null;

    internal static int Count(BoundType type) => Struct(type)?.Fields.Length ?? 0;

    // Selection identities include inherited fields, base first. Construction and destruction keep their separate layers.
    internal static int StorageCount(BoundType type)
        => Count(type) + (Base(type) is { } parent ? StorageCount(parent) : 0);

    internal static bool FindField(BoundType type, BindingSymbol? symbol, out BoundType? fieldType, out int position)
    {
        for (var layer = type; layer is not null; layer = Base(layer))
        {
            var fields = Struct(layer)?.Fields;
            for (var i = 0; fields is not null && i < fields.Length; i++)
            {
                if (ReferenceEquals(fields[i].BoundSymbol, symbol))
                {
                    position = i + (Base(layer) is { } parent ? StorageCount(parent) : 0);
                    fieldType = FieldType(layer, i);
                    return fieldType is not null;
                }
            }
        }

        position = -1;
        fieldType = null;
        return false;
    }

    internal static bool HasDestructorOnPath(BoundType type, BindingSymbol? field)
    {
        for (var layer = type; layer is not null; layer = Base(layer))
        {
            if (Destructor(layer) is not null)
            {
                return true;
            }

            if (ReferenceEquals(field?.Scope.Owner.BoundSymbol, layer.Symbol))
            {
                break;
            }
        }

        return false;
    }

    internal static PropertyKoto Field(BoundType type, int index) => Struct(type)!.Fields[index];

    // The logical stored index of the Field `name`. Generated code addresses Kimi library record Fields by name; a missing
    // Field is a defect of the compiler build.
    internal static int IndexOf(BoundType type, string name)
    {
        var fields = Struct(type)?.Fields;
        for (var i = 0; fields is not null && i < fields.Length; i++)
        {
            if (fields[i].NameKoto.IdentifierName == name)
            {
                return i;
            }
        }

        throw new InvalidOperationException($"{type} stores no Field {name}.");
    }

    internal static FunctionKoto? Destructor(BoundType type) => Struct(type)?.DestructorKoto;

    internal static BoundType? ReceiverType(FunctionKoto function)
        => (function.IsConstructor || function.IsDestructor) && function.BoundSymbol?.Scope.Owner.BoundSymbol is { } owner
            ? function.CodeContext.Compilation.Binding.SelfType(owner) : null;

    internal static BoundEnumCase? Case(BoundType type, int ordinal)
        => type.Symbol is { Declaration: EnumKoto, Adt.Cases: { } cases } && (uint)ordinal < (uint)cases.Length ? cases[ordinal] : null;

    // Publishes a pass's Fields and Cases, reusing the arrays while their lengths are unchanged.
    internal void SetParts(List<PropertyKoto> fields, List<BoundEnumCase?> cases)
    {
        this.Fields = Refill(this.Fields, fields);
        this.Cases = Refill(this.Cases, cases);
    }

    private static AdtDef? Struct(BoundType? type) => IsStruct(type) ? type!.Symbol!.Adt : null;

    private static InstanceStorage Instance(BoundType type) => type.Symbol!.Declaration.CodeContext.Compilation.Binding.Instance(type);

    private static T[] Refill<T>(T[] array, List<T> items)
    {
        if (array.Length != items.Count)
        {
            array = new T[items.Count];
        }

        items.CopyTo(array);
        return array;
    }

    /// <summary>The stored Types of one struct or enum instance under its substitution.</summary>
    internal sealed class InstanceStorage
    {
        internal BoundType? Base { get; set; }

        internal BoundType?[] Fields { get; set; } = [];

        internal BoundType[]? Cases { get; set; }

        // The pass that substituted these Types and the pass whose preparation validated them; neither is current otherwise.
        internal ulong Version { get; set; }

        internal ulong Prepared { get; set; }
    }
}
