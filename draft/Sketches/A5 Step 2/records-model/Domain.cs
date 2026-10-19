// The fixture producer publishes immutable identity domains BEFORE recording.
// Its declared boundary/return-carried facts are assumptions, not a Binding/MIR bridge proof.
namespace RecordsModel;

sealed record Obligation(Id<PointName> Boundary, Id<AccessName> Access, Outcome Outcome, bool ReturnFit);

sealed class Domain(int version)
{
    private readonly Dictionary<Id<ObligationName>, Obligation> obligations = [];
    public int Version { get; } = version;
    public Id<T> Id<T>(T name) where T : struct, Enum => new(Version, name);
    public Body Body(BodyName name = BodyName.Zero) => new(Id(name));
    public CaseMeaning[] Cases(Body body) => body.Id?.Name switch
    {
        BodyName.Zero => [CaseMeaning.Alpha, CaseMeaning.Beta, CaseMeaning.Gamma, CaseMeaning.Delta],
        BodyName.Other => [CaseMeaning.Red, CaseMeaning.Blue],
        _ => throw new ProtocolFault(Fault.Domain),
    };

    public void Declare(ObligationName name, PointName boundary, AccessName access, Outcome outcome, bool returnFit)
    {
        var id = Id(name);
        var value = new Obligation(Id(boundary), Id(access), outcome, returnFit);
        if (obligations.TryGetValue(id, out var previous) && previous != value)
            throw new ProtocolFault(Fault.Position);
        obligations[id] = value;
    }

    public Obligation Obligation(Id<ObligationName> id)
    {
        Check(id);
        return obligations.TryGetValue(id, out var result) ? result : throw new ProtocolFault(Fault.Domain);
    }

    public static Domain Fixture(int version = 1)
    {
        var domain = new Domain(version);
        domain.Declare(ObligationName.ReturnA, PointName.Return, AccessName.First, Outcome.Refuted, true);
        domain.Declare(ObligationName.ReturnB, PointName.Return, AccessName.First, Outcome.Refuted, true);
        domain.Declare(ObligationName.SecondBoundary, PointName.Return2, AccessName.First, Outcome.Refuted, true);
        domain.Declare(ObligationName.GuardA, PointName.Cleanup, AccessName.First, Outcome.Refuted, false);
        domain.Declare(ObligationName.GuardB, PointName.Cleanup, AccessName.Second, Outcome.Refuted, false);
        domain.Declare(ObligationName.Independent, PointName.Earlier, AccessName.First, Outcome.Refuted, false);
        domain.Declare(ObligationName.Unknown, PointName.Cleanup, AccessName.First, Outcome.Unknown, false);
        domain.Declare(ObligationName.Proof, PointName.Cleanup, AccessName.First, Outcome.Proof, false);
        return domain;
    }

    public void Check<T>(Id<T> id) where T : struct, Enum
    {
        if (id.Version != Version || !Enum.IsDefined(id.Name)) throw new ProtocolFault(Fault.Domain);
    }

    public void Check(Body body)
    {
        if (body.Id is { } id) Check(id);
    }

    public void Check(Finite source)
    {
        switch (source)
        {
            case Finite.None: break;
            case Finite.Borrow borrow: Check(borrow.Id); break;
            case Finite.Guard guard: Check(guard.Id); break;
            default: throw new ProtocolFault(Fault.Domain);
        }
    }

    public void Check(EffectKey key)
    {
        switch (key)
        {
            case EffectKey.Unknown: break;
            case EffectKey.Static field when Enum.IsDefined(field.Access): Check(field.Field); break;
            default: throw new ProtocolFault(Fault.Domain);
        }
    }

    public void Check(IssueRef reference)
    {
        Check(reference.Body);
        Check(reference.Subject);
        _ = Catalog.Of(reference.Code);
        if (reference.Issue is not Issue.Source && reference.Body != Body()) throw new ProtocolFault(Fault.Domain);
        switch (reference.Issue)
        {
            case Issue.Source source when source.Condition >= 0 && reference.Code is Code.Source or Code.Unsupported: break;
            case Issue.Origin origin:
                if (reference.Body.Id is null || reference.Code is not (Code.OriginRefuted or Code.OriginUnknown))
                    throw new ProtocolFault(Fault.Pair);
                var obligation = Obligation(origin.Obligation);
                Check(origin.Chain);
                if (obligation.Outcome == Outcome.Proof ||
                    (obligation.Outcome == Outcome.Refuted) != (reference.Code == Code.OriginRefuted))
                    throw new ProtocolFault(Fault.Outcome);
                break;
            case Issue.Conflict conflict when Enum.IsDefined(conflict.Kind):
                if (reference.Body.Id is null) throw new ProtocolFault(Fault.Pair);
                Check(conflict.At); Check(conflict.Access); Check(conflict.Cause);
                var code = conflict.Kind switch
                {
                    ConflictKind.DefaultBorrow => Code.DefaultBorrow,
                    ConflictKind.DefaultAccess => Code.DefaultAccess,
                    _ => Code.Loan,
                };
                if (reference.Code != code && reference.Code != Code.Unsupported) throw new ProtocolFault(Fault.Pair);
                break;
            case Issue.Effect effect:
                if (reference.Body.Id is null || reference.Code is not (Code.Loan or Code.Unsupported))
                    throw new ProtocolFault(Fault.Pair);
                Check(effect.At); Check(effect.Borrow); Check(effect.Key);
                break;
            default: throw new ProtocolFault(Fault.Domain);
        }
    }

