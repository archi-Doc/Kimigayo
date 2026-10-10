// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591 // Shared ownership-plan vocabulary.

[Flags]
public enum PlaceState : byte
{
    None = 0,
    MustInit = 1,
    MayInit = 2,
    MayMoved = 4,
    MayAssigned = 8,

    // The Place may hold a value it owns, not only a Copy remnant left by a Copy-or-Move acquisition (whose destruction has
    // no effect). An effect bound counts a destruction only where this holds.
    MayOwn = 16,
}

public enum OwnershipPlaceKind : byte
{
    Local,
    Parameter,
    Temporary,
    Result,
    Payload,
    Subject,

    // SPEC 8.4.10.4: the Loans that the Origins of one abstract input Type may denote, which only the derived effects of
    // generic requirement calls access; no operation initializes or uses it.
    EffectRegion,

    // SPEC 5.2.2: the fresh anchor of a raw Place borrow, named by its Anchor Origin. Like the referent of a borrowed parameter it
    // is external: it holds no value, and its Loans are checked only against Places derived from it. No operation uses it.
    Anchor,
}

public enum PlaceUseKind : byte
{
    None,
    Read,
    Consume,
    Write,
    Borrow,
}

public enum AcquisitionKind : byte
{
    None,
    Copy,
    Move,
    CopyOrMove,
}

public enum OwnershipOperationKind : byte
{
    Entry,
    Declare,
    Produce,
    Read,
    Consume,
    Write,
    Borrow,
    CallEntry,
    Call,
    Cleanup,
    Deliver,
    Branch,
    Exit,
    PayloadPlacement,
    CompleteConstruction,
    InitializeSubject,
    DecomposeCase,
    AcquirePattern,
    MatchDispatch,
    PatternTest,
    EndComparisonLoans,
    ProjectElement,
    LocateReceiver,
    WriteElement,
    InitializeReceiverField,
    CheckReceiverField,
    WriteBorrowedField,
    UpdateTarget,
    ReplaceBorrowed,
    ExchangeBorrowed,
    SwapBorrowed,
    TestObserve,
    TestMessage,
    TestAbort,
    ActivateCallBorrows,
    StorePointer,
    CheckDictionaryKey,
    StoreDictionaryEntry,
}

public enum PlacementKind : byte
{
    None,
    Initialization,
    Reinitialization,
    Replacement,
    ConditionalReplacement,
    EmptyPlacement,
}

public enum CleanupAction : byte
{
    Skip,
    Destroy,
    Conditional,
}

public enum CleanupReason : byte
{
    ExpressionEnd,
    ScopeExit,
    Return,
    LoopTransfer,
    Replacement,
    SelectionResult,
}

public enum OwnershipEdgeKind : byte
{
    Normal,
    True,
    False,
    Back,
    Return,
    Abort,
    MatchArm,
    Unmatched,
}

public enum OwnershipFailure : byte
{
    UninitializedUse,
    PossiblyMovedUse,
    ReassignedLet,
    Unsupported,
    ExpansionLimit,
    ComparisonLoanConflict,
    DefaultArgumentMove,
    DefaultArgumentAccess,
    DefaultArgumentBorrow,
    Internal,

    // SPEC 15.1.5: a bare Place never Moves; a Non-Copy or Copy-unproven Place needs @move.
    TransferRequired,

    // SPEC 15.4.4: an Origin obligation that neither Binding nor this analysis proves, such as an Origin nothing constrains.
    UnprovenOrigin,

    // SPEC 8.4.5, 22.1.2.4: the destructions an Iterator.next or BufferWriter.reserve implementation performs exceed the
    // published effect bound of its Contract.
    EffectBound,
    CallableEffectBound,
    VirtualEffectBound,

    // SPEC 4.6.1, 15.1.3: an element is moved only through a static Move Path: a nonnegative integer-literal index within an
    // owned fixed array.
    StaticMovePathRequired,

    // SPEC 8.4.10.4, 8.4.10.6: a generic requirement call's derived effects may conflict with a Loan an earlier result keeps.
    CallEffectConflict,

    StorageLimit,

    // SPEC 8.10, 23.3.6.1: the Semantics cases of a definition exceed the implementation bound (OwnershipAnalysis.CaseBound).
    CaseLimit,
}

