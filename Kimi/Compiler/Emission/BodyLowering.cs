// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>
/// Lowers one verified ownership body into a physical function (SPEC 21.4). It consumes the existing
/// operation/Place identities and edge cleanup plans, and never guesses a representation, terminator or cleanup.
/// </summary>
/// <remarks>
/// The supported CFG is a straight normal path whose calls may take a terminal abort edge. Every operation,
/// including unreachable ones, must have a lowering; branches, locals and borrowed values fail preparation.
/// Extend lowering together with its unsupported-case tests: add explicit blocks for branches, a worklist of
/// selected implementations for user calls, and a ValueLowering for each new Type.
/// </remarks>
internal sealed class BodyLowering
{
    private const byte NormalMark = 1;
    private const byte AbortMark = 2;
    private const byte CleanupMark = 4;

    private readonly SourceLocationTable locations = new();
    private readonly List<int> arguments = new();
    private byte[] marks = [];

    internal bool Lower(CoreIntrinsics core, OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string projectDirectory, out string? failure)
    {
        this.arguments.Clear();
        var count = body.Operations.Count;
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

        for (var p = 0; p < body.Places.Count; p++)
        {
            if (WindowsLowering.GetValue(body.Places[p].Type) is not { } value)
            {
                return Fail("A Place needs an unsupported layout or value representation.", out failure);
            }

            if (value.Layout.Size != 0)
            {
                function.Slots.Add(new(p, value));
            }
        }

        var cursor = 0;
        while (true)
        {
            if ((uint)cursor >= (uint)count || (marks[cursor] & (NormalMark | AbortMark)) != 0 || !body.IsReachable(cursor))
            {
                // A missing successor or cycle is an error, never an invented ret/unreachable.
                return Fail("The verified CFG needs unsupported or incomplete control-flow lowering.", out failure);
            }

            marks[cursor] |= NormalMark;
            var operation = body.Operations[cursor];
            if (!this.LowerOperation(core, body, function, constants, projectDirectory, cursor, marks, out failure))
            {
                return false;
            }

            var next = -1;
            for (var e = body.EdgeHeads[cursor]; e >= 0; e = body.Edges[e].Next)
            {
                var edge = body.Edges[e];
                if (edge.Kind == OwnershipEdgeKind.Abort)
                {
                    // Core's abort is terminal inside the nonreturning runtime path, not a caller continuation.
                    if (operation.Kind != OwnershipOperationKind.Call || (uint)edge.To >= (uint)count ||
                        body.Operations[edge.To].Kind != OwnershipOperationKind.Exit || body.EdgeHeads[edge.To] >= 0 || (marks[edge.To] & NormalMark) != 0)
                    {
                        return Fail("An abort edge needs unsupported lowering.", out failure);
                    }

                    marks[edge.To] |= AbortMark;
                }
                else if (edge.Kind is not (OwnershipEdgeKind.Normal or OwnershipEdgeKind.Return) || next >= 0)
                {
                    return Fail("A control-flow edge needs unsupported branch lowering.", out failure);
                }
                else
                {
                    next = edge.To;
                }
            }

            if (operation.Kind == OwnershipOperationKind.Exit)
            {
                if (next >= 0 || this.arguments.Count != 0)
                {
                    return Fail("The verified CFG needs unsupported or incomplete control-flow lowering.", out failure);
                }

                function.Add(EmissionOpcode.ReturnVoid, cursor);
                break;
            }

            cursor = next;
        }

        for (var i = 0; i < count; i++)
        {
            var kind = body.Operations[i].Kind;
            // Unexecuted operations still require a supported lowering (SPEC 21.4.1).
            if ((body.IsReachable(i) && (marks[i] & (NormalMark | AbortMark)) == 0) ||
                (kind == OwnershipOperationKind.Cleanup && (marks[i] & CleanupMark) == 0) ||
                kind is not (OwnershipOperationKind.Entry or OwnershipOperationKind.Exit or OwnershipOperationKind.Deliver or OwnershipOperationKind.Produce or
                OwnershipOperationKind.CallEntry or OwnershipOperationKind.Call or OwnershipOperationKind.Cleanup))
            {
                return Fail("An ownership operation needs unsupported lowering or Loan/Origin verification.", out failure);
            }
        }

        failure = null;
        return true;
    }

    private static bool Fail(string message, out string? failure)
    {
        failure = message;
        return false;
    }

    private bool MarkCleanupPlans(OwnershipBody body, Span<byte> marks)
    {
        for (var p = 0; p < body.CleanupPlans.Count; p++)
        {
            var cleanup = body.CleanupPlans[p];
            if ((uint)cleanup.Edge >= (uint)body.Edges.Count || cleanup.Count <= 0 || cleanup.Start < 0 ||
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
            }

            if (body.Edges[cleanup.Edge].To != body.CleanupSteps[cleanup.Start].Operation)
            {
                return false;
            }
        }

        return true;
    }

