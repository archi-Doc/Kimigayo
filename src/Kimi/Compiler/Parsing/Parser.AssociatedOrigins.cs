// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public static partial class Parser
{
    internal static OriginApplicationKoto ParseOriginApplication(ref TokenReader reader, Koto type)
    {
        reader.Advance();
        var arguments = default(TemporaryKotoList);
        var end = reader.CurrentTokenRange.End;
        if (reader.CurrentTokenKind == TokenKind.CloseParenthesis)
        {
            reader.Expect(SyntaxForm.Name);
        }

        while (reader.CanRead && reader.CurrentTokenKind is not (TokenKind.CloseParenthesis or TokenKind.Separator or TokenKind.EndBlock))
        {
            arguments.Add(ParseOriginAtom(ref reader));
            if (!reader.TryConsume(TokenKind.Comma))
            {
                break;
            }

            if (reader.CurrentTokenKind == TokenKind.CloseParenthesis)
            {
                reader.Expect(SyntaxForm.Name);
            }
        }

        if (reader.TryConsume(TokenKind.CloseParenthesis, out var close, true))
        {
            end = close.End;
        }

        return new(ref reader, SourceSpan.FromBounds(type.Span.Start, end), type, arguments.ToArray());
    }

    internal static void ValidateAssociatedParameters(ref TokenReader reader, Koto head)
    {
        while (head is TypeSemanticsKoto { IsTransparentWrapper: true, Type: { } inner })
        {
            head = inner;
        }

        if (head is OriginApplicationKoto application)
        {
            for (var i = 0; i < application.ArgumentNodes.Count; i++)
            {
                var parameter = application.ArgumentNodes[i];
                if (parameter is not IdentifierNameKoto { IdentifierName: not ("_" or "static") } && parameter.Expected(SyntaxForm.Name) is { } cause)
                {
                    // The application keeps a recovery parameter, so Binding's own check of its Origins rests on the Error.
                    application.ReplaceArgument(i, new ErrorKoto(ref reader, parameter.Span) { Cause = cause });
                }
            }
        }
    }
}