    public void Check(Reason reason)
    {
        if (reason is Reason.Empty) return;
        if (reason is not Reason.Origin origin || !Enum.IsDefined(origin.Relation)) throw new ProtocolFault(Fault.Domain);
        Check(origin.Longer); Check(origin.Shorter);
        switch (origin.Source)
        {
            case OriginSource.Declared or OriginSource.WellFormed: break;
            case OriginSource.Fit { Destination: FitDestination.OriginOnly }: break;
            case OriginSource.Fit { Destination: FitDestination.Type type }: Check(type.Value); break;
            default: throw new ProtocolFault(Fault.Domain);
        }
    }

    private void Check(End end)
    {
        Check(end.Value);
        switch (end)
        {
            case End.Expression expression when Enum.IsDefined(expression.Notation): break;
            case End.Borrow borrow: Check(borrow.Source); Check(borrow.Site); break;
            case End.Omitted omitted: Check(omitted.TypeOccurrence); break;
            case End.Closure closure when Enum.IsDefined(closure.Part): Check(closure.Header); break;
            default: throw new ProtocolFault(Fault.Domain);
        }
    }

    public void Check(Input row)
    {
        Check(row.Ref); Check(row.Reason);
        var category = Catalog.Of(row.Ref.Code);
        if (!Enum.IsDefined(row.Requirement) || !Enum.IsDefined(row.Assessment)) throw new ProtocolFault(Fault.Domain);
        var universe = row.Ref.Body.Id is null ? 1UL : (1UL << Cases(row.Ref.Body).Length) - 1;
        if (row.Mask == 0 || (row.Mask & ~universe) != 0 || (row.Ref.Body.Id is null && row.Mask != 1))
            throw new ProtocolFault(Fault.Mask);
        if (row.Assessment == Assessment.Limit && category != Category.Unsupported) throw new ProtocolFault(Fault.Pair);
        if (row.Assessment == Assessment.Held && (category != Category.Language || row.Ref.Body.Id is null ||
            row.Ref.Issue is not (Issue.Conflict or Issue.Effect))) throw new ProtocolFault(Fault.Pair);
        if (category == Category.Unsupported && (row.Reason is not Reason.Empty || row.Facts.Items.Any()))
            throw new ProtocolFault(Fault.Pair);
        if ((row.Ref.Issue is Issue.Origin) != (row.Reason is Reason.Origin)) throw new ProtocolFault(Fault.Pair);
        if (row.Ref.Issue is Issue.Origin origin && Obligation(origin.Obligation).ReturnFit &&
            row.Reason is not Reason.Origin { Source: OriginSource.Fit }) throw new ProtocolFault(Fault.Pair);
        switch (row.Copy)
        {
            case Copy.Outside: break;
            case Copy.Inlined copy when copy.Scope > 0:
                if (row.Ref.Body.Id is null) throw new ProtocolFault(Fault.Pair);
                Check(copy.Call); break;
            default: throw new ProtocolFault(Fault.Domain);
        }
        foreach (var fact in row.Facts.Items)
        {
            switch (fact)
            {
                case Fact.Site site when Enum.IsDefined(site.Role): Check(site.At); break;
                case Fact.Effect effect: Check(effect.Key); Check(effect.Call); break;
                case Fact.CoveredBy covered: CoveragePair(row, covered); break;
                default: throw new ProtocolFault(Fault.Domain);
            }
        }
    }

    private void CoveragePair(Input row, Fact.CoveredBy edge)
    {
        Check(edge.Target); // A valid identity need not have a failed recorded row.
        if (row.Ref.Body != edge.Target.Body || row.Ref.Body.Id is null || row.Requirement != Requirement.Ownership)
            throw new ProtocolFault(Fault.Pair);
        var source = row.Ref.Issue;
        var target = edge.Target.Issue;
        var valid = edge.Rule switch
        {
            Coverage.GuardReverse => row.Ref.Code == Code.OriginRefuted && source is Issue.Origin { Chain: Finite.Guard } o &&
                target is Issue.Conflict { Kind: ConflictKind.GuardEnd } c && o.Chain == c.Cause &&
                Obligation(o.Obligation).Boundary == c.At,
            Coverage.ReturnStorageEnd => source is Issue.Conflict { Kind: ConflictKind.StorageEnd, Cause: Finite.Borrow } c &&
                edge.Target.Code == Code.OriginRefuted && target is Issue.Origin o && o.Chain == c.Cause &&
                Obligation(o.Obligation) is { ReturnFit: true } obligation && obligation.Boundary == c.At &&
                c.At.Name is PointName.Return or PointName.Return2, // Hand-published: this Return _0 carries the named Borrow.
            Coverage.DefaultAccess => source is Issue.Conflict { Kind: ConflictKind.DefaultAccess } c &&
                target is Issue.Conflict { Kind: ConflictKind.DefaultBorrow } t && c.Cause is Finite.Borrow &&
                c.Cause == t.Cause && c.At == t.At && c.Access == t.Access,
            Coverage.DefaultOrigin => row.Ref.Code == Code.OriginRefuted && source is Issue.Origin { Chain: Finite.Borrow } o &&
                target is Issue.Conflict { Kind: ConflictKind.DefaultBorrow } t && o.Chain == t.Cause &&
                Obligation(o.Obligation).Boundary == t.At && Obligation(o.Obligation).Access == t.Access,
            _ => false,
        };
        // The fixture declares all A/B borrows PreparedInput-rooted; Other has a different root.
        if (edge.Rule is Coverage.DefaultAccess or Coverage.DefaultOrigin &&
            target is Issue.Conflict { Cause: Finite.Borrow { Id.Name: BorrowName.Other } }) valid = false;
        if (!valid) throw new ProtocolFault(Fault.Pair);
    }
}
