// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns MIR bodies by definition key, the validator's findings, the drop table the analyses find, the builder's layer-walk work and the
// version's MirReport: the records of construction and collection (MirRecordClass; R6-check H3-1) and the analyses' problems.
// Input: built definitions, findings, records and problems. Output: reusable bodies identified by MirDefKey and numbered in order, the body
// a callee runs (Callee), the initializer a staticEnsure names (StaticInit) and a record class's rows (Records, which MirDump, tests and
// benchmarks read; publication reads MirReport.Merged, records included, DECISIONS R5-M185).
// Owns the ordinal space (R8-L1 M-B1, R8-M33; PLAN G951 U2-K, G950 R-4): [0, LibraryCount) is the library's range in every build, the
// library's templates and then the library walk's definitions, each at its position in the walk, which MirBuilder numbers through its
// one node-to-definition filter without building (MirBuilder.Library, Slot; Reserve states the range), so a library body has one
// ordinal in an eager and a lazy build and in every version of a library segment, and a slot whose definition no lookup demanded is
// empty; this class numbers every other body from LibraryCount in build order (Begin). A slot is built exactly when the key map holds
// its definition. LibraryCount's readers are this class (Reserve, ClearProgram: the next ordinal it numbers), the tests' premise rows
// and the dump; no consumer tests an ordinal against it.
// Keeps a body's storage by its ordinal (the pool) and the bodies in build order (PLAN G951 U2-K). Body(ordinal), the one read by
// ordinal, reads the pool and fails for an ordinal no body of the version is built at. Bodies is the build-order list, the compilation's
// own bodies, which a walker of the whole program iterates. Its walkers: MirAnalysis's step 4, MirOrigins.Findings, MirDump.Write,
// InstanceCollector's roots (with A3b) and templates, and the Benchmark's counts and queries (CompilerPipelineBenchmark,
// CollectionCostMeasurements, PairCaseMeasurements, StaticKeyMeasurements). LibraryBodies is the list's prefix, the library list (the
// bodies built when the library segment ended: in an eager build every library body), and VersionBodies the rest, the bodies built past
// the library segment in the version: the program's and, in a build whose root range is the program's, each library body a lookup
// demanded; each is the span a walker of that part takes as its input: MirAnalysis's library step and StaticKeyMeasurements' local
// facts. A position of the list is not an ordinal: the builder's program pass (MirBuilder.Name, the library segment's drained before
// EndLibrary), the analyses' stepping (MirAnalysis.Steps) and the collector's templates walk the list by position from the library
// list's length or 0. No walker tests an ordinal against LibraryCount
// itself; an owner keeping per-body entries in a store per segment reads them by the ordinal itself through one published fact, A2's
// MirDropTable.LibraryHolds (its library store holds the body's closed entry; PLAN G951 U2-K, the Back's C-378 (1)(a) as amended):
// MirDropTable.Of, MirEffects.Base and InstanceCollector.TemplatesOf read it.
// Owns the segments' versions (PLAN G950): the library segment, its bodies, their keys and findings and its construction records, is kept
// while its library version holds (LibraryVersion, EndLibrary), the program segment rebuilt per program version over it (ClearProgram);
// the library's findings stay the prefix of Findings and its records are replayed into the version's report first, where the builder's
// library walk added them, so a kept version's report and findings equal a rebuild's row for row.
// A body is built in its segment's reused body (Begin), one for the library's prefix and one for the program's bodies, so that neither
// segment's construction allocates by the other's bodies, and stored in the next pooled body at End, which registers its key: a stored
// body is grown only to exactly the rows stored in it, and a building body's tables and interning grow once to its segment's largest
// body's.
// Its lookups (this[key], Callee, StaticInit) are the one demand point (PLAN G951): a key without a body is looked up by its phase's row
// of the demand table (Phase): while Open, the builder that opened the version builds the body if the HIR publishes the definition with
// a value outside the root range the builder built eagerly (MirBuilder.Demand); while the analyses judge, a lookup that would build throws;
// once Sealed, a lookup reads only. Null means only "this definition has no body", never "not built yet".
// One key identifies one body, in one key map whose lookup is one read; warm rebuilding reuses body storage; Clear resets the report and
// both segments, ClearProgram the report and the version's bodies.
// Must not re-bind definitions or maintain a second body map in a consumer; a reader by ordinal reads through Body, never offsets an
// ordinal, computes one from LibraryCount or takes a position of the build-order list for one.

