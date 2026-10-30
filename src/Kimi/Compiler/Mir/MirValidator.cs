// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns structural and Type validation of producer-owned MIR in every configuration.
// Input: MIR bodies, their program and published Type facts. Output: located invariant findings.
// Every IR invariant is checked here; a broken producer contract is InternalInvariant_Kd.
// Must not repair broken IR, recover silently or leave consumers to revalidate it.

#pragma warning disable SA1402 // The validator's rules and findings belong to it.

namespace Kimi.Compiler.Mir;

/// <summary>The rule a validator finding breaks.</summary>
internal enum MirViolation : byte
{
    UnknownKind, // A statement, rvalue, terminator, operand, projection, callee, intrinsic, local or borrow kind without a row.
    NoBlocks,
    Unterminated,
    StatementRange,
    TargetRange, // A successor, case range, check target or MustNotComplete block out of range; a MustNotComplete end with a check or not Unreachable.
    CheckTarget, // A check target on a terminator other than Goto, a Call without a target and Unreachable.
    Unreachable, // A block that neither runtime nor check edges reach from the entry.
    Entry, // An edge into a region, the body or an Inlined scope's locations, elsewhere than its first location, or from it into that block.
    Diverged, // A read, move or borrow, where runtime edges reach, of a Temp whose only writes are diverging Calls' destinations (G131).
    NeverWrite, // A Temp of Type Never written by other than an unadapted Copy or Move of a Never Place, an Opaque value or a diverging Call.
    Local, // Local 0 is not the Return local, an Arg local does not directly follow it, a local has no Type or one stating Unstated (a kept
    // part's mark only, DECISIONS BU-46), or a Result local is no Temp of local 0's Type or is read other than by the whole Move into _0
    // that delivers it (R4-M132).
    Scope, // A scope that its outer scope does not precede, or a source outside the scopes.
    StorageLocal, // StorageLive or StorageDead of a Place other than a Var or Temp local.
    EndBorrowLocal, // EndBorrow of a Place other than a reference local.
    Place, // A root or a projection chain out of range.
    Projection, // A projection its base Type does not admit, or a chain out of order.
    Const, // A constant id out of range.
    Binder, // A PairAcquire of a binder out of range or other than its Place's pair layer, or more cases than PairBinder.CaseBound.
    OperandRange,
    Arity, // An rvalue's or a Call's operands disagree with its kind, its Type or its callee.
    Callee, // A Fn, Requirement or Virtual callee without its function, a Value one without its signature, a misdeclared or diverging intrinsic.
    Discriminant, // A Discriminant of a Place that is not an enum.
    Len, // A Len of a Place that is not a sequence.
    Opaque, // An Opaque site or root out of range, or Opaque sites without an Opaque value.
    Type, // An rvalue, an aggregate's operand, a call's argument or result, or _0 at a Return that disagrees with the Type it goes to; a
    // testRecordValue's value other than a Scalar.
    Operation, // An operator's, a Cast's, a SwitchInt's or an Adapt's operand, a Ref's Type or an Adapt's case operation, that its kind does not admit;
    // a PairAcquire other than the operand of a Use assigned to a local's whole Place with the Uniq or TwoPhaseUniq it marks (A3 E13 (4)), a
    // marked Use of another operand, or a function item constant other than the operand of an Assign's Use, the collector's site (G154).
    StaticSlot, // A staticEnsure's operand other than an Integer constant indexing the body's statics, which MirProgram.StaticInit decodes (F10),
    // or, over the program, its static without a StaticInit body (G191).
    Unensured, // A static root of a Place a location accesses, other than an Opaque site's, that no staticEnsure of the body names (G191).
    Lent, // A Temp a Call takes as an input that reserves (MirCases.Reserves), written by a Ref or an Adapt and by another write (G136).
    SharedPath, // A projected Place written, destroyed in place or acquired exclusively (a Ref Uniq or TwoPhaseUniq, a PairAcquire in its
    // binder's exclusive case, an Adapt's exclusive borrow under a view) past a shared layer (MirTypes.IsSharedLayer) after its last raw
    // Deref; a default template's slot step is the slot itself, and an inline default copy is its template's check (W1 rule 1).
}

/// <summary>A validator finding: its rule and the syntax of its statement, terminator or local, else the body's. The build reports it
/// as <c>InternalInvariant_Kd</c>.</summary>
/// <param name="Rule">The rule.</param>
/// <param name="Subject">The syntax.</param>
internal readonly record struct MirFinding(MirViolation Rule, SourceRef Subject);

