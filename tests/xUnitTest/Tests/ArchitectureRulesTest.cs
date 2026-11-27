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
        "Binding/Binding.RawOrigins.Borrows.cs=2;Binding/Binding.RawOrigins.Holder.cs=4;Binding/Binding.RawOrigins.Steps.cs=3;Binding/Binding.RawOrigins.cs=4;" +
        "Binding/Binding.Storage.cs=3;Binding/Binding.TypeOrigins.cs=3;" +
        "Binding/Binding.TypeRelations.Origins.cs=5;Binding/Binding.TypeRelations.cs=2;" +
        "Binding/BindingModel.cs=2;Binding/TyCtxt.Raw.cs=1;Binding/TyCtxt.cs=4";

    // The R7-R9 plan's unit M2 (ComponentIsReadOnlyByItsReaders): Build's readers, Project and the commands, the hosts that call it, and
    // DependencyLock (ArtifactFiles.Replace only).
    private const string BuildReaders = "SolutionAndProject/DependencyLock.cs;SolutionAndProject/Project.cs;Unit/Command/BuildCommand.cs;" +
        "Unit/Command/CommandExecution.cs;Unit/Command/RestoreCommand.cs;Unit/Command/RunCommand.cs;Unit/Command/TataraCommand.cs;Unit/Command/TestCommand.cs;Unit/Command/ToolchainCommand.cs";

    // The R7-R9 plan's unit M1 (ComponentIsReadOnlyByItsReaders): Documentation's readers, hover's projection in check and its rendering in
    // the LSP. The compiler reads only the association, Core's DocumentationSource, which Documentation reads in turn.
    private const string DocumentationReaders = "Checking/Hover/HoverBuilder.cs;Checking/HoverFacts.cs;Checking/HoverPlacement.cs;Lsp/HoverRenderer.cs";

    // R7-N1e2-1R (LookupEntriesStayAtTheirSites): the evaluation entries and their sites, the use entries and theirs (the census).
    private const string NameEvaluations = "ResolvePath|ResolveName|ResolveValue|ResolveMember|ResolveRootPath|ResolveAdaptationTarget|ProbePath|ReadAgain|PublishPath|EvaluateUse|EvaluateName";

    private const string NameEvaluationSites = "Binding.BaseLayers.cs=3;Binding.Calls.cs=1;Binding.Qualification.cs=2;Binding.ResolvePath.cs=28;" +
        "Binding.Types.cs=2";

    private const string UseLookups = "NamePath|TypeName|LookupAdaptationTarget|PublishLookup|ReadLeft|MayBeValueQualifier";

    private const string UseLookupSites = "Binding.Aliases.cs=1;Binding.AssociatedInference.Discovery.cs=1;Binding.AssociatedTypes.cs=3;" +
        "Binding.Constraints.Requirements.cs=1;Binding.Constraints.cs=2;Binding.ContainerReferences.cs=1;Binding.Contracts.cs=1;" +
        "Binding.Conversions.cs=1;Binding.Diagnostics.cs=1;Binding.EffectDeclarations.cs=1;" +
        "Binding.Lengths.cs=1;Binding.MemberLookup.cs=1;Binding.OriginDeclarations.cs=1;Binding.Patterns.cs=1;Binding.ResolvePath.cs=4;Binding.ResultSurvey.cs=1;" +
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
    private const string ParsingSemanticSlotSites = "Koto/Declarations/FunctionKoto.cs=9;Koto/Expressions/BinaryKoto.cs=5;" +
        "Koto/Expressions/ControlFlow/ForKoto.cs=1;Koto/Expressions/FormattingKoto.cs=2;" +
        "Koto/Expressions/SyntheticKoto.cs=4;Koto/Expressions/UnaryKoto.cs=2;Koto/Koto.Ownership.cs=4;Koto/Koto.cs=3";

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

    // The R7-R9 plan's §10.2 item 3 (R8-X1): the components, members, kinds and templates R8 deleted (DECISIONS R8-M6, R8-M11, R8-M12,
    // R8-M14, R8-M18, R8-M22, R8-M24, R8-M30, R8-M39, R8-M44, R8-M45, R8-M46, R8-M48, R8-M52) and its Transitional tag; no source, test or
    // project file names one. The intrinsic writer's deleted member counts only qualified, its bare name being an English word. A letter in
    // brackets keeps the pattern from matching its source.
    private const string DeletedByR8 =
        @"\bLegacyRuntimeHelper[s]|\bLegacyWrite[r]\b|\b(?:StorageKernel|Format|StringFormat|Writer)Kin[d]\b|" +
        @"\b(?:Array|Dictionary)Helpe[r](?:Row|Kind)?\b|\bDictionaryCalle[e]\b|\b(?:KimiLibrary|TyCtxt)\.DictionaryStorag[e]\b|\bLengthWor[d]\b|" +
        @"\bHelperNeed[s]\b|\bUtf8(?:Buffer|Format)Runtim[e]\b|\bCompilerFunctio[n](?:Kind|Effect)?\b|\bIntrinsicDispositio[n]\.(?:Of|Effect)\b|" +
        @"\bKimiLibraryContaine[r]\b|\bIs(?:Storage|Raw)Operatio[n]\b|\bIsUnsource[d]\b|\bRuntimeAb[i]\.(?:TextWriter|WriteLine)\b|" +
        @"\bFormattin[g](?:Helper)?Row\b|\bForma[t](?:Estimate|Fixed|Heap)\b|\bIntrinsicWriter\.Unproduce[d]\b|Transitional\(""R[8]""";

    // The same for the seven names R8 deleted that tests still use (DECISIONS R8-M11, R8-M19, R8-M24, R8-M30, R8-M44, R8-M48): six in
    // fail-before and history comments, one as the label of FunctionCodegenTest's first FormattingPrograms row, a live test name. Only the
    // sources and the project file must not name them.
    private const string DeletedByR8InSources =
        @"\bBodylessWitnes[s]\b|\bLegacyRo[w]\b|\bBuiltinForma[t]\b|\bFormattin[g]Rows\b|\bForma[t](?:Buffer|Finish|Segment)\b";

    // R7-O1 c (RP6; DECISIONS R7-M51, R7-M66, R7-M95, R7-M120): the body judge's members R7-O1 deleted, its pending list and its
    // Origin relation slot, and the two names c replaced (FormCallRelation, ConstrainsRegions); no source or test names one, comments
    // included. A letter in brackets keeps the pattern from matching its source.
    private const string DeletedByO1 =
        @"\b(?:RecordOriginFitFailur[e]|OriginFitSearc[h]|OriginFitMod[e]|OriginRelationFail[s]|RecordOriginChain[s]|IsTwinRecor[d]|" +
        @"pendingRelation[s]|(?:Add|Leave|Judge|Route)PendingRelatio[n]s?|HasCallRelatio[n]|JudgeCallPositio[n]|" +
        @"RecordFurtherOriginRecor[d]|originRelation[s]|BodyChai[n])\b";

    // PLAN G609 (InterfaceEnumerationsStayAtTheirSites): the compiler's cold interface enumerations, by top-level type and method.
    private const string InterfaceEnumerationSites = "DeclarationContainerKoto.TryParsePropertyOrFunction=1;DeclarationContainerKoto.WriteAsBlockItem=2;" +
        "FunctionKoto.WriteDeclarationTo=2;Koto.CopyDocumentation=1;Kotonoha.AddSources=2;Kotonoha.CheckNames=1;Kotonoha.ReplaceDocumentation=1;" +
        "MirDump.Write=2;Parser.ParseRequirementBody=1;Parser.ValidateReceiverParameters=1";

    // R7-N5 (NodeKeyedTablesStayAtTheirSites): the node-keyed tables outside Syntax, by top-level type. One string, three owners: MirBody
    // and MirBuilder the Middle's, DiagnosticOwner the Diagnostics owner's, HoverBuilder and CodeContext PLAN G1069's; Binding's count,
    // R7-N5's, reached 0 in R7-E4 E4e (its last table, `ownedConversions`), so Binding has no row. Each lowering edits its own count, so
    // whichever of two units lands second takes the merged counts.
    private const string NodeKeyedTableSites = "CodeContext=1;DiagnosticOwner=3;HoverBuilder=5;MirBody=1;MirBuilder=1";

    // The IL opcodes by value (MethodCalls).
    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(static x => (OpCode)x.GetValue(null)!).ToDictionary(static x => x.Value);

    // The keyed collections' interfaces (NodeKeyedTablesStayAtTheirSites): a dictionary's or a set's, whose first type argument is its key.
    private static readonly Type[] KeyedInterfaces = [typeof(IReadOnlyDictionary<,>), typeof(IDictionary<,>), typeof(IReadOnlySet<>), typeof(ISet<>)];

    // R7-C3 C3c (TypeComparisonsOutsideTheJudgeStayAtTheirSites): the closed list of reasons an equality as written answers SPEC 8.2.3's
    // question. kind: one side is a static Type, or both are primitives, a kind test of the other side as given; unchanged: a Type
    // derived from another (rebuilt, substituted, peeled, completed, formed) against its input, so nothing changed; key: a key, memo,
    // index, slot or set member (the judge's ClassKey or an interned key), no verdict; sufficient: "identical as written" answers yes and
    // anything else asks the judge or a relation; formed: both sides passed a judgment's intake (ClassForm at J1-J4 and (ii), or the pair
    // RelationWalk.Intake published); declared: declared shapes or published Types in their own terms (signatures, a declaration's Self,
    // associated bindings, an overlap's two paths, a layout's parts: no form crosses views); representation: TyCtxt's interning and a
    // Type's own equality, below every view; record: a record's or note's selection, no verdict; finding: an operand that is not what its
    // owner publishes (a defect found, not fixed here), kept until its PLAN open issue's fix lands; the row's comment names that row.
    private static readonly string[] TypeComparisonReasons = ["kind", "unchanged", "key", "sufficient", "formed", "declared", "representation", "record", "finding"];

    // R7-C3 C3c: one row per site and reason, `File:Member=count reason`.
    private static readonly string[] TypeComparisonSites =
    [
        "Binding.Access.cs:IsReceiverType=1 declared",
        "Binding.Aliases.cs:PrepareAliases=1 declared",
        "Binding.ArgumentKinds.Checked.cs:CheckedClass=1 unchanged",
        "Binding.ArgumentKinds.Evidence.cs:InferBoundary=1 kind",
        "Binding.ArgumentKinds.Evidence.cs:InferClosureHeader=1 kind",
        "Binding.ArgumentKinds.Evidence.cs:InferComposite=3 unchanged",
        "Binding.ArgumentKinds.Evidence.cs:InferPosition=1 kind",
        "Binding.ArgumentKinds.Fit.cs:ProbeRangeBoundary=1 formed",
        "Binding.ArgumentKinds.Fit.cs:ProbeRangeBoundary=1 kind",
        "Binding.ArgumentKinds.Positions.cs:LiteralInputType=1 unchanged",
        "Binding.ArgumentKinds.Solution.cs:RestoreSlotValues=1 unchanged",
        "Binding.ArgumentKinds.Wholes.cs:BindWholes=1 key",
        "Binding.ArgumentKinds.Wholes.cs:HoldsNeverPart=1 kind",
        "Binding.ArgumentKinds.Wholes.cs:SameBinding=1 sufficient",
        "Binding.ArgumentOperations.cs:AdaptInput=2 declared",
        "Binding.ArgumentOperations.cs:AdaptInput=2 kind",
        "Binding.ArgumentOperations.cs:AdaptInput=2 sufficient",
        "Binding.ArgumentOperations.cs:SharedReferenceThroughLayers=1 sufficient",
        "Binding.ArithmeticContracts.cs:NotString=1 kind",
        "Binding.ArithmeticOperators.cs:TryArithmetic=1 sufficient",
        "Binding.ArrayInference.cs:ArrayElementEvidence=1 kind",
        "Binding.ArrayInference.cs:BindContextualArrayLiteral=1 kind",
        "Binding.ArrayInference.cs:CollectLiteralElementEvidence=1 kind",
        "Binding.AsWritten.cs:Composed=3 unchanged",
        "Binding.AsWritten.cs:Through=1 unchanged",
        "Binding.AsWritten.cs:Written=1 unchanged",
        "Binding.AsWritten.cs:WrittenEnvironment=1 unchanged",
        "Binding.AsWritten.cs:WrittenProjection=1 kind",
        "Binding.AsWritten.cs:WrittenProjection=2 unchanged",
        "Binding.AssociatedFormation.cs:StoresTypeArgument=2 declared",
        "Binding.AssociatedInference.Demand.cs:DecideAtLimit=1 declared",
        "Binding.AssociatedInference.Demand.cs:HeldValue=1 declared",
        "Binding.AssociatedInference.Demand.cs:PublishInferred=1 declared",
        "Binding.AssociatedInference.Demand.cs:SuppliesInBoth=1 declared",
        "Binding.AssociatedInference.Demand.cs:SuppliesInBoth=1 key",
        "Binding.AssociatedInference.cs:JoinEvidence=4 sufficient",
        "Binding.AssociatedOrigins.cs:AssociatedIdentityMatches=3 key",
        "Binding.AssociatedOrigins.cs:CanonicalAssociatedOrigins=1 unchanged",
        "Binding.AssociatedOrigins.cs:FactStates=1 key",
        "Binding.AssociatedTypes.Specification.cs:PathsAgree=1 declared",
        "Binding.AssociatedTypes.Specification.cs:UnconditionalBinding=1 declared",
        "Binding.AssociatedTypes.cs:AssociatedIdentity=1 declared",
        "Binding.AssociatedTypes.cs:AssociatedInPath=1 sufficient",
        "Binding.AssociatedTypes.cs:PrepareAssociatedBindings=1 declared",
        "Binding.AssociatedTypes.cs:ResolveAssociated=1 declared",
        "Binding.Attributes.cs:HasNoCCounterpart=1 kind",
        "Binding.Attributes.cs:ImportAbi=1 kind",
        "Binding.BareAcquisition.cs:ReborrowedType=1 unchanged",
        "Binding.BoundContracts.cs:BoundContractReference=2 declared",
        "Binding.BoundContracts.cs:SubstituteRequirementContract=1 unchanged",
        "Binding.CallRelations.cs:CollectCallRelations=1 sufficient",
        "Binding.CallSelection.Candidates.cs:InferExpectedResult=1 unchanged",
        "Binding.CallSelection.Candidates.cs:TryCandidateCore=1 unchanged",
        "Binding.CallSlots.cs:FittedPattern=1 unchanged",
        "Binding.CallSlots.cs:FormedApplication=1 unchanged",
        "Binding.CallSlots.cs:Infer=4 formed",
        "Binding.CallSlots.cs:Infer=2 sufficient",
        "Binding.CallSlots.cs:Infer=1 unchanged",
        "Binding.CallSlots.cs:IsFormedLayer=1 finding", // PLAN G898 (D-next): a layer of a pair admitting only owner is its target by the pair row, read as written
        "Binding.CallSlots.cs:IsSource=1 finding", // PLAN G898 (D-next): two formed layers whose pairs admit one Semantics have one head, compared as written
        "Binding.CallSlots.cs:IsSource=1 formed",
        "Binding.CallSlots.cs:IsSource=1 kind",
        "Binding.CallSlots.cs:LayerTarget=1 finding", // PLAN G898 (D-next): two pairs' wholes compared as written where one single-Semantics head is one layer
        "Binding.CallableContracts.cs:CompareCallableContracts=1 declared",
        "Binding.CallableEffects.cs:CheckCallableEffectUses=2 key",
        "Binding.CallableEffects.cs:CollectCallableEffectUses=2 key",
        "Binding.CallableSelection.cs:ItemPartFails=1 unchanged",
        "Binding.CallableSelection.cs:ProbeValueCall=1 unchanged",
        "Binding.CallableSelection.cs:SameCallableSignature=1 sufficient",
        "Binding.CallableSelection.cs:SameProofPremise=1 key",
        "Binding.CallableSelection.cs:SelectValueCall=1 declared",
        "Binding.CandidateEvaluation.cs:BetterCandidate=1 declared",
        "Binding.Capabilities.cs:StructuralCapability=1 unchanged",
        "Binding.Capabilities.cs:TryLeafCapability=2 kind",
        "Binding.ClosureInstances.cs:ClosePartCore=1 unchanged",
        "Binding.ClosureResults.cs:PublishedParts=1 unchanged",
        "Binding.Closures.cs:ClosureSignatureFits=1 kind",
        "Binding.Closures.cs:FailClosureHeader=1 kind",
        "Binding.Comparisons.cs:CompleteComparison=9 kind",
        "Binding.ComponentSummaries.cs:AddInstance=1 key",
        "Binding.ComponentSummaries.cs:WalkProjection=1 key",
        "Binding.ComponentSummaries.cs:WalkProjection=1 unchanged",
        "Binding.ConditionalConformances.cs:DisjointConformanceFacts=1 declared",
        "Binding.ConditionalConformances.cs:DisjointConformanceFacts=1 key",
        "Binding.ConditionalConformances.cs:SameConformanceType=1 sufficient",
        "Binding.ConstraintValidation.Proofs.cs:UnprovenInputRead=1 sufficient",
        "Binding.ConstraintValidation.cs:SameTypeOperand=1 unchanged",
        "Binding.ConstraintValidation.cs:SemanticsPremise=1 declared",
        "Binding.Constraints.Model.cs:Equals=2 key",
        "Binding.Constraints.Model.cs:NameUnprovenInput=1 key",
        "Binding.Constraints.cs:JudgeConstraintAtom=1 unchanged",
        "Binding.Constraints.cs:NormalizeProofConstraint=2 unchanged",
        "Binding.Constraints.cs:NormalizeProofTypeCore=1 unchanged",
        "Binding.Constraints.cs:NormalizedContract=1 unchanged",
        "Binding.ConstructorValidation.cs:StableConstructionPremises=1 sufficient",
        "Binding.ConstructorValidation.cs:ValidateFixedConstruction=1 key",
        "Binding.ConstructorValidation.cs:ValidateFixedConstruction=1 sufficient",
        "Binding.ContractMatching.cs:ProveSpecification=1 declared",
        "Binding.ContractSolver.Rows.cs:Add=1 key",
        "Binding.ContractSolver.Rows.cs:DropClassKeys=1 key",
        "Binding.ContractSolver.Rows.cs:EnumerateClosed=1 key",
        "Binding.ContractSolver.Rows.cs:FirstRows=1 key",
        "Binding.ContractSolver.Rows.cs:IndexWritten=1 key",
        "Binding.ContractSolver.Rows.cs:PremiseRefines=1 key",
        "Binding.ContractSolver.cs:EnumerateMates=2 key",
        "Binding.ContractSolver.cs:SameClass=1 key",
        "Binding.ContractSolver.cs:SpokenVerdict=1 key",
        "Binding.ContractTypes.cs:ContractTypeCore=3 unchanged",
        "Binding.ContractTypes.cs:Hop=1 key",
        "Binding.ContractTypes.cs:InstantiatedContract=1 unchanged",
        "Binding.ContractTypes.cs:WithConformingSelf=1 unchanged",
        "Binding.Contracts.Expansion.cs:AddArgumentPasses=2 formed",
        "Binding.Contracts.cs:IsSelectionCandidate=1 sufficient",
        "Binding.ControlFlow.cs:EndArrival=1 kind",
        "Binding.ControlFlow.cs:InfersUnit=1 kind",
        "Binding.ControlFlow.cs:ReadTypeUnification=1 formed",
        "Binding.ControlFlow.cs:SelectCommonType=2 kind",
        "Binding.Conversions.Raw.cs:CompletePointerConversion=2 kind",
        "Binding.Conversions.cs:BindConversion=11 formed",
        "Binding.Conversions.cs:BindConversion=2 kind",
        "Binding.Conversions.cs:IsNeverValue=1 kind",
        "Binding.Conversions.cs:IsRuntimeRecordType=3 kind",
        "Binding.Conversions.cs:ResultNeverEvidence=2 kind",
        "Binding.Diagnostics.Operators.cs:ReportOperatorOperand=1 kind",
        "Binding.Diagnostics.cs:FailedCallableClause=1 record",
        "Binding.Diagnostics.cs:SharedObjectAuthorityMismatch=1 record",
        "Binding.Dictionaries.cs:BindDictionaryLiteral=2 kind",
        "Binding.Dictionaries.cs:TryDictionaryLiteralKey=1 kind",
        "Binding.ElementAccess.cs:IsSynthesizedAccess=2 unchanged",
        "Binding.Elements.cs:BindElement=3 kind",
        "Binding.Elements.cs:BindElement=1 unchanged",
        "Binding.Enums.cs:BindEnumConstruction=1 declared",
        "Binding.Enums.cs:BindEnumConstruction=1 kind",
        "Binding.Enums.cs:FixConstructionSlots=1 kind",
        "Binding.Enums.cs:QualifiedEnumAt=1 declared",
        "Binding.ExplicitAdaptations.cs:BindSemanticsAdaptation=1 kind",
        "Binding.ExplicitAdaptations.cs:BindSemanticsAdaptation=3 unchanged",
        "Binding.ExplicitAdaptations.cs:CheckAdaptationRows=2 unchanged",
        "Binding.Expressions.cs:BindAndAdaptNode=2 unchanged",
        "Binding.Expressions.cs:BindNodeCore=1 kind",
        "Binding.Expressions.cs:IsIntegerLiteralOnly=1 kind",
        "Binding.Expressions.cs:NumericLiteralDefault=2 kind",
        "Binding.FunctionReferences.cs:ExplicitReferenceAgrees=1 sufficient",
        "Binding.FunctionReferences.cs:SolveOmittedOrigins=1 sufficient",
        "Binding.FunctionTypes.cs:ConvertFunctionValue=1 sufficient",
        "Binding.FunctionTypes.cs:SameExceptOmittedOrigins=1 sufficient",
        "Binding.FunctionWitnesses.cs:ProjectRequirementReceiver=1 declared",
        "Binding.Hir.Adjust.cs:DecidesAlike=2 key",
        "Binding.Hir.Patterns.cs:Subject=1 unchanged",
        "Binding.Hir.cs:Layers=1 unchanged",
        "Binding.Imports.cs:AddImport=1 declared",
        "Binding.InPlaceReads.cs:ElementRead=1 unchanged",
        "Binding.InPlaceReads.cs:StringThroughLayers=2 kind",
        "Binding.Indexers.cs:TryBindIndexer=1 unchanged",
        "Binding.InferCtxt.cs:BindsDistinctTypes=1 formed",
        "Binding.InlineLayouts.cs:AddInlinePart=1 key",
        "Binding.InlineLayouts.cs:WithComponent=1 unchanged",
        "Binding.Interpolation.cs:BindTryWrite=1 kind",
        "Binding.Interpolation.cs:FormattingCall=1 kind",
        "Binding.Lengths.cs:EvaluateLength=1 kind",
        "Binding.Lengths.cs:LengthTypeEvidence=1 kind",
        "Binding.Lengths.cs:SameLengthSignature=1 declared",
        "Binding.LibraryWitnesses.cs:WitnessGroup=6 kind",
        "Binding.LibraryWitnesses.cs:WitnessReceiver=6 kind",
        "Binding.Lints.cs:FailureTargetFits=2 record",
        "Binding.Lints.cs:HasValue=2 kind",
        "Binding.Materialization.cs:Materialization=1 unchanged",
        "Binding.MemberLookup.cs:LookupMember=1 unchanged",
        "Binding.MemberLookup.cs:SuppliesIdentity=1 declared",
        "Binding.NumericConversions.cs:BindBitConversion=3 kind",
        "Binding.NumericConversions.cs:BindWrapConversion=1 kind",
        "Binding.NumericConversions.cs:BitWidth=2 kind",
        "Binding.NumericConversions.cs:TryFloatLiteral=1 kind",
        "Binding.ObjectViews.cs:FindObjectErasure=1 declared",
        "Binding.ObjectViews.cs:FindObjectErasure=2 key",
        "Binding.ObjectViews.cs:ObjectUpcastFits=1 formed",
        "Binding.ObjectViews.cs:RequireObjectErasure=1 sufficient",
        "Binding.OpenOrigins.cs:FillOpenOrigins=1 sufficient",
        "Binding.OpenOrigins.cs:FillOpenOrigins=1 unchanged",
        "Binding.OpenOrigins.cs:OpenKnownOrigins=1 unchanged",
        "Binding.OpenOrigins.cs:OpenSlotOrigins=1 unchanged",
        "Binding.OpenOrigins.cs:PerCallStandIn=1 unchanged",
        "Binding.OpenOrigins.cs:ReplaceOpenOrigins=1 unchanged",
        "Binding.OpenSlots.cs:FillOpenSlots=1 sufficient",
        "Binding.OpenSlots.cs:FillOpenSlots=1 unchanged",
        "Binding.OperatorApplicability.cs:ArithmeticPairApplicability=2 kind",
        "Binding.OperatorApplicability.cs:ComparisonApplicability=3 kind",
        "Binding.OperatorApplicability.cs:OperatorApplicabilityOf=4 kind",
        "Binding.OperatorApplicability.cs:OperatorApplicabilityOf=1 unchanged",
        "Binding.OperatorApplicability.cs:RightOperatorApplicability=1 kind",
        "Binding.OperatorApplicability.cs:ShiftCountApplicability=1 kind",
        "Binding.Operators.cs:BindBinary=1 kind",
        "Binding.Operators.cs:CompleteBinary=2 kind",
        "Binding.Operators.cs:NonNumericOperand=2 kind",
        "Binding.OriginDiagnostics.cs:IsExcludedInputOrigin=1 record",
        "Binding.OriginInheritance.cs:CompleteImplementationOrigins=2 formed",
        "Binding.OriginPremises.cs:Environment=2 key", // PLAN G1082: a premise session environment's key, a comparison's required signature and each enclosing one.
        "Binding.OriginRequirements.cs:PairOrdinal=1 key",
        "Binding.OwnedConversions.cs:JudgeErasedOwned=2 key", // R7-O2 O2-N's N-B3a: an Owned obligation's Type against the use's published subject, which deferral is the use's last
        "Binding.PairCases.cs:CaseType=2 unchanged",
        "Binding.PairCases.cs:PairSlotCondition=1 key",
        "Binding.ParameterShapes.cs:SameBinding=1 sufficient",
        "Binding.Patterns.cs:BindPattern=1 kind",
        "Binding.Patterns.cs:BorrowsInPlace=1 kind",
        "Binding.Patterns.cs:CalculateMatchCoverage=1 kind",
        "Binding.Patterns.cs:ResolvePatternCase=1 declared",
        "Binding.Patterns.cs:TryPatternLiteral=3 kind",
        "Binding.Payloads.cs:TryPayloadProjection=1 sufficient",
        "Binding.PlaceAuthority.cs:BorrowableTemporary=1 kind",
        "Binding.Properties.cs:AccessorTypeMismatch=1 declared",
        "Binding.Properties.cs:BindAccessorBody=1 kind",
        "Binding.Properties.cs:ValidateAccessor=1 declared",
        "Binding.PropertyMatching.cs:MatchPropertyOperation=3 declared",
        "Binding.Ranges.cs:BindFromEnd=1 kind",
        "Binding.Ranges.cs:BindPositionKey=1 kind",
        "Binding.Ranges.cs:BindRangeValue=2 kind",
        "Binding.Ranges.cs:DirectBoundary=1 kind",
        "Binding.Ranges.cs:FitDirectBoundary=1 kind",
        "Binding.Ranges.cs:FitsLiteralPosition=2 kind",
        "Binding.Ranges.cs:FromEndOffset=1 kind",
        "Binding.Ranges.cs:IndependentBoundary=1 kind",
        "Binding.Ranges.cs:TryBindKeyedSelection=2 kind",
        "Binding.RawOrigins.Borrows.cs:Join=1 key",
        "Binding.RawOrigins.Borrows.cs:Mode=1 key",
        "Binding.RawOrigins.cs:SameExceptRawOrigins=1 sufficient",
        "Binding.ReadTypes.cs:ReadTypeReferent=1 unchanged",
        "Binding.RecursiveContracts.cs:IsRecursive=2 declared",
        "Binding.RecursiveContracts.cs:OfSearchSelf=1 declared",
        "Binding.ResultPremises.cs:DefinitionContractFits=1 kind",
        "Binding.ResultPremises.cs:ProvesInputPremises=1 sufficient",
        "Binding.ResultPremises.cs:SolveValueCallOrigins=1 kind",
        "Binding.ResultPropagation.Parts.cs:AddResultPart=1 kind",
        "Binding.ResultPropagation.Parts.cs:AddResults=1 kind",
        "Binding.ResultPropagation.cs:BindWaitedSources=1 kind",
        "Binding.ResultPropagation.cs:DefaultWaitingPart=1 kind",
        "Binding.ResultPropagation.cs:FittedTupleType=1 kind",
        "Binding.ResultPropagation.cs:LateAdaptation=1 unchanged",
        "Binding.ResultPropagation.cs:LateResultType=1 kind",
        "Binding.ResultPropagation.cs:LateType=1 kind",
        "Binding.ResultPropagation.cs:PartAwaits=1 kind",
        "Binding.ResultPropagation.cs:ResultCandidates=1 kind",
        "Binding.ResultPropagation.cs:SourcesAwait=1 kind",
        "Binding.ResultSurvey.cs:SourceEvidence=1 kind",
        "Binding.Retention.Declared.cs:View=1 unchanged",
        "Binding.Retention.Declared.cs:Viewed=1 unchanged",
        "Binding.Retention.Rows.cs:Hands=1 key",
        "Binding.Retention.Rows.cs:Takes=1 kind",
        "Binding.Retention.Rows.cs:Writes=1 kind",
        "Binding.Retention.Rows.cs:Written=1 key",
        "Binding.Retention.Sources.cs:CaseFits=1 key",
        "Binding.Retention.Sources.cs:Fits=1 declared",
        "Binding.Retention.Storage.cs:Parts=1 key",
        "Binding.Retention.Storage.cs:StorageWalk=1 key",
        "Binding.Retention.cs:AddErasedSignature=2 kind",
        "Binding.Retention.cs:Handed=2 key",
        "Binding.RuntimeTypeTests.cs:BindRuntimeTypeTest=3 kind",
        "Binding.SemanticsAdaptations.cs:AdaptedType=1 kind",
        "Binding.SemanticsAdaptations.cs:FamilyApplication=1 unchanged",
        "Binding.SemanticsAdaptations.cs:PairApplication=1 unchanged",
        "Binding.SemanticsAdaptations.cs:SplitIndex=1 key",
        "Binding.Sequences.cs:BeginIterationStep=1 formed",
        "Binding.Sequences.cs:IterationEntry=1 unchanged",
        "Binding.ShapeEvidence.cs:SameArgumentEvidence=1 kind",
        "Binding.ShapeEvidence.cs:SameShapeBinding=1 sufficient",
        "Binding.Signatures.cs:SignatureEquals=1 sufficient",
        "Binding.SlotVariables.cs:SkeletonOf=1 unchanged",
        "Binding.SlotVariables.cs:SlotValuesOf=1 unchanged",
        "Binding.Specializations.cs:MayMatch=1 sufficient",
        "Binding.Startup.cs:ValidMain=1 kind",
        "Binding.Storage.cs:CompletedParts=2 unchanged",
        "Binding.Storage.cs:MarkedParts=1 unchanged",
        "Binding.Storage.cs:StoredOriginParts=1 unchanged",
        "Binding.Structs.cs:PrepareTypeStorage=1 key",
        "Binding.Try.cs:TryFailure=1 record",
        "Binding.TypeArities.cs:AddTypeCandidates=1 declared",
        "Binding.TypeHeight.cs:Mark=1 key",
        "Binding.TypeOrigins.cs:OriginFormationSite=2 record",
        "Binding.TypeRelations.Origins.cs:PlaceCommonType=1 sufficient",
        "Binding.TypeRelations.Walk.cs:Discharged=1 formed",
        "Binding.TypeRelations.Walk.cs:FunctionFits=1 formed",
        "Binding.TypeRelations.Walk.cs:Names=1 unchanged",
        "Binding.TypeRelations.Walk.cs:Relate=1 formed",
        "Binding.TypeRelations.cs:CompatibleResult=1 kind",
        "Binding.TypeRelations.cs:CompatibleSignature=1 unchanged",
        "Binding.TypeRelations.cs:SubstituteTypeCore=3 unchanged",
        "Binding.Types.cs:BindTypeStructure=1 kind",
        "Binding.ValueCalls.cs:BindValueCall=1 kind",
        "Binding.ValueCalls.cs:CallableClosure=1 unchanged",
        "Binding.ValueCalls.cs:FormValueCallee=1 unchanged",
        "Binding.ValueCalls.cs:IndirectObjectCallee=1 unchanged",
        "Binding.ValueCalls.cs:TryCallable=1 unchanged",
        "Binding.ValueContext.cs:DiscardsFunctionBody=1 kind",
        "Binding.ValueContext.cs:IsUnitType=1 kind",
        "Binding.ValueFits.cs:CheckTypeUseCore=5 formed",
        "Binding.ValueFits.cs:NeverFits=1 kind",
        "Binding.VirtualOverrides.cs:SelectVirtualCall=2 declared",
        "Binding.WellFormedPremises.cs:FirstMet=1 key",
        "BindingModel.cs:Equals=1 representation",
        "BindingModel.cs:IsScalar=2 kind",
        "BindingModel.cs:IsWrappingInteger=1 representation",
        "ExplicitAdaptationPlan.cs:Select=7 formed",
        "ExplicitAdaptationPlan.cs:Select=2 kind",
        "TyCtxt.Comparison.cs:OperatorCompares=1 kind",
        "TyCtxt.Comparison.cs:OperatorTableRuns=2 kind",
        "TyCtxt.Formatting.cs:FormatBound=5 kind",
        "TyCtxt.Retention.Calls.cs:Retains=1 kind",
        "TyCtxt.Substitutions.cs:HeldContext=1 representation",
        "TyCtxt.Substitutions.cs:Intern=3 representation",
        "TyCtxt.Substitutions.cs:Synthesized=1 representation",
        "TyCtxt.cs:CoreContent=1 kind",
        "TyCtxt.cs:Intern=2 representation",
        "TyCtxt.cs:IsStoredReference=2 kind",
        "TyCtxt.cs:PairBindings=1 representation",
        "TyCtxt.cs:StorageMatches=1 representation",
    ];

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
    // The R7-R9 plan's §10.2 item 3 (R8-X1): the same for R8's deleted components, the project file included (it lists every embedded
    // template).
    [InlineData("Source references to R8's deleted components", "src", "*.cs", DeletedByR8 + "|" + DeletedByR8InSources, 0)]
    [InlineData("Project references to R8's deleted components", "src/Kimi", "*.csproj", DeletedByR8 + "|" + DeletedByR8InSources, 0)]
    [InlineData("Test references to R8's deleted components", "tests", "*.cs", DeletedByR8, 0)]
    // R7-O1 c (RP6): the same for R7-O1's deleted body judge and the names c replaced.
    [InlineData("Compiler references to R7-O1's deleted body judge", "src/Kimi", "*.cs", DeletedByO1, 0)]
    [InlineData("Test references to R7-O1's deleted body judge", "tests", "*.cs", DeletedByO1, 0)]
    // R7-I2 (RP6): a slot frame is private to InferCtxt (the type system admits no unlogged write); SlotSolution's trial copies of
    // its frame (PerCallCallables' stand-ins) fall to 0 with PLAN G988 (R7-I2 c keeps the copy: in place it moves constructors).
    [InlineData("SlotSolution's trial copies of a frame", "src/Kimi/Compiler/Binding", "Binding.ArgumentKinds*.cs", @"\btypeScratch\.Rent\(", 1)]
    // R6-check H14 (D7; DECISIONS R5-M9): the reads of the Koto semantic slots outside Binding end, area by area; comments count.
    [InlineData("Mir reads of Koto semantic slots", "src/Kimi/Compiler/Mir", "*.cs", SemanticSlotReads, 0)]
    [InlineData("Backend reads of Koto semantic slots", "src/Kimi/Compiler/Backend", "*.cs", SemanticSlotReads, 0)]
    // The root helpers, each by its file (SourceFiles recurses), and the other areas outside Binding.
    [InlineData("StrictBestCandidate reads of Koto semantic slots", "src/Kimi/Compiler", "StrictBestCandidate.cs", SemanticSlotReads, 0)]
    [InlineData("Documentation reads of Koto semantic slots", "src/Kimi/Documentation", "*.cs", SemanticSlotReads, 0)]
    [InlineData("Core reads of Koto semantic slots", "src/Kimi/Compiler/Core", "*.cs", SemanticSlotReads, 0)]
    [InlineData("Testing reads of Koto semantic slots", "src/Kimi/Testing", "*.cs", SemanticSlotReads, 0)]
    [InlineData("Benchmark reads of Koto semantic slots", "src/Benchmark", "*.cs", SemanticSlotReads, 0)]
    // DECISIONS R4-M101 E8 (2a0-D D7, D8): only the C1-F4 view decodes an Origin's slot, binder or forming body, so no Mir source reads
    // them (G163); fixed-fixed entailment has one owner, so OriginPremiseClosure.AddEdge has one caller, the premise catalog's writer
    // (AddPremiseEdge, bu20's C0d).
    [InlineData("Mir reads of an Origin's slot, binder or forming body", "src/Kimi/Compiler/Mir", "*.cs", @"(\b[Oo]rigin|[Aa]tom)\w*\??\.(Slot|Binder|Body)\b|\.DeclaringBody\b", 0)]
    [InlineData("Premise edges written outside the premise catalog's writer", "src/Kimi/Compiler", "*.cs", @"(?<!this)\.AddEdge\(", 1)]
    // DECISIONS BU-105 (R7-D1): BindingSymbol.Resolving is the variable binding's own Running flag until R7-D2 d4 makes the variable
    // binding a declaration query; the header's use of it went to Binding.DeclQuery. A property pattern reads it too.
    [InlineData("Binding reads and writes of BindingSymbol.Resolving (0 at R7-D2 d4)", "src/Kimi/Compiler/Binding", "*.cs", @"\.Resolving\b|\bResolving\s*:", 5)]
    // DECISIONS BU-111 (amended by G478): a Binding failure fact records typed parts, and its explanation text is formed only by the
    // diagnostics adapter. None is left: R7-E3 E3b-1 records the unbound slot as a related location (UnboundSlotFact.Note went).
    [InlineData("String explanations in Binding failure facts", "src/Kimi/Compiler/Binding", "*.cs", @"\bstring (?:Condition|Reason|Note)\b(?=[,)])", 0)]
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
    // R7-N4 (RP6): Binding names no attribute by string; AttributeKoto.Classify is the one read of an attribute's Name.
    [InlineData("Attribute Name strings in Binding", "src/Kimi/Compiler/Binding", "*.cs", @"""(?:Layout|LibraryImport|Test|Lang|Intrinsic|CallerLocation|Unsupported)""", 0)]
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
    // DECISIONS Z17 (N1): every derived Opaque value is formed by the MIR builder's gate (MirBuilder.Opaque.cs) from a producer table row;
    // an Opaque site is added only there (AddSite, its one MirBody.AddOpaque call), Site is reached only from the gate, Unsupported and
    // Invariant, and each caller of the gate is a table row, so a new producer fails here until its row is reviewed.
    [InlineData("Formers of an Opaque site", "src/Kimi/Compiler/Mir", @"\.AddOpaque\(|\bthis\.AddSite\(", "Build/MirBuilder.Opaque.cs=3")]
    [InlineData("Callers of the derived-value gate", "src/Kimi/Compiler/Mir/Build", @"\bthis\.(?:Opaque|Derived|DerivedValue|Site)\(", "MirBuilder.Acquisitions.cs=1;MirBuilder.Blocks.cs=2;MirBuilder.Constructors.cs=1;MirBuilder.Defaults.cs=1;MirBuilder.Match.cs=1;MirBuilder.Opaque.cs=6;MirBuilder.Places.cs=2;MirBuilder.Transfers.cs=1;MirBuilder.Values.cs=2;MirBuilder.cs=1")]
    // DECISIONS Z17: the readers of MirUses are the census's, each reading an Opaque site's undecided use at its root through
    // MirDescriptors.Judged, or at its Place for A1's reason (MirInit, MirMoveData), so a new reader fails here until its census row is
    // reviewed. An implementer lists IMirUses in its base list after any other; a generic constraint (where T : struct, IMirUses) is none.
    [InlineData("Readers of MirUses", "src/Kimi", @"(?<!\bwhere\s+\w+\s*):\s*(?:[\w.<>]+\s*,\s*)*IMirUses\b", "Compiler/Backend/Instances/InstanceCollector.Layouts.cs=1;Compiler/Mir/Analysis/MirBorrows.Accesses.cs=1;Compiler/Mir/Analysis/MirBorrows.Addresses.cs=1;Compiler/Mir/Analysis/MirBorrows.Liveness.cs=1;Compiler/Mir/Analysis/MirBorrows.OwnSteps.cs=1;Compiler/Mir/Analysis/MirEffects.cs=1;Compiler/Mir/Analysis/MirInit.cs=1;Compiler/Mir/Analysis/MirMoveData.cs=1;Compiler/Mir/MirValidator.Diverged.cs=1;Compiler/Mir/MirValidator.cs=1")]
    // R6-check H14 (D7): Parsing reads the Koto semantic slots only in their declarations' bodies and on their write paths (a replaced
    // child's reset, the views and synthetic Kotos Binding builds), which R7 removes with the slots (DECISIONS R5-M9).
    [InlineData("Parsing reads of Koto semantic slots", "src/Kimi/Compiler/Parsing", SemanticSlotReads, ParsingSemanticSlotSites)]
    // DECISIONS BU-105: a declaration query's state has one writer, Binding.DeclQuery's transitions (R7-D2 d2's FailQuery the fourth),
    // and is read outside the component only at its interim: BindVariableCore's guard (Variables, until d4). PLAN G949 S1-1 (B4): the
    // freeze's verify half reads the library's states in the component (LibraryQueryFault, three reads); CompleteQuery and FailQuery
    // read the prior state, QueryRecord the failed answer, HeaderRunningOrFailed the frame fact (G812 rulings 174, 176: TypedByHeader's).
    [InlineData("Writes of a declaration query's state", "src/Kimi/Compiler", @"\.SetQueryState\(", "Binding/Binding.DeclQuery.cs=4")]
    [InlineData("Reads of a declaration query's state", "src/Kimi/Compiler", @"\.QueryState\(", "Binding/Binding.DeclQuery.cs=10;Binding/Binding.Variables.cs=1")]
    // R7-V1 (PLAN G375): a declared signature's walk is the row's, computed at the row and outside it, for a requirement's call and (PLAN
    // G949 S1-r2, DECISIONS BU-120's line) a demand before the gate; the walk's declaration is the third count. All three go at R7-C2
    // (PLAN G375). A conformance computes none (the owner's O-44).
    [InlineData("Computes of a declared signature", "src/Kimi/Compiler", @"\bFormSignature\(", "Binding/Binding.ConstraintValidation.Formation.cs=3")]
    // AllocationMeasurement's contract: every allocation window of the tests goes through Measure, MeasureOnce for one run that cannot
    // repeat, or MeasureHops for a compile-stack hop (DECISIONS R4-M178), which retire the thread's allocation context first; a window
    // that reads the thread's counter itself can be charged a runtime pause's unused context tail (up to ~8 KB) under load. Benchmarks
    // (src/Benchmark) are not tests. A letter in brackets keeps the pattern from matching its source.
    [InlineData("Allocation windows outside AllocationMeasurement", "tests", @"\bGC\.GetAllocatedBytesForCurrentThrea[d]\b", "xUnitTest/AllocationMeasurement.cs=7")]
    // R7-N4 (RP6): the parser classifies an attribute by its Name once, so AttributeKoto.Classify is the one read of the Name, and Binding
    // names no attribute by string (SourcePatternDoesNotGrow's row; its two hits were the deleted Kimi.Test shell's Name, R8-G1 G1-5).
    [InlineData("Reads of an attribute's Name", "src/Kimi/Compiler", @"\bIdentifierKoto\b", "Parsing/Koto/Expressions/UnaryKoto.cs=2")]
    // R7-N2a (RP6): a struct has one base clause (SPEC 6.2.2), read through StructKoto.BaseClause. `.Bases` stays only where the container may
    // be an enum, which keeps its whole list until PrepareStorage fails it, or a Contract, whose refinements are many (BuildContract).
    [InlineData("A container's base clauses", "src/Kimi/Compiler/Binding", @"\.Bases\b|\{ Bases\b", "Binding.Contracts.cs=2;Binding.Expressions.cs=2;Binding.OriginProof.cs=2;Binding.Storage.cs=3")]
    // R7-N1a (RP6): ResolvePath reads the Type namespace; the others are the alias and module walks (R7-N1 e) and their own tables. PublishPath
    // alone writes a Type name's imported environment; the value lookup's site (Imports) ends at R7-N1 b. R7-N1 f: a scope's formal
    // parameters (BindingScope.Parameters.Types) are Type-namespace reads too: TypeStage's (the root path reads its root through it) and
    // the library witness rows' own slots.
    [InlineData("Type-namespace reads", "src/Kimi/Compiler/Binding", @"\.Types\.(?:GetValueOrDefault|TryGetValue)\(", "Binding.Aliases.cs=1;Binding.LibraryWitnesses.cs=1;Binding.Modules.cs=2;Binding.ResolvePath.cs=2;Binding.Types.cs=1")]
    [InlineData("Writes of a use's imported environment", "src/Kimi/Compiler/Binding", @"\.importedContainers\[", "Binding.ResolvePath.cs=1")]
    // PLAN G647: a walk of one Type's parts has no count of its own, so a depth or count compared with a literal stays at the census's
    // sites, over the whole compiler (Mir/Build and Mir/Analysis are in scope and have none): Origin-term nesting (FunctionTypes'
    // inner Origin, TypeRelations' InstanceOutlives), a record path's length (TypeRelations.Walk) and sizes that bound no walk
    // (Attributes' stackalloc threshold, SlotSet's word count, TemporaryKotoList's inline slots). A walk over syntax has none
    // (SyntaxNesting's bound, IMPL A.4; D-932). A walk across a declaration names its bound, which this pattern does not read.
    [InlineData("A walk's own count", "src/Kimi/Compiler", @"\b(?:[dD]epth|layers|count|steps|level|fields)\s*(?:<=|>=|==|<|>)\s*(?:[2-9]|\d\d+)\b", "Binding/Binding.Attributes.cs=1;Binding/Binding.FunctionTypes.cs=1;Binding/Binding.SlotSet.cs=1;Binding/Binding.TypeRelations.Walk.cs=1;Binding/Binding.TypeRelations.cs=1;Parsing/Koto/Common/TemporaryKotoList.cs=2")]
    // DECISIONS R7-C2e1: a view's state is counted where it is written, so a ConstraintEnvironment's fact sets are written only by its own
    // members (Invalid and the flags have private setters), and a BindingScope's link only through its counting setters.
    [InlineData("Writes of a constraint environment's fact sets", "src/Kimi/Compiler", @"\b(?:Facts|DirectFacts|DerivedFacts)\.(?:Add|Remove\w*|Clear|UnionWith|ExceptWith|IntersectWith|SymmetricExceptWith|Insert|TrimExcess)\(", "Binding/Binding.Constraints.Model.cs=6")] // R7-C3 (F's C2 rulings 272-273): a failed premise stays in the sets (its state)
    [InlineData("Counts of a view's state", "src/Kimi/Compiler/Binding", @"\b(?:Version|LinkVersion)\+\+", "Binding.Constraints.Model.cs=3;BindingModel.cs=1")] // R7-C3: FailPremise's change replaces Fail(Koto)'s (rulings 272-273)
    // R7-C3 cost (F's C2 ruling 318, C1): only the census's writers write the Failed state. ConstraintEnvironment.FailPremise is called
    // by Binding.FailPremise only, and NameUnprovenInput by FailInput only (Binding.premiseFailures). A direct call anywhere else would
    // leave the census at 0, and the per-proof readers would answer "nothing failed".
    [InlineData("Writers of the Failed state", "src/Kimi/Compiler", @"(?<!\bthis)\.(?:FailPremise|NameUnprovenInput)\(", "Binding/Binding.ConstraintValidation.cs=1;Binding/Binding.Diagnostics.Prerequisites.cs=1")]
    // Z3-B1 (SPEC 6.4.2 step 3, 8.11.4): a premise's Statements are recorded only by the two writers that hold another declaration's
    // premise under a substitution (ConstraintEnvironment's contract); a new copying writer whose premises carry bounds joins here.
    [InlineData("Writers of a premise's Statements", "src/Kimi/Compiler", @"\.AddStatement\(", "Binding/Binding.ContractMatching.cs=1;Binding/Binding.VirtualOverrides.cs=1")]
    // DECISIONS R7-C2e1: Completion's zero is Provisional and only the whitelist's members (W1-W8 and an outermost walk, the
    // ContractSolver header) give Complete, so a new route is Provisional until a reviewed row joins them: a further site fails here.
    // Every spelling of Complete is counted (an assignment, a return, an arm, an argument, a cast), the comparison in Reading included.
    // R7-C3a: W7, the identity classes' Completion member (DECISIONS R7-M20's amendment). R7-C2e2 e2-3a: W8, ReadSourceState
    // (SourceStates.cs); W6's meets in AvailableContractPremise and AvailableConstraintFact (ContractSolver.cs); and an outermost
    // walk's start and admission in each keeper, PremiseRefines and FirstRows (ContractSolver.Rows.cs).
    // R7-C2c2 c2-4a (review-impl-c24a B1, a row for the review): MemberOf's Complete where the view has no identity premise (no
    // table is read); with one it gives the table's W7.
    // R7-C2c2 c2-4b (design R3.3): ClassForm's Complete where the view has no identity premise (with one it gives the table's W7, as
    // MemberOf); SubstituteIdentity's and IdentityPremise's (TypeIdentity.cs's two; W2, W3, which W7 replaces, DECISIONS R7-M20) went
    // with those members (a removed subject, not a weakened rule).
    [InlineData("Answers given Complete", "src/Kimi/Compiler", @"\bCompletion\.Complete\b|\(Completion\)", "Binding/Binding.AssociatedTypes.Specification.cs=1;Binding/Binding.BoundContracts.cs=2;Binding/Binding.BuiltinArithmetic.cs=1;Binding/Binding.Constraints.cs=2;Binding/Binding.ContractSolver.Rows.cs=4;Binding/Binding.ContractSolver.cs=4;Binding/Binding.ContractTypes.cs=3;Binding/Binding.FixedArrays.cs=1;Binding/Binding.SourceStates.cs=1;Binding/Binding.TypeIdentity.Classes.cs=1;Binding/Binding.TypeIdentity.Keys.cs=2")]
    // R7-C2c2 c2-4a (design §4's Owner row, review-impl-c24a N4): an identity class's Member reaches the judgments only through
    // MemberOf, which the Contract judge, the capability judge, JudgeConstraintAtom's other arms and ContractTypeCore's (E) arm read;
    // IdentityClasses.Member's other reader is Refuted; R7-C3 C7: the capability works' key reads it once more (JudgedCapabilityKey, the
    // subject the judge judged). A further reader fails here until its row is reviewed.
    [InlineData("Readers of a class's Member", "src/Kimi/Compiler", @"\bMemberOf\(", "Binding/Binding.Capabilities.cs=2;Binding/Binding.Constraints.cs=1;Binding/Binding.ContractTypes.cs=1;Binding/Binding.Contracts.cs=1;Binding/Binding.TypeIdentity.Keys.cs=1")]
    // R7-C2c2 c2-4b (design R4.12b, R4.12c; ClassForm's contract in Keys.cs): an identity class's one form reaches Types only through
    // ClassForm, read by the one intake of each judgment over identical Types: J1 a proposition's identity (Constraints.cs:
    // NormalizedForm, NormalizeProofConstraint's subject, required Type and Contract reference (NormalizedContract, which ProveConformance
    // reads too, R7-C2c2 c2-5c); Contracts.cs: ProveConformance's subject), J3 the Type relation at a use (TypeRelations.Walk.cs: the walk
    // root's two sides; CallableSelection.cs:
    // CallableContractType; a failed fit's record and CheckTypeUse read the pair the walk's intake published, F's C2 rulings 307 (2),
    // 315), J4 a slot's bindings agree (CallSlots.cs: the evidence at Infer's head and the kept binding in its
    // bound-slot branch; J4's parameter side, a pattern's Type parameter that is no slot of the solve, a nested callee's enclosing
    // parameter: R7-C3, F's C2 rulings 276 (1), 278), and (ii) a use's binding
    // input (Expressions.cs: the expected and the bound Type; Variables.cs; Conversions.cs: a conversion's written target,
    // BindConversionTarget, PLAN G1240), and (iii) a member step's receiver (R7-N1 c-b: MemberLookup.cs, QualifierSelection's
    // receiver Type; ResolvePath.cs, EvaluateProjection's normal form), and (iv) SPEC 8.3.3's expanding recursion (G563-C, F's
    // G563C-1 Q1 (a), G563C-3: Contracts.Expansion.cs, a Contract's parameters and each supplied reference in its view, through J1's
    // NormalizedForm); Keys.cs is ClassForm's
    // own recursion. J2 reads the classes, no form. NormalizedForm, J1's reading through ClassForm, is counted with it, so its callers
    // are reviewed rows too (G563-C's implementation review N2, F's G563C-6): Constraints.cs 5 (NormalizedForm's declaration and its
    // ClassForm, NormalizeProofConstraint's subject and required Type, NormalizedContract's reference), Contracts.Expansion.cs 2 ((iv): the parameters and each supplied reference).
    // A further reader fails here until its row is reviewed.
    [InlineData("Readers of a class's form", "src/Kimi/Compiler", @"\b(?:ClassForm|NormalizedForm)\(", "Binding/Binding.CallSlots.cs=3;Binding/Binding.CallableContracts.cs=4;Binding/Binding.CallableSelection.cs=1;Binding/Binding.Constraints.cs=5;Binding/Binding.Contracts.Expansion.cs=2;Binding/Binding.Contracts.cs=1;Binding/Binding.Conversions.cs=1;Binding/Binding.Expressions.cs=2;Binding/Binding.InlineLayouts.cs=1;Binding/Binding.MemberLookup.cs=1;Binding/Binding.OriginInheritance.cs=4;Binding/Binding.ResolvePath.cs=1;Binding/Binding.Structs.cs=1;Binding/Binding.TypeIdentity.Keys.cs=5;Binding/Binding.TypeRelations.Walk.cs=2;Binding/Binding.Variables.cs=1")]
    // H11b-0 B0 (RP6): an Origin declaration's state is private; its formation alone names `Forming`, file by file, each occurrence
    // forming the declaration it names (the type's file: the member and its contract's three mentions). Every other read is one of
    // OriginDeclaration's read members, so the readers of a declaration, a failed one included, are those members' callers. R7-O2 A4-L:
    // `Binding.LocalOrigins.cs`'s are the initializer's and the annotation's formation alone, which give the omitted slots regions and
    // set the declarations' state (30 -> 12: SolveLocalBounds, the `let` branch's publish loop and the `Inferred` side table went); a
    // `let` region's value is published by FixDeclaredRegions (Binding.LocalRegions.cs), which reads the read members `Relations` and
    // `SubstitutionPairs`, so that file has no row.
    // PLAN G1369: `Binding.OriginDeclarations.Failures.cs` lost the dead first-failure copy (5 -> 4); `Binding.OriginDeclarations.cs`'s
    // SubstituteOriginEqualities reads the completed relations and counts a local's fixed regions (FixedRegions; 29 -> 28).
    [InlineData("Formation of an Origin declaration", "src/Kimi/Compiler", @"\bForming\b", "Binding/Binding.LocalOrigins.cs=12;Binding/Binding.OriginDeclarations.Failures.cs=4;Binding/Binding.OriginDeclarations.cs=28;Binding/Binding.OriginInheritance.cs=6;Binding/Binding.Origins.Model.cs=4")]
    // PLAN G949 S1-0b r2 and r3 (the library-layer dump's number rule, KimiLibraryShapeTest.LibraryLayer): a number naming a walk's or
    // memo's order, or a change counter, is read only by the walk or memo that assigns it. Each row counts every name through which the
    // number is reached, its holder and a member returning it included:
    // - a scope's view serial and the chain state it names: declared in BindingModel and read by ContractSolver's SerialOf, whose callers
    //   compare the serial for equality only (ContractSolver, its Rows and TypeIdentity's memo). From PLAN G949 S1-1 the library pass
    //   settles every library scope's serial before the freeze (SettleViewSerials) and the dump compares it by value, so w1 sees a later
    //   step's write to a library scope's (G949 ruling 205 C2);
    // - an Origin requirement work's Consumer mark: read by OriginRequirements' edge collection; compared by value from S1-1 too, since a
    //   frozen library work is never active, so no later call marks it;
    // - an inline summary's Tarjan Index and Low: read in InlineLayouts, the one file that names the summary, its stores or Summary, the
    //   member returning one (ComponentSummaries' own Summary shares the name and is counted too); the dump prints them as # and compares
    //   the rest of their row. All are Binding's private members, so only Binding's partial files, in its directory, reach them.
    // A further reader fails here until its use is reviewed (equality only for a serial or a mark; the dump's # rule for a Tarjan number).
    [InlineData("Readers of a scope's view serial and state", "src/Kimi/Compiler", @"\bView(?:Serial|State)\b|\bSerialOf\b", "Binding/Binding.ContractSolver.Rows.cs=3;Binding/Binding.ContractSolver.cs=12;Binding/Binding.TypeIdentity.cs=3;Binding/BindingModel.cs=3")]
    [InlineData("Readers of an Origin requirement work's Consumer mark", "src/Kimi/Compiler", @"\.Consumer\b|\bConsumer:", "Binding/Binding.OriginRequirements.cs=2")]
    [InlineData("Holders of an inline summary's Tarjan numbers", "src/Kimi/Compiler/Binding", @"\b(?:InlineSummary|inlineSummaries|inlineSummaryPool|inlineComponents|Summary)\b", "Binding.ComponentSummaries.cs=7;Binding.InlineLayouts.cs=43")]
    // PLAN G949 S1-0b r3 (the dump's epoch rule): the memos of one capability epoch are the program's because each pass begins with
    // ResetCapabilities: its one call is ResetPass's, which the pass driver's RunPass runs first in each pass of each run, the library
    // pass's and the program pass's, and ResetPasses for each pass on an edit (Invalidate; Binding.Passes.cs; G949 S1-r1a and S1-r1b, the
    // premise reviewed there: an epoch store a pass reads is its own part's or ends at that pass's start, so after a Bind the program
    // pass's epoch is the one the shared stores hold, and the layer's part's closure index the library pass's). A further caller fails
    // here until the epoch rule's premise is reviewed again. Binding's own ResetPass calls are this.ResetPass (a member call's spelling);
    // the conformance identities' and paths' ResetPass(ulong) (S1-b1) are other members of the name. PLAN G949 S1-1 (B4): an edit's
    // revocation resets the program pass alone, in Invalidate (Binding.cs), where S1-r1b's ResetPasses reset both passes in the driver.
    [InlineData("A pass's start ends the capability epoch", "src/Kimi/Compiler", @"\bResetCapabilities\(|\bthis\.ResetPass\(", "Binding/Binding.Capabilities.cs=1;Binding/Binding.Pass.cs=1;Binding/Binding.Passes.cs=1;Binding/Binding.cs=1")]
    // PLAN G949 S1-r1b (review-r1a RC1, review-s1r1 Re-check 3 RC-c): the running part's bounded Callable clauses are written by their
    // owner (Binding.EffectDeclarations.cs) and handed over by the driver (Binding.Passes.cs); every reader reads the pass's view
    // (selectableEffectClauses), which holds the library layer's clauses too. A read of the running part alone (CollectCallableEffectUses'
    // guard, AvailableCallableEffects) would skip a library callee's bounded clause in a program that states none, which no program
    // can show until the library declares one (KimiLibraryShapeTest.TheLibraryDeclaresNoBoundedCallableClause).
    [InlineData("The running part's Callable effect clauses are read through the pass's view", "src/Kimi/Compiler", @"\bthis\.callableEffectClauses\b", "Binding/Binding.EffectDeclarations.cs=2;Binding/Binding.Passes.cs=1")]
    // PLAN G949 S1-r1b (review-s1r1b N-c): AvailableEffectBounds reads the composed view of every pass's effect bounds, which holds the
    // program part of the previous Bind while the library pass runs, so only readers after the Bind call it: EffectObligations (MIR's
    // effect solve), SemanticQuery and the hover (and its own overload). An in-pass caller fails here; it takes the pass's view, as
    // selectableEffectClauses does for the Callable clauses (PassPart's in-pass rule).
    [InlineData("Every pass's effect bounds are read after the Bind", "src/Kimi", @"\bAvailableEffectBounds\(", "Checking/Hover/HoverBuilder.Effects.cs=2;Compiler/Binding/Binding.EffectDeclarations.cs=3;Compiler/Binding/Binding.EffectObligations.cs=2;Compiler/Binding/SemanticQuery.cs=2")]
    // PLAN G949 S1-r1b (rule (a), F's ruling 170): inside a pass Binding reads a conformance identity's paths only through ImplTable.PathsOf,
    // the one read that applies the running pass's admission to a library identity (from S1-r1b's split its point-1 paths while the
    // conditional registrar has not run: OwningIterator's and BorrowingIterator's LendingIterator identity gains its Iterator ancestor's path
    // at point 2). PathStorage is ImplTable's and the model's own; the public Paths serves readers after the Bind and tests. An inference
    // batch's Paths (a batch's, not an identity's) is read through a batch (R7-C4 removed the last other match).
    [InlineData("Binding reads an identity's paths through ImplTable.PathsOf", "src/Kimi/Compiler/Binding", @"\.PathStorage\b|(?<!InPass|impls|batch)\.Paths\b|\{ (?:Paths|PathStorage)(?:\.|:)", "Binding.Contracts.Model.cs=7;ImplTable.cs=4")]
    // PLAN G949 Stage 2 (R8-L1 under O-381 (b)): a reader reaches its Binding's facts through its own Binding, never through a node's
    // compilation, so a compilation's MIR builder can read library nodes after the freeze's Binding is dropped. The route's sites, file
    // by file: the route units lowered them to CodeContext's and the parser's, which R7 owns.
    [InlineData("Routes from a node to its compilation", "src/Kimi", @"CodeContext\.Compilation|Kotonoha\.Compilation", "Compiler/Core/CodeContext.cs=1;Compiler/Parsing/Koto/Declarations/DeclarationContainers/DeclarationContainerKoto.cs=2;Compiler/Parsing/Koto/Koto.Ownership.cs=3;Compiler/Parsing/Parser.Directives.cs=3;Compiler/Parsing/Parser.PrimaryExpressions.cs=3")]
    // R7-O1 c (RP6; DECISIONS R7-M66, R7-M120, R7-M123): Binding judges no body relation, so the relation judge is read only by OJ1, the
    // declaration frame (JudgeDeclarationPosition's two directions, JudgeOriginObligation's two), OJ3, Owned's static check
    // (RefutedStatic), and OJ4, a Callable comparison (the walk's OriginFailure, ComparedRegionLimit, LeavesBodyRows), besides its
    // declaration and its meet recursion; and Binding constructs an Origin relation record only at V5 (the declaration judge's two), V6
    // (an Unstated end; its other direction, in the same conditional, is target-typed), V7 (a holder place) and the Owned record (OJ3's
    // judged `origin outlives static`, R7-M123). A further site fails here until its row is reviewed.
    [InlineData("The relation judge's call sites (OJ1, OJ3, OJ4)", "src/Kimi/Compiler/Binding", @"\bJudgeOriginRelation\(", "Binding.OriginClauses.cs=2;Binding.OriginProof.cs=6;Binding.OwnedConversions.cs=1;Binding.TypeRelations.Walk.cs=1")]
    [InlineData("Binding's Origin relation records (V5, V6, V7)", "src/Kimi/Compiler", @"\bnew OriginRelationFact\(", "Binding/Binding.Obligations.cs=1;Binding/Binding.OriginClauses.cs=2;Binding/Binding.OwnedConversions.cs=1;Binding/Binding.RawOrigins.Holder.cs=1")]
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
            "Parsing/Parser.ControlFlow.cs|ParseForSlot|\"_\"",
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
            .SelectMany(MethodCalls).Where(static x => x.Called is { Name: "GetEnumerator", DeclaringType.IsInterface: true }).Select(static x => x.Site)
            .GroupBy(static x => x, StringComparer.Ordinal).Select(static x => $"{x.Key}={x.Count()}")
            .Order(StringComparer.Ordinal).ToArray();
        Assert.True(sites.Split(';').Order(StringComparer.Ordinal).SequenceEqual(actual), $"Interface enumerations: {string.Join(";", actual)}; the sites are {sites}.");
    }

    // R7-N5, PLAN G949 S1-N5 and G1314 (DECISIONS BU-220; COMPILER_ARCHITECTURE's Syntax identity): a node-keyed table outside Syntax is a
    // field of a keyed collection (a dictionary, a set or a weak table) whose key's equality reaches a node: a Koto type or an interface
    // one implements, object, or a Kimi type, value tuple or Nullable that is a value type or has its own Equals, one of whose fields
    // reaches one (ShapePair, BindingObligation). Read from the compiled fields, so a named key type hides no node; a field of
    // a constructed Kimi generic type is read through its fields. A class with reference equality (BindingSymbol, BoundType) is its own
    // identity and a SourceRef key a node's value identity, no node; a BCL struct's internals never count. Not seen: a node in a BCL key's
    // generic arguments (KeyValuePair, Tuple) or a node-keyed collection held as a value or an element; review reads those. Counted by
    // top-level type (NodeKeyedTableSites); each batch lowers its type's count, and R7-N5's exit, Binding's types at 0, was reached by R7-E4 E4e.
    [Theory]
    [InlineData("Kimi", NodeKeyedTableSites)]
    public void NodeKeyedTablesStayAtTheirSites(string prefix, string sites)
    {
        const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var assembly = typeof(Kimi.Compiler.Compilation).Assembly;
        var types = assembly.GetTypes();
        var nodes = types.Where(static x => typeof(Kimi.Compiler.Parsing.Koto).IsAssignableFrom(x)).SelectMany(static x => x.GetInterfaces().Append(x)).ToHashSet();
        var actual = types
            .Where(x => x.Namespace?.StartsWith(prefix, StringComparison.Ordinal) == true && x.Namespace?.StartsWith("Kimi.Compiler.Parsing", StringComparison.Ordinal) != true && !x.Name.StartsWith('<'))
            .SelectMany(x => x.GetFields(Declared).Where(f => HoldsNodeKeys(f.FieldType, 0)).Select(_ => TopLevel(x).Name))
            .GroupBy(static x => x, StringComparer.Ordinal).Select(static x => $"{x.Key}={x.Count()}").Order(StringComparer.Ordinal).ToArray();
        Assert.True(sites.Split(';').Order(StringComparer.Ordinal).SequenceEqual(actual), $"Node-keyed tables: {string.Join(";", actual)}; the sites are {sites}.");

        static Type TopLevel(Type type) => type.DeclaringType is { } outer ? TopLevel(outer) : type;

        // A keyed collection whose key reaches a node, or a constructed Kimi generic type holding one (two levels: TypeMemo, StampedColumn).
        bool HoldsNodeKeys(Type type, int depth)
            => KeyOf(type) is { } key ? ReachesNode(key, [])
                : depth < 2 && type.IsConstructedGenericType && type.Assembly == assembly && type.GetFields(Fields).Any(x => HoldsNodeKeys(x.FieldType, depth + 1));

        static Type? KeyOf(Type type)
            => !type.IsConstructedGenericType ? null
                : type.GetGenericTypeDefinition() == typeof(System.Runtime.CompilerServices.ConditionalWeakTable<,>) ? type.GetGenericArguments()[0]
                : type.GetInterfaces().Append(type).FirstOrDefault(static x => x.IsConstructedGenericType && KeyedInterfaces.Contains(x.GetGenericTypeDefinition()))?.GetGenericArguments()[0];

        bool ReachesNode(Type key, HashSet<Type> seen)
            => key == typeof(object) || nodes.Contains(key) ||
                (!key.IsGenericParameter && !key.IsPrimitive && !key.IsEnum &&
                    (key.Assembly == assembly || key.FullName?.StartsWith("System.ValueTuple", StringComparison.Ordinal) == true || Nullable.GetUnderlyingType(key) is not null) &&
                    (key.IsValueType || key.GetMethod("Equals", [typeof(object)])?.DeclaringType != typeof(object)) && seen.Add(key) &&
                    key.GetFields(Fields).Any(x => ReachesNode(x.FieldType, seen)));
    }

    // G949 D2b-d's d13 (DECISIONS BU-220): NodeColumns holds Binding's private node families, read and written only through each
    // family's accessor, so no source file outside Compiler/Binding names it.
    [Fact]
    public void NodeColumnsIsNamedOnlyInBinding()
    {
        var binding = Path.Combine(Repository.Root, "src", "Kimi", "Compiler", "Binding") + Path.DirectorySeparatorChar;
        var name = new Regex(@"\bNodeColumns\b");
        Assert.DoesNotContain(SourceFiles("src", "*.cs"), x => !x.StartsWith(binding, StringComparison.OrdinalIgnoreCase) && name.IsMatch(File.ReadAllText(x)));
    }

    // PLAN G949 S1-b2 (d11; DECISIONS BU-220): every Type carries the dense id its maker gave it, so only TyCtxt, which numbers each Type it
    // makes (Intern, Parameter), and BoundType, whose statics hold one fixed list of ids (its type initializer, CreatePrimitives and
    // CreateWrappingScalars), construct one, each site as many times as listed (the type initializer's four: Unit, Never, Boolean, Range),
    // so a construction added inside an allowed method fails too. Read from the IL, where a target-typed `new(...)` is a constructor
    // call like any other.
    [Fact]
    public void OnlyTyCtxtAndBoundTypeConstructTypes()
    {
        var sites = typeof(Kimi.Compiler.Compilation).Assembly.GetTypes().SelectMany(MethodCalls)
            .Where(static x => x.Called is ConstructorInfo && x.Called.DeclaringType == typeof(Kimi.Compiler.BoundType))
            .GroupBy(static x => x.Site, StringComparer.Ordinal).Select(static x => $"{x.Key}={x.Count()}").Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(
            new[] { "BoundType..cctor=4", "BoundType.CreatePrimitives=1", "BoundType.CreateWrappingScalars=1", "TyCtxt.Intern=1", "TyCtxt.Parameter=1" }, sites);
    }

    // R7-C2a: Enumerate is a leaf of the judgment graph (Binding.ContractSolver.cs's contract). The members of its walk call exactly
    // the names listed here, none of which proves, resolves a binding, normalizes or requests a capability (ProveConformance,
    // ProveConstraint, ResolveAssociated, NormalizeProof*, ClassForm, RequestCapability), and the two callees that can
    // normalize are told not to. A new callee is read for judgments before it joins the list. PremiseRefines, FirstRows, PremiseRows,
    // ExactAssumption and the indexes are the request boundary (Binding.ContractSolver.Rows.cs), not the walk; a walk that reaches a
    // request fails at run time (InternalInvariant). R7-C2c2 c2-3b: the walk's row atom (RowAtom, c2-5b) and row merge (AddRow) are members.
    // G1407: row (2)'s intake of one source row's supply (AddSupplyRows) is a member; the conjunct split it calls (AddConjuncts, in
    // Binding.Constraints.cs) only appends a constraint and its conjuncts. R7-C2c2: the walk's class
    // keys (SameClass, and ClassKey and CongruentKey in Binding.TypeIdentity.Keys.cs) are walk members too, so no judgment is reached from
    // a key: they read the built table (KeyOf, Representative) and intern (Rebuild).
    [Fact]
    public void EnumeratesWalkCallsNoJudgment()
    {
        string[] walk =
        [
            "Enumerate", "EnumerateLink", "EnumerateMates", "HeadDepth", "ClauseHeads", "AvailableContractPremise", "AvailableConstraintFact", "ContractSupply", "SameClass", "ClassKey", "CongruentKey",
            "RowContract", "RowAtom", "AddRow", "AddSupplyRows", "HoldsOpenLink", "RowWeight",
        ];
        string[] allowed =
        [
            "Add", "AddConjuncts", "AddRow", "AddSupplyRows", "Applied", "AppliedAssociatedContract", "AsSpan", "AssociatedIdentityMatches", "AvailableConstraintFact",
            "AvailableContractPremise", "CauseOf", "ChecksOwnSite", "ClassKey", "ClauseHeads", "CongruentKey", "Contains", "ContractConstraint", "DependentConstraint",
            "DerivedFailure", "Enumerate", "EnumerateLink", "EnumerateMates", "FactStates", "GetValueOrDefault", "HeadDepth", "Heads", "HoldsOpenLink",
            "InstantiatedContract", "InvalidDeclarationContext", "IsContractSelf", "IsFailed", "KeyOf", "Max", "Meet",
            "ProjectionMembers", "ReadSourceState", "Rebuild", "ReferenceEquals", "RemoveAt", "RemoveRange", "RentTypes", "Representative", "Return",
            "RowAtom", "RowContract", "RowWeight", "SameClass", "SetKey", "SubjectKey", "TryGetValue", "UnresolvedTypeDeclarationContext",
            "WalkAvailable",
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

    // R7-C3 C3c (RP6; DECISIONS R7-M24, R7-M63; the Front's C3 design §3 and §7 item 2): SPEC 8.2.3's identity of two Types in a view
    // is the judge's (Binding.TypeIdentity: IdentityProven, IdentityRefuted, and ClassForm at the intakes of the judgments over
    // identical Types). Types are interned, so every other comparison of two Types is an equality as written, which answers SPEC's
    // question only where its reason holds. Each comparison outside the judge's files stays at its site under one reason
    // (TypeComparisonReasons); a further site fails here until its row is reviewed. Read by the semantic model: `==`, `!=`,
    // ReferenceEquals and Equals whose two operands are Types (a null literal is none), SameCompleteType, and a library search of Types
    // for a Type (Contains, IndexOf, LastIndexOf, SequenceEqual, Remove), each counted at its file and member (a lambda's or local
    // function's in its declaring member), a conditional access's too (`a?.Equals(b)`, `xs?.Contains(t)`). A candidate the model does
    // not resolve (an error-typed operand, or an Equals, ReferenceEquals or search call without its method) fails here, naming its site.
    // Not seen, so review reads them: a Type as a key (a Dictionary's or HashSet's lookup, a tuple's or record's generated equality), an
    // equality through a comparer or an object-typed operand.
    [Fact]
    public void TypeComparisonsOutsideTheJudgeStayAtTheirSites()
    {
        var rows = TypeComparisonSites.Select(static x => x.Split(' ')).ToArray();
        Assert.All(rows, static x => Assert.True(x.Length == 2 && TypeComparisonReasons.Contains(x[1]), $"A row names one reason: {string.Join(' ', x)}."));
        var expected = rows.Select(static x => x[0].Split('=')).GroupBy(static x => x[0], StringComparer.Ordinal)
            .Select(static x => $"{x.Key}={x.Sum(static y => int.Parse(y[1], System.Globalization.CultureInfo.InvariantCulture))}").Order(StringComparer.Ordinal).ToArray();
        var sites = TypeComparisons();
        var unresolved = sites.Where(static x => x.Unresolved).Select(static x => x.Site).Distinct().Order(StringComparer.Ordinal).ToArray();
        Assert.True(unresolved.Length == 0, $"Candidate Type comparisons the semantic model does not resolve: {string.Join(";", unresolved)}.");
        var actual = sites.GroupBy(static x => x.Site, StringComparer.Ordinal).Select(static x => $"{x.Key}={x.Count()}").Order(StringComparer.Ordinal).ToArray();
        Assert.True(
            expected.SequenceEqual(actual),
            $"Type comparisons outside the judge: new or changed {string.Join(";", actual.Except(expected))}; the rows say {string.Join(";", expected.Except(actual))}.");
    }

    // The comparisons of two Types in Binding outside the judge's files (Binding.TypeIdentity.cs and its Binding.TypeIdentity.<Concern>.cs),
    // one `File:Member` per comparison, read by Roslyn's semantic model over the compiler's sources; Unresolved marks a candidate the model
    // does not resolve (the source generators' members are absent, so an operand that is one is error-typed).
    private static (string Site, bool Unresolved)[] TypeComparisons()
    {
        var options = new CSharpParseOptions(LanguageVersion.Preview);
        var root = Path.GetFullPath(Path.Combine(Repository.Root, "src", "Kimi"));
        var trees = SourceFiles("src/Kimi", "*.cs").Select(Path.GetFullPath)
            .Where(x => Path.GetRelativePath(root, x).Split(Path.DirectorySeparatorChar)[0] is not ("bin" or "obj"))
            .AsParallel().Select(x => CSharpSyntaxTree.ParseText(File.ReadAllText(x), options, x, cancellationToken: TestContext.Current.CancellationToken))
            .Append(CSharpSyntaxTree.ParseText(
                "global using System; global using System.Collections.Generic; global using System.IO; global using System.Linq; " +
                "global using System.Net.Http; global using System.Threading; global using System.Threading.Tasks;",
                options,
                cancellationToken: TestContext.Current.CancellationToken))
            .ToArray();
        var kimi = typeof(Kimi.Compiler.Compilation).Assembly.Location;
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(x => !string.Equals(x, kimi, StringComparison.OrdinalIgnoreCase)).Select(static x => MetadataReference.CreateFromFile(x));
        var compilation = CSharpCompilation.Create(
            "Kimi", trees, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable));
        var type = compilation.GetTypeByMetadataName("Kimi.Compiler.BoundType") ?? throw new InvalidOperationException("No BoundType.");
        var binding = Path.Combine(Repository.Root, "src", "Kimi", "Compiler", "Binding") + Path.DirectorySeparatorChar;
        return trees.Where(x => x.FilePath.StartsWith(binding, StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(x.FilePath).StartsWith("Binding.TypeIdentity.", StringComparison.Ordinal))
            .AsParallel().SelectMany(x => Sites(x, compilation.GetSemanticModel(x))).ToArray();

        IEnumerable<(string Site, bool Unresolved)> Sites(SyntaxTree tree, SemanticModel model)
        {
            foreach (var node in tree.GetRoot(TestContext.Current.CancellationToken).DescendantNodes())
            {
                // Null: a candidate the model does not resolve.
                bool? compares = node switch
                {
                    BinaryExpressionSyntax x when x.IsKind(SyntaxKind.EqualsExpression) || x.IsKind(SyntaxKind.NotEqualsExpression) => Types(x.Left, x.Right),
                    InvocationExpressionSyntax { ArgumentList.Arguments: [var a, var b] } x when Name(x.Expression) is "ReferenceEquals" or "Equals" => Resolved(x, Types(a.Expression, b.Expression)),
                    InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax or MemberBindingExpressionSyntax, ArgumentList.Arguments: [var a] } x when Name(x.Expression) is "Equals" =>
                        Resolved(x, Types(Receiver(x.Expression), a.Expression)),
                    InvocationExpressionSyntax x when Name(x.Expression) is "SameCompleteType" => true,
                    InvocationExpressionSyntax x when Name(x.Expression) is "Contains" or "IndexOf" or "LastIndexOf" or "SequenceEqual" or "Remove" =>
                        model.GetSymbolInfo(x, TestContext.Current.CancellationToken).Symbol is IMethodSymbol method
                            ? !SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly) && (method.TypeArguments.Any(Is) || method.ContainingType.TypeArguments.Any(Is) || method.Parameters.Any(p => Is(p.Type)))
                            : null,
                    _ => false,
                };
                if (compares is not false)
                {
                    yield return ($"{Path.GetFileName(tree.FilePath)}:{Member(node)}", compares is null);
                }
            }

            bool? Types(ExpressionSyntax left, ExpressionSyntax right)
            {
                if (left.IsKind(SyntaxKind.NullLiteralExpression) || right.IsKind(SyntaxKind.NullLiteralExpression))
                {
                    return false;
                }

                var leftType = model.GetTypeInfo(left, TestContext.Current.CancellationToken).Type;
                var rightType = model.GetTypeInfo(right, TestContext.Current.CancellationToken).Type;
                return leftType is { TypeKind: TypeKind.Error } || rightType is { TypeKind: TypeKind.Error } ? null : Is(leftType) && Is(rightType);
            }

            bool? Resolved(InvocationExpressionSyntax call, bool? compares) => model.GetSymbolInfo(call, TestContext.Current.CancellationToken).Symbol is null ? null : compares;
        }

        bool Is(ITypeSymbol? candidate) => SymbolEqualityComparer.Default.Equals(candidate, type);

        // A called name, a conditional access's member binding (`a?.Equals(b)`) included.
        static string? Name(ExpressionSyntax called) => called is MemberBindingExpressionSyntax binding ? binding.Name.Identifier.Text : CalledName(called);

        // An instance call's receiver: its member access's, or for a member binding its conditional access's.
        static ExpressionSyntax Receiver(ExpressionSyntax called) => called is MemberAccessExpressionSyntax access ? access.Expression
            : called.Ancestors().OfType<ConditionalAccessExpressionSyntax>().First(x => x.WhenNotNull.Span.Contains(called.Span)).Expression;

        static string Member(SyntaxNode node) => node.Ancestors().FirstOrDefault(static x => x is BaseMethodDeclarationSyntax or BasePropertyDeclarationSyntax or BaseFieldDeclarationSyntax) switch
        {
            MethodDeclarationSyntax x => x.Identifier.Text,
            ConstructorDeclarationSyntax x => x.Identifier.Text,
            PropertyDeclarationSyntax x => x.Identifier.Text,
            OperatorDeclarationSyntax x => "operator " + x.OperatorToken.Text,
            BaseFieldDeclarationSyntax x => x.Declaration.Variables[0].Identifier.Text,
            _ => "this[]",
        };
    }

    // Any receiver: a nested visitor calls its Binding by a field (`binding.TypeName`), which a `this.`-only match missed (round 2).
    private static string? CalledName(ExpressionSyntax called) => called switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
        IdentifierNameSyntax name => name.Identifier.Text,
        _ => null,
    };

    // The methods and constructors a type's own method bodies call, construct or load, read from their IL, each with its site
    // `TopLevelType.Method`.
    private static IEnumerable<(string Site, MethodBase Called)> MethodCalls(Type type)
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
                    is { } called)
                {
                    yield return ($"{top.Name}.{name}", called);
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
