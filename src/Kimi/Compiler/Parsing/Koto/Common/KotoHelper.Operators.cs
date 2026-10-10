// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public static partial class KotoHelper
{
    internal static KotoKind CompoundOperation(KotoKind assignment) => assignment switch
    {
        KotoKind.PlusEquals => KotoKind.Plus,
        KotoKind.MinusEquals => KotoKind.Minus,
        KotoKind.AsteriskEquals => KotoKind.Asterisk,
        KotoKind.SlashEquals => KotoKind.Slash,
        KotoKind.PercentEquals => KotoKind.Percent,
        KotoKind.AmpersandEquals => KotoKind.Ampersand,
        KotoKind.CaretEquals => KotoKind.Caret,
        KotoKind.BarEquals => KotoKind.Bar,
        KotoKind.LessThanLessThanEquals => KotoKind.LessThanLessThan,
        KotoKind.GreaterThanGreaterThanEquals => KotoKind.GreaterThanGreaterThan,
        _ => KotoKind.Invalid,
    };

    /// <summary>Gets the literal of a signed number literal: a number literal, or one under a unary <c>-</c> or <c>+</c>.</summary>
    /// <param name="node">The syntax; parentheses are not unwrapped.</param>
    /// <param name="negative">Receives whether the literal is under a unary <c>-</c>.</param>
    /// <returns>The number literal, or <see langword="null"/> when the syntax is not a signed number literal.</returns>
    internal static NumberLiteralKoto? SignedNumber(Koto node, out bool negative)
    {
        negative = false;
        if (node is UnaryKoto { Akind: KotoKind.PrefixMinus or KotoKind.PrefixPlus } sign)
        {
            negative = sign.Akind == KotoKind.PrefixMinus;
            node = sign.Operand;
        }

        if (node is NumberLiteralKoto number)
        {
            return number;
        }

        negative = false;
        return null;
    }

    // The node kind of an infix operator token that has no node class of its own.
    private static KotoKind BinaryOperator(TokenKind token) => token switch
    {
        TokenKind.Asterisk => KotoKind.Asterisk,
        TokenKind.Slash => KotoKind.Slash,
        TokenKind.Percent => KotoKind.Percent,
        TokenKind.Plus => KotoKind.Plus,
        TokenKind.Minus => KotoKind.Minus,
        TokenKind.LessThanLessThan => KotoKind.LessThanLessThan,
        TokenKind.GreaterThanGreaterThan => KotoKind.GreaterThanGreaterThan,
        TokenKind.LessThan => KotoKind.LessThan,
        TokenKind.LessThanEquals => KotoKind.LessThanEquals,
        TokenKind.GreaterThan => KotoKind.GreaterThan,
        TokenKind.GreaterThanEquals => KotoKind.GreaterThanEquals,
        TokenKind.As => KotoKind.As,
        TokenKind.EqualsEquals => KotoKind.EqualsEquals,
        TokenKind.ExclamationEquals => KotoKind.ExclamationEquals,
        TokenKind.Ampersand => KotoKind.Ampersand,
        TokenKind.Caret => KotoKind.Caret,
        TokenKind.Bar => KotoKind.Bar,
        TokenKind.And => KotoKind.And,
        TokenKind.Or => KotoKind.Or,
        TokenKind.Equals => KotoKind.Equals,
        TokenKind.PlusEquals => KotoKind.PlusEquals,
        TokenKind.MinusEquals => KotoKind.MinusEquals,
        TokenKind.AsteriskEquals => KotoKind.AsteriskEquals,
        TokenKind.SlashEquals => KotoKind.SlashEquals,
        TokenKind.PercentEquals => KotoKind.PercentEquals,
        TokenKind.AmpersandEquals => KotoKind.AmpersandEquals,
        TokenKind.CaretEquals => KotoKind.CaretEquals,
        TokenKind.BarEquals => KotoKind.BarEquals,
        TokenKind.LessThanLessThanEquals => KotoKind.LessThanLessThanEquals,
        TokenKind.GreaterThanGreaterThanEquals => KotoKind.GreaterThanGreaterThanEquals,
        _ => throw new InvalidOperationException(),
    };
}
