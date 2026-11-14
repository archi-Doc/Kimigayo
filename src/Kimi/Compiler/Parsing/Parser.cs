// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The parser of SPEC Appendix F: a static recursive-descent grammar over one TokenReader, with a binding-power loop for expressions. This
// file holds the contract and the token tables; each other Parser.<Concern>.cs part owns one concern, but Parser.Syntax.cs, mixed until R9-F2.
// Input: a TokenReader over the Tokenizer's tokens, which carries the pending prefix of the next item, the layout region, the excluded-syntax
// state and the CodeContext. The two parse entries reach the grammar through the declaration Kotos' Parse methods and item loops.
// Output: Koto nodes with their spans; typed reports through the reader, a SyntaxForm or a DiagnosticCode (DIAGNOSTICS.md §4.4); recovery and
// omission records in the CodeContext; Hover and documentation anchors.
// Invariants: a source error is reported where it is read and the parse goes on, what was read kept as a recovery where a check could read it
// (DIAGNOSTICS.md §4.3, §4.4); excluded syntax takes the same grammar and registers nothing (SPEC 19.4); the tables are filled once, by the
// static constructor, and only read after it; the constructor itself reads each infix row it has just written, to fill IsComparisonKind.
// Prohibitions: no name resolution or Type judgment, since Binding judges what the parser keeps; the exceptions are a directive's Condition,
// evaluated for selection (SPEC 19, CompileTimeConditionEvaluator), and a body's Constraint prefix, which reads its function's parameter Names.
// No message text at a reporting site.

/// <summary>
/// Parses tokens into Koto syntax-tree nodes and writes nodes as source text.
/// </summary>
public static partial class Parser
{
    private const int PrefixBindingPower = 100;

    /// <summary>
    /// Prefix <c>try</c> binds below <c>@</c> (90) and above the multiplicative operators (80):
    /// <c>try x@move</c> is <c>try (x@move)</c> and <c>try a * b</c> is <c>(try a) * b</c>.
    /// </summary>
    private const int TryBindingPower = 85;

    private const int ComparisonBindingPower = 30;

    private const int RangeLeftBindingPower = 8;

    private const int RangeRightBindingPower = 9;

    // Per-token-kind tables replace switch chains: on the expression hot path, and for the words of a header line (Parser.ItemPrefixes).
    private static readonly byte[] InfixLeftBindingPower = new byte[TokenHelper.MaxTokens];

    private static readonly byte[] InfixRightBindingPower = new byte[TokenHelper.MaxTokens];

    // The node kinds of Comparison's operators: IsComparisonOperator's tokens, each under KotoHelper.BinaryOperator.
    private static readonly bool[] IsComparisonKind = new bool[Koto.MaxKind];

    private static readonly bool[] IsPrefixOperator = new bool[TokenHelper.MaxTokens];

    private static readonly bool[] IsPostfixOperator = new bool[TokenHelper.MaxTokens];

    private static readonly bool[] IsExpressionBoundaryKind = new bool[TokenHelper.MaxTokens];

    // The first words of SPEC F.10.2's LabelableConstruct: a label's targets and, with `func`, the body-bearing expressions of F.4.4.
    private static readonly bool[] IsLabelableConstruct = new bool[TokenHelper.MaxTokens];

    private static readonly HeaderWord[] HeaderWords = new HeaderWord[TokenHelper.MaxTokens];

