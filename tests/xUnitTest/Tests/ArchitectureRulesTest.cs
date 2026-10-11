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

    // The Binding types the MIR builder may name: the published tables, plans and declaration facts. Frozen at the R2b exit; R3 U1 adds
    // the builder's gaps (BuiltinForm, the key of the builder-only intrinsic rows, and CompilerFunctionKind, the intrinsic table's own key),
    // R3 U13 TestDefinition (the syntax-level #Test marker that keys a test's body); only the declaration views deferred to their first
    // reader (ImplTable, VTableDef, UniversalRelations, EffectBoundTable, AvailableBounds; R4/R5) join it.
    private static readonly string[] PublishedBindingTypes =
    [
        "AdtDef", "BindingSymbol", "BindingSymbolKind", "BoundCapture", "BoundClosure", "BoundDefaultArgument", "BoundEnumCase",
        "BoundEnumConstruction", "BoundLength", "BoundMatch", "BoundMatchArm", "BoundOrigin", "BoundPattern", "BoundPatternKind",
        "BoundType", "BoundTypeKind", "BuiltinForm", "CallPlan", "CalleeKind", "CompilerFunctionKind", "ConversionBinding", "DispositionKind", "ExpansionKind", "ExpansionPlan", "FunctionResultMode", "HirAcquire",
        "HirAdaptation", "HirAdjust", "HirCompletion", "HirOp", "HirPlace", "HirPlaceKind", "HirRes", "HirTables", "IntrinsicDisposition", "IntrinsicId", "KimiLibrary",
        "PairBinder", "PairCase", "PatternLiteral", "PatternLiteralKind", "PlanRow", "Subst", "TestDefinition", "TyCtxt",
    ];

    private static readonly Regex LineComment = new(@"//[^\n]*");

    private static readonly Regex TopLevelDeclaration = new(@"(?m)^(?:\w+ )*(?:class|struct|enum|interface|record)(?: struct| class)? (\w+)");

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

    // R3 U6: tests run and read analysis and generation through the Compilation entry points. The old path's structure is read only
    // where no entry point carries it, until the stage that replaces it: dump goldens (R4), the MIR emission (R5, R6).
    [InlineData("Test calls of the old analysis and generation entry points", "tests/xUnitTest", "*.cs", @"\.Ownership\.(?:Analyze|Result|ReportDiagnostics)\b|\.Emission\.(?:WriteIr|Validate)\b|\.Emission\.TryPrepare\(out _,", 0)]
    [InlineData("Test reads of ownership issues (facts a Problem lacks)", "tests/xUnitTest", "*.cs", @"\.Ownership\.Issues\b", 18)]
    [InlineData("Test reads of ownership bodies (R4 dump goldens)", "tests/xUnitTest", "*.cs", @"\.Ownership\.Bodies\b", 258)]
    [InlineData("Test reads of control-flow structure (R4 dump goldens)", "tests/xUnitTest", "*.cs", @"\.Ownership\.ControlFlow\b", 38)]
    [InlineData("Test inspections of a prepared emission module (R5)", "tests/xUnitTest", "*.cs", @"\.Emission\.TryPrepare\(out (?!_,)", 119)]
    [InlineData("Test reads of instance analysis (R5, R6)", "tests/xUnitTest", "*.cs", @"\b(?:Failed|Failure|Analyze)Instance\b", 11)]
    [InlineData("Test reads of generation failure facts without an entry point", "tests/xUnitTest", "*.cs", @"\.Emission\.(?:FailureIsResourceLimit|ReportFailure)\b", 21)]
    public void SourcePatternDoesNotGrow(string rule, string directory, string pattern, string regex, int limit)
    {
        var expression = new Regex(regex);
        var count = SourceFiles(directory, pattern).Sum(x => expression.Count(File.ReadAllText(x)));
        Assert.True(count <= limit, $"{rule}: {count} occurrences; the limit is {limit}.");
    }

    /// <summary>The MIR layer reads Binding only through what Binding publishes (docs/dev/COMPILER_ARCHITECTURE.md,
    /// dependency rules): it names no other type declared at the top level of a Binding or root compiler file (the root
    /// files are Binding's re-derivation helpers) or of the old Analysis and Emission paths (deleted in R6), unless Mir
    /// declares that name itself, no <c>StructuralCompletion</c>, no Koto semantic slot, no re-resolving <c>KotoHelper</c>
    /// walk and no parent.</summary>
    [Fact]
    public void MirReadsNoBindingInternals()
    {
        var compiler = Path.Combine("src", "Kimi", "Compiler");
        var internals = SourceFiles(Path.Combine(compiler, "Binding"), "*.cs")
            .Concat(Directory.EnumerateFiles(Path.Combine(Repository.Root, compiler), "*.cs"))
            .Concat(SourceFiles(Path.Combine(compiler, "Analysis"), "*.cs"))
            .Concat(SourceFiles(Path.Combine(compiler, "Emission"), "*.cs"))
            .SelectMany(DeclaredTypes)
            .Append("StructuralCompletion")
            .Except(PublishedBindingTypes)
            .Except(SourceFiles(Path.Combine(compiler, "Mir"), "*.cs").SelectMany(DeclaredTypes))
            .Order(StringComparer.Ordinal);
        var forbidden = new Regex($@"\b(?:{string.Join('|', internals)})\b|\.(?:{SemanticSlots})\b|\bKotoHelper\.(?:UnwrapParentheses|ResolveTransferTarget)\b|\.Parent\b");
        var references = MirReferences(forbidden, x => true);
        Assert.True(references.Count == 0, $"Mir references Binding internals: {string.Join(", ", references)}.");
    }

    /// <summary>Outside the MIR builder (<c>Mir/Build</c>), the MIR analyses and the MIR model read MIR, declaration tables
    /// and <c>TyCtxt</c> only (dependency rules; plan section 14-2): no syntax (a Koto class, <c>KotoKind</c>, the Parsing
    /// namespace but for <c>SemanticsKind</c>, a Type vocabulary that lives there and is named through an alias, a symbol's
    /// <c>Declaration</c>) and no HIR (the HIR columns and vocabularies, plan rows, <c>CallPlan</c>, <c>ExpansionPlan</c>,
    /// <c>BoundMatch</c>, <c>BoundClosure</c>, <c>DefaultDecl</c>).</summary>
    [Fact]
    public void OnlyTheMirBuilderReadsSyntaxAndHir()
    {
        var forbidden = new Regex(@"\b\w*Koto(?:Kind)?\b|\bKimi\.Compiler\.Parsing\b(?!\.SemanticsKind;)|\b(?:Hir\w+|PlanRow|CallPlan|ExpansionPlan|BoundMatch|BoundClosure|DefaultDecl)\b|\.Declaration\b");
        var build = Path.Combine(Repository.Root, "src", "Kimi", "Compiler", "Mir", "Build") + Path.DirectorySeparatorChar;
        var references = MirReferences(forbidden, x => !x.StartsWith(build, StringComparison.Ordinal));
        Assert.True(references.Count == 0, $"Mir outside its builder reads syntax or HIR: {string.Join(", ", references)}.");
    }

    [Theory]
    [InlineData("Kimi.Compiler.OwnershipOperationKind", 38)]
    [InlineData("Kimi.Compiler.OwnershipValueKind", 31)]
    [InlineData("Kimi.Compiler.EmissionOpcode", 49)]
    [InlineData("Kimi.Compiler.CompilerFunctionKind", 76)]
    [InlineData("Kimi.Compiler.BindingFailure", 89)]
    [InlineData("Kimi.Compiler.HirOp", 40)]
    [InlineData("Kimi.Compiler.HirRes", 9)]
    [InlineData("Kimi.Compiler.HirAcquire", 7)]
    [InlineData("Kimi.Compiler.HirAdaptation", 9)]
    [InlineData("Kimi.Compiler.HirPlaceKind", 11)]
    [InlineData("Kimi.Compiler.HirCompletion", 3)]
    [InlineData("Kimi.Compiler.ExpansionKind", 7)]
    [InlineData("Kimi.Compiler.IntrinsicId", 31)] // 30 + the temporary Capacity (owner approval 2026-10-11); R8 removes it and the limit returns to 30.
    [InlineData("Kimi.Compiler.Mir.MirStatementKind", 5)]
    [InlineData("Kimi.Compiler.Mir.MirRvalueKind", 9)]
    [InlineData("Kimi.Compiler.Mir.MirTerminatorKind", 6)]
    [InlineData("Kimi.Compiler.Mir.MirProjectionKind", 7)]
    [InlineData("Kimi.Compiler.Mir.MirOperandKind", 4)]
    [InlineData("Kimi.Compiler.Mir.MirCalleeKind", 5)]
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

    // The names of the types a C# file declares at the top level (line comments ignored).
    private static IEnumerable<string> DeclaredTypes(string file) =>
        TopLevelDeclaration.Matches(LineComment.Replace(File.ReadAllText(file), string.Empty)).Select(x => x.Groups[1].Value);

    // The matches of a forbidden pattern in the Mir sources a filter selects (line comments ignored), as "file: match".
    private static List<string> MirReferences(Regex forbidden, Func<string, bool> filter) =>
        SourceFiles(Path.Combine("src", "Kimi", "Compiler", "Mir"), "*.cs")
            .Where(filter)
            .SelectMany(x => forbidden.Matches(LineComment.Replace(File.ReadAllText(x), string.Empty)).Select(y => $"{Path.GetFileName(x)}: {y.Value}"))
            .ToList();
}
