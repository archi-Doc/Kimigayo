// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;
using System.Text.Json;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>The semantic snapshot of the compiler reduction (docs/dev/COMPILER_ARCHITECTURE.md, R2a): the Binding facts of every
/// syntax node and the published records over the Kimi library, the milestone programs and the snippets of <c>tests/diagnostics</c>,
/// so a structural change shows that it leaves every semantic result unchanged.</summary>
/// <remarks><c>KIMI_SEMANTIC_SNAPSHOT=write</c> writes one file per case into the directory <c>KIMI_SEMANTIC_BASELINE</c>;
/// <c>check</c> compares the current snapshot with that directory and, when <c>KIMI_SEMANTIC_OUTPUT</c> names a directory, writes the
/// current snapshot and its differences there. Without the variable nothing is requested and the test does nothing.
/// <c>KIMI_SEMANTIC_HIR=1</c> publishes the HIR columns before the ownership analysis and adds a <c>.hir.txt</c> file per case: one
/// line per node with its id, Op, Res, a jump's target, its completion, the Adjust its consumer gives it, its Place and its expansion
/// (R2b). <c>KIMI_SEMANTIC_MIR=1</c> builds the MIR before the ownership analysis and adds a <c>.mir.txt</c> file per case: the MIR dump of
/// its module's bodies, construction records and validator findings (R3).</remarks>
public sealed class SemanticSnapshotTest
{
    [Fact]
    public void SnapshotsAreDeterministic()
    {
        string[] names = ["milestone/Milestone1", "pair/" + PairCases()[0].Name];
        var first = names.Select(static x => Take(x, false).Text).ToArray();
        Assert.Equal(first, names.Select(static x => Take(x, false).Text));
        Assert.All(first, static x => Assert.Contains(" Resolved t=", x, StringComparison.Ordinal));
    }

    [Fact]
    public void RequestedSnapshotMatchesTheBaseline()
    {
        var mode = Environment.GetEnvironmentVariable("KIMI_SEMANTIC_SNAPSHOT");
        if (string.IsNullOrEmpty(mode))
        {
            return;
        }

        Assert.True(mode is "write" or "check", $"KIMI_SEMANTIC_SNAPSHOT is write or check, not '{mode}'.");
        var baseline = Environment.GetEnvironmentVariable("KIMI_SEMANTIC_BASELINE");
        Assert.False(string.IsNullOrEmpty(baseline), "KIMI_SEMANTIC_BASELINE names the baseline directory.");
        var hir = Environment.GetEnvironmentVariable("KIMI_SEMANTIC_HIR") == "1";
        var mir = Environment.GetEnvironmentVariable("KIMI_SEMANTIC_MIR") == "1";
        var names = CaseNames();
        var snapshots = new (string Text, string Hir, string Mir)[names.Length];
        Parallel.For(0, names.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, i => snapshots[i] = Take(names[i], hir, mir));
        var files = names.Select(static (x, i) => (File: FileName(x), Index: i, Kind: 0))
            .Concat(hir ? names.Select(static (x, i) => (File: FileName(x)[..^4] + ".hir.txt", Index: i, Kind: 1)) : [])
            .Concat(mir ? names.Select(static (x, i) => (File: FileName(x)[..^4] + ".mir.txt", Index: i, Kind: 2)) : []).ToArray();
        if (mode == "write")
        {
            Write(baseline, files, snapshots);
            return;
        }

        var output = Environment.GetEnvironmentVariable("KIMI_SEMANTIC_OUTPUT");
        if (!string.IsNullOrEmpty(output))
        {
            Write(output, files, snapshots);
        }

        var differences = new List<string>();
        var expected = Directory.GetFiles(baseline, "*.txt", SearchOption.AllDirectories)
            .Select(x => Path.GetRelativePath(baseline, x).Replace('\\', '/'))
            .Where(x => (hir || !x.EndsWith(".hir.txt", StringComparison.Ordinal)) && (mir || !x.EndsWith(".mir.txt", StringComparison.Ordinal))).ToHashSet(StringComparer.Ordinal);
        foreach (var (file, index, kind) in files)
        {
            if (!expected.Remove(file))
            {
                differences.Add($"{file}: added");
                continue;
            }

            var before = File.ReadAllText(Path.Combine(baseline, file)).Split('\n');
            var after = Of(snapshots[index], kind).Split('\n');
            var line = 0;
            while (line < before.Length && line < after.Length && before[line] == after[line])
            {
                line++;
            }

            if (line < before.Length || line < after.Length)
            {
                differences.Add($"{file}:{line + 1}: {(line < before.Length ? before[line] : "<end>")} -> {(line < after.Length ? after[line] : "<end>")}");
            }
        }

        differences.AddRange(expected.Order(StringComparer.Ordinal).Select(static x => $"{x}: removed"));
        if (!string.IsNullOrEmpty(output))
        {
            File.WriteAllLines(Path.Combine(output, "differences.txt"), differences, new UTF8Encoding(false));
        }

        Assert.True(differences.Count == 0, $"{differences.Count} of {names.Length} cases differ:\n{string.Join('\n', differences.Take(50))}");
    }

