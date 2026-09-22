// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

internal sealed partial class GenericStoragePlan
{
    private static bool PrepareMatches(OwnershipBody body, Binding binding, out int[] tests, out bool[] acquisitions, out bool[] decompositions)
    {
        tests = new int[body.Operations.Count];
        Array.Fill(tests, -1);
        acquisitions = new bool[body.Operations.Count];
        decompositions = new bool[body.Operations.Count];
        var initializers = new int[body.Places.Count];
        Array.Fill(initializers, -1);
        for (var id = 0; id < body.Operations.Count; id++)
        {
            var operation = body.Operations[id];
            if (operation.Kind != OwnershipOperationKind.InitializeSubject)
            {
                continue;
            }

            if ((uint)operation.Place >= (uint)body.Places.Count || (uint)operation.Input >= (uint)body.Places.Count ||
                initializers[operation.Place] >= 0 || body.Places[operation.Place].Kind != OwnershipPlaceKind.Subject ||
                body.Places[operation.Input].Kind is not (OwnershipPlaceKind.Temporary or OwnershipPlaceKind.Result) ||
                !ReferenceEquals(body.Places[operation.Place].Type, body.Places[operation.Input].Type) ||
                (body.IsReachable(id) && ((body.GetInputState(id, operation.Place) & PlaceState.MayInit) != 0 ||
                    (body.GetInputState(id, operation.Input) & PlaceState.MustInit) == 0)))
            {
                return false;
            }

            initializers[operation.Place] = id;
        }

        for (var m = 0; m < body.Matches.Count; m++)
        {
            var match = body.Matches[m];
            var plan = match.Binding;
            if (!plan.IsCurrent || plan.Coverage.State != MatchCoverageState.Exhaustive || match.ArmCount <= 0 ||
                (uint)match.Subject >= (uint)body.Places.Count || initializers[match.Subject] < 0 ||
                !ReferenceEquals(body.Places[match.Subject].Type, plan.Syntax.Expression.BoundType) ||
                !EnumStorage.IsEnum(body.Places[match.Subject].Type) ||
                match.ArmCount != plan.Arms.Count || match.ArmStart < 0 || match.ArmStart > body.MatchArms.Count - match.ArmCount)
            {
                return false;
            }

            var dispatch = Next(body, initializers[match.Subject]);
            if (!Is(body, dispatch, OwnershipOperationKind.MatchDispatch, match.Subject) || body.OperationSteps[dispatch] != m ||
                !ReferenceEquals(body.Operations[dispatch].Source, plan.Syntax) || !Pure(body, dispatch) ||
                Next(body, dispatch, OwnershipEdgeKind.MatchArm) != body.MatchArms[match.ArmStart].Test)
            {
                return false;
            }

            for (var a = 0; a < match.ArmCount; a++)
            {
                var armIndex = match.ArmStart + a;
                var arm = body.MatchArms[armIndex];
                if (arm.Match != m || (uint)arm.Pattern >= (uint)plan.Positions.Count || arm.Pattern != plan.Arms[a].Pattern ||
                    (uint)arm.Test >= (uint)tests.Length || tests[arm.Test] >= 0 || arm.GuardEntry != -1 || arm.GuardBranch != -1 ||
                    arm.GuardValue != -1 || arm.GuardCleanupStart != -1 || plan.Arms[a].Syntax.Guard is not null ||
                    body.Operations[arm.Test].Kind != OwnershipOperationKind.PatternTest || body.Operations[arm.Test].Place != match.Subject ||
                    body.OperationSteps[arm.Test] != armIndex || !Pure(body, arm.Test) ||
                    (uint)arm.BodyEntry >= (uint)body.Operations.Count || body.Operations[arm.BodyEntry].Kind != OwnershipOperationKind.Branch ||
                    !ReferenceEquals(body.Operations[arm.BodyEntry].Source, plan.Arms[a].Syntax.Body) ||
                    !Continuations(body, arm.Test, arm.BodyEntry, a + 1 < match.ArmCount ? body.MatchArms[armIndex + 1].Test : -1))
                {
                    return false;
                }

                var pattern = plan.Positions[arm.Pattern];
                if (pattern.Kind != BoundPatternKind.Case || pattern.Case is not { } selected || pattern.Parent >= 0 ||
                    pattern.AccessMode != PatternAccessMode.Owned || pattern.ImplicitDeref != PatternImplicitDeref.None ||
                    !ReferenceEquals(pattern.MatchedType, body.Places[match.Subject].Type) ||
                    !ReferenceEquals(EnumStorage.Case(pattern.MatchedType, selected.Ordinal), selected) ||
                    !ReferenceEquals(pattern.Source, body.Operations[arm.Test].Source) ||
                    pattern.End != arm.Pattern + 1 + selected.Payload.Length || pattern.End > plan.Positions.Count)
                {
                    return false;
                }

                tests[arm.Test] = selected.Ordinal;
                var needed = false;
                for (var child = arm.Pattern + 1; child < pattern.End; child++)
                {
                    var node = plan.Positions[child];
                    var element = child - arm.Pattern - 1;
                    if (node.Parent != arm.Pattern || node.Element != element || node.End != child + 1 ||
                        node.Kind is not (BoundPatternKind.Binding or BoundPatternKind.Wildcard) ||
                        node.AccessMode != PatternAccessMode.Owned || node.ImplicitDeref != PatternImplicitDeref.None ||
                        !ReferenceEquals(node.MatchedType, pattern.MatchedType.StoredCases![selected.Ordinal].Components[element]))
                    {
                        return false;
                    }

                    needed |= node.Kind == BoundPatternKind.Binding;
                }

                if (arm.DecompositionCount != (needed ? 1 : 0))
                {
                    return false;
                }

                if (!needed)
                {
                    continue;
                }

                if ((uint)arm.DecompositionStart >= (uint)body.Decompositions.Count)
                {
                    return false;
                }

                var split = body.Decompositions[arm.DecompositionStart];
                if (split.Place != match.Subject || !ReferenceEquals(split.Case, selected) || split.PayloadCount != selected.Payload.Length ||
                    split.PayloadStart < 0 || split.PayloadStart > body.Places.Count - split.PayloadCount)
                {
                    return false;
                }

                var cursor = Next(body, arm.BodyEntry);
                for (var i = 0; i < split.PayloadCount; i++)
                {
                    if (!Is(body, cursor, OwnershipOperationKind.Declare, split.PayloadStart + i) ||
                        !ReferenceEquals(body.Places[split.PayloadStart + i].Type, plan.Positions[arm.Pattern + 1 + i].MatchedType))
                    {
                        return false;
                    }

                    cursor = Next(body, cursor);
                }

                if (!Is(body, cursor, OwnershipOperationKind.DecomposeCase, match.Subject) || body.OperationSteps[cursor] != arm.DecompositionStart)
                {
                    return false;
                }

                decompositions[cursor] = true;
                cursor = Next(body, cursor);
                for (var i = 0; i < split.PayloadCount; i++)
                {
                    var node = plan.Positions[arm.Pattern + 1 + i];
                    if (node.Kind != BoundPatternKind.Binding)
                    {
                        continue;
                    }

                    if (node.BodySymbol is null || node.Acquisition is not (PatternAcquisition.Copy or PatternAcquisition.Move or PatternAcquisition.CopyOrMove) ||
                        !body.SymbolPlaces.TryGetValue(node.BodySymbol, out var local) || !Is(body, cursor, OwnershipOperationKind.Declare, local))
                    {
                        return false;
                    }

                    cursor = Next(body, cursor);
                    if (!Is(body, cursor, OwnershipOperationKind.AcquirePattern, split.PayloadStart + i) ||
                        body.Operations[cursor].Input != local || body.Operations[cursor].Acquisition != (node.Acquisition == PatternAcquisition.Copy ? AcquisitionKind.Copy : node.Acquisition == PatternAcquisition.Move ? AcquisitionKind.Move : AcquisitionKind.CopyOrMove) ||
                        !ReferenceEquals(body.Operations[cursor].Source, node.Source))
                    {
                        return false;
                    }

                    acquisitions[cursor] = true;
                    cursor = Next(body, cursor);
                }
            }
        }

        return true;

        static bool Is(OwnershipBody body, int id, OwnershipOperationKind kind, int place)
            => (uint)id < (uint)body.Operations.Count && body.Operations[id].Kind == kind && body.Operations[id].Place == place;

        static bool Pure(OwnershipBody body, int id) => body.Values[id].Kind == OwnershipValueKind.None && body.Operations[id].Input == -1 &&
            (body.LoanInputs.Count == 0 || body.LoanInputs[id] == body.LoanStates[id]);

        static bool Continuations(OwnershipBody body, int id, int success, int failure)
        {
            var yes = false;
            var no = false;
            var count = 0;
            for (var e = body.EdgeHeads[id]; e >= 0; e = body.Edges[e].Next)
            {
                if ((uint)e >= (uint)body.Edges.Count || ++count > 2)
                {
                    return false;
                }

                var edge = body.Edges[e];
                if (edge.Kind == OwnershipEdgeKind.True && edge.To == success && !yes)
                {
                    yes = true;
                }
                else if (edge.Kind == OwnershipEdgeKind.False && failure >= 0 && edge.To == failure && !no)
                {
                    no = true;
                }
                else
                {
                    return false;
                }
            }

            return yes && no == (failure >= 0);
        }

        static int Next(OwnershipBody body, int id, OwnershipEdgeKind kind = OwnershipEdgeKind.Normal)
        {
            if ((uint)id >= (uint)body.Operations.Count || body.EdgeHeads[id] is not (>= 0 and var edge) || (uint)edge >= (uint)body.Edges.Count)
            {
                return -1;
            }

            return body.Edges[edge].Next < 0 && body.Edges[edge].Kind == kind ? body.Edges[edge].To : -1;
        }
    }
}
