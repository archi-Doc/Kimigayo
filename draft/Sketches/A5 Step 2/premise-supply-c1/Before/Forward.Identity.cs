// Reference identity uses explicit symmetric/transitive pairs and congruence,
// independently of demand's union/find and its template unifier.
namespace PremiseSupply;

internal sealed class ForwardIdentity(Arena arena, Budget budget)
{
    private readonly HashSet<(Expr, Expr)> pairs = [];
    internal bool Equal(Expr a, Expr b)
    {
        budget.Charge(Counter.Work, "reference equality");
        return ReferenceEquals(a, b) || pairs.Contains((a, b));
    }

    internal bool Close(IEnumerable<Expr> equations)
    {
        var changed = false;
        foreach (var equation in equations)
        {
            changed |= pairs.Add((equation.Args[0], equation.Args[1]));
            changed |= pairs.Add((equation.Args[1], equation.Args[0]));
        }
        var again = true;
        while (again)
        {
            again = false;
            foreach (var left in pairs.ToArray())
            foreach (var right in pairs.ToArray())
            {
                budget.Charge(Counter.Work, "reference transitivity");
                if (ReferenceEquals(left.Item2, right.Item1)) again |= pairs.Add((left.Item1, right.Item2));
            }
            var terms = arena.All.Where(x => x.Ground).ToArray();
            foreach (var a in terms)
            foreach (var b in terms)
            {
                budget.Charge(Counter.Work, "reference congruence");
                if (a.Head == b.Head && a.Args.Length == b.Args.Length && a.Args.Zip(b.Args).All(x => Equal(x.First, x.Second)))
                    again |= pairs.Add((a, b));
            }
            changed |= again;
        }
        return changed;
    }

    internal Dictionary<string, Expr>? Bind(Expr template, Expr actual)
    {
        if (!actual.Ground) throw new InvalidOperationException("Reference enumerates actual ground instances");
        var bindings = new Dictionary<string, Expr>(StringComparer.Ordinal);
        return Visit(template, actual) ? bindings : null;
        bool Visit(Expr pattern, Expr ground)
        {
            budget.Charge(Counter.Work, "reference actual-instance match");
            if (pattern.Slot)
            {
                if (bindings.TryGetValue(pattern.Head, out var previous)) return Equal(previous, ground);
                bindings.Add(pattern.Head, ground);
                return true;
            }
            if (pattern.Ground && Equal(pattern, ground)) return true;
            if (pattern.Head != ground.Head || pattern.Args.Length != ground.Args.Length) return false;
            for (var i = 0; i < pattern.Args.Length; i++) if (!Visit(pattern.Args[i], ground.Args[i])) return false;
            return true;
        }
    }
}
