// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

/// <summary>Structural ratchets of the compiler reduction (docs/dev/COMPILER_ARCHITECTURE.md). Each limit is the count at
/// the stage that set it; a count may fall but never rise. A deleted construct counts as zero.</summary>
public class ArchitectureRulesTest
{
    // The semantic slots on Koto classes, read as members.
    private const string SemanticSlots = "Bound(?:Type|Origin|Symbol|Call|ValueCall|Constraint|RuntimeTest|Meaning|Closure)|BindingState|BindingFailure|HasCurrentBinding|CallStorage|ClosureStorage|ConversionBinding|FoldedConstant|CreationCall|ErasedFunctionType|EntryCall|FormattingStorage|ArithmeticCall|ComparisonCall|IsDirectStorage|FillCount|SemanticsIndexed|RequirementStorage|ImplementationStorage";

    // The Binding types the MIR builder may name: the published tables, plans and declaration facts.
    private static readonly string[] PublishedBindingTypes =
    [
        "AdtDef", "BindingSymbol", "BindingSymbolKind", "BoundCapture", "BoundClosure", "BoundDefaultArgument", "BoundEnumCase",
        "BoundEnumConstruction", "BoundLength", "BoundMatch", "BoundMatchArm", "BoundOrigin", "BoundPattern", "BoundPatternKind",
        "BoundType", "BoundTypeKind", "CallPlan", "CalleeKind", "FunctionResultMode", "HirTables", "KimiLibrary", "PairBinder",
        "PairCase", "PatternLiteral", "PatternLiteralKind", "PlanRow",
    ];

    private static readonly Regex LineComment = new(@"//[^\n]*");

    [Theory]
    [InlineData("Koto-keyed dictionaries in Binding", "src/Kimi/Compiler/Binding", "*.cs", @"Dictionary<\w*Koto\b", 96)]
    [InlineData("Supports* gates", "src/Kimi/Compiler", "*.cs", @"\bbool\s+Supports\w*\s*[(<]", 13)]
    [InlineData("Emission failure strings", "src/Kimi/Compiler/Emission", "*.cs", @"\bFail\(\$?""", 446)]
    [InlineData("Unsupported diagnostic codes", "src/Kimi/Diagnostics", "DiagnosticCode.tinyhand", @"Category=""Unsupported""", 1)]
    [InlineData("Advice in the diagnostic catalog", "src/Kimi/Diagnostics", "DiagnosticCode.tinyhand", @"(?m)^\s*Advice=", 0)]
    [InlineData("Parsing calls into Binding", "src/Kimi/Compiler/Parsing", "*.cs", @"\bBinding\.\w+\(", 0)]
    [InlineData("Hover reads of Binding or Koto semantic slots", "src/Kimi/Checking/Hover", "*.cs", @"\bBinding\.|\.(?:Bound(?:Type|Origin|Symbol|Call|ValueCall|Constraint|RuntimeTest)|BindingState|BindingFailure|HasCurrentBinding)\b", 0)]
    [InlineData("Test reads and writes of Koto semantic slots", "tests/xUnitTest", "*.cs", @"(?<!Compiler)\.(?:" + SemanticSlots + @")\b|[{,]\s*(?:Bound(?:Type|Origin|Symbol|Call|ValueCall|Constraint|RuntimeTest|Meaning|Closure)|CallStorage|ClosureStorage|ConversionBinding|EntryCall|Iteration|IsDirectStorage|FillCount|SemanticsIndexed)\s*:", 13)]
    [InlineData("Operator Koto subclasses", "src/Kimi/Compiler/Parsing/Koto/Expressions", "*.cs", @"\bclass\s+\w+\s*:\s*(?:Binary|Unary)Koto\b", 9)]
    [InlineData("Child walks other than ForEachChildSlot", "src/Kimi/Compiler", "*.cs", @"\b(?:VisitChildrenCore|GetChildNodes|ReplaceChildCore|ReplaceInList)\b", 0)]
    [InlineData("Semantic auto-properties on Koto classes", "src/Kimi/Compiler/Parsing/Koto", "*.cs", @"\b(?:Bound[A-Z]\w*|BindingSymbol|CallPlan)\??\s+\w+\s*\{\s*get;\s*(?:(?:internal|private|protected)\s+)?set;\s*\}", 7)]
    public void SourcePatternDoesNotGrow(string rule, string directory, string pattern, string regex, int limit)
    {
        var expression = new Regex(regex);
        var count = SourceFiles(directory, pattern).Sum(x => expression.Count(File.ReadAllText(x)));
        Assert.True(count <= limit, $"{rule}: {count} occurrences; the limit is {limit}.");
    }

    /// <summary>The MIR layer reads Binding only through what Binding publishes (docs/dev/COMPILER_ARCHITECTURE.md,
    /// dependency rules): it names no other type declared at the top level of a Binding or root compiler file (the root
    /// files are Binding's re-derivation helpers), no <c>StructuralCompletion</c>, no Koto semantic slot, no re-resolving
    /// <c>KotoHelper</c> walk and no parent.</summary>
    [Fact]
    public void MirReadsNoBindingInternals()
    {
        var declaration = new Regex(@"(?m)^(?:\w+ )*(?:class|struct|enum|interface|record)(?: struct| class)? (\w+)");
        var compiler = Path.Combine("src", "Kimi", "Compiler");
        var internals = SourceFiles(Path.Combine(compiler, "Binding"), "*.cs")
            .Concat(Directory.EnumerateFiles(Path.Combine(Repository.Root, compiler), "*.cs"))
            .SelectMany(x => declaration.Matches(LineComment.Replace(File.ReadAllText(x), string.Empty)).Select(y => y.Groups[1].Value))
            .Append("StructuralCompletion")
            .Except(PublishedBindingTypes)
            .Order(StringComparer.Ordinal);
        var forbidden = new Regex($@"\b(?:{string.Join('|', internals)})\b|\.(?:{SemanticSlots})\b|\bKotoHelper\.(?:UnwrapParentheses|ResolveTransferTarget)\b|\.Parent\b");
        var references = SourceFiles(Path.Combine(compiler, "Mir"), "*.cs")
            .SelectMany(x => forbidden.Matches(LineComment.Replace(File.ReadAllText(x), string.Empty)).Select(y => $"{Path.GetFileName(x)}: {y.Value}"))
            .ToList();
        Assert.True(references.Count == 0, $"Mir references Binding internals: {string.Join(", ", references)}.");
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

    // A directory that does not exist (deleted, or not yet created) has no files.
    private static IEnumerable<string> SourceFiles(string directory, string pattern)
    {
        var path = Path.Combine(Repository.Root, directory);
        return Directory.Exists(path) ? Directory.EnumerateFiles(path, pattern, SearchOption.AllDirectories) : [];
    }
}
