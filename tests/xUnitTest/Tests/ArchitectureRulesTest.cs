// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using Kimi.Compiler.Lexing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
        "Binding/Binding.Storage.cs=3;Binding/Binding.TypeOrigins.cs=3;" +
        "Binding/Binding.TypeRelations.Origins.cs=7;Binding/Binding.TypeRelations.cs=2;" +
        "Binding/BindingModel.cs=2;Binding/TyCtxt.Raw.cs=1;Binding/TyCtxt.cs=4";

    // The R7-R9 plan's unit M2 (ComponentIsReadOnlyByItsReaders): Build's readers, Project and the commands, the hosts that call it, and
    // DependencyLock (ArtifactFiles.Replace only).
    private const string BuildReaders = "SolutionAndProject/DependencyLock.cs;SolutionAndProject/Project.cs;Unit/Command/BuildCommand.cs;" +
        "Unit/Command/CommandExecution.cs;Unit/Command/RunCommand.cs;Unit/Command/TestCommand.cs;Unit/Command/ToolchainCommand.cs";

    // The R7-R9 plan's unit M1 (ComponentIsReadOnlyByItsReaders): Documentation's readers, hover's projection in check and its rendering in
    // the LSP. The compiler reads only the association, Core's DocumentationSource, which Documentation reads in turn.
    private const string DocumentationReaders = "Checking/Hover/HoverBuilder.cs;Checking/HoverFacts.cs;Checking/HoverPlacement.cs;Lsp/HoverRenderer.cs";

    // R7-N1e2-1R (LookupEntriesStayAtTheirSites): the evaluation entries and their sites, the use entries and theirs (the census).
    private const string NameEvaluations = "ResolvePath|ResolveName|ResolveMember|ResolveRootPath|ResolveAdaptationTarget|ProbePath|PublishPath";

    private const string NameEvaluationSites = "Binding.BaseLayers.cs=3;Binding.Calls.cs=1;Binding.Qualification.cs=2;Binding.ResolvePath.cs=17;" +
        "Binding.Types.cs=2";

    private const string UseLookups = "NamePath|TypeName|LookupAdaptationTarget";

    private const string UseLookupSites = "Binding.Aliases.cs=1;Binding.AssociatedTypes.cs=3;" +
        "Binding.Constraints.Requirements.cs=1;Binding.Constraints.cs=2;Binding.ContainerReferences.cs=1;Binding.Contracts.cs=1;" +
        "Binding.Conversions.cs=1;Binding.Diagnostics.cs=2;Binding.EffectDeclarations.cs=1;" +
        "Binding.Lengths.cs=1;Binding.MemberLookup.cs=2;Binding.OriginDeclarations.cs=1;Binding.Patterns.cs=1;Binding.ResolvePath.cs=2;" +
        "Binding.Structs.cs=1;Binding.Types.cs=3";

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

    // R8-S1a (RP6): the paths the library witness table replaced, one rule and one witness kind for a built-in Type's library conformance.
    private const string ReplacedByWitnessTable = @"\w*FixedArrayEntry\b|" +
        @"\b(?:FixedArrayWitness|RentFixedArrayArguments|ReturnFixedArrayArguments|FixedArraySlots)\b|" +
        @"\b(?:Is|Instantiate)IntegerPosition\b|\bIntegerPosition(?:Witness|Slot)\b|(?:Kind|TyCtxt|this)\.IntegerPosition\b|\bComparisonTypes\.IsBuiltin\b";

    // PLAN G609 (InterfaceEnumerationsStayAtTheirSites): the compiler's cold interface enumerations, by top-level type and method.
    private const string InterfaceEnumerationSites = "DeclarationContainerKoto.TryParsePropertyOrFunction=1;DeclarationContainerKoto.WriteAsBlockItem=2;" +
        "FunctionKoto.WriteDeclarationTo=2;Koto.CopyDocumentation=1;Kotonoha.AddSources=2;Kotonoha.CheckNames=1;Kotonoha.ReplaceDocumentation=1;" +
        "MirDump.Write=2;Parser.ParseRequirementBody=1;Parser.ValidateReceiverParameters=1";

    // The IL opcodes by value (InterfaceEnumerationsStayAtTheirSites).
    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(static x => (OpCode)x.GetValue(null)!).ToDictionary(static x => x.Value);

    [Theory]
    [InlineData("Emission failure strings", "src/Kimi/Build", "*.cs", @"\bFail\(\$?""", 0)]
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
    // R8-S1a (RP6): no second statement of a table row; the test class FixedArrayEntryTest keeps its own name.
    [InlineData("Compiler references to the paths the library witness table replaced", "src/Kimi", "*.cs", ReplacedByWitnessTable, 0)]
    // R7-I2 (RP6): a slot frame is private to InferCtxt (the type system admits no unlogged write); SlotSolution's trial copies of
    // its frame fall to 0 with part c.
    [InlineData("SlotSolution's trial copies of a frame", "src/Kimi/Compiler/Binding", "Binding.ArgumentKinds*.cs", @"\btypeScratch\.Rent\(", 1)]
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
    [InlineData("SlotTypes reads of Koto semantic slots", "src/Kimi/Compiler", "SlotTypes.cs", SemanticSlotReads, 0)]
    [InlineData("StrictBestCandidate reads of Koto semantic slots", "src/Kimi/Compiler", "StrictBestCandidate.cs", SemanticSlotReads, 0)]
    [InlineData("Documentation reads of Koto semantic slots", "src/Kimi/Documentation", "*.cs", SemanticSlotReads, 0)]
    [InlineData("Core reads of Koto semantic slots", "src/Kimi/Compiler/Core", "*.cs", SemanticSlotReads, 0)]
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
    // DECISIONS BU-111 (amended by G478): a Binding failure fact records typed parts, and its explanation text is formed only by the
    // diagnostics adapter. The one left is UnboundSlotFact.Note (PLAN's check-formed prose row).
    [InlineData("String explanations in Binding failure facts", "src/Kimi/Compiler/Binding", "*.cs", @"\bstring (?:Condition|Reason|Note)\b(?=[,)])", 1)]
    // R7-E1b: a problem's fact keeps a Type or a closed row, and a check writes no text into one: the adapter owns each row's.
    [InlineData("Text a check keeps as a problem's fact", "src/Kimi/Compiler/Binding", "*.cs", @"\bKeep\((?:object\b|\$?"")|\bRecordMismatch\([^;\n]*\$?""", 0)]
    // R7-N1a (RP6): ResolvePath is the one evaluation of a Type name and PublishPath its one writer, so these entries stay gone.
    [InlineData("Lookup entries replaced by ResolvePath", "src/Kimi/Compiler/Binding", "*.cs", @"\b(?:RootTypeName|LookupImportedType|LookupImportedValue|StopInheritedLookup|SelectTypeCandidate)\b", 0)]
    // R7-N2d (RP6): one reading of a clause as a declaration and one walk of those readings; the guards and the copies stay gone.
    [InlineData("Base-walk guards", "src/Kimi/Compiler/Binding", "*.cs", @"\bmemberLookupVisiting\b|\bqualificationVisiting\b", 0)]
    [InlineData("A clause read as a declaration", "src/Kimi/Compiler/Binding", "*.cs", @"BoundType\?\.Symbol \?\? this\.TypeName\(", 0)]
    // G416 Q2: HoldStep absorbed RawStep, so one vocabulary names a layer's step (RP6).
    [InlineData("The RawStep vocabulary", "src/Kimi/Compiler", "*.cs", @"\bRawStep\b", 0)]
    // G416 U1: a value's parts are Holdings' entries, read by every fit and fold; the private walks stay gone (RP6).
    [InlineData("Private walks of what a value holds", "src/Kimi/Compiler/Binding", "*.cs", @"\b(?:HeldParts|BorrowShaped|BorrowTarget|PartPlace|HeldPart|Stores)\b(?=\()|\bPartPlace\b", 0)]
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
    // Mir/ has none: A5 reads a raw layer's step only through TyCtxt.StoredStep, a stored layer's from its declaration (design-final §15),
    // and its members only through TyCtxt.RawMembers and TyCtxt.LayerMembers, never its Origin.
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
    // R7-V1 (PLAN G375): a declared signature's walk is the row's, computed at the row and its requirement exclusion; the walk's declaration
    // is the third count. All three go at R7-C2 (PLAN G375). A conformance computes none (the owner's O-44).
    [InlineData("Computes of a declared signature", "src/Kimi/Compiler", @"\bFormSignature\(", "Binding/Binding.ConstraintValidation.Formation.cs=3")]
    // AllocationMeasurement's contract: every allocation window of the tests goes through Measure, MeasureOnce for one run that cannot
    // repeat, or MeasureHops for a compile-stack hop (DECISIONS R4-M178), which retire the thread's allocation context first; a window
    // that reads the thread's counter itself can be charged a runtime pause's unused context tail (up to ~8 KB) under load. Benchmarks
    // (src/Benchmark) are not tests. A letter in brackets keeps the pattern from matching its source.
    [InlineData("Allocation windows outside AllocationMeasurement", "tests", @"\bGC\.GetAllocatedBytesForCurrentThrea[d]\b", "xUnitTest/AllocationMeasurement.cs=7")]
    // R7-N4 (RP6): the parser classifies an attribute by its Name once, so AttributeKoto.Classify is the one read of the Name, and Binding
    // names no attribute by string (its two hits are the Kimi.Test group's Name). A Name the compiler reads joins the second pattern.
    [InlineData("Reads of an attribute's Name", "src/Kimi/Compiler", @"\bIdentifierKoto\b", "Parsing/Koto/Expressions/UnaryKoto.cs=2")]
    [InlineData("Attribute Name strings in Binding", "src/Kimi/Compiler/Binding", @"""(?:Layout|LibraryImport|Test)""", "KimiLibrary.cs=2")]
    // R7-N2a (RP6): a struct has one base clause (SPEC 6.2.2), read through StructKoto.BaseClause. `.Bases` stays only where the container may
    // be an enum, which keeps its whole list until PrepareStorage fails it, or a Contract, whose refinements are many (BuildContract).
    [InlineData("A container's base clauses", "src/Kimi/Compiler/Binding", @"\.Bases\b|\{ Bases\b", "Binding.Contracts.cs=2;Binding.Expressions.cs=2;Binding.OriginProof.cs=2;Binding.Storage.cs=3")]
    // R7-N1a (RP6): ResolvePath reads the Type namespace; the others are the alias and module walks (R7-N1 e) and their own tables. PublishPath
    // alone writes a Type name's imported environment; the value lookup's site (Imports) ends at R7-N1 b.
    [InlineData("Type-namespace reads", "src/Kimi/Compiler/Binding", @"\.Types\.(?:GetValueOrDefault|TryGetValue)\(", "Binding.Aliases.cs=2;Binding.LibraryWitnesses.cs=1;Binding.Modules.cs=2;Binding.ResolvePath.cs=2;Binding.Types.cs=1")]
    [InlineData("Writes of a use's imported environment", "src/Kimi/Compiler/Binding", @"\.importedContainers\[", "Binding.ResolvePath.cs=1")]
    // PLAN G647: a walk of one Type's parts has no count of its own, so a depth or count compared with a literal stays at the census's
    // sites, over the whole compiler (Mir/Build and Mir/Analysis are in scope and have none): Origin-term nesting (FunctionTypes'
    // inner Origin, TypeRelations' InstanceOutlives), syntax-path walks (ElementAccess, PlaceAuthority, PlaceOrigins, OriginDiagnostics'
    // RootLocal, Diagnostics' `+` chain), a record path's length (TypeRelations.Walk) and sizes that bound no walk (Attributes' stackalloc
    // threshold, SlotSet's word count, TemporaryKotoList's inline slots). A walk across a declaration names its
    // bound, which this pattern does not read.
    [InlineData("A walk's own count", "src/Kimi/Compiler", @"\b(?:[dD]epth|layers|count|steps|level|fields)\s*(?:<=|>=|==|<|>)\s*(?:[2-9]|\d\d+)\b", "Binding/Binding.Attributes.cs=1;Binding/Binding.Diagnostics.cs=1;Binding/Binding.FunctionTypes.cs=1;Binding/Binding.OriginDiagnostics.cs=1;Binding/Binding.PlaceAuthority.Raw.cs=1;Binding/Binding.PlaceAuthority.cs=2;Binding/Binding.PlaceOrigins.cs=1;Binding/Binding.SlotSet.cs=1;Binding/Binding.TypeRelations.Walk.cs=1;Binding/Binding.TypeRelations.cs=1;ElementAccess.cs=6;Parsing/Koto/Common/TemporaryKotoList.cs=2")]
    // DECISIONS R7-C2e1: a view's state is counted where it is written, so a ConstraintEnvironment's fact sets are written only by its own
    // members (Invalid and the flags have private setters), and a BindingScope's link only through its counting setters.
    [InlineData("Writes of a constraint environment's fact sets", "src/Kimi/Compiler", @"\b(?:Facts|DirectFacts|DerivedFacts)\.(?:Add|Remove\w*|Clear|UnionWith|ExceptWith|IntersectWith|SymmetricExceptWith|Insert|TrimExcess)\(", "Binding/Binding.Constraints.Model.cs=6")]
    [InlineData("Counts of a view's state", "src/Kimi/Compiler/Binding", @"\b(?:Version|LinkVersion)\+\+", "Binding.Constraints.Model.cs=3;BindingModel.cs=1")]
    // DECISIONS R7-C2e1: Completion's zero is Provisional and only the whitelist's members (W1-W6, the ContractSolver header) give
    // Complete, so a new route is Provisional until a reviewed row joins them: a further site fails here. Every spelling of Complete is
    // counted (an assignment, a return, an arm, an argument, a cast), the comparison in Reading included.
    // R7-C3a: W7, the identity classes' Completion member (DECISIONS R7-M20's amendment).
    [InlineData("Answers given Complete", "src/Kimi/Compiler", @"\bCompletion\.Complete\b|\(Completion\)", "Binding/Binding.AssociatedTypes.Specification.cs=1;Binding/Binding.BoundContracts.cs=2;Binding/Binding.BuiltinArithmetic.cs=1;Binding/Binding.Constraints.cs=2;Binding/Binding.ContractSolver.cs=2;Binding/Binding.ContractTypes.cs=3;Binding/Binding.FixedArrays.cs=1;Binding/Binding.TypeIdentity.Classes.cs=1;Binding/Binding.TypeIdentity.cs=2")]
    // H11b-0 B0 (RP6): an Origin declaration's state is private; its formation alone names `Forming`, file by file, each occurrence
    // forming the declaration it names (the type's file: the member and its contract's three mentions). Every other read is one of
    // OriginDeclaration's read members, so the readers of a declaration, a failed one included, are those members' callers.
    [InlineData("Formation of an Origin declaration", "src/Kimi/Compiler", @"\bForming\b", "Binding/Binding.LocalOrigins.cs=27;Binding/Binding.OriginDeclarations.Failures.cs=5;Binding/Binding.OriginDeclarations.cs=29;Binding/Binding.OriginInheritance.cs=6;Binding/Binding.Origins.Model.cs=4")]
    public void SourcePatternStaysAtItsSites(string rule, string directory, string regex, string sites)
    {
        var expression = new Regex(regex);
        var root = Path.Combine(Repository.Root, directory);
        var actual = SourceFiles(directory, "*.cs")
            .Select(x => (Name: Path.GetRelativePath(root, x).Replace(Path.DirectorySeparatorChar, '/'), Count: expression.Count(File.ReadAllText(x))))
            .Where(static x => x.Count != 0).Select(static x => $"{x.Name}={x.Count}").Order(StringComparer.Ordinal).ToArray();
        Assert.True(sites.Split(';').Order(StringComparer.Ordinal).SequenceEqual(actual), $"{rule}: {string.Join(";", actual)}; the sites are {sites}.");
    }

    // The R7-R9 plan's unit M2 (§5.3): a component moved out of the compiler is read only by its listed readers, so the compiler, check,
    // LSP, Testing and Diagnostics read none of its types. A reader is a file of src/Kimi outside the component's folder that names a
    // type the folder declares, however the name reaches it (a using, a namespace or type alias, a qualified name), that imports the
    // component's namespace, or that declares it. Roslyn reads the tokens, so comments and strings do not count; the component's own
    // files are not readers; build output (bin, obj) is not source. A further reader fails here until its row is reviewed. Not seen,
    // so review reads them (COMPILER_ARCHITECTURE, "Dependency rules"): a type reached without its name (a var over a member's result,
    // reflection); a delegate type the folder declares (the types are its class, struct, record, interface and enum declarations); an
    // attribute class used by its short name ([Foo] for FooAttribute). Build and Documentation declare neither today. The types are the
    // folder's top-level ones (the plan's unit M1): a nested type is named through its containing type, so a nested name that another file
    // spells for its own member (Container, Bracket, Parts) reads nothing.
    [Theory]
    [InlineData("src/Kimi/Build", "Kimi.Build", BuildReaders)]
    [InlineData("src/Kimi/Documentation", "Kimi.Documentation", DocumentationReaders)]
    public void ComponentIsReadOnlyByItsReaders(string folder, string name, string readers)
    {
        var root = Path.Combine(Repository.Root, "src/Kimi");
        var inside = Path.GetRelativePath(root, Path.Combine(Repository.Root, folder)).Replace(Path.DirectorySeparatorChar, '/') + "/";
        var files = SourceFiles("src/Kimi", "*.cs")
            .Select(x => (Name: Path.GetRelativePath(root, x).Replace(Path.DirectorySeparatorChar, '/'), Path: x))
            .Where(static x => !x.Name.StartsWith("bin/", StringComparison.Ordinal) && !x.Name.StartsWith("obj/", StringComparison.Ordinal))
            .ToArray();
        var types = files.Where(x => x.Name.StartsWith(inside, StringComparison.Ordinal))
            .SelectMany(static x => CSharpSyntaxTree.ParseText(File.ReadAllText(x.Path)).GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            .Where(static x => x.Parent is BaseNamespaceDeclarationSyntax or CompilationUnitSyntax)
            .Select(static x => x.Identifier.ValueText).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(types);

        // Only a file that spells a type's name or the namespace's last part can read or declare it.
        var spelled = new Regex(@"\b(?:" + string.Join("|", types.Append(name[(name.LastIndexOf('.') + 1)..]).Select(Regex.Escape)) + @")\b");
        var actual = files.Where(x => !x.Name.StartsWith(inside, StringComparison.Ordinal))
            .Where(x => File.ReadAllText(x.Path) is var text && spelled.IsMatch(text) && Reads(CSharpSyntaxTree.ParseText(text).GetRoot()))
            .Select(static x => x.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.True(
            readers.Split(';').Order(StringComparer.Ordinal).SequenceEqual(actual),
            $"{folder}: {string.Join(";", actual)}; the readers are {readers}.");

        bool Reads(SyntaxNode unit)
            => unit.DescendantTokens().Any(x => x.IsKind(SyntaxKind.IdentifierToken) && types.Contains(x.ValueText)) ||
                unit.DescendantNodes().OfType<UsingDirectiveSyntax>().Any(x => Within(x.NamespaceOrType.ToString())) ||
                unit.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().Any(x => Within(string.Join(
                    ".", x.AncestorsAndSelf().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(static y => y.Name.ToString()))));

        bool Within(string written)
        {
            written = written.Replace(" ", string.Empty, StringComparison.Ordinal);
            written = written.StartsWith("global::", StringComparison.Ordinal) ? written["global::".Length..] : written;
            return written == name || written.StartsWith(name + ".", StringComparison.Ordinal);
        }
    }

    // R7-N1e2-1R (the owner's O-111 (a)): a written Type name is evaluated only by name resolution's own file (ResolvePath) and named
    // exceptions, each publishing nothing: BaseLayers' DirectBase read of a clause not yet bound and its second run's knownBases (R7-M9: a
    // read that saw a clause not ready stores nothing; both go at R7-D2 d3), BaseLayers' early-read check (it evaluates again by design),
    // Qualification's repair probe of a Container's own name, Constraints' Container question and Calls' question whether a length
    // argument's spelling also names a Type (questions about another reading, not the use's lookup), and Types' semantics parameter name
    // (a first lookup). A use is looked up only through NamePath, at the census's
    // sites: its first call resolves and publishes the use, every later one reads it in the same row (HirTables.Lookups). A further site
    // fails here until its row is reviewed. Roslyn reads the invocations on any receiver, so comments and strings do not count.
    [Theory]
    [InlineData("Evaluations of a written name", NameEvaluations, NameEvaluationSites)]
    [InlineData("Lookups of a Type-name use", UseLookups, UseLookupSites)]
    public void LookupEntriesStayAtTheirSites(string rule, string entries, string sites)
    {
        var names = entries.Split('|');
        var actual = SourceFiles("src/Kimi/Compiler/Binding", "*.cs")
            .Select(x => (Name: Path.GetFileName(x), Count: CSharpSyntaxTree.ParseText(File.ReadAllText(x)).GetRoot().DescendantNodes()
                .OfType<InvocationExpressionSyntax>().Count(i => CalledName(i.Expression) is { } name && names.Contains(name))))
            .Where(static x => x.Count != 0).Select(static x => $"{x.Name}={x.Count}").Order(StringComparer.Ordinal).ToArray();
        Assert.True(sites.Split(';').Order(StringComparer.Ordinal).SequenceEqual(actual), $"{rule}: {string.Join(";", actual)}; the sites are {sites}.");
    }

    // R9-F4 (the owner's O-98, O-99, O-100 and O-106, (a)): TokenHelper's spelling table is the one statement of an F.3 keyword, so no
    // other Lexing or Parsing literal starts with one, alone or after '#' (SPEC F.11 writes "#" "if" and "#" "switch" with the keyword
    // tokens). Roslyn reads every literal token, those in interpolation holes included: string, UTF-8 string, char, raw string and the
    // text parts of interpolated strings, each by its value. The spellings come from the table itself (TokenKind.ToText over the keyword
    // kinds). A '$' name inside the quotes does not start with a spelling: after '$', F.3 matches a Composition Root operation name by
    // spelling ($require, $expect), and $try.value and $try.error are synthesized names. Each allowed site is a file, its member and the
    // literal, with the reason SPEC fixes its text. Bounded: the two folders only.
    [Fact]
    public void KeywordSpellingsStayInTheTable()
    {
        string[] sites =
        [
            // '_' as a Name character (SPEC 2.5), not the keyword.
            "Lexing/Tokenizer.cs|ScanAsciiIdentifierLength|'_'",

            // The destructor's Name (SPEC 16.2.1).
            "Parsing/Koto/Declarations/DeclarationContainers/DeclarationContainerKoto.cs|TryParsePropertyOrFunction|\"drop\"",

            // A constructor's Name (SPEC 6.3).
            "Parsing/Koto/Declarations/FunctionKoto.cs|FunctionKoto|\"init\"",
            "Parsing/Parser.Functions.cs|TryParseFunctionName|\"init\"",

            // The discard Name (SPEC 14.8.1).
            "Parsing/Parser.ControlFlow.cs|TryParseForBinding|\"_\"",
            "Parsing/Parser.Syntax.cs|ParsePattern|\"_\"",

            // The Origin names static and _ (SPEC 15.2.1, 15.2.3).
            "Parsing/Parser.Origins.cs|ParseTypeOrigin|\"static\"",
            "Parsing/Parser.Origins.cs|ParseTypeOrigin|\"_\"",
            "Parsing/Parser.AssociatedTypes.cs|ValidateAssociatedParameters|\"static\"",
            "Parsing/Parser.AssociatedTypes.cs|ValidateAssociatedParameters|\"_\"",
        ];
        SyntaxKind[] literals =
        [
            SyntaxKind.StringLiteralToken, SyntaxKind.Utf8StringLiteralToken, SyntaxKind.CharacterLiteralToken,
            SyntaxKind.SingleLineRawStringLiteralToken, SyntaxKind.MultiLineRawStringLiteralToken, SyntaxKind.Utf8SingleLineRawStringLiteralToken,
            SyntaxKind.Utf8MultiLineRawStringLiteralToken, SyntaxKind.InterpolatedStringTextToken,
        ];
        var spellings = Enumerable.Range(1, (int)TokenKind.Identifier - 1).Select(static x => ((TokenKind)x).ToText()).Where(static x => x.Length > 0);
        var spelling = new Regex(@"^\s*#?(?:" + string.Join("|", spellings.OrderByDescending(static x => x.Length).Select(Regex.Escape)) + @")(?!\w)");
        var root = Path.Combine(Repository.Root, "src/Kimi/Compiler");
        var actual = SourceFiles("src/Kimi/Compiler/Lexing", "*.cs").Concat(SourceFiles("src/Kimi/Compiler/Parsing", "*.cs"))
            .Where(static x => Path.GetFileName(x) != "TokenHelper.cs")
            .SelectMany(file => CSharpSyntaxTree.ParseText(File.ReadAllText(file), new CSharpParseOptions(LanguageVersion.Preview)).GetRoot()
                .DescendantTokens().Where(x => literals.Contains(x.Kind()) && spelling.IsMatch(x.ValueText))
                .Select(x => $"{Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/')}|{Member(x)}|" +
                    (x.IsKind(SyntaxKind.CharacterLiteralToken) ? $"'{x.ValueText}'" : $"\"{x.ValueText}\"")))
            .Order(StringComparer.Ordinal).ToArray();
        Assert.True(sites.Order(StringComparer.Ordinal).SequenceEqual(actual), $"Keyword spellings outside the table: {string.Join("; ", actual)}.");

        // The nearest named declaration around a token: its method, constructor, property, field or type.
        static string Member(SyntaxToken token) => token.Parent!.AncestorsAndSelf().Select(static x => x switch
        {
            MethodDeclarationSyntax method => method.Identifier.Text,
            ConstructorDeclarationSyntax constructor => constructor.Identifier.Text,
            PropertyDeclarationSyntax property => property.Identifier.Text,
            FieldDeclarationSyntax field => field.Declaration.Variables[0].Identifier.Text,
            BaseTypeDeclarationSyntax type => type.Identifier.Text,
            _ => null,
        }).FirstOrDefault(static x => x is not null) ?? string.Empty;
    }

    // R7-C2a: Enumerate is a leaf of the judgment graph (Binding.ContractSolver.cs's contract). The members of its walk call exactly
    // the names listed here, none of which proves, resolves a binding, normalizes or requests a capability (ProveConformance,
    // ProveConstraint, ResolveAssociated, NormalizeProof*, SubstituteIdentity, RequestCapability), and the two callees that can
    // normalize are told not to. A new callee is read for judgments before it joins the list. PremiseRefines and the indexes are the
    // request boundary, not the walk; a walk that reaches PremiseRefines fails at run time (InternalInvariant). R7-C2c2: the walk's class
    // keys (SameClass, and ClassKey and CongruentKey in Binding.TypeIdentity.Keys.cs) are walk members too, so no judgment is reached from
    // a key: they read the built table (KeyOf, Representative) and intern (Rebuild).
    [Fact]
    public void EnumeratesWalkCallsNoJudgment()
    {
        string[] walk = ["Enumerate", "EnumerateLink", "HeadDepth", "ClauseHeads", "AvailableContractPremise", "AvailableConstraintFact", "ContractSupply", "SameClass", "ClassKey", "CongruentKey"];
        string[] allowed =
        [
            "Add", "AppliedAssociatedContract", "AsSpan", "AssociatedIdentityMatches", "AvailableConstraintFact", "AvailableContractPremise", "ClassKey", "ClauseHeads",
            "Clear", "CongruentKey", "Contains", "ContractConstraint", "DependentConstraint", "EnumerateLink", "FactStates", "GetValueOrDefault", "HeadDepth", "Heads",
            "InstantiatedAncestor", "InstantiatedContract", "InvalidDeclarationContext", "IsContractSelf", "KeyOf", "Max", "ReadSourceState", "Rebuild",
            "ReferenceEquals", "RemoveAt", "RemoveRange", "RentTypes", "Representative", "Return", "SameClass", "SetKey", "SubjectKey", "TryGetValue",
            "UnresolvedTypeDeclarationContext",
        ];
        var members = new[] { "Binding.ContractSolver.cs", "Binding.TypeIdentity.Keys.cs" }
            .Select(static x => File.ReadAllText(Path.Combine(Repository.Root, "src/Kimi/Compiler/Binding", x)))
            .SelectMany(static source => CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview), cancellationToken: TestContext.Current.CancellationToken).GetRoot(TestContext.Current.CancellationToken).DescendantNodes())
            .Where(x => (x is MethodDeclarationSyntax method && walk.Contains(method.Identifier.Text)) || (x is StructDeclarationSyntax type && walk.Contains(type.Identifier.Text))).ToArray();
        Assert.Equal(walk.Length, members.Length);
        var calls = members.SelectMany(static x => x.DescendantNodes().OfType<InvocationExpressionSyntax>()).ToArray();
        var actual = calls.Select(static x => CalledName(x.Expression)).Distinct().Order(StringComparer.Ordinal).ToArray();
        Assert.True(allowed.SequenceEqual<string?>(actual), $"Enumerate's walk calls: {string.Join(", ", actual)}.");
        Assert.All(
            calls.Where(static x => CalledName(x.Expression) is "InstantiatedContract" or "ContractConstraint"),
            static x => Assert.Equal("false", x.ArgumentList.Arguments[3].Expression.ToString()));
    }

    // PLAN G609 (RP6): the compiler reads a published list by index and a node's children through the one walk (Koto.VisitChildren), so a
    // warm rebind allocates nothing: `foreach` over an interface calls its GetEnumerator, which boxes a List<T>'s enumerator or allocates
    // an array's whenever the list is nonempty. The compiler's IL calls an interface's GetEnumerator only at these cold sites (parsing and
    // printing syntax, adding sources, dumping MIR), counted by top-level type and method, a lambda's, local function's or state machine's
    // in its declaring method's; every other Kimi.Compiler method calls none (52dc76fd1's site was Compilation's pipeline, not Binding).
    // Not seen: an enumeration a library method makes for the compiler (LINQ over an interface, AddRange, string.Join or a constructor
    // over an IEnumerable<T>); review reads those.
    [Theory]
    [InlineData("Kimi.Compiler", InterfaceEnumerationSites)]
    public void InterfaceEnumerationsStayAtTheirSites(string prefix, string sites)
    {
        var actual = typeof(Kimi.Compiler.Compilation).Assembly.GetTypes().Where(x => x.Namespace?.StartsWith(prefix, StringComparison.Ordinal) == true)
            .SelectMany(InterfaceEnumerations).GroupBy(static x => x, StringComparer.Ordinal).Select(static x => $"{x.Key}={x.Count()}")
            .Order(StringComparer.Ordinal).ToArray();
        Assert.True(sites.Split(';').Order(StringComparer.Ordinal).SequenceEqual(actual), $"Interface enumerations: {string.Join(";", actual)}; the sites are {sites}.");
    }

    // Any receiver: a nested visitor calls its Binding by a field (`binding.TypeName`), which a `this.`-only match missed (round 2).
    private static string? CalledName(ExpressionSyntax called) => called switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
        IdentifierNameSyntax name => name.Identifier.Text,
        _ => null,
    };

    // The calls of an interface's GetEnumerator in a type's own method bodies, read from their IL, one `TopLevelType.Method` each.
    private static IEnumerable<string> InterfaceEnumerations(Type type)
    {
        const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var top = type;
        while (top.DeclaringType is { } outer)
        {
            top = outer;
        }

        foreach (var method in type.GetMethods(Declared).Concat<MethodBase>(type.GetConstructors(Declared)))
        {
            // A compiler-generated member (<Name>b__0, <Name>g__Local|0) or state machine (<Name>d__0.MoveNext) names its declaring method.
            var name = method.Name.StartsWith('<') ? method.Name : type.Name.StartsWith('<') && !type.Name.StartsWith("<>", StringComparison.Ordinal) ? type.Name : method.Name;
            name = name.StartsWith('<') ? name[1..name.IndexOf('>')] : name;
            var il = method.GetMethodBody()?.GetILAsByteArray() ?? [];
            for (var i = 0; i < il.Length;)
            {
                var code = OpCodesByValue[il[i] == 0xFE ? (short)(0xFE00 | il[i + 1]) : il[i]];
                i += code.Size;
                if (code.OperandType == OperandType.InlineMethod && type.Module.ResolveMethod(
                    BitConverter.ToInt32(il, i), type.IsGenericType ? type.GetGenericArguments() : null, method.IsGenericMethod ? method.GetGenericArguments() : null)
                    is { Name: "GetEnumerator", DeclaringType.IsInterface: true })
                {
                    yield return $"{top.Name}.{name}";
                }

                i += code.OperandType switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, i)),
                    _ => 4,
                };
            }
        }
    }

    // A directory that does not exist (deleted, or not yet created) has no files.
    private static IEnumerable<string> SourceFiles(string directory, string pattern)
    {
        var path = Path.Combine(Repository.Root, directory);
        return Directory.Exists(path) ? Directory.EnumerateFiles(path, pattern, SearchOption.AllDirectories) : [];
    }
}
