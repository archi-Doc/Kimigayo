// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using System.Text.RegularExpressions;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>The MIR dump goldens (R3 V10): each program of <c>tests/mir</c> (one with a <c>#Test</c> in a test build) dumps the MIR of its
/// module as its <c>.mir</c> file says, and a body with every statement, rvalue, terminator, operand and projection form as
/// <c>every-form.mir</c> says. <c>KIMI_MIR_GOLDEN=write</c> writes the goldens instead.</summary>
[Collection(nameof(MirValidatorTest))]
public sealed class MirDumpTest
{
    public static TheoryData<string> Programs
        => new(Directory.GetFiles(DiagnosticCorpus.RepositoryPath("tests", "mir"), "*.kimi").Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal)!);

    [Theory]
    [MemberData(nameof(Programs))]
    public void ProgramDumpsItsGolden(string name)
        => Golden(name, Dump(Compile(name)));

    // The corpus includes deliberate recovery witnesses; every other fixture must parse without an Error (G136 N2).
    [Fact]
    public void GoldenProgramsHaveOnlyExpectedSyntaxErrors()
        => Assert.Equal(
            ["rejected: MisplacedSyntax_Kd", "rejected: MissingSyntax_Kd", "rejected: InvalidEscape_Kd"],
            Programs.SelectMany(program => TestDiagnostics.Of(Compile(program.Data))
                .Where(static x => x.Severity == DiagnosticSeverity.Error).Select(x => $"{program.Data}: {x.Code}")));

    // The spellings of every form: Deref, Field, Base, Index, ConstIndex, Downcast and PairLayer projections, the borrows (a two-phase one at a
    // library location and a raw one at another module's, added to the validator's body), the operands, constants, rvalues, terminators,
    // scopes, binders, statics and Opaque sites.
    [Fact]
    public void BodyDumpsEveryForm()
    {
        var body = MirValidatorTest.Build(null);
        var (index, reference) = (new MirPlace(4), new MirPlace(7));
        var check = body.Blocks.Length - 1;
        var library = new SourceInfo(new(SyntaxTable.LibraryModule, 6), 0, false);
        body.Push(check, MirStatement.Assign(reference, MirRvalue.Ref(MirRefKind.TwoPhaseUniq, index, body.TypeOf(reference)!), library));
        body.Push(check, MirStatement.Assign(reference, MirRvalue.Ref(MirRefKind.Raw, index, body.TypeOf(reference)!), new(new(1, 5), 1, true)));
        var text = new StringBuilder();
        MirDump.Write(body, text, new MirDropTable());
        Golden("every-form", text.ToString());
    }

    // SPEC 8.15.3 (PLAN G597): a keyed Storage's operands follow its Place, the start then the key's Type token (fail-before: the dump
    // gave the Place only, so a keyed Storage read as an unkeyed one).
    [Theory]
    [InlineData("r", "_2 = storage(_1, copy _3, const type R)")]
    [InlineData("from", "_2 = storage(_1, copy _3, const type Kimi.Range<S, Kimi.End>)")]
    public void AKeyedStorageDumpsItsStartAndKey(string key, string expected)
    {
        var text = new StringBuilder();
        MirDump.Write(MirValidatorTest.KeyedStorage(key, 2).Body, text, new MirDropTable());
        Assert.Contains(expected, text.ToString(), StringComparison.Ordinal);
    }

    // SPEC 14.6.1 (PLAN G427): an iteration body Binding rejected (Op Error; tests/diagnostics/syntax.json `while-header-cut`) is a
    // statement's value, its derived Opaque value in one temp of its own, ended before the next iteration.
    [Fact]
    public void ARejectedIterationBodyIsOneStatementValue()
    {
        var c = CompilationTestHelper.Parse("public func main()\n    while", "Program.kimi");
        c.Bind();
        _ = c.Binding.CheckStartup(OutputKind.Application);
        var program = c.BuildMir();
        var main = Array.Find(program.Bodies.ToArray(), static x => x.Key.Decl?.Name == "main")!;
        Assert.Equal(
            """
            fn main
              _0: () return
              _1: () temp s1
              _2: bool temp s1
              _3: () temp s1
              s1: lexical in s0
              opaque 0:
              opaque 1:
              bb0:
                  StorageLive(_1) s1
                * goto bb1 s1
              bb1:
                  StorageLive(_2) s1
                  _2 = opaque 0: bool s1
                  switch copy _2 [0: bb3, otherwise: bb2] s1
              bb2:
                  StorageDead(_2) s1
                  StorageLive(_3) s1
                * _3 = opaque 1: () s1
                  StorageDead(_3) s1
                  goto bb1 s1
              bb3:
                  StorageDead(_2) s1
                  _1 = const () s1
                  goto bb4 s1
              bb4:
                  StorageDead(_1) s1
                  _0 = const () s1
                  return

            """.ReplaceLineEndings("\n"),
            Unsited(program, main));
    }

    // A golden's program, including deliberate syntax recovery, compiled as a test build when it has a #Test.
    internal static Compilation Compile(string name)
    {
        var source = File.ReadAllText(DiagnosticCorpus.RepositoryPath("tests", "mir", name + ".kimi"));
        var c = CompilationTestHelper.Parse(source, "Program.kimi");
        c.IsTestBuild = source.Contains("#Test", StringComparison.Ordinal);
        c.Bind();
        _ = c.IsTestBuild ? c.Binding.CheckTestStartup() : c.Binding.CheckStartup(OutputKind.Application);
        return c;
    }

    // The dump of a compilation's first source module, as a golden holds it.
    internal static string Dump(Compilation c)
    {
        var text = new StringBuilder();
        MirDump.Write(c.BuildMir(), 0, text);
        return text.ToString();
    }

    // One body's dump without its sites (` @` and a syntax id), which a spelling or an edit changes (R6-check H8a D10).
    internal static string Unsited(MirProgram program, MirBody body)
    {
        var text = new StringBuilder();
        MirDump.Write(body, text, program.Drops);
        return Regex.Replace(text.ToString(), @" @\d+", string.Empty);
    }

    private static void Golden(string name, string actual)
    {
        var path = DiagnosticCorpus.RepositoryPath("tests", "mir", name + ".mir");
        if (Environment.GetEnvironmentVariable("KIMI_MIR_GOLDEN") == "write")
        {
            File.WriteAllText(path, actual, new UTF8Encoding(false));
            return;
        }

        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), actual);
    }
}
