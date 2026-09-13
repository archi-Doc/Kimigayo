// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>
/// Lowers one verified ownership body into a physical function (SPEC 21.4). It consumes the existing
/// operation/Place identities and edge cleanup plans, and never guesses a representation, terminator or cleanup.
/// </summary>
/// <remarks>
/// Supports bool/8–64-bit integer values, Unit control flow, checked arithmetic and owned string locals.
/// Every operation, including unreachable ones, is validated before physical blocks are assembled.
/// Direct scalar calls use the module's pre-registered signatures; borrowed values and general aggregate
/// results require additional verified plans.
/// </remarks>
internal sealed partial class BodyLowering
{
    private const byte NormalMark = 1;
    private const byte AbortMark = 2;
    private const byte CleanupMark = 4;
    private const byte DeferredEntryMark = 8;
    private const byte DeferredContinuationMark = 16;

    private readonly SourceLocationTable locations = new();
    private readonly List<int> arguments = new();
    private byte[] marks = [];
    private int[] deferredOwners = [];
    private int[] deliveries = [];
    private int pointerWidth;

    internal bool Lower(CoreIntrinsics core, OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string projectDirectory, Dictionary<FunctionKoto, FunctionAbi> functions, ControlFlowAnalysis flow, int pointerWidth, out string? failure)
    {
        this.pointerWidth = pointerWidth;
        this.functions = functions;
        this.flow = flow;
        this.arguments.Clear();
        var count = body.Operations.Count;
        Grow(ref this.deliveries, count);
        this.deliveries.AsSpan(0, count).Fill(-1);
        for (var i = 0; i < body.Deliveries.Count; i++)
        {
            var id = body.Deliveries[i].Operation;
            if ((uint)id >= (uint)count || this.deliveries[id] >= 0 || body.Operations[id].Kind != OwnershipOperationKind.Deliver)
            {
                return Fail("Invalid function delivery plan.", out failure);
            }

            this.deliveries[id] = i;
        }

        if (!ValidateValues(body))
        {
            return Fail("Missing or inconsistent value-flow plan.", out failure);
        }

        if (count == 0 || body.Operations[0].Kind != OwnershipOperationKind.Entry)
        {
            return Fail("The verified CFG has no entry operation.", out failure);
        }

        if (this.marks.Length < count)
        {
            Array.Resize(ref this.marks, Math.Max(count, this.marks.Length * 2));
        }

        var marks = this.marks.AsSpan(0, count);
        marks.Clear();
        if (!this.MarkCleanupPlans(body, marks))
        {
            return Fail("Cleanup lowering requires every cleanup operation in exactly one matching edge plan.", out failure);
        }

        return this.LowerGraph(core, body, function, constants, projectDirectory, marks, out failure);
    }

    private static bool Fail(string message, out string? failure)
    {
        failure = message;
        return false;
    }

