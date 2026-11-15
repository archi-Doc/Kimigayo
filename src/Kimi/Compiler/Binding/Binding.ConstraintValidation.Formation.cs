// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// A Type's formation in a scope (SPEC 8.3, 15.6.1; DECISIONS BU-19, BU-55, R4-M163), a part of constraint validation.
// - Responsibility: two walks over one per-part step. The step (BeginTypeFormation, EndTypeFormation) judges a part's declaration
//   context, its Origin clauses and a constructed part's clauses with every enclosing container's under its arguments. The per-use
//   walk (CheckTypeConstraints) judges an instantiated Type and enters every component; the declaration walk
//   (CheckDeclaredTypeConstraints) judges a callee's declared signature in its own scope (FormSignature; CheckSignatureTypeConstraints
//   keeps its Proven as the declaration query SignatureVerdict) and enters no Function Type's components at any depth.
// - Inputs: a Type and the scope whose premises apply; the declaration contexts, Origin declarations and constraint clauses bound so far.
// - Outputs: the proof, combined as P < U < L < R < E, with the cause it rests on read into the enclosing proof scope; in the per-use
//   walk, also the clause fact at the part's verdict, handed on as a clause list hands its own (LeaveClause; PLAN G354 (2)).
// - Invariants: the step runs in the same order in both walks: the declaration context, the Origin clauses, the walk's components, the
//   clauses; each walk then closes the part's proof scope; each distinct part with components is decided once per query, and each walk
//   call begins its own query, so the two walks never share an entry; a per-use walk call made while a per-use walk of the same scope
//   runs (a conformance's instance formation inside a part's clauses) first reads a part that walk has decided on settled verdicts
//   alone (no provisional read, ProvisionalReads, and no part read that was not), which holds at the call's deeper obligation, and
//   stores nothing in that walk's query, so the walk is linear in the distinct parts however deep its instances nest, and an answer
//   formed while an obligation or a capability request is pending is never read past it. A cached part and a computed one leave the
//   same facts.
// - Prohibitions: no record and no failure is written here; a nested signature's result is judged where it is written
//   (BindFunctionType, R4-M162) and its inputs at its value calls (R4-M137), never by the declaration walk; the declaration walk hands
//   no clause fact on while PLAN G375's sigref1 stays open.
public sealed partial class Binding
{
    // CheckTypeConstraints' and CheckDeclaredTypeConstraints' proofs of a Type's parts in one query, each with the cause it rests on
    // (DECISIONS BU-55), the clause fact it hands on (G354 (2); none in the declaration walk) and whether its computation read only
    // settled verdicts (a nested per-use call's condition; the declaration walk's entries are read by their own walk only).
    private readonly TypeMemo<BoundType, (ConstraintProof Proof, ICheckCause? Cause, CallableUnknown? Fact, ClauseFact? Clause, bool Settled)> formedParts = new();

    // The walks the SignatureVerdict row ran in this pass.
    private int signatureWalks;

    /// <summary>Gets the walks the SignatureVerdict row ran in the latest pass, which a scaling check reads.</summary>
    internal int SignatureWalkWork => this.signatureWalks;

    // The innermost per-use walk running: its scope and query (no scope outside every per-use walk).
    private (BindingScope? Scope, uint Query) openFormation;

    // The reads of a part not decided on settled verdicts: a part whose computation read one rests on a provisional verdict through it.
    private int unsettledReads;

    // The per-use walk over an instantiated Type. A part's proof and the cause it rests on depend on the part alone in one query (its
    // scope fixed), so each distinct part is proven once (FormedPart). A call made while a per-use walk of the same scope runs (a
    // part's clause proving a conformance, whose evidence rests on the instance's formation: InstanceFormation) reads a part that walk
    // has decided, so a nested instance is not formed again per enclosing level. It runs inside a clause's proof, at a deeper obligation
    // than the walk's, so it reads only a part decided on settled verdicts (an assumption or a re-entry read holds only at the depth it
    // was read at), and it stores nothing in that walk's query: what it computes there may rest on provisional verdicts and is decided in
    // a query of its own, which ends with it.
    private ConstraintProof CheckTypeConstraints(BoundType type, BindingScope scope)
    {
        if (ReferenceEquals(this.openFormation.Scope, scope) && this.formedParts.TryGet(type, this.openFormation.Query, out var decided) && decided.Settled)
        {
            this.BeginProof();
            return this.ReadPart(decided);
        }

        var outer = this.openFormation;
        this.openFormation = (scope, this.formedParts.Begin(this.storageVersion));
        try
        {
            return this.CheckTypeConstraints(type, scope, this.openFormation.Query);
        }
        finally
        {
            this.openFormation = outer;
        }
    }

