// G170 C1 prototype: judgments over C's answers, for the Resource transport test (SPEC 8.3.1–8.3.3, 23.1.1; IMPL 21.3.2).
// A judgment combines atom answers by SPEC 8.3.2's table (Error absorbs any result; otherwise a Refuted operand refutes a
// conjunction and a Proven one proves a disjunction). Resource is the fifth outcome (ConstraintProof.Resource): it absorbs as
// Error does, never becomes Unknown or Refuted, and never marks a source Invalid; a polarity established before the limit stands
// (proposal B 3.10: Proven, Refuted or Error with the limit's record). A required judgment that is Unknown or Refuted
// reports Unproven (a Language diagnostic); Error and Resource report nothing more (their own record stands, one per limit and
// scope). A conformance check publishes Valid, Invalid or Resource; a judgment that read a Pending source is not final and is
// judged again after publication (the scheduler's restart), so records never depend on checking order.
#pragma warning disable
#nullable enable

namespace G170C1;

public enum Proof : byte { Proven, Refuted, Unknown, Error, Resource }

public sealed class Checker
{
    private readonly ProgramModel m;
    private readonly Limits limits;

    public Checker(ProgramModel model, Limits? limits = null)
    {
        this.m = model;
        this.limits = limits ?? new();
    }

    /// <summary>Gets the records: Resource (where a limit was hit), Unproven and Invalid (Language), in judgment order.</summary>
    public List<string> Records { get; } = new();

    /// <summary>Judges a required proposition in a scope; a Pending read leaves it for the restart (returns null).</summary>
    public Proof? Require(string scope, Formula f, View? view = null)
    {
        var (proof, final) = this.Judge(f, view ?? View.Ordinary, scope);
        if (!final)
        {
            return null;
        }

        if (proof is Proof.Unknown or Proof.Refuted)
        {
            this.Records.Add($"Unproven({scope})");
        }

        return proof;
    }

    /// <summary>Checks a conformance's obligations (its Contract's clauses with the conforming Type for Self, its own bindings
    /// completed, its own conformance excluded: SPEC 8.3.3) and publishes its state; null while a source it reads is Pending.</summary>
    public Status? Verify(Conformance c, Limits? limits = null)
    {
        var decl = this.m.DeclOf(c.Ref)!;
        var view = new View("check " + c.Type, [], [c.Id]);
        var state = Status.Valid;
        var published = c.Status;
        c.Status = Status.Valid; // its completed bindings are read; its own conformance row is excluded by the view
        this.m.Prepare();
        foreach (var clause in decl.Clauses)
        {
            var beta = new Term?[decl.Arity + 1];
            beta[0] = c.Type;
            for (var i = 0; i < decl.Arity; i++)
            {
                beta[i + 1] = c.Ref.Kids[i];
            }

            var (proof, final) = this.Judge(clause.Subst(this.m.Terms, beta), view, "check " + c.Type + " is " + c.Ref, limits);
            if (!final)
            {
                c.Status = published;
                this.m.Prepare();
                return null;
            }

            if (proof == Proof.Resource)
            {
                state = Status.Resource;
            }
            else if (proof != Proof.Proven && state == Status.Valid)
            {
                state = Status.Invalid;
                this.Records.Add($"Invalid(check {c.Type} is {c.Ref})");
            }
        }

        c.Status = state;
        this.m.Prepare(); // publication: the next requests read the new state
        return state;
    }

    private (Proof Proof, bool Final) Judge(Formula f, View view, string scope, Limits? limits = null)
    {
        switch (f.Kind)
        {
            case FK.And or FK.Or:
                {
                    var (l, lf) = this.Judge(f.L!, view, scope, limits);
                    var (r, rf) = this.Judge(f.R!, view, scope, limits);
                    return (Combine(f.Kind, l, r), lf && rf);
                }

            case FK.Not when !f.IsAtom:
                {
                    var (p, final) = this.Judge(f.L!, view, scope, limits);
                    return (p == Proof.Proven ? Proof.Refuted : p == Proof.Refuted ? Proof.Proven : p, final);
                }
        }

        var a = new Solver(this.m).Supply(f, view, limits ?? this.limits);
        if (a.Outcome == Outcome.Resource && !a.Reason.StartsWith("source state", StringComparison.Ordinal))
        {
            this.Records.Add($"Resource({scope}: {a.Reason})"); // the limit was hit here: one record for the scope
        }

        // B 3.10 (D9), read literally: at a limit the Proven (and Error) the identities obtained give stand; a Refuted or Unknown that
        // depends on the limited analysis is Resource (more analysis could turn an established Refuted into Error).
        var proof = a.Positive && a.Negative ? Proof.Error : a.Positive ? Proof.Proven : a.Outcome == Outcome.Resource ? Proof.Resource : a.Negative ? Proof.Refuted : Proof.Unknown;
        return (proof, a.Outcome != Outcome.Provisional);
    }

    private static Proof Combine(FK kind, Proof l, Proof r)
    {
        if (l == Proof.Error || r == Proof.Error)
        {
            return Proof.Error;
        }

        if (l == Proof.Resource || r == Proof.Resource)
        {
            return Proof.Resource; // absorbed as Error is: no dependent Language diagnostic
        }

        return kind == FK.And
            ? (l == Proof.Refuted || r == Proof.Refuted ? Proof.Refuted : l == Proof.Proven && r == Proof.Proven ? Proof.Proven : Proof.Unknown)
            : (l == Proof.Proven || r == Proof.Proven ? Proof.Proven : l == Proof.Refuted && r == Proof.Refuted ? Proof.Refuted : Proof.Unknown);
    }
}