using System.Collections;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

/// <summary>The MIR bodies of a compilation by <see cref="MirDefKey"/>, with the records their construction, validation and collection made.
/// <see cref="Clear"/> returns every body to the pool and <see cref="End"/> reuses the pooled body of each ordinal, so a warm rebuild of
/// the same definitions allocates nothing.</summary>
internal sealed class MirProgram
{
    private readonly Dictionary<MirDefKey, MirBody> bodies = new();
    private readonly MirBody[] building = [new(), new()]; // The body each segment is built in: the library's prefix, then the program's.
    private readonly MirReport.Kept libraryRecords = new(); // The library segment's construction records, replayed first in a kept version.
    private MirBody[] pool = []; // By ordinal: a body's storage, reused by the body built at the ordinal next (End).
    private MirBody[] order = []; // The bodies built, in build order: the library list, then the version's bodies.
    private int count; // The bodies built, the build-order list's length.
    private int library; // The library list's length: the bodies built when the library segment ended (EndLibrary).
    private int next; // The next ordinal this class numbers (Begin): past every ordinal built and, once reserved, the library's range.
    private int libraryFindings; // The library segment's findings, the prefix of Findings.
    private MirBuilder? builder; // The builder that opened the version, which a lookup while Open demands a body from.

    /// <summary>The demand table (PLAN G951): a row per phase of a program version, each what a lookup of a key without a body does.</summary>
    internal enum Phase
    {
        /// <summary>From <see cref="Open"/> (the build, A4's rounds, the collector's runs): a lookup builds the body the HIR publishes a
        /// value for, validates it and runs its program pass (<see cref="MirBuilder.Demand"/>); the driver's one stepping path
        /// (<see cref="MirAnalysis.Step"/>) steps it before the reader's next round.</summary>
        Open,

        /// <summary>While the analyses judge (MirAnalysis.Run's step 4 and MirOrigins.Findings): a lookup that builds a body is a broken
        /// contract and throws. It is unreachable: A5 looks up only at a site with a static Loan or a static's Drop, where its keys replay A4's
        /// site inputs, whose lookups A4's clean round made (one there that built a body with views would have made the round miss), and its
        /// static initializers, which the root program pass built; Findings looks nothing up. A definition without a body is null, as in
        /// every row (null means only "no body"): a replayed site may name a witness or import without one.</summary>
        Judging,

        /// <summary>Before a builder opens a version and from the end of Compilation's <c>BuildMir</c>: a lookup reads only.</summary>
        Sealed,
    }

    /// <summary>Gets the bodies a walker of the whole program visits, in build order: the compilation's own, the library's included
    /// where the compilation builds them. A position here is not an ordinal.</summary>
    internal ReadOnlySpan<MirBody> Bodies => this.order.AsSpan(0, this.count);

    /// <summary>Gets the library list, the prefix of <see cref="Bodies"/>: the bodies built when the library segment ended
    /// (<see cref="EndLibrary"/>), which a kept segment keeps.</summary>
    internal ReadOnlySpan<MirBody> LibraryBodies => this.order.AsSpan(0, this.library);

    /// <summary>Gets the version's bodies, the rest of <see cref="Bodies"/>: those built past the library segment in this version, the
    /// program's and each library body a lookup of the version demanded.</summary>
    internal ReadOnlySpan<MirBody> VersionBodies => this.order.AsSpan(this.library, this.count - this.library);

