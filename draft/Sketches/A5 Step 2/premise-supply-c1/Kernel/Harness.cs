// Semantic observer. Expectations are external frozen data; mismatch never
// updates them. Timing-only benchmarks and production resource limits are absent.
using System.Text.Json;

namespace PremiseSupply;

internal sealed record Expectation(
    string Case, string Query, Completion Completion, Proof? Value,
    bool? Positive = null, bool? Negative = null, string? Failure = null,
    string[]? Candidates = null, string[]? Errors = null,
    string[]? Supports = null, string[]? AbsentSupports = null,
    string[]? Required = null, string[]? Pending = null, string[]? WrittenBounds = null,
    bool? Closed = null, bool? UsableCandidates = null, string[]? PrivateCandidates = null,
    string[]? ExactErrors = null, string? FailureOperation = null);

internal sealed class Harness(JsonSerializerOptions options, Expectation[] expectations)
{
    internal int Checks { get; private set; }
    internal int Failures { get; private set; }

    internal void Matrix(Input[] inputs)
    {
        var ids = inputs.SelectMany(x => x.Queries.Select(q => x.Id + "/" + q.Id)).Order(StringComparer.Ordinal).ToArray();
        Check(ids.SequenceEqual(expectations.Select(x => x.Case + "/" + x.Query).Order(StringComparer.Ordinal)), "expectation census");
        foreach (var source in inputs)
        foreach (var order in new[] { "forward", "reverse" })
        {
            var input = order == "forward" ? source : source with
            {
                Roots = source.Roots.Reverse().ToArray(), Rules = source.Rules.Reverse().ToArray(),
                Queries = source.Queries.Reverse().ToArray(),
            };
            var before = JsonSerializer.Serialize(input, options);
            foreach (var query in input.Queries)
            {
                var expected = expectations.Single(x => x.Case == input.Id && x.Query == query.Id);
                var demand = Observe(input, query, "demand", order);
                Assert(expected, demand, "demand/" + order);
                AssertWrittenBounds(input, expected, demand);
                var reference = Observe(input, query, "forward-reference", order);
                if (input.Oracle == Oracle.ClosedForward)
                {
                    Assert(expected, reference, "forward-reference/" + order);
                    AssertWrittenBounds(input, expected, reference);
                    Compare(demand, reference, input.Id + "/" + query.Id + "/" + order);
                }
                else
                {
                    Check(reference.Completion == Completion.Failed && reference.Failure?.Kind == "Resource",
                        input.Id + ": reference must not claim finite closure");
                }
            }
            Check(before == JsonSerializer.Serialize(input, options), input.Id + ": immutable snapshot");
        }
    }

    private void AssertWrittenBounds(Input input, Expectation expected, Answer answer)
    {
        // Observation joins exact original-root IDs only; it is not effect
        // selection or a rule for manufacturing bounds from equal propositions.
        var rows = input.Roots.Where(x => x.WrittenBound is not null &&
            answer.Supports.Any(e => e.Key == "root:" + x.Id)).Select(x => new
            { root = x.Id, written = x.WrittenBound! }).OrderBy(x => x.root, StringComparer.Ordinal).ToArray();
        Print(new { kind = "original-written-bounds", input.Id, rows, effectSelectionImplemented = false });
        if (expected.WrittenBounds is { } wanted)
            Check(rows.Select(x => x.root + "/" + x.written.Clause + "/" + x.written.Bound + "/" + x.written.Kind)
                .SequenceEqual(wanted), input.Id + ": exact original bound transport");
    }

