// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

// TEMP (bu22): not committed. Dumps the MIR and A5 problems of the source in BU22_PROBE.
public class Bu22ProbeTest(ITestOutputHelper output)
{
    [Fact]
    public void Dump()
    {
        var path = Environment.GetEnvironmentVariable("BU22_PROBE");
        if (path is null)
        {
            return;
        }

        var c = MirBuildTest.Compile(File.ReadAllText(path));
        foreach (var issue in c.Binding.Issues)
        {
            output.WriteLine($"BINDING {issue.Code} `{issue.Node}` at {issue.Node.Span.Start}");
        }

        var program = c.BuildMir();
        foreach (var finding in program.Findings)
        {
            output.WriteLine($"FINDING {finding}");
        }

        var problems = program.Problems.All.ToArray();
        for (var i = 0; i < problems.Length; i++)
        {
            var x = problems[i];
            if (x.Subject.Module != 0)
            {
                continue;
            }

            var facts = string.Join("; ", program.Problems.Facts(i).ToArray().Select(f => $"{f.Kind} `{c.SyntaxOf(f.Site).ToString().Split('\n')[0]}`"));
            output.WriteLine($"PROBLEM {x.Code} `{c.SyntaxOf(x.Subject).ToString().Split('\n')[0]}` [{facts}]");
        }

        var text = new StringBuilder();
        MirDump.Write(program, 0, text);
        output.WriteLine(text.ToString());
    }
}