public readonly record struct OwnershipPlace(int Id, Koto Source, BoundType Type, OwnershipPlaceKind Kind, bool Mutable, AcquisitionKind Acquisition)
{
    // Deferred bodies share syntax, but each expansion has distinct temporary storage.
    internal int DeferredExecution { get; init; } = -1;

    internal int DefaultContext { get; init; } = -1;

    internal bool CleanupObservesBorrows { get; init; }
}

/// <summary>One CFG program point; Place/Input are IDs in its body's Place table.</summary>
public readonly record struct OwnershipOperation(OwnershipOperationKind Kind, Koto Source, int Place = -1, int Input = -1, AcquisitionKind Acquisition = AcquisitionKind.None, PlacementKind Placement = PlacementKind.None, LoanRequirement LoanMode = LoanRequirement.None, int Projection = -1, int Reservation = -1)
{
    public PlaceUseKind Use => this.Kind switch
    {
        OwnershipOperationKind.Read or OwnershipOperationKind.PatternTest => PlaceUseKind.Read,
        OwnershipOperationKind.Consume or OwnershipOperationKind.AcquirePattern or OwnershipOperationKind.StorePointer => PlaceUseKind.Consume,
        OwnershipOperationKind.Write or OwnershipOperationKind.WriteElement or OwnershipOperationKind.PayloadPlacement => PlaceUseKind.Write,
        OwnershipOperationKind.Borrow or OwnershipOperationKind.UpdateTarget => PlaceUseKind.Borrow,
        _ => PlaceUseKind.None,
    };

    internal bool IsWholeUpdate => this.Kind is OwnershipOperationKind.ReplaceBorrowed or OwnershipOperationKind.ExchangeBorrowed or OwnershipOperationKind.SwapBorrowed;
}

public readonly record struct OwnershipEdge(int From, int To, OwnershipEdgeKind Kind, int Next);

public readonly record struct OwnershipCleanupStep(int Operation, int Place, Koto Source, CleanupAction Action);

/// <summary>A cleanup sequence on an edge, in execution order, indexed into CleanupSteps.</summary>
public readonly record struct OwnershipCleanupPlan(int Edge, int Start, int Count, CleanupReason Reason);

/// <summary>One deferred execution, including empty bodies; endpoints are explicit CFG operations.</summary>
public readonly record struct OwnershipDeferredPlan(Koto Source, int Edge, int Entry, int Continuation, int End, int Parent, bool CanComplete);

/// <summary>One construction's N-to-one responsibility transfer; payload Places are contiguous. Case is null for tuples and fixed arrays.</summary>
public readonly record struct OwnershipConstructionPlan(int Place, BoundEnumCase? Case, int PayloadStart, int PayloadCount);

/// <summary>Analysis fans out to every arm; runtime selection tests these Patterns in source order.</summary>
public readonly record struct OwnershipMatchPlan(BoundMatch Binding, int Subject, int Result, int ArmStart, int ArmCount);

/// <summary>One successful Pattern test and its ownership decomposition before the arm body.</summary>
public readonly record struct OwnershipMatchArmPlan(int Match, int Pattern, int Test, int DecompositionStart, int DecompositionCount,
    int GuardEntry = -1, int GuardBranch = -1, int BodyEntry = -1, int GuardValue = -1, int GuardCleanupStart = -1, int GuardLoan = -1);

/// <summary>A reserved input: its lending point and the invocation it prepares (SPEC 15.6.7).</summary>
public readonly record struct OwnershipLending(Koto Input, Koto Call);

