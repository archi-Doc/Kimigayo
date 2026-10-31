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
            if (this.IndependentDictionaryType(literal, scope) is not { } independent)
            {
                return null;
            }

            expected = independent;
        }

        BoundType? key = null;
        BoundType? value = null;
        var complete = true;
        for (var i = 0; i < literal.Entries.Count; i++)
        {
            var entry = literal.Entries[i];
            var actualKey = this.RequireAtPosition(entry.Key, scope, expected.Components[0]);
            var actualValue = this.RequireAtPosition(entry.Value, scope, expected.Components[1]);
            if (actualKey is null || actualValue is null || entry.Key.BindingState == BindingState.Invalid || entry.Value.BindingState == BindingState.Invalid)
            {
                complete = false;
                continue;
            }

            if (!ReferenceEquals(actualKey, BoundType.Never))
            {
                key = key is null ? actualKey : this.CommonOriginType(key, actualKey, out _);
                complete &= key is not null;
            }

            if (!ReferenceEquals(actualValue, BoundType.Never))
            {
                value = value is null ? actualValue : this.CommonOriginType(value, actualValue, out _);
                complete &= value is not null;
            }
        }

        // Check only the syntax required by SPEC 12.4.3, after all children have
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
        return Complete(literal, complete ? this.types.Intern(BoundTypeKind.Dictionary, expected.Symbol, SemanticsKind.Owner, [key ?? expected.Components[0], value ?? expected.Components[1]]) : null);
    }

    // SPEC 4.3, 12.4.3: without a Dictionary expectation, the key and value Types its entries establish, as for an array literal's
    // element Type (BindIndependentArrayLiteral); null when the literal waits for its position's Type or fails.
    private BoundType? IndependentDictionaryType(DictionaryLiteralKoto literal, BindingScope scope)
    {
        BoundType? inferredKey = null;
        BoundType? inferredValue = null;
        BoundType? keyDefault = null;
        BoundType? valueDefault = null;
        var collecting = this.collectingElements;
        this.collectingElements = literal;
        var inferred = true;
        for (var i = 0; i < literal.Entries.Count; i++)
        {
            inferred &= this.CollectLiteralElementEvidence(literal.Entries[i].Key, scope, ref inferredKey, ref keyDefault);
            inferred &= this.CollectLiteralElementEvidence(literal.Entries[i].Value, scope, ref inferredValue, ref valueDefault);
        }

        this.collectingElements = collecting;
        if (inferred && (inferredKey is null || inferredValue is null) && this.DecidedLater(literal))
        {
            return Complete(literal, null); // SPEC 14.9.2 step 3, 4.3: a key or value Type only literals supply waits for its position's.
        }

        // Without an established Type, or when a part failed (SPEC 23.1.2: the others are still bound), the waiting parts default.
        for (var i = 0; i < literal.Entries.Count; i++)
        {
            if (!inferred || inferredKey is null)
            {
                this.DefaultWaitingPart(literal.Entries[i].Key, ref keyDefault);
            }

            if (!inferred || inferredValue is null)
            {
                this.DefaultWaitingPart(literal.Entries[i].Value, ref valueDefault);
            }
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

        var dictionary = this.Library.GetSymbol(KimiDeclarationId.Dictionary);
        return this.types.Intern(BoundTypeKind.Dictionary, dictionary, SemanticsKind.Owner, [inferredKey, inferredValue]);
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