    private Answer Observe(Input input, Query query, string algorithm, string order, Budget? budget = null)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = algorithm == "demand" ? Demand.Run(input, query, budget) : Forward.Run(input, query, budget);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Print(new { kind = "actual", input.Id, input.Scope, input.Oracle, query, algorithm, order,
            input.Correspondence, allocatedBytes = allocated, result });
        return result;
    }

    private void Assert(Expectation expected, Answer actual, string run)
    {
        var name = expected.Case + "/" + expected.Query + "/" + run;
        Print(new { kind = "expected", run, expected });
        Check(actual.Completion == expected.Completion && actual.Value == expected.Value, name + ": completion/verdict");
        if (expected.Positive is { } positive) Check(actual.Positive == positive, name + ": positive support");
        if (expected.Negative is { } negative) Check(actual.Negative == negative, name + ": negative support");
        Check(actual.Failure?.Kind == expected.Failure, name + ": typed failure");
        if (expected.FailureOperation is { } operation) Check(actual.Failure?.Operation == operation, name + ": failure owner");
        if (expected.Closed is { } closed) Check(actual.Closed == closed, name + ": computational closure");
        if (expected.UsableCandidates is { } usable)
            Check((actual.Completion == Completion.Complete && actual.Value is null) == usable, name + ": candidate consumer guard");
        if (expected.PrivateCandidates is { } discoveries)
            Check(actual.PrivateCandidates.SequenceEqual(discoveries), name + ": private discoveries");
        if (expected.ExactErrors is { } errors)
            Check(actual.RequiredErrors.SequenceEqual(errors), name + ": exact required errors");
        if (expected.Candidates is { } candidates) Check(actual.Candidates.SequenceEqual(candidates), name + ": complete candidate set");
        if (expected.Pending is { } pending) Check(actual.Pending.SequenceEqual(pending), name + ": pending dependencies");
        foreach (var error in expected.Errors ?? []) Check(actual.RequiredErrors.Contains(error), name + ": required Error " + error);
        foreach (var key in expected.Supports ?? []) Check(actual.Supports.Any(x => x.Key == key), name + ": retained support " + key);
        foreach (var key in expected.AbsentSupports ?? []) Check(actual.Supports.All(x => x.Key != key), name + ": unavailable support " + key);
        foreach (var key in expected.Required ?? []) Check(actual.RequiredEdges.Any(x => x.Key == key), name + ": required edge " + key);
        Check(actual.Completion == Completion.Complete || actual.Value is null && actual.Candidates.Length == 0,
            name + ": incomplete result exposes no logical verdict/candidate selection");
        Check(actual.Value != Proof.Error || actual.Candidates.Length == 0, name + ": erroneous result exposes no candidates");
        Check(actual.Closed == (actual.Completion == Completion.Complete), name + ": closure is completion, not success");
    }

    private void Compare(Answer demand, Answer reference, string name)
    {
        Check(demand.Completion == reference.Completion && demand.Value == reference.Value, name + ": independent verdict");
        if (demand.Completion == Completion.Failed) return;
        Check(demand.Positive == reference.Positive && demand.Negative == reference.Negative, name + ": both support polarities");
        Check(demand.Candidates.SequenceEqual(reference.Candidates), name + ": published candidates");
        Check(demand.Pending.SequenceEqual(reference.Pending), name + ": required pending work");
        Check(demand.Sources.All(reference.Sources.Contains), name + ": source availability reads represented in full reference");
        foreach (var error in demand.RequiredErrors)
            Check(reference.RequiredErrors.Contains(error), name + ": required Error represented in reference " + error);
        // The reference contains unrelated forward closure; compare the actual
        // root/application edges demanded in this request, not its whole universe.
        foreach (var edge in demand.RequiredEdges.Where(x => x.Operation == "clause-antecedent"))
            Check(reference.RequiredEdges.Any(x => x.Key == edge.Key && x.Dependencies.SequenceEqual(edge.Dependencies)),
                name + ": required antecedent " + edge.Key);
        foreach (var edge in demand.Supports.Where(x => x.Operation is "root" or "clause"))
            Check(reference.Supports.Any(x => x.Key == edge.Key && x.Dependencies.SequenceEqual(edge.Dependencies)),
                name + ": finite support " + edge.Key);
    }

    internal void Boundaries(Input input)
    {
        foreach (var algorithm in new[] { "demand", "forward-reference" })
        {
            var query = input.Queries.Single();
            var baseline = Observe(input, query, algorithm, "boundary-baseline");
            Check(baseline.Completion == Completion.Complete && baseline.Value == Proof.Proven, algorithm + ": finite baseline");
            foreach (var counter in Enum.GetValues<Counter>())
            {
                var used = baseline.Counters[counter.ToString()];
                Check(used > 0, algorithm + ": exercised counter " + counter);
                if (used == 0) continue;
                var exact = Observe(input, query, algorithm, "exact-" + counter, new(new Dictionary<Counter, long> { [counter] = used }));
                Check(exact.Completion == Completion.Complete && exact.Value == Proof.Proven, algorithm + ": exact bound " + counter);
                var oneLess = Observe(input, query, algorithm, "required-bound-plus-one-" + counter, new(new Dictionary<Counter, long> { [counter] = used - 1 }));
                Check(oneLess.Completion == Completion.Failed && oneLess.Failure is { Kind: "Resource" } && oneLess.Failure.Counter == counter,
                    algorithm + ": first disallowed operation " + counter);
            }
        }
    }

    internal void Lifecycle(Input[] inputs)
    {
        var transitions = new[]
        {
            (Before: "v2-8-pending", After: "v2-8-final", Name: "external-availability"),
            (Before: "scope-conditional", After: "scope-outer", Name: "external-remove-root"),
            (Before: "scope-outer", After: "scope-conditional", Name: "external-append-root"),
            (Before: "g170-d8", After: "scope-outer", Name: "large-to-tiny"),
        };
        foreach (var transition in transitions)
        {
            var budget = new Budget();
            Print(new { kind = "synthetic-transition", transition, noProductionScheduler = true });
            foreach (var id in new[] { transition.Before, transition.After, transition.After })
            {
                budget.Charge(Counter.Preparation, "explicit external transition/repeat");
                var input = inputs.Single(x => x.Id == id);
                var query = input.Queries[0];
                var answer = Observe(input, query, "demand", transition.Name, budget);
                Assert(expectations.Single(x => x.Case == id && x.Query == query.Id), answer, transition.Name);
            }
        }
        var invalid = inputs.Single(x => x.Id == "v2-6");
        var repaired = invalid with { Id = "external-repaired-D", Operands = [] };
        var repairBudget = new Budget();
        Print(new { kind = "synthetic-transition", before = invalid, after = repaired, noProductionScheduler = true });
        Assert(expectations.Single(x => x.Case == invalid.Id), Observe(invalid, invalid.Queries[0], "demand", "external-repair", repairBudget), "external-repair");
        repairBudget.Charge(Counter.Preparation, "external operand repair");
        var repairedExpected = new Expectation(repaired.Id, "q0", Completion.Complete, Proof.Proven, true, false);
        Assert(repairedExpected, Observe(repaired, repaired.Queries[0], "demand", "external-repair", repairBudget), "external-repair");
        Print(new { kind = "memory-boundary", managedBytesAfterCollection = GC.GetTotalMemory(true),
            claim = "process observation only; no retained-capacity or production layout/performance claim" });
    }

    private void Check(bool ok, string name)
    {
        Checks++;
        if (!ok) Failures++;
        Print(new { kind = "check", ok, name });
    }
    private void Print(object value) => Console.WriteLine(JsonSerializer.Serialize(value, options));
}