    static Parser()
    {
        for (var i = 0; i < TokenHelper.MaxTokens; i++)
        {
            var (left, right) = GetInfixBindingPower((TokenKind)i);
            InfixLeftBindingPower[i] = (byte)left;
            InfixRightBindingPower[i] = (byte)right;
            if (IsComparisonOperator((TokenKind)i))
            {
                IsComparisonKind[(int)KotoHelper.BinaryOperator((TokenKind)i)] = true;
            }
        }

        Mark(
            IsPostfixOperator,
            [
                TokenKind.Dot, TokenKind.OpenParenthesis, TokenKind.LessThan,
                TokenKind.OpenBracket, TokenKind.PlusPlus, TokenKind.MinusMinus,
                TokenKind.At, // Only @follow is postfix (SPEC 13.1); other @ operations keep their infix precedence.
            ],
            true);

        Mark(
            IsPrefixOperator,
            [
                TokenKind.Dollar, TokenKind.Asterisk, TokenKind.Plus,
                TokenKind.Minus, TokenKind.Not, TokenKind.Caret, TokenKind.PlusPlus, TokenKind.MinusMinus,
            ],
            true);

        Mark(
            IsExpressionBoundaryKind,
            [
                TokenKind.Separator, TokenKind.StartBlock, TokenKind.EndBlock, TokenKind.Else,
                TokenKind.EqualsGreaterThan, TokenKind.Comma, TokenKind.Exclamation, TokenKind.CloseParenthesis, TokenKind.CloseBracket,
            ],
            true);

        Mark(
            IsLabelableConstruct,
            [TokenKind.If, TokenKind.Match, TokenKind.For, TokenKind.While, TokenKind.Loop, TokenKind.Do],
            true);

        Mark(
            HeaderWords,
            [TokenKind.Public, TokenKind.Internal, TokenKind.Private, TokenKind.Protected, TokenKind.Open, TokenKind.Static],
            HeaderWord.Modifier);

        Mark(
            HeaderWords,
            [
                TokenKind.RootGroup, TokenKind.Group, TokenKind.Struct, TokenKind.Enum, TokenKind.Contract, TokenKind.Extension, TokenKind.Func,
                TokenKind.Init, TokenKind.Drop, TokenKind.Let, TokenKind.Var, TokenKind.Computed, TokenKind.Property, TokenKind.Associate,
            ],
            HeaderWord.DeclarationIntroducer);

        Mark(HeaderWords, [TokenKind.Get, TokenKind.Set], HeaderWord.AccessorIntroducer);

        static void Mark<T>(T[] table, ReadOnlySpan<TokenKind> kinds, T value)
        {
            foreach (var kind in kinds)
            {
                table[(int)kind] = value;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsExpressionBoundary(ref TokenReader reader)
        => !reader.CanRead || IsExpressionBoundaryKind[(byte)reader.CurrentTokenKind];

    // The operators of SPEC F.9.1's Comparison, one non-associative level (F.9.2): GetInfixBindingPower's rows at ComparisonBindingPower
    // are the one written set. `is` shares the level and is the production's other alternative, which takes a NamedCoreType.
    private static bool IsComparisonOperator(TokenKind kind)
        => InfixLeftBindingPower[(byte)kind] == ComparisonBindingPower && kind != TokenKind.Is;

    private static (int Left, int Right) GetInfixBindingPower(TokenKind kind)
        => kind switch
        {
            // Conversion (below prefix operators, above multiplication).
            // "@" parses its right operand as a Type in ParseExpression.
            TokenKind.At => (90, 91),

            // Multiplicative
            TokenKind.Asterisk => (80, 81),
            TokenKind.Slash => (80, 81),
            TokenKind.Percent => (80, 81),

            // Additive
            TokenKind.Plus => (70, 71),
            TokenKind.Minus => (70, 71),

            // Shift
            TokenKind.LessThanLessThan => (60, 61),
            TokenKind.GreaterThanGreaterThan => (60, 61),

            // Bitwise
            TokenKind.Ampersand => (50, 51),
            TokenKind.Caret => (45, 46),
            TokenKind.Bar => (40, 41),

            // Comparisons share one non-associative level. ParseExpression
            // diagnoses chains while retaining their nodes for error recovery.
            TokenKind.LessThan => (ComparisonBindingPower, ComparisonBindingPower + 1),
            TokenKind.LessThanEquals => (ComparisonBindingPower, ComparisonBindingPower + 1),
            TokenKind.GreaterThan => (ComparisonBindingPower, ComparisonBindingPower + 1),
            TokenKind.GreaterThanEquals => (ComparisonBindingPower, ComparisonBindingPower + 1),
            TokenKind.EqualsEquals => (ComparisonBindingPower, ComparisonBindingPower + 1),
            TokenKind.ExclamationEquals => (ComparisonBindingPower, ComparisonBindingPower + 1),
            TokenKind.Is => (ComparisonBindingPower, ComparisonBindingPower + 1),

            // Logical
            TokenKind.And => (20, 21),
            TokenKind.Or => (10, 11),

            // Range (non-associative; parsed specially because either endpoint may be omitted)
            TokenKind.DotDot => (RangeLeftBindingPower, RangeRightBindingPower),
            TokenKind.DotDotEquals => (RangeLeftBindingPower, RangeRightBindingPower),

            // Assignment
            TokenKind.Equals => (5, 5),
            TokenKind.PlusEquals => (5, 5),
            TokenKind.MinusEquals => (5, 5),
            TokenKind.AsteriskEquals => (5, 5),
            TokenKind.SlashEquals => (5, 5),
            TokenKind.PercentEquals => (5, 5),
            TokenKind.AmpersandEquals => (5, 5),
            TokenKind.CaretEquals => (5, 5),
            TokenKind.BarEquals => (5, 5),
            TokenKind.LessThanLessThanEquals => (5, 5),
            TokenKind.GreaterThanGreaterThanEquals => (5, 5),

            _ => default,
        };
}