    /// <summary>Lists the corpus: the library, every milestone program and every mutation, syntax and pair case of <c>tests/diagnostics</c>.</summary>
    /// <returns>The case names.</returns>
    internal static string[] CaseNames()
    {
        var milestones = Directory.GetFiles(DiagnosticCorpus.RepositoryPath("tests", "milestones"), "Milestone*.kimi")
            .Select(static x => "milestone/" + Path.GetFileNameWithoutExtension(x));
        var mutations = DiagnosticCorpus.Mutations.Select(static x => "mutation/" + x.Name);
        var syntax = DiagnosticCorpus.SyntaxCases.Select(static x => "syntax/" + x.Name);
        var pairs = PairCases().Select(static x => "pair/" + x.Name);
        string[] names = ["library", .. milestones.Concat(mutations).Concat(syntax).Concat(pairs).Order(StringComparer.Ordinal)];
        Assert.Equal(names.Length, names.Select(FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        return names;
    }

    /// <summary>Binds a case of the corpus, as every snapshot does: the library case binds an empty program.</summary>
    /// <param name="name">The case name.</param>
    /// <returns>The bound compilation and the module the case prints: the library for the library case, else the program.</returns>
    internal static (Compilation Compilation, Kotonoha Module) Bind(string name)
    {
        var slash = name.IndexOf('/');
        var kind = slash < 0 ? name : name[..slash];
        var subject = name[(slash + 1)..];
        var source = kind switch
        {
            "library" => string.Empty,
            "milestone" => File.ReadAllText(DiagnosticCorpus.RepositoryPath("tests", "milestones", subject + ".kimi")),
            "mutation" => DiagnosticCorpus.Apply(DiagnosticCorpus.Mutation(subject)),
            "syntax" => DiagnosticCorpus.Syntax(subject).Source,
            _ => PairCases().Single(x => x.Name == subject).Source,
        };

        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Program.kimi", source));
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        return (c, kind == "library" ? c.Library.Kotonoha : c.Kotonoha);
    }

    /// <summary>Takes the snapshot of a case; with <paramref name="hir"/> the HIR columns are published before the ownership analysis
    /// and printed too, and with <paramref name="mir"/> the MIR is built before it and dumped.</summary>
    /// <param name="name">The case name.</param>
    /// <param name="hir">Whether to publish and print the HIR columns.</param>
    /// <param name="mir">Whether to build and dump the MIR.</param>
    /// <returns>The semantic snapshot, the HIR snapshot and the MIR dump (each empty when not requested).</returns>
    internal static (string Text, string Hir, string Mir) Take(string name, bool hir, bool mir = false)
    {
        var printer = new SemanticPrinter(hir);
        var dump = new StringBuilder();
        try
        {
            var (c, module) = Bind(name);
            if (hir)
            {
                HirTables.Publish(c);
            }

            var program = mir ? c.BuildMir() : null;
            c.Analyze();
            if (program is not null)
            {
                MirDump.Write(program, module.Syntax.Module, dump);
            }

            printer.Print(c, module);
        }
        catch (Exception exception)
        {
            printer.Fail(exception);
        }

        return (printer.ToString(), printer.Hir, dump.ToString());
    }

    private static PairCaseRow[] PairCases()
        => JsonSerializer.Deserialize<PairCaseRow[]>(File.ReadAllText(DiagnosticCorpus.RepositoryPath("tests", "diagnostics", "pair-cases.json")))!;

    private static string FileName(string name)
    {
        var builder = new StringBuilder(name.Length + 4);
        foreach (var c in name)
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '/' ? c : '_');
        }

        return builder.Append(".txt").ToString();
    }

    private static void Write(string directory, (string File, int Index, int Kind)[] files, (string Text, string Hir, string Mir)[] snapshots)
    {
        foreach (var (file, index, kind) in files)
        {
            var path = Path.Combine(directory, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Of(snapshots[index], kind), new UTF8Encoding(false));
        }
    }

