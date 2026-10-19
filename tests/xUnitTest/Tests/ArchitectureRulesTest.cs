// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Xunit;

namespace XunitTest;

/// <summary>Structural ratchets of the compiler reduction (docs/dev/COMPILER_ARCHITECTURE.md). Each limit is the count at
/// the stage that set it; a count may fall but never rise. A deleted construct counts as zero.</summary>
public class ArchitectureRulesTest
{
    // Test-only semantic-slot ratchet; production member policy is reviewed against COMPILER_ARCHITECTURE.
    private const string SemanticSlots = "Bound(?:Type|Origin|Symbol|Call|ValueCall|Constraint|RuntimeTest|Meaning|Closure)|BindingState|BindingFailure|HasCurrentBinding|CallStorage|ClosureStorage|ConversionBinding|FoldedConstant|CreationCall|ErasedFunctionType|EntryCall|FormattingStorage|ArithmeticCall|ComparisonCall|IsDirectStorage|FillCount|SemanticsIndexed|RequirementStorage|ImplementationStorage";

    [Theory]
    [InlineData("Emission failure strings", "src/Kimi/Compiler/Emission", "*.cs", @"\bFail\(\$?""", 446)]
    [InlineData("Unsupported diagnostic codes", "src/Kimi/Diagnostics", "DiagnosticCode.tinyhand", @"Category=""Unsupported""", 1)]
    [InlineData("Advice in the diagnostic catalog", "src/Kimi/Diagnostics", "DiagnosticCode.tinyhand", @"(?m)^\s*Advice=", 0)]
    [InlineData("Test reads and writes of Koto semantic slots", "tests/xUnitTest", "*.cs", @"(?<!Compiler)\.(?:" + SemanticSlots + @")\b|[{,]\s*(?:Bound(?:Type|Origin|Symbol|Call|ValueCall|Constraint|RuntimeTest|Meaning|Closure)|CallStorage|ClosureStorage|ConversionBinding|EntryCall|Iteration|IsDirectStorage|FillCount|SemanticsIndexed)\s*:", 13)]
    [InlineData("Operator Koto subclasses", "src/Kimi/Compiler/Parsing/Koto/Expressions", "*.cs", @"\bclass\s+\w+\s*:\s*(?:Binary|Unary)Koto\b", 9)]
    [InlineData("Semantic auto-properties on Koto classes", "src/Kimi/Compiler/Parsing/Koto", "*.cs", @"\b(?:Bound[A-Z]\w*|BindingSymbol|CallPlan)\??\s+\w+\s*\{\s*get;\s*(?:(?:internal|private|protected)\s+)?set;\s*\}", 7)]

    // R3 U6: tests run and read analysis and generation through the Compilation entry points. The old path's structure is read only
    // where no entry point carries it, until the stage that replaces it: dump goldens (R4), the MIR emission (R5, R6). R4-M13a: the
    // control-flow structure is read only by LegacyRecords, and direct calls of the old control-flow entries cannot grow.
    [InlineData("Test calls of the old analysis and generation entry points", "tests/xUnitTest", "*.cs", @"\.Ownership\.(?:Analyze|Result|ReportDiagnostics)\b|\.Emission\.(?:WriteIr|Validate)\b|\.Emission\.TryPrepare\(out _,", 0)]
    [InlineData("Test reads of ownership issues (facts a Problem lacks)", "tests/xUnitTest", "*.cs", @"\.Ownership\.Issues\b", 16)]
    [InlineData("Test reads of ownership bodies (R4 dump goldens)", "tests/xUnitTest", "*.cs", @"\.Ownership\.Bodies\b", 168)]
    [InlineData("Test reads of control-flow structure (R4 dump goldens)", "tests/xUnitTest", "*.cs", @"\.Ownership\.ControlFlow\b", 1)]
    [InlineData("Test calls of the old control-flow entries (R6)", "tests/xUnitTest", "*.cs", @"\bAnalyzeControlFlow\(\)|\bControlFlowAnalysis\.Analyze\(", 64)]
    [InlineData("Test inspections of a prepared emission module (R5)", "tests/xUnitTest", "*.cs", @"\.Emission\.TryPrepare\(out (?!_,)", 119)]
    [InlineData("Test reads of instance analysis (R5, R6)", "tests/xUnitTest", "*.cs", @"\b(?:Failed|Failure|Analyze)Instance\b", 11)]
    [InlineData("Test reads of generation failure facts without an entry point", "tests/xUnitTest", "*.cs", @"\.Emission\.(?:FailureIsResourceLimit|ReportFailure)\b", 21)]
    public void SourcePatternDoesNotGrow(string rule, string directory, string pattern, string regex, int limit)
    {
        var expression = new Regex(regex);
        var count = SourceFiles(directory, pattern).Sum(x => expression.Count(File.ReadAllText(x)));
        Assert.True(count <= limit, $"{rule}: {count} occurrences; the limit is {limit}.");
    }

    // A directory that does not exist (deleted, or not yet created) has no files.
    private static IEnumerable<string> SourceFiles(string directory, string pattern)
    {
        var path = Path.Combine(Repository.Root, directory);
        return Directory.Exists(path) ? Directory.EnumerateFiles(path, pattern, SearchOption.AllDirectories) : [];
    }
}
