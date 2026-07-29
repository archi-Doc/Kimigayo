// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public sealed partial class StructKoto : GroupKoto
{
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

    public override void UnparseTo(ref IndentedStringBuilder writer)
    {// public group A: @B
        base.UnparseTo(ref writer);

        if (this.BaseList.Count != 0)
        {
            writer.Append(": ");

            foreach (var x in this.BaseList)
            {
                writer.Append(x.Text);
                writer.Append(", ");
            }
        }
    }
}