    private ConstraintProof CheckTypeConstraints(BoundType type, BindingScope scope, uint query)
    {
        if (this.BeginTypeFormation(type, scope) is not { } proof)
        {
            return ConstraintProof.Error;
        }

        // A Function Type's components are its inputs and its result.
        for (var i = 0; i < type.Components.Count; i++)
        {
            proof = CombineProof(proof, this.FormedPart(type.Components[i], scope, query), true);
        }

        // G354 (2): a Type's formation is one judgment, which hands its clause lists' fact at its verdict on to its enclosing scope.
        proof = this.EndTypeFormation(type, scope, proof);
        var clause = this.ProofClause(proof);
        var verdict = this.EndProof(proof);
        this.LeaveClause(clause, proof);
        return verdict;
    }

    // SPEC 15.6.1, 15.3.4 (DECISIONS R4-M163): the declaration walk, over a Type written in a callee's own signature, in that callee's
    // scope. It enters no Function Type's components at any depth: a nested signature's result is judged where it is written
    // (BindFunctionType, R4-M162), and its inputs are that signature's premises, proven at its value calls (R4-M137).
    private ConstraintProof CheckDeclaredTypeConstraints(BoundType type, BindingScope scope)
        => this.CheckDeclaredTypeConstraints(type, scope, this.formedParts.Begin(this.storageVersion));

    private ConstraintProof CheckDeclaredTypeConstraints(BoundType type, BindingScope scope, uint query)
    {
        if (this.BeginTypeFormation(type, scope) is not { } proof)
        {
            return ConstraintProof.Error;
        }

        for (var i = 0; type.Kind != BoundTypeKind.Function && i < type.Components.Count; i++)
        {
            proof = CombineProof(proof, this.DeclaredPart(type.Components[i], scope, query), true);
        }

        // It hands no clause fact on while PLAN G375's sigref1 stays open (G354 (2)): a failed signature is computed again at each use.
        return this.EndProof(this.EndTypeFormation(type, scope, proof));
    }

    // The step's opening: a part whose declaration context failed is Error with no component entered (null); otherwise its proof scope
    // opens with its unresolved declaration context and its Origin clauses.
    private ConstraintProof? BeginTypeFormation(BoundType type, BindingScope scope)
    {
        if (type.Symbol is { } symbol && this.InvalidDeclarationContext(symbol.Declaration))
        {
            return null;
        }

        this.BeginProof();
        var proof = type.Symbol is { } declaration && UnresolvedTypeDeclarationContext(declaration.Declaration) ?
            ConstraintProof.Unknown : ConstraintProof.Proven;
        return CombineProof(proof, this.CheckTypeOriginRelations(type, scope), true);
    }

    // The step's closing judgment, after the walk's components: a constructed part's clauses and every enclosing container's under its
    // arguments; the walk then closes the part's proof scope.
    private ConstraintProof EndTypeFormation(BoundType type, BindingScope scope, ConstraintProof proof)
    {
        if (type is { Kind: BoundTypeKind.Constructed or BoundTypeKind.Dictionary, Symbol.Declaration: DeclarationContainerKoto container } &&
            (type.Kind != BoundTypeKind.Dictionary || type.Components.Count != 0))
        {
            for (var parent = container; parent is not null && !parent.IsRoot; parent = parent.Parent as DeclarationContainerKoto)
            {
                proof = CombineProof(proof, this.CheckConstraints(parent.ConstraintNodes, new CallSubstitution(container, (BoundType[])type.Components, origins: (BoundOrigin[])type.OriginArguments), scope), true);
            }
        }

        return proof;
    }

