// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns MIR bodies by definition key, the validator's findings, the drop table the analyses find, and the version's MirReport: the records
// of construction and collection (MirRecordClass; R6-check H3-1) and the analyses' problems.
// Input: built definitions, findings, records and problems. Output: reusable bodies identified by MirDefKey and numbered in order, the body
// a callee runs (Callee), the initializer a staticEnsure names (StaticInit) and a record class's rows (Records), their one path.
// One key identifies one body; warm rebuilding reuses body storage; Clear resets the report with the bodies.
// Must not re-bind definitions or maintain a second body map in a consumer.

using System.Collections;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Mir;

/// <summary>The MIR bodies of a compilation by <see cref="MirDefKey"/>, with the records their construction, validation and collection made.
/// <see cref="Clear"/> returns every body to the pool and <see cref="Begin"/> reuses them in order, so a warm rebuild of the same
/// definitions allocates nothing.</summary>
internal sealed class MirProgram
{
    private readonly Dictionary<MirDefKey, MirBody> bodies = new();
    private MirBody[] pool = [];
    private int count;

    /// <summary>Gets the bodies in the order they were begun.</summary>
    internal ReadOnlySpan<MirBody> Bodies => this.pool.AsSpan(0, this.count);

    /// <summary>Gets or sets the Binding pass the bodies were built from (0: none).</summary>
    internal ulong Version { get; set; }

    /// <summary>Gets the validator's findings, which the <c>mir</c> pipeline publishes as <c>InternalInvariant_Kd</c>.</summary>
    internal List<MirFinding> Findings { get; } = [];

    /// <summary>Gets the records and problems of this version: the construction and collection records (<see cref="Report"/>), which
    /// Compilation publishes through <see cref="Records"/>, and the problems the analyses found (R4: unpublished, compared by the difference
    /// ledger); <see cref="Clear"/> resets them with the bodies.</summary>
    internal MirReport Problems { get; } = new();

    /// <summary>Gets drop elaboration's verdicts and flags in this version, by body ordinal (A2, DECISIONS R4-M6a); only
    /// <see cref="MirAnalysis"/> resets them, once per version.</summary>
    internal MirDropTable Drops { get; } = new();

    internal MirBody? this[MirDefKey key] => this.bodies.TryGetValue(key, out var body) ? body : null;

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
        this.count = 0;
    }

    /// <summary>Records a construction or collection record at its location, a ControlFlow problem of every view, once per class, code and
    /// location however many bodies or items meet it (MirReport's key).</summary>
    /// <param name="site">The syntax.</param>
    /// <param name="code">For construction an Unsupported form, the default expansion limit or a broken HIR contract (DECISIONS R4-M61); for
    /// collection a code of the checked set's records (InstanceCollector).</param>
    /// <param name="recordClass">The record's class, its body value.</param>
    internal void Report(SourceRef site, DiagnosticCode code = DiagnosticCode.Unsupported_Kd, MirRecordClass recordClass = MirRecordClass.Construction)
        => this.Problems.Add(new(code, DiagnosticRequirement.ControlFlow, site, (int)recordClass, ulong.MaxValue));

    /// <summary>Gets the rows of a record class in the order they were first recorded, the one path to them: the construction records, an
    /// Unsupported form's <c>Unsupported_Kd</c> (plan 6.2), the default expansion limit's <c>DefaultExpansionLimit_Kd</c> (Resource) and a
    /// broken HIR contract's <c>InternalInvariant_Kd</c>, or the collection records of the checked set; enumerating allocates nothing.</summary>
    /// <param name="recordClass">The class.</param>
    /// <returns>The rows.</returns>
    internal RecordRows Records(MirRecordClass recordClass) => new(this.Problems, recordClass);

    /// <summary>Begins the body of a definition with the next pooled body.</summary>
    /// <param name="key">The definition, which has no body yet.</param>
    /// <param name="source">The body's syntax.</param>
    /// <returns>The empty body.</returns>
    internal MirBody Begin(MirDefKey key, SourceRef source)
    {
        if (this.count == this.pool.Length)
        {
            Array.Resize(ref this.pool, Math.Max(16, this.count * 2));
        }

        var body = this.pool[this.count] ??= new();
        body.Ordinal = this.count++;
        body.Reset(key, source);
        this.bodies.Add(key, body);
        return body;
    }

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
