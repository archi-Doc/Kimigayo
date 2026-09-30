// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // A Range<S, E> or ClosedRange<S, E>, whose iteration condition is `S is PrimitiveInteger` and `E is S` (SPEC 4.6.3.4).
    private static bool IsRangeShape(BoundType subject)
        => subject is { Symbol.LibraryDeclaration: KimiDeclarationId.Range or KimiDeclarationId.ClosedRange, Components.Count: 2 };

    private bool BindSequenceMember(MemberAccessKoto source, BindingScope scope, out BoundType? result)
    {
        result = null;
        if (source.Right is not IdentifierNameKoto name || name.IdentifierName is not ("indices" or "length" or "isEmpty" or "capacity"))
        {
            return false;
        }

        var receiver = this.BindNode(source.Left, scope);
        if (ReferenceTypes.IsArray(receiver) || ReferenceTypes.IsDictionary(receiver) || FormattingTypes.IsSliceBorrow(receiver) || ReferenceTypes.IsSlice(receiver) || receiver is { Kind: BoundTypeKind.Semantics, Components: [{ Kind: BoundTypeKind.Array }] })
        {
            receiver = receiver!.Components[0]; // SPEC 4.6.1: metadata shares access through a reference to the sequence.
        }

        var utf8 = FormattingTypes.IsUtf8Slice(receiver);
        if (!utf8 && receiver?.Kind is not (BoundTypeKind.FixedArray or BoundTypeKind.Slice or BoundTypeKind.Array or BoundTypeKind.Dictionary))
        {
            return false; // A ResolvedRange exposes its fields and computed Properties through the library struct (SPEC 4.6.3).
        }

        // SPEC 4.6.1, 4.7.2 and 4.7.4: fixed arrays and Array expose length and indices, Slice and Array add isEmpty, Array
        // adds capacity.
        var valid = name.IdentifierName switch
        {
            "indices" => !utf8 && receiver!.Kind != BoundTypeKind.Dictionary,
            "length" => true,
            "isEmpty" => receiver!.Kind is BoundTypeKind.Slice or BoundTypeKind.Array,
            "capacity" => receiver!.Kind is BoundTypeKind.Array or BoundTypeKind.Dictionary,
            _ => false,
        };
        if (!valid)
        {
            result = this.Fail(source, BindingFailure.MissingName);
            return true;
        }

        result = name.IdentifierName == "indices" ? this.ResolvedRangeType : name.IdentifierName == "isEmpty" ? BoundType.Boolean : BoundType.ISize;
        Complete(name, result);
        Complete(source, result);
        return true;
    }

    private BoundType? BindIteration(ForKoto source, BindingScope scope)
    {
        var iterable = this.BindNode(source.Iterable, scope);
        source.Iteration?.Decomposition.Reset(null);
        var followed = this.PairSubject(source.Iterable, iterable, scope);
        iterable = followed ?? iterable;
        source.Mode = SubjectModeOf(source.Iterable, iterable);
        if (iterable is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components: [{ Kind: BoundTypeKind.Nominal, Symbol.LibraryDeclaration: KimiDeclarationId.ResolvedRange }] })
        {
            // SPEC 14.6.2, 3.4.1: the iteration entry is selected through the reference; the Copy interval is the
            // entry receiver and is read once for the loop.
            this.adaptations[source.Iterable] = new(ExpectedAdaptationKind.ReferentRead, iterable.Components[0]);
            iterable = iterable.Components[0];
        }

        // Dynamic Array, Dictionary, fixed-array and Slice loops use their Kimigayo entries and iterators (SPEC 14.6.2,
        // 22.1.2.5). SPEC 4.6.3.4: a ResolvedRange loop yields the isize positions of its RangeIterator<isize> entries
        // directly; a validated interval never Aborts, so no iterator value is formed.
        var view = iterable;
        var element = view is not null && ReferenceTypes.IsResolvedRange(view) ? BoundType.ISize : null;

        var userEntry = element is null && iterable is not null &&
            this.BindUserIteration(source, scope, iterable, out element);

        // SPEC 14.6.2, 14.8.1: structural decomposition selects every safe reference layer. A shared
        // layer bounds the path; Copying a ref restarts its Origin, while uniq keeps the reached dependency.
        var tuple = element;
        var access = PatternAccessMode.Owned;
        BoundOrigin? tupleOrigin = null;
        var layers = 0;
        if (source.IsTupleBinding && (userEntry || ReferenceTypes.IsTuple(element)))
        {
            while (tuple is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 })
            {
                access = tuple.Semantics == SemanticsKind.Ref || access == PatternAccessMode.Shared ? PatternAccessMode.Shared : PatternAccessMode.Exclusive;
                tupleOrigin = tuple.Semantics == SemanticsKind.Ref || tupleOrigin is null ? tuple.Origin ?? tupleOrigin
                    : tuple.Origin is { } layer ? this.Meet(tupleOrigin, layer) : tupleOrigin;
                tuple = tuple.Components[0];
                layers++;
            }
        }

        var sharedTuple = layers != 0;
        var result = this.BeginResult(source, scope, BoundType.Unit);
        var duplicate = false;
        for (var i = 0; i < source.Bindings.Count; i++)
        {
            var name = source.Bindings[i];
            duplicate |= name.BoundSymbol!.Next is not null;
            var slot = source.IsTupleBinding && tuple?.Kind == BoundTypeKind.Tuple && i < tuple.Components.Count ? tuple.Components[i] : element;
            if (sharedTuple && slot is not null)
            {
                slot = this.InternType(BoundTypeKind.Semantics, null, access == PatternAccessMode.Shared ? SemanticsKind.Ref : SemanticsKind.Uniq, [slot], origin: tupleOrigin);
            }

            name.BoundSymbol!.Type = slot;
            name.BoundSymbol.BindsReference = sharedTuple || (!userEntry && source.Mode != SubjectMode.ByValue);
            Complete(name, slot);
        }

        if (userEntry && element is not null)
        {
            this.BindIterationStep(source, scope, element, tuple!, access, layers);
        }

        this.BindNode(source.Body, scope);
        if (duplicate)
        {
            return this.Fail(source, BindingFailure.Duplicate);
        }

        if (iterable is null)
        {
            return Complete(source, null);
        }

        if (!userEntry && !ReferenceTypes.IsResolvedRange(view))
        {
            return this.FailIterationSubject(source, scope, iterable);
        }

        if (source.IsTupleBinding && (tuple?.Kind != BoundTypeKind.Tuple || tuple.Components.Count != source.Bindings.Count))
        {
            return this.Fail(source, BindingFailure.TypeMismatch);
        }

        return this.FinishResult(source, result);
    }

    // SPEC 14.6.2: a missing entry conformance is an error of the Subject, decided like any other Contract requirement:
    // refuted for a Type that lacks it, unproven for one whose conformance is not established, such as an unconstrained Type
    // parameter. A proven conformance whose entry or step did not bind remains an implementation boundary of the loop.
    private BoundType? FailIterationSubject(ForKoto source, BindingScope scope, BoundType subject)
    {
        var entry = this.IterationEntry(source, ref subject);
        var proof = this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, subject, contract: entry)), scope);
        if (proof == ConstraintProof.Proven)
        {
            return this.Fail(source, BindingFailure.Unsupported);
        }

        if (proof is ConstraintProof.Refuted or ConstraintProof.Unknown && source.Iterable.BindingFailure == BindingFailure.None && IsRangeShape(subject))
        {
            (this.rangeIterationFailures ??= new(ReferenceEqualityComparer.Instance))[source.Iterable] = (subject, entry);
        }

        this.Fail(source.Iterable, proof == ConstraintProof.Refuted ? BindingFailure.UnsatisfiedConstraint : proof == ConstraintProof.Error ? BindingFailure.InvalidConstraint : BindingFailure.UnprovenConstraint);
        return Complete(source, null);
    }

    // SPEC 14.6.2, 3.4.1: the Subject mode selects the entry, which a borrowing mode searches at the referent of every
    // reference layer.
    private BindingSymbol IterationEntry(ForKoto source, ref BoundType subject)
    {
        if (source.Mode == SubjectMode.ByValue)
        {
            return this.Library.IntoIterable;
        }

        while (subject is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 })
        {
            subject = subject.Components[0];
        }

        return source.Mode == SubjectMode.Exclusive ? this.Library.UniqIterable : this.Library.Iterable;
    }

    private bool BindUserIteration(ForKoto source, BindingScope scope, BoundType subject, out BoundType? item)
    {
        item = null;
        var entry = this.IterationEntry(source, ref subject);
        var method = source.Mode == SubjectMode.ByValue ? "intoIterator" : source.Mode == SubjectMode.Exclusive ? "iterateUniq" : "iterate";
        // SPEC 8.4.8.2: a conditional conformance whose condition is refuted supplies no entry; the Subject is then diagnosed.
        // A range's condition is decided at the Subject as well when it is only unproven, so the loop reports it once there
        // (SPEC 4.6.3.4).
        var nominal = (subject.Symbol is { Declaration: StructKoto or EnumKoto } owner && this.ConformanceByDeclaration(owner, entry, out _) is not null &&
                this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, subject, contract: entry)), scope) is var proof &&
                (proof == ConstraintProof.Proven || (proof != ConstraintProof.Refuted && !IsRangeShape(subject)))) ||
            (IsFixedArrayEntry(subject, entry) && this.FixedArrayWitness(entry) is not null);
        if (!nominal && !this.HasContractFact(subject, entry, scope))
        {
            return false;
        }

        // SPEC 14.6.2: only the selected entry conformance authorizes enumeration. The ordinary call keeps
        // receiver acquisition, substitutions and Origins; no method-name fallback grants conformance.
        if (source.EntryCall is not { } call || ((MemberAccessKoto)call.Method).Right is not IdentifierNameKoto name || name.IdentifierName != method)
        {
            var member = new MemberAccessKoto(source, source.Iterable, new IdentifierNameKoto(source, method));
            source.EntryCall = call = new InvocationKoto(source, member, []);
        }
        else
        {
            ResetSynthetic(call);
            ResetSynthetic(call.Method);
            ResetSynthetic(((MemberAccessKoto)call.Method).Right);
        }

        this.projectionUses.Add((source, subject, entry));
        if (this.BindCall(call, scope, null) is not { } iterator)
        {
            return false;
        }

        this.projectionUses.Add((source, iterator, this.Library.LendingIterator));
        if (this.BindIterationNext(source, scope, iterator) is not { Components.Count: 1 } option || !ReferenceEquals(option.Symbol, this.Library.Option))
        {
            return false;
        }

        item = option.Components[0];
        this.userIterations.Add((source, subject, iterator, entry));
        return true;
    }

    // SPEC 14.6.2: only the selected entry conformance authorizes enumeration. The entry and step are ordinary member calls,
    // so once conformances are verified each must have selected that conformance's witness, or, through a Constraint fact,
    // the requirement itself; a same-named member that shadows the witness makes the loop ambiguous.
    private void ValidateIterationWitnesses()
    {
        for (var i = 0; i < this.userIterations.Count; i++)
        {
            var (loop, subject, iterator, entry) = this.userIterations[i];
            if (loop.EntryCall?.BoundCall is { } call && loop.Iteration?.Next.BoundCall is { } next &&
                !(this.SelectsWitness(call.Target, subject, entry) && this.SelectsWitness(next.Target, iterator, this.Library.LendingIterator)))
            {
                this.Fail(loop, BindingFailure.Ambiguous);
            }
        }
    }

    private bool SelectsWitness(BindingSymbol target, BoundType self, BindingSymbol contract)
    {
        if (target.Declaration is FunctionKoto { IsRequirement: true })
        {
            return target.Scope.Owner is ContractKoto owner && RefinesDeclaration(contract, owner);
        }

        if (IsFixedArrayEntry(self, contract))
        {
            return ReferenceEquals(this.FixedArrayWitness(contract), target);
        }

        if (self.Symbol is not { } type || !this.conformancesByType.TryGetValue(type, out var identities))
        {
            return false;
        }

        var unverified = false;
        for (var i = 0; i < identities.Count; i++)
        {
            for (var p = 0; p < identities[i].PathStorage.Count; p++)
            {
                var path = identities[i].PathStorage[p];
                if (!path.IsVerified)
                {
                    unverified = true; // The conformance reports its own failure.
                    continue;
                }

                for (var w = 0; w < path.WitnessStorage.Count; w++)
                {
                    var witness = path.WitnessStorage[w];
                    if (ReferenceEquals(witness.Implementation, target) && witness.Requirement.Scope.Owner is ContractKoto declaration && RefinesDeclaration(contract, declaration))
                    {
                        return true;
                    }
                }
            }
        }

        return unverified;
    }

    private BoundType? BindIterationNext(ForKoto source, BindingScope scope, BoundType iterator)
    {
        var plan = source.Iteration ??= new(source);
        plan.Scope.Parent = scope;
        plan.Scope.Function = scope.Function;
        var symbol = plan.Iterator.BoundSymbol ??= new BindingSymbol("$for.iterator", BindingSymbolKind.Local, plan.Iterator, plan.Scope);
        symbol.Type = iterator;
        plan.Scope.Values[symbol.Name] = symbol;
        plan.Receiver.BoundSymbol = symbol;
        Complete(plan.Iterator, iterator);
        Complete(plan.Receiver, iterator);
        ResetSynthetic(plan.Next);
        ResetSynthetic(plan.Next.Method);
        ResetSynthetic(((MemberAccessKoto)plan.Next.Method).Right);
        return this.BindCall(plan.Next, plan.Scope, null);
    }

    private void BindIterationStep(ForKoto source, BindingScope scope, BoundType item, BoundType tuple, PatternAccessMode access, int layers)
    {
        var plan = source.Iteration!;
        if (plan.Next.BoundType is not { } option || !ReferenceEquals(option.Symbol, this.Library.Option) ||
            option.Components.Count != 1 || !ReferenceEquals(option.Components[0], item))
        {
            this.Fail(source, BindingFailure.TypeMismatch);
            return;
        }

        // SPEC 14.6.2: Some delivers one owned item, independently of the entry mode. Reuse the ordinary
        // match decomposition and its acquisition validation; the synthetic nodes never replace source parents.
        var match = plan.Decomposition;
        match.Reset(plan.Match);
        match.Mode = SubjectMode.ByValue;
        match.Coverage = new(MatchCoverageState.Exhaustive);
        Complete(plan.Match, BoundType.Unit);
        Complete(plan.Match.Arms[1].Body, BoundType.Unit);
        Complete(plan.Item, item);
        var some = plan.Match.Arms[0].Pattern;
        var none = plan.Match.Arms[1].Pattern;
        Complete(some, option);
        Complete(none, option);
        var end = source.IsTupleBinding ? source.Bindings.Count + 2 : 2;
        var borrowedTuple = layers != 0;
        match.PositionStorage.Add(new(some, option, BoundPatternKind.Case, -1, -1, end, Case: Compiler.EnumStorage.Case(option, 0)));
        if (source.IsTupleBinding)
        {
            if (tuple.Kind != BoundTypeKind.Tuple || tuple.Components.Count != source.Bindings.Count)
            {
                match.IsCurrent = false;
                return;
            }

            match.PositionStorage.Add(new(plan.Item, item, BoundPatternKind.Tuple, 0, 0, end, AccessMode: access, ImplicitFollows: layers));
        }

        for (var i = 0; i < source.Bindings.Count; i++)
        {
            var name = source.Bindings[i];
            var type = borrowedTuple ? tuple.Components[i] : name.BoundType!;
            var proof = this.ProveCopy(type, name);
            var acquisition = borrowedTuple ? PatternAcquisition.Borrow : proof == ConstraintProof.Proven ? PatternAcquisition.Copy : proof == ConstraintProof.Refuted ? PatternAcquisition.Move : PatternAcquisition.CopyOrMove;
            match.PositionStorage.Add(new(name, type, BoundPatternKind.Binding, source.IsTupleBinding ? 1 : 0, source.IsTupleBinding ? i : 0, match.Positions.Count + 1, BodySymbol: name.BoundSymbol, Acquisition: acquisition, WholePosition: true, AccessMode: access));
        }

        match.PositionStorage.Add(new(none, option, BoundPatternKind.Case, -1, -1, end + 1, Case: Compiler.EnumStorage.Case(option, 1)));
        match.ArmStorage.Add(new(plan.Match.Arms[0], 0));
        match.ArmStorage.Add(new(plan.Match.Arms[1], end));
    }
}
