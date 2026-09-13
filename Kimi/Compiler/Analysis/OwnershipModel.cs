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
}

public enum OwnershipPlaceKind : byte
{
    Local,
    Parameter,
    Temporary,
    Result,
    Payload,
    Subject,
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
    Unsupported,
    PayloadPlacement,
    CompleteConstruction,
    InitializeSubject,
    DecomposeCase,
    AcquirePattern,
    MatchDispatch,
    PatternTest,
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
    Unsupported,
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
}

public readonly record struct OwnershipPlace(int Id, Koto Source, BoundType Type, OwnershipPlaceKind Kind, bool Mutable, AcquisitionKind Acquisition);

/// <summary>One CFG program point; Place/Input are IDs in its body's Place table.</summary>
public readonly record struct OwnershipOperation(OwnershipOperationKind Kind, Koto Source, int Place = -1, int Input = -1, AcquisitionKind Acquisition = AcquisitionKind.None, PlacementKind Placement = PlacementKind.None)
{
    public PlaceUseKind Use => this.Kind switch
    {
        OwnershipOperationKind.Read or OwnershipOperationKind.PatternTest => PlaceUseKind.Read,
        OwnershipOperationKind.Consume or OwnershipOperationKind.AcquirePattern => PlaceUseKind.Consume,
        OwnershipOperationKind.Write or OwnershipOperationKind.PayloadPlacement => PlaceUseKind.Write,
        OwnershipOperationKind.Borrow => PlaceUseKind.Borrow,
        _ => PlaceUseKind.None,
    };
}

public readonly record struct OwnershipEdge(int From, int To, OwnershipEdgeKind Kind, int Next);

public readonly record struct OwnershipCleanupStep(int Operation, int Place, Koto Source, CleanupAction Action);

/// <summary>A cleanup sequence on an edge, in execution order, indexed into CleanupSteps.</summary>
public readonly record struct OwnershipCleanupPlan(int Edge, int Start, int Count, CleanupReason Reason);

/// <summary>One deferred execution, including empty bodies; endpoints are explicit CFG operations.</summary>
public readonly record struct OwnershipDeferredPlan(Koto Source, int Edge, int Entry, int Continuation, int End, int Parent, bool CanComplete);

/// <summary>One construction's N-to-one responsibility transfer; payload Places are contiguous.</summary>
public readonly record struct OwnershipConstructionPlan(int Place, BoundEnumCase Case, int PayloadStart, int PayloadCount);

/// <summary>Analysis fans out to every arm; runtime selection tests these Patterns in source order.</summary>
public readonly record struct OwnershipMatchPlan(BoundMatch Binding, int Subject, int Result, int ArmStart, int ArmCount);

/// <summary>One successful Pattern test and its ownership decomposition before the arm body.</summary>
public readonly record struct OwnershipMatchArmPlan(int Match, int Pattern, int Test, int DecompositionStart, int DecompositionCount);

public readonly record struct OwnershipIssue(Koto Source, OwnershipFailure Failure, int Place = -1);

/// <summary>Verification of the supported ownership subset, never an executable-emission certificate.</summary>
public readonly record struct OwnershipResult(bool IsVerified, int BodyCount, int ErrorCount, int UnsupportedCount);

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
    internal readonly List<int> ValueOperands = new();
    internal readonly List<OwnershipPhiInput> PhiInputs = new();
    internal readonly List<OwnershipDelivery> Deliveries = new();
    internal readonly List<OwnershipCleanupStep> CleanupStepStorage = new();
    internal readonly List<OwnershipCleanupPlan> CleanupPlanStorage = new();
    internal readonly List<OwnershipDeferredPlan> DeferredPlanStorage = new();
    internal readonly List<OwnershipConstructionPlan> ConstructionStorage = new();
    internal readonly List<OwnershipConstructionPlan> DecompositionStorage = new();
    internal readonly List<OwnershipMatchPlan> MatchStorage = new();
    internal readonly List<OwnershipMatchArmPlan> MatchArmStorage = new();
    internal readonly List<OwnershipIssue> IssueStorage = new();
    internal readonly List<int> OperationRegions = new();
    internal readonly List<OwnershipCheckingRegion> CheckingRegions = new();
    internal readonly Dictionary<BindingSymbol, int> SymbolPlaces = new(ReferenceEqualityComparer.Instance);
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
    private readonly HashSet<(Koto Source, OwnershipFailure Failure)> reportedIssues = new();

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

    public bool IsReachable(int operation) => this.Reachable[operation];

    internal void Reset(FunctionKoto function)
    {
        this.Function = function;
        this.IsVerified = false;
        this.IsConcrete = function.GenericArguments.Count == 0;
        this.PlaceStorage.Clear();
        this.OperationStorage.Clear();
        this.EdgeStorage.Clear();
        this.EdgeHeads.Clear();
        this.IncomingEdges.Clear();
        this.OperationSteps.Clear();
        this.Values.Clear();
        this.ValueOperands.Clear();
        this.PhiInputs.Clear();
        this.Deliveries.Clear();
        this.CleanupStepStorage.Clear();
        this.CleanupPlanStorage.Clear();
        this.DeferredPlanStorage.Clear();
        this.ConstructionStorage.Clear();
        this.DecompositionStorage.Clear();
        this.MatchStorage.Clear();
        this.MatchArmStorage.Clear();
        this.IssueStorage.Clear();
        this.reportedIssues.Clear();
        this.OperationRegions.Clear();
        this.CheckingRegions.Clear();
        this.CheckingRegions.Add(new(-1, -1)); // Region zero is ordinary source flow.
        this.checkingSolved = false;
        this.ResetCompletion();
        this.SymbolPlaces.Clear();
    }

    internal void ReportIssue(OwnershipIssue issue)
    {
        if (this.reportedIssues.Add((issue.Source, issue.Failure)))
        {
            this.IssueStorage.Add(issue);
        }
    }
}

// A checking-only seed edge. Its source is replayed after its containing region
// converges; it never enters EdgeStorage or contributes a runtime predecessor.
internal readonly record struct OwnershipCheckingRegion(int Seed, int Entry);

// Values use their defining operation ID; Input on OwnershipOperation remains a Place ID.
internal enum OwnershipValueKind : byte
{
    None,
    Constant,
    Parameter,
    Call,
    Alias,
    Convert,
    Unary,
    Binary,
    Phi,
}

// Start/Count address PhiInputs for Phi, otherwise ValueOperands.
// Constant holds the signed-extended N-bit integer payload, or the logical index for Parameter.
internal readonly record struct OwnershipValue(OwnershipValueKind Kind, int Start, int Count, long Constant = 0, KotoKind Operator = default);

// Value is a defining operation, Edge is the actual arrival after cleanup, Write secures a result or is -1.
internal readonly record struct OwnershipPhiInput(int Value, int Edge, int Write);

// The value is captured before cleanup; Write is the operation that secured it.
internal readonly record struct OwnershipDelivery(int Operation, int Value, int Write);
