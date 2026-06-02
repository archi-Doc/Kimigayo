// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo;

public static class Constants
{
    public const int IndentationSpaces = 4;

    public const char LfChar = '\n';
    public const char CrChar = '\r';
    public const char SpaceChar = ' ';
    public const char AttributeChar = '#';
    public const char AssignmentChar = '=';
    public const char OpenParenthesisChar = '(';
    public const char CloseParenthesisChar = ')';
    public const char OpenBracketChar = '[';
    public const char CloseBracketChar = ']';

    public static ReadOnlySpan<char> Move => "<=";

    public static ReadOnlySpan<char> Map => "=>";
}
