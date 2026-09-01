// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Runtime.CompilerServices;
using Arc.Collections;

namespace Kimi.Compiler.Lexing;

#pragma warning disable SA1202 // Elements should be ordered by access

/// <summary>
/// Provides token-related helper methods used by the Kimi tokenizer.
/// </summary>
public static partial class TokenHelper
{
    private readonly record struct TokenDescriptor(TokenKind Kind, bool IsKeyword, string Text);

    /// <summary>
    /// The maximum number of token kinds supported by the descriptor table.
    /// </summary>
    public const int MaxTokens = 256;
    private static readonly TokenDescriptor[] TokenDescriptors;

    /// <summary>
    /// Maps UTF-16 keyword spellings to their corresponding keyword token kinds.
    /// </summary>
    public static readonly Utf16Hashtable<TokenKind> KeywordToTokenKind;

    private static readonly SearchValues<char> Separators = SearchValues.Create(
    [// Characters that terminate an identifier/keyword scan.
        ' ', '\t', '\r', '\n',
        '(', ')', '{', '}', '[', ']',
        '.', ',', ';', ':', '?',
        '+', '-', '*', '/', '%',
        '&', '|', '^', '!', '~',
        '=', '<', '>', '#', '$', '@',
    ]);

    static TokenHelper()
    {
        TokenDescriptors = new TokenDescriptor[MaxTokens];

        // Invalid
        Set(TokenKind.Invalid, false, string.Empty);

        // Keywords (Primitive types)
        Set(TokenKind.Bool, true, Constants.BoolKeyword);
        Set(TokenKind.Isize, true, Constants.IsizeKeyword);
        Set(TokenKind.Usize, true, Constants.UsizeKeyword);
        Set(TokenKind.I8, true, Constants.I8Keyword);
        Set(TokenKind.I16, true, Constants.I16Keyword);
        Set(TokenKind.I32, true, Constants.I32Keyword);
        Set(TokenKind.I64, true, Constants.I64Keyword);
        Set(TokenKind.I128, true, Constants.I128Keyword);
        Set(TokenKind.U8, true, Constants.U8Keyword);
        Set(TokenKind.U16, true, Constants.U16Keyword);
        Set(TokenKind.U32, true, Constants.U32Keyword);
        Set(TokenKind.U64, true, Constants.U64Keyword);
        Set(TokenKind.U128, true, Constants.U128Keyword);
        Set(TokenKind.F32, true, Constants.F32Keyword);
        Set(TokenKind.F64, true, Constants.F64Keyword);
        Set(TokenKind.String, true, Constants.StringKeyword);

        // Keywords
        Set(TokenKind.True, true, Constants.TrueKeyword);
        Set(TokenKind.False, true, Constants.FalseKeyword);
        Set(TokenKind.Let, true, Constants.LetKeyword);
        Set(TokenKind.Var, true, Constants.VarKeyword);
        Set(TokenKind.Func, true, Constants.FuncKeyword);

        // Expression keyword
        Set(TokenKind.If, true, Constants.IfKeyword);
        Set(TokenKind.Else, true, Constants.ElseKeyword);
        // Set(TokenKind.Block, true, "block");
        Set(TokenKind.As, true, Constants.AsKeyword);
        Set(TokenKind.Is, true, Constants.IsKeyword);
        Set(TokenKind.Not, true, Constants.NotKeyword);
        Set(TokenKind.And, true, Constants.AndKeyword);
        Set(TokenKind.Or, true, Constants.OrKeyword);
        Set(TokenKind.For, true, Constants.ForKeyword);
        Set(TokenKind.In, true, Constants.InKeyword);
        Set(TokenKind.While, true, Constants.WhileKeyword);
        Set(TokenKind.Loop, true, Constants.LoopKeyword);
        Set(TokenKind.Match, true, Constants.MatchKeyword);
        Set(TokenKind.Return, true, Constants.ReturnKeyword);
        Set(TokenKind.Break, true, Constants.BreakKeyword);
        Set(TokenKind.Continue, true, Constants.ContinueKeyword);
        Set(TokenKind.Yield, true, Constants.YieldKeyword);

        // Contextual keyword
        Set(TokenKind.Alias, true, Constants.AliasKeyword);
        Set(TokenKind.RootGroup, true, Constants.RootgroupKeyword);
        Set(TokenKind.Group, true, Constants.GroupKeyword);
        Set(TokenKind.Struct, true, Constants.StructKeyword);
        Set(TokenKind.Enum, true, Constants.EnumKeyword);
        Set(TokenKind.Extension, true, Constants.ExtensionKeyword);
        Set(TokenKind.Contract, true, Constants.ContractKeyword);
        Set(TokenKind.Static, true, Constants.StaticKeyword);
        Set(TokenKind.Public, true, Constants.PublicKeyword);
        Set(TokenKind.Protected, true, Constants.ProtectedKeyword);
        Set(TokenKind.Private, true, Constants.PrivateKeyword);
        Set(TokenKind.Internal, true, Constants.InternalKeyword);
        Set(TokenKind.ProtectedOrInternal, true, Constants.ProtectedOrInternalKeyword);
        Set(TokenKind.ProtectedAndInternal, true, Constants.ProtectedAndInternalKeyword);
        Set(TokenKind.Open, true, Constants.OpenKeyword);
        Set(TokenKind.Associate, true, Constants.AssociateKeyword);
        Set(TokenKind.Get, true, Constants.GetKeyword);
        Set(TokenKind.Set, true, Constants.SetKeyword);
        Set(TokenKind.Has, true, Constants.HasKeyword);

        // Not keyword
        Set(TokenKind.Identifier, false, string.Empty);
        Set(TokenKind.StartBlock, false, string.Empty);
        Set(TokenKind.EndBlock, false, string.Empty);
        Set(TokenKind.Separator, false, string.Empty);
        Set(TokenKind.NumericLiteral, false, string.Empty);
        Set(TokenKind.CharLiteral, false, string.Empty);
        Set(TokenKind.StringLiteral, false, string.Empty);
        Set(TokenKind.RawStringLiteral, false, string.Empty);
        Set(TokenKind.SingleLineComment, false, string.Empty);
        Set(TokenKind.MultiLineComment, false, string.Empty);

        // Single token
        Set(TokenKind.Sharp, false, "#");
        Set(TokenKind.Dollar, false, "$");
        Set(TokenKind.At, false, "@");
        Set(TokenKind.Comma, false, ",");
        Set(TokenKind.OpenBracket, false, "[");
        Set(TokenKind.CloseBracket, false, "]");
        Set(TokenKind.OpenParenthesis, false, "(");
        Set(TokenKind.CloseParenthesis, false, ")");
        Set(TokenKind.OpenBrace, false, "{");
        Set(TokenKind.CloseBrace, false, "}");
        Set(TokenKind.Colon, false, ":");
        Set(TokenKind.Semicolon, false, ";");
        Set(TokenKind.Question, false, "?");

        // Others
        Set(TokenKind.Ampersand, false, "&");
        Set(TokenKind.AmpersandAmpersand, false, "&&");
        Set(TokenKind.AmpersandEquals, false, "&=");
        Set(TokenKind.Asterisk, false, "*");
        Set(TokenKind.AsteriskEquals, false, "*=");
        Set(TokenKind.Bar, false, "|");
        Set(TokenKind.BarBar, false, "||");
        Set(TokenKind.BarEquals, false, "|=");
        Set(TokenKind.Caret, false, "^");
        Set(TokenKind.CaretEquals, false, "^=");
        Set(TokenKind.Dot, false, ".");
        Set(TokenKind.DotDot, false, "..");
        Set(TokenKind.DotDotEquals, false, "..=");
        Set(TokenKind.Equals, false, "=");
        Set(TokenKind.EqualsEquals, false, "==");
        Set(TokenKind.EqualsGreaterThan, false, "=>");
        Set(TokenKind.Exclamation, false, "!");
        Set(TokenKind.ExclamationEquals, false, "!=");
        Set(TokenKind.GreaterThan, false, ">");
        Set(TokenKind.GreaterThanEquals, false, ">=");
        Set(TokenKind.GreaterThanGreaterThan, false, ">>");
        Set(TokenKind.GreaterThanGreaterThanEquals, false, ">>=");
        Set(TokenKind.LessThan, false, "<");
        Set(TokenKind.LessThanEquals, false, "<=");
        Set(TokenKind.LessThanLessThan, false, "<<");
        Set(TokenKind.LessThanLessThanEquals, false, "<<=");
        Set(TokenKind.Minus, false, "-");
        Set(TokenKind.MinusEquals, false, "-=");
        Set(TokenKind.MinusGreaterThan, false, "->");
        Set(TokenKind.MinusMinus, false, "--");
        Set(TokenKind.Percent, false, "%");
        Set(TokenKind.PercentEquals, false, "%=");
        Set(TokenKind.Plus, false, "+");
        Set(TokenKind.PlusEquals, false, "+=");
        Set(TokenKind.PlusPlus, false, "++");
        Set(TokenKind.Slash, false, "/");
        Set(TokenKind.SlashEquals, false, "/=");

        KeywordToTokenKind = new();
        foreach (var x in TokenDescriptors)
        {
            if (x.IsKeyword)
            {
                KeywordToTokenKind.TryAdd(x.Text, x.Kind);
            }
        }

        static void Set(TokenKind kind, bool isKeyword, string text)
        {
            TokenDescriptors[(int)kind] = new TokenDescriptor(kind, isKeyword, text);
        }
    }

