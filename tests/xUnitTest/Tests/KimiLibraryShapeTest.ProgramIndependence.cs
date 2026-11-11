// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;
using Printer = XunitTest.SemanticSnapshotTest.SemanticPrinter;

namespace XunitTest;

// R8-L1 U1 (design §2 C9, §5 (a), M-B3): the library is a compiler-build constant (SPEC 22.1). Each row is a fresh compilation's check
// (Compilation.Check: the Final Bind, the startup check, then Analyze: the MIR's construction, validation, A1-A5 and the collector),
// compared with the empty program's check, fact by fact: the library table's parsed count; every parsed node's facts and HIR row as the
// semantic snapshot prints them; every library declaration's own symbol state; the library's scopes; the catalog; the library imports;
// and the library's MIR dump. Not compared:
// - the Type interners (TyCtxt), which hold the program's instantiations by design (C7); F-B3's identity test (U3+U4) guards them;
// - the declaration query states, valid only under Binding's private pass stamp; U2's Frozen stamp (F-B2) pins them;
// - the library bodies' A4 summaries and Field numbers and the checked set, program-ordered by design (C8 rev. 1; U5b, U5c).
public partial class KimiLibraryShapeTest
{
    private const string Milestone31 = "tests/milestones/Milestone31.kimi";

    // The empty program's check, the reference of every row; not the Library compilation the other tests read, which is bound only.
    private static readonly Lazy<Compilation> EmptyProgram = new(() => Check(Compilation.CreateForTest(), string.Empty, WindowsProfile.Target));

    private static readonly Lazy<(string Fact, string[] Lines)[]> EmptyProgramFacts = new(() => Facts(EmptyProgram.Value));

    // The empty program again (determinism), the census's and the MIR review's programs (Milestones 27 and 30 renumber synthesized ids),
    // and suite programs that use the library widely: Dictionary (Milestones 31, 32, 35), ranges, Array and Option (Milestone35, Ranges)
    // and Text formatting (Utf8Formatting).
    [Theory]
    [InlineData("")]
    [InlineData("docs/examples/Hello/Hello.kimi")]
    [InlineData("tests/milestones/Milestone26.kimi")]
    [InlineData("tests/milestones/Milestone27.kimi")]
    [InlineData("tests/milestones/Milestone30.kimi")]
    [InlineData(Milestone31)]
    [InlineData("tests/milestones/Milestone32.kimi")]
    [InlineData("tests/milestones/Milestone35.kimi")]
    [InlineData("docs/examples/Ranges/Ranges.kimi")]
    [InlineData("docs/examples/Utf8Formatting/Utf8Formatting.kimi")]
    public void TheLibraryIsIndependentOfTheProgram(string program)
    {
        var c = Check(Compilation.CreateForTest(), program, WindowsProfile.Target);
        SameAsTheEmptyPrograms(c);
        SynthesizedNodes.KnownDifference(program, c);
    }

    // The inputs a compilation gives besides the program: C9's test build (Binding) and target (the frozen library's key, design §4, so a
    // library that came to read it would move this row to a comparison per target), and C11's parse-time inputs: the compile-time
    // variables (Debug; Parser.Directives) and the documentation and hover collection, which the LSP sets together (Project.cs).
    [Theory]
    [InlineData("test build")]
    [InlineData("32-bit target")]
    [InlineData("compile-time variables")]
    [InlineData("documentation and hover")]
    public void TheLibraryIsIndependentOfTheInputs(string input)
    {
        var (c, target) = (Compilation.CreateForTest(), WindowsProfile.Target);
        switch (input)
        {
            case "test build":
                c.IsTestBuild = true;
                break;
            case "32-bit target":
                target = "i686-unknown-linux-gnu";
                break;
            case "compile-time variables":
                c.Project.KimiOptions.Debug = true;
                break;
            case "documentation and hover":
                (c.CollectDocumentation, c.CollectHover) = (true, true);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(input), input, "No such input row.");
        }

