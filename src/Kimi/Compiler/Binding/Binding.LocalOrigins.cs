// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// A local's omitted Origins (SPEC 15.3.6, 15.4.4): the Origins its initializer infers while it binds, and the Origins its annotation
// omits, each a region slot of the local.
// - Responsibility: ResolveInitializerOrigins closes the initializer's acquisition (initializerOrigins, whose bounds the fits record in
//   Binding.OriginProof.cs) and rewrites the initializer's Type; InferLocalOrigins matches the annotation against that Type, giving the
//   local's omitted slots regions (LocalRegionSlot, each row its slot's variance in the Type), `let` or `var` alike, whose bounds the
//   declaration records and whose `let` values FixDeclaredRegions fixes (Binding.LocalRegions.cs).
// - Inputs: the local's declared and initializer Types, the bounds its initializer's fits recorded.
// - Outputs: the replacements of the local's two Origin declarations, the local's rewritten Types and annotation nodes, the discharged
//   inference obligations of resolved Origins, and the region bounds that publish their resolutions.
// - Invariants: a local's Origins are resolved once, when its initializer is bound, before any later use reads its Type; the
//   initializer's declaration is closed (State 3) before the annotation's is matched.
// - Prohibitions: no judgment of a relation (Binding.OriginClauses.cs and the proof judge them); no region value (Binding.LocalRegions.cs
//   publishes it); no message text.
public sealed partial class Binding
{
    // Origins omitted in a local initializer's own Type expressions (SPEC 15.4.4), inferred while that initializer binds. They
    // are kept apart from declared Origin contracts, which are completed before bodies bind; entries are reused across binds.
    private readonly InitializerOriginRows initializerOrigins;

    // One bound of an Origin a local's initializer omits (SPEC 15.4.4), met with its earlier bounds.
    private void MeetInitializerBound(OriginDeclaration pending, BoundOrigin variable, BoundOrigin bound)
    {
        var replacements = pending.Forming.Replacements;
        replacements[variable] = replacements.TryGetValue(variable, out var previous) ? this.Meet(previous, bound) : bound;
        this.originStateVersion++;
    }

    // SPEC 15.4.4, 15.3.6: `local`'s initializer has acquired every bound. An Origin its initializer omits whose bounds reach no Origin
    // but its own variables (none, or a cycle among them) has no candidate and is a local region of `local` (DECISIONS R4-M112); each
    // resolved Origin's inference obligation is discharged, and the resolutions are published as region bounds, kept as their producers
    // read them at their uses (a fit's bound, a reference's binding; the owner's O-385 (a): never read again here). `slot` is the local's
    // next region slot (BindVariableCore).
    private BoundType ResolveInitializerOrigins(BoundType type, OriginDeclaration declaration, VariableKoto local, BindingScope scope, ref int slot)
    {
        for (var i = this.obligations.Count - 1; i >= 0; i--)
        {
            var obligation = this.obligations[i];
            if (obligation is not { Kind: BindingObligationKind.OriginInference, Longer: { } pending })
            {
                continue;
            }

            var bound = declaration.Forming.Replacements.ContainsKey(pending);
            if (!bound || declaration.Forming.Replacements.ContainsKey(this.ResolveOrigin(pending, declaration)))
            {
                if (IsWithin(pending.Binder!, local.InitializerKoto!) && ReferenceEquals(this.OpenInitializer(pending, obligation.Use), local))
                {
                    declaration.Forming.Replacements[pending] = this.LocalRegionSlot(local, slot--, this.OccurrenceVariance(type, pending, this));
                }
                else if (!bound)
                {
                    continue;
                }
            }

            this.obligationSet.Remove(new(obligation));
            this.obligations.RemoveAt(i);
        }

        declaration.Forming.Scope = scope;
        declaration.Forming.State = 3;
        this.originStateVersion++;
        foreach (var replacement in declaration.Forming.Replacements)
        {
            this.RecordRegionBound(this.ResolveOrigin(replacement.Value, declaration), replacement.Key, local);
        }

        return this.RewriteOrigins(type, declaration);
    }