// Input is the record's own reserved input; ConflictingReservation is the earlier reservation the operation conflicts with.
// Destroyed names the borrowed Place a destruction ends while a live value keeps its Loan (empty for a Place without a name, such
// as DestroyedTemporary); Borrow is the Borrow that created the Loan, with BorrowCapture its capture entry when it is a closure's
// (SPEC 15.6.2, 16.2.2). Cases is the set of Semantics cases the problem was found under, one bit per case of the function's
// enumeration (SPEC 8.10, 23.3.6.4); empty outside a case run and once every case found the problem.
public readonly record struct OwnershipIssue(Koto Source, OwnershipFailure Failure, int Place = -1, int Reservation = -1, bool Activation = false, Koto? LoanSource = null,
    string? StorageTable = null, long RequiredBytes = 0, long LimitBytes = 0, int Capture = -1, Koto? Related = null,
    OwnershipLending? Input = null, OwnershipLending? ConflictingReservation = null, BindingObligation? Obligation = null,
    Koto? Borrow = null, int BorrowCapture = -1, string? Destroyed = null, Koto? DestroyedTemporary = null, ulong Cases = 0)
{
    public DiagnosticCode Code => this.Failure switch
    {
        OwnershipFailure.UninitializedUse => DiagnosticCode.UninitializedPlace_Kd,
        OwnershipFailure.PossiblyMovedUse => DiagnosticCode.MovedPlace_Kd,
        OwnershipFailure.ReassignedLet => DiagnosticCode.ReassignedLet_Kd,
        OwnershipFailure.ExpansionLimit => DiagnosticCode.DeferredExpansionLimit_Kd,
        OwnershipFailure.ComparisonLoanConflict => this.Activation ? (this.Source is InvocationKoto ? DiagnosticCode.CallActivationConflict_Kd : DiagnosticCode.PlacementActivationConflict_Kd) :
            this.Reservation >= 0 ? DiagnosticCode.CallReservationConflict_Kd : DiagnosticCode.ComparisonLoanConflict_Kd,
        OwnershipFailure.DefaultArgumentMove => DiagnosticCode.DefaultArgumentMove_Kd,
        OwnershipFailure.DefaultArgumentAccess => DiagnosticCode.DefaultArgumentAccess_Kd,
        OwnershipFailure.DefaultArgumentBorrow => DiagnosticCode.DefaultArgumentBorrow_Kd,
        OwnershipFailure.TransferRequired => DiagnosticCode.TransferRequired_Kd,
        OwnershipFailure.UnprovenOrigin => this.Obligation is { Kind: BindingObligationKind.OriginOutlives, Longer: { } longer, Shorter: { } shorter }
            ? Binding.RefutesOriginRelation(longer, shorter) ? DiagnosticCode.UnsatisfiedOriginRelation_Kd : DiagnosticCode.UnprovenOriginRelation_Kd
            : DiagnosticCode.UnprovenConstraint_Kd,
        OwnershipFailure.Internal => DiagnosticCode.InternalInvariant_Kd,
        OwnershipFailure.EffectBound => DiagnosticCode.IncompatibleContractImplementation_Kd,
        OwnershipFailure.CallableEffectBound => DiagnosticCode.UnsatisfiedEffectBound_Kd,
        OwnershipFailure.VirtualEffectBound => DiagnosticCode.UnsatisfiedEffectBound_Kd,
        OwnershipFailure.StaticMovePathRequired => DiagnosticCode.StaticMovePathRequired_Kd,
        OwnershipFailure.CallEffectConflict => DiagnosticCode.CallEffectConflict_Kd,
        OwnershipFailure.StorageLimit => DiagnosticCode.OwnershipStorageLimit_Kd,
        OwnershipFailure.CaseLimit => DiagnosticCode.OwnershipCaseLimit_Kd,
        _ => DiagnosticCode.Unsupported_Kd,
    };
}

/// <summary>Verification of the supported ownership subset, never an executable-emission certificate.</summary>
public readonly record struct OwnershipResult(bool IsVerified, int BodyCount, int ErrorCount);

