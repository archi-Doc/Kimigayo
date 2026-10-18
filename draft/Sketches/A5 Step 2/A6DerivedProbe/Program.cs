// Probe for unit 2x-P10 (A6 derived rules, A5 step 2 plan 4.1; record: A6DerivedPrototype.md): the A1/A2/A3 MIR problems of
// Binding-rejected programs today and under ER-derived's write rule, on a copy of the built compiler (shadow; nothing published).
// Model run: each Opaque site of an assignment or update the probe resolves is rewritten in the built MIR into the site with its
// value's roots (read, no Move), then for an update the written Place's read, then an Opaque write of each written Place (MirInit's
// Define of its Move Path only); a target without a Move Path (through a reference, an index or a computed Property) keeps today's form.
// A fresh MirAnalysis then reruns A3, A1, A2 (and A4, A5, not listed). A RequireFallthrough_Kd is classed derived when every runtime
// path from its failure body's entry to its end passes an Opaque site Binding.IsGuess flags (replicated here), else direct, beside
// Binding.GuessedCompletion's verdict on the same failure body.
// Usage: Benchmark <file.kimi | directory | cases.json (Name, Source rows)>...
//   PROBE_ALL=1        also prints Binding-complete programs
//   PROBE_NOREWRITE=1  the self-check: reruns the analyses without the rewrite (must change no record)
//   PROBE_E4A=1        simulates unit 2x-E4a: a Move of a part of drop's self becomes a read (Binding's rejected node, no Move)
//   PROBE_DUMP=1       prints each source body's MIR
// Built as an assembly named Benchmark (Kimi's InternalsVisibleTo) against a copy of src/Kimi/bin/Release/net10.0 in ../kimibin,
// outside the repository so its Directory.Build.props does not apply.
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

var all = Environment.GetEnvironmentVariable("PROBE_ALL") is not null;
var programs = new List<(string Name, List<(string File, string Text)> Docs)>();
foreach (var arg in args)
{
    if (Directory.Exists(arg))
    {
        foreach (var file in Directory.GetFiles(arg, "*.kimi").OrderBy(x => x, StringComparer.Ordinal))
        {
            programs.Add((Path.GetFileName(file), Split(File.ReadAllText(file))));
        }
    }
    else if (arg.EndsWith(".json", StringComparison.Ordinal))
    {
        foreach (var entry in JsonDocument.Parse(File.ReadAllText(arg)).RootElement.EnumerateArray())
        {
            if (entry.TryGetProperty("Source", out var source))
            {
                programs.Add((Path.GetFileNameWithoutExtension(arg) + ":" + entry.GetProperty("Name").GetString(), [("Program.kimi", source.GetString()!)]));
            }
        }
    }
    else
    {
        programs.Add((Path.GetFileName(arg), Split(File.ReadAllText(arg))));
    }
}

