// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public partial class GenericsKoto : UnaryKoto
{// A<B, C>
    private readonly List<Koto> typeList;

    public GenericsKoto(ref TokenReader reader, SourceRange range, Koto operand, List<Koto> typeList)
        : base(ref reader, range, operand)
    {
        this.typeList = typeList;
    }

    public override void WriteTo(ref IndentedStringBuilder builder)
    {
        this.Operand.WriteTo(ref builder);
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
