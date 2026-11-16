// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// PLAN G949 Stage 1 (the design's coverage rows c13 and c17): the library pass binds the Kimi library once per compilation, and every
// later Bind is a program pass over the source modules. Both rows fail at dev a63b1598b and are Explicit until G949 S1-1 lands the
// library pass (c17 first: S1-1's SyntaxTable refusal lands only after c17 passes); S1-1 removes Explicit.
public partial class KimiLibraryShapeTest
{
    // c13 (bound once): after the first Bind, a second Bind's node table holds the program's nodes only, while its Result keeps the
    // library layer's counts (design R1.1, c15). S1-1 adds the library's publication version (R1.3), unchanged by the second pass.
    [Fact(Explicit = true)] // Until G949 S1-1.
    public void ASecondBindBindsNoLibraryNode()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Program.kimi", Repository.ReadText("docs/examples/Hello/Hello.kimi".Split('/'))));
        var first = c.Bind();
        Assert.True(first.IsComplete);
        var second = c.Bind();
        Assert.Equal(first, second);
        Assert.DoesNotContain(c.Binding.Semantics.BoundNodes, x => ReferenceEquals(x.Kotonoha, c.Library.Kotonoha));
    }

    // c17 (RC1): the library pass numbers every library node a later phase reads, so the library table's count after the first Bind
    // equals its count after the first Analyze. At dev the MIR builder's CallPlan.Accessor creates and numbers 7 library accessors'
    // execution functions (Binding.AccessorFunction) after Bind: 16,120 against 16,134. c2's programs and, since S1-0b, the library-layer
    // rows' whole corpus (c16's), valid or not: a check analyzes whatever Binding found (SPEC 23.1.2).
    [Theory(Explicit = true)] // Until G949 S1-1.
    [MemberData(nameof(LayerPrograms))]
    [MemberData(nameof(LayerCorpus))]
    public void TheLibraryPassNumbersEveryLibraryNode(string program)
    {
        var c = Bound(program);
        var bound = c.Library.Kotonoha.Syntax.Count;
        c.Analyze();
        Assert.Equal(bound, c.Library.Kotonoha.Syntax.Count);
    }
}
