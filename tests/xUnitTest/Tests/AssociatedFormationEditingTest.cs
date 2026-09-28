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
}
