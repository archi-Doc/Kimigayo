// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private BoundType? BindDictionaryLiteral(DictionaryLiteralKoto literal, BindingScope scope, BoundType? expected)
    {
        if (expected is not { Kind: BoundTypeKind.Dictionary, Components.Count: 2 })
        {
            this.BindUnknownChildren(literal, scope);
            return Fail(literal, BindingFailure.Unsupported);
        }

        BoundType? key = null;
        BoundType? value = null;
        var complete = true;
        for (var i = 0; i < literal.Entries.Count; i++)
        {
            var entry = literal.Entries[i];
            var actualKey = this.BindNode(entry.Key, scope, expected.Components[0]);
            var actualValue = this.BindNode(entry.Value, scope, expected.Components[1]);
            if (actualKey is null || actualValue is null)
            {
                complete = false;
                continue;
            }

            key = key is null ? actualKey : this.CommonOriginType(key, actualKey);
            value = value is null ? actualValue : this.CommonOriginType(value, actualValue);
            complete &= key is not null && value is not null;
        }

        return Complete(literal, complete ? this.InternType(BoundTypeKind.Dictionary, expected.Symbol, SemanticsKind.Owner, [key ?? expected.Components[0], value ?? expected.Components[1]]) : null);
    }
}
