// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal static class EnumStorage
{
    internal static bool IsEnum(BoundType? type) => type?.Semantics == SemanticsKind.Owner && type.Symbol?.Declaration is EnumKoto;

    internal static int Count(BoundType type)
    {
        var count = 0;
        if (type.Symbol?.Declaration is EnumKoto declaration)
        {
            for (var i = 0; i < declaration.Members.Count; i++)
            {
                var member = declaration.Members[i];
                if (member.BoundSymbol?.EnumCase is not null)
                {
                    count++;
                }
            }
        }

        return count;
    }

    internal static BoundEnumCase? Case(BoundType type, int ordinal)
    {
        if (type.Symbol?.Declaration is EnumKoto declaration)
        {
            for (var i = 0; i < declaration.Members.Count; i++)
            {
                var member = declaration.Members[i];
                if (member.BoundSymbol?.EnumCase is { } selected && selected.Ordinal == ordinal)
                {
                    return selected;
                }
            }
        }

        return null;
    }
}
