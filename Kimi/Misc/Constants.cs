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
    public const string Scrub2FileName = "Scrub2.kimi";
    public const string RootKotoName = "Root";

    // Primitive type keywords
    public const string BoolKeyword = "bool";
    public const string IsizeKeyword = "isize";
    public const string UsizeKeyword = "usize";
    public const string I8Keyword = "i8";
    public const string I16Keyword = "i16";
    public const string I32Keyword = "i32";
    public const string I64Keyword = "i64";
    public const string I128Keyword = "i128";
    public const string U8Keyword = "u8";
    public const string U16Keyword = "u16";
    public const string U32Keyword = "u32";
    public const string U64Keyword = "u64";
    public const string U128Keyword = "u128";
    public const string F32Keyword = "f32";
    public const string F64Keyword = "f64";
    public const string StringKeyword = "string";

    // Language keywords
    public const string TrueKeyword = "true";
    public const string FalseKeyword = "false";
    public const string LetKeyword = "let";
    public const string VarKeyword = "var";
    public const string FuncKeyword = "func";
    public const string IfKeyword = "if";
    public const string ElseKeyword = "else";
    public const string AsKeyword = "as";
    public const string IsKeyword = "is";
    public const string NotKeyword = "not";
    public const string AndKeyword = "and";
    public const string OrKeyword = "or";
    public const string ForKeyword = "for";
    public const string WhileKeyword = "while";
    public const string LoopKeyword = "loop";
    public const string MatchKeyword = "match";
    public const string ReturnKeyword = "return";
    public const string BreakKeyword = "break";
    public const string ContinueKeyword = "continue";
    public const string YieldKeyword = "yield";

    // Contextual keywords
    public const string RootgroupKeyword = "rootgroup";
    public const string AliasKeyword = "alias";
    public const string GroupKeyword = "group";
    public const string StructKeyword = "struct";
    public const string EnumKeyword = "enum";
    public const string ExtensionKeyword = "extension";
    public const string ContractKeyword = "contract";
    public const string StaticKeyword = "static";
    public const string PublicKeyword = "public";
    public const string ProtectedKeyword = "protected";
    public const string PrivateKeyword = "private";
    public const string InternalKeyword = "internal";
    public const string ProtectedOrInternalKeyword = "protected_or_internal";
    public const string ProtectedAndInternalKeyword = "protected_and_internal";
    public const string OpenKeyword = "open";

    // Contextual names used by type semantics and constraints
    public const string SemanticsKeyword = "semantics";
    public const string OriginKeyword = "origin";
    public const string FromKeyword = "from";
    public const string SelfKeyword = "Self";
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
    public const string ValueKeyword = "value";
    public const string ValueBorrowKeyword = "valueborrow";
    public const string ObjectKeyword = "object";
    public const string ObjectBorrowKeyword = "objectborrow";
    public const string OwningKeyword = "owning";
    public const string ReferenceKeyword = "reference";

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
