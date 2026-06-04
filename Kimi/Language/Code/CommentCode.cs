// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

public class CommentCode : Code
{
    public ReadOnlyMemory<char> Comment { get; }

    public CommentCode(Token token)
    {
        this.Comment = token.Text;
    }
}
