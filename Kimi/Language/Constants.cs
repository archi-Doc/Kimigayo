// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo;

public static class Constants
{
    public const int IndentationSpaces = 4;

    public const char LfChar = '\n';
    public const char CrChar = '\r';

    public const char AttributeChar = '#';
    public const char CommaChar = ',';
    public const char ColonChar = ':';
    public const char CloseBracketChar = ']';
    public const char CloseParenthesisChar = ')';
    public const char DollarChar = '$';
    public const char DotChar = '.';
    public const char EqualsChar = '=';
    public const char OpenParenthesisChar = '(';
    public const char OpenBracketChar = '[';
    public const char SemicolonChar = ';';
    public const char SpaceChar = ' ';
    public const char TildeChar = '~';

    public static ReadOnlySpan<char> Move => "<=";

    public static ReadOnlySpan<char> Map => "=>";
}
