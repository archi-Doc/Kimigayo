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
}

public enum OwnershipEdgeKind : byte
{
    Normal,
    True,
    False,
    Back,
    Return,
    Abort,
}

public enum OwnershipFailure : byte
{
    UninitializedUse,
    PossiblyMovedUse,
    ReassignedLet,
    Unsupported,
}

public readonly record struct OwnershipPlace(int Id, Koto Source, BoundType Type, OwnershipPlaceKind Kind, bool Mutable, AcquisitionKind Acquisition);

/// <summary>One CFG program point; Place/Input are IDs in its body's Place table.</summary>
public readonly record struct OwnershipOperation(OwnershipOperationKind Kind, Koto Source, int Place = -1, int Input = -1, AcquisitionKind Acquisition = AcquisitionKind.None, PlacementKind Placement = PlacementKind.None)
{
    public PlaceUseKind Use => this.Kind switch
    {
        OwnershipOperationKind.Read => PlaceUseKind.Read,
        OwnershipOperationKind.Consume => PlaceUseKind.Consume,
        OwnershipOperationKind.Write or OwnershipOperationKind.PayloadPlacement => PlaceUseKind.Write,
        OwnershipOperationKind.Borrow => PlaceUseKind.Borrow,
        _ => PlaceUseKind.None,
    };
}

public readonly record struct OwnershipEdge(int From, int To, OwnershipEdgeKind Kind, int Next);

public readonly record struct OwnershipCleanupStep(int Operation, int Place, Koto Source, CleanupAction Action);

/// <summary>A cleanup sequence on an edge, in execution order, indexed into CleanupSteps.</summary>
public readonly record struct OwnershipCleanupPlan(int Edge, int Start, int Count, CleanupReason Reason);

/// <summary>One construction's N-to-one responsibility transfer; payload Places are contiguous.</summary>
public readonly record struct OwnershipConstructionPlan(int Place, BoundEnumCase Case, int PayloadStart, int PayloadCount);

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
    internal readonly List<OwnershipCleanupStep> CleanupStepStorage = new();
    internal readonly List<OwnershipCleanupPlan> CleanupPlanStorage = new();
    internal readonly List<OwnershipConstructionPlan> ConstructionStorage = new();
    internal readonly List<OwnershipIssue> IssueStorage = new();
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

    public FunctionKoto Function { get; internal set; } = null!;

    public IReadOnlyList<OwnershipPlace> Places => this.PlaceStorage;

    public IReadOnlyList<OwnershipOperation> Operations => this.OperationStorage;

    public IReadOnlyList<OwnershipEdge> Edges => this.EdgeStorage;

    public IReadOnlyList<OwnershipCleanupStep> CleanupSteps => this.CleanupStepStorage;

    public IReadOnlyList<OwnershipCleanupPlan> CleanupPlans => this.CleanupPlanStorage;

    public IReadOnlyList<OwnershipConstructionPlan> Constructions => this.ConstructionStorage;

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
        this.CleanupStepStorage.Clear();
        this.CleanupPlanStorage.Clear();
        this.ConstructionStorage.Clear();
        this.IssueStorage.Clear();
        this.SymbolPlaces.Clear();
    }
}
