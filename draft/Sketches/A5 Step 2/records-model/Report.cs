// Owns capture, disposition, exact surviving-mask joins, and publication of one complete model program.
// Consumes only published symbolic facts; never proves relations or searches source. No family store exists.
// ProtocolFault stands in for MirProgram.Report(subject, InternalInvariant_Kd) -> Compilation.MirRecords,
// outside MirReport. It is caught only by the test harness and is never recursively recorded here.
namespace RecordsModel;

sealed class Report
{
    private enum State { New, Collecting, Complete }
    private State state;
    private int generation;
    private Domain? domain;
    private readonly Dictionary<RowKey, ulong> rows = [];
    private readonly Dictionary<IssueRef, Requirement> requirements = [];
    private Result[] results = [];

    public int CapturedCount => rows.Count;

    public void Begin(Domain next)
    {
        if (state == State.Collecting) throw new ProtocolFault(Fault.Lifecycle);
        // Clear every reference-containing logical store. This is not physical retained-memory evidence.
        rows.Clear(); requirements.Clear(); results = []; domain = next;
        generation++;
        state = State.Collecting;
    }

    public void Record(Input row)
    {
        if (state != State.Collecting) throw new ProtocolFault(Fault.Lifecycle);
        domain!.Check(row);
        if (requirements.TryGetValue(row.Ref, out var requirement) && requirement != row.Requirement)
            throw new ProtocolFault(Fault.Requirement);
        requirements[row.Ref] = row.Requirement;
        // Capture a new typed set: a caller's mutable scratch collection cannot escape.
        var key = new RowKey(row.Ref, row.Requirement, row.Reason, row.Assessment, row.Copy, new(row.Facts.Items));
        rows[key] = rows.GetValueOrDefault(key) | row.Mask;
    }

    public void Complete()
    {
        if (state != State.Collecting) throw new ProtocolFault(Fault.Lifecycle);
        var pending = new List<Result>();
        foreach (var (key, mask) in rows)
        {
            // Current M69 known-Language disposition precedes hold. New caller-effect paths stay gated.
            if (key.Copy is Copy.Inlined && Catalog.Of(key.Ref.Code) == Category.Language) continue;
            pending.Add(key.Assessment switch
            {
                Assessment.Checked => new(key, key.Ref.Code, mask),
                Assessment.Held => new(key with { Requirement = Requirement.Ownership, Reason = new Reason.Empty(), Facts = new([]) }, Code.Unsupported, mask),
                Assessment.Limit => new(key, key.Ref.Code, mask),
                _ => throw new ProtocolFault(Fault.Domain),
            });
        }

        var surviving = new Dictionary<IssueRef, ulong>();
        var final = new List<Result>();
        // Dependencies are a closed DAG by issue kind, not by recorder arrival or source location.
        for (var rank = 0; rank < 3; rank++)
        {
            foreach (var row in pending.Where(row => Rank(row.Key.Ref.Issue) == rank))
            {
                var mask = row.Mask;
                foreach (var edge in row.Key.Facts.Items.OfType<Fact.CoveredBy>())
                    mask &= ~surviving.GetValueOrDefault(edge.Target);
                if (mask == 0) continue;
                final.Add(row with { Mask = mask });
                if (row.Key.Assessment == Assessment.Checked && Catalog.Of(row.Code) == Category.Language)
                    surviving[row.Key.Ref] = surviving.GetValueOrDefault(row.Key.Ref) | mask;
            }
        }
        results = final.OrderBy(Format.Row, StringComparer.Ordinal).ToArray();
        state = State.Complete;
    }

    private static int Rank(Issue issue) => issue switch
    {
        Issue.Conflict { Kind: ConflictKind.StorageEnd } => 2,
        Issue.Origin or Issue.Conflict { Kind: ConflictKind.DefaultAccess } => 1,
        _ => 0,
    };

    public IReadOnlyList<Result> All
    {
        get
        {
            if (state != State.Complete) throw new ProtocolFault(Fault.Lifecycle);
            return results;
        }
    }

    public Handle HandleAt(int index)
    {
        _ = All;
        if (index < 0 || index >= results.Length) throw new ProtocolFault(Fault.Handle);
        return new(generation, index);
    }

    public Result Read(Handle handle)
    {
        if (handle.Generation != generation) throw new ProtocolFault(Fault.Handle);
        _ = All;
        if (handle.Index < 0 || handle.Index >= results.Length) throw new ProtocolFault(Fault.Handle);
        return results[handle.Index];
    }

    public Publication[] Publish()
    {
        return Publish(All, domain!);
    }

    public static Publication[] Publish(IEnumerable<Result> all, Domain domain)
    {
        return all.GroupBy(row => new PublicKey(row.Code, row.Key.Ref.Subject, Public(row.Key.Reason)))
            .Select(group => new Publication(group.Key,
                group.GroupBy(row => (row.Key.Ref.Body, Facts: new ValueSet<Fact>(row.Key.Facts.Items.Where(f => f is not Fact.CoveredBy))))
                    .Select(explanation =>
                    {
                        var body = explanation.Key.Body;
                        var mask = explanation.Aggregate(0UL, (mask, row) => mask | row.Mask); // Within this body ONLY.
                        var cases = body.Id is null ? [] : domain.Cases(body).Where((_, bit) => (mask & (1UL << bit)) != 0);
                        return new Association(body, explanation.Key.Facts, new(cases), body.Id is null);
                    }).OrderBy(Format.Association, StringComparer.Ordinal).ToArray()))
            .OrderBy(Format.Publication, StringComparer.Ordinal).ToArray();
    }

    private static PublicReason Public(Reason reason) => reason switch
    {
        Reason.Empty => new(null, null, null, null),
        Reason.Origin origin => new(origin.Relation, Public(origin.Longer), Public(origin.Shorter), origin.Source),
        _ => throw new ProtocolFault(Fault.Domain),
    };

    private static PublicEnd Public(End end) => end switch
    {
        End.Expression expression => new(end.Value, EndRecipe.Expression, expression.Notation, null, null),
        End.Borrow borrow => new(end.Value, EndRecipe.Borrow, null, borrow.Site.Name, null),
        End.Omitted omitted => new(end.Value, EndRecipe.Omitted, null, omitted.TypeOccurrence.Name, null),
        End.Closure closure => new(end.Value, EndRecipe.Closure, null, closure.Header.Name, closure.Part),
        _ => throw new ProtocolFault(Fault.Domain),
    };
}

// Models the MirAnalysis version gate and validator-findings exit, not the production analyses.
sealed class Driver
{
    private int version;
    private readonly Report report = new();
    public int Evaluations { get; private set; }
    public Report Run(Domain domain, bool validatorRejected, Action<Report> analyze)
    {
        if (domain.Version == version) return report;
        report.Begin(domain);
        if (!validatorRejected) { analyze(report); Evaluations++; }
        report.Complete();
        version = domain.Version;
        return report;
    }
}