/// <summary>Reusable per-body CFG and plans. The next analysis replaces its contents.</summary>
public sealed partial class OwnershipBody
{
#pragma warning disable SA1401 // Builder and solver share retained storage directly.
    internal readonly List<OwnershipPlace> PlaceStorage = new();
    internal readonly List<OwnershipOperation> OperationStorage = new();
    internal readonly List<OwnershipEdge> EdgeStorage = new();
    internal readonly List<int> EdgeHeads = new();
    internal readonly List<int> IncomingEdges = new();
    internal readonly List<int> OperationSteps = new();
    internal readonly List<OwnershipValue> Values = new();
    internal readonly List<OwnershipSequence> Sequences = new();
    internal readonly List<int> ValueOperands = new();
    internal readonly List<OwnershipPhiInput> PhiInputs = new();
    internal readonly List<OwnershipSlotResult> SlotResults = new();
    internal readonly List<OwnershipResultArrival> ResultArrivals = new();
    internal readonly List<OwnershipResultWrite> ResultWrites = new();
    internal readonly Dictionary<Koto, int> SlotResultPlaces = new(ReferenceEqualityComparer.Instance);
    internal readonly List<OwnershipDelivery> Deliveries = new();
    internal readonly List<OwnershipCleanupStep> CleanupStepStorage = new();
    internal readonly List<OwnershipCleanupPlan> CleanupPlanStorage = new();
    internal readonly List<OwnershipDeferredPlan> DeferredPlanStorage = new();
    internal readonly List<OwnershipConstructionPlan> ConstructionStorage = new();
    internal readonly List<OwnershipConstructionPlan> DecompositionStorage = new();
    internal readonly List<OwnershipMatchPlan> MatchStorage = new();
    internal readonly List<OwnershipMatchArmPlan> MatchArmStorage = new();
    internal readonly List<OwnershipIssue> IssueStorage = new();
    internal readonly List<OwnershipProjection> Projections = new();
    internal readonly List<OwnershipElementUpdate> ElementUpdates = new();
    internal readonly List<OwnershipStringComparison> StringComparisons = new();
    internal readonly List<OwnershipComparisonLoan> ComparisonLoans = new();
    internal readonly List<OwnershipCallLoans> CallLoans = new();
    internal readonly List<OwnershipCallReservation> CallReservations = new();
    internal readonly List<int> LoanInputs = new();
    internal readonly List<int> LoanStates = new();
    internal readonly Dictionary<BindingSymbol, int> SymbolPlaces = new(ReferenceEqualityComparer.Instance);
    internal int ReceiverBase = -1;
    internal bool[] Reachable = [];
    internal bool[] BlockReachable = [];
    internal bool[] BlockQueued = [];
    internal int[] BlockQueue = [];
    internal int[] BlockOf = [];
    internal int[] BlockLeaders = [];
    internal int[] IncomingCounts = [];
    internal ulong[] BlockStates = [];
    internal ulong[] Scratch = [];
#pragma warning restore SA1401
    private readonly Dictionary<(Koto Source, OwnershipFailure Failure), int> reportedIssues = new();
    private PairCase[] caseStorage = [];
    private int caseCount;
    private ulong caseBit;

    public FunctionKoto Function { get; internal set; } = null!;

    public IReadOnlyList<OwnershipPlace> Places => this.PlaceStorage;

    public IReadOnlyList<OwnershipOperation> Operations => this.OperationStorage;

    public IReadOnlyList<OwnershipEdge> Edges => this.EdgeStorage;

    public IReadOnlyList<OwnershipCleanupStep> CleanupSteps => this.CleanupStepStorage;

    public IReadOnlyList<OwnershipCleanupPlan> CleanupPlans => this.CleanupPlanStorage;

    public IReadOnlyList<OwnershipDeferredPlan> DeferredPlans => this.DeferredPlanStorage;

    public IReadOnlyList<OwnershipConstructionPlan> Constructions => this.ConstructionStorage;

    public IReadOnlyList<OwnershipConstructionPlan> Decompositions => this.DecompositionStorage;

    public IReadOnlyList<OwnershipMatchPlan> Matches => this.MatchStorage;

    public IReadOnlyList<OwnershipMatchArmPlan> MatchArms => this.MatchArmStorage;

    public IReadOnlyList<OwnershipIssue> Issues => this.IssueStorage;

    public bool IsVerified { get; internal set; }

    public bool IsConcrete { get; internal set; }

    internal int DefaultParameter { get; set; } = -1;

    internal int ParameterCount => this.DefaultParameter >= 0 ? this.DefaultParameter : this.Function.Parameters.Count;

    internal BoundType? DeclaredResultType => this.DefaultParameter >= 0 ? this.Function.Parameters[this.DefaultParameter].Type.BoundType : this.Function.BoundSymbol?.Type;

    internal List<OwnershipIdentity>? Identities { get; set; }

    /// <summary>Gets or sets each definition-time conditional plan's acquired Place and the Place its Reborrow case borrows (SPEC 8.9).</summary>

    /// <summary>Gets or sets the derived effects of the definition's generic requirement calls on their abstract inputs (SPEC 8.4.10.4).</summary>
    internal List<OwnershipRequirementEffect>? RequirementEffects { get; set; }

    /// <summary>Gets or sets the Loans the results of those calls may keep in the regions of their inputs (SPEC 8.4.10.4).</summary>
    internal List<OwnershipRequirementResult>? RequirementResults { get; set; }

    /// <summary>Gets or sets the anchor Places of the body's raw Place borrows, each sourced at its borrow (SPEC 5.2.2).</summary>
    internal List<int>? Anchors { get; set; }

