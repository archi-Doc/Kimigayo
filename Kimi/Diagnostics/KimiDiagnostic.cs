// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Kimi.Diagnostics;

namespace Kimi;

public enum KimiDiagnostic
{
    Template_Kd, // First sentinel

    ConditionMustBeBool_Kd,
    DivisionByZero_Kd,
    DuplicateModifier_Kd,
    IdentifierExpected_Kd,
    IncompleteEscape_Kd,
    IncompleteSyntax_Kd,
    IndentationLevelMismatch_Kd,
    InvalidAttributeKoto_Kd,
    InvalidCharacter_Kd,
    InvalidCharacterAtEndOfFile_Kd,
    InvalidIdentifier_Kd,
    InvalidIfAttributeArgumentCount_Kd,
    InvalidIndentation_Kd,
    InvalidNumericLiteral_Kd,
    InvalidReferenceSyntax_Kd,
    InvalidUnicodeEscape_Kd,
    InvalidUnicodeScalar_Kd,
    IntegerOverflow_Kd,
    MissingBlockCommentEnd_Kd,
    MissingComma_Kd,
    MissingExpectedToken_Kd,
    MissingStringLiteralEnd_Kd,
    MultipleAccessibilityModifiers_Kd,
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
    UnsupportedIfAttributeConditionType_Kd,
    UnsupportedEscape_Kd,

    Count, // Last sentinel
}

public static class DiagnosticEntries
{
    private static DiagnosticEntry[] table = [];

    static DiagnosticEntries()
    {
        LoadAssembly(Assembly.GetExecutingAssembly(), "Diagnostics.KimiDiagnosticEntries.tinyhand");
    }

    public static bool TryGet(KimiDiagnostic kimiDiagnostic, [MaybeNullWhen(false)] out DiagnosticEntry entry)
    {
        if (kimiDiagnostic >= KimiDiagnostic.Count)
        {
            entry = default;
            return false;
        }
        else
        {
            entry = table[(int)kimiDiagnostic];
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

            table = new DiagnosticEntry[(int)KimiDiagnostic.Count + 1];
            var entries = TinyhandSerializer.DeserializeFromUtf8<DiagnosticEntry[]>(bytes);
            if (entries is not null)
            {
                foreach (var e in entries)
                {
                    if (Enum.TryParse<KimiDiagnostic>(e.Name, out var kimiDiagnostic))
                    {
                        table[(int)kimiDiagnostic] = e;
                    }
                }
            }
        }
        catch
        {
        }
    }
}
