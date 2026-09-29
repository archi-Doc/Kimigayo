// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimi.Diagnostics;

namespace Kimi;

public enum DiagnosticCode
{
    Template_Kd, // First sentinel

    UnresolvedBinding_Kd,
    AmbiguousBinding_Kd,
    DuplicateBinding_Kd,
    DuplicateDictionaryKey_Kd,
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
    InvalidSelfClause_Kd,
    NotObjectPayload_Kd,
    UnprovenConstraint_Kd,
    UnsatisfiedConstraint_Kd,
    InvalidKimiLibrary_Kd,
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
    HiddenNamedAlias_Kd,
    // Reserved for required constant evaluation; runtime arithmetic uses Abort reason codes.
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
    InvalidTry_Kd,
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
    DefaultArgumentMove_Kd,
    TransferRequired_Kd,
    ExclusiveBorrowRequired_Kd,
    SharedBindingAssignment_Kd,
    ExclusiveBindingAssignment_Kd,
    SharedPathAccess_Kd,
    ExclusivePathTake_Kd,
    PlaceRequired_Kd,
    ReceiverShapeMismatch_Kd,
    BareOwningShorthand_Kd,
    NonCopyOperand_Kd,
    MissingSpecializationTarget_Kd,
    SpecializationInputMismatch_Kd,

    InvalidPattern_Kd,
    NonExhaustiveMatch_Kd,
    UnreachablePattern_Kd,
    ControlFlowWarning_Kd,
    OuterCloserInBody_Kd,

    GenerationFailed_Kd,
    GenerationResourceLimit_Kd,

    DeferredExpansionLimit_Kd,
    InternalInvariant_Kd,
    ComparisonLoanConflict_Kd,
    CallReservationConflict_Kd,
    CallActivationConflict_Kd,

    InvalidDependencyConfiguration_Kd,
    UnresolvedDependencyGraph_Kd,
    InvalidTestDefinition_Kd,
    InvalidLayoutAttribute_Kd,
    ConflictingLayout_Kd,
    InvalidLibraryImport_Kd,
    MissingNativeRequirement_Kd,
    UnsupportedImportSignature_Kd,
    ConflictingImportSignature_Kd,
    ConflictingRuntimeSymbol_Kd,
    ConflictingImportSupply_Kd,
    UnsafeFunctionValue_Kd,
    UnavailableReservedImport_Kd,
    SplitCLayoutStorage_Kd,
    InvalidCLayout_Kd,
    InvalidInlineLayout_Kd,

    UnavailableFeature_Kd,

    ProjectPreparationFailed_Kd,
    ProjectLoadFailed_Kd,
    TargetSelectionRequired_Kd,
    TestTargetUnavailable_Kd,
    DocumentDesynchronized_Kd,
    CheckFaulted_Kd,
    PositionAlwaysFails_Kd,
    IncompatibleSerializedSource_Kd,
    SourceReadFailed_Kd,

    Count, // Last sentinel
}

/// <summary>The diagnostic catalog (SPEC 23.3.6.1), validated once when it loads.</summary>
public static class DiagnosticEntries
{
    private const string ResourceName = "Diagnostics.DiagnosticCode.tinyhand";

    private static readonly DiagnosticEntry?[] Table;

    static DiagnosticEntries()
    {
        var assembly = typeof(DiagnosticEntries).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetName().Name + "." + ResourceName);
        byte[]? bytes = null;
        if (stream is not null)
        {
            bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
        }

        (Table, Anomalies) = Load(bytes);
    }

    /// <summary>Gets the anomalies found when the catalog loaded; a valid catalog has none, and any anomaly faults every check.</summary>
    public static IReadOnlyList<string> Anomalies { get; }

    public static bool TryGet(DiagnosticCode code, [MaybeNullWhen(false)] out DiagnosticEntry entry)
    {
        entry = code >= 0 && code < DiagnosticCode.Count ? Table[(int)code] : null;
        return entry is not null;
    }

    /// <summary>Loads a catalog and lists its anomalies: an unreadable resource, unknown, duplicate and missing entries, and invalid message templates.</summary>
    /// <param name="utf8">The catalog text, or <see langword="null"/> when the resource is missing.</param>
    /// <returns>The entries by code and the anomalies.</returns>
    internal static (DiagnosticEntry?[] Table, string[] Anomalies) Load(byte[]? utf8)
    {
        var table = new DiagnosticEntry?[(int)DiagnosticCode.Count];
        var anomalies = new List<string>();
        DiagnosticEntry[]? entries = null;
        if (utf8 is null)
        {
            anomalies.Add("The catalog resource is missing.");
        }
        else
        {
            try
            {
                entries = TinyhandSerializer.DeserializeFromUtf8<DiagnosticEntry[]>(utf8);
            }
            catch (Exception ex)
            {
                anomalies.Add("The catalog cannot be read: " + ex.Message);
            }
        }

        foreach (var entry in entries ?? [])
        {
            if (!Enum.TryParse<DiagnosticCode>(entry.Name, false, out var code) || code == DiagnosticCode.Count || code.ToString() != entry.Name)
            {
                anomalies.Add($"{entry.Name}: no DiagnosticCode has this name.");
                continue;
            }

            if (table[(int)code] is not null)
            {
                anomalies.Add($"{entry.Name}: the entry is duplicated.");
                continue;
            }

            if (!Enum.IsDefined(entry.Category))
            {
                anomalies.Add($"{entry.Name}: the entry has no category.");
                continue;
            }

            if (entry.Message.Length == 0)
            {
                anomalies.Add($"{entry.Name}: the entry has no message.");
                continue;
            }

            try
            {
                entry.Prepare();
            }
            catch (FormatException ex)
            {
                anomalies.Add($"{entry.Name}: the message is not a valid template ({ex.Message}).");
                continue;
            }

            if (entry.Arity > 2)
            {
                anomalies.Add($"{entry.Name}: the message takes more than two arguments.");
            }
            else if (entry.Arity == 0 && entry.Message.AsSpan().IndexOfAny('{', '}') >= 0)
            {
                anomalies.Add($"{entry.Name}: a message without arguments contains a brace.");
            }

            table[(int)code] = entry;
        }

        if (entries is not null)
        {
            for (var i = 0; i < table.Length; i++)
            {
                if (table[i] is null)
                {
                    anomalies.Add($"{(DiagnosticCode)i}: the code has no catalog entry.");
                }
            }
        }

        return (table, anomalies.ToArray());
    }
}
