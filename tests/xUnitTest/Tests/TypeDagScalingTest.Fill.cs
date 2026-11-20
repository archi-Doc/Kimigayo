// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>The paths MirMoveData creates for a Fill (SPEC 4.3.3, 15.1.3; the step 5 review's S5-N4).</summary>
public partial class TypeDagScalingTest
{
    // A Fill lists its components, a child per distinct constant index of the body below its length (MirMoveData.Elements), only where a
    // fact needs them: a Fill whose operand carries no fact creates no child, so a body of N such Fills and N distinct constant indices
    // creates paths linear in N (each doubling of N doubles them, within MirBorrowsTest.Grows' linear bound). Fail-before: step 5 listed
    // the components of every Fill, N x N children. A Fill whose operand may hold nothing on an assumed path still carries to each
    // component (the decision's bound: O(Fills x indices) only with such operands).
    [Fact]
    public void AFillWithoutAFactCreatesNoChild()
    {
        long[] created = [.. new[] { 100, 200, 400 }.Select(Created)];
        Assert.True(MirBorrowsTest.Grows(created, 2), $"created paths {string.Join(", ", created)}");

        static long Created(int n)
        {
            var text = new StringBuilder($"func use(n: i32) => ()\nfunc f(y: i32)\n    let z: [{n} of i32] = [{n} of y]\n");
            for (var i = 0; i < n; i++)
            {
                text.Append($"    use(z[{i}])\n    let a{i}: [{n} of i32] = [{n} of y]\n");
            }

            var c = MirBuildTest.Compile(text.ToString());
            var program = c.BuildMir();
            Assert.Empty(program.Findings);
            Assert.DoesNotContain(program.Problems.All.ToArray(), static x => x.Subject.Module == 0);
            var body = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "f");
            var (flow, moves) = (new MirDataflow(new MirReach(), new MirCases(c.Types)), new MirMoveData(c.Types));
            flow.Begin(body);
            moves.Build(body, flow);
            return moves.Created;
        }
    }
}