    /// <summary>Gets the library's ordinal range, <c>[0, LibraryCount)</c>, the same in every build of a library segment (R8-L1 M-B1,
    /// R8-M33; PLAN G951 U2-K): the library's templates and the library walk's definitions (<see cref="Reserve"/>), whether or not a
    /// definition's body is built; every other body's ordinal follows it.</summary>
    internal int LibraryCount { get; private set; }

    /// <summary>Gets the library version the library segment was built for (0: none), the stamp a component keeping a library segment's
    /// facts compares (PLAN G950): the Binding pass until G949's library publication version replaces it (G950-K).</summary>
    internal ulong LibraryVersion { get; private set; }

    /// <summary>Gets the count of the library segments this program has built, which <see cref="EndLibrary"/> advances and nothing
    /// resets: with the program itself, the key a component keeping a library segment's facts compares (the analyses' library state,
    /// the collector's library templates; PLAN G951 U2-K), so facts of another program or of a segment built again are never kept.</summary>
    internal int LibrarySegment { get; private set; }

    /// <summary>Gets or sets the Binding pass the bodies were built from (0: none).</summary>
    internal ulong Version { get; set; }

    /// <summary>Gets the root range the version was opened with: the builder built the bodies of the modules below it eagerly, and builds
    /// any other on demand (<see cref="MirBuilder.Build"/>); one version is built with one range, and every version of a builder with
    /// the library on one side of it.</summary>
    internal int RootModules { get; private set; }

    /// <summary>Gets the version's row of the demand table.</summary>
    internal Phase Demands { get; private set; } = Phase.Sealed;

    /// <summary>Gets the validator's findings, which Compilation's <c>PublishMirRecords</c> publishes as <c>InternalInvariant_Kd</c> after
    /// the merged problems (DECISIONS R5-M185): the library segment's first, its program pass's included, then the program's.</summary>
    internal List<MirFinding> Findings { get; } = [];

    /// <summary>Gets the records and problems of this version: the construction and collection records (<see cref="Report"/>) and the
    /// problems the analyses found, which Compilation publishes from <c>MirReport.Merged</c>, records included, and whose Errors decide
    /// acceptance (DECISIONS R5-M185); <see cref="Clear"/> resets them with the bodies, and <see cref="ClearProgram"/> to the library's records.</summary>
    internal MirReport Problems { get; } = new();

    /// <summary>Gets drop elaboration's verdicts and flags in this version, by body ordinal (A2, DECISIONS R4-M6a): its program store is
    /// cleared where a version starts (<see cref="ClearProgram"/>, <see cref="Clear"/>) and its library store where a library segment
    /// starts (<see cref="Clear"/>; <see cref="MirAnalysis"/> again where its library state is another program's or segment's); only
    /// MirAnalysis's library step writes the library store.</summary>
    internal MirDropTable Drops { get; } = new();

    /// <summary>Gets or sets the work of the builder's layer walks in this version (PLAN G647): one per iteration of a walk (a layer's
    /// step and a targeted walk's agreement test together), and a premising as the premised Type's height plus one, the parts premising
    /// visits on a layer spine and fewer than it visits on a wide Type; the walks' scaling rows read it, and <see cref="Clear"/> and
    /// <see cref="ClearProgram"/> reset it.</summary>
    internal long LayerWalkWork { get; set; }

    /// <summary>Gets the body the current segment is built in: the library's until the library segment ends (<see cref="EndLibrary"/>),
    /// then the version's.</summary>
    private MirBody Building => this.building[this.LibraryVersion != 0 ? 1 : 0];

    /// <summary>Gets the body of a definition, the demand point: a key without a body is looked up by the version's row of the demand
    /// table (<see cref="Demands"/>).</summary>
    /// <param name="key">The definition.</param>
    /// <returns>The body, or null for a definition without one.</returns>
    internal MirBody? this[MirDefKey key] => this.bodies.TryGetValue(key, out var body) ? body : this.Demand(key);

