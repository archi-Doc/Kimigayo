// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 8.3.3 Recursion (R7-C2e2 e2-3b): the obligations being proven, each open at its depth (the number open when it opened; they
    // nest, every open and close a try/finally pair): a nominal `T is C` while ResolveConformance proves it, keyed by the obligation,
    // not the requester, and a declaration path while VerifyConformance verifies it (every instance), keyed by the path. A request
    // reaching an open one reads no evidence (Unknown).
    private readonly Dictionary<Obligation, int> openObligations = new();

    // SPEC 8.3.3 Finality: the least depth of an open obligation the answers in progress read (int.MaxValue: none), which an answer's
    // reader asks at its boundary (BeginAnswer, EndAnswer).
    private int dependence = int.MaxValue;

    // SPEC 8.6.3, 8.3.3 Finality (R7-C2e2 e2-3b Revision 4): how many of the open obligations the innermost requirement's evidence holds
    // pending: those open when it began (ProveRequirement); 0 in a formation read (a verification's body, the binding cache's formation,
    // the inferred bindings' collection: what it forms is judged where written, after the conformance holds). A projection whose route's
    // evidence depends on a held obligation names no Type in that evidence (EndHeldAnswer): its part stays the projection, so exactly the
    // judgments that read the part read the obligation pending. Written only by this file's brackets (BeginEvidence, BeginVerification,
    // BeginFormation and their ends) and ResetContracts; read only through HeldPending.
    private int heldObligations;

    // An open obligation's key: an instance's (Type, Contract), or a declaration path being verified.
    private readonly record struct Obligation(BoundType? Type, BindingSymbol? Contract, BoundConformancePath? Path);

    // A pair's overlap scope, kept with its left path (BoundConformancePath.OverlapScopes); both paths share their Type, so their store.
    private static BindingScope OverlapScope(BoundConformancePath a, BoundConformancePath b)
    {
        var scopes = a.OverlapScopes ??= new();
        for (var i = 0; i < scopes.Count; i++)
        {
            if (ReferenceEquals(scopes[i].Right, b))
            {
                return scopes[i].Scope;
            }
        }

        var scope = new BindingScope(a.Declaration, a.Type.Declaration); // SPEC 8.9.2: the pair's agreement is the Type's.
        scopes.Add((b, scope));
        return scope;
    }

    // SPEC 8.9.1: two conformances' conditions exclude each other when a's chain and b's hold facts of one subject that cannot both
    // hold: identities to different complete Types, or Semantics sets that share no member. Each TypeIdentity or Semantics fact of a's
    // chain meets b's facts of its kind and subject key in Closure(view of b)'s row (ContractSolver's Readers: nothing here proves).
    private bool DisjointConformanceFacts(BindingScope a, BindingScope b)
    {
        ViewIndex? index = null;
        try
        {
            for (var left = a; left is not null; left = left.Parent)
            {
                if (left.Constraints is not { } x)
                {
                    continue;
                }

                for (var right = b; right is not null; right = right.Parent)
                {
                    if (right.Constraints is not { } y)
                    {
                        continue;
                    }

                    foreach (var p in x.Facts)
                    {
                        if (p is not { Kind: ConstraintKind.TypeIdentity or ConstraintKind.Semantics, Subject: { } subject } ||
                            (index ??= this.BeginClosureRead(b)).Buckets.GetValueOrDefault((right, p.Kind, SubjectKey(subject).Symbol, SubjectKey(subject).Type)) is not { } bucket)
                        {
                            continue;
                        }

                        foreach (var (q, _) in bucket)
                        {
                            if (!ReferenceEquals(subject, q.Subject) || !this.AvailableConstraintFact(x, p, out _) || !this.AvailableConstraintFact(y, q, out _))
                            {
                                continue;
                            }

                            // Substitute explicit identities, then compare determined concrete atoms.
                            // No case splitting, contrapositive, or general satisfiability solver.
                            if (p.Kind == ConstraintKind.TypeIdentity ?
                                p.RequiredType is { } pt && q.RequiredType is { } qt && !this.DependentType(pt) && !this.DependentType(qt) && !this.SameCompleteType(pt, qt) :
                                (p.Mask & q.Mask) == 0)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
        }
        finally
        {
            if (index is not null)
            {
                this.EndClosureRead();
            }
        }

        return false;
    }

    private ConstraintProof VerifyConformanceAgreement(BoundConformance identity)
    {
        if (identity.Invalid)
        {
            return ConstraintProof.Error;
        }

        var proof = ConstraintProof.Proven;
        for (var i = 0; i < this.impls.PathsOf(identity).Count; i++)
        {
            var a = this.impls.PathsOf(identity)[i];
            var available = this.VerifyConformance(a);
            if (a.InheritedFrom is not null && available != ConstraintProof.Proven)
            {
                if (available != ConstraintProof.Refuted)
                {
                    proof = CombineProof(proof, available, true);
                }

                continue; // An unavailable inherited path is not a declaration obligation.
            }

            proof = CombineProof(proof, available, true);
            for (var j = 0; j < i; j++)
            {
                var b = this.impls.PathsOf(identity)[j];
                if (b.InheritedFrom is not null && !b.IsVerified)
                {
                    continue;
                }

                if (!a.IsVerified || !b.IsVerified)
                {
                    proof = CombineProof(proof, ConstraintProof.Unknown, true);
                    continue;
                }

                // Only refutation in an independently valid environment establishes exclusion.
                // Arbitrary errors (invalid names, declarations, cycles) are never disjointness.
                var self = this.SelfType(a.Type);
                var ab = this.ProveConformanceConditions(a, self, b.Scope);
                var ba = this.ProveConformanceConditions(b, self, a.Scope);
                var disjoint = this.DisjointConformanceFacts(a.Scope, b.Scope);
                if (ab == ConstraintProof.Error || ba == ConstraintProof.Error)
                {
                    // Substitution can expose a directly inconsistent intersection of two
                    // individually valid paths. Only the concrete fact check may exclude it.
                    if (!disjoint)
                    {
                        proof = ConstraintProof.Error;
                        continue;
                    }
                }

                if (ab == ConstraintProof.Refuted || ba == ConstraintProof.Refuted || disjoint)
                {
                    continue;
                }

                var scope = OverlapScope(a, b);

                // SPEC 8.9.2: the pair's intersection is a's conformance scope with every premise of b's, read along b's scope chain where
                // b keeps them (a declared path's in its block, an inherited path's in its own scope), as the Contract agreement check
                // reads them, so the judgment does not depend on which path is declared first.
                scope.Reset();
                scope.Parent = a.Scope;
                this.AddOverlapFacts(scope, b.Scope);

                var same = a.WitnessStorage.Count == b.WitnessStorage.Count && a.AssociatedStorage.Count == b.AssociatedStorage.Count && a.PropertyWitnessStorage.Count == b.PropertyWitnessStorage.Count;
                for (var w = 0; w < a.WitnessStorage.Count; w++)
                {
                    var witness = a.WitnessStorage[w];
                    same &= b.WitnessMap.TryGetValue(witness.Identity, out var otherWitness) && ReferenceEquals(witness.Implementation, otherWitness.Implementation) && this.SameFunctionWitness(witness.Function, otherWitness.Function, scope);
                }

                foreach (var binding in a.AssociatedStorage)
                {
                    same &= b.AssociatedStorage.TryGetValue(binding.Key, out var otherType) && this.SameConformanceType(binding.Value, otherType, scope);
                }

                for (var w = 0; w < a.PropertyWitnessStorage.Count; w++)
                {
                    var x = a.PropertyWitnessStorage[w];
                    if (!b.PropertyWitnessMap.TryGetValue((x.Identity, x.Requirement.Kind), out var y))
                    {
                        same = false;
                        continue;
                    }

                    same &= this.SamePropertyWitness(x, y, scope);
                }

                if (!same)
                {
                    identity.Invalid = true;
                    this.Record(a.Declaration, BindingFailure.IncompatibleImplementation, []);
                    this.Record(b.Declaration, BindingFailure.IncompatibleImplementation, []);
                    proof = ConstraintProof.Error;
                }
            }
        }

        identity.IsVerified = proof == ConstraintProof.Proven;
        return proof;
    }

    private bool SameConformanceType(BoundType? a, BoundType? b, BindingScope scope)
        => ReferenceEquals(a, b) || (a is not null && b is not null && this.ProveConstraint(this.InternConstraint(new(ConstraintKind.TypeIdentity, this.ContractType(a, scope), this.ContractType(b, scope))), scope) == ConstraintProof.Proven);

    // SPEC 8.6.1, 8.2.3: a conformance's bound Contract reference `formal`, in its declaration's terms, names the bound reference
    // `required` of the same Contract for `receiver` when its instance is one Type with `required` in `scope`, judged as two paths are
    // (SameConformanceType): the substitution can leave a projection over the receiver's arguments that names a Type
    // (`Iter<S.(HasItem).Item>` of `B<SrcI>` is `Iter<i32>`), or a premise of `scope` names one, so the substituted form's reference
    // decides no identity.
    private bool NamesReference(BoundType formal, BoundType receiver, BoundType required, BindingScope scope)
        => ReferenceEquals(formal.Symbol, required.Symbol) && this.StoredType(formal, receiver) is { } instance &&
            this.SameConformanceType(instance, required, scope);

    private void BindConditionalDeclarations(bool conditions)
    {
        for (var n = 0; n < this.nodes.Count; n++)
        {
            if (this.nodes[n] is not SyntaxFormKoto { Akind: KotoKind.ConditionalConformance, Parent: DeclarationContainerKoto container } syntax)
            {
                continue;
            }

            if (container is not (StructKoto or EnumKoto) || syntax.Operands.Length is not (2 or 3) || syntax.Operands[0] is not IsKoto target || syntax.Operands[1] is not SyntaxFormKoto premises)
            {
                this.Record(syntax, BindingFailure.InvalidConstraint, []);
                continue;
            }

            var outer = this.scopes[container];
            var scope = this.GetScope(syntax, outer);
            if (IsRecovery(target, out _) || IsRecovery(target.Right, out _))
            {
                // SPEC 23.1.2: a head whose subject or ContractReference Parsing rejected (F.6.2) explains the conformance's verdicts.
                this.AddPrerequisite(syntax, IsRecovery(target, out _) ? target : target.Right);
            }

            if (!conditions)
            {
                this.ValidateConditionalBlock(syntax, container);
                this.BindConstraint(target, outer);
                // A subject other than Self is Parsing's record (F.6.2, ParseConformanceConditions); its head rests (the prerequisite above).
                if (container.BoundSymbol!.Schema!.GenericSlots.Count == 0 || target.BoundConstraint is not { Kind: ConstraintKind.Contract or ConstraintKind.Unresolved } || premises.Operands.Length == 0)
                {
                    this.Record(syntax, BindingFailure.InvalidConstraint, []);
                    continue;
                }

                if (target.BoundConstraint.Kind == ConstraintKind.Unresolved)
                {
                    continue; // A pending Contract reference (SPEC 9.2, 9.5) registers nothing yet; the pending loop leaves the conformance and its Type pending (PendingDeclaration).
                }

                var contract = target.BoundConstraint.Contract!;

                if (this.IsClosedContractGrant(contract, container.BoundSymbol!))
                {
                    this.Record(target, BindingFailure.ClosedContractConformance, []); // SPEC 8.7: the conforming Types of a closed Contract are fixed.
                }
                else if (IntrinsicContract.Of(contract.Intrinsic).Declaration is not BindingFailure.None and var failure)
                {
                    this.Record(target, failure, []); // SPEC 8.7.1: decided by the Type's structure alone.
                }
                else
                {
                    this.RegisterConformanceDeclaration(container.BoundSymbol!, contract, target, scope, premises);
                    if (ReferenceEquals(contract, this.Library.Copy))
                    {
                        this.RegisterCopy(target, container, scope, premises);
                    }
                }

                continue;
            }

            var valid = syntax.BindingState != BindingState.Invalid;
            // Bind ordinary parameter facts before projections, independent of clause order.
            for (var pass = 0; pass < 2; pass++)
            {
                for (var i = 0; i < premises.Operands.Length; i++)
                {
                    if (premises.Operands[i] is not IsKoto condition)
                    {
                        valid = false;
                        continue;
                    }

                    if (this.DeferredConstraint(condition, scope) != (pass != 0))
                    {
                        continue;
                    }

                    this.BindConstraint(condition, scope);
                    this.AddPrerequisite(syntax, condition);
                    this.AddPrerequisite(target, condition);
                    var subject = condition.BoundConstraint?.Subject ?? condition.Left.BoundType;
                    while (subject?.Kind == BoundTypeKind.AssociatedProjection)
                    {
                        subject = subject.Components[0];
                    }

                    // A pending subject (BindConstraint) is judged by its root, the base its projection chain published.
                    valid &= condition.BoundConstraint is { } requirement && PositiveRequirement(requirement) &&
                        (subject?.Symbol ?? SubjectBase(condition.Left).BoundSymbol) is { } parameter && ContainerSlot(container, parameter) >= 0;
                }

                this.ExpandScopeContractPremises(scope);
            }

            Complete(premises, BoundType.Boolean);
            if (valid)
            {
                Complete(syntax, BoundType.Unit);
            }
            else
            {
                this.Record(syntax, BindingFailure.InvalidConstraint, []);
                this.FailFacts(scope, syntax); // R7-C3: an invalid condition's premises fail with its judgment, never the environment
            }
        }
    }

    private ConstraintProof ResolveConformance(BoundType type, BindingSymbol contract, BindingScope scope, out BoundConformancePath? evidence)
    {
        evidence = null;
        if (this.contractHeadersReady && this.BuiltinArithmeticConformance(type, contract, scope))
        {
            return contract.Declaration.BindingState == BindingState.Invalid ? ConstraintProof.Error : ConstraintProof.Proven;
        }

        // SPEC 4.6.1, 8.7, 22.1: a conformance the library witness table records for a built-in Type.
        if (this.contractHeadersReady && WitnessGroup(type, contract.LibraryDeclaration) is not null)
        {
            return contract.Declaration.BindingState == BindingState.Invalid ? ConstraintProof.Error : ConstraintProof.Proven;
        }

        if (this.contractHeadersReady && contract.LibraryDeclaration is KimiDeclarationId.Equatable or KimiDeclarationId.Comparable &&
            TyCtxt.OperatorTableRuns(type, KimiDeclarationId.Equatable))
        {
            return contract.Declaration.BindingState == BindingState.Invalid ? ConstraintProof.Error
                : TyCtxt.OperatorTableRuns(type, contract.LibraryDeclaration) ? ConstraintProof.Proven : ConstraintProof.Refuted;
        }

        if (this.contractHeadersReady && contract.LibraryDeclaration is KimiDeclarationId.Equatable or KimiDeclarationId.Comparable && TyCtxt.IsComposite(type))
        {
            return contract.Declaration.BindingState == BindingState.Invalid
                ? ConstraintProof.Error : this.ComparisonProof(type, contract, scope, false, out _);
        }

        // SPEC 8.3: a primitive's conformances are its intrinsic capabilities and the built-in comparison and formatting
        // conformances above; no declaration can add another (SPEC 8.7.1), so its fixed environment refutes the rest. Never
        // has none of the built-in ones (SPEC 3.1.3).
        if (this.contractHeadersReady && this.capabilityMode == BindingMode.Final && type.Kind == BoundTypeKind.Primitive &&
            contract.Intrinsic == IntrinsicKind.None)
        {
            return contract.Declaration.BindingState == BindingState.Invalid ? ConstraintProof.Error : ConstraintProof.Refuted;
        }

        // SPEC 22.1: the standard collections declare their conformances in Kimigayo behind their compiler-managed kinds.
        if (!this.contractHeadersReady || type.Kind is not (BoundTypeKind.Nominal or BoundTypeKind.Constructed or BoundTypeKind.Array or BoundTypeKind.Dictionary or BoundTypeKind.Slice) || type.Symbol is not { } symbol)
        {
            return ConstraintProof.Unknown;
        }

        if (this.OperandContexts(type).Invalid || contract.Declaration.BindingState == BindingState.Invalid)
        {
            return ConstraintProof.Error;
        }

        var identity = this.ConformanceOf(symbol, contract);
        if (identity is null && contract.Type is { } contractReference && !ReferenceEquals(contractReference.Symbol, contract) && this.ConformancesOf(symbol) is { } bindings)
        {
            foreach (var candidate in bindings)
            {
                if (candidate.Contract.Type is { } formal && this.NamesReference(formal, type, contractReference, scope))
                {
                    identity = candidate;
                    break;
                }
            }
        }

        if (identity is null && !IsBoundContractReference(contract))
        {
            // SPEC 8.6.1: a Contract named without Type arguments (a requirement's owner) is satisfied by the one conformance
            // to a bound reference of its declaration; several such conformances stay undecided here.
            identity = this.ConformanceByDeclaration(symbol, contract, out var ambiguous);
            if (ambiguous)
            {
                return ConstraintProof.Unknown;
            }
        }

        if (identity is null || this.impls.PathsOf(identity).Count == 0)
        {
            return this.capabilityMode == BindingMode.Final && !this.PathNotRuledOut(type, symbol, contract, scope) ? ConstraintProof.Refuted : ConstraintProof.Unknown;
        }

        if (identity.Invalid)
        {
            return ConstraintProof.Error;
        }

        var key = new Obligation(type, contract, null);
        if (!this.OpenObligation(key))
        {
            return ConstraintProof.Unknown;
        }

        try
        {
            this.BeginProof(); // The paths are one disjunction (DECISIONS BU-22).
            var result = ConstraintProof.Refuted;
            for (var i = 0; i < this.impls.PathsOf(identity).Count; i++)
            {
                var path = this.impls.PathsOf(identity)[i];
                var definition = this.VerifyConformance(path);
                if (path.Unsupported && this.conformanceChecks == 0)
                {
                    // SPEC 23.1.1, 23.1.2: a check that needs a conformance limited by OCC-X rests on its one record (TakeFailedCause).
                    this.failedCause ??= path;
                    definition = ConstraintProof.Error;
                }

                var condition = this.PathEvidence(path, type, scope);
                var available = definition == ConstraintProof.Proven ? condition : condition == ConstraintProof.Error ? ConstraintProof.Error : definition;
                result = CombineProof(result, available, false);
                if (available == ConstraintProof.Proven)
                {
                    evidence ??= path;
                }
            }

            if (result == ConstraintProof.Proven)
            {
                result = CombineProof(result, this.InstanceFormation(type, scope), true);
            }

            if (result != ConstraintProof.Proven)
            {
                evidence = null;
            }

            return this.EndProof(result);
        }
        finally
        {
            this.openObligations.Remove(key);
        }
    }

    // The start of an answer whose dependence a reader asks: the depth now and the dependence of the answers enclosing it.
    private (int Depth, int Enclosing) BeginAnswer()
    {
        var mark = (this.openObligations.Count, this.dependence);
        this.dependence = int.MaxValue;
        return mark;
    }

    // SPEC 8.3.3 Finality ("a fact depends on a judgment when it does not hold with that judgment's result held pending"): whether the
    // answer begun at `mark`, with `verdict`, depends on an obligation open before it began: it read one (an obligation opened and closed
    // inside it reads at or above its depth) and is Unknown. A re-entry reads Unknown, so the answer already holds the obligation pending,
    // and the proof rules are monotone in an Unknown operand: a Proven or Refuted answer stays decided whatever the obligation settles to
    // (Error absorbs, but a failed obligation has its own record: record selection, Q13), so it depends on none. The enclosing answers
    // keep what it read.
    private bool EndAnswer((int Depth, int Enclosing) mark, ConstraintProof verdict)
    {
        var read = this.dependence < mark.Depth;
        this.dependence = Math.Min(mark.Enclosing, this.dependence);
        return read && verdict == ConstraintProof.Unknown;
    }

    // SPEC 8.6.3: the start of a requirement's evidence, an answer holding pending every obligation open when it began (heldObligations).
    private (int Depth, int Enclosing, int Held) BeginEvidence()
    {
        var mark = this.BeginAnswer();
        var held = this.heldObligations;
        this.heldObligations = mark.Depth;
        return (mark.Depth, mark.Enclosing, held);
    }

    // The evidence's verdict: Unknown when it depends on an obligation it held pending (EndAnswer), since it then supplies none.
    private ConstraintProof EndEvidence((int Depth, int Enclosing, int Held) mark, ConstraintProof verdict)
    {
        this.heldObligations = mark.Held;
        return this.EndAnswer((mark.Depth, mark.Enclosing), verdict) ? ConstraintProof.Unknown : verdict;
    }

    // Whether the evidence in progress holds pending the obligation at `depth` (int.MaxValue: none).
    private bool HeldPending(int depth) => depth < this.heldObligations;

    // Whether no evidence in progress holds an obligation pending: a formation read (the outermost obligation not held).
    private bool FormationRead => !this.HeldPending(0);

    // Whether the answer begun at `mark`, with `verdict`, depends (EndAnswer) on an obligation the evidence in progress holds pending: an
    // answer, such as a path's evidence, that supplies none to that evidence.
    private bool EndHeldAnswer((int Depth, int Enclosing) mark, ConstraintProof verdict)
    {
        var read = this.dependence;
        return this.EndAnswer(mark, verdict) && this.HeldPending(read);
    }

    // Opens an obligation at the next depth; when it is open already, the request reads no evidence there: false, and the answers in
    // progress read it.
    private bool OpenObligation(Obligation key)
    {
        ref var depth = ref CollectionsMarshal.GetValueRefOrAddDefault(this.openObligations, key, out var open);
        if (open)
        {
            this.dependence = Math.Min(this.dependence, depth);
            return false;
        }

        depth = this.openObligations.Count - 1;
        return true;
    }

    // Whether a request reaching `key` finds it open (it then reads no evidence, and the answers in progress read it).
    private bool ReadOpen(Obligation key)
    {
        if (!this.openObligations.TryGetValue(key, out var depth))
        {
            return false;
        }

        this.dependence = Math.Min(this.dependence, depth);
        return true;
    }

    // SPEC 8.3.3 Recursion, 8.6.3 (R7-C2e2 e2-3b Revision 4): opens a declaration path's verification (VerifyConformance,
    // VerifyInheritedConformance); false when it is open already. Its body is a formation read (a specification's identity atom,
    // signatures, normalization: judged where written, after the conformance holds), so it holds no obligation pending (`held`, the
    // enclosing count, which EndVerification restores); only its requirements' evidence does (BeginEvidence).
    private bool BeginVerification(BoundConformancePath path, out int held)
    {
        var opened = this.OpenObligation(new(null, null, path));
        held = opened ? this.BeginFormation() : this.heldObligations;
        return opened;
    }

    private void EndVerification(BoundConformancePath path, int held)
    {
        this.openObligations.Remove(new(null, null, path));
        this.EndFormation(held);
    }

    // SPEC 8.6.3: a formation read inside whatever answer reads it (a verification's body, the binding cache's formation, the inferred
    // bindings' collection): what it forms is judged where written, so it holds no obligation pending; EndFormation restores the count.
    private int BeginFormation()
    {
        var held = this.heldObligations;
        this.heldObligations = 0;
        return held;
    }

    private void EndFormation(int held) => this.heldObligations = held;

    // Whether a declaration path is being verified (its obligation open).
    private bool Verifying(BoundConformancePath path) => this.openObligations.ContainsKey(new(null, null, path));

    // SPEC 8.6.1, 8.6.3, 8.3.1: evidence about an instance of a generic declaration (a conformance, a capability, a stored instance)
    // holds only for the bindings the declaration's Constraints allow, so it rests on those Constraints at the instance's arguments (the
    // formation step alone, BeginTypeFormation and EndTypeFormation; R7-C2e2 e2-3b R2.5), each proven with SPEC 8.3.3's re-entry rule: a
    // Constraint whose obligation is open supplies none. An argument's own formation is its written site's, judged there.
    private ConstraintProof InstanceFormation(BoundType type, BindingScope scope)
        => type.Symbol is not { Declaration: DeclarationContainerKoto, Schema.GenericSlots.Count: > 0 } symbol ? ConstraintProof.Proven
            : type.Components.Count != symbol.Schema.GenericSlots.Count ? ConstraintProof.Unknown
            : this.BeginTypeFormation(type, scope) is { } proof ? this.EndProof(this.EndTypeFormation(type, scope, proof)) : ConstraintProof.Error;

    // SPEC 8.3.2's closed world before ResolveConformance refutes: a conformance to the required reference's Contract declaration is a
    // path not ruled out unless its reference, in the conforming Type's terms, is known to differ from the required one, in the
    // scope's (IdentityRefuted), or each of its paths' own conditions is Refuted (PLAN G866). The walk opens the obligation as
    // ResolveConformance does (openObligations); a request reaching it open reads no evidence and rules out nothing.
    private bool PathNotRuledOut(BoundType type, BindingSymbol symbol, BindingSymbol contract, BindingScope scope)
    {
        if (contract.Type is not { } required || ReferenceEquals(required.Symbol, contract) || this.ConformancesOf(symbol) is not { } bindings)
        {
            return false;
        }

        var key = new Obligation(type, contract, null);
        if (!this.OpenObligation(key))
        {
            return true;
        }

        try
        {
            for (var i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].Contract.Type is { } formal && ReferenceEquals(formal.Symbol, required.Symbol) && this.StoredType(formal, type) is { } instance &&
                    !this.IdentityRefuted(instance, required, scope, type))
                {
                    for (var p = 0; p < this.impls.PathsOf(bindings[i]).Count; p++)
                    {
                        if (this.ProveConformanceConditions(this.impls.PathsOf(bindings[i])[p], type, scope) != ConstraintProof.Refuted)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }
        finally
        {
            this.openObligations.Remove(key);
        }
    }

    // R7-C3 C3-1b (F's C2 rulings 289-290; SPEC 8.3.2, 23.1.2): where evidence is taken from a path (ResolveConformance and the projection
    // specification's PathConditions), an Invalid path is a failed input resting on the path (its Use failed, derived where linked), so
    // no binding or witness of it is read; otherwise the path's conditions. An inherited path's binding is its declared path's
    // (DeclaredPath, SPEC 8.6.2), so that path's Failed state is read the same way (the joint code review's B1, F's C2 ruling 302). A
    // test of whether a path can apply reads the conditions alone (ProveConformanceConditions): an Invalid path is never evidence that
    // the conformance does not apply (8.3.2 closed world).
    private ConstraintProof PathEvidence(BoundConformancePath path, BoundType type, BindingScope scope)
        => (path.Invalid ? path : path.DeclaredPath is { Invalid: true } declared ? declared : null) is { } failed ? this.RestOnFailure(failed)
            : this.ProveConformanceConditions(path, type, scope);

    private ConstraintProof ProveConformanceConditions(BoundConformancePath path, BoundType type, BindingScope scope)
    {
        if (path.InheritedFrom is not null)
        {
            // SPEC 8.8.2: the declared path's conditions at the declared Self at this Type (BoundConformancePath.DeclaredPath); where the
            // generic Self was refused, the instance's is formed one base clause at a time. A Type past the limit is refused here.
            this.inheritedPremiseSteps++;
            if (path.DeclaredSelf is { } layer)
            {
                return this.StoredType(layer, type) is { } declared ? this.ProveConformanceConditions(path.DeclaredPath!, declared, scope) : ConstraintProof.Unknown;
            }

            return this.StoredType(path.InheritedBase!, type) is { } parent ? this.ProveConformanceConditions(path.InheritedFrom, parent, scope) : ConstraintProof.Unknown;
        }

        return this.ProveConditionalPremises(path.Premises, type, scope);
    }

    private ConstraintProof ProveConditionalPremises(SyntaxFormKoto? premises, BoundType type, BindingScope scope)
    {
        if (premises is null)
        {
            return ConstraintProof.Proven;
        }

        // The conditional conformance's own failure, as its owner decides it: a premise error keeps the context (SPEC 8.2.3), so the
        // conditions are judged and the error is the scope's one record, not the target's again: the reading check rests on it.
        if (premises.Parent is { } conditional && this.ReadFailedDeclaration(conditional) is not null)
        {
            return ConstraintProof.Error;
        }

        this.BeginProof();
        var result = ConstraintProof.Proven;
        var derived = false;
        ClauseFact? conditions = null;
        for (var i = 0; i < premises.Operands.Length; i++)
        {
            var condition = (IsKoto)premises.Operands[i];
            var bound = condition.BoundConstraint is { } written ? this.ContractConstraint(this.StoredConstraint(written, type), scope, type) : null;
            var limit = this.ScopeReads.Limit;
            var error = this.ScopeReads.Error;
            var proof = bound is null ? ConstraintProof.Unknown : this.ProveConstraint(bound, scope);
            if (this.DerivedObligation(proof, error))
            {
                derived = true;
                continue;
            }

            if (proof != ConstraintProof.Proven)
            {
                var onLimit = proof == ConstraintProof.Unknown && this.ScopeReads.Limit is { } read && !ReferenceEquals(read, limit);
                conditions = FirstAtVerdict(conditions, new(condition, bound, proof, onLimit));
            }

            result = CombineProof(result, proof, true);
        }

        result = this.DerivedVerdict(result, derived);
        var verdict = this.EndProof(result);
        this.LeaveClause(conditions, result); // The conditions are a clause list (G351a).
        return verdict;
    }
}
