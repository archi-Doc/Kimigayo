// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipBody
{
    internal readonly record struct DefaultEvaluation(int Operation, BoundCall Call, int Parameter, int Start);

    internal List<DefaultEvaluation>? DefaultEvaluations { get; set; }

    internal List<(int Place, int Value, int Read)>? DefaultInputs { get; set; }

    private List<int>? defaultLoanWork;
    private bool[] defaultLoanVisited = [];

    // SPEC 7.2.3: the same completed acquisition/value-flow plan checks an unused default and every omitted evaluation.
    // Prepared parameters are read-only until call activation, and a result may not retain newly borrowed authority.
    internal void VerifyPreparedDefault(int result)
    {
        this.PrepareBorrowDefinitions();
        for (var root = 0; root < this.Places.Count; root++)
        {
            var parameter = this.Places[root];
            if (parameter.Kind != OwnershipPlaceKind.Parameter)
            {
                continue;
            }

            var escaped = this.Places[result].Type.CarriesOrigin ? this.PreparedLoanInResult(result, root) : -1;
            for (var id = 0; id < this.Operations.Count; id++)
            {
                var op = this.Operations[id];
                // The escaping Reborrow is explained by the result diagnostic below; report any separate mutation as well.
                if (id == escaped || op.Kind is OwnershipOperationKind.Produce or OwnershipOperationKind.Cleanup or OwnershipOperationKind.CallEntry or OwnershipOperationKind.Deliver)
                {
                    continue;
                }

                var conflict = ConflictsWithComparison(op.Kind, op.Place, op.Input, op.Acquisition, root, access: op.LoanMode) ||
                    this.ElementAccessConflicts(op, root, LoanRequirement.Ref);
                var value = this.Values[id];
                if (value.Count > 0 && (value.Kind is OwnershipValueKind.PointerStore or OwnershipValueKind.BorrowedFieldWrite or OwnershipValueKind.BorrowedUpdate ||
                    (op.Kind == OwnershipOperationKind.Borrow && op.LoanMode == LoanRequirement.Uniq)))
                {
                    var receiver = this.ValueOperands[value.Start];
                    var place = ValuePlaceForBorrow(this.Operations[receiver]);
                    conflict |= place == root || Depends(place, root);
                }

                if (conflict)
                {
                    this.ReportIssue(new(op.Source, OwnershipFailure.DefaultArgumentAccess, Related: parameter.Source));
                    break;
                }
            }

            if (escaped >= 0)
            {
                this.ReportIssue(new(this.Operations[escaped].Source, OwnershipFailure.DefaultArgumentBorrow, Related: parameter.Source));
            }
        }

        bool Depends(int place, int root) => place >= 0 && this.borrowRoots is { } roots && roots.Contains(root) &&
            this.borrowDependencies[(place * this.Places.Count) + root] != LoanRequirement.None;
    }

    // Follow the acquired value, not the address used to read it. Loading an existing shared reference from a prepared
    // argument copies that reference; a new shared Reborrow, including one returned by a call or placed in an aggregate,
    // still reaches its Borrow operation. Reuse the worklist and visit each operation at most once.
    private int PreparedLoanInResult(int result, int root)
    {
        var work = this.defaultLoanWork ??= new();
        work.Clear();
        Grow(ref this.defaultLoanVisited, this.Operations.Count);
        this.defaultLoanVisited.AsSpan(0, this.Operations.Count).Clear();
        for (var id = 0; id < this.Operations.Count; id++)
        {
            if (this.Operations[id] is { Kind: OwnershipOperationKind.Write } write && write.Place == result)
            {
                work.Add(id);
            }
        }

        while (work.Count > 0)
        {
            var id = work[^1];
            work.RemoveAt(work.Count - 1);
            if ((uint)id >= (uint)this.Operations.Count || this.defaultLoanVisited[id])
            {
                continue;
            }

            this.defaultLoanVisited[id] = true;
            var operation = this.Operations[id];
            var value = this.Values[id];
            if ((operation.Kind == OwnershipOperationKind.Borrow && operation.Place == root) ||
                (value.Kind == OwnershipValueKind.Sequence && this.Sequences[(int)value.Constant] is { Kind: SequenceOperation.Borrow } sequence && sequence.Receiver == root))
            {
                return id;
            }

            if (value.Kind == OwnershipValueKind.Sequence)
            {
                PushDefinitions(this.Sequences[(int)value.Constant].Receiver, id);
                continue;
            }

            if (value.Kind is OwnershipValueKind.PointerLoad or OwnershipValueKind.BorrowedField && value.Count > 0)
            {
                var pointer = this.ValueOperands[value.Start];
                var holder = ValuePlaceForBorrow(this.Operations[pointer]);
                var stored = holder >= 0 ? this.StoredReferent(pointer, holder) : -1;
                if (stored >= 0 && stored != holder && this.Places[stored].Kind != OwnershipPlaceKind.Parameter)
                {
                    PushDefinitions(stored, id);
                }

                continue;
            }

            if (operation.Kind == OwnershipOperationKind.Call)
            {
                if (this.ResultArgument(id) is >= 0 and var sole)
                {
                    work.Add(sole);
                }
                else if (operation.Place >= 0)
                {
                    if (value.Kind == OwnershipValueKind.DefaultCall)
                    {
                        var evaluation = this.DefaultEvaluations![(int)value.Constant];
                        for (var i = 0; i < evaluation.Parameter; i++)
                        {
                            var input = this.DefaultInputs![evaluation.Start + i];
                            if (this.NamesResultOrigin(this.Places[operation.Place].Type, this.Places[input.Place].Type))
                            {
                                work.Add(input.Read);
                            }
                        }

                        continue;
                    }

                    // A public result contract can retain either of several inputs. Each contributing acquisition must
                    // be checked; failure to identify a sole ancestor is not proof that a new Loan cannot escape.
                    for (var entry = id - 1; entry >= 0 && this.Operations[entry] is { Kind: OwnershipOperationKind.CallEntry } input && ReferenceEquals(input.Source, operation.Source); entry--)
                    {
                        if (input.Place >= 0 && this.NamesResultOrigin(this.Places[operation.Place].Type, this.Places[input.Place].Type))
                        {
                            work.Add(entry);
                        }
                    }
                }

                continue;
            }

            for (var i = 0; i < value.Count; i++)
            {
                work.Add(value.Kind == OwnershipValueKind.Phi ? this.PhiInputs[value.Start + i].Value : this.ValueOperands[value.Start + i]);
            }

            var source = operation.Kind switch
            {
                OwnershipOperationKind.Read or OwnershipOperationKind.Consume or OwnershipOperationKind.Produce or OwnershipOperationKind.CallEntry => operation.Place,
                OwnershipOperationKind.Write or OwnershipOperationKind.PayloadPlacement or OwnershipOperationKind.InitializeSubject => operation.Input,
                OwnershipOperationKind.AcquirePattern => this.PayloadSubject(operation.Place),
                _ => -1,
            };
            if (source >= 0 && this.Places[source].Kind != OwnershipPlaceKind.Parameter)
            {
                PushDefinitions(source, id);
            }
        }

        return -1;

        void PushDefinitions(int place, int before)
        {
            if (this.borrowDefinitions[place] is >= 0 and var definition)
            {
                work.Add(definition);
                return;
            }

            for (var at = 0; at < before; at++)
            {
                if (this.Operations[at] is { Kind: OwnershipOperationKind.Write } write && write.Place == place)
                {
                    work.Add(at);
                }
            }

            foreach (var construction in this.Constructions)
            {
                if (construction.Place != place)
                {
                    continue;
                }

                for (var at = 0; at < before; at++)
                {
                    if (this.Operations[at] is { Kind: OwnershipOperationKind.PayloadPlacement } placed && placed.Place >= construction.PayloadStart && placed.Place < construction.PayloadStart + construction.PayloadCount)
                    {
                        work.Add(at);
                    }
                }
            }

            work.Add(this.ProducingValue(place, before));
        }
    }

    // Preorder evaluation intervals preserve the declaration-to-call Type/Origin substitution at each replica of a default.
    // A nested default substitutes its own call first, then its enclosing defaults, then the body's case or instance.
    internal List<(int Start, int End, BoundCall Call, int Parent)>? DefaultContexts { get; set; }

    internal Dictionary<(BindingSymbol Symbol, int Context), int>? DefaultSymbolPlaces { get; set; }

    internal bool TrySymbolPlace(BindingSymbol symbol, int context, out int place)
    {
        for (; context >= 0; context = this.DefaultContexts![context].Parent)
        {
            if (this.DefaultSymbolPlaces is { } locals && locals.TryGetValue((symbol, context), out place))
            {
                return true;
            }
        }

        return this.SymbolPlaces.TryGetValue(symbol, out place);
    }

    internal bool TrySymbolPlaceAt(BindingSymbol symbol, int operation, out int place)
        => this.TrySymbolPlace(symbol, this.DefaultContextAt(operation), out place);

    internal int DefaultContextAt(int operation)
    {
        if (this.DefaultContexts is not { Count: > 0 } contexts)
        {
            return -1;
        }

        var low = 0;
        var high = contexts.Count;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (contexts[middle].Start <= operation)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        var context = low - 1;
        while (context >= 0 && operation >= contexts[context].End)
        {
            context = contexts[context].Parent;
        }

        return context;
    }

    internal BoundType? SubstituteDefaultType(BoundType? type, int context)
    {
        while (type is not null && context >= 0)
        {
            var entry = this.DefaultContexts![context];
            type = this.Function.CodeContext.Compilation.Binding.InstantiateStorageType(type, entry.Call);
            context = entry.Parent;
        }

        return type;
    }

    internal BoundType? ConcreteAt(BoundType? type, int operation)
        => this.Concrete(this.SubstituteDefaultType(type, this.DefaultContextAt(operation)));

    internal BoundCall? CallAt(int operation)
        => this.Values[operation].Kind == OwnershipValueKind.DefaultCall ? null : this.SubstituteDefaultCall((this.Operations[operation].Source as Parsing.InvocationKoto)?.BoundCall, this.DefaultContextAt(operation));

    internal BoundCall? SubstituteDefaultCall(BoundCall? call, int context)
    {
        for (; call is not null && context >= 0; context = this.DefaultContexts![context].Parent)
        {
            call = this.Function.CodeContext.Compilation.Binding.InstantiateDefaultCall(call, this.DefaultContexts![context].Call);
        }

        return call;
    }
}