var codes = new HashSet<DiagnosticCode>
{
    DiagnosticCode.UninitializedPlace_Kd, DiagnosticCode.MovedPlace_Kd, DiagnosticCode.ReassignedLet_Kd, DiagnosticCode.StaticMovePathRequired_Kd,
    DiagnosticCode.RequireFallthrough_Kd, DiagnosticCode.DefaultArgumentMove_Kd, DiagnosticCode.OwnershipStorageLimit_Kd,
};
var summary = new StringBuilder();
var (incomplete, changed, changedRecords, a3Derived) = (0, 0, 0, 0);
foreach (var (name, docs) in programs)
{
    var c = Compilation.CreateForTest();
    c.Pipeline = CompilerPipeline.Mir;
    if (!c.Prepare(WindowsProfile.Target))
    {
        Console.WriteLine("PREPARE FAILED " + name);
        continue;
    }

    foreach (var (file, source) in docs)
    {
        c.Kotonoha.AddSource(new SourceDocument(file, source));
    }

    c.Bind();
    _ = c.Binding.CheckStartup(OutputKind.Application);
    _ = c.Analyze();
    var complete = c.Binding.Result.IsComplete && c.Binding.LintErrors == 0;
    if (complete && !all)
    {
        continue;
    }

    incomplete += complete ? 0 : 1;
    var program = c.BuildMir();
    var text = new StringBuilder();
    text.Append("=== ").Append(name).Append(complete ? " [complete]" : " [incomplete]").Append(program.Findings.Count > 0 ? " [validator findings: not analyzed]" : string.Empty).Append('\n');
    foreach (var issue in c.Binding.Issues)
    {
        text.Append("BIND ").Append(issue.Code).Append(" (").Append(issue.Failure).Append(") | ").Append(Line(issue.Node)).Append('\n');
    }

    foreach (var (node, code) in c.Binding.LintErrorRecords)
    {
        text.Append("LINT ").Append(code).Append(" | ").Append(Fn(node)).Append(": ").Append(Line(node)).Append('\n');
    }

    foreach (var issue in c.Ownership.ControlFlow?.Issues ?? [])
    {
        text.Append("CF   ").Append(issue.Code).Append(" | ").Append(Fn(issue.Node)).Append(": ").Append(Line(issue.Node)).Append('\n');
    }

    foreach (var issue in c.Ownership.Issues)
    {
        text.Append("OLD  ").Append(issue.Code).Append(" (").Append(issue.Failure).Append(") | ").Append(Fn(issue.Source)).Append(": ").Append(Line(issue.Source)).Append('\n');
    }

    foreach (var record in program.Records)
    {
        text.Append("REC  ").Append(record.Code).Append(" | ").Append(Show(c, record.Subject)).Append('\n');
    }

    if (Environment.GetEnvironmentVariable("PROBE_DUMP") is not null)
    {
        foreach (var body in program.Bodies)
        {
            if (body.Source.Module == 0)
            {
                text.Append("--- body ").Append(body.Key).Append('\n');
                MirDump.Write(body, text, program.Drops, body.Ordinal);
            }
        }
    }

    var today = Problems(c, program, codes);
    var dropsToday = Drops(c, program);
    var sites = new StringBuilder();
    var flagged = new List<(MirBody Body, int Block, int Index)>();
    foreach (var body in program.Bodies)
    {
        if (body.Source.Module != 0 || !body.HasOpaque)
        {
            continue;
        }

        for (var i = 0; i < body.OpaqueSites.Length; i++)
        {
            var site = body.OpaqueSites[i];
            var node = c.SyntaxOf(site.Source);
            var roots = string.Join(",", body.OpaqueRoots.Slice(site.Start, site.Count).ToArray().Select(r => r < 0 ? "static" : $"_{r}:{body.Locals[r].Name ?? body.Locals[r].Kind.ToString()}"));
            var causes = c.Binding.FailureCauses(node)?.Length ?? 0;
            sites.Append("OPQ  ").Append(Fn(node)).Append(" site ").Append(i).Append(' ').Append(node.GetType().Name).Append(" state=").Append(node.BindingState)
                .Append(" completion=").Append(HirTables.CompletionOf(node)).Append(" causes=").Append(causes).Append(" roots=[").Append(roots).Append("] | ").Append(Line(node)).Append('\n');
        }
    }

    var rewrites = new StringBuilder();
    var a3 = new StringBuilder();
    List<string> model = [];
    if (program.Findings.Count == 0)
    {
        // A3 under the derived rule, on the built body: direct when a runtime path from the failure body's entry reaches its end without
        // passing an undecided Opaque site, derived when only paths through one do.
        foreach (var body in program.Bodies)
        {
            if (body.Source.Module != 0)
            {
                continue;
            }

            foreach (var row in body.MustNotComplete)
            {
                var full = Reaches(body, row.Entry, row.End, null);
                if (!full)
                {
                    continue;
                }

                var flaggedKinds = new SortedSet<string>(StringComparer.Ordinal);
                var direct = Reaches(body, row.Entry, row.End, (block, index) =>
                {
                    var stops = Undecided(c, body, block, index);
                    if (stops)
                    {
                        var st = body.Statements[body.Blocks[block].Start + index];
                        flaggedKinds.Add(Kind(c.SyntaxOf(body.OpaqueSites[st.Rvalue.Arg].Source)));
                    }

                    return stops;
                });
                var guessed = c.Binding.GuessedCompletion(c.SyntaxOf(row.Subject), c.SyntaxOf(row.Subject)) is not null;
                a3Derived += direct ? 0 : 1;
                a3.Append("A3   RequireFallthrough_Kd ").Append(direct ? "direct" : "derived").Append(" (Binding.GuessedCompletion ").Append(guessed ? "rests" : "direct").Append(')')
                    .Append(direct ? string.Empty : " through [" + string.Join(", ", flaggedKinds) + "]").Append(" | ").Append(Show(c, row.Subject)).Append('\n');
            }
        }

        foreach (var body in program.Bodies)
        {
            if (body.Source.Module == 0 && body.HasOpaque && Environment.GetEnvironmentVariable("PROBE_NOREWRITE") is null)
            {
                Rewrite(c, body, rewrites);
            }

            // Unit 2x-E4a simulated: Binding rejects a Move of a part of drop's self (SPEC 16.3.1), so the Move is part of a rejected node,
            // whose Opaque value reads the receiver and moves nothing (the model's rule): each such Move operand becomes a read.
            if (body.Source.Module == 0 && Environment.GetEnvironmentVariable("PROBE_E4A") is not null &&
                c.SyntaxOf(body.Source) is FunctionKoto { IsDestructor: true } && body.Locals.Length > 1)
            {
                ref var operands = ref MemoryMarshal.GetReference(body.Operands);
                for (var i = 0; i < body.Operands.Length; i++)
                {
                    ref var operand = ref Unsafe.Add(ref operands, i);
                    if (operand.Kind == MirOperandKind.Move && operand.Place.Root == 1 && operand.Place.Projection != 0)
                    {
                        rewrites.Append("E4A  Binding would report ExclusivePathTake_Kd at the Move of ").Append(Place(body, operand.Place)).Append('\n');
                        operand = operand with { Kind = MirOperandKind.Copy };
                    }
                }
            }
        }

        new MirAnalysis(c.Types).Run(program);
        model = Problems(c, program, codes);
        var dropsModel = Drops(c, program);
        for (var i = 0; i < dropsToday.Count; i++)
        {
            text.Append("A2   ").Append(dropsToday[i]).Append(i < dropsModel.Count && dropsModel[i] != dropsToday[i] ? "   => under the model: " + dropsModel[i].Trim() : string.Empty).Append('\n');
        }
    }

    var gone = today.Except(model).ToList();
    var added = model.Except(today).ToList();
    foreach (var p in today)
    {
        text.Append("NOW  ").Append(p).Append(gone.Contains(p) ? "   <-- gone under the model" : string.Empty).Append('\n');
    }

    foreach (var p in added)
    {
        text.Append("NEW  ").Append(p).Append("   <-- added under the model\n");
    }

    text.Append(sites).Append(rewrites).Append(a3);
    if (gone.Count + added.Count > 0)
    {
        changed++;
        changedRecords += gone.Count + added.Count;
        foreach (var p in gone)
        {
            summary.Append(name).Append("\tgone\t").Append(p).Append('\n');
        }

        foreach (var p in added)
        {
            summary.Append(name).Append("\tadded\t").Append(p).Append('\n');
        }
    }

    Console.Write(text.ToString());
}