        SameAsTheEmptyPrograms(Check(c, Milestone31, target));
    }

    // C9's module inputs: DefaultAliases, Configuration and References are keyed by module, and the library's is never a key
    // (Compilation.Modules.cs: PrepareModules keys the graph's modules only), so the library reads the required aliases, no configuration
    // and no references whatever the root declares. Here the root declares an alias, a native requirement and a module reference.
    [Fact]
    public void TheLibraryHasNoModuleInputs()
    {
        var source = Repository.ReadText(Milestone31.Split('/'));
        var c = Checked(ModuleBindingTest.Create(source, "public group Api\n    public func value() => ()", configure: static (root, _) =>
        {
            root.Alias = ["Lib.Api"];
            root.NativeRequirements[WindowsProfile.Target] = new(StringComparer.Ordinal) { ["codec"] = new() { Kind = "static" } };
        }));
        Assert.Contains("Lib.Api", c.DefaultAliases(c.Kotonoha));
        Assert.NotNull(c.Configuration(c.Kotonoha)!.NativeRequirements[WindowsProfile.Target]["codec"]);
        Assert.NotNull(c.References(c.Kotonoha));
        var library = c.Library.Kotonoha;
        Assert.Equal(["Kimi"], c.DefaultAliases(library));
        Assert.Null(c.Configuration(library));
        Assert.Null(c.References(library));
        SameAsTheEmptyPrograms(c);
    }

    // M-N4, R2-N5: the library declares no parameter default, for a function or an `init`, so after its Final Bind its published HIR holds
    // no default and its MIR no default template. Temporary: a frozen library would have to freeze a template's omission facts (Recursive,
    // Failed, Reads), which the builder's omission graph reads; removal: U5b or a later unit freezes them, and this pin goes.
    [Fact]
    public void TheLibraryDeclaresNoParameterDefault()
    {
        var c = EmptyProgram.Value;
        var functions = KotoTree.Walk(c.Library.Kotonoha.RootKoto).OfType<FunctionKoto>().ToArray();
        Assert.Contains(functions, static x => x.IsConstructor);
        Assert.Empty(functions.Where(static x => x.Parameters.Any(static y => y.DefaultValue is not null)).Select(Line));
        Assert.Empty(c.Library.Kotonoha.Syntax.Hir.Defaults!);
        Assert.DoesNotContain(c.ConstructMir().Bodies.ToArray(), static x => x.Source.IsLibrary && x.Key.Kind == MirBodyKind.DefaultTemplate);
    }

    // A program read from the repository, checked.
    private static Compilation Check(Compilation c, string program, string target)
    {
        Assert.True(c.Prepare(target));
        c.Kotonoha.AddSource(new SourceDocument("Program.kimi", program.Length == 0 ? string.Empty : Repository.ReadText(program.Split('/'))));
        return Checked(c);
    }

    // A compilation's check, complete and verified; the empty program is no Application, and its check analyzes all the same (SPEC 23.1.2).
    private static Compilation Checked(Compilation c)
    {
        c.Check();
        Assert.True(c.Binding.Result.IsComplete && c.Analysis.IsVerified, string.Join('\n', TestDiagnostics.Of(c)));
        return c;
    }

    private static void SameAsTheEmptyPrograms(Compilation c)
    {
        foreach (var ((fact, expected), (_, actual)) in EmptyProgramFacts.Value.Zip(Facts(c)))
        {
            var line = 0;
            while (line < expected.Length && line < actual.Length && expected[line] == actual[line])
            {
                line++;
            }

            var (before, after) = (line < expected.Length ? expected[line] : "<end>", line < actual.Length ? actual[line] : "<end>");
            Assert.True(
                line == expected.Length && line == actual.Length,
                $"{fact} ({expected.Length} -> {actual.Length} lines) differ at line {line + 1}: {before} -> {after}");
        }
    }

    // The library's facts after a check; those of its synthesized nodes as SynthesizedNodes states.
    private static (string Fact, string[] Lines)[] Facts(Compilation c)
    {
        var library = c.Library;
        var table = library.Kotonoha.Syntax;
        var printer = new Printer(true);
        printer.Visit(library.Kotonoha.RootKoto);
        var symbols = new List<string>();
        var scopes = new HashSet<BindingScope>(ReferenceEqualityComparer.Instance);
        for (var id = 1; id <= table.Count; id++)
        {
            if (table.Nodes[id]!.SymbolOf() is { } symbol && ReferenceEquals(symbol.Declaration, table.Nodes[id]))
            {
                scopes.Add(symbol.Scope);
                if (id <= table.ParsedCount)
                {
                    symbols.Add($"{id} {State(symbol, printer)}");
                }
            }
        }

        var synthesized = SynthesizedNodes.Lines(table);
        var mir = new StringBuilder();
        MirDump.Write(c.ConstructMir(), SyntaxTable.LibraryModule, mir); // The program the check analyzed, memoized per pass.
        return
        [
            ("parsed count", [table.ParsedCount.ToString(CultureInfo.InvariantCulture)]),
            ("parsed nodes", printer.ToString().Split('\n')),
            ("parsed HIR", SynthesizedNodes.Spell(printer.Hir, table, synthesized).Split('\n')),
            ("library symbols", [.. symbols]),
            ("library scopes", [.. scopes.Select(Scope).Order(StringComparer.Ordinal)]),
            ("catalog", [.. Enum.GetValues<KimiDeclarationId>().Select(x => $"{x} {Where(library.GetSymbol(x))}")]),
            ("library imports", [.. c.Binding.LibraryImports.Where(x => x.Decl.Declaration.CodeContext.Kotonoha == library.Kotonoha)
                .Select(static x => $"{x.Library} {x.Symbol} {x.Kind} {Where(x.Decl)}")]),
            ("library MIR", SynthesizedNodes.Spell(mir.ToString(), table, synthesized).Split('\n')),
            ("synthesized nodes", SynthesizedNodes.Facts(table, synthesized, printer)),
        ];
    }

    // A declaration's own symbol state, which each pass rewrites (design C3) and the tree's lines do not print.
    private static string State(BindingSymbol s, Printer printer)
        => $"{s.Kind}:{s.Name} type={printer.Type(s.Type)} whole={printer.Type(s.WholeType)} self={printer.Type(s.ContractSelf)} receiver={s.ReceiverIndex}" +
            $" next={Where(s.Next)} selfOf={Where(s.SelfOf)} pair={Where(s.Pair)} optOut={Where(s.ObjectPayloadOptOut)} scope={Where(s.Scope.Owner)}" +
            $" contract={s.Contract is not null} property={s.Property is not null} case={s.EnumCase?.Ordinal} schema={s.Schema is not null}" +
            $" adt={s.Adt is not null} source={s.SourceState} resolving={s.Resolving} slot={s.Slot}" +
            $" aggregate=[{string.Join(',', s.AggregateInputOrigins?.Select(Printer.Origin) ?? [])}]";

    // A scope's members and facts: a program pass that added to a library scope would show here.
    private static string Scope(BindingScope scope)
        => $"{Where(scope.Owner)} {scope.Owner.Akind} parent={Where(scope.Parent?.Owner)} types=[{Names(scope.Types)}] values=[{Names(scope.Values)}]" +
            $" origins=[{string.Join(',', scope.Origins?.Keys.Order(StringComparer.Ordinal) ?? Enumerable.Empty<string>())}]" +
            $" constraints={scope.Constraints is not null}" +
            $" premises={Where(scope.ImplementationPremises?.Owner)} path={scope.ConformancePath is not null}";

    private static string Names(Dictionary<string, BindingSymbol> names)
        => string.Join(',', names.OrderBy(static x => x.Key, StringComparer.Ordinal).Select(static x => $"{x.Key}@{Where(x.Value)}"));

    private static string Where(BindingSymbol? symbol) => Where(symbol?.Declaration);

    private static string Where(Koto? node) => node is null ? "-" : $"{CompilationTestHelper.Location(node)}+{node.Span.Length}";

    /// <summary>A temporary expectation (DECISIONS R5-M168's recorded exception; R8-L1 design M-B3): a library node outside the parsed
    /// tree takes the next id at its first semantic write, in Bind or in the MIR phase, so a program's uses order these ids (Milestones 27
    /// and 30 renumber most of them). They are compared by what they synthesize, not by id: their lines (facts, HIR row and own symbol
    /// state) as a sorted multiset, and a MIR location or HIR jump at one spelled by its node's printer line. It assumes no MIR body is
    /// keyed by a synthesized node today, so the dump is compared in order; one that appears fails Milestone27 loudly. Removal: U2's
    /// freeze numbers every library node, so KnownDifference fails, and this class goes; they then compare by id.</summary>
    private static partial class SynthesizedNodes
    {
        private const string Renumbering = "tests/milestones/Milestone27.kimi";

        // Each synthesized node's printer line, by id.
        internal static Dictionary<int, string> Lines(SyntaxTable table)
        {
            var lines = new Dictionary<int, string>();
            for (var id = table.ParsedCount + 1; id <= table.Count; id++)
            {
                var printer = new Printer(false);
                printer.Node(table.Nodes[id]!);
                lines.Add(id, printer.ToString().TrimEnd('\n'));
            }

            return lines;
        }

        // Each synthesized node's line, its HIR row without its id and its own symbol's state, sorted.
        internal static string[] Facts(SyntaxTable table, Dictionary<int, string> lines, Printer types)
        {
            var facts = new List<string>();
            foreach (var (id, line) in lines)
            {
                var (printer, node) = (new Printer(true), table.Nodes[id]!);
                printer.Node(node);
                var hir = Spell(printer.Hir.TrimEnd('\n'), table, lines);
                var own = node.SymbolOf() is { } symbol && ReferenceEquals(symbol.Declaration, node) ? State(symbol, types) : "-";
                facts.Add($"{line} | {hir[(hir.IndexOf(' ', StringComparison.Ordinal) + 1)..]} | {own}");
            }

            return [.. facts.Order(StringComparer.Ordinal)];
        }

        // A MIR location (@Kimi:id) or a HIR jump (->id) at a synthesized node, spelled by its line.
        internal static string Spell(string text, SyntaxTable table, Dictionary<int, string> lines)
            => Reference().Replace(text, x => int.Parse(x.Groups[2].ValueSpan, CultureInfo.InvariantCulture) is var id && id > table.ParsedCount
                ? $"{x.Groups[1].Value}{{{lines[id]}}}" : x.Value);

        // The known difference, stated exactly: Milestone27 orders the synthesized ids otherwise than the empty program.
        internal static void KnownDifference(string program, Compilation c)
        {
            if (program == Renumbering)
            {
                var equal = Lines(EmptyProgram.Value.Library.Kotonoha.Syntax).Values.SequenceEqual(Lines(c.Library.Kotonoha.Syntax).Values);
                Assert.False(equal, "Milestone27 no longer renumbers: after U2 delete SynthesizedNodes; before U2 retarget to Milestone30");
            }
        }

        [GeneratedRegex(@"(@Kimi:|->)(\d+)")]
        private static partial Regex Reference();
    }
}
