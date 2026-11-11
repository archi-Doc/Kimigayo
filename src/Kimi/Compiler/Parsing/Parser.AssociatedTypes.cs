// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// The associated Types' syntax (Appendix F.6.2, F.8; SPEC 8.5): a Container body's associate item, its Origin parameters and the
// Origin application of an associated Type name.
public static partial class Parser
{
    /// <summary>Parses an <c>associate</c> item of a Container body (Appendix F.6.2): an associated Type requirement
    /// (<c>AssociatedTypeDeclaration</c>), which only a Contract declares and keeps among its Constraints when it has a requirement, or an
    /// associated Type specification (<c>AssociatedTypeSpecification</c>), a member of its owner, whatever the owner (SPEC 8.5.1).</summary>
    /// <param name="reader">The token reader at <c>associate</c>.</param>
    /// <param name="container">The Container whose body holds the item.</param>
    /// <param name="token">The <c>associate</c> token.</param>
    internal static void ParseAssociatedTypeItem(ref TokenReader reader, DeclarationContainerKoto container, Token token)
    {
        // An associate declaration takes no attributes (SPEC 6.7) and no access word (SPEC F.6.2).
        ReportPendingAttributes(ref reader);
        RejectAccessWord(ref reader);
        reader.Advance();
        if (IsTypeConstraintStart(ref reader, declarationContext: true))
        {
            var associated = ParseAssociatedConstraint(ref reader);
            if (associated is not null)
            {
                associated.IsAssociatedConstraint = true;
                ValidateAssociatedParameters(ref reader, associated.Left);
                if (reader.TryConsume(TokenKind.For))
                {
                    associated.FormationType = ParseType(ref reader, TypeContext.Type);
                    associated.FormationType.Parent = associated;
                    associated.Span = SourceSpan.FromBounds(associated.Span.Start, associated.FormationType.Span.End);
                }

                ParseAttachedOriginBlock(ref reader, associated);
                reader.Document(associated, associated.Span.End);

                // SPEC 8.5.1: in a Contract a Name with `is` declares its associated Type with that requirement, a Constraint of the
                // Contract; every other item is a specification (8.5.1, 8.5.3), a member of its owner, whatever the owner.
                if (container is ContractKoto && IsAssociatedTypeName(associated.Left, out var qualified) && !qualified)
                {
                    container.AddTypeConstraint(associated);
                }
                else
                {
                    container.AddLast(associated);
                }
            }
        }
        else
        {
            if (container is not ContractKoto)
            {
                // Only a Contract declares an associated Type requirement; the declaration is skipped with its line.
                reader.Unexpected(SyntaxForm.AssociatedTypeDeclaration, token.Span);
                SkipDeclarationLine(ref reader);
                return;
            }

            var name = reader.CurrentToken;
            if (!name.Kind.IsIdentifierOrContextualKeyword())
            {
                OmitDeclaration(ref reader, reader.Expect(SyntaxForm.Name));
                return;
            }

            reader.Advance();
            if (!IdentifierNameKoto.TryCreate(ref reader, name, out var identifier))
            {
                OmitDeclaration(ref reader, reader.Diagnostic.LastError); // The invalid identifier is reported once.
            }
            else
            {
                Koto head = reader.CurrentTokenKind == TokenKind.OpenParenthesis ? ParseOriginApplication(ref reader, identifier) : identifier;
                ValidateAssociatedParameters(ref reader, head);
                var formation = reader.TryConsume(TokenKind.For) ? ParseType(ref reader, TypeContext.Type) : null;
                var associated = new SyntaxFormKoto(
                    ref reader,
                    SourceSpan.FromBounds(name.Span.Start, (formation ?? head).Span.End),
                    KotoKind.AssociatedType,
                    TokenKind.Associate.ToText(),
                    formation is null ? [head] : [head, formation],
                    separator: TokenKind.For.ToText());
                ParseAttachedOriginBlock(ref reader, associated);
                reader.Document(associated, associated.Span.End);
                container.AddLast(associated);
            }
        }
    }

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

        reader.ExpectCloser(TokenKind.CloseParenthesis, out var close);
        end = Math.Max(end, close.End);

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
