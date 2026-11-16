// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns MIR bodies by definition key, the validator's findings, the drop table the analyses find, the builder's layer-walk work and the
// version's MirReport: the records of construction and collection (MirRecordClass; R6-check H3-1) and the analyses' problems.
// Input: built definitions, findings, records and problems. Output: reusable bodies identified by MirDefKey and numbered in order, the body
// a callee runs (Callee), the initializer a staticEnsure names (StaticInit) and a record class's rows (Records, which MirDump, tests and
// benchmarks read; publication reads MirReport.Merged, records included, DECISIONS R5-M185).
// Owns the ordinal space, numbered in build order here (Begin) and nowhere else. In an eager build, whose root range holds the library,
// it has a library prefix (R8-L1 M-B1): the library's bodies are 0 to LibraryCount - 1, the program's follow; in a build whose root range
// is the program's, LibraryCount counts the library's templates and a demanded body takes the next ordinal (PLAN G951 U0; U2 gives
// library bodies fixed ordinals, G950 R-4). Body(ordinal) reads either segment. Bodies is the compilation's own bodies, which a walker
// iterates: both segments while a compilation builds its library's bodies. Its walkers: MirAnalysis.Run, MirOrigins.Findings,
// MirDump.Write, InstanceCollector's roots and templates, and the Benchmark's counts (CompilerPipelineBenchmark,
// CollectionCostMeasurements, PairCaseMeasurements, StaticKeyMeasurements); the program pass reads each body by ordinal from the
// builder's build-order queue (MirBuilder.Name).
// A body is built in its segment's reused body (Begin), one for the library's prefix and one for the program's bodies, so that neither
// segment's construction allocates by the other's bodies, and stored in the next pooled body at End, which registers its key: a stored
// body holds exactly its rows, and a building body's tables and interning grow once to its segment's largest body's.
// Its lookups (this[key], Callee, StaticInit) are the one demand point (PLAN G951): a key without a body is looked up by its phase's row
// of the demand table (Phase): while Open, the builder that opened the version builds the body if the HIR publishes the definition with
// a value outside the root range the builder built eagerly (MirBuilder.Demand); once Sealed, a lookup reads only. Null means only "this
// definition has no body", never "not built yet".
// One key identifies one body; warm rebuilding reuses body storage; Clear resets the report with the bodies.
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
    private MirBody[] pool = [];
    private int count;
    private MirBuilder? builder; // The builder that opened the version, which a lookup while Open demands a body from.

    /// <summary>The demand table (PLAN G951): a row per phase of a program version, each what a lookup of a key without a body does.</summary>
    internal enum Phase
    {
        /// <summary>From <see cref="Open"/>: a lookup builds the body the HIR publishes a value for, validates it and runs its program
        /// pass (<see cref="MirBuilder.Demand"/>).</summary>
        Open,

        /// <summary>Before a builder opens a version and from <see cref="Seal"/> (the end of Compilation's <c>BuildMir</c>): a lookup reads
        /// only.</summary>
        Sealed,
    }

    /// <summary>Gets the bodies a walker visits, in ordinal order: the compilation's own, the library's prefix included while the
    /// compilation builds it.</summary>
    internal ReadOnlySpan<MirBody> Bodies => this.pool.AsSpan(0, this.count);

    /// <summary>Gets the number of the bodies built before the program's: in an eager build the library's, which hold ordinals 0 to the
    /// count less one, the program's bodies following (R8-L1 M-B1); in a build whose root range is the program's, the library's templates
    /// (PLAN G951).</summary>
    internal int LibraryCount { get; private set; }

    /// <summary>Gets or sets the Binding pass the bodies were built from (0: none).</summary>
    internal ulong Version { get; set; }

    /// <summary>Gets the root range the version was opened with: the builder built the bodies of the modules below it eagerly, and builds
    /// any other on demand (<see cref="MirBuilder.Build"/>); one version is built with one range.</summary>
    internal int RootModules { get; private set; }

    /// <summary>Gets the version's row of the demand table.</summary>
    internal Phase Demands { get; private set; } = Phase.Sealed;

    /// <summary>Gets the validator's findings, which Compilation's <c>PublishMirRecords</c> publishes as <c>InternalInvariant_Kd</c> after
    /// the merged problems (DECISIONS R5-M185).</summary>
    internal List<MirFinding> Findings { get; } = [];

    /// <summary>Gets the records and problems of this version: the construction and collection records (<see cref="Report"/>) and the
    /// problems the analyses found, which Compilation publishes from <c>MirReport.Merged</c>, records included, and whose Errors decide
    /// acceptance (DECISIONS R5-M185); <see cref="Clear"/> resets them with the bodies.</summary>
    internal MirReport Problems { get; } = new();

    /// <summary>Gets drop elaboration's verdicts and flags in this version, by body ordinal (A2, DECISIONS R4-M6a); only
    /// <see cref="MirAnalysis"/> resets them, once per version.</summary>
    internal MirDropTable Drops { get; } = new();

    /// <summary>Gets or sets the work of the builder's layer walks in this version (PLAN G647): one per iteration of a walk (a layer's
    /// step and a targeted walk's agreement test together), and a premising as the premised Type's height plus one, the parts premising
    /// visits on a layer spine and fewer than it visits on a wide Type; the walks' scaling rows read it, and <see cref="Clear"/> resets
    /// it.</summary>
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

    internal void Clear()
    {
        this.bodies.Clear();
        this.Problems.Clear();
        this.Findings.Clear();
        this.count = this.LibraryCount = 0;
        this.LayerWalkWork = 0;
    }

    /// <summary>Opens a version for its builder: a lookup builds a missing body on demand until <see cref="Seal"/>.</summary>
    /// <param name="builder">The builder of the version.</param>
    /// <param name="rootModules">The root range the builder builds eagerly.</param>
    internal void Open(MirBuilder builder, int rootModules) => (this.builder, this.RootModules, this.Demands) = (builder, rootModules, Phase.Open);

    /// <summary>Seals the version (the end of Compilation's <c>BuildMir</c>): a lookup reads only.</summary>
    internal void Seal() => this.Demands = Phase.Sealed;

    /// <summary>Ends the library's prefix: the bodies begun so far are the library's (<see cref="LibraryCount"/>).</summary>
    internal void EndLibrary() => this.LibraryCount = this.count;

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
