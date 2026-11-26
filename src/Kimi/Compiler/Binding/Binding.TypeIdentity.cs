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
//   record per error group at its last own premise clause and a supplied group at its supplier (R7-C3 C3d), by one recorder, the
//   validation step over its own reading (RecordIdentityPremiseError, e2-7 R3.2);
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
//   premise environment cannot be rented); a reading reads one table, R(A) over its asked closures (e2-7 R3.1), and only a Final
//   table is kept, by SerialOf, for one publication (the base table and T(G) per view, a reading's per its key), a table that is not
//   being rebuilt per read: the provisional path costs reads times the view's premise nodes (the reads include the premise clauses'
//   own, so it is quadratic in the premises and linear in the uses); warm 0 B (pooled tables).
// - Prohibitions: no premise is withheld, ranked or chosen by order, and no mode parameter; no judgment inside the closure but the
//   attribute domains', which read no identity; an erroneous class proves nothing and makes its declaration's context no failed one.
public sealed partial class Binding
{
    // e2-7 (R3.1, R3.4): per view scope, the base table T(G ∪ U) (a reading with no closure, ReadingOf) and T(G) (a closure's first
    // round, ExtendedClassesOf), each with the serial it is kept for (-1: kept, not memoized), rebuilt in place at its next read.
    private readonly Dictionary<BindingScope, (int Serial, IdentityClasses Classes)> identityClasses = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BindingScope, (int Serial, IdentityClasses Classes)> identityGrounds = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<BindingScope> identityBuilding = new(ReferenceEqualityComparer.Instance);
    // A scope's recorded premise error, written by the validation step (RecordIdentityPremiseError, the one recorder) before its first
    // record, once per pass: whether it is the owner's failure (the owner had none before it), whether an own class reached the closure
    // limit, and the site of its first record when the owner rests on its records (R7-C3 C3d: no record at the owner and no failure of
    // its own; an own clause or the supplier, the owner's published cause, IdentityErrorSite, and the owner's own checks are its
    // readers), else null; and its own premise clauses holding a group record, a range of identityClauses (HoldsGroupRecord).
    private readonly Dictionary<BindingScope, (bool Own, bool Limited, Koto? Site, (int Start, int Count) Clauses)> identityRecorded = new(ReferenceEqualityComparer.Instance);
    private readonly List<(int Group, byte State)> identityGroups = new(); // OwnError's own error groups (R7-C3 C3d), a stack of ranges
    private readonly List<Koto> identityClauses = new(); // identityRecorded's clause ranges, reused between passes
    private readonly Stack<IdentityClasses> identityPool = new();
    private readonly List<IdentityClasses> identityLent = new();
    private readonly List<IdentityRow> identityRows = new(); // the readings' rows while their tables are built, a stack of ranges

    // e2-7 (SPEC 8.2.3, 8.3.3 Limits; G1406-11 C2, G1406-16 B1, C2): the publication's closed roots (ContractSolver's CloseRoot), each
    // closed part filed by FileClosure under its view scope and the part as asked: its closure (IdentityClosure), a Limited one as a
    // Complete one (G1406-19). The readings' kept tables (ReadingOf), each under its closure set (a range of identityReadingClosures) and
    // Limited bit, with the serial it is memoized for (-1: kept, not memoized), chained per view scope from its last reading
    // (identityReadingHeads; G1406-18 N1: a key scan reads its scope's readings alone). All are forgotten at each publication and each
    // pass, capacity kept.
    private readonly Dictionary<(BindingScope Scope, BoundType Key), int> identityRoots = new();
    private readonly List<IdentityClosure> identityClosures = new();
    private readonly List<IdentityRow> identityClosed = new();
    private readonly Dictionary<BindingScope, int> identityReadingHeads = new(ReferenceEqualityComparer.Instance);
    private readonly List<(int Next, int Serial, int Start, int Count, bool Limited, IdentityClasses Classes)> identityReadings = new();
    private readonly List<int> identityReadingClosures = new();
    private readonly List<int> identityAsked = new(); // ClassesOf's A(X) closures while it reads, a stack of ranges
    private readonly List<BoundType> identityParts = new(); // CloseAsked's parts of X, a stack of ranges
    private readonly List<BoundType?> identitySides = new(); // OwnClasses' X, the own premises' sides, a stack of ranges
    private IdentityClasses? identityExtended; // R7-C2c2 c2-2: ExtendedClassesOf's one request-local table

