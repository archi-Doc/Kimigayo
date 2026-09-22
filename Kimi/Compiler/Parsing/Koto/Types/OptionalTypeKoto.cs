// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

/// <summary>A source-preserving Option suffix; Binding supplies the recognized Identity.</summary>
public sealed class OptionalTypeKoto : TypeKoto
{
    internal OptionalTypeKoto(ref TokenReader reader, SourceSpan span, Koto type)
        : base(ref reader, span)
    {
        this.Type = type;
        this.Adopt(type);
    }

    /// <inheritdoc/>
    public override KotoKind Akind => KotoKind.OptionalType;

    /// <summary>Gets the complete payload Type syntax.</summary>
    public Koto Type { get; private set; }

    /// <inheritdoc/>
    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.Type.WriteTo(ref builder);
        builder.Append('?');
    }

    protected override void VisitChildrenCore(KotoVisitor visitor) => visitor.Visit(this.Type);

    protected override IEnumerable<Koto> GetChildNodes()
    {
        yield return this.Type;
    }

    protected override bool ReplaceChildCore(Koto oldKoto, Koto newKoto)
    {
        if (this.Type != oldKoto)
        {
            return false;
        }

        this.Type = newKoto;
        return true;
    }
}
