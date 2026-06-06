// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

public static class NodeHelper
{
    public static Node FromToken(Token token)
    {
        var code = token.Kind switch
        {
            TokenKind.SingleLineComment => new CommentNode(token),
            _ => default!,
        };

        return code;
    }
}
