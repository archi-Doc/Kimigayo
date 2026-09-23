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
        Koto body = this;
        var count = 0;
        while (body is OptionalTypeKoto optional)
        {
            count++;
            body = optional.Type;
        }

        var annotated = body as TypeSemanticsKoto;
        if (annotated is { IsTransparentWrapper: false, Type: not null, HasOrigin: true })
        {
            annotated.WriteTypeTo(ref builder, writeBorrowOrigin: false);
        }
        else
        {
            annotated = null;
            body.WriteTo(ref builder);
        }

        for (var i = 0; i < count; i++)
        {
            builder.Append('?');
        }

        annotated?.WriteOriginTo(ref builder);
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
