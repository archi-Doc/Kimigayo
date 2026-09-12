// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Kimi.Diagnostics;

namespace Kimi;

public enum DiagnosticCode
{
    Template_Kd, // First sentinel

    UnresolvedBinding_Kd,
    AmbiguousBinding_Kd,
    DuplicateBinding_Kd,
    NotCallable_Kd,
    NoApplicableOverload_Kd,
    CyclicBinding_Kd,
    InvalidAssignment_Kd,
    InaccessibleBinding_Kd,
    InvalidCaptureBinding_Kd,
    UnsupportedBinding_Kd,
    InvalidOriginBinding_Kd,
    MissingOriginBinding_Kd,
    InvalidTypeFormation_Kd,
    InvalidConstraint_Kd,
    UnprovenConstraint_Kd,
    UnsatisfiedConstraint_Kd,
    InvalidCoreIntrinsics_Kd,
    MissingContractImplementation_Kd,
    IncompatibleContractImplementation_Kd,
    InvalidAssociatedType_Kd,

    ConditionMustBeBool_Kd,
    InvalidCompileTimeCondition_Kd,
    UnknownCompileTimeName_Kd,
    InvalidCompileTimeSetting_Kd,
    UnsupportedLanguageVersion_Kd,
    CompileTimeCaseOutsideSwitch_Kd,
    EmptyCompileTimeSwitch_Kd,
    InvalidCompileTimeSwitchItem_Kd,
    CompileTimeCaseFallbackMustBeLast_Kd,
    DuplicateCompileTimeCaseFallback_Kd,
    DeclarationOrderWarning_Kd,
    DivisionByZero_Kd,
    DuplicateModifier_Kd,
    DuplicatePropertyAccessor_Kd,
    DuplicateTypeConstraintDefinition_Kd,
    IdentifierExpected_Kd,
    IncompleteEscape_Kd,
    IncompleteSyntax_Kd,
    IndentationLevelMismatch_Kd,
    IndentationLevelMismatchWarning_Kd,
    InvalidAttributeKoto_Kd,
    InvalidCharacter_Kd,
    SemicolonNotAllowed_Kd,
    InvalidCharLiteral_Kd,
    InvalidSourceEncoding_Kd,
    InvalidCharacterAtEndOfFile_Kd,
    InvalidIdentifier_Kd,
    InvalidIndentation_Kd,
    InvalidNumericLiteral_Kd,
    InvalidReferenceSyntax_Kd,
    InvalidSemanticsConstraint_Kd,
    InvalidUnicodeEscape_Kd,
    InvalidUnicodeScalar_Kd,
    IntegerOverflow_Kd,
    LetPropertyCannotHaveSetter_Kd,
    MissingBlockCommentEnd_Kd,
    CodeAfterMultilineComment_Kd,
    MissingComma_Kd,
    MissingExpectedToken_Kd,
    MissingStringLiteralEnd_Kd,
    MissingCharLiteralEnd_Kd,
    MultipleAccessibilityModifiers_Kd,
    NonExhaustiveCompileTimeCase_Kd,
    TokenMismatch_Kd,
    TopLevelKeywordAfterCode_Kd,
    TypeMismatch_Kd,
    UnexpectedIndent_Kd,
    UnexpectedToken_Kd,
    UnexpectedTrailingToken_Kd,
    UnmatchedEndBlock_Kd,
    UnmatchedAngleBracket_Kd,
    UnmatchedBrace_Kd,
    UnmatchedBracket_Kd,
    UnmatchedParenthesis_Kd,
    UnmatchedToken_Kd,
    UnsupportedCompileTimeConditionType_Kd,
    UnsupportedEscape_Kd,

    ControlFlow_Kd,

    MissingReturnType_Kd,

    EmptyExecutableBlock_Kd,
    BlockStatementInExpression_Kd,
    InvalidInlineStatement_Kd,

    ChainedComparison_Kd,

    MissingStartupBody_Kd,
    MixedStartupBodies_Kd,
    MultipleStartupSources_Kd,
    MultipleStartupMains_Kd,
    InvalidStartupMain_Kd,
    LibraryRuntimeBody_Kd,

    UninitializedPlace_Kd,
    MovedPlace_Kd,
    ReassignedLet_Kd,
    UnsupportedOwnership_Kd,

    InvalidPattern_Kd,
    NonExhaustiveMatch_Kd,
    UnreachablePattern_Kd,
    ControlFlowWarning_Kd,
    OuterCloserInBody_Kd,

    Count, // Last sentinel
}

public static class DiagnosticEntries
{
    private static DiagnosticEntry[] table = [];

    static DiagnosticEntries()
    {
        LoadAssembly(Assembly.GetExecutingAssembly(), "Diagnostics.DiagnosticCode.tinyhand");
    }

    public static bool TryGet(DiagnosticCode code, [MaybeNullWhen(false)] out DiagnosticEntry entry)
    {
        if (code >= DiagnosticCode.Count)
        {
            entry = default;
            return false;
        }
        else
        {
            entry = table[(int)code];
            return entry is not null;
        }
    }

    internal static void LoadAssembly(Assembly assembly, string name)
    {
        try
        {
            using Stream? stream = assembly.GetManifestResourceStream(assembly.GetName().Name + "." + name);
            if (stream == null)
            {
                throw new FileNotFoundException();
            }

            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);

            table = new DiagnosticEntry[(int)DiagnosticCode.Count + 1];
            var entries = TinyhandSerializer.DeserializeFromUtf8<DiagnosticEntry[]>(bytes);
            if (entries is not null)
            {
                foreach (var e in entries)
                {
                    if (Enum.TryParse<DiagnosticCode>(e.Name, out var code))
                    {
                        table[(int)code] = e;
                    }
                }
            }
        }
        catch
        {
        }
    }
}
