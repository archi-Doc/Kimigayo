// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private readonly List<int> resultHeads = new();
    private readonly List<(int Join, int Place, int Declare)> resultJoins = new();
    private readonly List<int> resultDeclarations = new();
    private readonly List<PendingResult> pendingResults = new();

    private static bool ScalarResult(BoundType type) => ScalarTypes.Supports(type);

    private int ResultJoin(Koto source, int place)
    {
        var join = this.New(OwnershipOperationKind.Branch, source);
        if (place >= 0 && ScalarResult(this.body.Places[place].Type))
        {
            this.body.OperationStorage[join] = this.body.Operations[join] with { Place = place };
        }

        if (place >= 0 && (ScalarResult(this.body.Places[place].Type) || ReferenceEquals(this.body.Places[place].Type, BoundType.String)))
        {
            this.resultHeads[join] = -1;
            this.resultJoins.Add((join, place, this.resultDeclarations[place]));
        }

        return join;
    }

    private int ResultPlace(Koto source)
    {
        var owned = ReferenceEquals(source.BoundType, BoundType.String);
        var place = owned && this.body.StringResultPlaces.TryGetValue(source, out var shared) ? shared : this.Temporary(source, false);
        if (owned)
        {
            this.body.StringResultPlaces[source] = place;
        }

        if (ScalarResult(this.body.Places[place].Type) || owned)
        {
            this.body.PlaceStorage[place] = this.body.Places[place] with { Kind = OwnershipPlaceKind.Result };
            // A fresh result lifetime on every evaluation, including evaluations inside a loop.
            this.resultDeclarations[place] = this.Emit(OwnershipOperationKind.Declare, source, place);
        }

        return place;
    }

    private int WriteResult(Koto source, int place, int input)
    {
        var write = input >= 0 || ReferenceEquals(this.body.Places[place].Type, BoundType.Unit) ? this.Emit(OwnershipOperationKind.Write, source, place, input) : -1;
        if (write >= 0 && ReferenceEquals(this.body.Places[place].Type, BoundType.String) && this.resultDeclarations[place] >= 0)
        {
            this.body.ResultWrites.Add(new(write, this.resultDeclarations[place]));
        }

        return write;
    }

    private void Deliver(Koto source, int secured)
    {
        var value = secured >= 0 && ScalarResult(this.body.Places[this.resultPlace].Type) && this.body.Values[secured].Kind == OwnershipValueKind.Alias ? this.body.ValueOperands[this.body.Values[secured].Start] : -1;
        var delivery = this.Emit(OwnershipOperationKind.Deliver, source, this.resultPlace);
        this.body.Deliveries.Add(new(delivery, value, secured));
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
        foreach (var result in this.resultJoins)
        {
            var join = result.Join;
            var scalar = ScalarResult(this.body.Places[result.Place].Type);
            var start = scalar ? this.body.PhiInputs.Count : this.body.ResultArrivals.Count;
            for (var pending = this.resultHeads[join]; pending >= 0; pending = this.pendingResults[pending].Next)
            {
                var input = this.pendingResults[pending];
                if (!this.body.IsReachable(this.body.Edges[input.Edge].From))
                {
                    continue;
                }

                if (!scalar)
                {
                    this.body.ResultArrivals.Add(new(input.Edge, input.Write));
                    continue;
                }

                var value = input.Value;
                while (value >= 0 && this.body.Values[value].Kind == OwnershipValueKind.Alias)
                {
                    value = this.body.ValueOperands[this.body.Values[value].Start];
                }

                this.body.PhiInputs.Add(new(value, input.Edge, input.Write));
            }

            if (scalar)
            {
                this.body.Values[join] = new(OwnershipValueKind.Phi, start, this.body.PhiInputs.Count - start);
            }
            else
            {
                this.body.StringResults.Add(new(result.Place, result.Declare, join, start, this.body.ResultArrivals.Count - start));
            }
        }
    }

    private readonly record struct PendingResult(int Value, int Edge, int Write, int Next);
}