Console.WriteLine($"### programs {programs.Count}, Binding-incomplete {incomplete}, with a changed A1/A2 record {changed}, changed records {changedRecords}, A3 rows derived {a3Derived}");
Console.Write(summary.ToString());

// The source documents of a ledger source (each after a "// <file>.kimi" line), or one document.
static List<(string File, string Text)> Split(string source)
{
    var lines = source.Split('\n');
    if (lines.Length == 0 || !Regex.IsMatch(lines[0], @"^// [\w.\-]+\.kimi$"))
    {
        return [("Program.kimi", source)];
    }

    var docs = new List<(string File, StringBuilder Text)>();
    foreach (var line in lines)
    {
        var m = Regex.Match(line, @"^// ([\w.\-]+\.kimi)$");
        if (m.Success)
        {
            docs.Add((m.Groups[1].Value, new StringBuilder()));
        }
        else
        {
            docs[^1].Text.Append(line).Append('\n');
        }
    }

    return docs.Select(d => (d.File, d.Text.ToString().TrimEnd('\n') + "\n")).ToList();
}

// The Drop verdict lines of the source modules' destructor bodies (MirDump), A2's output.
static List<string> Drops(Compilation c, MirProgram program)
{
    var list = new List<string>();
    foreach (var body in program.Bodies)
    {
        if (body.Source.Module == 0 && c.SyntaxOf(body.Source) is FunctionKoto { IsDestructor: true })
        {
            var dump = new StringBuilder();
            MirDump.Write(body, dump, program.Drops, body.Ordinal);
            list.AddRange(dump.ToString().Split('\n').Where(l => l.TrimStart().StartsWith("drop _", StringComparison.Ordinal)).Select(l => l.Trim()));
        }
    }

    return list;
}

