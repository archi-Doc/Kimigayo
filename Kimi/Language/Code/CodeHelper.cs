// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

public static class CodeHelper
{

    public static Code FromToken(Token token)
    {
        var code = token.Kind switch
        {
            TokenKind.SingleLineComment => new CommentCode(token),
            _ => default!,
        };

        return code;
    }
}
