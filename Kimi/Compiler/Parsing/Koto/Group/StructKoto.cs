// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public sealed partial class StructKoto : GroupKoto
{
    public override KotoKind _Kind => KotoKind.Struct;

    #region FieldAndProperty

    [IgnoreMember]
    public List<Token> BaseList { get; } = [];

    #endregion

    public StructKoto(ref TokenReader reader, SourceRange range)
        : base(ref reader, range)
    {
    }

    internal StructKoto(CodeContext codeContext, TokenState state, SourceRange range)
        : base(codeContext, state, range)
    {
    }

    public override void WriteTo(ref IndentedStringBuilder builder)
    {// public group A: @B
        base.WriteTo(ref builder);

        if (this.BaseList.Count != 0)
        {
            builder.Append(": ");

            foreach (var x in this.BaseList)
            {
                builder.Append(x.Text);
                builder.Append(", ");
            }
        }
    }
}