// The A1/A2/A3 problems of the source modules, as text.
static List<string> Problems(Compilation c, MirProgram program, HashSet<DiagnosticCode> codes)
{
    var list = new List<string>();
    foreach (var p in program.Problems.All)
    {
        if (p.Subject.Module == 0 && codes.Contains(p.Code))
        {
            var at = string.Empty;
            foreach (var body in program.Bodies)
            {
                foreach (var site in body.OpaqueSites)
                {
                    at = site.Source == p.Subject ? " @site(" + Kind(c.SyntaxOf(site.Source)) + ")" : at;
                }
            }

            list.Add($"{p.Code} views={(p.Views == ulong.MaxValue ? "all" : "0x" + p.Views.ToString("x"))}{at} | {Show(c, p.Subject)}");
        }
    }

    return list;
}

// The class of an Opaque site's node: an assignment or update (a leaf write), a node containing statements or control flow (non-leaf),
// else a leaf whose roots its resolved children read.
static string Kind(Koto node) => node switch
{
    BinaryKoto { Akind: >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals } and not MemberAccessKoto => "write",
    UnaryKoto { Akind: KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus } => "write",
    CodeBlockKoto or ErrorKoto when node.CodeContext.RecoveryCause(node) is not null => "supplied",
    DoKoto or CodeBlockKoto or IfKoto or MatchKoto or LoopKoto or ForKoto or WhileKoto or FunctionKoto => "non-leaf",
    _ => "leaf " + node.GetType().Name,
};

// Whether a runtime path from a block reaches another, not passing a statement the filter stops at.
static bool Reaches(MirBody body, int from, int to, Func<int, int, bool>? stops)
{
    var seen = new bool[body.Blocks.Length];
    var work = new Stack<int>();
    work.Push(from);
    seen[from] = true;
    while (work.Count > 0)
    {
        var block = work.Pop();
        var data = body.Blocks[block];
        var stopped = false;
        for (var i = 0; stops is not null && i < data.Count && !stopped; i++)
        {
            stopped = stops(block, i);
        }

        if (stopped)
        {
            continue; // The path ends at the undecided site: neither the block's end nor its successors are reached past it.
        }

        if (block == to)
        {
            return true; // The end block's terminator, past its statements.
        }

        for (var e = 0; e < MirUses.Successors(data.Terminator); e++)
        {
            var next = MirUses.Successor(body, data.Terminator, e);
            if (next >= 0 && !seen[next])
            {
                seen[next] = true;
                work.Push(next);
            }
        }
    }

    return false;
}

// An Opaque statement whose site's completion Binding left undecided: an expression Binding did not resolve (Binding.IsGuess's rule).
static bool Undecided(Compilation c, MirBody body, int block, int index)
{
    var statement = body.Statements[body.Blocks[block].Start + index];
    if (statement.Kind != MirStatementKind.Assign || statement.Rvalue.Kind != MirRvalueKind.Opaque)
    {
        return false;
    }

    var node = c.SyntaxOf(body.OpaqueSites[statement.Rvalue.Arg].Source);
    return IsGuess(node);
}

