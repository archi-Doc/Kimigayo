// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// R8-L1 U1 (design §2 C9, §5 (a), M-B3): the library is a compiler-build constant (SPEC 22.1), so what a compilation's check (Bind,
// startup, Analyze) leaves in its library is the same for every program and every input the library could read beside its sources.
// Each row is a fresh compilation compared with the empty program's, fact by fact: the table's parsed count, every parsed node's facts as
// the semantic snapshot prints them, the catalog symbols, the library imports and the library's MIR dump.
public partial class KimiLibraryShapeTest
{
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
    [InlineData("tests/milestones/Milestone31.kimi")]
    [InlineData("tests/milestones/Milestone32.kimi")]
    [InlineData("tests/milestones/Milestone35.kimi")]
    [InlineData("docs/examples/Ranges/Ranges.kimi")]
    [InlineData("docs/examples/Utf8Formatting/Utf8Formatting.kimi")]
    public void TheLibraryIsIndependentOfTheProgram(string program)
        => SameAsTheEmptyPrograms(Check(Compilation.CreateForTest(), program, WindowsProfile.Target));

    // Design §2 C9: Binding reads the target, the configuration and the test build beside the sources, and the library's parse reads the
    // documentation and hover collection; the library reads none of them today. The target is part of the frozen library's key (design §4),
    // so a library that came to read it would move that row to a comparison per target.
    [Theory]
    [InlineData("test build")]
    [InlineData("documentation")]
    [InlineData("hover")]
    [InlineData("debug")]
    [InlineData("target")]
    public void TheLibraryIsIndependentOfTheInputs(string input)
    {
        var c = Compilation.CreateForTest();
        _ = input switch
        {
            "test build" => c.IsTestBuild = true,
            "documentation" => c.CollectDocumentation = true,
            "hover" => c.CollectHover = true,
            "debug" => c.Project.KimiOptions.Debug = true,
            _ => false,
        };

        SameAsTheEmptyPrograms(Check(c, "tests/milestones/Milestone31.kimi", input == "target" ? "x86_64-unknown-linux-gnu" : WindowsProfile.Target));
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

    // A compilation's check (Compilation.Check: Bind, the startup check, Analyze) of a program read from the repository, complete and
    // verified; the empty program is no Application, and its check analyzes all the same (SPEC 23.1.2).
    private static Compilation Check(Compilation c, string program, string target)
    {
        Assert.True(c.Prepare(target));
        c.Kotonoha.AddSource(new SourceDocument("Program.kimi", program.Length == 0 ? string.Empty : Repository.ReadText(program.Split('/'))));
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

            Assert.True(
                line == expected.Length && line == actual.Length,
                $"{fact} ({expected.Length} -> {actual.Length} lines) differ at line {line + 1}: {(line < expected.Length ? expected[line] : "<end>")} -> {(line < actual.Length ? actual[line] : "<end>")}");
        }
    }

    // The library's facts after a check; those of its synthesized nodes as SynthesizedNodes states.
    private static (string Fact, string[] Lines)[] Facts(Compilation c)
    {
        var library = c.Library;
        var table = library.Kotonoha.Syntax;
        var catalog = Enum.GetValues<KimiDeclarationId>().Select(x => library.GetSymbol(x) is { } symbol
            ? $"{x} {SemanticSnapshotTest.Line(symbol.Declaration)} type={(symbol.Type is { } type ? TyCtxt.Name(type) : "-")} contract={symbol.Contract is not null} receiver={symbol.ReceiverIndex}"
            : $"{x} -");
        var imports = c.Binding.LibraryImports.Where(x => x.Decl.Declaration.CodeContext.Kotonoha == library.Kotonoha)
            .Select(static x => $"{x.Library} {x.Symbol} {x.Kind} {CompilationTestHelper.Location(x.Decl.Declaration)}");
        var mir = new StringBuilder();
        MirDump.Write(c.ConstructMir(), SyntaxTable.LibraryModule, mir); // The program the check analyzed, memoized per pass.
        return
        [
            ("parsed count", [table.ParsedCount.ToString(CultureInfo.InvariantCulture)]),
            ("parsed nodes", SemanticSnapshotTest.Tree(library.Kotonoha.RootKoto).Split('\n')),
            ("catalog symbols", [.. catalog]),
            ("library imports", [.. imports]),
            ("library MIR", SynthesizedNodes.Spell(table, mir.ToString()).Split('\n')),
            ("synthesized nodes", SynthesizedNodes.Lines(table)),
        ];
    }

    /// <summary>A temporary expectation (DECISIONS R5-M168's recorded exception; R8-L1 design M-B3): a library node outside the parsed
    /// tree takes the next id at its first semantic write, in Bind or in the MIR phase, so a program's uses order these ids (Milestones 27
    /// and 30 renumber most of them). They are compared by what they synthesize, not by id: their lines (kind, location, facts) as a
    /// sorted multiset, and in the library's MIR each such id is spelled by its node's location and kind. Removal: U2's freeze numbers
    /// every library node, then they compare by id with the parsed ones and this class goes.</summary>
    private static partial class SynthesizedNodes
    {
        internal static string[] Lines(SyntaxTable table)
        {
            var lines = new List<string>();
            for (var id = table.ParsedCount + 1; id <= table.Count; id++)
            {
                lines.Add(SemanticSnapshotTest.Line(table.Nodes[id]!));
            }

            return [.. lines.Order(StringComparer.Ordinal)];
        }

        internal static string Spell(SyntaxTable table, string mir)
            => LibraryId().Replace(mir, x => int.Parse(x.Groups[1].ValueSpan, CultureInfo.InvariantCulture) is var id && id > table.ParsedCount
                ? $"@Kimi:{CompilationTestHelper.Location(table.Nodes[id])}+{table.Nodes[id]!.Span.Length} {table.Nodes[id]!.Akind}" : x.Value);

        [GeneratedRegex(@"@Kimi:(\d+)")]
        private static partial Regex LibraryId();
    }
}