    /// <summary>Gets or sets the closed call whose substitution this instance plan carries; null for a source body (SPEC 21.3.1).</summary>
    internal BoundCall? Instance { get; set; }

    internal Binding? InstanceBinding { get; set; }

    /// <summary>Gets the Semantics case this definition run analyzes (SPEC 8.10); empty outside a case run.</summary>
    internal ReadOnlyMemory<PairCase> Cases => this.caseStorage.AsMemory(0, this.caseCount);

    public bool IsReachable(int operation) => this.Reachable[operation];

    // A case or instance plan carries its substitution from the start, so every phase that reads a declared Type through
    // Concrete, from building and solving to lowering, sees the substituted Type.
    internal OwnershipTransfers TransfersAt(int operation)
    {
        var entry = this.Operations[operation];
        return OwnershipFlow.FlowOf(entry.Kind) switch
        {
            OwnershipFlow.OperationFlow.Construction => OwnershipFlow.Transfers(this.Constructions[this.OperationSteps[operation]]),
            OwnershipFlow.OperationFlow.Decomposition => OwnershipFlow.Transfers(this.Decompositions[this.OperationSteps[operation]], decompose: true),
            OwnershipFlow.OperationFlow.DictionaryEntry => new(entry.Place, entry.Input, 2, SourceStep: this.OperationSteps[operation] - entry.Input),
            _ => OwnershipFlow.Transfers(entry),
        };
    }

    internal void Reset(FunctionKoto function, BoundCall? instance, Binding? instanceBinding, ReadOnlySpan<PairCase> cases = default, ulong caseBit = 0)
    {
        this.ResetResolvedCalls();
        this.ResetCleanupEffects();
        this.Function = function;
        this.DefaultParameter = -1;
        this.Instance = instance;
        this.caseBit = caseBit;
        if (this.caseStorage.Length < cases.Length)
        {
            this.caseStorage = new PairCase[Math.Max(cases.Length, 4)];
        }

        cases.CopyTo(this.caseStorage);
        this.caseCount = cases.Length;
        this.InstanceBinding = instance is null && cases.IsEmpty ? null : instanceBinding;
        this.IsVerified = false;
        this.IsConcrete = function.IsSpecialization || function.GenericArguments.Count == 0;
        this.PlaceStorage.Clear();
        this.OperationStorage.Clear();
        this.DefaultContexts?.Clear();
        this.DefaultSymbolPlaces?.Clear();
        this.DefaultEvaluations?.Clear();
        this.DefaultInputs?.Clear();
        this.EdgeStorage.Clear();
        this.EdgeHeads.Clear();
        this.IncomingEdges.Clear();
        this.OperationSteps.Clear();
        this.Values.Clear();
        this.Sequences.Clear();
        this.Identities?.Clear();
        this.RequirementEffects?.Clear();
        this.RequirementResults?.Clear();
        this.Anchors?.Clear();
        this.ValueOperands.Clear();
        this.PhiInputs.Clear();
        this.SlotResults.Clear();
        this.ResultArrivals.Clear();
        this.ResultWrites.Clear();
        this.SlotResultPlaces.Clear();
        this.Deliveries.Clear();
        this.CleanupStepStorage.Clear();
        this.CleanupPlanStorage.Clear();
        this.DeferredPlanStorage.Clear();
        this.ConstructionStorage.Clear();
        this.DecompositionStorage.Clear();
        this.MatchStorage.Clear();
        this.MatchArmStorage.Clear();
        this.IssueStorage.Clear();
        this.Projections.Clear();
        this.ReceiverBase = -1;
        this.movePaths.Clear();
        this.movePathIndex.Clear();
        this.movePathOrder.Clear();
        this.ElementUpdates.Clear();
        this.StringComparisons.Clear();
        this.ComparisonLoans.Clear();
        this.CallLoans.Clear();
        this.CallReservations.Clear();
        this.LoanInputs.Clear();
        this.LoanStates.Clear();
        this.reportedIssues.Clear();
        this.reservedElementWrites?.Clear();
        this.ResetCompletion();
        this.SymbolPlaces.Clear();
    }

    // SPEC 8.10, 23.3.6.4: a problem of a Semantics case run carries its case; one problem found by several cases of this body's
    // function is one record whose cases merge (MergeIssue).
    internal void ReportIssue(OwnershipIssue issue)
    {
        if (this.reportedIssues.TryAdd((issue.Source, issue.Failure), this.IssueStorage.Count))
        {
            this.IssueStorage.Add(this.caseBit == 0 ? issue : issue with { Cases = this.caseBit });
        }
    }

