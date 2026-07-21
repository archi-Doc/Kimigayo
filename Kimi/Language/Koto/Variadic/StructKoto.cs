// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Arc.Collections;
using Kimigayo.Diagnostics;
using Kimigayo.Language;

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

    internal StructKoto(CodeContext codeContext, TokenState state)
        : base(codeContext, state)
    {
    }

    public override void UnparseTo(StringWriter writer)
    {// public group A: @B
        base.UnparseTo(writer);

        if (this.BaseList.Count != 0)
        {
            writer.Write(": ");

            foreach (var x in this.BaseList)
            {
                writer.Write(x.Text);
                writer.Write(", ");
            }
        }
    }
}
