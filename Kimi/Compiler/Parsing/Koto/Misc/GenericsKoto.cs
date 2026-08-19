// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public partial class GenericsKoto : Koto
{// A<B, C>
    public override KotoKind Akind => KotoKind.Generics;

    [IgnoreMember]
    public Koto Identifier { get; private set; }

    private readonly List<Koto> typeList;

    public GenericsKoto(ref TokenReader reader, SourceSpan range, Koto identifier, List<Koto> typeList)
        : base(ref reader, range)
    {
        this.Identifier = identifier;
        this.typeList = typeList;
    }

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.Identifier.WriteTo(ref builder);
        // builder.Append(this.Identifier);
        builder.Append(Constants.LessThanChar);

        for (var i = 0; i < this.typeList.Count; i++)
        {
            this.typeList[i].WriteTo(ref builder);

            if (i != (this.typeList.Count - 1))
            {
                builder.AppendCommaAndSpace();
            }
        }

        builder.Append(Constants.GreaterThanChar);
    }
}