    // A side case body's problem merges into this listed body: a problem already found gains the cases, a new one is added.
    internal void MergeIssue(in OwnershipIssue issue)
    {
        if (this.reportedIssues.TryGetValue((issue.Source, issue.Failure), out var index))
        {
            this.IssueStorage[index] = this.IssueStorage[index] with { Cases = this.IssueStorage[index].Cases | issue.Cases };
            return;
        }

        this.reportedIssues.Add((issue.Source, issue.Failure), this.IssueStorage.Count);
        this.IssueStorage.Add(issue);
    }

    // SPEC 23.3.6.4: after the last case, a problem found under every case of `all` shows no case.
    internal void DropUniversalCases(ulong all)
    {
        for (var i = 0; i < this.IssueStorage.Count; i++)
        {
            if (this.IssueStorage[i].Cases == all)
            {
                this.IssueStorage[i] = this.IssueStorage[i] with { Cases = 0 };
            }
        }
    }

    // An implementation invariant that decides the analysis outcome is checked in every configuration
    // (SPEC 21.3.5): a violation is an internal issue that leaves the body unverified, never a silent
    // inconsistency that only a Debug build would notice.
    internal bool Invariant(bool condition, Koto? source = null)
    {
        if (!condition && this.IssueStorage.Count == 0)
        {
            // A graph left partial where an earlier issue stopped it is not evidence of an implementation fault.
            this.ReportIssue(new(source ?? this.Function, OwnershipFailure.Internal));
        }

        return condition;
    }

    // The root stage of Resolve: a case run sees its case Types and an instance plan its closed substitution. Every consumer
    // names its interpretation context through Resolve (SPEC 7.2.3, 8.10).
    private BoundType? Concrete(BoundType? type)
        => type is null ? type : this.Instance is not null ? this.InstanceBinding!.InstantiateStorageType(type, this.Instance)
        : this.Cases.IsEmpty ? type : this.InstanceBinding!.CaseType(type, this.Cases.Span);
}

// Values use their defining operation ID; Input on OwnershipOperation remains a Place ID.
internal enum OwnershipValueKind : byte
{
    None,
    StaticRead,
    Constant,
    Parameter,
    Call,
    DefaultCall,
    DefaultRead,
    Alias,
    Convert,
    Unary,
    Binary,
    StringComparison,
    ContractComparison,
    RuntimeTypeTest,
    DictionaryLiteral,
    Element,
    Borrow,
    Phi,
    Address,
    BorrowedField,
    BorrowedFieldWrite,
    BorrowedUpdate,
    Sequence,
    Formatting,
    PatternProjection,
    Closure,
    Capture,
    ClosureErasure,

    // SPEC 5.2: a Copy/Move read through a raw pointer; the input is the pointer value.
    PointerLoad,

    // SPEC 5.2: replacement through a raw pointer; inputs are the pointer and (for scalars) value.
    // Constant retains the acquired source Place, including aggregate/Unit storage without an SSA value.
    PointerStore,

    // SPEC 5.2, 12: the raw address of an inline stored part of a raw pointer Place. Inputs are the
    // containing pointer value and, for a computed array index, the isize index (Constant -1);
    // otherwise Constant is the logical path position. Accesses nothing.
    PointerProject,
}

internal enum SequenceOperation : byte
{
    FromEnd,
    Borrow,
    Indices,
    Length,
    Start,
    End,
    IsEmpty,
    Capacity,
    Slice,
    Read,
}

internal readonly record struct OwnershipSequence(int Operation, SequenceOperation Kind, int Receiver, int Projection = -1, int Index = -1, int End = -1, int Address = -1);

// Start/Count address PhiInputs for Phi, otherwise ValueOperands.
// Constant holds the signed-extended N-bit integer payload, or the logical index for Parameter.
// Put the small operator before the 16-byte payload to avoid tail padding in every CFG value.
internal readonly record struct OwnershipValue(OwnershipValueKind Kind, int Start, int Count, KotoKind Operator = default, Int128 Constant = default)
{
    // SPEC 4.6.9: the Constant of a Convert value that makes an integer position an isize one; a value isize cannot hold
    // becomes -1, which the position's one bounds check rejects.
    internal const int PositionConversion = 1;

    // SPEC 13.5.4.3: the Constant of a Convert value that wraps an integer to the target's width, without a check.
    internal const int WrapConversion = 2;

    // SPEC 13.5.4.4: the Constant of a Convert value that reinterprets bits between a floating-point and an integer Type.
    internal const int BitConversion = 3;

    // SPEC 5.2.2: the Constant of a Convert value that borrows a raw Place: the reference is the Place's address.
    internal const int RawPlaceBorrow = 4;
}