/// <summary>The one structural verifier of MIR (plan 14-5): a body's structure, locals, projections against their base Types, operands,
/// rvalues and arities, the Types values flow between (<see cref="MirTypes.Agree"/>, modulo Origins), each Adapt under every
/// Semantics case view (<see cref="MirCases"/>), the blocks that read a Temp only diverging Calls write (G131), a Call's input temps
/// (G136), where each region, the body or a default's copy, is entered (DECISIONS R5-M90), the statics its Places access (G191) and the
/// shared layers a write, an in-place destruction or an exclusive acquisition passes (W1 rule 1); and over a program, the initializer each staticEnsure names
/// (G191), before any analysis reads it. It keeps its scratch, so a warm run allocates nothing.</summary>
/// <remarks>An intrinsic's operands are checked by its row's arity (its declaration's parameters come first, a fact of the table,
/// MirValidatorTest) and staticEnsure's also as its static's slot (DECISIONS R5-M71), a Value callee's against its Type's signature where
/// that Type is a Function Type, else by its declared signature's arity only, and every Call's whole-local inputs by one write of an input
/// that reserves and that a Ref or an Adapt writes (DECISIONS R5-M72). A write, an in-place destruction or an exclusive acquisition
/// passes no shared layer; a Move is A1's (DECISIONS R5-M123, R5-M139).</remarks>
/// <param name="types">The Type context of the bodies, which interns their case Types.</param>
internal sealed partial class MirValidator(TyCtxt types)
{
    private readonly MirCases cases = new(types);
    private readonly List<(MirRvalue Adapt, SourceInfo Source)> adapts = []; // The body's Adapts its case views resolve.
    private readonly MirReach reach = new();
    private readonly HashSet<BoundType> parts = new(ReferenceEqualityComparer.Instance); // StatesUnstated's scratch (the Local row).
    private byte[] writes = []; // Per local, its writes' kinds.

    /// <summary>Validates a body.</summary>
    /// <param name="body">The body.</param>
    /// <param name="findings">The caller's list the findings are added to.</param>
    internal void Validate(MirBody body, List<MirFinding> findings)
    {
        if (MirCases.Count(body) > PairBinder.CaseBound)
        {
            findings.Add(new(MirViolation.Binder, body.Source)); // Binding binds no part of a definition beyond the bound.
            return; // Nor are its views resolved.
        }

        if (this.writes.Length < body.Locals.Length)
        {
            this.writes = new byte[Math.Max(body.Locals.Length, this.writes.Length * 2)];
        }

        Array.Clear(this.writes, 0, body.Locals.Length);
        this.adapts.Clear();
        var checker = new Checker(body, findings, this.Unknown(body), this.Ensured(body), this.adapts, this.writes, this.parts, types);
        if (!checker.Tables())
        {
            return; // A chain or site out of order or range cannot be walked.
        }

        var blocks = body.Blocks;
        var walked = blocks.Length > 0;
        if (!walked)
        {
            checker.Report(MirViolation.NoBlocks);
        }

        for (var block = 0; block < blocks.Length; block++)
        {
            walked &= checker.Block(block);
        }

        checker.Finish();
        checker.Views(this.cases);
        if (walked)
        {
            this.Diverged(body, findings);
            this.Reach(body, findings);
        }

        if (walked && !checker.Unscoped)
        {
            this.Entries(body, findings); // It walks the scope table and each location's scope.
        }
    }

    // The checks of one body: tables first, then each location before MirUses walks it, and the visitor's checks of what it reports; it
    // records each local's writes.
    private partial struct Checker(
        MirBody body,
        List<MirFinding> findings,
        bool[] unknown,
        bool[] ensured,
        List<(MirRvalue Adapt, SourceInfo Source)> adapts,
        byte[] writes,
        HashSet<BoundType> parts,
        TyCtxt types) : IMirUses
    {
        private SourceInfo at;
        private int opaqueValues;
        private int named; // The roots of the Opaque site being walked that are still to come (MirValidator.Statics).
        private bool acquires; // Whether the operands walked are a marked Use's assigned to a local's whole Place, a PairAcquire's one position.
        private bool used; // Whether the operands walked are an Assign's Use's, a function item constant's one position.
        private bool delivering; // Whether they are a Move's into _0's whole Place, a Result local's one read (MirValidator.Tables).

        // Gets a value indicating whether the scope table or a location's scope cannot be walked, a Scope finding; only Entries needs them.
        internal bool Unscoped { get; private set; }

        // An Opaque value exactly when the body has Opaque sites, and the writes of the Call inputs (G136).
        internal void Finish()
        {
            this.at = default;
            if (body.HasOpaque != this.opaqueValues > 0)
            {
                this.Report(MirViolation.Opaque);
            }

            this.Lent();
        }

        internal readonly void Report(MirViolation rule) => findings.Add(new(rule, body.Subject(this.at)));

        // Whether a range of a table lies within it.
        private static bool InRange(int start, int count, int length) => start >= 0 && count >= 0 && start <= length - count;

        // False when the source lies outside the scopes.
        private bool At(in SourceInfo source)
        {
            this.at = source;
            if (source.Scope >= body.Scopes.Length)
            {
                this.Report(MirViolation.Scope);
                return false;
            }

            return true;
        }
    }
}
