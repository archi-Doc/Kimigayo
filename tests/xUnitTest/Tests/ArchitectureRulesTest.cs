// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

/// <summary>Structural ratchets of the compiler reduction (docs/dev/COMPILER_ARCHITECTURE.md). Each limit is the count at
/// the stage that set it; a count may fall but never rise. A deleted construct counts as zero.</summary>
public class ArchitectureRulesTest
{
    [Theory]
    [InlineData("Koto-keyed dictionaries in Binding", "src/Kimi/Compiler/Binding", "*.cs", @"Dictionary<\w*Koto\b", 101)]
    [InlineData("Supports* gates", "src/Kimi/Compiler", "*.cs", @"\bbool\s+Supports\w*\s*[(<]", 15)]
    [InlineData("Emission failure strings", "src/Kimi/Compiler/Emission", "*.cs", @"\bFail\(\$?""", 446)]
    [InlineData("Unsupported diagnostic codes", "src/Kimi/Diagnostics", "DiagnosticCode.tinyhand", @"Category=""Unsupported""", 1)]
    [InlineData("Advice in the diagnostic catalog", "src/Kimi/Diagnostics", "DiagnosticCode.tinyhand", @"(?m)^\s*Advice=", 0)]
    [InlineData("Parsing calls into Binding", "src/Kimi/Compiler/Parsing", "*.cs", @"\bBinding\.\w+\(", 0)]
    [InlineData("Hover reads of Binding or Koto semantic slots", "src/Kimi/Checking/Hover", "*.cs", @"\bBinding\.|\.(?:Bound(?:Type|Origin|Symbol|Call|ValueCall|Constraint|RuntimeTest)|BindingState|BindingFailure|HasCurrentBinding)\b", 0)]
    [InlineData("Test reads and writes of Koto semantic slots", "tests/xUnitTest", "*.cs", @"(?<!Compiler)\.(?:Bound(?:Type|Origin|Symbol|Call|ValueCall|Constraint|RuntimeTest|Meaning|Closure)|BindingState|BindingFailure|HasCurrentBinding|CallStorage|ClosureStorage|ConversionBinding|FoldedConstant|CreationCall|ErasedFunctionType|EntryCall|FormattingStorage|ArithmeticCall|ComparisonCall|IsDirectStorage|FillCount|SemanticsIndexed|RequirementStorage|ImplementationStorage)\b|[{,]\s*(?:Bound(?:Type|Origin|Symbol|Call|ValueCall|Constraint|RuntimeTest|Meaning|Closure)|CallStorage|ClosureStorage|ConversionBinding|EntryCall|Iteration|IsDirectStorage|FillCount|SemanticsIndexed)\s*:", 13)]
    [InlineData("Operator Koto subclasses", "src/Kimi/Compiler/Parsing/Koto/Expressions", "*.cs", @"\bclass\s+\w+\s*:\s*(?:Binary|Unary)Koto\b", 9)]
    [InlineData("Child walks other than ForEachChildSlot", "src/Kimi/Compiler", "*.cs", @"\b(?:VisitChildrenCore|GetChildNodes|ReplaceChildCore|ReplaceInList)\b", 0)]
    public void SourcePatternDoesNotGrow(string rule, string directory, string pattern, string regex, int limit)
    {
        var expression = new Regex(regex);
        var count = Directory.EnumerateFiles(Path.Combine(Repository.Root, directory), pattern, SearchOption.AllDirectories)
            .Sum(x => expression.Count(File.ReadAllText(x)));
        Assert.True(count <= limit, $"{rule}: {count} occurrences; the limit is {limit}.");
    }

    [Theory]
    [InlineData("Kimi.Compiler.OwnershipOperationKind", 38)]
    [InlineData("Kimi.Compiler.OwnershipValueKind", 31)]
    [InlineData("Kimi.Compiler.EmissionOpcode", 49)]
    [InlineData("Kimi.Compiler.CompilerFunctionKind", 76)]
    [InlineData("Kimi.Compiler.BindingFailure", 89)]
    public void VocabularyDoesNotGrow(string type, int limit)
    {
        var members = typeof(Compilation).Assembly.GetType(type) is { } enumeration ? Enum.GetNames(enumeration).Length : 0;
        Assert.True(members <= limit, $"{type} has {members} members; the limit is {limit}.");
    }
}
