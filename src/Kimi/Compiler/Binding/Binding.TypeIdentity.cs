// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Binding.TypeIdentity: SPEC 8.2.3's Type identity of a scope's view, the one identity judge (R7-C3a).
// - Responsibility: a view's identity classes (Binding.TypeIdentity.Classes): every available identity premise applied, closed under
//   decomposition and congruence (a node keyed by its head, compared attributes and components' class ids); a class's state One,
//   Unsupported, Unproven, Cyclic or Conflict from its constructed members' meets, each compared attribute decided by its domain's own
//   rule (Origins: OriginDeclaration's `==`, then the outlives judgment both ways, SPEC 15.6.5; lengths: the normalized expression;
//   Function Types: quantified Origins paired by position within the pair; pair heads: the admitted set, SPEC 8.3.4, an application
//   admitting exactly `owner` being its target from its addition); cycles: the class graph's strongly connected components, its
//   edges a constructor's components and a projection's Contract arguments (SPEC 8.3.3's expansion), Cyclic only through a
//   constructor edge (R7-C2c2 c2-4b); a Type that is no node is in the class of the node it is congruent with (ClassOf); the
//   premise error an error class is (SPEC 8.2.3's table), recorded once per pass at the declaration whose premises first form it, one
//   record per error group at its last own premise clause and a supplied group at its supplier (R7-C3 C3d);
//   the identity proposition's Proven (IdentityProven; two complete Types by SameCompleteType, Binding.TypeIdentity.Complete, R7-C3
//   C3c); the class key the premise supply compares by (ClassKey, R7-C2c2 c2-1); the
//   one form every Type of a class takes in a view (ClassForm, R7-C2c2 c2-4b: its own contract in Binding.TypeIdentity.Keys, its
//   readers closed to the intakes of the judgments over identical Types); and BU-72's HIR premise (`P is` the ClassForm of `P`).
// - Inputs: the view's chain of valid Constraint environments: positive identity premises and their availability (R7-C2e1's
//   Completion); the Origin declarations' relations and completion; the pairs' admitted sets.
// - Outputs: per view and pass, the classes (ClassesOf) and their Completion (DECISIONS R7-M20's W7); Proven; the premise error; the
//   own premise clauses holding its group records (HoldsGroupRecord), whose declaration's frame is their own check, never their reader
//   (ChecksOwnSite, read by Fail and Consulted alike); for a declaration resting on its records, their site (IdentityErrorSite, the
//   cause queries' one answer for it): a declaration rests on its own premise error only through that site, and only until it has a
//   record of its own (R7-C3 C3d, F's ruling C3d-R1); a
//   Type's key in a table (ClassKey: a parameter's or projection's class's least parameter or projection by a total structural order,
//   Representative, or the Type over its components' keys; c2-4: a constructed member keys to its class's Representative, so
//   every member's premise rows are the class's, joined with the Member's closed world by polarity), which relates only what Proven relates (a class in state One with complete meets, whatever the
//   table's finality) and which only ContractSolver's supply reads: no reader receives a key as a Type; such a class's Member (the
//   same scan: its constructor member as the cycle marking counts it, closed first, R7-C2c2 c2-4, c2-4b), which the atom judges take as their subject (MemberOf) and
//   IdentityRefuted reads; whether two Types are known to differ at a corresponding position (IdentityRefuted: the classes'
//   Refuted, SPEC 8.2.3's Refuted read by the one table their premise meets read, Meet; an abstract Type by proper parts, up to
//   structure; a conformance's instance in the conforming Type's terms, the required side in the scope's), which the Contract
//   judge's paths and the Callable judge's signature read before they refute (PLAN G866).
// - Invariants: the classes are a function of the premise set (union by the smaller index, states from the round that joins nothing);
//   one finality rule per class (final unless a meet rests on an Origin "no" while a declaration it read is not complete or the
//   premise environment cannot be rented); only a Complete table is memoized, by SerialOf, for one publication, and a table that is not is
//   rebuilt per read: the provisional path costs reads times the view's premise nodes (the reads include the premise clauses' own, so
//   it is quadratic in the premises and linear in the uses); one table per view and pass, rebuilt in place; warm 0 B (pooled tables).
// - Prohibitions: no premise is withheld, ranked or chosen by order, and no mode parameter; no judgment inside the closure but the
//   attribute domains', which read no identity; an erroneous class proves nothing and makes its declaration's context no failed one.
public sealed partial class Binding
{
    private readonly Dictionary<BindingScope, (int Serial, IdentityClasses Classes)> identityClasses = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<BindingScope> identityBuilding = new(ReferenceEqualityComparer.Instance);
    // A scope's recorded premise error: whether it is the owner's failure, whether an own class reached the closure limit, and the site
    // of its first record when the owner rests on its records (R7-C3 C3d: no record at the owner and no failure of its own; an own
    // clause or the supplier, the owner's published cause, IdentityErrorSite, and the owner's own checks are its readers), else null;
    // and its own premise clauses holding a group record, a range of identityClauses (HoldsGroupRecord).
    private readonly Dictionary<BindingScope, (bool Own, bool Limited, Koto? Site, (int Start, int Count) Clauses)> identityRecorded = new(ReferenceEqualityComparer.Instance);
    private readonly List<(int Group, byte State)> identityGroups = new(); // RecordIdentityPremiseError's own error groups (R7-C3 C3d)
    private readonly List<Koto> identityClauses = new(); // identityRecorded's clause ranges, reused between passes
    private readonly Stack<IdentityClasses> identityPool = new();
    private readonly List<IdentityClasses> identityLent = new();
    private IdentityClasses? identityExtended; // R7-C2c2 c2-2: ExtendedClassesOf's one request-local table

