// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;

namespace Kimi.Compiler.Parsing;

/// <summary>
/// Represents a local binding declaration.
/// </summary>
[TinyhandObject]
public sealed partial class FieldKoto : VariableKoto
{
    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.Field;

    /// <summary>Initializes a new instance of the <see cref="FieldKoto"/> class.</summary>
    /// <param name="reader">The token reader.</param>
    /// <param name="token">The declaration keyword token.</param>
    /// <param name="nameKoto">The declared name.</param>
    /// <param name="typeKoto">The declared type, if specified.</param>
    /// <param name="initializerKoto">The initializer expression, if present.</param>
    public FieldKoto(ref TokenReader reader, Token token, IdentifierNameKoto nameKoto, Koto? typeKoto, Koto? initializerKoto)
        : base(ref reader, token, nameKoto, typeKoto, initializerKoto)
    {
    }
}
