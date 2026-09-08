// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Declares a named slot or a complete-Type pair.</summary>
public sealed class GenericParameterKoto : TypeKoto
{
    internal GenericParameterKoto(ref TokenReader reader, SourceSpan span, string name, string? semantics)
        : base(ref reader, span)
    {
        this.Identifier = name;
        this.SemanticsParameter = semantics;
    }

    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.GenericParameter;

    /// <inheritdoc/>
    public override string Identifier { get; }

    /// <inheritdoc/>
    public override string? SemanticsParameter { get; }

    /// <inheritdoc/>
    public override SemanticsKind SemanticsKind => this.SemanticsParameter is null ? SemanticsKind.Owner : SemanticsKind.Parameter;

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        if (this.SemanticsParameter is not null)
        {
            builder.Append(this.SemanticsParameter);
            builder.Append('/');
        }

        builder.Append(this.Identifier);
    }
}
