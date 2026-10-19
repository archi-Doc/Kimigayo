// Deliberately incorrect controls, isolated from the model. A detected mismatch is REQUIRED.
namespace RecordsModel;

static class Ablations
{
    public static Input[] FirstFacts(Scenario scenario) => scenario.Rows
        .GroupBy(row => (row.Ref.Body, row.Ref.Code, row.Ref.Subject))
        .Select(group => group.First() with { Mask = group.Aggregate(0UL, (mask, row) => mask | row.Mask) }).ToArray();

    public static Input[] Span(Scenario scenario) => scenario.Rows.Where(row =>
        !(row.Ref.Issue is Issue.Origin && scenario.Rows.Any(preferred => preferred.Ref.Code == Code.DefaultBorrow &&
            preferred.Ref.Body == row.Ref.Body && preferred.Ref.Subject == row.Ref.Subject))).ToArray();

    public static Input[] Loan(Scenario scenario) => scenario.Rows.Where(row =>
        !(row.Ref.Issue is Issue.Conflict conflict && scenario.Rows.Any(preferred =>
            preferred.Ref.Issue is Issue.Origin origin && preferred.Ref.Body == row.Ref.Body && origin.Chain == conflict.Cause))).ToArray();

    public static Result[] RemovedPreferred(Scenario scenario, IReadOnlyList<Result> final) => final.Select(row =>
    {
        var mask = row.Mask;
        foreach (var edge in row.Key.Facts.Items.OfType<Fact.CoveredBy>())
            foreach (var preferred in scenario.Rows.Where(input => input.Ref == edge.Target))
                mask &= ~preferred.Mask; // Wrong: the original provisional row, including a removed row, is treated as cover.
        return row with { Mask = mask };
    }).Where(row => row.Mask != 0).ToArray();
}
