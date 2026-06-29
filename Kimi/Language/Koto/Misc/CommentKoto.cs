// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimigayo.Diagnostics;

namespace Kimigayo.Language;

[TinyhandObject]
public partial class CommentKoto : Koto
{
    [Key(1)]
    public string Comment { get; private set; }

    public CommentKoto(ref TokenReader reader, Token token)
        : base(ref reader, token.Range)
    {
        this.Comment = token.Text.ToString();
    }

    public override string ToString()
        => $"/*{this.Comment}*/";
}
