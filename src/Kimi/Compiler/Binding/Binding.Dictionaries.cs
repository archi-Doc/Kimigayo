// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly HashSet<PatternLiteral> dictionaryLiteralKeys = new();

    private BoundType? BindDictionaryLiteral(DictionaryLiteralKoto literal, BindingScope scope, BoundType? expected)
    {
        if (expected is not { Kind: BoundTypeKind.Dictionary, Components.Count: 2 })
        {
            BoundType? inferredKey = null;
            BoundType? inferredValue = null;
            BoundType? keyDefault = null;
            BoundType? valueDefault = null;
            var inferred = true;
            for (var i = 0; i < literal.Entries.Count; i++)
            {
                inferred &= this.CollectLiteralElementEvidence(literal.Entries[i].Key, scope, ref inferredKey, ref keyDefault);
                inferred &= this.CollectLiteralElementEvidence(literal.Entries[i].Value, scope, ref inferredValue, ref valueDefault);
            }

            if (!inferred)
            {
                return Complete(literal, null);
            }

            inferredKey ??= keyDefault;
            inferredValue ??= valueDefault;
            if (inferredKey is null || inferredValue is null)
            {
                return this.Fail(literal, BindingFailure.MissingType, true);
            }

            expected = this.InternType(BoundTypeKind.Dictionary, this.Library.GetSymbol(KimiDeclarationId.Dictionary), SemanticsKind.Owner, [inferredKey, inferredValue]);
        }

        BoundType? key = null;
        BoundType? value = null;
        var complete = true;
        for (var i = 0; i < literal.Entries.Count; i++)
        {
            var entry = literal.Entries[i];
            var actualKey = this.RequireType(entry.Key, scope, expected.Components[0]);
            var actualValue = this.RequireType(entry.Value, scope, expected.Components[1]);
            if (actualKey is null || actualValue is null || entry.Key.BindingState == BindingState.Invalid || entry.Value.BindingState == BindingState.Invalid)
            {
                complete = false;
                continue;
            }

            if (!ReferenceEquals(actualKey, BoundType.Never))
            {
                key = key is null ? actualKey : this.CommonOriginType(key, actualKey);
                complete &= key is not null;
            }

            if (!ReferenceEquals(actualValue, BoundType.Never))
            {
                value = value is null ? actualValue : this.CommonOriginType(value, actualValue);
                complete &= value is not null;
            }
        }

        // Check only the syntax required by SPEC 12.3.4, after all children have
        // bound so nested literals can reuse this scratch set without interference.
        this.dictionaryLiteralKeys.Clear();
        for (var i = 0; i < literal.Entries.Count; i++)
        {
            var syntax = literal.Entries[i].Key;
            if (syntax.BoundType is { } actual && this.TryDictionaryLiteralKey(syntax, actual, out var constant) && !this.dictionaryLiteralKeys.Add(constant))
            {
                this.Fail(syntax, BindingFailure.DuplicateDictionaryKey);
                complete = false;
            }
        }

        this.dictionaryLiteralKeys.Clear();
        return Complete(literal, complete ? this.InternType(BoundTypeKind.Dictionary, expected.Symbol, SemanticsKind.Owner, [key ?? expected.Components[0], value ?? expected.Components[1]]) : null);
    }

    private bool TryDictionaryLiteralKey(Koto syntax, BoundType type, out PatternLiteral literal)
    {
        syntax = KotoHelper.UnwrapParentheses(syntax);
        if (ReferenceEquals(type, BoundType.Unit) && syntax is UnitLiteralKoto or TupleLiteralKoto { Elements.Count: 0 })
        {
            literal = default;
            return true;
        }

        if (syntax is UnaryKoto { Akind: KotoKind.PrefixPlus, Operand: NumberLiteralKoto number })
        {
            syntax = number;
        }

        return this.TryPatternLiteral(syntax, type, out literal);
    }
}