    // A case's semantic snapshot (kind 0), HIR snapshot (1) or MIR dump (2).
    private static string Of((string Text, string Hir, string Mir) snapshot, int kind) => kind switch { 0 => snapshot.Text, 1 => snapshot.Hir, _ => snapshot.Mir };

    /// <summary>One row of <c>tests/diagnostics/pair-cases.json</c>; only its name and source are read here.</summary>
    /// <param name="Name">The case name.</param>
    /// <param name="Source">The source text.</param>
    private sealed record PairCaseRow(string Name, string Source);

    /// <summary>Prints one line per node in pre-order and then the published records. Every fact the snapshot compares is read here,
    /// so a change of where the compiler stores a fact changes only this printer.</summary>
    private sealed class SemanticPrinter(bool hir) : KotoVisitor
    {
        private readonly StringBuilder text = new();
        private readonly StringBuilder? hir = hir ? new() : null;
        private readonly Dictionary<object, string> types = new(ReferenceEqualityComparer.Instance);
        private int depth;
        private Koto? generated;

        internal string Hir => this.hir?.ToString() ?? string.Empty;

        public override void Visit(Koto node)
        {
            if (ReferenceEquals(node, this.generated))
            {
                this.generated = null;
            }

            this.Node(node);
            this.depth++;
            base.Visit(node);
            this.depth--;
        }

        public override string ToString()
            => this.text.ToString();

        // The tree, the outcome of each phase, the problems Binding, control flow and ownership retain, and the published records.
        internal void Print(Compilation compilation, Kotonoha kotonoha)
        {
            // The generated function of top-level statements is printed once: where the tree holds it, or else after the tree.
            this.generated = kotonoha.GeneratedFunction;
            this.Visit(kotonoha.RootKoto);
            if (this.generated is { } generated)
            {
                this.text.Append("# generated\n");
                this.Visit(generated);
            }

            var text = this.text;
            var (binding, startup, ownership) = (compilation.Binding.Result, compilation.Binding.Startup, compilation.Analysis);
            text.Append("= binding ").Append(binding.Mode).Append(' ').Append(binding.ResolvedCount).Append('/').Append(binding.UnresolvedCount).Append('/').Append(binding.InvalidCount)
                .Append(" startup ").Append(startup.Kind).Append(' ').Append(startup.IsComplete).Append(' ').Append(Location(startup.Function))
                .Append(" ownership ").Append(ownership.IsVerified).Append(' ').Append(ownership.BodyCount).Append('/').Append(ownership.ErrorCount).Append('\n');
            foreach (var issue in compilation.Binding.Issues)
            {
                text.Append("? binding ").Append(issue.Code).Append(' ').Append(issue.Failure).Append(' ').Append(Location(issue.Node)).Append('+').Append(issue.Node.Span.Length).Append('\n');
            }

            foreach (var issue in compilation.Ownership.ControlFlow?.Issues ?? [])
            {
                text.Append("? flow ").Append(issue.Code).Append(' ').Append(Location(issue.Node)).Append('+').Append(issue.Node.Span.Length).Append('\n');
            }

            foreach (var issue in compilation.Ownership.Issues)
            {
                text.Append("? ownership ").Append(issue.Code).Append(' ').Append(issue.Failure).Append(' ').Append(Location(issue.Source)).Append('+').Append(issue.Source.Span.Length).Append('\n');
            }

            foreach (var diagnostic in TestDiagnostics.Of(compilation))
            {
                text.Append("! ").Append(diagnostic).Append('\n');
            }
        }

        internal void Fail(Exception exception)
            => this.text.Append("exception ").Append(exception.GetType().Name).Append(": ").Append(exception.Message.Split('\n')[0]).Append('\n');

        private static string Document(Koto? node)
        {
            var path = node?.CodeContext?.SourceDocument?.Path;
            return path is null ? "?" : path.StartsWith("compiler://", StringComparison.Ordinal) ? "Kimi/" + path[(path.LastIndexOf('/') + 1)..] : path;
        }

        private static string Location(Koto? node)
            => node is null ? "?" : Document(node) + ":" + node.Span.Start;

        private static string Symbol(BindingSymbol? symbol)
            => symbol is null ? "-" : $"{symbol.Kind}:{symbol.Name}@{Location(symbol.Declaration)}";