    /// <summary>Gets the body of an ordinal, the library's or the program's, the one read by ordinal: of a body built in this version
    /// or kept with the library segment only. An ordinal without one (never built, or returned to the pool) is a broken contract, never
    /// null or an earlier version's body.</summary>
    /// <param name="ordinal">The body's ordinal (<see cref="MirBody.Ordinal"/>).</param>
    /// <returns>The body.</returns>
    internal MirBody Body(int ordinal) => (uint)ordinal < (uint)this.pool.Length && this.pool[ordinal] is { } body && body.Ordinal == ordinal ? body
        : throw new InvalidOperationException($"No body is built at ordinal {ordinal} (InternalInvariant).");

    /// <summary>Gets the body a Fn callee runs: a default template's (its Arg, the index plus 1), else its function's, getter's or setter's.</summary>
    /// <param name="callee">The callee.</param>
    /// <returns>The body, or null for a function without one.</returns>
    internal MirBody? Callee(in MirCallee callee) => callee.Arg > 0 ? this[new(callee.Symbol, MirBodyKind.DefaultTemplate, callee.Arg - 1)]
        : this[new(callee.Symbol, MirBodyKind.Fn)] ?? this[new(callee.Symbol, MirBodyKind.Getter)] ?? this[new(callee.Symbol, MirBodyKind.Setter)];

    /// <summary>Gets the initializer a <c>staticEnsure</c> names (plan 5.6.6): the StaticInit body of the static whose index in the calling
    /// body's statics the call's operand holds as a constant. The builder ensures only statics whose initializer it builds, which
    /// <see cref="MirValidator"/> checks, so a consumer of a validated program never meets null (G191).</summary>
    /// <param name="body">The calling body.</param>
    /// <param name="operand">The staticEnsure call's operand.</param>
    /// <returns>The body, null only in a program the validator rejects.</returns>
    internal MirBody? StaticInit(MirBody body, in MirOperand operand) => this.StaticInit(body, (int)body.Consts[operand.Arg].Bits);

    /// <summary>Gets the initializer of a static a body accesses: the StaticInit body of the static at an index of the body's statics. A
    /// validated body ensures each static its Places access, and each staticEnsure's static has its initializer (<see cref="MirValidator"/>,
    /// G191), so a consumer of a validated program never meets null for such a static; an Opaque site's root may name a static without one.</summary>
    /// <param name="body">The accessing body.</param>
    /// <param name="slot">The static's index in <see cref="MirBody.Statics"/>.</param>
    /// <returns>The body, null for a static no staticEnsure of a validated body names.</returns>
    internal MirBody? StaticInit(MirBody body, int slot) => this[new(body.Statics[slot].Symbol, MirBodyKind.StaticInit)];

    /// <summary>Returns both segments' bodies to the pool and clears the report, A2's table, the findings and the work, for a new library
    /// segment.</summary>
    internal void Clear()
    {
        foreach (var body in this.Bodies)
        {
            body.Ordinal = -1; // Returned: no ordinal reads it (Body).
        }

        this.bodies.Clear();
        this.Problems.Clear();
        this.Drops.Clear(); // F18: no step fact of an earlier version reaches the next version.
        this.Findings.Clear();
        this.count = this.library = this.next = this.LibraryCount = this.libraryFindings = 0;
        this.LibraryVersion = 0;
        this.LayerWalkWork = 0;
    }

    /// <summary>Opens a version for its builder: a lookup builds a missing body on demand until the version is sealed.</summary>
    /// <param name="builder">The builder of the version.</param>
    /// <param name="rootModules">The root range the builder builds eagerly.</param>
    internal void Open(MirBuilder builder, int rootModules) => (this.builder, this.RootModules, this.Demands) = (builder, rootModules, Phase.Open);

