// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // Persist declaration storage across provisional/final binding passes. No environment is
    // allocated for declarations with neither written relations nor binding-set names.
    private readonly Dictionary<Koto, OriginDeclaration> originDeclarations = new(ReferenceEqualityComparer.Instance);

    // Origins omitted in a local initializer's own Type expressions (SPEC 15.4.4), inferred while that initializer binds. They
    // are kept apart from declared Origin contracts, which are completed before bodies bind; entries are reused across binds.
    private readonly Dictionary<Koto, OriginDeclaration> initializerOrigins = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Koto, List<string>> discoveredOrigins = new(ReferenceEqualityComparer.Instance);
    private OriginRewriteVisitor? originRewriteVisitor;

    private static Koto? OriginOwner(Koto node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (current is FunctionKoto or PropertyAccessorKoto or DeclarationContainerKoto or AliasKoto or IsKoto { IsAssociatedConstraint: true } ||
                current is VariableKoto || current.Akind is KotoKind.EnumCase or KotoKind.AssociatedType)
            {
                // SPEC 15.3.3, 15.3.4: a Case's payload list is an inner form of the same kind; the Case, which the clauses attach to,
                // owns the sets of all its payloads.
                return current is { Akind: KotoKind.EnumCase, Parent: { Akind: KotoKind.EnumCase } outer } ? outer : current;
            }
        }

        return null;
    }

    private static bool OriginVisible(BoundOrigin origin, Koto use)
    {
        if (origin.Binder is FunctionTypeKoto binder)
        {
            var inside = false;
            for (var node = use; node is not null; node = node.Parent)
            {
                inside |= ReferenceEquals(node, binder);
            }

            if (!inside)
            {
                return false;
            }
        }

        for (var i = 0; i < origin.Operands.Count; i++)
        {
            if (!OriginVisible(origin.Operands[i], use))
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 15.3.5: the variance of a position nested at `inner` within a position of variance `outer`; an unused position is compared
    // as an invariant one (FitsTypeCore).
    private static OriginVariance ComposeVariance(OriginVariance outer, OriginVariance inner)
        => outer == OriginVariance.Invariant || inner is OriginVariance.Invariant or OriginVariance.Unused ? OriginVariance.Invariant
            : inner == OriginVariance.Covariant ? outer
            : outer == OriginVariance.Covariant ? OriginVariance.Contravariant : OriginVariance.Covariant;

    // SPEC 15.3.1, 23.3.6.4: a binding set that reuses a visible name failed as DuplicateBinding_Kd and declares nothing.
    private static bool RepeatedSet(TypeSemanticsKoto set) => set.BindingFailure == BindingFailure.Duplicate;

    private OriginDeclaration OriginDeclarationFor(Koto owner)
    {
        if (!this.originDeclarations.TryGetValue(owner, out var declaration))
        {
            this.originDeclarations.Add(owner, declaration = new(owner));
        }

        return declaration;
    }

    private void PrepareOriginDeclarations()
    {
        foreach (var declaration in this.originDeclarations.Values)
        {
            declaration.Reset();
        }

        foreach (var declaration in this.initializerOrigins.Values)
        {
            declaration.Reset();
        }

        foreach (var names in this.discoveredOrigins.Values)
        {
            names.Clear();
            ((OriginNameList)names).Spans.Clear();
        }

        this.absentSlotProjections?.Clear();
        this.absentSlotFunctions?.Clear();

        for (var i = 0; i < this.nodes.Count; i++)
        {
            var node = this.nodes[i];
            if (OriginClauses.Get(node).Count != 0)
            {
                this.OriginDeclarationFor(node);
            }

            if (node is not TypeSemanticsKoto annotation || OriginOwner(node) is not { } owner)
            {
                continue;
            }

            if (annotation.BindingSetName is { } label)
            {
                var declaration = this.OriginDeclarationFor(owner);
                if (!declaration.Sets.TryAdd(label, annotation))
                {
                    this.Fail(annotation, BindingFailure.Duplicate);
                }

                if (owner is VariableKoto and not PropertyKoto && owner.BoundSymbol is { } local)
                {
                    local.Scope.OriginSets ??= new(StringComparer.Ordinal);
                    if (!local.Scope.OriginSets.TryAdd(label, annotation))
                    {
                        this.Fail(annotation, BindingFailure.Duplicate);
                    }
                }

                continue;
            }

            if (annotation.OriginExpression is not { } expression)
            {
                continue;
            }

            // Only a callable signature introduces a name by writing it (SPEC 15.3.4); a Type's own slots are declared in its
            // header alone, so storage names never become slots (SPEC 15.3.2). Nested function Types are a binding boundary.
            var nested = false;
            for (var parent = annotation.Parent; parent is not null && !ReferenceEquals(parent, owner); parent = parent.Parent)
            {
                nested |= parent is FunctionTypeKoto;
            }

            // SPEC 8.8.2: a specialization's written binder names are preliminary; CompleteSpecializationOrigins maps them to the original's.
            if (nested || (owner is FunctionKoto function && (function.IsAnonymous || !IsSignature(annotation, function))) || owner is not (FunctionKoto or PropertyAccessorKoto))
            {
                continue;
            }

            Discover(expression, owner);
        }

        static bool IsSignature(Koto node, FunctionKoto function)
        {
            while (node.Parent is { } parent && !ReferenceEquals(parent, function))
            {
                node = parent;
            }

            if (ReferenceEquals(node, function.ReturnType))
            {
                return true;
            }

            for (var i = 0; i < function.Parameters.Count; i++)
            {
                if (ReferenceEquals(node, function.Parameters[i].Type))
                {
                    return true;
                }
            }

            return false;
        }

        void Discover(Koto expression, Koto binder)
        {
            if (expression is IdentifierNameKoto name && name.IdentifierName is not ("static" or "_"))
            {
                if (!this.discoveredOrigins.TryGetValue(binder, out var names))
                {
                    this.discoveredOrigins.Add(binder, names = new OriginNameList());
                }

                if (!names.Contains(name.IdentifierName))
                {
                    ((OriginNameList)names).Add(name.IdentifierName, name.Span);
                }
            }
            else if (expression is ParenthesizedKoto grouped)
            {
                Discover(grouped.Operand, binder);
            }
            else if (expression is BinaryKoto { Akind: KotoKind.And } meet)
            {
                Discover(meet.Left, binder);
                Discover(meet.Right, binder);
            }
        }
    }

    private IReadOnlyList<string> DiscoverOriginNames(Koto owner, IReadOnlyList<string> written, BindingScope scope)
    {
        if (!this.discoveredOrigins.TryGetValue(owner, out var names))
        {
            return written;
        }

        // A name already visible names that Origin; only the remaining names are introduced by the signature (SPEC 15.3.4).
        for (var i = names.Count - 1; i >= 0; i--)
        {
            var name = names[i];
            for (var current = scope; current is not null; current = current.Parent)
            {
                if (current.Origins?.ContainsKey(name) == true ||
                    (current.Values.TryGetValue(name, out var value) && value.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Capture &&
                        (value.Kind != BindingSymbolKind.Local || value.Declaration.Span.Start <= owner.Span.Start)) ||
                    this.originDeclarations.GetValueOrDefault(current.Owner)?.Sets.ContainsKey(name) == true)
                {
                    names.RemoveAt(i);
                    ((OriginNameList)names).Spans.RemoveAt(i);
                    break;
                }
            }
        }

        if (owner is FunctionKoto function)
        {
            function.SetOrigins(names);
        }
        else if (owner is PropertyAccessorKoto accessor)
        {
            accessor.Origins = names;
        }

        return names;
    }

    private OriginDeclaration? BeginOriginDeclaration(Koto owner, BindingScope scope)
    {
        if (!this.originDeclarations.TryGetValue(owner, out var declaration) || declaration.State != 0)
        {
            return null;
        }

        declaration.Scope = scope;
        declaration.State = 1;
        foreach (var entry in declaration.Sets)
        {
            for (var current = scope; current is not null; current = current.Parent)
            {
                var scalar = current.Origins?.ContainsKey(entry.Key) == true;
                var value = current.Values.TryGetValue(entry.Key, out var symbol) && symbol.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Capture &&
                    (symbol.Kind != BindingSymbolKind.Local || symbol.Declaration.Span.Start <= owner.Span.Start);
                // Like a value, a local's set is visible only to declarations that follow it (SPEC 15.3.1, 15.4.4).
                var local = current.OriginSets?.GetValueOrDefault(entry.Key);
                var set = this.originDeclarations.GetValueOrDefault(current.Owner)?.Sets.GetValueOrDefault(entry.Key) ??
                    (local is not null && OriginOwner(local) is { } localOwner && localOwner.Span.Start <= owner.Span.Start ? local : null);
                if (scalar || value || (set is not null && !ReferenceEquals(set, entry.Value)))
                {
                    this.Fail(entry.Value, BindingFailure.Duplicate);
                    break;
                }
            }
        }

        return declaration;
    }

    // A name that resolves to a binding set that failed as a repeat yields that set as repeated (RestOnRepeatedSet) beside its Type.
    private bool OriginCandidate(string name, Koto use, BindingScope scope, out BoundOrigin? scalar, out BoundType? carrier, out TypeSemanticsKoto? repeated)
    {
        scalar = null;
        carrier = null;
        repeated = null;
        var owner = OriginOwner(use);
        if (owner is not null && !ReferenceEquals(owner, scope.Owner) &&
            this.originDeclarations.GetValueOrDefault(owner)?.Sets.TryGetValue(name, out var localSet) == true)
        {
            carrier = this.BindOriginSetType(localSet, scope);
            repeated = RepeatedSet(localSet) ? localSet : null;
            return true;
        }

        for (var current = scope; current is not null; current = current.Parent)
        {
            var visibleValue = current.Values.TryGetValue(name, out var competing) &&
                competing.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Capture &&
                (competing.Kind != BindingSymbolKind.Local || ReferenceEquals(competing.Declaration, owner) || competing.Declaration.Span.End <= use.Span.Start);
            var sets = this.originDeclarations.GetValueOrDefault(current.Owner)?.Sets;
            if (visibleValue && (current.Origins?.ContainsKey(name) == true || sets?.ContainsKey(name) == true))
            {
                this.Fail(use, BindingFailure.Duplicate);
                return true;
            }

            if (sets?.TryGetValue(name, out var set) == true)
            {
                carrier = this.BindOriginSetType(set, current);
                repeated = RepeatedSet(set) ? set : null;
                return true;
            }

            if (current.OriginSets?.TryGetValue(name, out var local) == true && OriginOwner(local) is { } localOwner &&
                (ReferenceEquals(localOwner, owner) || localOwner.Span.End <= use.Span.Start))
            {
                carrier = this.BindOriginSetType(local, current);
                repeated = RepeatedSet(local) ? local : null;
                return true;
            }

            if (current.Origins?.TryGetValue(name, out scalar) == true)
            {
                return true;
            }

            var count = InputCount(current.Owner);
            for (var i = 0; i < count; i++)
            {
                if (InputName(current.Owner, i) == name)
                {
                    var nameNode = use is MemberAccessKoto member ? member.Left : use;
                    if (current.Values.TryGetValue(name, out var parameter))
                    {
                        nameNode.BoundSymbol = parameter;
                    }

                    carrier = this.BoundInputType(current.Owner, i) ?? (InputType(current.Owner, i) is { } syntax ? this.BindType(syntax, current) : null);
                    return true;
                }
            }

            if (current.Values.TryGetValue(name, out var symbol) &&
                symbol.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Capture)
            {
                if (symbol.Kind == BindingSymbolKind.Local && !ReferenceEquals(symbol.Declaration, owner) && symbol.Declaration.Span.End > use.Span.Start)
                {
                    continue;
                }

                (use is MemberAccessKoto member ? member.Left : use).BoundSymbol = symbol;
                carrier = symbol.Type ?? (symbol.Declaration is VariableKoto { TypeKoto: { } type } ? this.BindType(type, current) : null);

                return true; // An unsuitable nearest candidate never falls through.
            }
        }

        return false;
    }

    // SPEC 15.3.1, 23.3.6.4: a name that resolves to a binding set that failed as a repeat names neither that set nor the outer
    // declaration it repeats, so the Origin it would denote, as in a clause attached to the same declaration or `during a` beside
    // it, rests on that DuplicateBinding_Kd and reports nothing of its own; it never resolves to the outer name.
    private void RestOnRepeatedSet(Koto use, TypeSemanticsKoto set)
    {
        if (use.BindingFailure == BindingFailure.None)
        {
            this.prerequisites[use] = (this.prerequisiteStore.Count, 1);
            this.prerequisiteStore.Add(set);
            this.Fail(use, BindingFailure.MissingName, true);
        }
    }

    private BoundType? BindOriginSetType(TypeSemanticsKoto syntax, BindingScope scope)
    {
        if (syntax.BoundType is { } known)
        {
            return known;
        }

        var symbol = this.TypeName(syntax, scope, false);
        return symbol?.Declaration is GroupKoto
            ? this.BindContainerQualifier(syntax, symbol, scope, this.TypeContext(syntax, scope))
            : this.BindType(syntax, scope);
    }

    private BoundOrigin? PendingOrigin(Koto use, BindingScope scope, TypeBindingContext context, LoanRequirement requirement, int slot, BindingSymbol? borrowCondition)
    {
        var owner = OriginOwner(use);
        if (owner is null || !this.originDeclarations.TryGetValue(owner, out var declaration) || declaration.State != 1 ||
            (context.Position == TypePosition.Parameter && slot < 0 && context.Direct))
        {
            return null;
        }

        for (var i = 0; i < declaration.Pending.Count; i++)
        {
            if (ReferenceEquals(declaration.Pending[i].Use, use) && declaration.Pending[i].Slot == slot)
            {
                return declaration.Pending[i].Origin;
            }
        }

        var origin = this.OriginAtom(use, OriginKind.Unbound, slot);
        declaration.Pending.Add(new(use, scope, context, requirement, slot, origin, borrowCondition));
        return origin;
    }

    private BoundOrigin ResolveOrigin(BoundOrigin origin, OriginDeclaration declaration, int depth = 0)
    {
        if (depth > declaration.Replacements.Count)
        {
            return origin;
        }

        if (declaration.Replacements.TryGetValue(origin, out var replacement))
        {
            return this.ResolveOrigin(replacement, declaration, depth + 1);
        }

        if (origin.Kind == OriginKind.Intersection)
        {
            var result = BoundOrigin.Static;
            for (var i = 0; i < origin.Operands.Count; i++)
            {
                result = this.Meet(result, this.ResolveOrigin(origin.Operands[i], declaration, depth));
            }

            return result;
        }

        return origin;
    }

    private void CompleteOriginDeclaration(OriginDeclaration? declaration)
    {
        if (declaration is null || declaration.State != 1)
        {
            return;
        }

        var clauses = OriginClauses.Get(declaration.Owner);
        for (var i = 0; i < clauses.Count; i++)
        {
            var clause = clauses[i];
            var a = this.BindOrigin(clause.Left, declaration.Scope!);
            var b = this.BindOrigin(clause.Right, declaration.Scope!);
            if (a is not null && b is not null)
            {
                declaration.Relations.Add(new(a, b, clause.IsEquality, clause));
                clause.BindingState = BindingState.Resolved;
            }
        }

        // Equalities are substitutions before result defaulting. Revisit equations after
        // each substitution so chains are independent of clause order.
        for (var pass = 0; pass <= declaration.Relations.Count; pass++)
        {
            var changed = false;
            foreach (var relation in declaration.Relations)
            {
                if (!relation.Equality)
                {
                    continue;
                }

                var a = this.ResolveOrigin(relation.Longer, declaration);
                var b = this.ResolveOrigin(relation.Shorter, declaration);
                if (ReferenceEquals(a, b))
                {
                    continue;
                }

                var leftRank = Flexibility(a);
                var rightRank = Flexibility(b);
                if (rightRank > leftRank || (rightRank == leftRank && rightRank != 0 && CompareOrigins(a, b) < 0))
                {
                    (a, b) = (b, a);
                }

                if (Flexibility(a) != 0 && !Contains(b, a))
                {
                    declaration.Replacements[a] = b;
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        declaration.State = 2; // Further omission performs the established position rule.
        foreach (var pending in declaration.Pending)
        {
            var origin = this.ResolveOrigin(pending.Origin, declaration);
            if (origin.Kind != OriginKind.Unbound || !ReferenceEquals(origin, pending.Origin))
            {
                continue;
            }

            // Only a nontrivial explicit outlives clause universally quantifies a result
            // slot. Intrinsic well-formedness obligations do not change result elision.
            var quantified = false;
            if (pending.Context.Position == TypePosition.Result && ReferenceEquals(pending.Context.Owner, declaration.Owner) && pending.Slot >= 0)
            {
                foreach (var relation in declaration.Relations)
                {
                    var a = this.ResolveOrigin(relation.Longer, declaration);
                    var b = this.ResolveOrigin(relation.Shorter, declaration);
                    if (!relation.Equality && !OriginOutlives(a, b) && (Contains(a, origin) || Contains(b, origin)))
                    {
                        quantified = true;
                        break;
                    }
                }
            }

            BoundOrigin? completed;
            if (quantified && declaration.Owner.BoundSymbol is { } symbol)
            {
                var slots = symbol.AggregateInputOrigins ??= new();
                completed = null;
                for (var i = 0; i < slots.Count; i++)
                {
                    if (ReferenceEquals(slots[i].Occurrence, pending.Use) && slots[i].TargetSlot == pending.Slot)
                    {
                        completed = slots[i];
                        break;
                    }
                }

                if (completed is null)
                {
                    completed = new(OriginKind.Input, declaration.Owner, InputCount(declaration.Owner) + slots.Count)
                    {
                        InputIndex = -1,
                        Occurrence = pending.Use,
                        TargetSlot = pending.Slot,
                    };
                    slots.Add(completed);
                }
            }
            else
            {
                // SPEC 15.4.1: the slot completes as it would have without the clauses, including its Semantics condition.
                completed = this.OmittedOrigin(pending.Use, pending.Scope, pending.Context, pending.Requirement, pending.Slot, pending.BorrowCondition);
            }

            if (completed is not null && !ReferenceEquals(completed, origin))
            {
                declaration.Replacements[origin] = completed;
            }
        }

        var visitor = this.originRewriteVisitor ??= new(this);
        visitor.Declaration = declaration;
        visitor.Visit(declaration.Owner);
        visitor.Declaration = null;
        for (var i = 0; i < declaration.Relations.Count; i++)
        {
            var relation = declaration.Relations[i];
            declaration.Relations[i] = relation with
            {
                Longer = this.ResolveOrigin(relation.Longer, declaration),
                Shorter = this.ResolveOrigin(relation.Shorter, declaration),
            };
        }

        declaration.State = 3;

        int Flexibility(BoundOrigin origin)
        {
            if (origin.Kind == OriginKind.Unbound)
            {
                // An enclosing input can complete an equality class containing a nested
                // signature or result. Such dependent occurrences must not elide first.
                foreach (var pending in declaration.Pending)
                {
                    if (ReferenceEquals(pending.Origin, origin))
                    {
                        return !ReferenceEquals(pending.Context.Owner, declaration.Owner) ? 4 :
                            pending.Context.Position == TypePosition.Parameter ? 2 : 3;
                    }
                }

                return 3;
            }

            return ReferenceEquals(origin.Binder, declaration.Owner) && declaration.Owner is FunctionKoto or PropertyAccessorKoto &&
                origin.Kind is OriginKind.Parameter or OriginKind.Input ? 1 : 0;
        }

        static bool Contains(BoundOrigin expression, BoundOrigin atom)
        {
            if (ReferenceEquals(expression, atom))
            {
                return true;
            }

            for (var i = 0; i < expression.Operands.Count; i++)
            {
                if (Contains(expression.Operands[i], atom))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private BoundType RewriteOrigins(BoundType type, OriginDeclaration declaration)
    {
        if (!type.CarriesOrigin)
        {
            return type;
        }

        var components = this.RentTypes(type.Components.Count);
        var arguments = this.originScratch.Rent(type.OriginArguments.Count);
        try
        {
            for (var i = 0; i < type.Components.Count; i++)
            {
                components[i] = this.RewriteOrigins(type.Components[i], declaration);
            }

            for (var i = 0; i < type.OriginArguments.Count; i++)
            {
                arguments[i] = this.ResolveOrigin(type.OriginArguments[i], declaration);
            }

            return this.InternType(type.Kind, type.Symbol, type.Semantics, components.AsSpan(0, type.Components.Count), type.Length, type.Origin is { } origin ? this.ResolveOrigin(origin, declaration) : null, arguments.AsSpan(0, type.OriginArguments.Count), type.LengthExpression, type.ClosureContext, type.LengthArguments, type.ResultMode);
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
            this.originScratch.Return(arguments, clearArray: true);
        }
    }

    private BoundType ResolveInitializerOrigins(BoundType type, OriginDeclaration declaration, BindingScope scope)
    {
        declaration.Scope = scope;
        declaration.State = 3;
        for (var i = this.obligations.Count - 1; i >= 0; i--)
        {
            var obligation = this.obligations[i];
            if (obligation.Kind == BindingObligationKind.OriginInference && obligation.Longer is { } pending && declaration.Replacements.ContainsKey(pending))
            {
                this.obligationSet.Remove(obligation);
                this.obligations.RemoveAt(i);
            }
        }

        return this.RewriteOrigins(type, declaration);
    }

    private BoundType InferLocalOrigins(BoundType declared, BoundType actual, VariableKoto owner, BindingScope scope)
    {
        if (!declared.CarriesOrigin || !actual.CarriesOrigin)
        {
            return declared;
        }

        OriginDeclaration? declaration = null;
        var localSlot = -1;
        Match(declared, actual, OriginVariance.Covariant);
        if (declaration is null)
        {
            return declared;
        }

        if (declaration.Inferred is { Count: > 0 } inferred)
        {
            this.MeetLocalUpperBounds(declaration, inferred);
        }

        declaration.Scope = scope;
        declaration.State = 3;
        var visitor = this.originRewriteVisitor ??= new(this);
        visitor.Declaration = declaration;
        visitor.Visit(owner.TypeKoto!);
        visitor.Declaration = null;
        for (var i = this.obligations.Count - 1; i >= 0; i--)
        {
            var obligation = this.obligations[i];
            if (obligation.Kind == BindingObligationKind.OriginInference && obligation.Longer is { } pending && declaration.Replacements.ContainsKey(pending))
            {
                this.obligationSet.Remove(obligation);
                this.obligations.RemoveAt(i);
            }
        }

        return this.RewriteOrigins(declared, declaration);

        // `polarity` is the variance of the position within the local's Type, composed as FitsTypeCore compares it.
        void Match(BoundType pattern, BoundType value, OriginVariance polarity)
        {
            if (pattern.Kind != value.Kind || pattern.Semantics != value.Semantics || !ReferenceEquals(pattern.Symbol, value.Symbol))
            {
                return;
            }

            if (pattern.Origin is { } p && value.Origin is { } a)
            {
                Bind(p, a, polarity);
            }

            for (var i = 0; i < Math.Min(pattern.OriginArguments.Count, value.OriginArguments.Count); i++)
            {
                Bind(pattern.OriginArguments[i], value.OriginArguments[i], ComposeVariance(polarity, pattern.Symbol?.Schema?.Origins[i].Variance ?? OriginVariance.Invariant));
            }

            for (var i = 0; i < Math.Min(pattern.Components.Count, value.Components.Count); i++)
            {
                var component = pattern.Kind is BoundTypeKind.Semantics or BoundTypeKind.SemanticsApplication ? (IsInvariantLayer(pattern, this) ? OriginVariance.Invariant : OriginVariance.Covariant)
                    : pattern.Kind == BoundTypeKind.Function && pattern.Components.Count == 2 ? (i == 0 ? OriginVariance.Contravariant : OriginVariance.Covariant)
                    : pattern.Kind == BoundTypeKind.Constructed && pattern.Symbol?.Schema is { } schema && i < schema.GenericSlots.Count ? schema.GenericSlots[i].OriginVariance
                    : OriginVariance.Covariant;
                Match(pattern.Components[i], value.Components[i], ComposeVariance(polarity, component));
            }
        }

        void Bind(BoundOrigin pending, BoundOrigin value, OriginVariance polarity)
        {
            if (pending.Kind == OriginKind.Inference && !IsLocalRegion(pending))
            {
                declaration ??= this.OriginDeclarationFor(owner);
                if (IsMutableDeclaration(owner))
                {
                    var region = declaration.Replacements.TryGetValue(pending, out var previous) && IsLocalRegion(previous)
                        ? previous : this.LocalRegionSlot(owner, localSlot--);
                    declaration.Replacements[pending] = region;
                    if (!ReferenceEquals(value, pending))
                    {
                        this.AddOriginFit(value, region, declared, owner.InitializerKoto!, polarity);
                    }
                }
                else
                {
                    declaration.Replacements[pending] = declaration.Replacements.TryGetValue(pending, out var previous) ? this.Meet(previous, value) : value;
                }

                if (declaration.Relations.Count != 0)
                {
                    // Only a local's own clauses read the variance (MeetLocalUpperBounds, JudgeDeclaredRelation).
                    var inferred = declaration.Inferred ??= new(ReferenceEqualityComparer.Instance);
                    inferred[pending] = inferred.TryGetValue(pending, out var seen) && seen != polarity ? OriginVariance.Invariant : polarity;
                }
            }
        }
    }

    // SPEC 15.3.6, 15.4.4: an omitted Origin at a covariant position of a local's Type is the meet of all its upper bounds, so a clause
    // `origin y outlives x.s` bounds the inferred x.s together with the initializer, instead of requiring y to outlive the initializer's
    // Origin. The meet only shrinks, so the passes reach a fixed point independent of clause order.
    private void MeetLocalUpperBounds(OriginDeclaration declaration, Dictionary<BoundOrigin, OriginVariance> inferred)
    {
        for (var pass = 0; pass <= declaration.Relations.Count; pass++)
        {
            var changed = false;
            foreach (var relation in declaration.Relations)
            {
                if (relation.Equality || !inferred.TryGetValue(relation.Shorter, out var variance) || variance != OriginVariance.Covariant ||
                    !declaration.Replacements.TryGetValue(relation.Shorter, out var current))
                {
                    continue;
                }

                var longer = this.ResolveOrigin(relation.Longer, declaration);
                if (IsLocalRegion(current))
                {
                    this.AddObligation(new(BindingObligationKind.OriginOutlives, relation.Syntax, BindingDeadline.BodyOrigins, null, longer, current, Clause: relation.Syntax));
                    continue;
                }

                var met = this.Meet(current, longer);
                if (!ReferenceEquals(met, current))
                {
                    declaration.Replacements[relation.Shorter] = met;
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }
    }

    private sealed class OriginRewriteVisitor(Binding binding) : KotoVisitor
    {
        internal OriginDeclaration? Declaration { get; set; }

        public override void Visit(Koto node)
        {
            if (node.Parent is FunctionKoto function && (ReferenceEquals(node, function.Body) || ReferenceEquals(node, function.ExpressionBody)))
            {
                return;
            }

            var declaration = this.Declaration!;
            if (node.BoundType is { } type)
            {
                node.BoundType = binding.RewriteOrigins(type, declaration);
            }

            if (node.BoundOrigin is { } origin)
            {
                node.BoundOrigin = binding.ResolveOrigin(origin, declaration);
                if (!OriginVisible(node.BoundOrigin, node))
                {
                    binding.Fail(node, BindingFailure.InvalidOrigin);
                }
            }

            if (node.BoundSymbol is { Type: { } symbolType } symbol && ReferenceEquals(symbol.Declaration, node))
            {
                symbol.Type = binding.RewriteOrigins(symbolType, declaration);
            }

            node.VisitChildren(this);
        }
    }

    private sealed class OriginDeclaration(Koto owner)
    {
        internal Koto Owner { get; } = owner;

        internal BindingScope? Scope { get; set; }

        internal int State { get; set; }

        internal Dictionary<string, TypeSemanticsKoto> Sets { get; } = new(StringComparer.Ordinal);

        internal Dictionary<BoundOrigin, BoundOrigin> Replacements { get; } = new(ReferenceEqualityComparer.Instance);

        internal List<PendingOriginSlot> Pending { get; } = new(2);

        internal List<OriginRelation> Relations { get; } = new(2);

        // A local's omitted Origins inferred from its initializer (SPEC 15.4.4), with the variance of their positions in its Type;
        // allocated only for a local that has such Origins and kept across binds.
        internal Dictionary<BoundOrigin, OriginVariance>? Inferred { get; set; }

        internal void Reset()
        {
            this.State = 0;
            this.Scope = null;
            this.Sets.Clear();
            this.Replacements.Clear();
            this.Pending.Clear();
            this.Relations.Clear();
            this.Inferred?.Clear();
        }
    }

    private readonly record struct PendingOriginSlot(Koto Use, BindingScope Scope, TypeBindingContext Context, LoanRequirement Requirement, int Slot, BoundOrigin Origin, BindingSymbol? BorrowCondition);

    private readonly record struct OriginRelation(BoundOrigin Longer, BoundOrigin Shorter, bool Equality, OriginRelationKoto Syntax);
}