        private static string Length(BoundLength? length)
            => length is null ? "_" : length.IsConstant ? length.Value.ToString(CultureInfo.InvariantCulture) :
                length.Parameter is { } parameter ? "$" + parameter.Name + "@" + Location(parameter.Declaration) : $"({Length(length.Left)} {length.Operation} {Length(length.Right)})";

        private static string Origin(BoundOrigin origin)
        {
            var builder = new StringBuilder();
            builder.Append(origin.Kind);
            if (origin.Name != origin.Kind.ToString())
            {
                builder.Append(':').Append(origin.Name);
            }

            if (origin.Binder is { } binder)
            {
                builder.Append('@').Append(Location(binder)).Append('#').Append(origin.Slot);
            }

            if (origin.Operands.Count != 0)
            {
                builder.Append('(').AppendJoin('&', origin.Operands.Select(Origin)).Append(')');
            }

            return builder.ToString();
        }

        private string Type(BoundType? type)
        {
            if (type is null)
            {
                return "-";
            }

            if (this.types.TryGetValue(type, out var known))
            {
                return known;
            }

            var builder = new StringBuilder();
            if (type.Kind != BoundTypeKind.Primitive)
            {
                builder.Append(type.Kind).Append(':');
            }

            builder.Append(type.Name);
            if (type.Kind != BoundTypeKind.Primitive && type.Symbol is { } symbol)
            {
                builder.Append('@').Append(Location(symbol.Declaration));
            }

            if (type.Semantics != SemanticsKind.Owner)
            {
                builder.Append('/').Append(type.Semantics);
            }

            if (type.ResultMode != FunctionResultMode.Value)
            {
                builder.Append('!').Append(type.ResultMode);
            }

            if (type.Components.Count != 0)
            {
                builder.Append('<').AppendJoin(',', type.Components.Select(this.Type)).Append('>');
            }

            if (type.Length != 0 || type.LengthExpression is not null)
            {
                builder.Append('[').Append(type.LengthExpression is { } expression ? Length(expression) : type.Length.ToString(CultureInfo.InvariantCulture)).Append(']');
            }

            if (type.LengthArguments.Length != 0)
            {
                builder.Append("[[").AppendJoin(',', type.LengthArguments.Select(Length)).Append("]]");
            }

            if (type.Origin is { } origin)
            {
                builder.Append(" @").Append(Origin(origin));
            }

            if (type.OriginArguments.Count != 0)
            {
                builder.Append('{').AppendJoin(',', type.OriginArguments.Select(Origin)).Append('}');
            }

            known = builder.ToString();
            this.types.Add(type, known);
            return known;
        }

