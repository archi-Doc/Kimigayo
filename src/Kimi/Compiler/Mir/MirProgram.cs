// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns MIR bodies by definition key, the validator's findings, the drop table the analyses find, the builder's layer-walk work and the
// version's MirReport: the records of construction and collection (MirRecordClass; R6-check H3-1) and the analyses' problems.
// Input: built definitions, findings, records and problems. Output: reusable bodies identified by MirDefKey and numbered in order, the body
// a callee runs (Callee), the initializer a staticEnsure names (StaticInit) and a record class's rows (Records, which MirDump, tests and
// benchmarks read; publication reads MirReport.Merged, records included, DECISIONS R5-M185).
// Owns the ordinal space, numbered in build order here (Begin) and nowhere else. In an eager build, whose root range holds the library,
// it has a library prefix (R8-L1 M-B1): the library's bodies are 0 to LibraryCount - 1, the program's follow; in a build whose root range
// is the program's, Compilation's since PLAN G951 U2, LibraryCount counts the library's templates and a demanded body takes the next
// ordinal (U0; U2-K gives library bodies fixed ordinals, G950 R-4). Body(ordinal) reads either segment. Bodies is the compilation's own bodies, which a walker
// of the whole program iterates: both segments while a compilation builds its library's bodies. Its walkers: MirAnalysis's step 4,
// MirOrigins.Findings, MirDump.Write, InstanceCollector's roots (with A3b) and the Benchmark's counts and queries
// (CompilerPipelineBenchmark, CollectionCostMeasurements, PairCaseMeasurements, StaticKeyMeasurements). LibraryBodies and ProgramBodies
// are the segments, each the span a walker of that segment takes as its input: MirAnalysis's library step and steps 1 and 2,
// InstanceCollector's templates and StaticKeyMeasurements' local facts. The program pass reads each body by ordinal from the builder's
// build-order queue (MirBuilder.Name), the library segment's drained before EndLibrary. No walker tests an ordinal against LibraryCount
// itself; an owner keeping per-body entries in a store per segment reads them by the ordinal itself through one split rule, the
// library's store below the library's count (R8-L1 M-B1; the Back's C-378 (1)(a)): MirDropTable.Of (its library store's count),
// MirEffects.Base and InstanceCollector.TemplatesOf (LibraryCount).
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
// both segments, ClearProgram the report and the program's.
// Must not re-bind definitions or maintain a second body map in a consumer; a reader by ordinal reads through Body, never offsets an
// ordinal or computes one from LibraryCount.

using System.Collections;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

/// <summary>The MIR bodies of a compilation by <see cref="MirDefKey"/>, with the records their construction, validation and collection made.
/// <see cref="Clear"/> returns every body to the pool and <see cref="End"/> reuses them in order, so a warm rebuild of the same
/// definitions allocates nothing.</summary>
internal sealed class MirProgram
{
    private readonly Dictionary<MirDefKey, MirBody> bodies = new();
    private readonly MirBody[] building = [new(), new()]; // The body each segment is built in: the library's prefix, then the program's.
    private readonly MirReport.Kept libraryRecords = new(); // The library segment's construction records, replayed first in a kept version.
    private MirBody[] pool = [];
    private int count;
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

    /// <summary>Gets the bodies a walker of the whole program visits, in ordinal order: the compilation's own, the library's prefix included
    /// while the compilation builds it.</summary>
    internal ReadOnlySpan<MirBody> Bodies => this.pool.AsSpan(0, this.count);

    /// <summary>Gets the library segment's bodies, ordinals 0 to <see cref="LibraryCount"/> less one.</summary>
    internal ReadOnlySpan<MirBody> LibraryBodies => this.pool.AsSpan(0, this.LibraryCount);

    /// <summary>Gets the program segment's bodies, the ordinals from <see cref="LibraryCount"/> on.</summary>
    internal ReadOnlySpan<MirBody> ProgramBodies => this.pool.AsSpan(this.LibraryCount, this.count - this.LibraryCount);

    /// <summary>Gets the number of the bodies built before the program's: in an eager build the library's, which hold ordinals 0 to the
    /// count less one, the program's bodies following (R8-L1 M-B1); in a build whose root range is the program's, the library's templates
    /// (PLAN G951).</summary>
    internal int LibraryCount { get; private set; }

