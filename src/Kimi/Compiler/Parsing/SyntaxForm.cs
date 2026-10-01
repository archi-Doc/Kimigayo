// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Parsing;

/// <summary>
/// A form of syntax the parser can expect at a position or find where it is not permitted (docs/dev/DIAGNOSTICS.md §4.4).
/// Each value is a requirement of the Syntax partition, <c>Syntax.&lt;Form&gt;</c>, whose phrase and advice come from
/// <c>DiagnosticRequirement.tinyhand</c>; a reporting site chooses a value and never writes text.
/// </summary>
public enum SyntaxForm : ushort
{
    /// <summary>The whole grammar; the requirement of lexical and rule checks that name no form.</summary>
    None,

    /// <summary>An expression, including an operand or an argument.</summary>
    Expression,

    /// <summary>A Type.</summary>
    Type,

    /// <summary>A Name.</summary>
    Name,

    /// <summary>The body of a function, a conditional or a loop.</summary>
    Body,

    /// <summary>The end of the line after a complete statement or header.</summary>
    LineEnd,

    /// <summary>A comma between elements.</summary>
    Comma,

    /// <summary>A colon.</summary>
    Colon,

    /// <summary>The closing parenthesis of an open grouping.</summary>
    CloseParenthesis,

    /// <summary>The closing bracket of an open grouping.</summary>
    CloseBracket,

    /// <summary>The closing brace of an open grouping.</summary>
    CloseBrace,

    /// <summary>The closing angle bracket of an open Type argument list.</summary>
    CloseAngleBracket,

    /// <summary>A closing delimiter with no open grouping; the delimiter itself is underlined.</summary>
    UnmatchedCloser,

    /// <summary>An indented body that no header introduces.</summary>
    IndentedBody,

    /// <summary>An opening parenthesis.</summary>
    OpenParenthesis,

    /// <summary>A declaration, which attributes, modifiers and compile-time prefixes introduce.</summary>
    Declaration,

    /// <summary>The Type annotation or the initializer a let or var declaration needs.</summary>
    TypeOrInitializer,

    /// <summary>The initializer an inferred element Type needs.</summary>
    Initializer,

    /// <summary>The get accessor a computed Property or Property requirement declares.</summary>
    Getter,

    /// <summary>An attribute where none is permitted.</summary>
    Attribute,

    /// <summary>The uppercase Name of an attribute.</summary>
    AttributeName,

    /// <summary>A member access, Type argument list or index after an attribute's Name.</summary>
    AttributeSuffix,

    /// <summary>The static keyword used as a modifier.</summary>
    StaticModifier,

    /// <summary>The open modifier on a declaration other than a struct.</summary>
    OpenModifier,

    /// <summary>A modifier or attribute on a declaration that takes none.</summary>
    Decoration,

    /// <summary>A receiver parameter that is repeated, renamed, defaulted or declared outside a struct or Contract.</summary>
    ReceiverParameter,

    /// <summary>A default value or attribute on a Contract requirement's parameter.</summary>
    RequirementParameterDefault,

    /// <summary>A body on a Contract requirement.</summary>
    RequirementBody,

    /// <summary>A qualified Name in a function declaration.</summary>
    QualifiedFunctionName,

    /// <summary>A Name on a function expression.</summary>
    FunctionExpressionName,

    /// <summary>A header element that init does not take.</summary>
    ConstructorHeader,

    /// <summary>A header element a specialization does not take, or the Type arguments it lacks.</summary>
    SpecializationHeader,

    /// <summary>The base list of a struct with other than one base.</summary>
    BaseList,

    /// <summary>An extension declaration, which the language does not introduce.</summary>
    ExtensionDeclaration,

    /// <summary>An associate declaration outside a Contract.</summary>
    AssociatedTypeDeclaration,

    /// <summary>A Property declaration in a container that does not take it.</summary>
    PropertyDeclaration,
}
