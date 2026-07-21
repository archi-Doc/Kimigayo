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

    public override string ToString()
    {
        if (this.BaseList.Count == 0)
        {
            return base.ToString();
        }
        else
        {
            var sb = new StringBuilder();
            sb.Append(base.ToString());
            sb.Append(": ");

            foreach (var x in this.BaseList)
            {
                sb.Append(x.Text);
                sb.Append(", ");
            }

            return sb.ToString();
        }
    }
}