    /// <summary>Enters a row of the demand table: Judging from Open and Open again from Judging around the analyses' judging
    /// (MirAnalysis.Run), Sealed at the end of Compilation's <c>BuildMir</c>. A Sealed version stays Sealed; only <see cref="Open"/> opens a
    /// version; any other step is a broken contract.</summary>
    /// <param name="phase">The row.</param>
    internal void Enter(Phase phase) => this.Demands = this.Demands == Phase.Sealed ? Phase.Sealed
        : phase == Phase.Sealed || (phase == Phase.Judging) == (this.Demands == Phase.Open) ? phase
        : throw new InvalidOperationException($"The demand table has no step from {this.Demands} to {phase} (InternalInvariant).");

    /// <summary>Reserves the library's ordinal range for a library segment, after its templates and its walk (MirBuilder.Library), which
    /// numbers the walk's definitions: <c>[0, ordinals)</c> is the library's, and the bodies this class numbers next follow it. A range
    /// below an ordinal already numbered is a broken contract.</summary>
    /// <param name="ordinals">The range's end (<see cref="LibraryCount"/>).</param>
    internal void Reserve(int ordinals) => (this.LibraryCount, this.next) = this.next <= ordinals ? (ordinals, ordinals)
        : throw new InvalidOperationException($"The library's range {ordinals} is below the next ordinal {this.next} (InternalInvariant).");

    /// <summary>Ends the library segment, built and validated for a library version (not 0): the bodies built so far are the library
    /// list (<see cref="LibraryBodies"/>), the findings so far its findings and the records so far its records, which
    /// <see cref="ClearProgram"/> keeps; it is one more segment (<see cref="LibrarySegment"/>).</summary>
    /// <param name="libraryVersion">The library version (<see cref="LibraryVersion"/>).</param>
    internal void EndLibrary(ulong libraryVersion)
    {
        (this.library, this.libraryFindings, this.LibraryVersion) = (this.count, this.Findings.Count, libraryVersion);
        this.LibrarySegment++;
        this.Problems.Keep(0, this.libraryRecords);
    }

    /// <summary>Returns the version's bodies (<see cref="VersionBodies"/>) to the pool, removing their keys, for a new program version over
    /// the kept library segment, a library body a lookup demanded included (its slot is empty again): the next ordinal numbered here is
    /// the first past the library's range, the findings keep the library's prefix, the report starts with the library's records again
    /// (MirReport.Replay), A2's program store is cleared (F18; its library store, the library step's, is kept) and the work counts the
    /// new version's.</summary>
    internal void ClearProgram()
    {
        foreach (var body in this.VersionBodies)
        {
            this.bodies.Remove(body.Key);
            body.Ordinal = -1; // Returned: no ordinal reads it (Body).
        }

        (this.count, this.next) = (this.library, this.LibraryCount);
        this.Findings.RemoveRange(this.libraryFindings, this.Findings.Count - this.libraryFindings);
        this.Problems.Clear();
        this.Problems.Replay(this.libraryRecords);
        this.Drops.ClearProgram(); // F18: no step fact of this version reaches the next version.
        this.LayerWalkWork = 0;
    }

    /// <summary>Records a construction or collection record at its location, a ControlFlow problem of every view, once per class, code and
    /// location however many bodies or items meet it (MirReport's key).</summary>
    /// <param name="site">The syntax.</param>
    /// <param name="code">For construction an Unsupported form, the expansion limit or a broken HIR contract (DECISIONS R4-M61); for
    /// collection a code of the checked set's records (InstanceCollector).</param>
    /// <param name="recordClass">The record's class, its body value.</param>
    internal void Report(SourceRef site, DiagnosticCode code = DiagnosticCode.Unsupported_Kd, MirRecordClass recordClass = MirRecordClass.Construction)
        => this.Problems.Add(new(code, DiagnosticRequirement.ControlFlow, site, (int)recordClass, ulong.MaxValue));

    /// <summary>Gets the rows of a record class in the order they were first recorded, the one path to them: the construction records, an
    /// Unsupported form's <c>Unsupported_Kd</c> (plan 6.2), the expansion limit's <c>ExpansionLimit_Kd</c> (Resource) and a
    /// broken HIR contract's <c>InternalInvariant_Kd</c>, or the collection records of the checked set; enumerating allocates nothing.</summary>
    /// <param name="recordClass">The class.</param>
    /// <returns>The rows.</returns>
    internal RecordRows Records(MirRecordClass recordClass) => new(this.Problems, recordClass);