        private void Node(Koto node)
        {
            if (this.hir is { } hir)
            {
                hir.Append(node.SyntaxId).Append(' ').Append(HirTables.Op(node));
                if (HirTables.ResOf(node) is not HirRes.None and var res)
                {
                    hir.Append(' ').Append(res);
                }

                if (HirTables.Op(node) is HirOp.Jump or HirOp.Continue && HirTables.PlanOf(node).Plan is Koto target)
                {
                    hir.Append(" ->").Append(target.SyntaxId);
                }

                if (HirTables.CompletionOf(node) is not HirCompletion.Unset and var completion)
                {
                    hir.Append(' ').Append(completion);
                }

                if (HirTables.AdjustOf(node) is { Acquire: not HirAcquire.Unset } adjust)
                {
                    hir.Append(" @").Append(adjust.Acquire);
                    if (adjust.Adaptation != HirAdaptation.None)
                    {
                        hir.Append('/').Append(adjust.Adaptation);
                    }
                }

                if (HirTables.PlaceOf(node) is { Kind: not HirPlaceKind.Unset } place)
                {
                    hir.Append(" ^").Append(place.Kind);
                    if (place.Layers != 0)
                    {
                        hir.Append('+').Append(place.Layers);
                    }
                }

                if (ExpansionPlan.Of(node) is { } expansion)
                {
                    hir.Append(" !").Append(expansion.Kind);
                    if (expansion.First is not null || expansion.Second is not null)
                    {
                        hir.Append(':').Append(expansion.First?.Target.Name).Append('/').Append(expansion.Second?.Target.Name);
                    }

                    hir.Append(expansion.Evaluated >= 0 ? "#" + expansion.Evaluated : string.Empty).Append(expansion.Direct ? "*" : string.Empty);
                }

                hir.Append('\n');
            }

            var text = this.text;
            text.Append(this.depth).Append(' ').Append(Location(node)).Append('+').Append(node.Span.Length).Append(' ').Append(node.Akind);
            if (node.StateOf() != BindingState.Unvisited)
            {
                text.Append(' ').Append(node.StateOf());
            }

            if (node.FailureOf() != BindingFailure.None)
            {
                text.Append('/').Append(node.FailureOf());
            }

            if (node.TypeOf() is { } type)
            {
                text.Append(" t=").Append(this.Type(type));
            }
            else if (node.OriginOf() is { } origin)
            {
                text.Append(" o=").Append(Origin(origin));
            }

            if (node.SymbolOf() is { } symbol)
            {
                text.Append(" s=").Append(Symbol(symbol));
            }

            if (node.ErasedTypeOf() is { } erased)
            {
                text.Append(" erased=").Append(this.Type(erased));
            }

            if (node.FormattingStorage is { } formatting)
            {
                text.Append(" format=").Append(formatting.Active);
            }

            if (node is InvocationKoto invocation)
            {
                this.Call(" call", invocation);
            }

            if (node is BinaryKoto binary)
            {
                this.Call(" arithmetic", binary.ArithmeticCall);
                this.Call(" comparison", binary.ComparisonCall);
            }

            if (node is UnaryKoto unary)
            {
                this.Call(" arithmetic", unary.ArithmeticCall);
            }

            if (node is ConversionKoto conversion)
            {
                text.Append(" conversion=").Append(conversion.ConversionOf().Kind);
                this.Call(" creation", conversion.ConversionOf().Creation);
                if (conversion.ConversionOf().Folded is { } folded)
                {
                    text.Append(" folded=").Append(folded.ToString(CultureInfo.InvariantCulture));
                }
            }

            if (node is IsKoto test)
            {
                text.Append(" constraint=").Append(test.ConstraintOf()?.Kind.ToString() ?? "-");
                if (test.IsRuntimeTest)
                {
                    text.Append(" runtime").Append(test.IsNegated ? "-not" : string.Empty).Append(test.RuntimeTestOf() is null ? string.Empty : "-bound");
                }
            }

            if (node is MemberAccessKoto { IsDirectStorage: true })
            {
                text.Append(" direct");
            }

            if (node is ForKoto loop && loop.IterationOf().Plan is { } iteration)
            {
                this.Call(" next", iteration.Next);
            }

            if (node is FunctionKoto { ClosureStorage: { } closure })
            {
                text.Append(" closure=").Append(this.Type(closure.Signature)).Append(" environment=").Append(this.Type(closure.EnvironmentType))
                    .Append(" receiver=").Append(closure.Receiver).Append(" captures=").Append(closure.Captures.Count);
            }

            if (node is ArrayLiteralKoto { FillCount: { } fill })
            {
                text.Append(" fill=").Append(Length(fill));
            }

            if (node is TryKoto { SemanticsIndexed: true })
            {
                text.Append(" indexed");
            }

            text.Append('\n');
        }

        // The call facts: callee kind, target, result Type and mode, Type and length arguments, and the argument mapping.
        private void Call(string label, InvocationKoto? invocation)
        {
            if (invocation is null)
            {
                return;
            }

            var text = this.text;
            if (invocation.CallOf() is { } call)
            {
                text.Append(label).Append('=').Append(call.Kind == CalleeKind.Virtual ? call.VirtualIsDirect ? "virtual-direct" : "virtual" : "direct")
                    .Append(' ').Append(Symbol(call.Target)).Append(" -> ").Append(this.Type(call.ReturnType));
                if (call.ResultMode != FunctionResultMode.Value)
                {
                    text.Append('!').Append(call.ResultMode);
                }

                if (call.TypeArguments.Length != 0)
                {
                    text.Append(" types=[").AppendJoin(',', call.TypeArguments.ToArray().Select(this.Type)).Append(']');
                }

                if (call.LengthArguments.Length != 0)
                {
                    text.Append(" lengths=[").AppendJoin(',', call.LengthArguments.ToArray().Select(Length)).Append(']');
                }

                text.Append(" map=[").AppendJoin(',', call.ArgumentToParameter.ToArray().Select(static x => x.ToString(CultureInfo.InvariantCulture))).Append(']');
                if (invocation.Method is RequirementCalleeKoto requirement)
                {
                    text.Append(" requirement=").Append(Symbol(requirement.RequirementStorage?.Target)).Append(" implementation=").Append(Symbol(requirement.ImplementationStorage?.Target));
                }
            }
            else if (invocation.ValueCallOf() is { } value)
            {
                text.Append(label).Append("=value ").Append(this.Type(value.Signature)).Append(" receiver=").Append(value.ReceiverKind);
            }
        }
    }
}
