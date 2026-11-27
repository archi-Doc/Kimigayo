// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Compiler.Target;
using Xunit;

namespace XunitTest;

// PLAN G949 Stage 2 U3 (rows s11, r3b, h9, s7c; L1 §6 1-12; C5; C-382 (1)): compilations read the process's frozen library at once. A
// class of its own, so its rows run beside every other class's.
public sealed class KimiLibraryConcurrencyTest
{
    // s11, r3b, h9 and s7c: the empty program, Milestones 26, 27, 30, 31, 32 and 35, every one-file docs/examples program and
    // KimiLibraryShapeTest.HopChainProgram (which forms hop chains through frozen paths: the one writer U3's Revision 3 fixed), each Checked
    // and Analyzed on eight threads at once over one library (Of), twice. No run throws (no reading compilation reaches a frozen object's
    // refusal or a library node's compilation), each program's records equal its sequential run's, and the frozen library is unchanged:
    // its nodes and published columns (FrozenDump), every store of its layer as a reading compilation lists them (w1's dump: the base
    // conformance paths with their hop chains and satellites, the library scopes' view serials and states, the constraints' negations, the
    // levels, the part, the region map), and every field of every object reachable from it and of every library node (FieldSnapshot).
    // Its limits (the implementation review's N1): it compares values after each round, so a store of the value already held, a write
    // undone before the comparison, a field no snapshot reaches and a write no listed program makes pass it, and no barrier forces two
    // compilations to overlap; the guards that refuse a write are U4's seal refusals, the freeze's verified-path check and the tripwires.
    [Fact]
    public void ConcurrentCompilationsLeaveTheFrozenLibraryUnchanged()
    {
        var library = KimiLibrary.Of(TargetTriple.Parse(TargetProfile.Windows.Target));
        var probe = Compile(string.Empty);
        Assert.True(probe.Bind().IsComplete);
        Assert.Same(library, probe.Library);
        var (frozen, layer, fields) = (KimiLibraryShapeTest.FrozenDump(library), KimiLibraryShapeTest.LibraryLayer.Dump(probe), KimiLibraryShapeTest.FieldSnapshot(probe));
        var programs = Programs();
        var sequential = programs.Select(Records).ToArray();
        for (var round = 0; round < 2; round++)
        {
            var concurrent = new string[programs.Length][];
            Parallel.For(0, programs.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i => concurrent[i] = Records(programs[i]));
            for (var i = 0; i < programs.Length; i++)
            {
                Assert.Equal(sequential[i], concurrent[i]);
            }

            Assert.Equal(frozen, KimiLibraryShapeTest.FrozenDump(library));
            Assert.Equal(layer, KimiLibraryShapeTest.LibraryLayer.Dump(probe));
            Assert.Empty(KimiLibraryShapeTest.FieldChanges(fields, KimiLibraryShapeTest.FieldSnapshot(probe)));
        }
    }

    // A program's published records after its Check and Analyze over the process's library.
    private static string[] Records(string source)
    {
        var c = Compile(source);
        c.Check();
        _ = c.Analyze();
        return [.. TestDiagnostics.Of(c).Select(static x => $"{x.Code} {x.Path} {x.Span}")];
    }

    private static Compilation Compile(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(TargetProfile.Windows.Target));
        c.Kotonoha.AddSource(new SourceDocument("Program.kimi", source));
        return c;
    }

    // The empty program, six milestones, every one-file program of docs/examples (Hello, Ranges and Utf8Formatting among them) and the
    // hop-chain program.
    private static string[] Programs()
    {
        var examples = Directory.GetDirectories(DiagnosticCorpus.RepositoryPath("docs", "examples")).Select(static x => Directory.GetFiles(x, "*.kimi"))
            .Where(static x => x.Length == 1).Select(static x => File.ReadAllText(x[0]));
        var milestones = new[] { 26, 27, 30, 31, 32, 35 }.Select(static x => Repository.ReadText("tests", "milestones", $"Milestone{x}.kimi"));
        return [string.Empty, .. milestones, .. examples, KimiLibraryShapeTest.HopChainProgram];
    }
}