    // The scaling rows' measures: the nodes the identity classes added in this compilation, and ClassKey's steps and the class members
    // its Representative scanned.
    internal int IdentityClassWork { get; private set; }

    internal long IdentityKeyWork { get; private set; }

    // SPEC 8.2.3: whether a Type-identity premise of any subject is available in `scope` or an enclosing scope (HasTypeIdentity).
    private static bool HasTypeIdentity(BindingScope scope)
    {
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is { HasTypeIdentity: true })
            {
                return true;
            }
        }

        return false;
    }

    // The index pass's reset of a member or element access's identity premise, which each pass publishes again (DECISIONS BU-72).
    [Transitional("R7", Reason = "DECISIONS BU-72: removed when G170's identity query lands (C3), when TyCtxt answers the identical Type under premises.")]
    private static void ClearIdentityPremise(Koto node)
    {
        if (node is MemberAccessKoto or IndexKoto)
        {
            HirTables.PlanFor(node, written: false).Second = null;
        }
    }

    // A publication starts (ContractSolver's ForgetKeptTables): every memoized table is rebuilt in place at its view's next read.
    private void ForgetIdentityClasses()
    {
        foreach (var scope in this.identityClasses.Keys)
        {
            CollectionsMarshal.GetValueRefOrNullRef(this.identityClasses, scope).Serial = -1;
        }
    }

    // The pass's tables go back to the pool (ResetCapabilities); each keeps its capacity.
    private void ResetIdentityClasses()
    {
        foreach (var classes in this.identityLent)
        {
            this.identityPool.Push(classes);
        }

        this.identityLent.Clear();
        this.identityClasses.Clear();
        this.identityRecorded.Clear();
        this.identityClauses.Clear();
    }

    // The view's classes and their Completion; a re-entered build answers nothing (Provisional).
    private IdentityClasses? ClassesOf(BindingScope scope, out Completion completion)
    {
        var serial = SerialOf(scope);
        if (this.identityClasses.TryGetValue(scope, out var kept) && kept.Serial == serial)
        {
            completion = kept.Classes.Completion;
            return kept.Classes;
        }

        completion = Completion.Provisional;
        if (!this.identityBuilding.Add(scope))
        {
            return null;
        }

        try
        {
            // The view's one table of the pass is rebuilt in place when its premises moved or it was not Complete: no reader holds a
            // view's table across a build of the same view (the attribute domains read no identity), and a build nested in another one
            // takes its own table.
            if (kept.Classes is not { } classes || this.identityBuilding.Count != 1)
            {
                classes = this.identityPool.Count != 0 ? this.identityPool.Pop() : new IdentityClasses(this);
                this.identityLent.Add(classes);
            }

            classes.Open(scope);
            this.ApplyIdentityPremises(classes, scope);
            classes.Close();
            this.IdentityClassWork += classes.Work;
            completion = classes.Completion;
            this.identityClasses[scope] = (classes.Final && SerialOf(scope) == serial ? serial : -1, classes); // -1: kept, not memoized
            return classes;
        }
        finally
        {
            this.identityBuilding.Remove(scope);
        }
    }

    // R7-C2c2 c2-2: the declaration's classes with the identity rows a request's walk recorded (SPEC 8.2.3's grounds include the
    // Constraints `T is C` supplies at every depth). Request-local: one reused table, never memoized as the view's; its rows are
    // Provisional (a derived availability), so nothing keyed by it is kept, and at R7-C2e2 e2-1's merge what it keys stays the
    // request's. Its keys follow the table's rule: a constructed member keys to itself (Representative). R7-C2c2 c2-4b (DECISIONS
    // R4.13): its premises are the declaration's grounds alone (ConstraintEnvironment.Declares, asked of an identity fact only),
    // never a use's supply, so the closure a request extends is a function of the declaration's premises and its own rows (SPEC
    // 8.3.3 Limits).
    private IdentityClasses? ExtendedClassesOf(BindingScope scope, List<(BoundType A, BoundType B)> rows)
    {
        if (!this.identityBuilding.Add(scope))
        {
            return null;
        }

        try
        {
            var classes = this.identityExtended ??= new IdentityClasses(this);
            classes.Open(scope);
            for (var current = scope; current is not null; current = current.Parent)
            {
                if (current.Constraints is not { } environment)
                {
                    continue;
                }

                foreach (var fact in environment.Facts)
                {
                    if (fact.Kind == ConstraintKind.TypeIdentity && environment.Declares(fact))
                    {
                        this.ApplyIdentityPremise(classes, environment, fact);
                    }
                }
            }

            for (var i = 0; i < rows.Count; i++)
            {
                classes.Premise(rows[i].A, rows[i].B, Completion.Provisional);
            }

            classes.Close();
            this.IdentityClassWork += classes.Work;
            return classes;
        }
        finally
        {
            this.identityBuilding.Remove(scope);
        }
    }

    private void ApplyIdentityPremises(IdentityClasses classes, BindingScope scope)
    {
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is not { } environment)
            {
                continue;
            }

            foreach (var fact in environment.Facts)
            {
                this.ApplyIdentityPremise(classes, environment, fact);
            }
        }
    }

    // One fact of an environment into a table: an available identity premise joins its sides.
    private void ApplyIdentityPremise(IdentityClasses classes, ConstraintEnvironment environment, BoundConstraint fact)
    {
        if (fact is not { Kind: ConstraintKind.TypeIdentity, Subject: { } subject, RequiredType: { } required })
        {
            return;
        }

        if (this.AvailableConstraintFact(environment, fact, out var availability))
        {
            classes.Premise(subject, required, availability);
        }
        else
        {
            classes.Read(availability); // An unavailable premise that may become available: the classes may grow.
        }
    }

    // SPEC 8.2.3: `a is b` holds in the view when the two are one complete Type, or its classes make them one (Verdict's Proven); an
    // erroneous or provisional class proves nothing.
    private bool IdentityProven(BoundType a, BoundType b, BindingScope scope)
    {
        if (this.SameCompleteType(a, b))
        {
            return true;
        }

        if (!HasTypeIdentity(scope) || this.ClassesOf(scope, out _) is not { } classes)
        {
            return false;
        }

        if (classes.Same(a, b))
        {
            return true;
        }

        this.ReadIdentityPremiseError(scope, a, b);
        return false;
    }

    // SPEC 8.2.3 (Refuted), 8.3.2 ("absence of proof is not enough"; "Unbound Types ... are never negative results"): whether two
    // Types are known to differ at a corresponding position in the view, `a` in the terms of the conforming Type `self` (a
    // conformance's instance: a Contract's Self is `self`, SPEC 8.4) and `b` in the scope's (a required reference: a Contract's Self is
    // the scope's own), completed projections first (ContractType); the view's classes judge it by their one table (Refuted). A judge
    // refutes two Types' agreement only so; a pair with no known difference that it cannot prove is Unknown (the Contract judge's
    // paths, the Callable judge's signature), and so is any pair while the view's table is being built.
    private bool IdentityRefuted(BoundType a, BoundType b, BindingScope scope, BoundType? self)
        => this.ClassesOf(scope, out _) is { } classes && classes.Refuted(this.ContractType(a, scope, self), this.ContractType(b, scope));

    // SPEC 8.2.3 ("an abstract Type ... takes the form its binding, a completed projection or its admitted set fixes"): the members of a
    // Type's identity class in the view, appended to `into`; none without a premise or where the class is erroneous or provisional. The
    // key walk's reader (Binding.Retention.PlaceKeys' View, PLAN G823), which chooses no premise: a class's constructed members are one.
    private void IdentityMembers(BoundType type, BindingScope scope, List<BoundType> into)
    {
        if (HasTypeIdentity(scope) && this.ClassesOf(scope, out _) is { } classes)
        {
            classes.Members(type, into);
        }
    }

    // SPEC 8.2.3's premise errors at the validation step: a scope's own final error classes are recorded once per pass, from the evidence
    // available there, as that step's own proofs are; the environment stays valid (its facts stay evidence, and an erroneous class
    // publishes no member). R7-C3 C3d (PLAN G830, G797): one record per own error group (a cycle's component is one), at the last own
    // premise clause with a Type in it (SPEC 8.2.3's example marks that clause); a group no own clause reaches is the supplying
    // declaration's where its own classes hold the supplied clauses erroneous (a Contract for what its Constraints alone cause, reported
    // there, never again at a use), else the owner's. In the Final pass an own error class still waiting for its Origin declarations is
    // the located limit (an Origin attribute the judge could not decide), and a limit (Unsupported, the closure limit) stays the owner's
    // record, as before.
    private bool RecordIdentityPremiseError(BindingScope scope, BindingMode mode)
    {
        if (scope.Constraints is not { HasTypeIdentity: true } environment || this.identityRecorded.ContainsKey(scope) ||
            this.ClassesOf(scope, out _) is not { Worst: not IdentityClasses.One } own)
        {
            return false;
        }

        var groups = this.identityGroups;
        groups.Clear();
        var (worst, waiting, limited) = own.OwnError(scope.Parent is { } parent ? this.ClassesOf(parent, out _) : null, groups);
        var failure = worst switch
        {
            IdentityClasses.One when waiting && mode == BindingMode.Final => BindingFailure.Unsupported,
            IdentityClasses.One => BindingFailure.None,
            IdentityClasses.Unproven => BindingFailure.UnprovenConstraint,
            IdentityClasses.Unsupported => BindingFailure.Unsupported,
            IdentityClasses.Limit => BindingFailure.PremiseClosureLimit, // R7-C2c2 c2-4: the binding grounds' closure limit (SPEC 8.3.3)
            _ => BindingFailure.InvalidConstraint,
        };

        if (failure == BindingFailure.None)
        {
            return false;
        }

        var owned = scope.Owner.BindingFailure == BindingFailure.None;
        var clauses = this.identityClauses.Count;
        this.identityRecorded.Add(scope, (owned, limited, null, (clauses, 0)));
        if (ImplementationLimit(failure) is not null)
        {
            // A Resource record (SPEC 8.3.3 Limits, 23.1.1) is written as every limit writer writes it (RefuseProof's, TypeHeight's): the
            // owner's failure, whose issue names its limit (ReportIssue); a problem carries no limit fact.
            this.Fail(scope.Owner, failure);
            return true;
        }

        if (failure == BindingFailure.Unsupported)
        {
            this.Record(scope.Owner, failure, []); // a problem of its own where the owner's first failure is another check's (SPEC 23.1.2)
            return true;
        }

        // Each group in an error state is its own problem (SPEC 23.1.2) beside any failure of its site; a group in a limit state beside a
        // worse one stays behind the worst, as before.
        Koto? first = null;
        var atOwner = false;
        foreach (var (group, state) in groups)
        {
            if (state < IdentityClasses.Unproven)
            {
                continue;
            }

            Koto? site = this.PremiseErrorClause(scope, environment, own, group);
            if (site is null && this.PremiseErrorSupplier(scope, own, group) is { } supplier)
            {
                first ??= supplier;
                continue;
            }

            if (site is not null)
            {
                // Published before its record, which Fail may read in an open frame (HoldsGroupRecord).
                this.identityClauses.Add(site);
                this.identityRecorded[scope] = (owned, limited, null, (clauses, this.identityClauses.Count - clauses));
            }

            site ??= scope.Owner;
            atOwner |= ReferenceEquals(site, scope.Owner);
            this.RecordNumbered(site, state == IdentityClasses.Unproven ? BindingFailure.UnprovenConstraint : BindingFailure.InvalidConstraint, []);

            // F's C2/C3 rulings 332, 333 (SPEC 8.3.4, 23.1.2, rule W): a group whose meets read a binder's emptied admitted set (PairHead's
            // 8.3.4 Error) read that failed input, so its record rests on the Error's, which the validation step's proof records after
            // this one at the owner of the environment emptying the set (JudgeFacts): a pending prerequisite, read at publication
            // (IsDerived). A group that reads no emptied set records at its site.
            if (own.EmptiedPair(group) is { } whole && this.EmptyingOwner(scope, whole) is { } emptier && !ReferenceEquals(emptier, site))
            {
                this.AddPendingPrerequisite(site, emptier);
            }

            first ??= site;
        }

        // F's C2/C3 ruling 331 (B): an owner with no failure and no record of its own is Invalid with no failure and no part, and its
        // cause is the first record's site (IdentityErrorSite), which the cause queries read: a use whose proof rests on the declaration
        // stays dependent (SPEC 8.2.3: never again at uses), and the context reader reads no part, so the owner keeps its context.
        var rest = owned && !atOwner ? first : null;
        this.identityRecorded[scope] = (owned, limited, rest, (clauses, this.identityClauses.Count - clauses));
        if (rest is not null)
        {
            this.SetRecordedState(scope.Owner, BindingState.Invalid);
        }

        return true;
    }

    // R7-C3 C3d (F's C2/C3 ruling 331 (B)): the site a declaration's premise error stands at when no record stands at the declaration
    // itself (its first own clause's record or its supplier), while that site is not resolved: the published cause of the declaration's
    // Invalid state, which the cause queries read (HasFailureCauses and the prerequisite walk, PushPrerequisites). The context reader
    // (InvalidDeclarationCause) reads no site, so the declaration keeps its context (KeepsPremiseErrorContext), as on dev. A declaration
    // that records a failure of its own later (another check's) has that record as its cause: its own record's derivation reads its own
    // inputs (RestsOnReported), not the site.
    private Koto? IdentityErrorSite(Koto declaration)
        => this.identityRecorded.Count != 0 && this.RecordedFailure(declaration) == BindingFailure.None && this.scopes.TryGetValue(declaration, out var scope) &&
            this.identityRecorded.TryGetValue(scope, out var recorded) && recorded.Site is { } site && this.IsUnresolvedPrerequisite(site) ? site : null;

    // R7-C3 C3d (F's ruling C3d-R1 (A)): whether `clause` is an own premise clause of `declaration` holding a record of its scope's error
    // group, so the declaration's frame is that record's own check (ChecksOwnSite), as it is where the record stands at the declaration.
    private bool HoldsGroupRecord(Koto declaration, Koto clause)
    {
        if (this.identityRecorded.Count == 0 || !this.scopes.TryGetValue(declaration, out var scope) || !this.identityRecorded.TryGetValue(scope, out var recorded))
        {
            return false;
        }

        var (start, count) = recorded.Clauses;
        return this.identityClauses.IndexOf(clause, start, count) >= 0;
    }

    // R7-C3 C3d (PLAN G830): an own error group's clause, the last of the owner's premise clauses (its own facts) with a Type in the group.
    private IsKoto? PremiseErrorClause(BindingScope scope, ConstraintEnvironment environment, IdentityClasses own, int group)
    {
        IsKoto? site = null;
        var clauses = scope.Owner is FunctionKoto function ? function.TypeConstraints : scope.Owner is DeclarationContainerKoto container ? container.ConstraintNodes : [];
        for (var i = 0; i < clauses.Count; i++)
        {
            if (clauses[i] is IsKoto { BoundConstraint: { } premise } clause && Reaches(premise))
            {
                site = clause;
            }
        }

        return site;

        // A clause's own fact (an And's parts) with its subject, required Type or bound Contract reference reaching the group.
        bool Reaches(BoundConstraint fact)
            => fact.Kind == ConstraintKind.And ? Reaches(fact.Left!) || Reaches(fact.Right!)
            : environment.DirectFacts.Contains(fact) && (own.InGroup(fact.Subject, group) || own.InGroup(fact.RequiredType, group) || own.InGroup(fact.Contract?.Type, group));
    }

    // R7-C3 C3d (PLAN G797; SPEC 8.2.3: "a Contract for what its Constraints alone cause ... never again at uses"): the declaration whose
    // record an own error group no own clause reaches is, when every available identity fact of the view with a Type in the group is a
    // supply's instance of a clause that a declaration outside the view's chain (the supplier, or the clause's own declaration) holds
    // erroneous in its own classes; else none, and the record is the owner's: a direct fact, or a supply its source holds valid (an
    // error its arguments here form), keeps it here.
    private Koto? PremiseErrorSupplier(BindingScope scope, IdentityClasses own, int group)
    {
        Koto? supplier = null;
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is not { } environment)
            {
                continue;
            }

            foreach (var fact in environment.Facts)
            {
                if (fact is not { Kind: ConstraintKind.TypeIdentity, Subject: { } subject, RequiredType: { } required } ||
                    !(own.InGroup(subject, group) || own.InGroup(required, group)))
                {
                    continue;
                }

                if (environment.DirectFacts.Contains(fact) || this.HeldAtSource(scope, environment, fact) is not { } held)
                {
                    return null;
                }

                supplier ??= held;
            }
        }

        return supplier;
    }

    // The declaration that holds each supply of `fact` erroneous (the first), or none when one supply's does not.
    private Koto? HeldAtSource(BindingScope scope, ConstraintEnvironment environment, BoundConstraint fact)
    {
        Koto? held = null;
        foreach (var (derived, source, origin) in environment.DerivedFacts)
        {
            if (!ReferenceEquals(derived, fact))
            {
                continue;
            }

            if (origin is not { BoundConstraint: { } clause, Parent: { } declaration } ||
                (this.HoldsClause(scope, source.Declaration, clause) ? source.Declaration : this.HoldsClause(scope, declaration, clause) ? declaration : null) is not { } holder)
            {
                return null;
            }

            held ??= holder;
        }

        return held;
    }

    // Whether a declaration outside `scope`'s chain holds a clause erroneous in its own classes.
    private bool HoldsClause(BindingScope scope, Koto declaration, BoundConstraint clause)
    {
        if (!this.scopes.TryGetValue(declaration, out var source))
        {
            return false;
        }

        for (var current = scope; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, source))
            {
                return false;
            }
        }

        return this.ClassesOf(source, out _) is { } classes && (classes.Holds(clause.Subject) || classes.Holds(clause.RequiredType));
    }

    // SPEC 8.2.3, 23.1.2: a declaration whose own failure is its premise error keeps its context, as a shape violation keeps its signature:
    // the error is reported once, at it, and its other checks are judged as in a valid view.
    // R7-C3 (B1'): any failed premise is this context's case: an identity premise error recorded at the declaration (identityRecorded),
    // or a premise whose proof failed and left the evidence (FailPremise), both its own record; and SPEC 13.3.4's direction contradiction
    // as its owner's own record (a premise error by 8.2.3's site, x41n's Reading 1), whose pair of clause instances fails no premise (F's C2
    // ruling 324).
    private bool KeepsPremiseErrorContext(Koto declaration)
        => this.scopes.TryGetValue(declaration, out var scope) && scope.Constraints is { } environment &&
            ((this.identityRecorded.TryGetValue(scope, out var recorded) && recorded.Own) ||
                ReferenceEquals(environment.FailedPremise?.Use, declaration) || this.RecordedFailure(declaration) == BindingFailure.ArithmeticDirection);

    // SPEC 8.3.3 Limits: whether a declaration's premise closure (its own premises or an enclosing declaration's) reached the limit,
    // whatever its first failure: a class past it is cut, so a judgment through it depends on the limited analysis. Binding.CaseLimits'
    // PartsVerified reads it until a cut class gives Binding's typing no member (the PLAN open issue on cut classes), which removes the read.
    private bool PremiseClosureLimited(Koto declaration)
    {
        for (var scope = this.scopes.GetValueOrDefault(declaration); scope is not null; scope = scope.Parent)
        {
            if (this.identityRecorded.TryGetValue(scope, out var recorded) && recorded.Limited)
            {
                return true;
            }
        }

        return false;
    }

    // R7-C3 (F's C2 ruling 276 (4); SPEC 8.2.3, 23.1.2): the class table's erroneous state is the identity premise error's one
    // representation (dev's producer channel, ruling 245 (ii)). A check that reads Types with a part in a final erroneous class of its
    // view reads that failed input, as the one consult (AvailableConstraintFact) reads a failed premise: the record of the outermost
    // scope whose own classes hold it (the producer) is its cause. A proof's read is its limit (an answer the valid facts leave open
    // rests on it, EndProof); a mismatch's record rests on it (RecordTypeMismatch). Nothing is read without a recorded identity error.
    private PremiseFailure? IdentityPremiseErrorRead(BindingScope scope, BoundType? a, BoundType? b)
    {
        BindingScope? producer = null;
        for (var current = scope; current is not null && this.identityRecorded.Count != 0; current = current.Parent)
        {
            // The producer's own checks (its frame) are the error's sites where it records at the owner, never its readers
            // (ChecksOwnSite); with its records at its clauses or its supplier (R7-C3 C3d) they read it as any check does.
            if (this.identityRecorded.TryGetValue(current, out var recorded) && recorded.Own && (recorded.Site is not null || !this.ChecksOwnSite(current.Owner)) &&
                this.ClassesOf(current, out _) is { } classes && (classes.Holds(a) || classes.Holds(b)))
            {
                producer = current;
            }
        }

        return producer?.Constraints!.Cause(producer.Owner);
    }

    // A judgment's read of an erroneous class (IdentityPremiseErrorRead) is its proof's limit.
    private void ReadIdentityPremiseError(BindingScope scope, BoundType a, BoundType? b = null)
    {
        if (this.identityRecorded.Count != 0 && this.IdentityPremiseErrorRead(scope, a, b) is { } failure)
        {
            this.ScopeReads.Limit ??= failure;
        }
    }

    // SPEC 8.2.3's Origin attribute, decided by the Origins' owner: OriginDeclaration's `==` (one Origin substituted by the other) and the
    // outlives judgment both ways (EntailsOrigin, SPEC 15.6.5's reachability over the declaration's premises). A "no" is final once the
    // declarations it read are complete and the premise environment can be rented; a "yes" only grows with the premises.
    private bool OriginsAgree(BoundOrigin? a, BoundOrigin? b, Koto owner, ref bool provisional)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        var incomplete = false;
        for (Koto? node = owner; node is not null; node = node.Parent)
        {
            if (!this.originDeclarations.TryGetValue(node, out var declaration))
            {
                continue;
            }

            incomplete |= !declaration.Completed;
            if (declaration.Resolves)
            {
                (a, b) = (declaration.Substitution(a) ?? a, declaration.Substitution(b) ?? b);
            }
        }

        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (!this.OriginPremisesAvailable)
        {
            provisional = true; // The premise environment would depend on itself (PLAN G74): no answer yet.
            return false;
        }

        var agree = this.EntailsOrigin(a, b, owner, default, 0) && this.EntailsOrigin(b, a, owner, default, 0);
        provisional |= !agree && incomplete;
        return agree;
    }

    // DECISIONS BU-72: a member access or an element access whose receiver's Type, through its Semantics layers, is a Type parameter P
    // that the premises make the complete Type U was resolved on U; it publishes the interned `P is U` (HirTables.IdentityPremise), since
    // a MIR Place keeps P.
    [Transitional("R7", Reason = "DECISIONS BU-72: removed when G170's identity query lands (C3), when TyCtxt answers the identical Type under premises.")]
    private void PublishIdentityPremise(Koto receiver, BoundType actual, BoundType substituted)
    {
        var access = receiver.Parent;
        while (access is ParenthesizedKoto)
        {
            access = access.Parent;
        }

        if (access is not (MemberAccessKoto or IndexKoto) || !ReferenceEquals(KotoHelper.UnwrapParentheses(((BinaryKoto)access).Left), receiver))
        {
            return;
        }

        while (actual is { Kind: BoundTypeKind.Semantics, Components.Count: 1 } && substituted is { Kind: BoundTypeKind.Semantics, Components.Count: 1 })
        {
            (actual, substituted) = (actual.Components[0], substituted.Components[0]);
        }

        if (actual.Kind == BoundTypeKind.Parameter && substituted.Kind != BoundTypeKind.Parameter)
        {
            HirTables.PlanFor(access, written: true).Second = this.InternConstraint(new(ConstraintKind.TypeIdentity, actual, substituted));
        }
    }
}