// Binding.IsGuess (Binding.Lints.cs:419), the one guess rule of completion checks: a body the parser supplied for a missing one, or an
// expression Binding did not resolve that is no recovery.
static bool IsGuess(Koto node)
{
    if ((node is CodeBlockKoto || (node is ErrorKoto && IsBodyItem(node))) && node.CodeContext.RecoveryCause(node) is not null)
    {
        return true;
    }

    return !(node.BindingState == BindingState.Resolved || node is not ExpressionKoto || node is ErrorKoto || node.CodeContext.RecoveryCause(node) is not null);
}

static bool IsBodyItem(Koto node) => node.Parent switch
{
    CodeBlockKoto block => block.IsExpressionBody,
    RequireKoto require => require.ElseBody == node,
    MatchKoto match => KotoHelper.IsSelectionBody(match, node),
    _ => false,
};

// Rewrites each Opaque assignment site of a body into the model's form: the site with its read roots, then for a compound update the
// written Place's read, then an Opaque write of each written Place (a Define of its Move Path). A target the probe cannot resolve keeps
// today's form.
static void Rewrite(Compilation c, MirBody body, StringBuilder log)
{
    ref var blocks = ref MemoryMarshal.GetReference(body.Blocks);
    for (var b = 0; b < body.Blocks.Length; b++)
    {
        var data = body.Blocks[b];
        var replaced = new List<MirStatement>();
        var touched = false;
        for (var i = 0; i < data.Count; i++)
        {
            var statement = body.Statements[data.Start + i];
            if (statement.Kind != MirStatementKind.Assign || statement.Rvalue.Kind != MirRvalueKind.Opaque)
            {
                replaced.Add(statement);
                continue;
            }

            var site = body.OpaqueSites[statement.Rvalue.Arg];
            var node = c.SyntaxOf(site.Source);
            var roots = body.OpaqueRoots.Slice(site.Start, site.Count).ToArray();
            Koto? target = null;
            Koto? value = null;
            var compound = false;
            if (node is BinaryKoto { Akind: >= KotoKind.Equals and <= KotoKind.GreaterThanGreaterThanEquals } assignment and not MemberAccessKoto)
            {
                (target, value, compound) = (assignment.Left, assignment.Right, assignment.Akind != KotoKind.Equals);
            }
            else if (node is UnaryKoto { Akind: KotoKind.PrefixPlusPlus or KotoKind.PrefixMinusMinus } step)
            {
                (target, compound) = (step.Operand, true);
            }

            if (target is null)
            {
                replaced.Add(statement);
                continue;
            }

            var writes = new List<MirPlace>();
            var unresolved = new List<string>();
            Targets(body, roots, target, writes, unresolved);
            if (writes.Count == 0)
            {
                log.Append("RW   site ").Append(statement.Rvalue.Arg).Append(" kept (target ").Append(string.Join("; ", unresolved)).Append(") | ").Append(Line(node)).Append('\n');
                replaced.Add(statement);
                continue;
            }

            // Read roots: the roots the value names, the roots of targets without a Move Path, and a compound update's own Place.
            var named = new HashSet<string>(StringComparer.Ordinal);
            Names(value, named);
            foreach (var t in unresolved)
            {
                named.Add(t);
            }

            var reads = roots.Where(r => r < 0 || named.Contains(body.Locals[r].Name ?? "?") || !writes.Any(w => w.Root == r)).ToArray();
            replaced.Add(statement with { Rvalue = MirRvalue.Opaque(statement.Rvalue.Type!, body.AddOpaque(site.Source, reads)) });
            foreach (var w in writes)
            {
                if (compound)
                {
                    replaced.Add(statement with { Rvalue = MirRvalue.Use(body.AddOperand(MirOperand.Copy(w))) });
                }

                replaced.Add(MirStatement.Assign(w, MirRvalue.Opaque(body.TypeOf(w) ?? BoundType.Unit, body.AddOpaque(site.Source, [])), statement.Source));
            }

            touched = true;
            log.Append("RW   site ").Append(statement.Rvalue.Arg).Append(compound ? " update" : " write").Append(" of ").Append(string.Join(", ", writes.Select(w => Place(body, w))))
                .Append(", reads [").Append(string.Join(",", reads.Select(r => r < 0 ? "static" : body.Locals[r].Name))).Append(']')
                .Append(unresolved.Count > 0 ? " (no Move Path: " + string.Join("; ", unresolved) + ")" : string.Empty).Append(" | ").Append(Line(node)).Append('\n');
        }

        if (!touched)
        {
            continue;
        }

        Unsafe.Add(ref blocks, b).Count = 0;
        foreach (var statement in replaced)
        {
            body.Push(b, statement);
        }
    }
}

