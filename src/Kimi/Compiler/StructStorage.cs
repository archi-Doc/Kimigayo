// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Logical stored fields of the concrete owned structure execution subset.</summary>
internal static class StructStorage
{
    internal static bool IsStruct(BoundType? type) => type is { Kind: BoundTypeKind.Nominal or BoundTypeKind.Constructed, Semantics: SemanticsKind.Owner, Symbol.Declaration: StructKoto };

    // A constructed Type's stored Field Types are its substitution of the declared ones: the storage an analysis run or an
    // instantiation prepared, and the same substitution on demand otherwise (a generic template whose Semantics cases are
    // substituted declares Types that no run prepares, SPEC 8.10). Nominal Types with Origin arguments need substitution too.
    internal static BoundType? FieldType(BoundType type, int index)
        => type.StoredFields is { } fields ? fields[index]
            : (type.Kind == BoundTypeKind.Constructed || type.OriginArguments.Count != 0) && Declaration(type) is { } declaration ? declaration.CodeContext.Compilation.Binding.StoredType(Field(type, index), type)
            : Field(type, index).BoundType;

    internal static StructKoto? Declaration(BoundType? type) => IsStruct(type) ? (StructKoto)type!.Symbol!.Declaration : null;

    internal static int Count(BoundType type)
    {
        var count = 0;
        if (Declaration(type) is { } declaration)
        {
            for (var i = 0; i < declaration.Members.Count; i++)
            {
                if (declaration.Members[i] is PropertyKoto { BoundSymbol.Property.IsStored: true })
                {
                    count++;
                }
            }
        }

        return count;
    }

    // Selection identities include inherited fields, base first. Construction and destruction keep their separate layers.
    internal static int StorageCount(BoundType type)
        => Count(type) + (type.StoredBase is { } parent ? StorageCount(parent) : 0);

    internal static bool FindField(BoundType type, BindingSymbol? symbol, out BoundType? fieldType, out int position)
    {
        for (var layer = type; layer is not null; layer = layer.StoredBase)
        {
            if (Declaration(layer) is not { } declaration)
            {
                continue;
            }

            var members = declaration.Members;
            var index = 0;
            for (var i = 0; i < members.Count; i++)
            {
                if (members[i] is not PropertyKoto { BoundSymbol.Property.IsStored: true } field)
                {
                    continue;
                }

                if (ReferenceEquals(field.BoundSymbol, symbol))
                {
                    position = index + (layer.StoredBase is { } parent ? StorageCount(parent) : 0);
                    fieldType = FieldType(layer, index);
                    return fieldType is not null;
                }

                index++;
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

    internal static PropertyKoto Field(BoundType type, int index)
    {
        var members = Declaration(type)!.Members;
        for (var i = 0; i < members.Count; i++)
        {
            if (members[i] is PropertyKoto { BoundSymbol.Property.IsStored: true } field && index-- == 0)
            {
                return field;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(index));
    }

    internal static FunctionKoto? Destructor(BoundType type)
    {
        if (Declaration(type) is { } declaration)
        {
            for (var i = 0; i < declaration.Members.Count; i++)
            {
                if (declaration.Members[i] is FunctionKoto { IsDestructor: true } destructor)
                {
                    return destructor;
                }
            }
        }

        return null;
    }

    internal static BoundType? ReceiverType(FunctionKoto function)
        => (function.IsConstructor || function.IsDestructor) && function.BoundSymbol?.Scope.Owner.BoundSymbol is { } owner
            ? function.CodeContext.Compilation.Binding.SelfType(owner) : null;
}