    private bool MarkDeferredPlans(OwnershipBody body, Span<byte> marks)
    {
        for (var d = 0; d < body.DeferredPlans.Count; d++)
        {
            var deferred = body.DeferredPlans[d];
            if (deferred.Source is not DeferredBlockKoto || (uint)deferred.Entry >= (uint)marks.Length ||
                deferred.Continuation != deferred.Entry + 1 || deferred.End <= deferred.Continuation || deferred.End > marks.Length ||
                deferred.Parent < -1 || deferred.Parent >= d || (d > 0 && deferred.Entry <= body.DeferredPlans[d - 1].Entry) ||
                marks[deferred.Entry] != 0 || marks[deferred.Continuation] != 0 ||
                body.Operations[deferred.Entry].Kind != OwnershipOperationKind.Branch || body.Operations[deferred.Continuation].Kind != OwnershipOperationKind.Branch ||
                !ReferenceEquals(body.Operations[deferred.Entry].Source, deferred.Source) || !ReferenceEquals(body.Operations[deferred.Continuation].Source, deferred.Source) ||
                deferred.Edge < -1 || (deferred.Edge >= 0 && ((uint)deferred.Edge >= (uint)body.Edges.Count || body.Edges[deferred.Edge].To != deferred.Entry)) ||
                (deferred.Edge < 0 && body.IsReachable(deferred.Entry)) ||
                (body.IsReachable(deferred.Entry) && body.IsReachable(deferred.Continuation) != deferred.CanComplete))
            {
                return false;
            }

            marks[deferred.Entry] |= DeferredEntryMark;
            marks[deferred.Continuation] |= DeferredContinuationMark;
        }

        // Preorder intervals identify the innermost body in one pass; nested cleanup
        // does not repeatedly scan the same operation or edge at each nesting level.
        Grow(ref this.deferredOwners, marks.Length);
        var active = -1;
        var next = 0;
        for (var op = 0; op < marks.Length; op++)
        {
            while (active >= 0 && op >= body.DeferredPlans[active].End)
            {
                active = body.DeferredPlans[active].Parent;
            }

            if (next < body.DeferredPlans.Count && body.DeferredPlans[next].Entry == op)
            {
                var plan = body.DeferredPlans[next];
                if (plan.Parent != active || (active >= 0 && plan.End > body.DeferredPlans[active].End))
                {
                    return false;
                }

                active = next++;
            }

            this.deferredOwners[op] = active;
        }

        for (var e = 0; e < body.Edges.Count; e++)
        {
            var edge = body.Edges[e];
            if ((uint)edge.From >= (uint)marks.Length || (uint)edge.To >= (uint)marks.Length)
            {
                return false;
            }

            if (edge.Kind != OwnershipEdgeKind.Abort)
            {
                var from = this.deferredOwners[edge.From];
                var to = this.deferredOwners[edge.To];
                if (to >= 0 && edge.To == body.DeferredPlans[to].Entry && e != body.DeferredPlans[to].Edge)
                {
                    return false;
                }

                if (from >= 0 && edge.From == body.DeferredPlans[from].Continuation)
                {
                    from = body.DeferredPlans[from].Parent;
                }

                if (to != from && !(to >= 0 && body.DeferredPlans[to].Parent == from && body.DeferredPlans[to].Entry == edge.To))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool MarkCleanupPlans(OwnershipBody body, Span<byte> marks)
    {
        if (body.DeferredPlans.Count > 0 && !this.MarkDeferredPlans(body, marks))
        {
            return false;
        }

        for (var p = 0; p < body.CleanupPlans.Count; p++)
        {
            var cleanup = body.CleanupPlans[p];
            if (cleanup.Edge < -1 || (cleanup.Edge >= 0 && (uint)cleanup.Edge >= (uint)body.Edges.Count) || cleanup.Count <= 0 || cleanup.Start < 0 ||
                cleanup.Start > body.CleanupSteps.Count - cleanup.Count)
            {
                return false;
            }

            for (var s = cleanup.Start; s < cleanup.Start + cleanup.Count; s++)
            {
                var step = body.CleanupSteps[s];
                if ((uint)step.Operation >= (uint)marks.Length || body.Operations[step.Operation].Kind != OwnershipOperationKind.Cleanup ||
                    body.OperationSteps[step.Operation] != s || (marks[step.Operation] & CleanupMark) != 0)
                {
                    return false;
                }

                marks[step.Operation] |= CleanupMark;
                if (s + 1 < cleanup.Start + cleanup.Count)
                {
                    var edge = body.EdgeHeads[step.Operation];
                    if (edge < 0 || body.Edges[edge].Next >= 0 || body.Edges[edge].To != body.CleanupSteps[s + 1].Operation)
                    {
                        return false;
                    }
                }
            }

            if (cleanup.Edge >= 0 && body.Edges[cleanup.Edge].To != body.CleanupSteps[cleanup.Start].Operation)
            {
                return false;
            }

            if (cleanup.Edge < 0 && body.IsReachable(body.CleanupSteps[cleanup.Start].Operation))
            {
                return false;
            }
        }

        return true;
    }

    private bool LowerOperation(CoreIntrinsics core, OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string projectDirectory, int index, ReadOnlySpan<byte> marks, out string? failure)
    {
        var operation = body.Operations[index];
        if (this.arguments.Count != 0 && operation.Kind is not (OwnershipOperationKind.CallEntry or OwnershipOperationKind.Call))
        {
            return Fail("Call entries must be consecutive and immediately precede their call.", out failure);
        }

        if (operation.Place >= 0 && ReferenceEquals(body.Places[operation.Place].Type, BoundType.String) &&
            operation.Kind is OwnershipOperationKind.Declare or OwnershipOperationKind.Produce or OwnershipOperationKind.Consume or OwnershipOperationKind.Write or OwnershipOperationKind.Cleanup)
        {
            return this.LowerString(body, function, constants, projectDirectory, index, marks, out failure);
        }

        switch (operation.Kind)
        {
            case OwnershipOperationKind.Entry when index == 0:
            case OwnershipOperationKind.Exit:
                break;

            case OwnershipOperationKind.Deliver:
                return this.LowerReturn(body, function, index, out failure);

            case OwnershipOperationKind.Declare:
            case OwnershipOperationKind.Read:
            case OwnershipOperationKind.Consume:
            case OwnershipOperationKind.Write:
            case OwnershipOperationKind.Branch:
                if (operation.Source is DeferredBlockKoto && (marks[index] & (DeferredEntryMark | DeferredContinuationMark)) == 0)
                {
                    return Fail("A deferred boundary has no execution plan.", out failure);
                }

                return this.LowerScalar(body, function, constants, projectDirectory, index, out failure);

            case OwnershipOperationKind.Produce:
                if (operation.Place >= 0 && IsScalar(body.Places[operation.Place].Type))
                {
                    return this.LowerScalar(body, function, constants, projectDirectory, index, out failure);
                }

                if (operation.Place < 0)
                {
                    return Fail("A produced value has no Place.", out failure);
                }

                var place = body.Places[operation.Place];
                if (ReferenceEquals(place.Type, BoundType.Unit))
                {
                    break; // Unit retains its verified effects but has no physical value (SPEC 21.3.3).
                }

                return Fail("A produced value needs unsupported value lowering.", out failure);

            case OwnershipOperationKind.CallEntry:
                if (operation.Place < 0 || (body.IsReachable(index) && (body.GetInputState(index, operation.Place) & PlaceState.MustInit) == 0))
                {
                    return Fail("A call argument is not proven initialized.", out failure);
                }

                this.arguments.Add(index);
                this.AddStringFlags(function, operation, index);
                break;

            case OwnershipOperationKind.Call:
                return this.LowerCall(core, body, function, constants, projectDirectory, index, out failure);

            case OwnershipOperationKind.Cleanup:
                var stepIndex = body.OperationSteps[index];
                if (stepIndex < 0 || (marks[index] & CleanupMark) == 0)
                {
                    return Fail("A cleanup operation has no edge cleanup plan.", out failure);
                }

                var step = body.CleanupSteps[stepIndex];
                if (step.Operation != index || step.Place != operation.Place || step.Action is not (CleanupAction.Skip or CleanupAction.Destroy or CleanupAction.Conditional))
                {
                    return Fail("Cleanup is conditional, mismatched or unsupported.", out failure);
                }

                if (operation.Place >= 0 && IsScalar(body.Places[operation.Place].Type))
                {
                    break;
                }

                if (step.Action == CleanupAction.Conditional)
                {
                    return Fail("Conditional non-scalar destruction is not implemented.", out failure);
                }

                // Unit has no value and no destructor; its verified cleanup has no physical output.
                if (step.Action == CleanupAction.Destroy && !(operation.Place >= 0 && ReferenceEquals(body.Places[operation.Place].Type, BoundType.Unit)))
                {
                    return Fail("Destruction needs unsupported Type lowering.", out failure);
                }

                break;

            default:
                return Fail("An ownership operation needs unsupported lowering or Loan/Origin verification.", out failure);
        }

        failure = null;
        return true;
    }

    private bool TryGetLocation(Koto source, string projectDirectory, LlvmConstantPool constants, out int constant)
    {
        if (!this.locations.TryGet(source, projectDirectory, out var text))
        {
            constant = -1;
            return false;
        }

        constant = constants.Intern(text, LlvmConstantKind.Location);
        return true;
    }
}
