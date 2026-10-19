// Deterministic lossless oracle format. No equality, merge, or coverage decision consumes this text.
namespace RecordsModel;

static class Format
{
    public static string Body(Body body) => body.Id?.Name.ToString() ?? "NoBody";
    public static string Finite(Finite finite) => finite switch
    {
        RecordsModel.Finite.None => "none",
        RecordsModel.Finite.Borrow b => $"borrow:{b.Id.Name}",
        RecordsModel.Finite.Guard g => $"guard:{g.Id.Name}",
        _ => throw new ProtocolFault(Fault.Domain),
    };
    public static string Key(EffectKey key) => key switch
    {
        EffectKey.Unknown => "Unknown",
        EffectKey.Static s => $"{s.Field.Name}/{s.Access}",
        _ => throw new ProtocolFault(Fault.Domain),
    };
    public static string Issue(Issue issue) => issue switch
    {
        RecordsModel.Issue.Source s => $"source:{s.Condition}",
        RecordsModel.Issue.Origin o => $"origin:{o.Obligation.Name}/{Finite(o.Chain)}",
        RecordsModel.Issue.Conflict c => $"{c.Kind}:{c.At.Name}/{c.Access.Name}/{Finite(c.Cause)}",
        RecordsModel.Issue.Effect e => $"effect:{e.At.Name}/borrow:{e.Borrow.Name}/{Key(e.Key)}",
        _ => throw new ProtocolFault(Fault.Domain),
    };
    public static string Reference(IssueRef r) => $"{Body(r.Body)}:{r.Code}@{r.Subject.Name}:{Issue(r.Issue)}";
    public static string Destination(FitDestination destination) => destination switch
    {
        FitDestination.Type t => $"Type({t.Value.Name})",
        FitDestination.OriginOnly => "OriginOnly",
        _ => throw new ProtocolFault(Fault.Domain),
    };
    public static string Source(OriginSource source) => source switch
    {
        OriginSource.Fit f => $"Fit({Destination(f.Destination)})",
        OriginSource.Declared => "Declared",
        OriginSource.WellFormed => "WellFormed",
        _ => throw new ProtocolFault(Fault.Domain),
    };
    public static string End(End end) => end switch
    {
        RecordsModel.End.Expression e => $"{e.Value.Name}:expr({e.Notation})",
        RecordsModel.End.Borrow b => $"{b.Value.Name}:borrow({Finite(b.Source)}@{b.Site.Name})",
        RecordsModel.End.Omitted o => $"{o.Value.Name}:omitted({o.TypeOccurrence.Name})",
        RecordsModel.End.Closure c => $"{c.Value.Name}:closure({c.Header.Name}/{c.Part})",
        _ => throw new ProtocolFault(Fault.Domain),
    };
    public static string Reason(Reason reason) => reason switch
    {
        RecordsModel.Reason.Empty => "empty",
        RecordsModel.Reason.Origin o => $"{o.Relation}({End(o.Longer)},{End(o.Shorter)},{Source(o.Source)})",
        _ => throw new ProtocolFault(Fault.Domain),
    };
    public static string Fact(Fact fact) => fact switch
    {
        RecordsModel.Fact.Site s => $"{s.Role}@{s.At.Name}",
        RecordsModel.Fact.Effect e => $"effect({Key(e.Key)}@{e.Call.Name})",
        RecordsModel.Fact.CoveredBy c => $"covered({c.Rule}=>{Reference(c.Target)})",
        _ => throw new ProtocolFault(Fault.Domain),
    };
    public static string Facts(ValueSet<Fact> facts) => string.Join(",", facts.Items.Select(Fact).Order(StringComparer.Ordinal));
    public static string Copy(Copy copy) => copy switch
    {
        RecordsModel.Copy.Outside => "outside",
        RecordsModel.Copy.Inlined c => $"inline:{c.Scope}@{c.Call.Name}",
        _ => throw new ProtocolFault(Fault.Domain),
    };
    public static string Row(Result row) => $"{Reference(row.Key.Ref)} => {row.Code}|{row.Key.Requirement}|{Reason(row.Key.Reason)}|{row.Key.Assessment}|{Copy(row.Key.Copy)}|[{Facts(row.Key.Facts)}]|{row.Mask:X}";
    public static string PublicEnd(PublicEnd end) => $"{end.Value.Name}:{end.Recipe}:{end.Notation?.ToString() ?? "-"}:{end.RecipeSite?.ToString() ?? "-"}:{end.Part?.ToString() ?? "-"}";
    public static string PublicReason(PublicReason reason) => reason.Relation is null ? "empty" : $"{reason.Relation}({PublicEnd(reason.Longer!)},{PublicEnd(reason.Shorter!)},{Source(reason.Source!)})";
    public static string Association(Association a) => $"{Body(a.Body)}{{{(a.Bodyless ? "present" : string.Join(",", a.Cases.Items.Order()))}}}:[{Facts(a.Facts)}]";
    public static string Publication(Publication p) => $"PUBLIC {p.Key.Code}@{p.Key.Subject.Name}|{PublicReason(p.Key.Reason)}|{string.Join(";", p.Explanations.Select(Association))}";
    public static string[] Snapshot(Report report) => report.All.Select(Row).Concat(report.Publish().Select(Publication)).ToArray();
}
