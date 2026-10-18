// Probe for S5 (draft/Proposals/2026-10-19 Effect Atoms Widen.md): A4's summaries (MirEffects) after the mir pipeline's analysis,
// read by reflection. Per source: Binding issues, MIR problems, A4 body views, views whose summary has a Limit (the located
// Unsupported_Kd where a bound is owed; any growth reaches one at TyCtxt.Apply's depth 64), recursive call edges (A4's call edges whose
// callee reaches the caller; destructor entries are not edges, so a cycle through cleanup has none, case rv-F), recursive edges whose
// substitution names an associated-Type projection, summary atoms by projection depth, and A4's evaluations.
// Build: a scratch console project named Benchmark (Kimi's InternalsVisibleTo admits it) referencing a copy of the built Kimi.dll and
// its dependencies, outside the repository. Run: `Benchmark cases` (the programs below), `Benchmark census <repository>` (the library
// and tests/milestones/Milestone1..44), or `Benchmark contracts <root>...` (the recursive-Contract scan; bin/ and obj/ are skipped
// relative to each root).
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;

const string Tree = "contract Tree\n    associate Child is Tree\n    func child(self: ref/Self) -> Option<Self.Child>\n        effect confined\n";
const string Walk = "group Trees\n    public func walk<T>(node: T, depth: i32)\n        T is Tree\n        if depth == 0 => return\n        match node.child()\n" +
    "            .Some(let next) => Trees.walk(next@move, depth - 1)\n            .None => return\n";
const string Sink = "contract Sink\n    func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        effect confined\n";
const string Layer = "contract Layer\n    associate Inner\n    func peel(self: ref/Self) -> Option<Self.Inner>\n        effect confined\n";
const string Unwind = "group Layers\n    public func unwind<T>(layer: T, budget: i32)\n        T is Layer\n{0}        if budget == 0 => return\n        match layer.peel()\n" +
    "            .Some(let inner) => Layers.unwind(inner@move, budget - 1)\n            .None => return\n";
