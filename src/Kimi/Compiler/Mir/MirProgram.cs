// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns MIR bodies by definition key, the records of construction and validation, and the problems and drop table the analyses find.
// Input: built definitions, findings and problems. Output: reusable bodies identified by MirDefKey and numbered in order, the body a callee
// runs (Callee) and the initializer a staticEnsure names (StaticInit).
// One key identifies one body; warm rebuilding reuses body storage.
// Must not re-bind definitions or maintain a second body map in a consumer.

#pragma warning disable SA1402 // The key belongs to the program.

namespace Kimi.Compiler.Mir;

/// <summary>The key of a MIR body (R3 decision 6): its definition's symbol (a Closure's own, an accessor's the function its calls
/// target), which body of it and the index of a default template (0 otherwise), so that a callee's symbol and its declaration name its
/// body. A module's top-level statements (SPEC 22.4.1) have no symbol; their index is the module's ordinal. R7's declaration table
/// replaces the symbol.</summary>
/// <param name="Decl">The definition, or null for a module's top-level statements.</param>
/// <param name="Kind">The body of the definition.</param>
/// <param name="Index">The index of a default template, or the module of top-level statements.</param>
internal readonly record struct MirDefKey(BindingSymbol? Decl, MirBodyKind Kind, int Index = 0);

/// <summary>The MIR bodies of a compilation by <see cref="MirDefKey"/>, with the records their construction and validation made.
/// <see cref="Clear"/> returns every body to the pool and <see cref="Begin"/> reuses them in order, so a warm rebuild of the same
/// definitions allocates nothing.</summary>
internal sealed class MirProgram
{
    private readonly Dictionary<MirDefKey, MirBody> bodies = new();
    private readonly HashSet<Problem> reported = new();
    private MirBody[] pool = [];
    private int count;

    /// <summary>Gets the bodies in the order they were begun.</summary>
    internal ReadOnlySpan<MirBody> Bodies => this.pool.AsSpan(0, this.count);

    /// <summary>Gets or sets the Binding pass the bodies were built from (0: none).</summary>
    internal ulong Version { get; set; }

    /// <summary>Gets the located records of construction, one per code and location, which the <c>mir</c> pipeline publishes: an
    /// Unsupported form's <c>Unsupported_Kd</c> (plan 6.2), the default expansion limit's <c>DefaultExpansionLimit_Kd</c> (Resource), and
    /// a broken HIR contract's <c>InternalInvariant_Kd</c> (a node the publisher found without its Op's facts; DECISIONS R4-M61).</summary>
    internal List<Problem> Records { get; } = [];

    /// <summary>Gets the validator's findings, which the <c>mir</c> pipeline publishes as <c>InternalInvariant_Kd</c>.</summary>
    internal List<MirFinding> Findings { get; } = [];

    /// <summary>Gets the problems the analyses found in this version (R4: unpublished, compared by the difference ledger); only
    /// <see cref="MirAnalysis"/> resets them, once per version.</summary>
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
        this.reported.Clear();
        this.Records.Clear();
        this.Findings.Clear();
        this.count = 0;
    }

    /// <summary>Records a problem of construction at its location, once however many bodies meet it.</summary>
    /// <param name="site">The syntax.</param>
    /// <param name="code">An Unsupported form, the default expansion limit, or a broken HIR contract.</param>
    internal void Report(SourceRef site, DiagnosticCode code = DiagnosticCode.Unsupported_Kd)
    {
        if (this.reported.Add(new(code, site)))
        {
            this.Records.Add(new(code, site));
        }
    }

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
}
