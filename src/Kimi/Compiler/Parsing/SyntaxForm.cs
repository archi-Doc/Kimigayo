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

    /// <summary>The keyword else.</summary>
    ElseKeyword,

    /// <summary>The keyword in of a for header.</summary>
    InKeyword,

    /// <summary>The keyword base of a constructor initializer.</summary>
    BaseKeyword,

    /// <summary>The Type annotation of a parameter.</summary>
    ParameterType,

    /// <summary>An external Name, default value or attribute on a function expression's parameter.</summary>
    FunctionExpressionParameter,

    /// <summary>A positional argument after a named one.</summary>
    PositionalAfterNamed,

    /// <summary>The argument list of a $require or $expect verification.</summary>
    TestVerificationArguments,

    /// <summary>The Type annotation of a computed Property or Property requirement.</summary>
    TypeAnnotation,

    /// <summary>An initializer on a computed Property or Property requirement.</summary>
    ComputedInitializer,

    /// <summary>A has accessor list on a Property that is not a Contract requirement.</summary>
    InlineAccessors,

    /// <summary>A get or set accessor.</summary>
    Accessor,

    /// <summary>An access modifier on a Contract requirement's accessor.</summary>
    AccessorAccessibility,

    /// <summary>The parameter list of a custom accessor or accessor requirement.</summary>
    AccessorParameters,

    /// <summary>The result Type after an accessor's parameter list.</summary>
    AccessorResult,

    /// <summary>A result Type other than () on a setter.</summary>
    SetterResult,

    /// <summary>The value parameter of a setter.</summary>
    SetterValueParameter,

    /// <summary>An 'origin' clause after an accessor or an executable item.</summary>
    OriginClause,

    /// <summary>A Constraint prefix after an executable item, or one whose subject the declaration does not permit.</summary>
    ConstraintPrefix,

    /// <summary>A '?' after a bare Semantics shorthand such as 'ref'.</summary>
    SemanticsShorthandSuffix,

    /// <summary>'move' or 'copy' written as a Semantics prefix.</summary>
    OperationAsSemanticsPrefix,

    /// <summary>An Origin annotation on the borrow layer of an adaptation target.</summary>
    AdaptationOrigin,

    /// <summary>A 'during' annotation after an ungrouped adaptation target.</summary>
    AdaptationDuring,

    /// <summary>An if expression nested directly in the body of an if.</summary>
    NestedIfExpression,

    /// <summary>The '=>' that introduces a body nested in an expression body.</summary>
    ArrowBody,

    /// <summary>A body-bearing expression such as if, match, for or func in a header.</summary>
    HeaderBodyExpression,

    /// <summary>A try expression as the direct operand of a prefix operator.</summary>
    TryOperand,

    /// <summary>A second range operator in one range.</summary>
    ChainedRange,

    /// <summary>The operand of a '$' prefix: abort(expression) or tryWrite(writer, literal).</summary>
    DollarOperand,

    /// <summary>The one Type argument of a conversion operation.</summary>
    ConversionTypeArgument,

    /// <summary>A compound array length that is not parenthesized.</summary>
    CompoundArrayLength,

    /// <summary>The Function Parameter List before '->'.</summary>
    FunctionParameterList,

    /// <summary>A 'length' parameter outside a function's Type parameter list.</summary>
    LengthParameter,

    /// <summary>'var' on the wildcard '_'.</summary>
    WildcardBinding,

    /// <summary>A 'during' annotation on its own line.</summary>
    DetachedDuring,

    /// <summary>The operation of a capture: move, ref or uniq.</summary>
    CaptureOperation,

    /// <summary>A Constraint in a signature's clause block.</summary>
    Constraint,

    /// <summary>The 'of' between a length and an element Type.</summary>
    OfKeyword,

    /// <summary>A literal Pattern: an integer, char, string or bool literal.</summary>
    PatternLiteral,

    /// <summary>A qualified case Pattern.</summary>
    CasePattern,

    /// <summary>A Pattern.</summary>
    Pattern,

    /// <summary>An 'else' that no if body precedes; its own body is read as part of the recovery.</summary>
    ElseWithoutIf,
}