    /// <summary>
    /// Gets the source spelling of a token kind.
    /// </summary>
    /// <param name="tokenKind">The token kind to convert.</param>
    /// <returns>The source spelling, or an empty string for synthetic tokens.</returns>
    public static string ToText(this TokenKind tokenKind)
    {
        return TokenDescriptors[(int)tokenKind].Text;
    }

    /// <summary>
    /// Finds the first tokenizer separator in the specified text.
    /// </summary>
    /// <param name="text">The text to search.</param>
    /// <returns>The zero-based index of the first separator, or -1 if no separator is found.</returns>
    public static int IndexOfSeparator(ReadOnlySpan<char> text)
        => text.IndexOfAny(Separators);

    /// <summary>
    /// Determines whether a token starts an indentation block.
    /// </summary>
    /// <remarks>The check relies on the block token kinds forming a contiguous range.</remarks>
    /// <param name="tokenKind">The token kind to inspect.</param>
    /// <returns><see langword="true"/> if <paramref name="tokenKind"/> is a block-starting token; otherwise, <see langword="false"/>.</returns>
    public static bool IsBlockToken(this TokenKind tokenKind)
        => tokenKind >= TokenKind.Group && tokenKind <= TokenKind.Match;

    /// <summary>
    /// Determines whether a token represents a primitive type keyword.
    /// </summary>
    /// <param name="tokenKind">The token kind to inspect.</param>
    /// <returns><see langword="true"/> for a primitive type keyword.</returns>
    public static bool IsPrimitiveType(this TokenKind tokenKind)
        => tokenKind >= TokenKind.Bool && tokenKind <= TokenKind.String;