// Value is a defining operation, Edge is the actual arrival after cleanup, Write secures a result or is -1.
internal readonly record struct OwnershipPhiInput(int Value, int Edge, int Write);

// The value is captured before cleanup; Write is the operation that secured it.
internal readonly record struct OwnershipDelivery(int Operation, int Value, int Write);

internal readonly record struct OwnershipIdentity(ConversionKoto Source, int Place);

// A dynamic expression result lifetime; deferred replicas can share Place but not Declare/Join.
internal readonly record struct OwnershipSlotResult(int Place, int Declare, int Join, int Start, int Count);

internal readonly record struct OwnershipResultArrival(int Edge, int Write);

// Includes secured results whose cleanup prevents arrival.
internal readonly record struct OwnershipResultWrite(int Operation, int Declare);

// Persistent stack links preserve independent branch environments.
// Calls, comparisons, guard inspection and element access share the same lexical chain.
// Read anchors acquisition: Read/Borrow, LocateReceiver for storage protection,
// and final ProjectElement for an exclusive write.
internal readonly record struct OwnershipComparisonLoan(int Read, int Place, int Parent, int Depth, LoanRequirement Mode = LoanRequirement.Ref, Koto? Call = null, int Guard = -1, bool Access = false, int Projection = -1, InvocationKoto? Callable = null, int Reservation = -1);

internal readonly record struct OwnershipCallReservation(Koto Call, int Borrow = -1, int Place = -1, int Activation = -1, int Loan = -1, int Next = -1, int Loaded = -1, int Argument = -1);

internal readonly record struct OwnershipCallLoans(int Call, int Result, int End, LoanRequirement ResultRequirement);

// The value an input of a requirement call designates: a root Place, possibly through its first Field; a negative root when
// unknown. Two live exclusive values are disjoint, and shared aliases only read, so effects reach another value only through
// the same root or a dependency between roots.
internal readonly record struct OwnershipValueIdentity(int Root, BindingSymbol? Field);

// SPEC 8.4.10.4: a generic requirement call reaches, in Mode, every Loan that the Origins of one abstract input Type may denote
// (the Region) through Input. Preserves holds when an available bound excludes the earlier results of the same requirement on
// the same receiver.
internal readonly record struct OwnershipRequirementEffect(int Call, int Region, LoanRequirement Mode, object Requirement, OwnershipValueIdentity Input, OwnershipValueIdentity Receiver, bool Preserves, BindingSymbol? Contract = null);

// SPEC 8.4.10.4: the result of a generic requirement call may keep, in Mode, Loans of a Region its call reached through Input.
internal readonly record struct OwnershipRequirementResult(int Call, int Result, int Region, LoanRequirement Mode, object Requirement, OwnershipValueIdentity Input, OwnershipValueIdentity Receiver, BindingSymbol? Contract = null);

internal readonly record struct OwnershipStringComparison(int Operation, int Left, int Right, int LeftLoan, int RightLoan, int LeftValue = -1, int RightValue = -1);

// Parent is another projection index. Output is the final Copy/Move acquisition, Write the replacement.
// An update has both and links its numeric calculation/result through ElementUpdates.
// Loan protects location; Exclusive replaces that protection for a write after final bounds resolution.
// Borrow identifies a Read/Borrow that replaces location protection with an element Loan without acquisition.
// Path/PathDepth identify the longest static prefix in this same projection table;
// Selector is the decoded literal element index, or -1 for a non-static selector.
internal readonly record struct OwnershipProjection(int Operation, int Root, int Parent, int Index, int Element, int Loan, int Output = -1, int Write = -1, int Update = -1, int Exclusive = -1, int Path = -1, int PathDepth = 0, int Selector = -1, int Borrow = -1, int ReplacementBorrow = -1);

// A completed numeric update links one projection's Copy and store to its calculation/result.
internal readonly record struct OwnershipElementUpdate(int Projection, int Right, int Computation, int Result);