    // The scaling rows' measures: the nodes the identity classes added in this compilation, ClassKey's steps and the class members
    // its Representative scanned, and the kept readings ReadingOf's key scan compared.
    internal int IdentityClassWork { get; private set; }

    internal long IdentityKeyWork { get; private set; }

    internal long IdentityReadingWork { get; private set; }

    // A row of an identity table (ClassesOver; SPEC 8.2.3's grounds): the identity premise `Fact` and its Source, which its availability
    // is read by. A fact of a view's chain names its environment (Environment: read there, as every premise is, AvailableConstraintFact);
    // a clause instance a request's walk recorded names the Contract supplying it and the clause it instantiates, as a DerivedFacts entry
    // names its source (Source, Origin; Environment null) and is read with its Availability: Provisional while its walk runs (a derived
    // availability, R7-M27), its closure's Completion once filed (FileClosure: a Complete closure's rows keep a reading's table Final);
    // a closure's pair row (FileClosure, G1406-16 B1) names nothing and is read with its closure's Completion.
    private readonly record struct IdentityRow(BoundConstraint Fact, ConstraintEnvironment? Environment, BindingSymbol? Source, IsKoto? Origin, Completion Availability);

    // A closed part's closure (FileClosure; G1406-11 C2, G1406-16 B1, C2): its final key; its pair row `part is Key` (an index of
    // identityClosed) when the part is not the key, else -1; its rows E (a range of identityClosed, the key's); what its closure spent of
    // its one budget from the part (G1406-19: its rounds and re-rootings, a filed key's included); whether it is Limited; and the serial
    // it is kept for, -1 if not kept (E27-BOUND (P), G1406-30: its walk read only what is final in the publication, CloseRoot's
    // WalkComplete, and so did its key's; read by Keeps alone, ForgetUnkeptClosures).
    private readonly record struct IdentityClosure(BoundType Key, int Pair, int Start, int Count, int Spent, bool Limited, int Serial);

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

    // The one keep rule of what was formed under `serial` (R7-M20): kept while the scope's view is the one the serial names (-1 names
    // none). Its readers: a table built for a reading, kept only when Final and the serial is unchanged across its build (C-b, R3.1); a
    // filing, by the serial it is kept for (IdentityClosure, ForgetUnkeptClosures).
    private static bool Keeps(BindingScope scope, int serial) => SerialOf(scope) == serial;

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

        foreach (var scope in this.identityGrounds.Keys)
        {
            CollectionsMarshal.GetValueRefOrNullRef(this.identityGrounds, scope).Serial = -1;
        }

