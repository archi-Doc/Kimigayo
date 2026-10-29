// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.RegularExpressions;
using Xunit;

namespace XunitTest;

/// <summary>Structural ratchets of the compiler reduction (docs/dev/COMPILER_ARCHITECTURE.md). Each limit is the count at
/// the stage that set it; a count may fall but never rise. A deleted construct counts as zero.</summary>
public class ArchitectureRulesTest
{
    // DECISIONS BU-24's closed families (SourcePatternStaysAtItsSites).
    private const string SelfRebuild = @"\.Intern\(\s*(\w+)\.Kind,\s*\1\.Symbol,\s*\1\.Semantics\b";
    private const string SelfRebuildSites = "Binding/Binding.ArrayInference.cs=1;Binding/Binding.ClosureInstances.cs=1;" +
        "Binding/Binding.LocalRegions.Types.cs=1;Binding/Binding.OriginDeclarations.cs=1;Binding/Binding.Storage.cs=1;" +
        "Binding/Binding.TypeRelations.Origins.cs=1;Binding/Binding.TypeRelations.cs=1;Binding/TyCtxt.Instances.cs=1;Binding/TyCtxt.cs=1";

    private const string RawOriginRole = @"\.RawOrigin\b|\bPointer\((?:[^()]|\([^()]*\))*,";
    private const string RawOriginRoleSites = "Binding/Binding.ClosureInstances.cs=1;Binding/Binding.ContractCalls.cs=1;Binding/Binding.Conversions.Raw.cs=7;" +
        "Binding/Binding.DiagnosticTypes.cs=1;Binding/Binding.Lints.cs=1;Binding/Binding.LocalRegions.Types.cs=2;" +
        "Binding/Binding.OriginDeclarations.cs=1;Binding/Binding.OriginInheritance.cs=4;Binding/Binding.PlaceAuthority.Raw.cs=1;" +
        "Binding/Binding.RawOrigins.Holder.cs=5;Binding/Binding.RawOrigins.Steps.cs=3;Binding/Binding.RawOrigins.cs=4;" +
        "Binding/Binding.Retention.Atoms.cs=1;Binding/Binding.Storage.cs=3;Binding/Binding.TypeOrigins.cs=3;" +
        "Binding/Binding.TypeRelations.Origins.cs=7;Binding/Binding.TypeRelations.cs=2;" +
        "Binding/BindingModel.cs=2;Binding/TyCtxt.Raw.cs=1;Binding/TyCtxt.cs=4";

    // DECISIONS R4-M136's OldRule ratchet: an OldRule test without a MirTargetSide call, from its Trait to its member's end, the line before
    // the blank line and the next member (StyleCop separates members by one) or the class's end, so an expression-bodied member ends at its
    // own end (PLAN G263; G:A5-F4's group review N4). A letter in brackets keeps the pattern from matching its source.
    private const string OldRuleWithoutTargetSide = @"\[Trai[t]\(""OldRule"", ""[^""]+""\)\](?:(?!MirTargetSide\.|\n\n    \S|\n\})[\s\S])*(?=\n\n    \S|\n\})";

    // An OldRule test's attributes in a member list, for TheOldRuleRatchetEndsEachMemberAtItsOwnEnd (escaped, so the ratchet skips it).
    private const string OldRuleTest = "    [Trait(\"OldRule\", \"X\")]\n    [Fact]\n";

    // Test-only semantic-slot ratchet; production member policy is reviewed against COMPILER_ARCHITECTURE.
    private const string SemanticSlots = "Bound(?:Type|Origin|Symbol|Call|ValueCall|Constraint|RuntimeTest|Meaning|Closure)|BindingState|BindingFailure|HasCurrentBinding|CallStorage|ClosureStorage|ConversionBinding|FoldedConstant|CreationCall|ErasedFunctionType|EntryCall|FormattingStorage|ArithmeticCall|ComparisonCall|IsDirectStorage|FillCount|SemanticsIndexed|RequirementStorage|ImplementationStorage";