// The written Places of an assignment target with a Move Path: a local, a stored Field or Tuple element of one, each element of a Tuple
// target; a target through a reference, an index or a computed Property has none (its roots are read).
static void Targets(MirBody body, int[] roots, Koto target, List<MirPlace> writes, List<string> unresolved)
{
    if (target is TupleLiteralKoto tuple)
    {
        foreach (var element in tuple.Elements)
        {
            Targets(body, roots, element, writes, unresolved);
        }

        return;
    }

    if (Resolve(body, roots, target) is { } place)
    {
        writes.Add(place);
    }
    else
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        Names(target, names);
        unresolved.AddRange(names);
    }
}

static MirPlace? Resolve(MirBody body, int[] roots, Koto node)
{
    switch (node)
    {
        case ParenthesizedKoto group:
            return Resolve(body, roots, group.Operand);
        case MemberAccessKoto member:
            if (Resolve(body, roots, member.Left) is not { } prefix || body.TypeOf(prefix) is not { } type)
            {
                return null;
            }

            if (member.Right is NumberLiteralKoto { IsInteger: true } number && type.Kind == BoundTypeKind.Tuple)
            {
                return body.Project(prefix, MirProjectionKind.Field, int.Parse(number.SourceSpelling));
            }

            if (member.Right is IdentifierNameKoto field)
            {
                try
                {
                    var index = AdtDef.IndexOf(type, field.IdentifierName);
                    return body.Project(prefix, MirProjectionKind.Field, index);
                }
                catch (Exception)
                {
                    return null; // A computed Property, or a member of a reference or handle.
                }
            }

            return null;
        case IdentifierNameKoto name when node is not BaseReferenceKoto:
            foreach (var root in roots)
            {
                if (root >= 0 && body.Locals[root].Name == name.IdentifierName)
                {
                    return new(root);
                }
            }

            if (name.IdentifierName == "self" && body.Locals.Length > 0 && body.Locals[0].Kind == MirLocalKind.Return && roots.Contains(0))
            {
                return new(0);
            }

            return null;
        default:
            return null;
    }
}

static void Names(Koto? node, HashSet<string>? names)
{
    if (node is null || names is null)
    {
        return;
    }

    if (node is IdentifierNameKoto name)
    {
        names.Add(name.IdentifierName);
    }

    foreach (var child in Children(node))
    {
        Names(child, names);
    }
}

static IEnumerable<Koto> Children(Koto node)
{
    var list = new List<Koto>();
    node.VisitChildren(new Collector(list));
    return list;
}

static string Place(MirBody body, MirPlace place)
{
    var text = new StringBuilder();
    var steps = new List<string>();
    for (var id = place.Projection; id > 0; id = body.Projections[id].Prefix)
    {
        steps.Insert(0, "." + body.Projections[id].Kind + body.Projections[id].Arg);
    }

    return "_" + place.Root + ":" + (body.Locals[place.Root].Name ?? body.Locals[place.Root].Kind.ToString()) + string.Concat(steps);
}

static string Show(Compilation c, SourceRef site) => (site.Module == 0 ? string.Empty : "[lib] ") + Fn(c.SyntaxOf(site)) + ": " + Line(c.SyntaxOf(site));

static string Line(Koto node) => node.ToString()?.Split('\n')[0].Trim() ?? "?";

static string Fn(Koto node) => node is FunctionKoto function ? function.Name?.ToString() ?? "?" : node.Parent is { } parent ? Fn(parent) : "-";

internal sealed class Collector(List<Koto> list) : KotoVisitor
{
    public override void Visit(Koto node) => list.Add(node);
}
