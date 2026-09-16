// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Logical stored fields of the concrete owned structure execution subset.</summary>
internal static class StructStorage
{
    internal static bool IsStruct(BoundType? type) => type is { Kind: BoundTypeKind.Nominal, Semantics: SemanticsKind.Owner, Symbol.Declaration: StructKoto };

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

    internal static BoundType? ReceiverType(FunctionKoto function) => function.IsConstructor || function.IsDestructor ? function.BoundSymbol?.Scope.Owner.BoundSymbol?.Type : null;
}