    [Theory]
    [InlineData("Emission failure strings", "src/Kimi/Compiler/Emission", "*.cs", @"\bFail\(\$?""", 446)]
    [InlineData("Unsupported diagnostic codes", "src/Kimi/Diagnostics", "DiagnosticCode.tinyhand", @"Category=""Unsupported""", 1)]
    [InlineData("Advice in the diagnostic catalog", "src/Kimi/Diagnostics", "DiagnosticCode.tinyhand", @"(?m)^\s*Advice=", 0)]
    [InlineData("Test reads and writes of Koto semantic slots", "tests/xUnitTest", "*.cs", @"(?<!Compiler)\.(?:" + SemanticSlots + @")\b|[{,]\s*(?:Bound(?:Type|Origin|Symbol|Call|ValueCall|Constraint|RuntimeTest|Meaning|Closure)|CallStorage|ClosureStorage|ConversionBinding|EntryCall|Iteration|IsDirectStorage|FillCount|SemanticsIndexed)\s*:", 13)]
    [InlineData("Operator Koto subclasses", "src/Kimi/Compiler/Parsing/Koto/Expressions", "*.cs", @"\bclass\s+\w+\s*:\s*(?:Binary|Unary)Koto\b", 9)]
    [InlineData("Semantic auto-properties on Koto classes", "src/Kimi/Compiler/Parsing/Koto", "*.cs", @"\b(?:Bound[A-Z]\w*|BindingSymbol|CallPlan)\??\s+\w+\s*\{\s*get;\s*(?:(?:internal|private|protected)\s+)?set;\s*\}", 7)]

    // R5 G199b: the collection of a collectible load context or dynamic assembly flushes the runtime's process-wide caches (the cast cache
    // among them) and charges their rebuild, 98,352 bytes, to whichever test's measuring thread inserts next. A test that needs the state
    // of a fresh process runs as an Explicit test in a child process (FreshProcess). A letter in brackets keeps this row's own pattern
    // from matching its source.
    [InlineData("Test collectible assemblies and load context unloads", "tests/xUnitTest", "*.cs", @"\bAssemblyLoad[C]ontext\b|\bRunAndCollec[t]\b|\.Unload\(\)", 0)]

    // R3 U6: tests run and read analysis and generation through the Compilation entry points. The old path's structure is read only
    // where no entry point carries it, until the stage that replaces it: dump goldens (R4), the MIR emission (R5, R6). R4-M13a: the
    // control-flow structure is read only by LegacyRecords, and direct calls of the old control-flow entries cannot grow.
    [InlineData("Test calls of the old analysis and generation entry points", "tests/xUnitTest", "*.cs", @"\.Ownership\.(?:Analyze|Result|ReportDiagnostics)\b|\.Emission\.(?:WriteIr|Validate)\b|\.Emission\.TryPrepare\(out _,", 0)]
    [InlineData("Test reads of ownership issues (facts a Problem lacks)", "tests/xUnitTest", "*.cs", @"\.Ownership\.Issues\b", 16)]
    [InlineData("Test reads of ownership bodies (R4 dump goldens)", "tests/xUnitTest", "*.cs", @"\.Ownership\.Bodies\b", 159)]
    [InlineData("Test reads of control-flow structure (R4 dump goldens)", "tests/xUnitTest", "*.cs", @"\.Ownership\.ControlFlow\b", 1)]
    [InlineData("Test calls of the old control-flow entries (R6)", "tests/xUnitTest", "*.cs", @"\bAnalyzeControlFlow\(\)|\bControlFlowAnalysis\.Analyze\(", 64)]
    [InlineData("Test inspections of a prepared emission module (R5)", "tests/xUnitTest", "*.cs", @"\.Emission\.TryPrepare\(out (?!_,)", 119)]
    [InlineData("Test reads of instance analysis (R5, R6)", "tests/xUnitTest", "*.cs", @"\b(?:Failed|Failure|Analyze)Instance\b", 7)]
    [InlineData("Test reads of generation failure facts without an entry point", "tests/xUnitTest", "*.cs", @"\.Emission\.(?:FailureIsResourceLimit|ReportFailure)\b", 9)]
    // DECISIONS R4-M136 (refining R4-M50, R4-M60): an OldRule test asserts its MIR target side through MirTargetSide. Without one now: A5-F8's
    // ContentUpdateTest.OldContentAndSlotViewsCannotSurviveReplacement, .ExclusiveContentChildrenBlockEveryUpdate and
    // .OldContentConflictExplainsTheCauseInEveryAdapter and DictionaryStoredReferenceTest.CompoundStoredReferencesOutliveTheContainer,
    // .ConstructionAndConditionalStorageRetainTheInput and .LocalInputsAndSharedCopiesOutliveTheSlot (their unit 2d-8), and Binding's
    // NormalizedConstraintProofBindingTest.NormalizationDoesNotAddLogicalRulesOrEraseTypeStructure (B3.3, R4-M136's open line). Removal: 0
    // by R4's exit (the A5 step 2 plan's N8).
    [InlineData("OldRule tests without a MIR target side", "tests/xUnitTest", "*.cs", OldRuleWithoutTargetSide, 7)]
    // DECISIONS R4-M101 E8 (2a0-D D7, D8): only the C1-F4 view decodes an Origin's slot, binder or forming body, so no Mir source reads
    // them (G163); fixed-fixed entailment has one owner, so OriginPremiseClosure.AddEdge has one caller, the premise catalog's writer
    // (AddPremiseEdge, bu20's C0d).
    [InlineData("Mir reads of an Origin's slot, binder or forming body", "src/Kimi/Compiler/Mir", "*.cs", @"(\b[Oo]rigin|[Aa]tom)\w*\??\.(Slot|Binder|Body)\b|\.EvaluatingBody\b", 0)]
    [InlineData("Premise edges written outside the premise catalog's writer", "src/Kimi/Compiler", "*.cs", @"(?<!this)\.AddEdge\(", 1)]
    public void SourcePatternDoesNotGrow(string rule, string directory, string pattern, string regex, int limit)
    {
        var expression = new Regex(regex);
        var count = SourceFiles(directory, pattern).Sum(x => expression.Count(File.ReadAllText(x)));
        Assert.True(count <= limit, $"{rule}: {count} occurrences; the limit is {limit}.");
    }

