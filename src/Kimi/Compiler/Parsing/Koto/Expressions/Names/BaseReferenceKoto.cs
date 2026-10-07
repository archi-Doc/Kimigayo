// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;

namespace Kimi.Compiler.Parsing;

/// <summary>The lexical self reference used only as a direct base-call receiver.</summary>
public sealed class BaseReferenceKoto : IdentifierNameKoto
{
    private BoundMemberPath? basePath;

    internal BaseReferenceKoto(ref TokenReader reader, Token token)
        : base(ref reader, token, "self")
    {
    }

    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.BaseReference;

    internal BoundMemberPath? BasePath
    {
        get => this.HasCurrentBinding ? this.basePath : null;
        set => this.basePath = value;
    }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder) => builder.Append("base");
}
