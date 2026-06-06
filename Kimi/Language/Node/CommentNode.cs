// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

public class CommentNode : Node
{
    public ReadOnlyMemory<char> Comment { get; }

    public CommentNode(Token token)
    {
        this.Comment = token.Text;
    }
}
