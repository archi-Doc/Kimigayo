// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>Applies existing Origin atoms to an associated Type, or records its declaration parameters.</summary>
public sealed class OriginApplicationKoto : ApplicationKoto
{
    internal OriginApplicationKoto(ref TokenReader reader, SourceSpan span, Koto type, IReadOnlyList<Koto> arguments)
        : base(ref reader, span, type, arguments)
    {
    }

    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.OriginApplication;

    /// <summary>Gets the associated-Type name or projection.</summary>
    public Koto Type => this.Target;

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.Type.WriteTo(ref builder);
        this.WriteArgumentsTo(ref builder, '(', ')');
    }
}
