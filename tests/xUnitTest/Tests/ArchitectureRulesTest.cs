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
    private const string RawOriginRoleSites = "Binding/Binding.ClosureInstances.cs=1;Binding/Binding.Conversions.Raw.cs=7;" +
        "Binding/Binding.DiagnosticTypes.cs=1;Binding/Binding.Lints.cs=1;Binding/Binding.LocalRegions.Types.cs=2;" +
        "Binding/Binding.OriginDeclarations.cs=1;Binding/Binding.OriginInheritance.cs=4;Binding/Binding.OriginSummaries.cs=2;" +
        "Binding/Binding.PlaceAuthority.Raw.cs=1;" +
        "Binding/Binding.RawOrigins.Holder.cs=4;Binding/Binding.RawOrigins.Steps.cs=3;Binding/Binding.RawOrigins.cs=4;" +
        "Binding/Binding.Retention.Atoms.cs=1;Binding/Binding.Storage.cs=3;Binding/Binding.TypeOrigins.cs=3;" +
        "Binding/Binding.TypeRelations.Origins.cs=7;Binding/Binding.TypeRelations.cs=2;" +
        "Binding/BindingModel.cs=2;Binding/TyCtxt.Raw.cs=1;Binding/TyCtxt.cs=4";

    // Test-only semantic-slot ratchet; production member policy is reviewed against COMPILER_ARCHITECTURE. R6-check H14 (D7): the names
    // of the Koto semantic slots, except Formatting, Adaptation, Mode, Iteration and BasePath, whose names live members of other types share.
    private const string SemanticSlots = "Bound(?:Type|Origin|Symbol|Call|ValueCall|Constraint|RuntimeTest|Meaning|Closure)|BindingState|" +
        "BindingFailure|HasCurrentBinding|CallStorage|ClosureStorage|ConversionBinding|FoldedConstant|CreationCall|ErasedFunctionType|" +
        "EntryCall|FormattingStorage|ArithmeticCall|ComparisonCall|IsDirectStorage|FillCount|SemanticsIndexed|RequirementStorage|" +
        "ImplementationStorage|ClosureDefinition|ClosureAsCreated|ArithmeticStorage|ComparisonStorage|ComparisonActive|CreationStorage|" +
        "AdaptationStorage";

    // R6-check H14 (D7): a read or write of a Koto semantic slot, as a member access or as a property pattern's name, nested or not (a
    // static `BoundType.X` argument is neither).
    private const string SemanticSlotReads = @"(?<!Compiler)\.(?:" + SemanticSlots + @")\b|[{,]\s*(?:" + SemanticSlots + @")(?:\.\w+)*\s*:";
    private const string ParsingSemanticSlotSites = "Koto/Declarations/FunctionKoto.cs=9;Koto/Expressions/BinaryKoto.cs=11;" +
        "Koto/Expressions/ControlFlow/ForKoto.cs=1;Koto/Expressions/FormattingKoto.cs=2;Koto/Expressions/InvocationKoto.cs=4;" +
        "Koto/Expressions/SyntheticKoto.cs=4;Koto/Expressions/UnaryKoto.cs=2;Koto/Koto.Ownership.cs=4;Koto/Koto.cs=4";

    // DECISIONS R5-M162 and R5-M163 (plan §4.9 row G10, §5.4 item 4): the old generator's components R6-gen deleted, their stems
    // included, and the old analysis's instance entry points; the R8 runtime helpers are not among them. A letter in brackets keeps the
    // pattern from matching its source.
    private const string DeletedByR6Gen =
        @"\b(?:BodyLowerin[g]|EmissionModul[e]|EmissionOpcod[e]|LlvmModuleWrite[r]|LlvmEmitte[r]|GenericStoragePla[n]|StaticScalarEntr[y]|" +
        @"(?:Object|Virtual|Default)GenerationPla[n]|CompilerFunctionAdapter[s]|WindowsLowerin[g]|(?:Failed|Failure|Analyze)Instanc[e]\b)";

    // DECISIONS R5-M189 (R6-check H10b, D8): the old ownership analysis H10b deleted, as its files' top-level types, its transitional
    // entry and its limit code. A letter in brackets keeps the pattern from matching its source.
    private const string DeletedByH10b =
        @"\b(?:Ownershi[p](?:Analysis|Body|Call|CallInput|CallReservation|CleanupPlan|CleanupStep|ComparisonLoan|ConstructionPlan|" +
        @"DeferredPlan|Edge|EdgeKind|ElementUpdate|Flow|Identity|Issue|Lending|MatchArmPlan|MatchPlan|Operation|OperationKind|PhiInput|" +
        @"Place|PlaceKind|Projection|Result|Sequence|Storage|StorageLimitException|Transfers|Value|ValueKind)|AcquisitionKin[d]|" +
        @"BorrowLivenes[s]|CallResultSourc[e]|Cleanu[p](?:Action|Reason)|InterpretationContex[t]|MovePat[h]|PackedAnalysisTabl[e]|" +
        @"Plac[e](?:State|UseKind)|PlacementKin[d]|SequenceOperatio[n]|AnalyzeLegacyOwnershi[p]|DeferredExpansionLimit_K[d])\b";

    // DECISIONS R5-M189 (R6-check H10b-2, H10b D8; H8b D9): the old control-flow analysis H10b-2 deleted, as its file's top-level types,
    // its Compilation entry and Binding's lint Error count its last stopped test read. A letter in brackets keeps the pattern from
    // matching its source.
    private const string DeletedByH10b2 = @"\b(?:ControlFlowAnalysi[s]|ControlFlowIssu[e]|ControlFlowNodeInf[o]|AnalyzeControlFlo[w]|LintError[s])\b";

    [Theory]
    [InlineData("Emission failure strings", "src/Kimi/Compiler/Emission", "*.cs", @"\bFail\(\$?""", 0)]
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

    // Zero-reference rows (DECISIONS R5-M162's Q3): they replace the test-read rows of the deleted emitter's members, which no
    // reference to its type admits, and of the instance entry points; comments count, so no deleted component is named as current.
    [InlineData("Compiler references to R6-gen's deleted components", "src/Kimi", "*.cs", DeletedByR6Gen, 0)]
    [InlineData("Benchmark references to R6-gen's deleted components", "src/Benchmark", "*.cs", DeletedByR6Gen, 0)]
    [InlineData("Playground references to R6-gen's deleted components", "src/Playground", "*.cs", DeletedByR6Gen, 0)]
    [InlineData("Test references to R6-gen's deleted components", "tests", "*.cs", DeletedByR6Gen, 0)]
    // R6-check H10b (D8): the same for the deleted old ownership analysis, replacing the test-read rows of its transitional entry; the
    // compiler's row is H11a's (D8), landing when the last Binding comment naming it goes.
    [InlineData("Compiler references to the old ownership analysis", "src/Kimi", "*.cs", DeletedByH10b, 0)]
    [InlineData("Test references to the old ownership analysis", "tests", "*.cs", DeletedByH10b, 0)]
    [InlineData("Benchmark references to the old ownership analysis", "src/Benchmark", "*.cs", DeletedByH10b, 0)]
    [InlineData("Playground references to the old ownership analysis", "src/Playground", "*.cs", DeletedByH10b, 0)]
    // R6-check H10b-2 (H10b D8): the same for the deleted old control-flow analysis, replacing the test-call row of its entries
    // (R4-M13a), which PLAN G428's fix brought to 0.
    [InlineData("Compiler references to the old control-flow analysis", "src/Kimi", "*.cs", DeletedByH10b2, 0)]
    [InlineData("Test references to the old control-flow analysis", "tests", "*.cs", DeletedByH10b2, 0)]
    [InlineData("Benchmark references to the old control-flow analysis", "src/Benchmark", "*.cs", DeletedByH10b2, 0)]
    [InlineData("Playground references to the old control-flow analysis", "src/Playground", "*.cs", DeletedByH10b2, 0)]
    // R6-check H14 (D7; DECISIONS R5-M9): the reads of the Koto semantic slots outside Binding end, area by area; comments count.
    [InlineData("Mir reads of Koto semantic slots", "src/Kimi/Compiler/Mir", "*.cs", SemanticSlotReads, 0)]
    [InlineData("Backend reads of Koto semantic slots", "src/Kimi/Compiler/Backend", "*.cs", SemanticSlotReads, 0)]
    // The root helpers, each by its file (SourceFiles recurses), and the other areas outside Binding.
    [InlineData("ComparisonTypes reads of Koto semantic slots", "src/Kimi/Compiler", "ComparisonTypes.cs", SemanticSlotReads, 0)]
    [InlineData("DefaultParameters reads of Koto semantic slots", "src/Kimi/Compiler", "DefaultParameters.cs", SemanticSlotReads, 0)]
    [InlineData("ElementAccess reads of Koto semantic slots", "src/Kimi/Compiler", "ElementAccess.cs", SemanticSlotReads, 0)]
    [InlineData("FormattingTypes reads of Koto semantic slots", "src/Kimi/Compiler", "FormattingTypes.cs", SemanticSlotReads, 0)]
    [InlineData("ReferenceTypes reads of Koto semantic slots", "src/Kimi/Compiler", "ReferenceTypes.cs", SemanticSlotReads, 0)]
    [InlineData("ScalarTypes reads of Koto semantic slots", "src/Kimi/Compiler", "ScalarTypes.cs", SemanticSlotReads, 0)]
    [InlineData("SharedReadTypes reads of Koto semantic slots", "src/Kimi/Compiler", "SharedReadTypes.cs", SemanticSlotReads, 0)]
    [InlineData("SlotTypes reads of Koto semantic slots", "src/Kimi/Compiler", "SlotTypes.cs", SemanticSlotReads, 0)]
    [InlineData("StrictBestCandidate reads of Koto semantic slots", "src/Kimi/Compiler", "StrictBestCandidate.cs", SemanticSlotReads, 0)]
    [InlineData("Documentation reads of Koto semantic slots", "src/Kimi/Compiler/Documentation", "*.cs", SemanticSlotReads, 0)]
    [InlineData("Testing reads of Koto semantic slots", "src/Kimi/Testing", "*.cs", SemanticSlotReads, 0)]
    [InlineData("Benchmark reads of Koto semantic slots", "src/Benchmark", "*.cs", SemanticSlotReads, 0)]
    // DECISIONS R4-M101 E8 (2a0-D D7, D8): only the C1-F4 view decodes an Origin's slot, binder or forming body, so no Mir source reads
    // them (G163); fixed-fixed entailment has one owner, so OriginPremiseClosure.AddEdge has one caller, the premise catalog's writer
    // (AddPremiseEdge, bu20's C0d).
    [InlineData("Mir reads of an Origin's slot, binder or forming body", "src/Kimi/Compiler/Mir", "*.cs", @"(\b[Oo]rigin|[Aa]tom)\w*\??\.(Slot|Binder|Body)\b|\.EvaluatingBody\b", 0)]
    [InlineData("Premise edges written outside the premise catalog's writer", "src/Kimi/Compiler", "*.cs", @"(?<!this)\.AddEdge\(", 1)]
    // DECISIONS BU-105 (R7-D1): BindingSymbol.Resolving is the variable binding's own Running flag until R7-D2 d4 makes the variable
    // binding a declaration query; the header's use of it went to Binding.DeclQuery. A property pattern reads it too.
    [InlineData("Binding reads and writes of BindingSymbol.Resolving (0 at R7-D2 d4)", "src/Kimi/Compiler/Binding", "*.cs", @"\.Resolving\b|\bResolving\s*:", 5)]
    public void SourcePatternDoesNotGrow(string rule, string directory, string pattern, string regex, int limit)
    {
        var expression = new Regex(regex);
        var count = SourceFiles(directory, pattern).Sum(x => expression.Count(File.ReadAllText(x)));
        Assert.True(count <= limit, $"{rule}: {count} occurrences; the limit is {limit}.");
    }

    // DECISIONS BU-24: the families of the raw Origin's representation are closed by their sites, file by file. A self-rebuild
    // `Intern(x.Kind, x.Symbol, x.Semantics, …)` is the keep-rebuild (TyCtxt.Rebuild), the erasure (EraseParts), a mapping rewriter that
    // G160-1 gives its raw arm (Storage, TypeRelations, OriginDeclarations, LocalRegions.Types, TypeRelations.Origins), a closure instance's
    // context change, which passes the raw role on, or an array literal's formation, never a raw layer. The raw role (BoundType.RawOrigin,
    // a Pointer that states one) is read and written only at its named sites; review checks the Intern calls whose Semantics is a variable.
    // Mir/ has none: A5 reads a raw layer's step only through TyCtxt.RawStep, a stored layer's from its declaration (design-final §15).
    [Theory]
    [InlineData("Self-rebuilds of a Type", "src/Kimi/Compiler", SelfRebuild, SelfRebuildSites)]
    [InlineData("Reads and writes of the raw Origin role", "src/Kimi/Compiler", RawOriginRole, RawOriginRoleSites)]
    // R6-check H14 (D7): Parsing reads the Koto semantic slots only in their declarations' bodies and on their write paths (a replaced
    // child's reset, the views and synthetic Kotos Binding builds), which R7 removes with the slots (DECISIONS R5-M9).
    [InlineData("Parsing reads of Koto semantic slots", "src/Kimi/Compiler/Parsing", SemanticSlotReads, ParsingSemanticSlotSites)]
    // DECISIONS BU-105: a declaration query's state has one writer, Binding.DeclQuery's transitions, and is read outside the component
    // only at its two interims: BindReference's cycle record (Names, until R7-D2 d2) and BindVariableCore's guard (Variables, until d4).
    [InlineData("Writes of a declaration query's state", "src/Kimi/Compiler", @"\.SetQueryState\(", "Binding/Binding.DeclQuery.cs=3")]
    [InlineData("Reads of a declaration query's state", "src/Kimi/Compiler", @"\.QueryState\(", "Binding/Binding.DeclQuery.cs=4;Binding/Binding.Names.cs=1;Binding/Binding.Variables.cs=1")]
    // AllocationMeasurement's contract: every allocation window of the tests goes through Measure, MeasureOnce for one run that cannot
    // repeat, or MeasureHops for a compile-stack hop (DECISIONS R4-M178), which retire the thread's allocation context first; a window
    // that reads the thread's counter itself can be charged a runtime pause's unused context tail (up to ~8 KB) under load. Benchmarks
    // (src/Benchmark) are not tests. A letter in brackets keeps the pattern from matching its source.
    [InlineData("Allocation windows outside AllocationMeasurement", "tests", @"\bGC\.GetAllocatedBytesForCurrentThrea[d]\b", "xUnitTest/AllocationMeasurement.cs=7")]
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
