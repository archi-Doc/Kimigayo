// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Kimi.Compiler.Helper;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public static partial class KotoHelper
{
    public static Koto NewUnaryKoto(ref TokenReader reader, Token token, Koto operand) => token.Kind switch
    {
        TokenKind.Sharp => new AttributeKoto(ref reader, token.Span, operand),
        TokenKind.Dollar => new MacroKoto(ref reader, token.Span, operand),
        // TokenKind.Asterisk => new ReferenceKoto(ref reader, token.Range, operand, ReferenceKind.None),
        TokenKind.Plus => new PrefixPlusKoto(ref reader, token.Span, operand),
        TokenKind.Minus => new PrefixMinusKoto(ref reader, token.Span, operand),
        TokenKind.Not => new NotKoto(ref reader, token.Span, operand),
        TokenKind.Caret => new PrefixCaretKoto(ref reader, token.Span, operand),
        TokenKind.PlusPlus => new PrefixPlusPlusKoto(ref reader, token.Span, operand),
        TokenKind.MinusMinus => new PrefixMinusMinusKoto(ref reader, token.Span, operand),
        _ => throw new InvalidOperationException(),
    };

    public static Koto NewBinaryKoto(ref TokenReader reader, Token token, Koto left, Koto right) => token.Kind switch
    {
        TokenKind.Asterisk => new AsteriskKoto(ref reader, token.Span, left, right),
        TokenKind.At => new ConversionKoto(ref reader, token.Span, left, right),
        TokenKind.Slash => new SlashKoto(ref reader, token.Span, left, right),
        TokenKind.Percent => new PercentKoto(ref reader, token.Span, left, right),
        TokenKind.Plus => new PlusKoto(ref reader, token.Span, left, right),
        TokenKind.Minus => new MinusKoto(ref reader, token.Span, left, right),
        TokenKind.LessThanLessThan => new LessThanLessThanKoto(ref reader, token.Span, left, right),
        TokenKind.GreaterThanGreaterThan => new GreaterThanGreaterThanKoto(ref reader, token.Span, left, right),
        TokenKind.LessThan => new LessThanKoto(ref reader, token.Span, left, right),
        TokenKind.LessThanEquals => new LessThanEqualsKoto(ref reader, token.Span, left, right),
        TokenKind.GreaterThan => new GreaterThanKoto(ref reader, token.Span, left, right),
        TokenKind.GreaterThanEquals => new GreaterThanEqualsKoto(ref reader, token.Span, left, right),
        TokenKind.As => new AsKoto(ref reader, token.Span, left, right),
        TokenKind.Is => new IsKoto(ref reader, token.Span, left, right),
        TokenKind.EqualsEquals => new EqualsEqualsKoto(ref reader, token.Span, left, right),
        TokenKind.ExclamationEquals => new ExclamationEqualsKoto(ref reader, token.Span, left, right),
        TokenKind.Ampersand => new AmpersandKoto(ref reader, token.Span, left, right),
        TokenKind.Caret => new CaretKoto(ref reader, token.Span, left, right),
        TokenKind.Bar => new BarKoto(ref reader, token.Span, left, right),
        TokenKind.And => new AndKoto(ref reader, token.Span, left, right),
        TokenKind.Or => new OrKoto(ref reader, token.Span, left, right),
        TokenKind.Equals => new EqualsKoto(ref reader, token.Span, left, right),
        TokenKind.PlusEquals => new PlusEqualsKoto(ref reader, token.Span, left, right),
        TokenKind.MinusEquals => new MinusEqualsKoto(ref reader, token.Span, left, right),
        TokenKind.AsteriskEquals => new AsteriskEqualsKoto(ref reader, token.Span, left, right),
        TokenKind.SlashEquals => new SlashEqualsKoto(ref reader, token.Span, left, right),
        TokenKind.PercentEquals => new PercentEqualsKoto(ref reader, token.Span, left, right),
        TokenKind.AmpersandEquals => new AmpersandEqualsKoto(ref reader, token.Span, left, right),
        TokenKind.CaretEquals => new CaretEqualsKoto(ref reader, token.Span, left, right),
        TokenKind.BarEquals => new BarEqualsKoto(ref reader, token.Span, left, right),
        TokenKind.LessThanLessThanEquals => new LessThanLessThanEqualsKoto(ref reader, token.Span, left, right),
        TokenKind.GreaterThanGreaterThanEquals => new GreaterThanGreaterThanEqualsKoto(ref reader, token.Span, left, right),

        _ => throw new InvalidOperationException(),
    };

    public static bool Replace(Koto parent, Koto oldKoto, Koto newKoto)
    {
        if (parent.ReplaceChild(oldKoto, newKoto))
        {
            // Koto structure
            newKoto.Parent = parent;
            newKoto.Goshujin?.ChildLinkChain.UnsafeReplaceInstance(oldKoto, newKoto);
            /*newKoto.ChildLinkLink.Previous = oldKoto.Previous;
            newKoto.Next = oldKoto.Next;

            oldKoto.Parent = default;
            oldKoto.Previous = default;
            oldKoto.Next = default;*/
            oldKoto.Goshujin = default;

            // Frontend Metadata
            newKoto.DiagnosticCollection = oldKoto.DiagnosticCollection;
            newKoto.Span = oldKoto.Span;
            newKoto.CodeContext = oldKoto.CodeContext;
            return true;
        }

        return false;
    }

    public static void Dump(Koto koto, TextWriter writer)
    {
        DumpKoto(koto, writer, indent: "  ", isLast: true, label: null);
    }

    public static string ValidateAndGetNamespace(ref TokenReader reader)
    {
        if (reader.IsEnd)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        var flag = true;
        while (reader.TryRead(out var token))
        {
            if (token.Kind == TokenKind.Separator)
            {
                break;
            }

            if (flag)
            {// Identifier
                flag = false;
                var span = reader.GetSpan(token);
                if (IdentifierHelper.IsValidIdentifier(span))
                {
                    sb.Append(span);
                }
                else
                {
                    reader.Diagnostic.Add(token.Span, DiagnosticCode.InvalidIdentifier_Kd, span.ToString());
                    break;
                }
            }
            else
            {// Dot
                flag = true;
                if (token.Kind == TokenKind.Dot)
                {
                    sb.Append(Constants.DotChar);
                }
                else
                {
                    reader.Diagnostic.Add(token.Span, DiagnosticCode.UnexpectedToken_Kd, token.Kind);
                    break;
                }
            }
        }

        if (flag)
        {
            reader.Diagnostic.Add(reader.CurrentTokenRange, DiagnosticCode.IdentifierExpected_Kd);
        }

        return sb.ToString();
    }

    public static List<string> ParseQualifiedNameSegments(ref TokenReader reader)
    {
        if (reader.IsEnd)
        {
            return [];
        }

        var list = new List<string>();
        var flag = true;
        while (reader.CanRead)
        {
            var token = reader.CurrentToken;
            reader.Advance();

            if (token.Kind == TokenKind.Separator)
            {
                break;
            }

            if (flag)
            {// Identifier
                flag = false;
                var span = reader.GetSpan(token);
                if (IdentifierHelper.IsValidIdentifier(span))
                {
                    list.Add(span.ToString());
                }
                else
                {
                    reader.Diagnostic.Add(token.Span, DiagnosticCode.InvalidIdentifier_Kd, span.ToString());
                    break;
                }
            }
            else
            {// Dot
                flag = true;
                if (token.Kind == TokenKind.Dot)
                {
                }
                else
                {
                    reader.Diagnostic.Add(token.Span, DiagnosticCode.UnexpectedToken_Kd, token);
                    break;
                }
            }
        }

        if (flag)
        {
            reader.Diagnostic.Add(reader.CurrentTokenRange, DiagnosticCode.IdentifierExpected_Kd);
        }

        return list;
    }

    private static void DumpKoto(Koto koto, TextWriter writer, string indent, bool isLast, string? label)
    {
        writer.Write(indent);

        if (indent.Length > 0)
        {
            writer.Write(isLast ? "└─ " : "├─ ");
        }

        var r = koto.Dump();
        writer.WriteLine(r.Text);

        var childIndent = indent;
        if (indent.Length > 0)
        {
            childIndent += isLast ? "   " : "│  ";
        }

        if (r.Children is { } children)
        {
            for (var i = 0; i < children.Length; i++)
            {
                DumpKoto(children[i], writer, childIndent, i == children.Length - 1, default);
            }
        }
    }
}