    /// <summary>
    /// Determines whether a token represents a reserved keyword.
    /// </summary>
    /// <param name="tokenKind">The token kind to inspect.</param>
    /// <returns><see langword="true"/> for a reserved keyword.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsKeyword(this TokenKind tokenKind)
        => tokenKind < TokenKind.Alias;

    /// <summary>
    /// Determines whether a token can be used as an identifier.
    /// </summary>
    /// <param name="tokenKind">The token kind to inspect.</param>
    /// <returns><see langword="true"/> for an identifier or contextual keyword.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsIdentifierOrContextualKeyword(this TokenKind tokenKind)
        => tokenKind == TokenKind.Identifier ||
        (tokenKind >= TokenKind.Alias && tokenKind < TokenKind.Identifier);

    /// <summary>
    /// Tries to classify a single-character token and reports its grouping-depth effect.
    /// </summary>
    /// <param name="c">The character to classify.</param>
    /// <param name="tokenKind">When this method returns, contains the token kind for <paramref name="c"/>, or <see cref="TokenKind.Invalid"/>.</param>
    /// <param name="groupingDepth">When this method returns, contains +1 for an opening grouping token, -1 for a closing grouping token, or 0 for a neutral token.</param>
    /// <returns><see langword="true"/> if <paramref name="c"/> is a recognized single-character token; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static bool TryGetSingleCharTokenKind(char c, out TokenKind tokenKind, out int groupingDepth)
    {
        (tokenKind, groupingDepth) = c switch
        {
            Constants.SharpChar => (TokenKind.Sharp, 0),
            Constants.DollarChar => (TokenKind.Dollar, 0),
            Constants.AmpersandChar => (TokenKind.Ampersand, 0),
            Constants.AsteriskChar => (TokenKind.Asterisk, 0),
            // Constants.AtChar => (TokenKind.At, 0),
            Constants.DotChar => (TokenKind.Dot, 0),
            Constants.CommaChar => (TokenKind.Comma, 0),
            Constants.OpenBracketChar => (TokenKind.OpenBracket, +1),
            Constants.CloseBracketChar => (TokenKind.CloseBracket, -1),
            Constants.OpenParenthesisChar => (TokenKind.OpenParenthesis, +1),
            Constants.CloseParenthesisChar => (TokenKind.CloseParenthesis, -1),
            Constants.OpenBraceChar => (TokenKind.OpenBrace, +1),
            Constants.CloseBraceChar => (TokenKind.CloseBrace, -1),
            Constants.ColonChar => (TokenKind.Colon, 0),
            Constants.SemicolonChar => (TokenKind.Semicolon, 0),
            Constants.BarChar => (TokenKind.Bar, 0),
            Constants.CaretChar => (TokenKind.Caret, 0),
            Constants.EqualsChar => (TokenKind.Equals, 0),
            Constants.ExclamationChar => (TokenKind.Exclamation, 0),
            Constants.GreaterThanChar => (TokenKind.GreaterThan, 0),
            Constants.LessThanChar => (TokenKind.LessThan, 0),
            Constants.MinusChar => (TokenKind.Minus, 0),
            Constants.PercentChar => (TokenKind.Percent, 0),
            Constants.PlusChar => (TokenKind.Plus, 0),
            Constants.SlashChar => (TokenKind.Slash, 0),
            Constants.QuestionChar => (TokenKind.Question, 0),
            _ => (TokenKind.Invalid, 0),
        };

        return tokenKind != TokenKind.Invalid;
    }
}