const string CopyTree = "contract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\n";
const string Inspect = "func inspect<T>(value: ref/T) => ()\n"; // The prelude of spec-cases.json's 15.6.4-static-anchors.
var cases = new (string Name, string Source)[]
{
    ("walk: T.Child grows", Tree + Walk + "func visit<U>(node: U)\n    U is Tree\n    Trees.walk(node@move, 2)\n"),
    ("walk under CopyTree in a confined witness", Sink + Tree + CopyTree + Walk +
        "struct Holder<T>\n    T is CopyTree\n    Self is Sink\n    var node: T\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n" +
        "        Trees.walk(self.node, 3)\n        return .Ok(())\n"),
    ("the SPEC example: visitCopies", Inspect + Tree + CopyTree + Walk + "group Counters\n    public var count: i32 = 0\n" +
        "func visitCopies<U>(node: U)\n    U is CopyTree\n    let c = Counters.count@ref\n    Trees.walk(node, 2)\n    inspect(c)\n"),
    ("a concrete Tree", Tree + "struct Node\n    Self is Tree\n    associate Tree.Child is Node\n    public var n: i32 = 0\n" +
        "    public func child(self: ref/Self) -> Option<Node> => .None\n"),
    ("identity premise T.Child is T", Tree + Walk.Replace("        T is Tree\n", "        T is Tree\n        T.(Tree).Child is T\n", StringComparison.Ordinal)),
    ("mutual: A.X is B, B.Y is A", "contract A\n    associate X is B\n    func toB(self: ref/Self) -> Option<Self.X>\n        effect confined\n" +
        "contract B\n    associate Y is A\n    func toA(self: ref/Self) -> Option<Self.Y>\n        effect confined\n" +
        "group M\n    public func f<T>(value: T, budget: i32)\n        T is A\n        if budget == 0 => return\n        match value.toB()\n" +
        "            .Some(let next) => M.g(next@move, budget - 1)\n            .None => return\n" +
        "    public func g<U>(value: U, budget: i32)\n        U is B\n        if budget == 0 => return\n        match value.toA()\n" +
        "            .Some(let next) => M.f(next@move, budget - 1)\n            .None => return\n"),
    ("rv-A, a generic Contract: Seq<E>.Next is Seq<Option<E>>",
        "contract Seq<E>\n    associate Next is Seq<Option<E>>\n    func next(self: ref/Self) -> Option<Self.Next>\n        effect confined\n" +
        "group S\n    public func walk<T, E>(node: T, depth: i32)\n        T is Seq<E>\n        if depth == 0 => return\n        match node.next()\n" +
        "            .Some(let n) => S.walk<T.(Seq<E>).Next, Option<E>>(n@move, depth - 1)\n            .None => return\n"),
    ("rv-B, a deep recursive base: Tree3.Item is Box, destroying T.Item.Inner",
        "contract Box\n    associate Inner\n    func inner(self: ref/Self) -> Self.Inner\n        effect confined\n" +
        "contract Tree3\n    associate Child is Tree3\n    associate Item is Box\n    func child(self: ref/Self) -> Option<Self.Child>\n        effect confined\n" +
        "    func item(self: ref/Self) -> Self.Item\n        effect confined\n" +
        "group G\n    public func walk<T>(node: ref/T, depth: i32)\n        T is Tree3\n        if depth == 0 => return\n        let box = node.item()\n" +
        "        let inner = box.inner()\n        match node.child()\n            .Some(let next) => G.walk(next@ref, depth - 1)\n            .None => return\n"),
    ("rv-F, growth through cleanup: Holder<T>.drop -> look<T> -> make<T.Child> -> Holder<T.Child>.drop",
        Tree + "struct Holder<T>\n    T is Tree\n    public var node: T\n    public init(node: T) => self.node = node@move\n    drop\n        Trees.look(self.node@ref)\n" +
        "group Trees\n    public func look<T>(n: ref/T)\n        T is Tree\n        match n.child()\n            .Some(let next) => Trees.make(next@move)\n            .None => return\n" +
        "    public func make<U>(n: U)\n        U is Tree\n        let h = Holder.init(n@move)\n"),
    ("recursion through a non-recursive Contract", Layer + string.Format(Unwind, string.Empty)),
    ("recursion with a depth-1 premise", Layer + string.Format(Unwind, "        T.(Layer).Inner is Layer\n")),
    ("ping: swapped arguments, Source not recursive", "contract Source\n    associate Item\n    func make(self: ref/Self) -> Self.Item\n        effect confined\n" +
        "group Ping\n    public func ping<L, R>(left: ref/L, right: ref/R, budget: i32)\n        L is Source\n        R is Source\n" +
        "        if budget == 0 => return\n        let item = left.make()\n        Ping.ping(right, left, budget - 1)\n" +
        "struct Name\n    Self is Source\n    public var n: i32 = 0\n    public func make(self: ref/Self) -> string => \"name\"\n" +
        "func run()\n    let a = Name.init()\n    let b = Name.init()\n    Ping.ping(a@ref, b@ref, 2)\n"),
    ("plain recursion", "group G\n    public func count(n: i32) -> i32\n        if n == 0 => return 0\n        return G.count(n - 1) + 1\n"),
    ("side finding: CopyTree witness, helper chain to T.Child.Child", Helpers("Trees.two(self.node)")),
    ("side finding: CopyTree witness, helper chain to T.Child", Helpers("Trees.one(self.node)")),
    ("side finding: Binding proves T.Child.Child Copy under CopyTree", "contract Tree\n    associate Child is Tree\n" + CopyTree + "group H\n" +
        "    public func twice<T>(value: T.(Tree).Child.(Tree).Child) -> (T.(Tree).Child.(Tree).Child, T.(Tree).Child.(Tree).Child)\n" +
        "        T is CopyTree\n        return (value, value)\n"),
};

