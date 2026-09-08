// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Declares a length slot without treating its name as a type slot.</summary>
public sealed class LengthParameterKoto : TypeKoto
{
    internal LengthParameterKoto(ref TokenReader reader, SourceSpan span, string name)
        : base(ref reader, span) => this.Identifier = name;

    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.LengthParameter;

    /// <inheritdoc/>
    public override string Identifier { get; }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append("length ");
        builder.Append(this.Identifier);
    }
}
