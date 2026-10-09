// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class AssociatedFormationEditingTest
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FormationEditRevokesAndRestoresTheCertificate(bool capability, bool nested)
    {
        var source = "contract C\n    associate Item(a)" + (capability ? " is Copy" : string.Empty) + " for " + (nested ? "(ref/i32 during a, i32)" : "ref/i32 during a") + "\nstruct S\n    Self is C\n    associate C.Item(a) is i32\nfunc f() -> S.Item(static) => 42\nrequire f() == 42 else => $abort(\"value\")";
        var c = MinimalEmissionTest.Analyze(source);
        using var output = new StringWriter();
        Assert.True(c.Emission.WriteIr(output, out var error), MinimalEmissionTest.Describe(c, error));
        var original = Formation(c.Kotonoha.RootKoto, nested);
        var parent = original.Parent!;
        var donor = ParseTestHelper.ParseSuccess(source.Replace("ref/i32 during a", "uniq/i32 during a", StringComparison.Ordinal));
        var replacement = Formation(donor.RootKoto, nested);
        Assert.True(KotoHelper.Replace(parent, original, replacement));
        output.GetStringBuilder().Clear();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
        Assert.False(c.Bind().IsComplete);
        Assert.True(KotoHelper.Replace(parent, replacement, original));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));

        static Koto Formation(Koto root, bool nested)
        {
            foreach (var node in KotoTree.Walk(root))
            {
                if (node is IsKoto { FormationType: { } formation })
                {
                    return nested ? Assert.IsType<TupleTypeKoto>(formation).ElementNodes[0] : formation;
                }

                if (node is SyntaxFormKoto { Akind: KotoKind.AssociatedType, Operands.Length: 2 } bare)
                {
                    return nested ? Assert.IsType<TupleTypeKoto>(bare.Operands[1]).ElementNodes[0] : bare.Operands[1];
                }
            }

            throw new InvalidOperationException("Missing formation Type.");
        }
    }

    // SPEC 8.1.2, 8.4.3: editing a pair-layer family's admitted set or its specification revokes its formation, and restoring the
    // clause restores it, without any state left from the edited pass.
    [Theory]
    [InlineData("s is owner or ref", "s is owner or obj")]
    [InlineData("associate Peek.Item(a) is s/T during a", "associate Peek.Item(a) is s/T during static")]
    public void PairLayerFamilyEditRevokesAndRestoresItsFormation(string original, string edited)
    {
        const string Source = "contract Peek\n    associate Item(a) for ref/Self during a\nstruct H<s/T>\n    s is owner or ref\n    T is Copy\n    Self is Peek\n" +
            "    associate Peek.Item(a) is s/T during a\n    var item: s/T\n    public init(item: s/T) => self.item = item@move\nlet h = H<i32>.init(4)";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var clause = Clause(c.Kotonoha.RootKoto, original);
        var parent = clause.Parent!;
        var replacement = Clause(ParseTestHelper.ParseSuccess(Source.Replace(original, edited, StringComparison.Ordinal)).RootKoto, edited);
        Assert.True(KotoHelper.Replace(parent, clause, replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.True(KotoHelper.Replace(parent, replacement, clause));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));

        static Koto Clause(Koto root, string text)
            => KotoTree.Walk(root).OfType<IsKoto>().First(x => x.ToString() == text);
    }
}