    // A component's formation (CheckTypeConstraints), each distinct one with components proven once per query: its proof and the cause it
    // rests on are read again into its own proof scope (ReadVerdict) where it recurs, as EndProof reads a recomputation; a leaf is cheaper
    // to prove again than to record.
    private ConstraintProof FormedPart(BoundType part, BindingScope scope, uint query)
    {
        if (part.Components.Count == 0)
        {
            return this.CheckTypeConstraints(part, scope, query);
        }

        this.BeginProof();
        if (!this.formedParts.TryGet(part, query, out var known))
        {
            var reads = this.ProvisionalReads + this.unsettledReads;
            var proof = this.CheckTypeConstraints(part, scope, query);
            known = (proof, this.ProofCause(proof), this.ProofFact(proof), this.ProofClause(proof), this.ProvisionalReads + this.unsettledReads == reads);
            this.formedParts.Set(part, query, known);
        }

        return this.ReadPart(known);
    }

    // A decided part read into the proof scope its reader opened: a cached formation and a computed one leave the same facts.
    private ConstraintProof ReadPart(in (ConstraintProof Proof, ICheckCause? Cause, CallableUnknown? Fact, ClauseFact? Clause, bool Settled) known)
    {
        this.unsettledReads += known.Settled ? 0 : 1;
        var verdict = this.EndProof(this.ReadVerdict(known.Proof, known.Cause, known.Fact));
        this.LeaveClause(known.Clause, known.Proof);
        return verdict;
    }

    // A component's formation in the declaration walk (CheckDeclaredTypeConstraints), decided once per query as FormedPart's is.
    private ConstraintProof DeclaredPart(BoundType part, BindingScope scope, uint query)
    {
        if (part.Components.Count == 0)
        {
            return this.CheckDeclaredTypeConstraints(part, scope, query);
        }

        this.BeginProof();
        if (!this.formedParts.TryGet(part, query, out var known))
        {
            var proof = this.CheckDeclaredTypeConstraints(part, scope, query);
            known = (proof, this.ProofCause(proof), this.ProofFact(proof), null, false);
            this.formedParts.Set(part, query, known);
        }

        return this.EndProof(this.ReadVerdict(known.Proof, known.Cause, known.Fact));
    }

    // A callee's declared signature, formed in its own scope by the declaration walk, at a call's candidate evaluation and a function
    // reference. Its Proven is a declaration query (DeclQueryKind.SignatureVerdict; PLAN G375), which leaves nothing to its reader. Any
    // other answer depends on who asks (a cause written to the channel), so each use computes it in its own scope (PLAN G375, until R7-C2).
    // A requirement's call is proven under its reference, not as its declaration, so that demand stays outside the query until R7-C2.
    private ConstraintProof CheckSignatureTypeConstraints(FunctionKoto function)
    {
        if (this.activeRequirementContract is not null)
        {
            return this.FormSignature(function);
        }

        var symbol = function.BoundSymbol!;
        switch (this.BeginQuery(symbol, DeclQueryKind.SignatureVerdict))
        {
            case DeclState.Done:
                this.BeginProof(); // A kept answer and a computed one leave the same facts: outside every scope, the frame drops untaken ones.
                return this.EndProof(ConstraintProof.Proven);
            case DeclState.Running:
                return ConstraintProof.Unknown; // SPEC 8.3.3: a revisited obligation supplies no evidence.
        }

        this.signatureWalks++;
        var proof = this.FormSignature(function);
        if (proof == ConstraintProof.Proven && this.contractHeadersReady)
        {
            this.CompleteQuery(symbol, DeclQueryKind.SignatureVerdict);
        }
        else
        {
            this.AbandonQuery(symbol, DeclQueryKind.SignatureVerdict);
        }

        return proof;
    }

    private ConstraintProof FormSignature(FunctionKoto function)
    {
        var scope = this.scopes[function];
        var proof = function.BoundSymbol?.Type is { } result ? this.CheckDeclaredTypeConstraints(result, scope) : ConstraintProof.Unknown;
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            proof = CombineProof(
                proof,
                function.Parameters[i].Type.BoundType is { } parameter ? this.CheckDeclaredTypeConstraints(parameter, scope) : ConstraintProof.Unknown,
                true);
        }

        return proof;
    }
}