    // DECISIONS R4-M136 (PLAN G263; G:A5-F4's group review N4): the OldRule ratchet's pattern ends a member at its own end. An expression-
    // bodied OldRule test before a member that calls MirTargetSide counts, where a pattern ending at the next closing brace was hidden by
    // that call; a block-bodied one before an expression-bodied one counts both; a blank line inside a body ends nothing; a test with a
    // MirTargetSide call counts none, in either form.
    [Theory]
    [InlineData("    public void A() => B();\n\n    [Fact]\n    public void C()\n    {\n        MirTargetSide.Check(x);\n    }\n}\n", 1)]
    [InlineData("    public void A()\n    {\n        B();\n    }\n\n" + OldRuleTest + "    public void C()\n        => D();\n}\n", 2)]
    [InlineData("    public void A()\n    {\n        B();\n\n        MirTargetSide.Check(x);\n    }\n}\n", 0)]
    [InlineData("    public void A() => MirTargetSide.Check(x);\n\n" + OldRuleTest + "    public void C()\n    {\n        MirTargetSide.Check(y);\n    }\n}\n", 0)]
    public void TheOldRuleRatchetEndsEachMemberAtItsOwnEnd(string members, int count)
        => Assert.Equal(count, new Regex(OldRuleWithoutTargetSide).Count(OldRuleTest + members));

    // DECISIONS BU-24: the families of the raw Origin's representation are closed by their sites, file by file. A self-rebuild
    // `Intern(x.Kind, x.Symbol, x.Semantics, …)` is the keep-rebuild (TyCtxt.Rebuild), the erasure (EraseParts), a mapping rewriter that
    // G160-1 gives its raw arm (Storage, TypeRelations, OriginDeclarations, LocalRegions.Types, TypeRelations.Origins), a closure instance's
    // context change, which passes the raw role on, or an array literal's formation, never a raw layer. The raw role (BoundType.RawOrigin,
    // a Pointer that states one) is read and written only at its named sites; review checks the Intern calls whose Semantics is a variable.
    // Mir/ has none: A5 reads a raw layer's step only through TyCtxt.RawStep, a stored layer's from its declaration (design-final §15).
    [Theory]
    [InlineData("Self-rebuilds of a Type", "src/Kimi/Compiler", SelfRebuild, SelfRebuildSites)]
    [InlineData("Reads and writes of the raw Origin role", "src/Kimi/Compiler", RawOriginRole, RawOriginRoleSites)]
    public void SourcePatternStaysAtItsSites(string rule, string directory, string regex, string sites)
    {
        var expression = new Regex(regex);
        var root = Path.Combine(Repository.Root, directory);
        var actual = SourceFiles(directory, "*.cs")
            .Select(x => (Name: Path.GetRelativePath(root, x).Replace(Path.DirectorySeparatorChar, '/'), Count: expression.Count(File.ReadAllText(x))))
            .Where(static x => x.Count != 0).Select(static x => $"{x.Name}={x.Count}").Order(StringComparer.Ordinal).ToArray();
        Assert.True(sites.Split(';').Order(StringComparer.Ordinal).SequenceEqual(actual), $"{rule}: {string.Join(";", actual)}; the sites are {sites}.");
    }

    // A directory that does not exist (deleted, or not yet created) has no files.
    private static IEnumerable<string> SourceFiles(string directory, string pattern)
    {
        var path = Path.Combine(Repository.Root, directory);
        return Directory.Exists(path) ? Directory.EnumerateFiles(path, pattern, SearchOption.AllDirectories) : [];
    }
}
