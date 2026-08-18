// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public partial class TupleTypeKoto : Koto
{
    public override KotoKind Akind => KotoKind.TupleType;

    [Key(1)]
    public List<Koto> Elements { get; private set; }

    public TupleTypeKoto(ref TokenReader reader, SourceRange range, List<Koto> elements)
        : base(ref reader, range)
    {
        this.Elements = elements;
    }

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        builder.Append('(');
        for (var i = 0; i < this.Elements.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendCommaAndSpace();
            }

            this.Elements[i].WriteTo(ref builder);
        }

        builder.Append(')');
    }
}
