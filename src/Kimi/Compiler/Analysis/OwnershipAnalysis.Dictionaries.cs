// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private int ConstructDictionary(DictionaryLiteralKoto source)
    {
        // The partial Dictionary is a live temporary throughout construction. Each successful insertion transfers
        // just that pair to it; a return while acquiring a later key/value uses the ordinary temporary cleanup order.
        var dictionary = this.ConstructAggregate(source, []);
        if (source.Entries.Count != 0)
        {
            this.CollectLibraryBody(this.compilation.Library.DictionaryRequireAbsent);
        }

        var completes = true;
        for (var i = 0; i < source.Entries.Count; i++)
        {
            var entry = source.Entries[i];
            var key = this.Expression(entry.Key);
            if (key >= 0)
            {
                var check = this.Emit(OwnershipOperationKind.CheckDictionaryKey, entry.Key, dictionary, key);
                this.SetValue(check, OwnershipValueKind.DictionaryLiteral, [this.Value(key)]);
                this.Connect(check, this.abortExit, OwnershipEdgeKind.Abort);
            }

            var loanDepth = this.comparisonDepth++;
            var reservationMark = this.body.CallReservations.Count;
            var value = this.Expression(entry.Value);
            if (key < 0 || value < 0)
            {
                this.EndComparisonLoans(loanDepth, entry.Value);
                this.comparisonDepth = loanDepth;
                completes = false;
                continue;
            }

            // An exclusive reference stored as a value is acquired exclusively by the literal at its insertion (PLAN G53).
            this.ReservePlacedReference(source, entry.Value, value, value);
            this.ActivateCallReservations(source, reservationMark);
            var insert = this.Emit(OwnershipOperationKind.StoreDictionaryEntry, entry.Key, dictionary, key);
            this.body.OperationSteps[insert] = value;
            this.SetValue(insert, OwnershipValueKind.DictionaryLiteral, [this.Value(key), this.Value(value)]);
            this.Connect(insert, this.abortExit, OwnershipEdgeKind.Abort);
            this.EndComparisonLoans(loanDepth, entry.Value);
            this.comparisonDepth = loanDepth;
        }

        return completes ? dictionary : -1;
    }
}
