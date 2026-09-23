// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // Persist declaration storage across provisional/final binding passes. No environment is
    // allocated for declarations with neither written relations nor binding-set names.
    private readonly Dictionary<Koto, OriginDeclaration> originDeclarations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Koto, List<string>> discoveredOrigins = new(ReferenceEqualityComparer.Instance);
    private OriginRewriteVisitor? originRewriteVisitor;

    private static Koto? OriginOwner(Koto node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (current is FunctionKoto or PropertyAccessorKoto or DeclarationContainerKoto or AliasKoto or IsKoto { IsAssociatedConstraint: true } ||
                current is VariableKoto || current.Akind == KotoKind.EnumCase)
            {
                return current;
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

        foreach (var names in this.discoveredOrigins.Values)
        {
            names.Clear();
            ((OriginNameList)names).Spans.Clear();
        }

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
                    Fail(annotation, BindingFailure.Duplicate);
                }

                if (owner is VariableKoto and not PropertyKoto && owner.BoundSymbol is { } local)
                {
                    local.Scope.OriginSets ??= new(StringComparer.Ordinal);
                    if (!local.Scope.OriginSets.TryAdd(label, annotation))
                    {
                        Fail(annotation, BindingFailure.Duplicate);
                    }
                }

                continue;
            }

            if (annotation.OriginExpression is not { } expression)
            {
                continue;
            }

            // A stored field contributes directly written scalar names to its containing
            // Type. Nested function Types are a binding boundary, never implicit binders.
            var nested = false;
            for (var parent = annotation.Parent; parent is not null && !ReferenceEquals(parent, owner); parent = parent.Parent)
            {
                nested |= parent is FunctionTypeKoto;
            }

            if (nested)
            {
                continue;
            }

            var binder = owner;
            if (owner is PropertyKoto { DeclarationKind: PropertyDeclarationKind.Let or PropertyDeclarationKind.Var } || owner.Akind == KotoKind.EnumCase)
            {
                if (owner is PropertyKoto storage && !IsWithin(annotation, storage.TypeKoto))
                {
                    continue;
                }

                binder = owner.Parent!;
            }

            // SPEC 8.8.2: a specialization's written binder names are preliminary; CompleteSpecializationOrigins maps them to the original's.
            if ((binder is FunctionKoto function && (function.IsAnonymous || !IsSignature(annotation, function))) ||
                binder is not (FunctionKoto or PropertyAccessorKoto or StructKoto or EnumKoto))
            {
                continue;
            }

            Discover(expression, binder);
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

        static bool IsWithin(Koto node, Koto? root)
        {
            for (var current = node; current is not null; current = current.Parent)
            {
                if (ReferenceEquals(current, root))
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

        var closed = owner is DeclarationContainerKoto { HasOriginHeader: true };
        for (var i = names.Count - 1; i >= 0; i--)
        {
            var name = names[i];
            var existing = false;
            for (var current = scope; current is not null; current = current.Parent)
            {
                if (current.Origins?.ContainsKey(name) == true ||
                    (current.Values.TryGetValue(name, out var value) && value.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Capture &&
                        (value.Kind != BindingSymbolKind.Local || value.Declaration.Span.Start <= owner.Span.Start)) ||
                    this.originDeclarations.GetValueOrDefault(current.Owner)?.Sets.ContainsKey(name) == true)
                {
                    existing = true;
                    break;
                }
            }

            if (closed)
            {
                if (!written.Contains(name) && !existing)
                {
                    Fail(owner, BindingFailure.InvalidOrigin);
                }

                names.RemoveAt(i);
                ((OriginNameList)names).Spans.RemoveAt(i);
            }
            else if (existing)
            {
                // Headerless stored names cannot accidentally capture an inherited slot.
                if (owner is StructKoto or EnumKoto)
                {
                    Fail(owner, BindingFailure.InvalidOrigin);
                }

                names.RemoveAt(i);
                ((OriginNameList)names).Spans.RemoveAt(i);
            }
        }

        if (owner is StructKoto or EnumKoto && !closed && names.Count > 1)
        {
            Fail(owner, BindingFailure.InvalidOrigin);
        }

        if (owner is FunctionKoto function)
        {
            function.SetOrigins(names);
        }
        else if (owner is PropertyAccessorKoto accessor)
        {
            accessor.Origins = names;
        }
        else if (owner is DeclarationContainerKoto container && !closed)
        {
            container.SetImplicitOrigins(names);
        }

        return closed ? written : names;
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
                var set = this.originDeclarations.GetValueOrDefault(current.Owner)?.Sets.GetValueOrDefault(entry.Key) ?? current.OriginSets?.GetValueOrDefault(entry.Key);
                if (scalar || value || (set is not null && !ReferenceEquals(set, entry.Value)))
                {
                    Fail(entry.Value, BindingFailure.Duplicate);
                    break;
                }
            }
        }

        return declaration;
    }

    private bool OriginCandidate(string name, Koto use, BindingScope scope, out BoundOrigin? scalar, out BoundType? carrier)
    {
        scalar = null;
        carrier = null;
        var owner = OriginOwner(use);
        if (owner is not null && !ReferenceEquals(owner, scope.Owner) &&
            this.originDeclarations.GetValueOrDefault(owner)?.Sets.TryGetValue(name, out var localSet) == true)
        {
            carrier = this.BindOriginSetType(localSet, scope);
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
                Fail(use, BindingFailure.Duplicate);
                return true;
            }

            if (sets?.TryGetValue(name, out var set) == true)
            {
                carrier = this.BindOriginSetType(set, current);
                return true;
            }

            if (current.OriginSets?.TryGetValue(name, out var local) == true && OriginOwner(local) is { } localOwner &&
                (ReferenceEquals(localOwner, owner) || localOwner.Span.End <= use.Span.Start))
            {
                carrier = this.BindOriginSetType(local, current);
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

                    carrier = BoundInputType(current.Owner, i) ?? (InputType(current.Owner, i) is { } syntax ? this.BindType(syntax, current) : null);
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

    private BoundOrigin? PendingOrigin(Koto use, BindingScope scope, TypeBindingContext context, LoanRequirement requirement, int slot)
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
        declaration.Pending.Add(new(use, scope, context, requirement, slot, origin));
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
                completed = this.OmittedOrigin(pending.Use, pending.Scope, pending.Context, pending.Requirement, pending.Slot);
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

            return this.InternType(type.Kind, type.Symbol, type.Semantics, components.AsSpan(0, type.Components.Count), type.Length, type.Origin is { } origin ? this.ResolveOrigin(origin, declaration) : null, arguments.AsSpan(0, type.OriginArguments.Count), type.LengthExpression);
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
            this.originScratch.Return(arguments, clearArray: true);
        }
    }

    private BoundType InferLocalOrigins(BoundType declared, BoundType actual, VariableKoto owner, BindingScope scope)
    {
        if (!declared.CarriesOrigin || !actual.CarriesOrigin)
        {
            return declared;
        }

        OriginDeclaration? declaration = null;
        Match(declared, actual);
        if (declaration is null)
        {
            return declared;
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

        void Match(BoundType pattern, BoundType value)
        {
            if (pattern.Kind != value.Kind || pattern.Semantics != value.Semantics || !ReferenceEquals(pattern.Symbol, value.Symbol))
            {
                return;
            }

            if (pattern.Origin is { } p && value.Origin is { } a)
            {
                Bind(p, a);
            }

            for (var i = 0; i < Math.Min(pattern.OriginArguments.Count, value.OriginArguments.Count); i++)
            {
                Bind(pattern.OriginArguments[i], value.OriginArguments[i]);
            }

            for (var i = 0; i < Math.Min(pattern.Components.Count, value.Components.Count); i++)
            {
                Match(pattern.Components[i], value.Components[i]);
            }
        }

        void Bind(BoundOrigin pending, BoundOrigin value)
        {
            if (pending.Kind == OriginKind.Inference)
            {
                declaration ??= this.OriginDeclarationFor(owner);
                declaration.Replacements[pending] = declaration.Replacements.TryGetValue(pending, out var previous) ? this.Meet(previous, value) : value;
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
                    Fail(node, BindingFailure.InvalidOrigin);
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

        internal void Reset()
        {
            this.State = 0;
            this.Scope = null;
            this.Sets.Clear();
            this.Replacements.Clear();
            this.Pending.Clear();
            this.Relations.Clear();
        }
    }

    private readonly record struct PendingOriginSlot(Koto Use, BindingScope Scope, TypeBindingContext Context, LoanRequirement Requirement, int Slot, BoundOrigin Origin);

    private readonly record struct OriginRelation(BoundOrigin Longer, BoundOrigin Shorter, bool Equality, OriginRelationKoto Syntax);
}
