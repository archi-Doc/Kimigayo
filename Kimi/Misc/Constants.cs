// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi;

public static class Constants
{
    public const int IndentationSpaces = 4;
    public const string KimiExtension = ".kimi";
    public const string KimiSolutionExtension = ".kimisln";
    public const string KimiProjectExtension = ".kimiproj";
    public const string TokenExtension = ".token";
    public const string DefaultNamespace = "Playground";
    public const string ScrubFileName = "Scrub.kimi";
    public const string RootgroupKeyword = "rootgroup";
    public const string AliasKeyword = "alias";
    public const string GroupKeyword = "group";
    public const string StructKeyword = "struct";
    public const string EnumKeyword = "enum";
    public const string ExtensionKeyword = "extension";
    public const string ContractKeyword = "contract";
    public const string FuncKeyword = "func";
    public const string StaticKeyword = "static";
    public const string PublicKeyword = "public";
    public const string ProtectedKeyword = "protected";
    public const string PrivateKeyword = "private";
    public const string InternalKeyword = "internal";
    public const string ProtectedOrInternalKeyword = "protected_or_internal";
    public const string ProtectedAndInternalKeyword = "protected_and_internal";
    public const string OpenKeyword = "open";

    public const int ExclusiveUpperBound = 126; // '}' + 1
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
    public const char AtChar = '@';
    public const char SharpChar = '#';
    public const char SpaceChar = ' ';
    public const char QuestionChar = '?';

    public static ReadOnlySpan<char> Move => "<=";

    public static ReadOnlySpan<char> Map => "=>";

    public static ReadOnlySpan<char> NamespaceKeyword => "namespace";

    public static ReadOnlySpan<char> IfAttribute => "If";

    public static ReadOnlySpan<char> CommaAndSpace => ", ";
}
