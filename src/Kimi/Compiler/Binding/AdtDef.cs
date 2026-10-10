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

    internal static bool IsStruct(BoundType? type) => type is { Kind: BoundTypeKind.Nominal or BoundTypeKind.Constructed, Semantics: SemanticsKind.Owner, Symbol.Declaration: StructKoto };

    internal static bool IsEnum(BoundType? type) => type?.Semantics == SemanticsKind.Owner && type.Symbol?.Declaration is EnumKoto;

    internal static StructKoto? Declaration(BoundType? type) => IsStruct(type) ? (StructKoto)type!.Symbol!.Declaration : null;

    // A constructed Type's stored Field Types are its substitution of the declared ones: the storage an analysis run or an
    // instantiation prepared, and the same substitution on demand otherwise (a generic template whose Semantics cases are
    // substituted declares Types that no run prepares, SPEC 8.10). Nominal Types with Origin arguments need substitution too.
    internal static BoundType? FieldType(BoundType type, int index)
        => type.StoredFields is { } fields ? fields[index]
            : (type.Kind == BoundTypeKind.Constructed || type.OriginArguments.Count != 0) && Declaration(type) is { } declaration ? declaration.CodeContext.Compilation.Binding.StoredType(Field(type, index), type)
            : Field(type, index).BoundType;

    internal static int Count(BoundType type) => Struct(type)?.Fields.Length ?? 0;

    // Selection identities include inherited fields, base first. Construction and destruction keep their separate layers.
    internal static int StorageCount(BoundType type)
        => Count(type) + (type.StoredBase is { } parent ? StorageCount(parent) : 0);

    internal static bool FindField(BoundType type, BindingSymbol? symbol, out BoundType? fieldType, out int position)
    {
        for (var layer = type; layer is not null; layer = layer.StoredBase)
        {
            var fields = Struct(layer)?.Fields;
            for (var i = 0; fields is not null && i < fields.Length; i++)
            {
                if (ReferenceEquals(fields[i].BoundSymbol, symbol))
                {
                    position = i + (layer.StoredBase is { } parent ? StorageCount(parent) : 0);
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
        for (var layer = type; layer is not null; layer = layer.StoredBase)
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

    private static T[] Refill<T>(T[] array, List<T> items)
    {
        if (array.Length != items.Count)
        {
            array = new T[items.Count];
        }

        items.CopyTo(array);
        return array;
    }
}