if (args.Length > 0 && args[0] == "contracts")
{
    // Per unit (a .kimi file, a fenced block of a .md file, a physical line of a .cs or .json file, whose "\n" escapes are undone): each
    // `contract` declaration (its indented block; inside a string literal it starts at indentation 0), its parents and the Contract
    // names on the right of its associated-Type Constraints (`associate X is ...`, `Self.(C).X is ...`), Type arguments ignored (review
    // B2). A Contract is recursive when those names, its parents' included, lead through the Constraints of the unit's Contracts to it
    // or to one that refines it (proposal 2.1). A grep-level scan: a declaration split over several C# lines is not followed.
    var (direct, transitive) = (0, 0);
    foreach (var root in args.Skip(1))
    {
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (!Regex.IsMatch(relative, @"\.(kimi|md|cs|json)$") || Regex.IsMatch("/" + relative, "/(bin|obj)/"))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            var units = relative.EndsWith(".kimi", StringComparison.Ordinal) ? [(1, text)]
                : relative.EndsWith(".md", StringComparison.Ordinal) ? Regex.Matches(text, "```[^\n]*\n(.*?)```", RegexOptions.Singleline)
                    .Select(m => (text[..m.Index].Count(c => c == '\n') + 1, m.Groups[1].Value)).ToArray()
                : text.Split('\n').Select((line, i) => (i + 1, line.Replace("\\n", "\n", StringComparison.Ordinal))).Where(u => u.Item2.Contains("contract ", StringComparison.Ordinal)).ToArray();
            foreach (var (start, unit) in units)
            {
                foreach (var (name, line, self) in RecursiveContracts(unit))
                {
                    (direct, transitive) = self ? (direct + 1, transitive) : (direct, transitive + 1);
                    Console.WriteLine($"{relative}:{start + line - 1} | {name} | {(self ? "names itself" : "through other Contracts")}");
                }
            }
        }
    }

    Console.WriteLine($"recursive Contracts: {direct} naming themselves, {transitive} through other Contracts");
}
else if (args.Length > 0 && args[0] == "census")
{
    var root = args[1];
    Console.Write(Probe("library", string.Empty, library: true));
    for (var i = 1; i <= 44; i++)
    {
        Console.Write(Probe($"Milestone{i}", File.ReadAllText(Path.Combine(root, "tests", "milestones", $"Milestone{i}.kimi")), library: false));
    }
}
else
{
    foreach (var (name, source) in cases)
    {
        Console.Write(Probe(name, source, library: false, details: true));
    }
}