    /// <summary>Gets the library version the library segment was built for (0: none), the stamp a component keeping a library segment's
    /// facts compares (PLAN G950): the Binding pass until G949's library publication version replaces it (G950-K).</summary>
    internal ulong LibraryVersion { get; private set; }

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

    /// <summary>Gets drop elaboration's verdicts and flags in this version, by body ordinal (A2, DECISIONS R4-M6a); only
    /// <see cref="MirAnalysis"/> resets them: the library segment's once per library version, the program's once per version.</summary>
    internal MirDropTable Drops { get; } = new();

    /// <summary>Gets or sets the work of the builder's layer walks in this version (PLAN G647): one per iteration of a walk (a layer's
    /// step and a targeted walk's agreement test together), and a premising as the premised Type's height plus one, the parts premising
    /// visits on a layer spine and fewer than it visits on a wide Type; the walks' scaling rows read it, and <see cref="Clear"/> and
    /// <see cref="ClearProgram"/> reset it.</summary>
    internal long LayerWalkWork { get; set; }

    /// <summary>Gets the body the current segment is built in: the library's until its prefix holds bodies (<see cref="EndLibrary"/>), then
    /// the program's.</summary>
    private MirBody Building => this.building[this.LibraryCount > 0 ? 1 : 0];

    /// <summary>Gets the body of a definition, the demand point: a key without a body is looked up by the version's row of the demand
    /// table (<see cref="Demands"/>).</summary>
    /// <param name="key">The definition.</param>
    /// <returns>The body, or null for a definition without one.</returns>
    internal MirBody? this[MirDefKey key] => this.bodies.TryGetValue(key, out var body) ? body : this.Demand(key);

    /// <summary>Gets the body of an ordinal, the library's or the program's, the one read by ordinal.</summary>
    /// <param name="ordinal">The body's ordinal (<see cref="MirBody.Ordinal"/>).</param>
    /// <returns>The body.</returns>
    internal MirBody Body(int ordinal) => this.Bodies[ordinal];

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

    /// <summary>Returns both segments' bodies to the pool and clears the report, the findings and the work, for a new library version.</summary>
    internal void Clear()
    {
        this.bodies.Clear();
        this.Problems.Clear();
        this.Findings.Clear();
        this.count = this.LibraryCount = this.libraryFindings = 0;
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

    /// <summary>Ends the library segment, built and validated for a library version: the bodies begun so far are the library's
    /// (<see cref="LibraryCount"/>), the findings so far its findings and the records so far its records, which
    /// <see cref="ClearProgram"/> keeps.</summary>
    /// <param name="libraryVersion">The library version (<see cref="LibraryVersion"/>).</param>
    internal void EndLibrary(ulong libraryVersion)
    {
        (this.LibraryCount, this.libraryFindings, this.LibraryVersion) = (this.count, this.Findings.Count, libraryVersion);
        this.Problems.Keep(0, this.libraryRecords);
    }

    /// <summary>Returns the program segment's bodies to the pool, removing their keys, for a new program version over the kept library
    /// segment: the findings keep the library's prefix, the report starts with the library's records again (MirReport.Replay) and the
    /// work counts the new version's.</summary>
    internal void ClearProgram()
    {
        foreach (var body in this.ProgramBodies)
        {
            this.bodies.Remove(body.Key);
        }

        this.count = this.LibraryCount;
        this.Findings.RemoveRange(this.libraryFindings, this.Findings.Count - this.libraryFindings);
        this.Problems.Clear();
        this.Problems.Replay(this.libraryRecords);
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

    /// <summary>Begins the body of a definition, of the next ordinal, in its segment's body being built, which <see cref="End"/> stores.</summary>
    /// <param name="key">The definition, which has no body yet.</param>
    /// <param name="source">The body's syntax.</param>
    /// <returns>The empty body being built.</returns>
    internal MirBody Begin(MirDefKey key, SourceRef source)
    {
        var built = this.Building;
        built.Ordinal = this.count;
        built.Reset(key, source);
        return built;
    }

    /// <summary>Ends the body being built: stores it in the next pooled body (<see cref="MirBody.Store"/>) under its definition.</summary>
    /// <returns>The stored body.</returns>
    internal MirBody End()
    {
        if (this.count == this.pool.Length)
        {
            Array.Resize(ref this.pool, Math.Max(16, this.count * 2));
        }

        var body = this.pool[this.count++] ??= new();
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
