// Private inert spelling for model snapshots, not a second production proposition
// language. Lowercase reserved heads mirror existing proposition kinds. Other
// heads identify complete Type/reference constructors, including explicit metadata.
namespace PremiseSupply;

internal sealed class Expr(string head, Expr[] args, string key)
{
    internal string Head { get; } = head;
    internal Expr[] Args { get; } = args;
    internal string Key { get; } = key;
    internal bool Slot => Head.StartsWith('$');
    internal bool Ground { get; } = !head.StartsWith('$') && args.All(x => x.Ground);
    internal int Depth { get; } = args.Length == 0 ? 1 : checked(1 + args.Max(x => x.Depth));
    internal IEnumerable<string> Slots => Slot ? [Head] : Args.SelectMany(x => x.Slots).Distinct(StringComparer.Ordinal);
    public override string ToString() => Key;
}

internal sealed class Arena(Budget budget)
{
    private readonly Dictionary<string, Expr> expressions = new(StringComparer.Ordinal);
    internal IEnumerable<Expr> All => expressions.Values;
    internal Expr Make(string head, params Expr[] args)
    {
        if (head == "not" && args.Length == 1 && args[0].Head == "not")
        {
            budget.Charge(Counter.Work, "double negation identity");
            return args[0].Args[0];
        }
        var depth = args.Length == 0 ? 1 : checked(1 + args.Max(x => x.Depth));
        budget.Depth(depth, "materialize " + head);
        var key = args.Length == 0 ? head : head + "(" + string.Join(',', args.Select(x => x.Key)) + ")";
        if (expressions.TryGetValue(key, out var existing)) return existing;
        budget.Charge(Counter.Terms, "intern " + head);
        var result = new Expr(head, args, key);
        expressions.Add(key, result);
        return result;
    }

    internal Expr Not(Expr value) => value.Head == "not" ? value.Args[0] : Make("not", value);

    internal Expr Parse(string text)
    {
        var position = 0;
        var result = Read(1);
        if (position != text.Length) throw new InvalidOperationException("Trailing input: " + text);
        return result;
        Expr Read(int depth)
        {
            budget.Depth(depth, "parse input");
            var start = position;
            while (position < text.Length && (char.IsLetterOrDigit(text[position]) || "_$:.-".Contains(text[position]))) position++;
            if (position == start) throw new InvalidOperationException("Expected identity at " + position + ": " + text);
            var head = text[start..position];
            var children = new List<Expr>();
            if (position < text.Length && text[position] == '(')
            {
                position++;
                do
                {
                    children.Add(Read(checked(depth + 1)));
                    if (position < text.Length && text[position] == ',') position++;
                    else break;
                }
                while (true);
                if (position == text.Length || text[position++] != ')') throw new InvalidOperationException("Missing )");
            }
            if (head.StartsWith('$') && children.Count != 0) throw new InvalidOperationException("Applied template slot");
            return Make(head, children.ToArray());
        }
    }

    internal Expr Substitute(Expr value, IReadOnlyDictionary<string, Expr> slots)
    {
        budget.Charge(Counter.Work, "substitute");
        if (value.Slot) return slots.TryGetValue(value.Head, out var bound) ? bound : value;
        return value.Ground ? value : Make(value.Head, value.Args.Select(x => Substitute(x, slots)).ToArray());
    }

    internal Expr CanonicalPattern(Expr value)
    {
        var slots = value.Slots.Select((x, i) => (x, y: Make("$" + i)))
            .ToDictionary(x => x.x, x => x.y, StringComparer.Ordinal);
        return Substitute(value, slots);
    }

    internal IEnumerable<(string Path, Expr Head)> Heads(Expr value)
    {
        var stack = new Stack<(string, Expr)>();
        stack.Push(("", value));
        while (stack.TryPop(out var row))
        {
            budget.Charge(Counter.Work, "enumerate exact/conjunction head");
            yield return row;
            if (row.Item2.Head == "and")
            {
                stack.Push((row.Item1 + ".R", row.Item2.Args[1]));
                stack.Push((row.Item1 + ".L", row.Item2.Args[0]));
            }
        }
    }
}

internal sealed class DemandIdentity(Budget budget)
{
    private readonly Dictionary<Expr, Expr> parent = [];
    internal bool Join(Expr left, Expr right)
    {
        left = Root(left); right = Root(right);
        if (ReferenceEquals(left, right)) return false;
        parent[right] = left;
        return true;
    }

    internal bool Equal(Expr left, Expr right)
    {
        budget.Charge(Counter.Work, "identity match");
        if (ReferenceEquals(Root(left), Root(right))) return true;
        if (left.Head != right.Head || left.Args.Length != right.Args.Length) return false;
        for (var i = 0; i < left.Args.Length; i++) if (!Equal(left.Args[i], right.Args[i])) return false;
        return true;
    }

    internal bool Close(IEnumerable<Expr> groundTerms)
    {
        var changed = false;
        var terms = groundTerms.Where(x => x.Ground).ToArray();
        for (var i = 0; i < terms.Length; i++)
        for (var j = i + 1; j < terms.Length; j++)
            if (Equal(terms[i], terms[j])) changed |= Join(terms[i], terms[j]);
        return changed;
    }

    private Expr Root(Expr value)
    {
        while (parent.TryGetValue(value, out var next))
        {
            budget.Charge(Counter.Work, "identity representative");
            value = next;
        }
        return value;
    }
}

internal sealed class DemandMatch(Arena arena, Budget budget, DemandIdentity identity)
{
    internal Dictionary<string, Expr>? Unify(Expr left, Expr right)
    {
        var slots = new Dictionary<string, Expr>(StringComparer.Ordinal);
        var pending = new Stack<(Expr, Expr)>();
        pending.Push((left, right));
        while (pending.TryPop(out var pair))
        {
            budget.Charge(Counter.Work, "demand head/instance match");
            var a = Resolve(pair.Item1); var b = Resolve(pair.Item2);
            if (ReferenceEquals(a, b)) continue;
            if (a.Slot || b.Slot)
            {
                if (!a.Slot) (a, b) = (b, a);
                if (Occurs(a.Head, b)) return null;
                slots[a.Head] = b;
                continue;
            }
            if (a.Ground && b.Ground && identity.Equal(a, b)) continue;
            if (a.Head != b.Head || a.Args.Length != b.Args.Length) return null;
            for (var i = 0; i < a.Args.Length; i++) pending.Push((a.Args[i], b.Args[i]));
        }
        return slots.Keys.ToDictionary(x => x, x => Expand(slots[x]), StringComparer.Ordinal);

        Expr Resolve(Expr x)
        {
            while (x.Slot && slots.TryGetValue(x.Head, out var y)) x = y;
            return x;
        }
        bool Occurs(string variable, Expr value)
        {
            value = Resolve(value);
            return value.Head == variable || value.Args.Any(x => Occurs(variable, x));
        }
        Expr Expand(Expr value)
        {
            value = Resolve(value);
            return value.Ground ? value : arena.Make(value.Head, value.Args.Select(Expand).ToArray());
        }
    }
}