    // SPEC 15.3.6, 15.4.4: every Origin the local's Type omits is a region slot of `owner`, `let` or `var` alike (Bind), numbered after the
    // initializer's: one the initializer's Type matches, whose bound the declaration's one fit records (CheckTypeUse), and one that no
    // fit binds, bound only to itself or no longer reached by the declared Type (a projection base's slot that the projection's
    // normalization drops), which has no bound from the initializer (DECISIONS R4-M112 (c)). The declaration fixes their values
    // (FixDeclaredRegions). `slot` is the local's next region slot (BindVariableCore).
    private BoundType InferLocalOrigins(BoundType declared, BoundType actual, VariableKoto owner, BindingScope scope, int slot)
    {
        OriginDeclaration? declaration = null;
        if (declared.CarriesOrigin && actual.CarriesOrigin)
        {
            Match(declared, actual);
        }

        // OmittedOrigin creates the local's declaration with the first Origin its Type omits.
        if (declaration is null && !this.originDeclarations.ContainsKey(owner))
        {
            return declared;
        }

        for (var i = this.obligations.Count - 1; i >= 0; i--)
        {
            var obligation = this.obligations[i];
            if (obligation is not { Kind: BindingObligationKind.OriginInference, Longer: { } pending })
            {
                continue;
            }

            if (declaration?.Forming.Replacements.ContainsKey(pending) != true)
            {
                if (!IsWithin(pending.Binder!, owner.TypeKoto!))
                {
                    continue;
                }

                Bind(pending);
            }

            this.obligationSet.Remove(new(obligation));
            this.obligations.RemoveAt(i);
        }

        if (declaration is null)
        {
            return declared;
        }

        declaration.Forming.Scope = scope;
        declaration.Forming.State = 3;
        this.originStateVersion++;
        var visitor = this.originRewriteVisitor ??= new(this);
        visitor.Declaration = declaration;
        visitor.Visit(owner.TypeKoto!);
        visitor.Declaration = null;
        return this.RewriteOrigins(declared, declaration);

        // The omitted slots the initializer's Type matches, position by position as RelationWalk pairs them.
        void Match(BoundType pattern, BoundType value)
        {
            if (pattern.Kind != value.Kind || pattern.Semantics != value.Semantics || !ReferenceEquals(pattern.Symbol, value.Symbol))
            {
                return;
            }

            if (pattern.Origin is { } p && value.Origin is not null)
            {
                Bind(p);
            }

            for (var i = 0; i < Math.Min(pattern.OriginArguments.Count, value.OriginArguments.Count); i++)
            {
                Bind(pattern.OriginArguments[i]);
            }

            for (var i = 0; i < Math.Min(pattern.Components.Count, value.Components.Count); i++)
            {
                Match(pattern.Components[i], value.Components[i]);
            }
        }

        // An omitted slot's region, its row the slot's variance in the local's Type (OccurrenceVariance); a slot matched again, or fixed
        // by an equality (FixedRegion), keeps its region.
        void Bind(BoundOrigin pending)
        {
            if (pending.Kind == OriginKind.Inference && !IsLocalRegion(pending))
            {
                declaration ??= this.OriginDeclarationFor(owner);
                if (!declaration.Forming.Replacements.TryGetValue(pending, out var previous) || !IsLocalRegion(previous))
                {
                    declaration.Forming.Replacements[pending] = this.LocalRegionSlot(owner, slot--, this.OccurrenceVariance(declared, pending, this));
                    this.originStateVersion++;
                }
            }
        }
    }

    /// <summary>The `initializerOrigins` family's one accessor (DECISIONS BU-220): a kept family, the Origins a local's initializer omits
    /// (SPEC 15.4.4), made once and reset in place by each pass (PrepareOriginDeclarations), kept in its key node's module's NodeColumns at
    /// its SyntaxId. Its members keep the names its readers use.</summary>
    private readonly struct InitializerOriginRows(Binding binding)
    {
        internal FamilyRows<OriginDeclaration?> Values => new(binding, static (table, _) => table.Hir.Families.InitializerOrigins.Enumerate());

        internal void Add(Koto node, OriginDeclaration value)
        {
            ref var row = ref HirTables.Of(node).Families.InitializerOrigins.Ref(node.SyntaxId);
            row = row is null ? value : throw new ArgumentException($"`initializerOrigins` already holds a row at `{node}`.", nameof(node));
        }

        internal bool ContainsKey(Koto node) => this.TryGetValue(node, out _);

        internal bool TryGetValue(Koto node, [NotNullWhen(true)] out OriginDeclaration? value)
            => (value = node.SyntaxOwner is { } table ? table.Hir.Families.InitializerOrigins.Get(node.SyntaxId) : null) is not null;
    }
}
