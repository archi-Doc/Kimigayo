// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private readonly List<int> resultHeads = new();
    private readonly List<int> resultJoins = new();
    private readonly List<PendingResult> pendingResults = new();

    private static bool ScalarResult(BoundType type) => ReferenceTypes.IsValue(type);

    private int ResultJoin(Koto source, int place)
    {
        var join = this.New(OwnershipOperationKind.Branch, source);
        if (place >= 0 && ScalarResult(this.body.Places[place].Type))
        {
            this.body.OperationStorage[join] = this.body.Operations[join] with { Place = place };
            this.resultHeads[join] = -1;
            this.resultJoins.Add(join);
        }

        return join;
    }

    private int ResultPlace(Koto source)
    {
        var stored = SlotTypes.IsResult(this.Resolve(source.BoundType, this.Active)); // An instance stores a substituted owned result.
        var place = stored && this.defaultContext < 0 && this.body.SlotResultPlaces.TryGetValue(source, out var shared) ? shared : this.Temporary(source, false);
        if (stored && this.defaultContext < 0)
        {
            this.body.SlotResultPlaces[source] = place;
        }

        if (ScalarResult(this.body.Places[place].Type) || stored)
        {
            this.body.PlaceStorage[place] = this.body.Places[place] with { Kind = OwnershipPlaceKind.Result };
            // A fresh result lifetime on every evaluation, including evaluations inside a loop.
            this.Emit(OwnershipOperationKind.Declare, source, place);
        }

        return place;
    }

    private int WriteResult(Koto source, int place, int input, int reported = -1)
    {
        if (input < 0 && reported >= 0 && this.body.IssueStorage.Count > reported)
        {
            // The result expression's analysis already reported why it has no value; the result is initialized here, so its
            // delivery is not reported again as an uninitialized result at the signature.
            this.Emit(OwnershipOperationKind.Produce, source, place);
            return -1;
        }

        return input >= 0 || ReferenceEquals(this.body.Places[place].Type, BoundType.Unit) ? this.Emit(OwnershipOperationKind.Write, source, place, input) : -1;
    }

    private void Deliver(Koto source)
    {
        if (this.body.DefaultParameter < 0)
        {
            this.CheckConstruction(source);
        }

        this.Emit(OwnershipOperationKind.Deliver, source, this.resultPlace);
    }

    private void ConnectResult(int join, int operation)
    {
        var edge = this.Connect(this.current, join);
        if (edge < 0 || this.resultHeads[join] == -2)
        {
            return;
        }

        var write = operation >= 0 && this.body.Operations[operation].Kind == OwnershipOperationKind.Write;
        var value = this.body.Operations[join].Place < 0 ? -1 : write ? this.body.ValueOperands[this.body.Values[operation].Start] : operation;
        this.pendingResults.Add(new(value, edge, write ? operation : -1, this.resultHeads[join]));
        this.resultHeads[join] = this.pendingResults.Count - 1;
    }

    private int CompleteResult(Koto source, int place, int join)
    {
        if (place >= 0 && ScalarResult(this.body.Places[place].Type))
        {
            this.placeValues[place] = join;
        }

        this.current = this.flow!.Nodes[source].CanCompleteNormally ? join : -1;
        return this.current >= 0 && place >= 0 ? this.RegisterTemporary(place) : -1;
    }

    private void FinalizeResults()
    {
        foreach (var join in this.resultJoins)
        {
            var start = this.body.PhiInputs.Count;
            for (var pending = this.resultHeads[join]; pending >= 0; pending = this.pendingResults[pending].Next)
            {
                var input = this.pendingResults[pending];
                if (!this.body.IsReachable(this.body.Edges[input.Edge].From))
                {
                    continue;
                }

                var value = input.Value;
                while (value >= 0 && this.body.Values[value].Kind == OwnershipValueKind.Alias)
                {
                    value = this.body.ValueOperands[this.body.Values[value].Start];
                }

                this.body.PhiInputs.Add(new(value, input.Edge, input.Write));
            }

            this.body.Values[join] = new(OwnershipValueKind.Phi, start, this.body.PhiInputs.Count - start);
        }
    }

    private readonly record struct PendingResult(int Value, int Edge, int Write, int Next);
}
