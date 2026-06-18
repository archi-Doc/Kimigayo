// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

[TinyhandObject]
public partial class CommentKoto : Koto
{
    [Key(0)]
    public string Comment { get; private set; }

    public CommentKoto(FileRoot rootNode, Token token)
        : base(rootNode)
    {
        this.Comment = token.Text.ToString();
    }
}
