// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

// The headers without a grammar of their own: a Declaration Container's after its keyword (SPEC 6.1, 8.4) and a local let or var binding
// (SPEC 6.6), with the declared Name and the initializer a binding shares with a Property.
// Input: a reader after the declaration keyword; a Container's keyword kind, which selects the parts its header takes.
// Output: a Container's Name, Type parameters, Origin names and bases for DeclarationContainerKoto; a FieldKoto.
// Invariants: a failed Name drops the declaration with its line and body, so nothing of it is kept (Parser.Recovery).
// Prohibitions: a Container's items belong to DeclarationContainerKoto, and which Type or initializer is eligible to Binding.
public static partial class Parser
{
    /// <summary>Parses a Declaration Container header after its keyword, according to the capabilities of its kind (SPEC 6.1, 8.4).</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="declarationKind">The declaration keyword kind; a struct, enum or Contract takes Type parameters, a struct or enum an Origin header, a struct or Contract a base list.</param>
    /// <returns>The name, generic parameters, origin names and bases; the reader stands at the body's <see cref="TokenKind.StartBlock"/> when one follows.
    /// The Name is <see langword="null"/> after an Error at it, and the caller skips the declaration with its body.</returns>
    internal static (string? Name, List<TypeKoto>? GenericArguments, List<string>? Origins, Koto[]? Bases) ParseDeclarationContainerHeader(ref TokenReader reader, TokenKind declarationKind)
    {
        var supportsGenerics = declarationKind is TokenKind.Struct or TokenKind.Enum or TokenKind.Contract;
        var supportsOrigins = declarationKind is TokenKind.Struct or TokenKind.Enum;
        List<TypeKoto>? genericArguments = default;
        List<string>? origins = default;
        Koto[]? bases = null;
        if (!reader.TryReadName(out var name, out _))
        {
            goto Exit;
        }

        if (supportsGenerics && reader.CurrentTokenKind == TokenKind.LessThan)
        {
            genericArguments = ParseGenericArguments(ref reader);
        }

        if (supportsOrigins)
        {
            origins = ParseOriginParameters(ref reader);
        }

        if (declarationKind is TokenKind.Struct or TokenKind.Contract && reader.TryConsume(TokenKind.Colon))
        {
            var types = default(TemporaryKotoList);
            do
            {
                types.Add(ParseType(ref reader, TypeContext.Type));
            }
            while (reader.TryConsume(TokenKind.Comma));
            bases = types.ToArray();
            if (declarationKind == TokenKind.Struct && bases.Length != 1)
            {
                // The recovery keeps the first base, so the struct is checked as derived from it alone.
                reader.Unexpected(SyntaxForm.BaseList, SourceSpan.FromBounds(bases[0].Span.Start, bases[^1].Span.End));
                bases = bases[..1];
            }
        }

        // The header ends with its line; a body may follow on indented lines.
        reader.ExpectLineEnd(bodyMayFollow: true);
        reader.TrySkipSeparatorsTo(TokenKind.StartBlock);

Exit:
        return (name, genericArguments, origins, bases);
    }

    /// <summary>
    /// Reads the Name of a let, var, computed or property declaration after its keyword. A keyword or other token there is
    /// where the Name was expected, and an invalid spelling is its own lexical Error; either way the rest of the declaration's
    /// line and the body it would have introduced are skipped, so nothing of the declaration is kept.
    /// </summary>
    private static IdentifierNameKoto? ParseDeclarationName(ref TokenReader reader)
    {
        reader.ReportMisplacedAttributes();
        var token = reader.CurrentToken;
        if (!token.Kind.IsIdentifierOrContextualKeyword())
        {
            OmitDeclaration(ref reader, reader.Expect(SyntaxForm.Name));
            return null;
        }

        reader.Advance();
        if (IdentifierNameKoto.TryCreate(ref reader, token, out var name))
        {
            return name;
        }

        OmitDeclaration(ref reader, reader.Diagnostic.LastError); // The invalid identifier is reported once.
        return null;
    }

    // SPEC 4.3.4: only a complete, unparenthesized declaration initializer is the directive. Eligibility is a Binding
    // check; even an ineligible declaration never falls back to a same-named value.
    private static Koto ParseVariableInitializer(ref TokenReader reader)
    {
        var value = ParseRequiredExpression(ref reader);
        if (value is not IdentifierNameKoto { IdentifierName: "noinit" })
        {
            return value;
        }

        return new NoInitKoto(ref reader, value.Span);
    }

    /// <summary>Parses a local binding declaration after its keyword (SPEC 6.6).</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="token">The declaration keyword token.</param>
    /// <returns>The parsed declaration, or <see langword="null"/> after an error.</returns>
    private static FieldKoto? ParseField(ref TokenReader reader, Token token)
    {
        // A local binding takes no access word, at the root and in a body alike (SPEC F.6.3 LocalBinding).
        RejectAccessWord(ref reader);
        var variableContext = reader.TakeContext();
        if (ParseDeclarationName(ref reader) is not { } nameKoto)
        {
            return null;
        }

        Koto? typeKoto = default;
        var inferredArrayElement = false;
        if (reader.TryConsume(TokenKind.Colon, out _, false))
        {
            reader.ReportMisplacedAttributes();

            // [N of _] may be the annotation itself or a directly nested array element (SPEC 4.3).
            reader.AllowArrayElementInference = reader.CurrentTokenKind == TokenKind.OpenBracket;
            reader.HasInferredArrayElement = false;
            try
            {
                typeKoto = ParseType(ref reader, TypeContext.Type);
                inferredArrayElement = reader.HasInferredArrayElement;
            }
            finally
            {
                reader.AllowArrayElementInference = false;
                reader.HasInferredArrayElement = false;
            }
        }

        Koto? initializerKoto = default;
        if (reader.TryConsume(TokenKind.Equals, out _, false))
        {
            initializerKoto = ParseVariableInitializer(ref reader);
        }

        // A binding states its Type or has an initializer, and an inferred element Type needs the initializer; the recovery
        // Type stands for what is missing, so the uses of the binding rest on this Error (SPEC 23.1.2).
        if (typeKoto is null && initializerKoto is null)
        {
            typeKoto = new ErrorKoto(ref reader, new SourceSpan(nameKoto.Span.End, 0)) { Cause = reader.Expect(SyntaxForm.TypeOrInitializer) };
        }
        else if (inferredArrayElement && initializerKoto is null)
        {
            typeKoto = new ErrorKoto(ref reader, typeKoto!.Span) { Cause = reader.Expect(SyntaxForm.Initializer) };
        }

        reader.RestoreContext(variableContext with { AttributeKoto = null });

        var fieldKoto = new FieldKoto(ref reader, token, nameKoto, typeKoto, initializerKoto);
        fieldKoto.SetAttributeChain(variableContext.AttributeKoto);
        reader.Document(fieldKoto, nameKoto.Span.End, variableContext.AttributeKoto);

        ParseAttachedOriginBlock(ref reader, fieldKoto);
        reader.ExpectLineEnd();
        return fieldKoto;
    }
}