        this.ForgetUnkeptClosures(); // E27-BOUND (P)
    }

    // The publication's closed roots and readings' tables are forgotten (G1406-11 C2: a closure is filed for one publication), capacity
    // kept.
    private void ForgetClosures()
    {
        this.identityRoots.Clear();
        this.identityClosures.Clear();
        this.identityClosed.Clear();
        this.identityReadingHeads.Clear();
        this.identityReadings.Clear();
        this.identityReadingClosures.Clear();
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
        this.identityGrounds.Clear();
        this.ForgetClosures();
        this.identityRecorded.Clear();
        this.identityClauses.Clear();
    }

    // e2-7 (SPEC 8.2.3, 8.3.3 Limits; R3.1, G1406-16 C1): the table a reading of the Types `asked` (its X) reads in the view `scope`,
    // and its Completion: R(A) (ReadingOf), A the closures of X's dependent abstract parts, closed at the request boundary (no request
    // open, no walk, no table build: CloseAsked, ContractSolver's CloseRoot). Inside a request, a walk or a build A is empty, so the
    // reading is the base table T(G ∪ U): it reads no closure another reader filed. Its Limited bit is the reading's own limit, read by
    // its judge alone (ReadIdentityLimit). A re-entered build answers nothing (Provisional).
    private IdentityClasses? ClassesOf(BindingScope scope, ReadOnlySpan<BoundType?> asked, out Completion completion)
    {
        var (start, serial) = (this.identityAsked.Count, SerialOf(scope));
        try
        {
            var limited = this.enumerateDepth == 0 && this.enumerateWalking == 0 && this.identityBuilding.Count == 0 && this.CloseAsked(scope, serial, asked, start);
            return this.ReadingOf(scope, serial, start, limited, out completion);
        }
        finally
        {
            this.identityAsked.RemoveRange(start, this.identityAsked.Count - start);
        }
    }

    // A(X)'s closures (G1406-11 C2; R3.1): each dependent abstract part of the asked Types (AddAbstractParts), read from the
    // publication's filing or closed under `serial` (ContractSolver's CloseRoot, which files it). Appended to identityAsked from `start`,
    // each once, are those holding rows of a reading (a pair row or E, G1406-16 B1); a closure with neither (a prefix closure) gives the
    // reading its Limited bit alone. Returns whether one is Limited.
    private bool CloseAsked(BindingScope scope, int serial, ReadOnlySpan<BoundType?> asked, int start)
    {
        var parts = this.identityParts.Count;
        foreach (var type in asked)
        {
            this.AddAbstractParts(type, parts);
        }

        var limited = false;
        for (var p = parts; p < this.identityParts.Count; p++)
        {
            var part = this.identityParts[p];
            if (!part.IsAbstractType)
            {
                continue; // a composite the walk met (AddAbstractParts), no part of A(X)
            }

            if (!this.identityRoots.TryGetValue((scope, part), out var closure))
            {
                closure = this.CloseRoot(scope, serial, part);
            }

            var filed = this.identityClosures[closure];
            limited |= filed.Limited;
            if ((filed.Pair >= 0 || filed.Count != 0) && this.identityAsked.IndexOf(closure, start, this.identityAsked.Count - start) < 0)
            {
                this.identityAsked.Add(closure);
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

    // Files a closed root for the publication (CloseRoot's one writer of the filing; G1406-11 C2, G1406-16 B1, C2) and returns the
    // closure of `entry`, closed to `key` by the open request (its last round: enumerateIdentities' rows, enumerateClasses' table,
    // enumerateCompletion) after spending `spent` of its one budget (G1406-19), kept for `serial` (-1: not kept; CloseRoot gives the
    // reading's serial where its walk read only what is final in the publication, IdentityClosure). A restart onto a
    // filed key reads that filing, so the entry spends what the key spent as well. An entry other than its key past
    // PremiseClosureLimit is filed alone, Limited, with no rows, and its key is not filed: the key's own closure has its own budget, so
    // what is filed is a function of (entry, G) whichever reader closes first (SPEC 8.3.3). Otherwise the key, unless filed, is filed
    // with E, its last round's rows that join its class in that round's table (R3.1: a prefix closure's E is empty), and its spending
    // (an unfiled key is the entry itself: CloseRoot closes a key other than its root first), Limited past the budget; a Limited
    // closure is filed for the publication as a Complete one is (G1406-19). An entry other than the key is filed with the key's rows and
    // the pair row `entry is key`, read with the request's Completion: the link its re-rootings rest on, which the restart's cleared rows
    // held (SPEC 8.3.3: the identities obtained before it are kept).
    private int FileClosure(BindingScope scope, BoundType entry, BoundType key, int spent, int serial, Completion completion)
    {
        var known = this.identityRoots.TryGetValue((scope, key), out var closure);
        spent += known ? this.identityClosures[closure].Spent : 0;
        if (spent > PremiseClosureLimit && !ReferenceEquals(entry, key))
        {
            return File(entry, new(entry, -1, 0, 0, spent, true, serial));
        }

        if (!known)
        {
            var start = this.identityClosed.Count;
            foreach (var row in this.enumerateIdentities)
            {
                if (this.enumerateClasses is { } last && (last.Joins(row.Fact.Subject!, key) || last.Joins(row.Fact.RequiredType!, key)))
                {
                    this.identityClosed.Add(row with { Availability = this.enumerateCompletion });
                }
            }

            closure = File(key, new(key, -1, start, this.identityClosed.Count - start, spent, spent > PremiseClosureLimit, serial));
        }

        if (ReferenceEquals(entry, key))
        {
            return closure;
        }

        var filed = this.identityClosures[closure];
        this.identityClosed.Add(new(this.InternConstraint(new(ConstraintKind.TypeIdentity, entry, filed.Key)), null, null, null, completion));
        return File(entry, filed with { Pair = this.identityClosed.Count - 1, Spent = spent, Serial = Math.Min(filed.Serial, serial) }); // kept when both are

        int File(BoundType part, IdentityClosure filing)
        {
            this.identityClosures.Add(filing);
            return this.identityRoots[(scope, part)] = this.identityClosures.Count - 1;
        }
    }

    // E27-BOUND (P): a publication starts: the readings are forgotten, and every filing but one kept for the view its scope still
    // denotes (Keeps, as for tables): such a closure read only what is final in the publication and is a function of (root, G) for an
    // unchanged G, so the publication cannot change it. A kept filing's rows stay in identityClosed. Each other filing is removed as it
    // is met (Dictionary.Remove keeps the enumeration valid), so the step allocates nothing.
    private void ForgetUnkeptClosures()
    {
        this.identityReadingHeads.Clear();
        this.identityReadings.Clear();
        this.identityReadingClosures.Clear();
        foreach (var (key, closure) in this.identityRoots)
        {
            if (!Keeps(key.Scope, this.identityClosures[closure].Serial))
            {
                this.identityRoots.Remove(key);
            }
        }
    }

    // e2-7 (R3.1; G1406-16 B1), the one keep step of a reading's table: R(A) = ClassesOver(G ∪ U ∪ the rows of A's closures, each pair
    // row and E), A the closures appended to identityAsked from `start` (CloseAsked), marked Limited when `limited` (a closure of A is).
    // It is keyed by (`serial`, A, the Limited bit), a function of (G, A, U), so the order of readers changes no reading; the key with
    // no closure and the bit clear is the base table T(G ∪ U), one per view scope. The key is scanned among the scope's readings alone
    // (identityReadingHeads' chain; G1406-18 N1). Each key's table is rebuilt in place at its next read unless a build is nested in
    // another (a reader may hold it), and memoized only when Final and the serial is unchanged across its build (Keeps). A re-entered
    // build answers nothing (Provisional).
    private IdentityClasses? ReadingOf(BindingScope scope, int serial, int start, bool limited, out Completion completion)
    {
        var count = this.identityAsked.Count - start;
        var based = count == 0 && !limited;
        var kept = based ? this.identityClasses.GetValueOrDefault(scope) : default;
        var at = -1; // the key's entry in identityReadings
        for (var k = based ? -1 : this.identityReadingHeads.GetValueOrDefault(scope, -1); at < 0 && k >= 0; k = this.identityReadings[k].Next)
        {
            var reading = this.identityReadings[k];
            this.IdentityReadingWork++;
            if (reading.Count == count && reading.Limited == limited && this.SameClosures(reading.Start, start, count))
            {
                (at, kept) = (k, (reading.Serial, reading.Classes));
            }
        }

        if (kept.Classes is { } held && kept.Serial == serial)
        {
            completion = held.Completion;
            return held;
        }

        completion = Completion.Provisional;
        if (!this.identityBuilding.Add(scope))
        {
            return null;
        }

        var rows = this.identityRows.Count;
        try
        {
            var classes = kept.Classes is { } reused && this.identityBuilding.Count == 1 ? reused : this.RentClasses();
            this.AddViewRows(scope);
            for (var a = start; a < this.identityAsked.Count; a++)
            {
                var closure = this.identityClosures[this.identityAsked[a]];
                if (closure.Pair >= 0)
                {
                    this.identityRows.Add(this.identityClosed[closure.Pair]);
                }

                this.identityRows.AddRange(CollectionsMarshal.AsSpan(this.identityClosed).Slice(closure.Start, closure.Count));
            }

            this.ClassesOver(classes, scope, this.identityRows, rows);
            if (limited)
            {
                classes.MarkLimited();
            }

            completion = classes.Completion;
            var memo = classes.Final && Keeps(scope, serial) ? serial : -1; // -1: kept, not memoized
            if (based)
            {
                this.identityClasses[scope] = (memo, classes);
            }
            else if (at >= 0)
            {
                var reading = this.identityReadings[at];
                this.identityReadings[at] = (reading.Next, memo, reading.Start, count, limited, classes);
            }
            else
            {
                this.identityReadings.Add((this.identityReadingHeads.GetValueOrDefault(scope, -1), memo, this.identityReadingClosures.Count, count, limited, classes));
                this.identityReadingHeads[scope] = this.identityReadings.Count - 1;
                this.identityReadingClosures.AddRange(CollectionsMarshal.AsSpan(this.identityAsked).Slice(start, count));
            }

            return classes;
        }
        finally
        {
            this.identityRows.RemoveRange(rows, this.identityRows.Count - rows);
            this.identityBuilding.Remove(scope);
        }
    }

    // A table from the pool, lent for the pass (ResetIdentityClasses returns it).
    private IdentityClasses RentClasses()
    {
        var classes = this.identityPool.Count != 0 ? this.identityPool.Pop() : new IdentityClasses(this);
        this.identityLent.Add(classes);
        return classes;
    }

    // Whether a kept reading's closure set (identityReadingClosures from `kept`) is the asked one (identityAsked from `asked`), both
    // `count` long.
    private bool SameClosures(int kept, int asked, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (this.identityAsked.IndexOf(this.identityReadingClosures[kept + i], asked, count) < 0)
            {
                return false;
            }
        }

        return true;
    }

    // G1406-11 C-a (SPEC 8.3.3 Limits), e2-7 R3.3: a judgment that its reading of a Limited table (R(A), ClassesOf) leaves unproven, or
    // a rows reader's answer that the cut rows could make negative (FirstRows' readers), is refused (RefuseProof: AnalysisLimit, at
    // the use whose check asked, else at the scope's owner), read from that reading alone, never from scope state; a judgment over a
    // table that is not Limited reads nothing. G1406-18 C1 (rule W, SPEC 23.1.2): the asking `node`, whose check reads the negative
    // answer, rests on the refusal's recorded site where that is another node (RestOnPart), so the check's later records over the same
    // reading derive from the limit (IsDerived); a refusal held (a trial or proof) or after the pass records nothing and rests nothing.
    // A judge inside a proof passes null: its answer reaches its check through the proof's own limit.
    private void ReadIdentityLimit(Koto? node, BindingScope scope, IdentityClasses? classes)
    {
        if (classes is { Limited: true } && this.RefuseProof(scope, BindingFailure.AnalysisLimit) is { } refused &&
            node is not null && !ReferenceEquals(refused, node))
        {
            this.RestOnPart(node, refused);
        }
    }

    // R7-C2c2 c2-2, e2-7 R3.4: a closure's round table, the declaration's classes with the identity rows its walks recorded (SPEC 8.2.3's
    // grounds include the Constraints `T is C` supplies at every depth): ClassesOver those rows. A round with rows builds one reused
    // request-local table, never memoized; its rows are Provisional (a derived availability), so nothing keyed by it is kept, and at
    // R7-C2e2 e2-1's merge what it keys stays the request's. The first round, with no rows, reads T(G), kept per view scope and
    // `serial` (Keeps), so a closure builds no table of the grounds alone. Its keys follow the table's rule: a constructed member keys
    // to itself (Representative). R7-C2c2 c2-4b (DECISIONS R4.13, R7-M44): its premises are the declaration's grounds alone
    // (ClassesOver's, ConstraintEnvironment.DeclaredIdentityFacts), never a use's supply, so a closure is a function of the declaration's
    // premises and its own rows (SPEC 8.3.3 Limits).
    private IdentityClasses? ExtendedClassesOf(BindingScope scope, int serial, List<IdentityRow> rows)
    {
        var kept = rows.Count == 0 ? this.identityGrounds.GetValueOrDefault(scope) : default;
        if (kept.Classes is { } held && kept.Serial == serial)
        {
            return held;
        }

        if (!this.identityBuilding.Add(scope))
        {
            return null;
        }

        try
        {
            if (rows.Count != 0)
            {
                return this.ClassesOver(this.identityExtended ??= new IdentityClasses(this), scope, rows, 0);
            }

            var classes = this.ClassesOver(kept.Classes is { } reused && this.identityBuilding.Count == 1 ? reused : this.RentClasses(), scope, rows, 0);
            this.identityGrounds[scope] = (classes.Final && Keeps(scope, serial) ? serial : -1, classes);
            return classes;
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
    // make them one (Verdict's Proven); an erroneous or provisional class proves nothing. `read` is the table the answer read: a judge
    // that leaves the judgment unproven reads its Limited bit (ReadIdentityLimit; G1406-11 C-a); none (null) where known heads differ
    // (IdentityClasses.Distinct: the answer reads no closure, G1406-26 D1). It never refuses, so a branch that reads it records nothing
    // of its own (C3).
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
        read = IdentityClasses.Distinct(a, b) ? null : classes;
        return false;
    }

    // SPEC 8.2.3 (Refuted), 8.3.2 ("absence of proof is not enough"; "Unbound Types ... are never negative results"): whether two
    // Types are known to differ at a corresponding position in the view, `a` in the terms of the conforming Type `self` (a
    // conformance's instance: a Contract's Self is `self`, SPEC 8.4) and `b` in the scope's (a required reference: a Contract's Self is
    // the scope's own), completed projections first (ContractType); the view's classes judge it by their one table (Refuted). A judge
    // refutes two Types' agreement only so; a pair with no known difference that it cannot prove is Unknown (the Contract judge's
    // paths, the Callable judge's signature), and so is any pair while the view's table is being built, and any pair at a Limited root
    // (SPEC 8.3.3: no Refuted rests on the limited analysis; G1406-11 N1) unless known heads differ (IdentityClasses.Distinct: the
    // answer reads no closure, G1406-26 D1): its judge left Unknown reads the limit of `read`, the table the answer read (ReadIdentityLimit).
    private bool IdentityRefuted(BoundType a, BoundType b, BindingScope scope, BoundType? self, out IdentityClasses? read)
    {
        var (left, right) = (this.ContractType(a, scope, self), this.ContractType(b, scope));
        var classes = this.ClassesOf(scope, [left, right], out _);
        read = IdentityClasses.Distinct(left, right) ? null : classes;
        return classes is not null && read is not { Limited: true } && classes.Refuted(left, right);
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

    // e2-7 (G1406-11's C1, R3.2; SPEC 8.2.3): a scope's step reading, the table its validation step records from: ClassesOf over X_s,
    // the sides of every identity fact the scope Declares (its written premises and the eager supply of its own Contract premises,
    // ConstraintEnvironment.DeclaredIdentityFacts), so their roots close at the step, and R(A(X_s)) is the table the recorder
    // (RecordIdentityPremiseError), its parent comparison and the record readers (IdentityPremiseErrorRead, HoldsClause) read. A scope with
    // no environment reads with no X. The sides are a range of identitySides, removed before it returns.
    private IdentityClasses? OwnClasses(BindingScope scope)
    {
        var start = this.identitySides.Count;
        for (var f = 0; scope.Constraints is { } environment && f < environment.DeclaredIdentityFacts.Count; f++)
        {
            this.identitySides.Add(environment.DeclaredIdentityFacts[f].Subject);
            this.identitySides.Add(environment.DeclaredIdentityFacts[f].RequiredType);
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
    // record, as before. e2-7 R3.2: the one recorder of a scope's premise error, once per pass, from its step reading (OwnClasses), whose
    // kinds, worst state, sites and numbering are that one table's; its own error groups are those the parent's step reading does not
    // hold (OwnError over OwnClasses(parent): a group the parent records is never recorded again in the child, SPEC 8.2.3). No closure
    // records: a group any reading forms in the scope has a side the scope Declares, so its class is in the step's table (R3.2's proof,
    // with the closures' pair rows, G1406-16 B1); a group formed only past a Limited closure's bound is the residual STATUS names, its
    // reader refusing.
    private bool RecordIdentityPremiseError(BindingScope scope, BindingMode mode)
    {
        if (scope.Constraints is not { HasTypeIdentity: true } environment || this.identityRecorded.ContainsKey(scope) ||
            this.OwnClasses(scope) is not { Worst: not IdentityClasses.One } own)
        {
            return false;
        }

        var start = this.identityGroups.Count;
        try
        {
            var (worst, waiting, limited) = own.OwnError(scope.Parent is { } parent ? this.OwnClasses(parent) : null, this.identityGroups);
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

            this.RecordErrorGroups(scope, environment, own, start);
            return true;
        }
        finally
        {
            this.identityGroups.RemoveRange(start, this.identityGroups.Count - start);
        }
    }

    // R7-C3 C3d (PLAN G830, G797; SPEC 8.2.3, 23.1.2): the step's own error groups (identityGroups past `start`, OwnError's over `own`,
    // the step reading), recorded after the scope's entry. Each group in an error state is its own problem beside any failure of its
    // site: at the last own premise clause reaching it (PremiseErrorClause), else at its supplier where the supplier holds it erroneous
    // (PremiseErrorSupplier: the supplier's own record stands, none here), else at the owner; RecordNumbered numbers the records at one
    // site. A group in a limit state beside a worse one stays behind the worst. Each clause site is published in the entry's clause range
    // before its record, which Fail may read in an open frame (HoldsGroupRecord).
    private void RecordErrorGroups(BindingScope scope, ConstraintEnvironment environment, IdentityClasses own, int start)
    {
        var (owned, limited, _, (clauses, _)) = this.identityRecorded[scope];
        Koto? first = null;
        var atOwner = false;
        for (var i = start; i < this.identityGroups.Count; i++)
        {
            var (group, state) = this.identityGroups[i];
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
                this.identityClauses.Add(site);
                this.identityRecorded[scope] = (owned, limited, null, (clauses, this.identityClauses.Count - clauses));
            }

            site ??= scope.Owner;
            first ??= site;
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
        }

        // F's C2/C3 ruling 331 (B): an owner with no failure and no record of its own is Invalid with no failure and no part, and its
        // cause is the first record's site (IdentityErrorSite), which the cause queries read: a use whose proof rests on the declaration
        // stays dependent (SPEC 8.2.3: never again at uses), and the context reader reads no part, so the owner keeps its context. A
        // record at the owner is its own cause: no site.
        var rest = owned && !atOwner ? first : null;
        this.identityRecorded[scope] = (owned, limited, rest, (clauses, this.identityClauses.Count - clauses));
        if (rest is not null)
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
    // group (identityRecorded's clause range), so the declaration's frame is that record's own check (ChecksOwnSite), as it is where the
    // record stands at the declaration.
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

    // Whether a declaration outside `scope`'s chain holds a clause erroneous in its own classes: its step reading (OwnClasses, e2-7 R3.2),
    // the table its own records come from (rule W).
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

        return this.OwnClasses(source) is { } classes && (classes.Holds(clause.Subject) || classes.Holds(clause.RequiredType));
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
    // e2-7 R3.2 (rule W): the producer's classes are its step reading (OwnClasses), the table its records come from, so a node rests on a
    // premise error only where a step recorded its group; a class in error only in the reader's own Limited reading refuses there.
    private PremiseFailure? IdentityPremiseErrorRead(BindingScope scope, BoundType? a, BoundType? b)
    {
        BindingScope? producer = null;
        for (var current = scope; current is not null && this.identityRecorded.Count != 0; current = current.Parent)
        {
            // The producer's own checks (its frame) are the error's sites where it records at the owner, never its readers
            // (ChecksOwnSite); with its records at its clauses or its supplier (R7-C3 C3d) they read it as any check does.
            if (this.identityRecorded.TryGetValue(current, out var recorded) && recorded.Own && (recorded.Site is not null || !this.ChecksOwnSite(current.Owner)) &&
                this.OwnClasses(current) is { } classes && (classes.Holds(a) || classes.Holds(b)))
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
