// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
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
    {
        var source = File.ReadAllText(DiagnosticCorpus.RepositoryPath("tests", "mir", name + ".kimi"));
        Golden(name, Dump(MirBuildTest.Compile(source, source.Contains("#Test", StringComparison.Ordinal))));
    }

    // The spellings of every form: Deref, Field, Base, Index, ConstIndex, Downcast and PairLayer projections, the borrows (a two-phase and a
    // raw one added to the validator's body), the operands, constants, rvalues, terminators, scopes, binders, statics and Opaque sites.
    [Fact]
    public void BodyDumpsEveryForm()
    {
        var body = MirValidatorTest.Build(null);
        var (index, reference) = (new MirPlace(4), new MirPlace(7));
        var check = body.Blocks.Length - 1;
        body.Push(check, MirStatement.Assign(reference, MirRvalue.Ref(MirRefKind.TwoPhaseUniq, index, body.TypeOf(reference)!), default));
        body.Push(check, MirStatement.Assign(reference, MirRvalue.Ref(MirRefKind.Raw, index, body.TypeOf(reference)!), new(new(1, 5), 1, true)));
        var text = new StringBuilder();
        MirDump.Write(body, text, new MirDropTable(), 0);
        Golden("every-form", text.ToString());
    }

    // The dump of a compilation's first source module, as a golden holds it.
    internal static string Dump(Compilation c)
    {
        var text = new StringBuilder();
        MirDump.Write(c.BuildMir(), 0, text);
        return text.ToString();
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