    /// <summary>Begins the body of a definition this class numbers, of the next ordinal (a library template's before the library's
    /// range is reserved, another body's past it), in its segment's body being built, which <see cref="End"/> stores.</summary>
    /// <param name="key">The definition, which has no body yet.</param>
    /// <param name="source">The body's syntax.</param>
    /// <returns>The empty body being built.</returns>
    internal MirBody Begin(MirDefKey key, SourceRef source) => this.Begin(key, source, this.next);

    /// <summary>Begins the body of a definition at its ordinal, a library definition's at the slot MirBuilder numbered it
    /// (MirBuilder.Slot), in its segment's body being built, which <see cref="End"/> stores.</summary>
    /// <param name="key">The definition, which has no body yet.</param>
    /// <param name="source">The body's syntax.</param>
    /// <param name="ordinal">The body's ordinal, at which no body is built.</param>
    /// <returns>The empty body being built.</returns>
    internal MirBody Begin(MirDefKey key, SourceRef source, int ordinal)
    {
        var built = this.Building;
        built.Ordinal = ordinal;
        built.Reset(key, source);
        return built;
    }

    /// <summary>Ends the body being built: stores it in its ordinal's pooled body (<see cref="MirBody.Store"/>) under its definition, the
    /// next of the build-order list.</summary>
    /// <returns>The stored body.</returns>
    internal MirBody End()
    {
        var ordinal = this.Building.Ordinal;
        if (ordinal >= this.pool.Length)
        {
            Array.Resize(ref this.pool, Math.Max(16, (ordinal + 1) * 2));
        }

        if (this.count == this.order.Length)
        {
            Array.Resize(ref this.order, Math.Max(16, this.count * 2));
        }

        this.next = Math.Max(this.next, ordinal + 1);
        var body = this.order[this.count++] = this.pool[ordinal] ??= new();
        body.Store(this.Building);
        this.bodies.Add(body.Key, body);
        return body;
    }

    // A lookup of a key without a body, by the version's row of the demand table.
    private MirBody? Demand(MirDefKey key) => this.Demands switch
    {
        Phase.Open => this.builder!.Demand(key),
        Phase.Judging => this.builder!.Demand(key) is null ? null : throw new InvalidOperationException($"A lookup while judging built {key} (InternalInvariant)."),
        Phase.Sealed => null,
        _ => throw new InvalidOperationException($"The demand table has no row {this.Demands} (InternalInvariant)."),
    };

    /// <summary>The rows of one record class of a version (<see cref="Records"/>), each its code and location.</summary>
    /// <param name="report">The version's report.</param>
    /// <param name="recordClass">The class.</param>
    internal readonly struct RecordRows(MirReport report, MirRecordClass recordClass) : IEnumerable<Problem>
    {
        public Enumerator GetEnumerator() => new(report, recordClass);

        IEnumerator<Problem> IEnumerable<Problem>.GetEnumerator() => this.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

        /// <summary>The enumerator of a class's rows.</summary>
        /// <param name="report">The version's report.</param>
        /// <param name="recordClass">The class.</param>
        internal struct Enumerator(MirReport report, MirRecordClass recordClass) : IEnumerator<Problem>
        {
            private int index = -1;

            public readonly Problem Current => new(report.All[this.index].Code, report.All[this.index].Subject);

            readonly object IEnumerator.Current => this.Current;

            public bool MoveNext()
            {
                while (++this.index < report.All.Length)
                {
                    if (report.All[this.index].Body == (int)recordClass)
                    {
                        return true;
                    }
                }

                return false;
            }

            public void Reset() => this.index = -1;

            public readonly void Dispose()
            {
            }
        }
    }
}