    private bool LowerOperation(CoreIntrinsics core, OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string projectDirectory, int index, ReadOnlySpan<byte> marks, out string? failure)
    {
        var operation = body.Operations[index];
        switch (operation.Kind)
        {
            case OwnershipOperationKind.Entry when index == 0:
            case OwnershipOperationKind.Exit:
                break;

            case OwnershipOperationKind.Deliver:
                if (operation.Place >= 0 && !ReferenceEquals(body.Places[operation.Place].Type, BoundType.Unit))
                {
                    return Fail("Aggregate or scalar function results need unsupported result lowering.", out failure);
                }

                break;

            case OwnershipOperationKind.Produce:
                if (operation.Place < 0)
                {
                    return Fail("A produced value has no Place.", out failure);
                }

                var place = body.Places[operation.Place];
                if (ReferenceEquals(place.Type, BoundType.Unit))
                {
                    break; // Unit retains its verified effects but has no physical value (SPEC 21.3.3).
                }

                if (ReferenceEquals(place.Type, BoundType.String) && place.Kind == OwnershipPlaceKind.Temporary &&
                    operation.Source is StringLiteralKoto { AttributeChain: null } literal)
                {
                    // An empty literal is Static/null/length zero with no backing constant (SPEC 21.5.6).
                    var constant = literal.Literal.Length == 0 ? -1 : constants.Intern(literal.Literal, LlvmConstantKind.Text);
                    function.Add(EmissionOpcode.StoreStaticString, index, operation.Place, constant);
                    break;
                }

                return Fail("A produced value needs unsupported value lowering.", out failure);

            case OwnershipOperationKind.CallEntry:
                if (operation.Place < 0 || (body.GetInputState(index, operation.Place) & PlaceState.MustInit) == 0)
                {
                    return Fail("A call argument is not proven initialized.", out failure);
                }

                this.arguments.Add(operation.Place);
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
                if (step.Operation != index || step.Place != operation.Place || step.Action is not (CleanupAction.Skip or CleanupAction.Destroy))
                {
                    return Fail("Cleanup is conditional, mismatched or unsupported.", out failure);
                }

                // Unit has no value and no destructor; its verified cleanup has no physical output.
                if (step.Action == CleanupAction.Destroy && !(operation.Place >= 0 && ReferenceEquals(body.Places[operation.Place].Type, BoundType.Unit)))
                {
                    if (operation.Place < 0 || !ReferenceEquals(body.Places[operation.Place].Type, BoundType.String) ||
                        !this.TryGetLocation(operation.Source, projectDirectory, constants, out var location))
                    {
                        return Fail("Destruction needs unsupported Type lowering or source provenance.", out failure);
                    }

                    function.AddCall(index, WindowsLowering.DestroyString, [new(EmissionOperandKind.SlotAddress, operation.Place), new(EmissionOperandKind.ConstantAddress, location), new(EmissionOperandKind.ConstantLength, location)]);
                }

                break;

            default:
                return Fail("An ownership operation needs unsupported lowering or Loan/Origin verification.", out failure);
        }

        failure = null;
        return true;
    }

    private bool LowerCall(CoreIntrinsics core, OwnershipBody body, EmissionFunction function, LlvmConstantPool constants, string projectDirectory, int index, out string? failure)
    {
        var operation = body.Operations[index];
        // Only compiler-provided Core implementations are callable; user bodies need the selected-implementation worklist.
        if (operation.Source is not InvocationKoto { AttributeChain: null, BoundCall: { } plan } call ||
            !ReferenceEquals(plan.Target, core.WriteLine) || WindowsLowering.GetCompilerFunction(plan.Target.CompilerFunction) is not { } callee ||
            plan.Receiver is not null || plan.TypeArguments.Length != 0 || plan.Origins.Length != 0 || !ReferenceEquals(plan.ReturnType, BoundType.Unit) ||
            plan.ArgumentOperations.Length != 1 || this.arguments.Count != 1 || plan.ArgumentToParameter[0] != 0 ||
            plan.ArgumentOperations[0].Kind != ArgumentOperationKind.Value || !ReferenceEquals(plan.ArgumentOperations[0].ParameterType, BoundType.String) ||
            !ReferenceEquals(body.Places[this.arguments[0]].Type, BoundType.String))
        {
            return Fail("A call needs unsupported callee, argument acquisition or result lowering.", out failure);
        }

        if (!this.TryGetLocation(call, projectDirectory, constants, out var location))
        {
            return Fail("A call has no source provenance for runtime diagnostics.", out failure);
        }

        // The owned string argument transfers to the callee, whose normal path destroys it once (SPEC 21.4.3, 22.5.5).
        function.AddCall(index, callee, [new(EmissionOperandKind.SlotAddress, this.arguments[0]), new(EmissionOperandKind.ConstantAddress, location), new(EmissionOperandKind.ConstantLength, location)]);
        this.arguments.Clear();
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
