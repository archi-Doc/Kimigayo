// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi;

public static class Constants
{
    public const int IndentationSpaces = 4;
    public const string KimiExtension = ".kimi";
    public const string KimiSolutionExtension = ".kimisln";
    public const string KimiProjectExtension = ".kimiproj";
    public const string RootKotoName = "Root";
    public const string GeneratedFunctionName = "<Generated>";

    // Contextual names used by type semantics and constraints
    public const string OwnerKeyword = "owner";
    public const string RefKeyword = "ref";
    public const string UniqKeyword = "uniq";
    public const string ObjKeyword = "obj";
    public const string ObjRefKeyword = "objref";
    public const string ObjUniqKeyword = "objuniq";
    public const string BorrowKeyword = "borrow";
    public const string RcKeyword = "rc";
    public const string ArcKeyword = "arc";
    public const string UnsafeKeyword = "unsafe";
    public const string RawKeyword = "raw";
    public const string ValueKeyword = "value";
    public const string ValueBorrowKeyword = "valueborrow";
    public const string ObjectKeyword = "object";
    public const string ObjectBorrowKeyword = "objectborrow";
    public const string OwningKeyword = "owning";
    public const string ReferenceKeyword = "reference";

    /// <summary>The transfer operation written after <c>@</c> (SPEC §13.5.3); an ordinary Name elsewhere.</summary>
    public const string MoveOperation = "move";

    /// <summary>The Copy operation written after <c>@</c> (SPEC §13.5.3); an ordinary Name elsewhere.</summary>
    public const string CopyOperation = "copy";

    /// <summary>The postfix follow operation written after <c>@</c> (SPEC §13.5.5); an ordinary Name elsewhere.</summary>
    public const string FollowOperation = "follow";

    /// <summary>The wrapping conversion written as <c>@wrap&lt;U&gt;</c> (SPEC §13.5.4); an ordinary Name elsewhere.</summary>
    public const string WrapOperation = "wrap";

    /// <summary>The bit conversion written as <c>@bits&lt;U&gt;</c> (SPEC §13.5.4); an ordinary Name elsewhere.</summary>
    public const string BitsOperation = "bits";

    /// <summary>The word that starts an effect item (SPEC §8.11.1); an ordinary Name elsewhere.</summary>
    public const string EffectKeyword = "effect";

    /// <summary>The <c>confined</c> effect bound (SPEC §8.11.2); an ordinary Name elsewhere.</summary>
    public const string ConfinedKeyword = "confined";

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

    public static ReadOnlySpan<char> CommaAndSpace => ", ";
}
