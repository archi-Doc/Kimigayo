// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;
using System.Text.Json;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>The semantic snapshot of the compiler reduction (docs/dev/COMPILER_ARCHITECTURE.md, R2a): the Binding facts of every
/// syntax node and the published records over the Kimi library, the milestone programs and the snippets of <c>tests/diagnostics</c>,
/// so a structural change shows that it leaves every semantic result unchanged.</summary>
/// <remarks><c>KIMI_SEMANTIC_SNAPSHOT=write</c> writes one file per case into the directory <c>KIMI_SEMANTIC_BASELINE</c>;
/// <c>check</c> compares the current snapshot with that directory and, when <c>KIMI_SEMANTIC_OUTPUT</c> names a directory, writes the
/// current snapshot and its differences there. Without the variable nothing is requested and the test does nothing.</remarks>
public sealed class SemanticSnapshotTest
{
    [Fact]
    public void SnapshotsAreDeterministic()
    {
        string[] names = ["milestone/Milestone1", "pair/" + PairCases()[0].Name];
        var first = names.Select(Take).ToArray();
        Assert.Equal(first, names.Select(Take));
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
        var names = CaseNames();
        var texts = new string[names.Length];
        Parallel.For(0, names.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, i => texts[i] = Take(names[i]));
        if (mode == "write")
        {
            Write(baseline, names, texts);
            return;
        }

        var output = Environment.GetEnvironmentVariable("KIMI_SEMANTIC_OUTPUT");
        if (!string.IsNullOrEmpty(output))
        {
            Write(output, names, texts);
        }

        var differences = new List<string>();
        var expected = Directory.GetFiles(baseline, "*.txt", SearchOption.AllDirectories)
            .Select(x => Path.GetRelativePath(baseline, x).Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i < names.Length; i++)
        {
            var file = FileName(names[i]);
            if (!expected.Remove(file))
            {
                differences.Add($"{names[i]}: added");
                continue;
            }

            var before = File.ReadAllText(Path.Combine(baseline, file)).Split('\n');
            var after = texts[i].Split('\n');
            var line = 0;
            while (line < before.Length && line < after.Length && before[line] == after[line])
            {
                line++;
            }

            if (line < before.Length || line < after.Length)
            {
                differences.Add($"{names[i]}:{line + 1}: {(line < before.Length ? before[line] : "<end>")} -> {(line < after.Length ? after[line] : "<end>")}");
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

    private static void Write(string directory, string[] names, string[] texts)
    {
        for (var i = 0; i < names.Length; i++)
        {
            var path = Path.Combine(directory, FileName(names[i]));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, texts[i], new UTF8Encoding(false));
        }
    }

    // The library case binds an empty program and prints the library tree; every other case prints its own program only.
    private static string Take(string name)
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

        var printer = new SemanticPrinter();
        try
        {
            var c = Compilation.CreateForTest();
            Assert.True(c.Prepare(WindowsProfile.Target));
            c.Kotonoha.AddSource(new SourceDocument("Program.kimi", source));
            c.Bind();
            c.Binding.CheckStartup(OutputKind.Application);
            c.Ownership.Analyze();
            printer.Print(c, kind == "library" ? c.Library.Kotonoha : c.Kotonoha);
        }
        catch (Exception exception)
        {
            printer.Fail(exception);
        }

        return printer.ToString();
    }

    /// <summary>One row of <c>tests/diagnostics/pair-cases.json</c>; only its name and source are read here.</summary>
    /// <param name="Name">The case name.</param>
    /// <param name="Source">The source text.</param>
    private sealed record PairCaseRow(string Name, string Source);

    /// <summary>Prints one line per node in pre-order and then the published records. Every fact the snapshot compares is read here,
    /// so a change of where the compiler stores a fact changes only this printer.</summary>
    private sealed class SemanticPrinter : KotoVisitor
    {
        private readonly StringBuilder text = new();
        private readonly Dictionary<object, string> types = new(ReferenceEqualityComparer.Instance);
        private int depth;
        private Koto? generated;

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
            var (binding, startup, ownership) = (compilation.Binding.Result, compilation.Binding.Startup, compilation.Ownership.Result);
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
            var text = this.text;
            text.Append(this.depth).Append(' ').Append(Location(node)).Append('+').Append(node.Span.Length).Append(' ').Append(node.Akind);
            if (node.BindingState != BindingState.Unvisited)
            {
                text.Append(' ').Append(node.BindingState);
            }

            if (node.BindingFailure != BindingFailure.None)
            {
                text.Append('/').Append(node.BindingFailure);
            }

            if (node.BoundMeaning is BoundType type)
            {
                text.Append(" t=").Append(this.Type(type));
            }
            else if (node.BoundMeaning is BoundOrigin origin)
            {
                text.Append(" o=").Append(Origin(origin));
            }

            if (node.BoundSymbol is { } symbol)
            {
                text.Append(" s=").Append(Symbol(symbol));
            }

            if (node.ErasedFunctionType is { } erased)
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
                text.Append(" conversion=").Append(conversion.ConversionBinding);
                this.Call(" creation", conversion.CreationCall);
                if (conversion.FoldedConstant is { } folded)
                {
                    text.Append(" folded=").Append(folded.ToString(CultureInfo.InvariantCulture));
                }
            }

            if (node is IsKoto test)
            {
                text.Append(" constraint=").Append(test.BoundConstraint?.Kind.ToString() ?? "-");
                if (test.IsRuntimeTest)
                {
                    text.Append(" runtime").Append(test.IsNegated ? "-not" : string.Empty).Append(test.BoundRuntimeTest is null ? string.Empty : "-bound");
                }
            }

            if (node is MemberAccessKoto { IsDirectStorage: true })
            {
                text.Append(" direct");
            }

            if (node is ForKoto { Iteration: { } iteration })
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
            if (invocation.BoundCall is { } call)
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
            else if (invocation.BoundValueCall is { } value)
            {
                text.Append(label).Append("=value ").Append(this.Type(value.Signature)).Append(" receiver=").Append(value.ReceiverKind);
            }
        }
    }
}
