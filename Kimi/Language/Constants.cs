// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo;

public static class Constants
{
    public const int IndentationSpaces = 4;
    public const string KimiExtension = ".kimi";
    public const string KimiSolutionExtension = ".kimisln";
    public const string KimiProjectExtension = ".kimiproj";

    public const char LfChar = '\n';
    public const char CrChar = '\r';

    public const char AmpersandChar = '&';
    public const char AsteriskChar = '*';
    public const char BarChar = '|';
    public const char CaretChar = '^';
    public const char GreaterThanChar = '>';
    public const char ExclamationChar = '!';
    public const char LessThanChar = '<';
    public const char MinusChar = '-';
    public const char PercentChar = '%';
    public const char PlusChar = '+';
    public const char SlashChar = '/';
    public const char CommaChar = ',';
    public const char ColonChar = ':';
    public const char CloseBraceChar = '}';
    public const char CloseBracketChar = ']';
    public const char CloseParenthesisChar = ')';
    public const char DollarChar = '$';
    public const char DotChar = '.';
    public const char EqualsChar = '=';
    public const char OpenBraceChar = '{';
    public const char OpenBracketChar = '[';
    public const char OpenParenthesisChar = '(';
    public const char SemicolonChar = ';';
    public const char SharpChar = '#';
    public const char SpaceChar = ' ';
    public const char TildeChar = '~';
    public const char QuestionChar = '?';

    public static ReadOnlySpan<char> Move => "<=";

    public static ReadOnlySpan<char> Map => "=>";
}
