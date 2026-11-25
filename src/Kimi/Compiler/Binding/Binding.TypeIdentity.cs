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
//   record per error group at its last own premise clause and a supplied group at its supplier (R7-C3 C3d), by one recorder for the
//   validation step's table and a closure's round table alike (RecordErrorGroups, e2-7 C1);
//   the identity proposition's Proven (IdentityProven; two complete Types by SameCompleteType, Binding.TypeIdentity.Complete, R7-C3
//   C3c); the class key the premise supply compares by (ClassKey, R7-C2c2 c2-1); the
//   one form every Type of a class takes in a view (ClassForm, R7-C2c2 c2-4b: its own contract in Binding.TypeIdentity.Keys, its
//   readers closed to the intakes of the judgments over identical Types); and BU-72's HIR premise (`P is` the ClassForm of `P`).
// - Inputs: the view's chain of valid Constraint environments: positive identity premises and their availability (R7-C2e1's
//   Completion); the Origin declarations' relations and completion; the pairs' admitted sets.
// - Outputs: per view and pass, the classes (ClassesOf) and their Completion (DECISIONS R7-M20's W7); Proven; the premise error; the
//   own premise clauses holding its group records (HoldsGroupRecord), whose declaration's frame is their own check: its failures and
//   reads of them are no prerequisites (ChecksOwnSite, read by Fail, Consulted and VerifyConformance's conformance use alike), though
//   its proofs read the error as their limit while its records stand at its clauses or its supplier (IdentityPremiseErrorRead); for a
//   declaration resting on its records, their site (IdentityErrorSite, the cause queries' one answer for it): a declaration rests on
//   its own premise error only through that site, and only until it has a record of its own (R7-C3 C3d, F's rulings C3d-R1, C3d-R2); a
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
    private readonly Dictionary<BindingScope, (int Serial, int Complete, IdentityClasses Classes)> identityClasses = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<BindingScope> identityBuilding = new(ReferenceEqualityComparer.Instance);
    // A scope's recorded premise error, written before its first record: whether it is the owner's failure (the owner had none before
    // it), whether an own class reached the closure limit, the site of its first record when the owner rests on its records (R7-C3
    // C3d: no record at the owner and no failure of its own; an own clause or the supplier, the owner's published cause,
    // IdentityErrorSite, and the owner's own checks are its readers), else null; and whether the validation step judged it in the pass
    // (Judged, RecordIdentityPremiseError's gate: its failure kinds are decided once per pass, e2-7 C1). Its group records' sites are
    // identityGroupSites' rows (RecordsAt).
    private readonly Dictionary<BindingScope, (bool Own, bool Limited, Koto? Site, bool Judged)> identityRecorded = new(ReferenceEqualityComparer.Instance);
    private readonly List<(int Group, byte State)> identityGroups = new(); // OwnError's own error groups (R7-C3 C3d), a stack of ranges
    private readonly List<(BindingScope Scope, Koto Site)> identityGroupSites = new(); // each group record's scope and site, reused between passes
    private readonly Stack<IdentityClasses> identityPool = new();
    private readonly List<IdentityClasses> identityLent = new();
    private readonly List<IdentityRow> identityRows = new(); // the views' rows while their tables are built (AddViewRows), a stack of ranges

    // e2-7 (SPEC 8.2.3, 8.3.3 Limits; G1406-11 C2, C-d): the publication's closed roots (ContractSolver's CloseRoot), each filed by
    // FileClosure under its view scope and final key, with its entry key and each part asked as (ClosureOf) mapped to it: its rows (a
    // range of identityClosed: a Complete root's E(r), a Limited root's last round L(r)) and whether it is Limited; and per view scope
    // the count of its Complete roots, which keys the view's table. T_A's kept tables (LimitedClassesOf), each under its view scope,
    // serial and closure set (a range of identityLimitedRoots). All are forgotten at each publication and each pass, capacity kept.
    private readonly Dictionary<(BindingScope Scope, BoundType Key), int> identityRoots = new();
    private readonly List<IdentityClosure> identityClosures = new();
    private readonly List<IdentityRow> identityClosed = new();
    private readonly Dictionary<BindingScope, int> identityComplete = new(ReferenceEqualityComparer.Instance);
    private readonly List<(BindingScope Scope, int Serial, int Start, int Count, IdentityClasses Classes)> identityLimited = new();
    private readonly List<int> identityLimitedRoots = new();
    private readonly List<int> identityAsked = new(); // ClassesOf's A(X) closures while it reads, a stack of ranges
    private readonly List<BoundType> identityParts = new(); // CloseAsked's parts of X, a stack of ranges
    private readonly List<BoundType?> identitySides = new(); // OwnClasses' X, the own premises' sides, a stack of ranges
    private IdentityClasses? identityExtended; // R7-C2c2 c2-2: ExtendedClassesOf's one request-local table

    // The scaling rows' measures: the nodes the identity classes added in this compilation, and ClassKey's steps and the class members
    // its Representative scanned.
    internal int IdentityClassWork { get; private set; }

    internal long IdentityKeyWork { get; private set; }

    // A row of an identity table (ClassesOver; SPEC 8.2.3's grounds): the identity premise `Fact` and its Source, which its availability
    // is read by. A fact of a view's chain names its environment (Environment: read there, as every premise is, AvailableConstraintFact);
    // a clause instance a request's walk recorded names the Contract supplying it and the clause it instantiates, as a DerivedFacts entry
    // names its source (Source, Origin; Environment null) and is read with its Availability: Provisional while its walk runs (a derived
    // availability, R7-M27), its closure's Completion once filed (FileClosure; G1406-11 C-d: a Complete closure's rows keep the view's
    // table Final).
    private readonly record struct IdentityRow(BoundConstraint Fact, ConstraintEnvironment? Environment, BindingSymbol? Source, IsKoto? Origin, Completion Availability);

    // A closed root (FileClosure): its view scope, final key, rows (identityClosed's range) and whether it is Limited (G1406-11 C2).
    private readonly record struct IdentityClosure(BindingScope Scope, BoundType Key, int Start, int Count, bool Limited);

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

        this.ForgetClosures();
    }

    // The publication's closed roots and T_A tables are forgotten (G1406-11 C2: a closure is filed for one publication), capacity kept.
    private void ForgetClosures()
    {
        this.identityRoots.Clear();
        this.identityClosures.Clear();
        this.identityClosed.Clear();
        this.identityComplete.Clear();
        this.identityLimited.Clear();
        this.identityLimitedRoots.Clear();
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
        this.ForgetClosures();
        this.identityRecorded.Clear();
        this.identityGroupSites.Clear();
    }

    // e2-7 (SPEC 8.2.3, 8.3.3 Limits; G1406-11 C-a, C-b, C-d, N2): the table a reading of the Types `asked` (its X) reads in the view
    // `scope`, and its Completion. At the request boundary (no request open, no walk, no table build) it first closes A(X), X's
    // dependent abstract parts, each keyed by its class (CloseAsked, ContractSolver's CloseRoot). It answers the view's table
    // (ViewClassesOf) when every root of A(X) is Complete, else T_A (LimitedClassesOf), whose Limited bit is the reading's own limit,
    // read by its judge alone (ReadIdentityLimit). Inside a request, a walk or a build it closes nothing and answers the view's table
    // as it stands (a nested read reads the table its outermost reader walks). A re-entered build answers nothing (Provisional).
    private IdentityClasses? ClassesOf(BindingScope scope, ReadOnlySpan<BoundType?> asked, out Completion completion)
    {
        var start = this.identityAsked.Count;
        try
        {
            return this.enumerateDepth == 0 && this.enumerateWalking == 0 && this.identityBuilding.Count == 0 && this.CloseAsked(scope, asked, start)
                ? this.LimitedClassesOf(scope, start, out completion)
                : this.ViewClassesOf(scope, out completion);
        }
        finally
        {
            this.identityAsked.RemoveRange(start, this.identityAsked.Count - start);
        }
    }

    // A(X)'s closures (G1406-11 C2), appended to identityAsked from `start`, each once: each dependent abstract part of the asked Types
    // (AddAbstractParts), closed or read from the publication's filing (ClosureOf). Returns whether one is Limited.
    private bool CloseAsked(BindingScope scope, ReadOnlySpan<BoundType?> asked, int start)
    {
        var parts = this.identityParts.Count;
        foreach (var type in asked)
        {
            this.AddAbstractParts(type, parts);
        }

        var limited = false;
        for (var p = parts; p < this.identityParts.Count; p++)
        {
            if (!this.identityParts[p].IsAbstractType)
            {
                continue; // a composite the walk met (AddAbstractParts), no part of A(X)
            }

            var closure = this.ClosureOf(scope, this.identityParts[p]);
            if (this.identityAsked.IndexOf(closure, start, this.identityAsked.Count - start) < 0)
            {
                this.identityAsked.Add(closure);
                limited |= this.identityClosures[closure].Limited;
            }
        }

        this.identityParts.RemoveRange(parts, this.identityParts.Count - parts);
        return limited;
    }

    // The Types of `type` with an abstract part (each once, appended to identityParts from `start`), among them its dependent abstract
    // parts (a parameter or projection at any part, BoundType.IsAbstractType). It keeps no query state (a linear test over the Types
    // met, a few), so a reading inside another walk's part query (visitedParts) leaves that query intact.
    private void AddAbstractParts(BoundType? type, int start)
    {
        if (type is not { HasAbstractPart: true } || this.identityParts.IndexOf(type, start, this.identityParts.Count - start) >= 0)
        {
            return;
        }

        this.identityParts.Add(type); // every Type met once, so a shared component is walked once; CloseAsked closes the abstract ones
        for (var i = 0; i < type.Components.Count; i++)
        {
            this.AddAbstractParts(type.Components[i], start);
        }
    }

    // The closure of `part` in `scope`'s view for the publication: filed under the part as asked, else closed (CloseRoot), which files it
    // under its final key and its entry key. Only CloseAsked calls it, at the request boundary.
    private int ClosureOf(BindingScope scope, BoundType part)
    {
        if (!this.identityRoots.TryGetValue((scope, part), out var closure))
        {
            closure = this.CloseRoot(scope, part);
            this.identityRoots[(scope, part)] = closure;
        }

        return closure;
    }

    // Files a closed root (CloseRoot's one writer of the filing; G1406-11 C2): its rows (a range of identityClosed, each read with the
    // closure's Completion, Complete when its walks read Complete), under its final key, and its entry key mapped to it; a Complete
    // root counts in its view's table (identityComplete, ViewClassesOf's key). Returns the closure.
    private int FileClosure(BindingScope scope, BoundType entry, BoundType key, List<IdentityRow> rows, bool limited, Completion completion)
    {
        var start = this.identityClosed.Count;
        foreach (var row in rows)
        {
            this.identityClosed.Add(row with { Availability = completion });
        }

        var closure = this.identityClosures.Count;
        this.identityClosures.Add(new(scope, key, start, rows.Count, limited));
        this.identityRoots[(scope, key)] = closure;
        this.identityRoots[(scope, entry)] = closure;
        if (!limited)
        {
            CollectionsMarshal.GetValueRefOrAddDefault(this.identityComplete, scope, out _)++;
        }

        return closure;
    }

    // The view's table (G1406-11 C-d): ClassesOver its rows (AddViewRows: every identity premise of its chain) and every Complete root's
    // rows closed in it this publication. Memoized by (SerialOf, the Complete roots' count) when Final: a reader that closes a new
    // Complete root rebuilds it, by the same call, which re-joins every kept root's rows; a rebuild takes a table from the pool when a
    // reader may hold the kept one (a build nested in another, or a closure since), so no held table changes under its reader.
    private IdentityClasses? ViewClassesOf(BindingScope scope, out Completion completion)
    {
        var serial = SerialOf(scope);
        var complete = this.identityComplete.GetValueOrDefault(scope);
        if (this.identityClasses.TryGetValue(scope, out var kept) && kept.Serial == serial && kept.Complete == complete)
        {
            completion = kept.Classes.Completion;
            return kept.Classes;
        }

        completion = Completion.Provisional;
        if (!this.identityBuilding.Add(scope))
        {
            return null;
        }

        var start = this.identityRows.Count;
        try
        {
            if (kept.Classes is not { } classes || this.identityBuilding.Count != 1 || kept.Complete != complete)
            {
                classes = this.identityPool.Count != 0 ? this.identityPool.Pop() : new IdentityClasses(this);
                this.identityLent.Add(classes);
            }

            this.AddViewRows(scope);
            foreach (var closure in this.identityClosures)
            {
                if (ReferenceEquals(closure.Scope, scope) && !closure.Limited)
                {
                    this.identityRows.AddRange(CollectionsMarshal.AsSpan(this.identityClosed).Slice(closure.Start, closure.Count));
                }
            }

            this.ClassesOver(classes, scope, this.identityRows, start);
            completion = classes.Completion;
            this.identityClasses[scope] = (classes.Final && SerialOf(scope) == serial ? serial : -1, complete, classes); // -1: kept, not memoized
            return classes;
        }
        finally
        {
            this.identityRows.RemoveRange(start, this.identityRows.Count - start);
            this.identityBuilding.Remove(scope);
        }
    }

    // T_A (G1406-11 C-a, C-b, N2; R2.1): a reading's table at a Limited root, ClassesOver the view's rows (its grounds and use supply) and
    // the rows of each closure it asked (identityAsked from `start`: a Limited root's last round, an asked Complete root's rows), never
    // another root's, so it is a function of (G, A, U). Marked Limited. Kept for the publication under (SerialOf, the set of its
    // closures) only when Final and SerialOf is unchanged across its build, as the view's table is.
    private IdentityClasses? LimitedClassesOf(BindingScope scope, int start, out Completion completion)
    {
        var serial = SerialOf(scope);
        var count = this.identityAsked.Count - start;
        foreach (var (keptScope, keptSerial, keysStart, keysCount, kept) in this.identityLimited)
        {
            if (ReferenceEquals(keptScope, scope) && keptSerial == serial && keysCount == count && this.SameClosures(keysStart, start, count))
            {
                completion = kept.Completion;
                return kept;
            }
        }

        completion = Completion.Provisional;
        if (!this.identityBuilding.Add(scope))
        {
            return null;
        }

        var rows = this.identityRows.Count;
        try
        {
            var classes = this.identityPool.Count != 0 ? this.identityPool.Pop() : new IdentityClasses(this);
            this.identityLent.Add(classes);
            this.AddViewRows(scope);
            for (var a = start; a < this.identityAsked.Count; a++)
            {
                var closure = this.identityClosures[this.identityAsked[a]];
                this.identityRows.AddRange(CollectionsMarshal.AsSpan(this.identityClosed).Slice(closure.Start, closure.Count));
            }

            this.ClassesOver(classes, scope, this.identityRows, rows);
            classes.MarkLimited();
            completion = classes.Completion;
            if (classes.Final && SerialOf(scope) == serial)
            {
                var keys = this.identityLimitedRoots.Count;
                this.identityLimitedRoots.AddRange(CollectionsMarshal.AsSpan(this.identityAsked).Slice(start, count));
                this.identityLimited.Add((scope, serial, keys, count, classes));
            }

            return classes;
        }
        finally
        {
            this.identityRows.RemoveRange(rows, this.identityRows.Count - rows);
            this.identityBuilding.Remove(scope);
        }
    }

    // Whether a kept T_A's closure set (identityLimitedRoots from `kept`) is the asked one (identityAsked from `asked`), both `count` long.
    private bool SameClosures(int kept, int asked, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (this.identityAsked.IndexOf(this.identityLimitedRoots[kept + i], asked, count) < 0)
            {
                return false;
            }
        }

        return true;
    }

    // G1406-11 C-a (SPEC 8.3.3 Limits): a judgment that its reading of a Limited root's table (T_A, ClassesOf) leaves unproven is refused
    // (RefuseProof: AnalysisLimit, at the use whose check asked, else at the scope's owner), read from that reading alone, never from
    // scope state; a judgment over a table that is not Limited reads nothing. Returns the refusal's site (RefuseProof's), which a check
    // over the same reading rests on (rule W, RecordTypeMismatch), or null when nothing is refused.
    private Koto? ReadIdentityLimit(BindingScope scope, IdentityClasses? classes)
        => classes is { Limited: true } ? this.RefuseProof(scope, BindingFailure.AnalysisLimit) : null;

    // R7-C2c2 c2-2: the declaration's classes with the identity rows a request's walk recorded (SPEC 8.2.3's grounds include the
    // Constraints `T is C` supplies at every depth): ClassesOver those rows. Request-local: one reused table, never memoized as the
    // view's; its rows are Provisional (a derived availability), so nothing keyed by it is kept, and at R7-C2e2 e2-1's merge what it
    // keys stays the request's. Its keys follow the table's rule: a constructed member keys to itself (Representative). R7-C2c2 c2-4b
    // (DECISIONS R4.13): its premises are the declaration's grounds alone (ClassesOver's, ConstraintEnvironment.Declares), never a
    // use's supply, so the closure a request extends is a function of the declaration's premises and its own rows (SPEC 8.3.3 Limits).
    private IdentityClasses? ExtendedClassesOf(BindingScope scope, List<IdentityRow> rows)
    {
        if (!this.identityBuilding.Add(scope))
        {
            return null;
        }

        try
        {
            return this.ClassesOver(this.identityExtended ??= new IdentityClasses(this), scope, rows, 0);
        }
        finally
        {
            this.identityBuilding.Remove(scope);
        }
    }

    // The one identity table builder (SPEC 8.2.3; e2-7's T(G ∪ rows)): `classes` opened for `scope`, its premises the declaration's
    // grounds (each declared identity fact of the chain, ConstraintEnvironment.DeclaredIdentityFacts; no other fact is walked: C-e) and
    // `rows` from `start`, each read by its Source (IdentityRow), then closed; its nodes are counted in IdentityClassWork. Input
    // invariant: rows that are facts of the chain come first and are all of them, in the chain's order (innermost scope first, each
    // environment in its order), as AddViewRows gives them; then the grounds are among those rows, and are not walked again. So a ground
    // is applied once, and a table is built in its premises' order, which Members' index order and the premise error's groups read.
    // The caller holds the re-entry guard (identityBuilding) and owns the table; no mode: what a table holds beyond the grounds is its
    // rows.
    private IdentityClasses ClassesOver(IdentityClasses classes, BindingScope scope, List<IdentityRow> rows, int start)
    {
        classes.Open(scope);
        var grounds = start == rows.Count || rows[start].Environment is null ? scope : null; // the chain's facts as rows hold the grounds
        for (var current = grounds; current is not null; current = current.Parent)
        {
            if (current.Constraints is not { } environment)
            {
                continue;
            }

            foreach (var fact in environment.DeclaredIdentityFacts)
            {
                this.ApplyIdentityPremise(classes, environment, fact);
            }
        }

        // A nested build reads its own range past these rows and removes it before it returns, so the index stays this table's.
        for (var r = start; r < rows.Count; r++)
        {
            if (rows[r].Environment is { } environment)
            {
                this.ApplyIdentityPremise(classes, environment, rows[r].Fact);
            }
            else
            {
                classes.Premise(rows[r].Fact.Subject!, rows[r].Fact.RequiredType!, rows[r].Availability);
            }
        }

        classes.Close();
        this.IdentityClassWork += classes.Work;
        return classes;
    }

    // A view's rows (ClassesOf): each TypeIdentity fact of its chain, in the chain's order (innermost scope first, each environment in
    // its order), read in its environment: the declaration's grounds and a use's supply alike, so the view's table holds every available
    // identity premise of its chain in that order (ApplyIdentityPremise). Appended to identityRows; the caller removes its range. A row
    // with an Environment gives no Availability of its own (e2-7 fact (e)): its availability is the fact's, read where it is applied.
    private void AddViewRows(BindingScope scope)
    {
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Constraints is not { } environment)
            {
                continue;
            }

            foreach (var fact in environment.IdentityFacts)
            {
                this.identityRows.Add(new(fact, environment, null, null, default));
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
            // An unavailable premise is read Provisional (W6): a derived one a Pending source may still supply (the classes may grow),
            // or one whose read consulted a failure (a failed premise, its derived failure, a failed source), which a kept table would
            // not replay. So the table is not Final (W7): not memoized, and the view's requests keep no entry or rows (RowsRequest).
            classes.Read(availability);
        }
    }

    // SPEC 8.2.3: `a is b` holds in the view when the two are one complete Type, or the classes of its reading (ClassesOf over a and b)
    // make them one (Verdict's Proven); an erroneous or provisional class proves nothing. `read` is that reading's table: a judge that
    // leaves the judgment unproven reads its Limited bit (ReadIdentityLimit; G1406-11 C-a). It never refuses, so a branch that reads it
    // records nothing of its own (C3).
    private bool IdentityProven(BoundType a, BoundType b, BindingScope scope, out IdentityClasses? read)
    {
        read = null;
        if (this.SameCompleteType(a, b))
        {
            return true;
        }

        if (!HasTypeIdentity(scope) || (read = this.ClassesOf(scope, [a, b], out _)) is not { } classes)
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
    // paths, the Callable judge's signature), and so is any pair while the view's table is being built, and any pair at a Limited root
    // (SPEC 8.3.3: no Refuted rests on the limited analysis; G1406-11 N1): its judge left Unknown reads the limit of `read`, the reading's
    // table (ReadIdentityLimit).
    private bool IdentityRefuted(BoundType a, BoundType b, BindingScope scope, BoundType? self, out IdentityClasses? read)
    {
        var (left, right) = (this.ContractType(a, scope, self), this.ContractType(b, scope));
        return (read = this.ClassesOf(scope, [left, right], out _)) is { Limited: false } classes && classes.Refuted(left, right);
    }

    // SPEC 8.2.3 ("an abstract Type ... takes the form its binding, a completed projection or its admitted set fixes"): the members of a
    // Type's identity class in the view, appended to `into`; none without a premise or where the class is erroneous or provisional. The
    // key walk's reader (Binding.Retention.PlaceKeys' View, PLAN G823), which chooses no premise: a class's constructed members are one.
    private void IdentityMembers(BoundType type, BindingScope scope, List<BoundType> into)
    {
        if (HasTypeIdentity(scope) && this.ClassesOf(scope, [type], out _) is { } classes)
        {
            classes.Members(type, into);
        }
    }

    // e2-7 (G1406-11's C1; SPEC 8.2.3): the table the validation step reads, ClassesOf over the sides of the scope's own written identity
    // premises (Declares) as its X, so their roots close at the step and a Limited root's table (T_A) is the one the recorder reads
    // (RecordIdentityPremiseError). The sides are a range of identitySides, removed before it returns.
    private IdentityClasses? OwnClasses(BindingScope scope, ConstraintEnvironment environment)
    {
        var start = this.identitySides.Count;
        foreach (var fact in environment.DeclaredIdentityFacts)
        {
            this.identitySides.Add(fact.Subject);
            this.identitySides.Add(fact.RequiredType);
        }

        try
        {
            return this.ClassesOf(scope, CollectionsMarshal.AsSpan(this.identitySides)[start..], out _);
        }
        finally
        {
            this.identitySides.RemoveRange(start, this.identitySides.Count - start);
        }
    }

    // SPEC 8.2.3's premise errors at the validation step: a scope's own final error classes are recorded once per pass, from the evidence
    // available there, as that step's own proofs are; the environment stays valid (its facts stay evidence, and an erroneous class
    // publishes no member). R7-C3 C3d (PLAN G830, G797): one record per own error group (a cycle's component is one), at the last own
    // premise clause with a Type in it (SPEC 8.2.3's example marks that clause); a group no own clause reaches is the supplying
    // declaration's where its own classes hold the supplied clauses erroneous (a Contract for what its Constraints alone cause, reported
    // there, never again at a use), else the owner's. In the Final pass an own error class still waiting for its Origin declarations is
    // the located limit (an Origin attribute the judge could not decide), and a limit (Unsupported, the closure limit) stays the owner's
    // record, as before. e2-7 C1 (G1406-12 (a)): the step decides the failure kinds once per pass (Judged); its error groups go to the
    // one recorder (RecordErrorGroups), which a closure's table (RecordClosureErrors) runs again for a group the scope has no record of.
    private bool RecordIdentityPremiseError(BindingScope scope, BindingMode mode)
    {
        if (scope.Constraints is not { HasTypeIdentity: true } environment || this.identityRecorded.GetValueOrDefault(scope).Judged ||
            this.OwnClasses(scope, environment) is not { Worst: not IdentityClasses.One } own)
        {
            return false;
        }

        var start = this.identityGroups.Count;
        try
        {
            var (worst, waiting, limited) = own.OwnError(scope.Parent is { } parent ? this.ClassesOf(parent, [], out _) : null, this.identityGroups);
            var failure = worst switch
            {
                IdentityClasses.One when waiting && mode == BindingMode.Final => BindingFailure.Unsupported,
                IdentityClasses.One => BindingFailure.None,
                IdentityClasses.Unproven => BindingFailure.UnprovenConstraint,
                IdentityClasses.Unsupported => BindingFailure.Unsupported,
                IdentityClasses.Limit => BindingFailure.AnalysisLimit, // R7-C2c2 c2-4: the binding grounds' closure limit (SPEC 8.3.3)
                _ => BindingFailure.InvalidConstraint,
            };

            if (failure == BindingFailure.None)
            {
                return false;
            }

            // A closure's records (RecordClosureErrors) may have written the entry first: its Own (the owner had no failure before them)
            // and Site stand.
            var recorded = this.identityRecorded.TryGetValue(scope, out var closed) ? closed : (scope.Owner.BindingFailure == BindingFailure.None, false, null, false);
            this.identityRecorded[scope] = (recorded.Own, recorded.Limited || limited, recorded.Site, true);
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

            this.RecordErrorGroups(scope, environment, own, start);
            return true;
        }
        finally
        {
            this.identityGroups.RemoveRange(start, this.identityGroups.Count - start);
        }
    }

    // e2-7 C1 (G1406-12 (a); SPEC 8.2.3, N3: the recorder stays per T_r): a closure's round table T_r (CloseRoot's last round, read
    // inside its request, so no closure nests) can form an own error group that the scope's validation step never reads (its table's X
    // is the own premises' sides, OwnClasses). Its own error groups (OwnError over the parent's table, as the step's) go to the one
    // recorder (RecordErrorGroups), which records only a group whose site holds no record of the scope's yet. It decides no failure
    // kind (the step's, Judged) and records nothing after the pass or for a table with no error class. A Limited reading's T_A is no
    // round table and records nothing (N3).
    private void RecordClosureErrors(BindingScope scope, IdentityClasses? table)
    {
        if (!this.producing || table is not { Worst: >= IdentityClasses.Unproven } || scope.Constraints is not { HasTypeIdentity: true } environment)
        {
            return;
        }

        var start = this.identityGroups.Count;
        try
        {
            _ = table.OwnError(scope.Parent is { } parent ? this.ClassesOf(parent, [], out _) : null, this.identityGroups);
            this.RecordErrorGroups(scope, environment, table, start);
        }
        finally
        {
            this.identityGroups.RemoveRange(start, this.identityGroups.Count - start);
        }
    }

    // R7-C3 C3d (PLAN G830, G797; SPEC 8.2.3, 23.1.2), the one recorder of a scope's own error groups (identityGroups past `start`,
    // OwnError's over `own`), for the validation step's table and a closure's alike (e2-7 C1). Each group in an error state is its own
    // problem beside any failure of its site: at the last own premise clause reaching it (PremiseErrorClause), else at its supplier
    // where the supplier holds it erroneous (PremiseErrorSupplier: the supplier's own record stands, none here), else at the owner. A
    // group whose site holds a record of the scope's from an earlier run (RecordsAt) is that record and records nothing; a group in a
    // limit state beside a worse one stays behind the worst. The scope's entry is written before any record (its readers read it; the
    // step writes its own, a closure's first error group here), and each group's site before its record, which Fail may read in an
    // open frame (HoldsGroupRecord). A table with no own group in an error state writes nothing.
    private void RecordErrorGroups(BindingScope scope, ConstraintEnvironment environment, IdentityClasses own, int start)
    {
        var (end, earlier) = (this.identityGroups.Count, this.identityGroupSites.Count);
        Koto? first = null;
        for (var i = start; i < end; i++)
        {
            var (group, state) = this.identityGroups[i];
            if (state < IdentityClasses.Unproven)
            {
                continue;
            }

            this.identityRecorded.TryAdd(scope, (scope.Owner.BindingFailure == BindingFailure.None, false, null, false));
            Koto? site = this.PremiseErrorClause(scope, environment, own, group);
            if (site is null && this.PremiseErrorSupplier(scope, own, group) is { } supplier)
            {
                first ??= supplier;
                continue;
            }

            site ??= scope.Owner;
            first ??= site;
            if (this.RecordsAt(scope, site, earlier))
            {
                continue;
            }

            this.identityGroupSites.Add((scope, site));
            this.RecordNumbered(site, state == IdentityClasses.Unproven ? BindingFailure.UnprovenConstraint : BindingFailure.InvalidConstraint, []);

            // F's C2/C3 rulings 332, 333 (SPEC 8.3.4, 23.1.2, rule W): a group whose meets read a binder's emptied admitted set (PairHead's
            // 8.3.4 Error) read that failed input, so its record rests on the Error's, which the validation step's proof records after
            // this one at the owner of the environment emptying the set (JudgeFacts): a pending prerequisite, read at publication
            // (IsDerived). A group that reads no emptied set records at its site.
            if (own.EmptiedPair(group) is { } whole && this.EmptyingOwner(scope, whole) is { } emptier && !ReferenceEquals(emptier, site))
            {
                this.AddPendingPrerequisite(site, emptier);
            }
        }

        // F's C2/C3 ruling 331 (B): an owner with no failure and no record of its own is Invalid with no failure and no part, and its
        // cause is the first record's site (IdentityErrorSite), which the cause queries read: a use whose proof rests on the declaration
        // stays dependent (SPEC 8.2.3: never again at uses), and the context reader reads no part, so the owner keeps its context. A
        // record at the owner is its own cause: no site.
        if (first is null)
        {
            return;
        }

        var recorded = this.identityRecorded[scope];
        var atOwner = this.RecordsAt(scope, scope.Owner, this.identityGroupSites.Count);
        var rest = atOwner ? null : recorded.Site ?? (recorded.Own ? first : null);
        this.identityRecorded[scope] = (recorded.Own, recorded.Limited, rest, recorded.Judged);
        if (rest is not null && recorded.Site is null)
        {
            this.SetRecordedState(scope.Owner, BindingState.Invalid);
        }
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
        => this.identityGroupSites.Count != 0 && this.scopes.TryGetValue(declaration, out var scope) && this.RecordsAt(scope, clause, this.identityGroupSites.Count);

    // e2-7 C1: whether one of the first `count` group records (identityGroupSites) is the scope's at `site`: a recorder's run reads the
    // earlier runs' records alone (RecordErrorGroups), every other reader all of them.
    private bool RecordsAt(BindingScope scope, Koto site, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (ReferenceEquals(this.identityGroupSites[i].Scope, scope) && ReferenceEquals(this.identityGroupSites[i].Site, site))
            {
                return true;
            }
        }

        return false;
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

            foreach (var fact in environment.IdentityFacts)
            {
                if (!(own.InGroup(fact.Subject, group) || own.InGroup(fact.RequiredType, group)))
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

        return this.ClassesOf(source, [clause.Subject, clause.RequiredType], out _) is { } classes && (classes.Holds(clause.Subject) || classes.Holds(clause.RequiredType));
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
                this.ClassesOf(current, [a, b], out _) is { } classes && (classes.Holds(a) || classes.Holds(b)))
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
