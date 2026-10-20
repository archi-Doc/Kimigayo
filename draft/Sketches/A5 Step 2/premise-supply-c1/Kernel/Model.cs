// Inert inputs/results for the bounded PS1 experiment. One proposition/term DAG,
// stable source snapshots, original single-antecedent declaration templates.
// No Type enumeration, program-variable solving, production source scheduler,
// public Resource code, or assumption that captured declaration state is complete.
namespace PremiseSupply;

internal enum Completion { Complete, Pending, Failed }
internal enum Proof { Unknown, Proven, Refuted, Error }
internal enum SourceState { Available, Unavailable, Pending, Resource, Unsupported }
internal enum Validation { Valid, Invalid, Pending, Resource, Unsupported }
internal enum Purpose { Ordinary, NominalCopy, StructuralOwned }
internal enum QueryKind { Proof, Candidates }
internal enum Oracle { ClosedForward, FiniteDerivation, GrowingResource }
internal enum Counter { Work, Terms, Depth, Demands, Applications, Supports, Preparation }
internal enum CallableBound { Confined, PreservesResults }

internal sealed record Source(string Id, SourceState State, string Provenance);
internal sealed record WrittenBound(string Clause, string Bound, CallableBound Kind);
internal sealed record Root(string Id, string Proposition, string Source, WrittenBound? WrittenBound = null);
internal sealed record Rule(string Id, string Antecedent, string Head, string Source);
internal sealed record Operand(string Proposition, Validation State);
internal sealed record OwnerLeaf(string Id, string Proposition, Purpose Purpose, Proof Value, Validation State, string Provenance);
internal sealed record Query(string Id, string Proposition, QueryKind Kind = QueryKind.Proof, Purpose Purpose = Purpose.Ordinary);
internal sealed record Input(
    string Id, string Scope, Oracle Oracle, Validation Environment,
    Source[] Sources, Root[] Roots, Rule[] Rules, Operand[] Operands,
    OwnerLeaf[] Leaves, Query[] Queries, string Correspondence);

internal sealed record Failure(string Kind, string Operation, Counter Counter, long Limit);
internal sealed record Edge(string Key, string Output, string Operation, string Source, string[] Dependencies);
internal sealed record Answer(
    string Query, Completion Completion, Proof? Value, bool Positive, bool Negative,
    string[] RequiredErrors, string[] Pending, Failure? Failure,
    string[] Candidates, string[] PrivateCandidates, Edge[] Supports,
    Edge[] RequiredEdges, string[] Sources, IReadOnlyDictionary<string, long> Counters, bool Closed);

internal sealed class Exhausted(Failure failure) : Exception(failure.Operation)
{
    internal Failure Failure { get; } = failure;
}

internal sealed class Budget
{
    private readonly long[] limits;
    private readonly long[] counts = new long[Enum.GetValues<Counter>().Length];
    internal Budget(IReadOnlyDictionary<Counter, long>? overrides = null)
    {
        limits = [4000000, 60000, 64, 12000, 60000, 120000, 64];
        if (overrides is not null) foreach (var item in overrides) limits[(int)item.Key] = item.Value;
        if (limits.Any(x => x < 0)) throw new ArgumentOutOfRangeException(nameof(overrides));
    }

    internal void Charge(Counter counter, string operation)
    {
        if (counter != Counter.Work) Charge(Counter.Work, operation);
        var i = (int)counter;
        if (counts[i] >= limits[i]) throw new Exhausted(new("Resource", operation, counter, limits[i]));
        counts[i] = checked(counts[i] + 1);
    }

    internal void Depth(int depth, string operation)
    {
        Charge(Counter.Work, operation);
        if (depth > limits[(int)Counter.Depth])
            throw new Exhausted(new("Resource", operation, Counter.Depth, limits[(int)Counter.Depth]));
        counts[(int)Counter.Depth] = Math.Max(counts[(int)Counter.Depth], depth);
    }

    internal IReadOnlyDictionary<string, long> Snapshot()
        => Enum.GetValues<Counter>().ToDictionary(x => x.ToString(), x => counts[(int)x]);
}

internal sealed class Facts
{
    internal HashSet<Expr> Positive { get; } = [];
    internal HashSet<Expr> Error { get; } = [];
    internal Dictionary<string, Edge> Supports { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, Edge> Required { get; } = new(StringComparer.Ordinal);
    internal HashSet<string> Pending { get; } = new(StringComparer.Ordinal);
    internal HashSet<Expr> Discovered { get; } = [];
    internal HashSet<string> Sources { get; } = new(StringComparer.Ordinal);
}

internal sealed record ParsedRoot(string Id, Expr Proposition, Source Source);
internal sealed record ParsedRule(string Id, Expr Antecedent, Expr Head, Source Source);
internal sealed record ParsedLeaf(string Id, Expr Proposition, Purpose Purpose, Proof Value, Validation State);

internal sealed class Inputs
{
    internal Inputs(Input input, Arena arena)
    {
        Original = input;
        var sources = input.Sources.ToDictionary(x => x.Id, StringComparer.Ordinal);
        if (sources.Values.Any(x => x.Provenance != "synthetic"))
            throw new InvalidOperationException("Kernel source mapping has not been proved; source capture stays separate");
        Roots = input.Roots.Select(x => new ParsedRoot(x.Id, arena.Parse(x.Proposition), sources[x.Source])).ToArray();
        Rules = input.Rules.Select(x => new ParsedRule(x.Id, arena.Parse(x.Antecedent), arena.Parse(x.Head), sources[x.Source])).ToArray();
        if (input.Roots.Select(x => x.Id).Distinct().Count() != input.Roots.Length ||
            input.Rules.Select(x => x.Id).Distinct().Count() != input.Rules.Length)
            throw new InvalidOperationException("Original root/rule identities must be unique");
        foreach (var root in input.Roots.Where(x => x.WrittenBound is not null))
            if (arena.Parse(root.Proposition).Head != "callable")
                throw new InvalidOperationException("Written Callable bound requires its original Callable clause");
        Operands = input.Operands.ToDictionary(x => arena.Parse(x.Proposition), x => x.State);
        Leaves = input.Leaves.Select(x =>
        {
            if (x.Provenance != "synthetic") throw new InvalidOperationException("Unproved owner mapping");
            return new ParsedLeaf(x.Id, arena.Parse(x.Proposition), x.Purpose, x.Value, x.State);
        }).ToArray();
        foreach (var root in Roots) if (!root.Proposition.Ground) throw new InvalidOperationException("A root cannot bind template slots");
        foreach (var rule in Rules)
        {
            if (rule.Antecedent.Head != "is") throw new InvalidOperationException("Original Contract antecedent required");
            if (rule.Head.Slots.Except(rule.Antecedent.Slots).Any()) throw new InvalidOperationException("Unbound original declaration slot");
        }
    }

    internal Input Original { get; }
    internal ParsedRoot[] Roots { get; }
    internal ParsedRule[] Rules { get; }
    internal Dictionary<Expr, Validation> Operands { get; }
    internal ParsedLeaf[] Leaves { get; }
}