static string Probe(string name, string source, bool library, bool details = false)
{
    var c = Compilation.CreateForTest();
    c.Pipeline = CompilerPipeline.Mir;
    if (!c.Prepare(WindowsProfile.Target))
    {
        return $"=== {name}: PREPARE FAILED\n";
    }

    c.Kotonoha.AddSource(new SourceDocument("Program.kimi", source));
    c.Bind();
    _ = c.Binding.CheckStartup(OutputKind.Application);
    _ = c.Analyze();
    var text = new StringBuilder($"=== {name}\n");
    var program = c.BuildMir();
    foreach (var issue in c.Binding.Issues)
    {
        text.Append(details ? $"BIND {issue.Code} | {Line(issue.Node)}\n" : string.Empty);
    }

    var problems = program.Problems.All.ToArray().Where(p => library || p.Subject.Module == 0).ToArray();
    foreach (var p in problems)
    {
        text.Append(details ? $"MIR  {p.Code} | {Line(c.SyntaxOf(p.Subject))}\n" : string.Empty);
    }

    var analysis = typeof(Compilation).GetField("mirAnalysis", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(c)!;
    var effects = analysis.GetType().GetField("effects", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(analysis)!;
    T F<T>(string field) => (T)effects.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(effects)!;
    var (nodeCount, owners, bases, classes) = (F<int>("nodeCount"), F<int[]>("owners"), F<int[]>("bases"), F<byte[]>("classes"));
    var (atomStarts, atomCounts, atoms, edges, edgeEnds) = (F<int[]>("atomStarts"), F<int[]>("atomCounts"), F<Array>("atoms"), F<Array>("edges"), F<int[]>("edgeEnds"));
    var graph = Enumerable.Range(0, program.Bodies.Length).Select(_ => new List<int>()).ToArray();
    int Callee(int e) => (int)edges.GetValue(e)!.GetType().GetProperty("Body")!.GetValue(edges.GetValue(e))!;
    for (var node = 0; node < nodeCount; node++)
    {
        for (var e = node == 0 ? 0 : edgeEnds[node - 1]; e < edgeEnds[node]; e++)
        {
            graph[owners[node]].Add(Callee(e));
        }
    }

    bool Reaches(int from, int to)
    {
        var (seen, stack) = (new bool[graph.Length], new Stack<int>([from]));
        while (stack.Count > 0)
        {
            var b = stack.Pop();
            if (b == to)
            {
                return true;
            }

            if (!seen[b])
            {
                seen[b] = true;
                graph[b].ForEach(stack.Push);
            }
        }

        return false;
    }

    var (views, limits, recursive, projections) = (0, 0, 0, 0);
    var depths = new int[8];
    var lines = new SortedSet<string>(StringComparer.Ordinal);
    for (var node = 0; node < nodeCount; node++)
    {
        var body = program.Bodies[owners[node]];
        if (!library && body.Source.Module != 0)
        {
            continue;
        }

        views++;
        var label = $"{body.Key.Decl?.Name} view {node - bases[owners[node]]}";
        if ((classes[node] & 4) != 0)
        {
            limits++;
            lines.Add($"LIMIT {label}");
        }

        for (var a = atomStarts[node]; a < atomStarts[node] + atomCounts[node]; a++)
        {
            var type = (BoundType)atoms.GetValue(a)!.GetType().GetProperty("Type")!.GetValue(atoms.GetValue(a))!;
            var depth = 0;
            for (var t = type; t.Kind == BoundTypeKind.AssociatedProjection && t.Components.Count > 0; t = t.Components[0])
            {
                depth++;
            }

            depths[Math.Min(depth, 7)]++;
        }

        for (var e = node == 0 ? 0 : edgeEnds[node - 1]; e < edgeEnds[node]; e++)
        {
            if (!Reaches(Callee(e), owners[node]))
            {
                continue;
            }

            recursive++;
            var subst = (Subst?)edges.GetValue(e)!.GetType().GetProperty("Subst")!.GetValue(edges.GetValue(e));
            if (subst is not null && subst.Types.ToArray().Any(t => t is not null && Projects(t)))
            {
                projections++;
                lines.Add($"PROJECTION-RECURSION {label} -> {program.Bodies[Callee(e)].Key.Decl?.Name}");
            }
        }
    }

    var evaluations = effects.GetType().GetProperty("Evaluations", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(effects);
    text.Append($"binding issues {c.Binding.Issues.Count()}, MIR problems {problems.Length}, views {views}, Limit {limits}, recursive edges {recursive}, ")
        .Append($"with a projection {projections}, atoms by depth {string.Join('/', depths)}, evaluations {evaluations}\n");
    foreach (var line in lines)
    {
        text.Append(details ? line + "\n" : string.Empty);
    }

    return text.ToString();
}

static bool Projects(BoundType type) => type.Kind == BoundTypeKind.AssociatedProjection || type.Components.Any(Projects);

// A confined witness under CopyTree calling a non-recursive helper chain: two<T> passes its T.Child to one<T>, which destroys its
// own T.Child, so put's summary destroys T, T.Child and T.Child.Child, each Copy under CopyTree.
static string Helpers(string call) => "contract Sink\n    func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n        effect confined\n" +
    "contract Tree\n    associate Child is Tree\n    func child(self: ref/Self) -> Option<Self.Child>\n        effect confined\n" +
    "contract CopyTree: Tree, Copy\n    Self.(Tree).Child is CopyTree\n" +
    "group Trees\n    public func two<T>(node: T)\n        T is Tree\n        match node.child()\n            .Some(let next) => Trees.one(next@move)\n" +
    "            .None => return\n    public func one<T>(node: T)\n        T is Tree\n        match node.child()\n            .Some(let next) => return\n" +
    "            .None => return\n" +
    "struct Holder<T>\n    T is CopyTree\n    Self is Sink\n    var node: T\n    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>\n" +
    $"        {call}\n        return .Ok(())\n";

// The recursive Contracts of one unit of text: (name, line in the unit, whether it names itself directly).
static List<(string Name, int Line, bool Self)> RecursiveContracts(string unit)
{
    var lines = unit.Split('\n');
    var contracts = new Dictionary<string, (HashSet<string> Parents, HashSet<string> Yields, int Line)>();
    for (var i = 0; i < lines.Length; i++)
    {
        var m = Regex.Match(lines[i], @"(^|[""\s])contract\s+([A-Z]\w*)(?:<[^>\n]*>)?\s*(?::\s*([^""\n]+))?");
        if (!m.Success)
        {
            continue;
        }

        var name = m.Groups[2].Value;
        var indent = Regex.IsMatch(lines[i][..m.Index], @"^\s*(?:(?:public|internal|private)\b\s*)*$") ? Indent(lines[i]) : 0;
        var entry = contracts.TryGetValue(name, out var known) ? known : ([], [], i + 1);
        foreach (Match parent in Regex.Matches(m.Groups[3].Value, @"(?:^|,)\s*([A-Z]\w*)"))
        {
            entry.Parents.Add(parent.Groups[1].Value);
        }

        for (var j = i + 1; j < lines.Length && (lines[j].Trim().Length == 0 || Indent(lines[j]) > indent); j++)
        {
            var line = lines[j].Trim();
            if ((line.StartsWith("associate ", StringComparison.Ordinal) || Regex.IsMatch(line, @"^Self\.\(\w+\)\.\w+\s+is\s"))
                && line.IndexOf(" is ", StringComparison.Ordinal) is var at and >= 0)
            {
                foreach (Match target in Regex.Matches(line[(at + 4)..].Split("//")[0].Split('"')[0], @"\b([A-Z]\w*)"))
                {
                    entry.Yields.Add(target.Groups[1].Value);
                }
            }
        }

        contracts[name] = entry;
    }

    IEnumerable<string> Yields(string k, int depth = 0) => depth < 16 && contracts.TryGetValue(k, out var c) ? c.Yields.Concat(c.Parents.SelectMany(p => Yields(p, depth + 1))) : [];
    bool Refines(string m, string k, int depth = 0) => m == k || (depth < 16 && contracts.TryGetValue(m, out var c) && c.Parents.Any(p => Refines(p, k, depth + 1)));
    var found = new List<(string Name, int Line, bool Self)>();
    foreach (var (name, entry) in contracts)
    {
        var (seen, stack) = (new HashSet<string>(), new Stack<string>(Yields(name).Distinct()));
        var hit = false;
        while (stack.Count > 0 && !hit)
        {
            var next = stack.Pop();
            hit = contracts.ContainsKey(next) && Refines(next, name);
            if (!hit && seen.Add(next))
            {
                foreach (var y in Yields(next).Distinct())
                {
                    stack.Push(y);
                }
            }
        }

        if (hit)
        {
            found.Add((name, entry.Line, entry.Yields.Contains(name)));
        }
    }

    return found;
}

static int Indent(string line) => line.Length - line.TrimStart(' ', '\t').Length;

static string Line(Koto node) => node.ToString()?.Split('\n')[0].Trim() ?? "?";
