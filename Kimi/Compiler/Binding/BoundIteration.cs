// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Reusable ordinary calls and Option decomposition for a user iteration entry.</summary>
internal sealed class BoundIteration
{
    internal BoundIteration(ForKoto source)
    {
        this.Scope = new(source);
        var reader = new TokenReader(source.CodeContext, [], []);
        // The internal local is already declared when the step at the for header is evaluated.
        var name = new IdentifierNameKoto(ref reader, new(TokenKind.Identifier, new Kimi.Diagnostics.SourceSpan(source.Span.Start, 0)), "$for.iterator");
        this.Iterator = new FieldKoto(ref reader, new(TokenKind.Var, name.Span), name, null, null) { Parent = source };
        this.Receiver = new IdentifierNameKoto(source, "$for.iterator");
        this.Next = new InvocationKoto(source, new MemberAccessKoto(source, this.Receiver, new IdentifierNameKoto(source, "next")), []);
        var some = new IdentifierNameKoto(source, "$for.some");
        var none = new IdentifierNameKoto(source, "$for.none");
        var end = new UnitLiteralKoto(ref reader, source.Span);
        this.Match = new MatchKoto(ref reader, source.Span, this.Next, [new(some, source.Body), new(none, end)]) { Parent = source };
        source.Body.Parent = source;
        this.Item = new IdentifierNameKoto(source, "$for.item");
    }

    internal FieldKoto Iterator { get; }

    internal BindingScope Scope { get; }

    internal IdentifierNameKoto Receiver { get; }

    internal InvocationKoto Next { get; }

    internal MatchKoto Match { get; }

    internal IdentifierNameKoto Item { get; }

    internal BoundMatch Decomposition { get; } = new();
}
