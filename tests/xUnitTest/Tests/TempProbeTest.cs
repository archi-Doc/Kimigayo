// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// The tracked KIMI_PROBE dumper: the bound Origins and the MIR of the source the variable names (R6-check H8a D8).
public class TempProbeTest(ITestOutputHelper output)
{
    [Fact]
    public void Dump()
    {
        var path = Environment.GetEnvironmentVariable("KIMI_PROBE");
        if (path is null)
        {
            return;
        }

        var c = MinimalEmissionTest.Analyze(File.ReadAllText(path));
        foreach (var node in KotoTree.Walk(c.Kotonoha.RootKoto))
        {
            if (node.TypeOf() is { } type && (type.Origin is not null || type.CarriesOrigin) && node is not TypeKoto)
            {
                output.WriteLine($"{node.Span.Start}: {node.GetType().Name} `{node}` : {Show(type)}");
            }
        }

        output.WriteLine(MinimalEmissionTest.Describe(c));
        var text = new StringBuilder();
        MirDump.Write(c.BuildMir(), 0, text);
        output.WriteLine(text.ToString());
    }

    private static string Show(BoundType? type)
    {
        if (type is null)
        {
            return "null";
        }

        var origin = type.Origin is null ? string.Empty : " @" + ShowOrigin(type.Origin);
        var parts = type.Components.Count == 0 ? string.Empty : "<" + string.Join(", ", type.Components.Select(Show)) + ">";
        return $"{type.Kind}/{type.Semantics}{parts}{origin}";
    }

    private static string ShowOrigin(BoundOrigin origin)
        => origin.Kind == OriginKind.Intersection ? "(" + string.Join(" & ", origin.Operands.Select(ShowOrigin)) + ")"
            : $"{origin.Kind}[{origin.Binder?.GetType().Name}:{origin.Binder?.ToString()?.Split('\n')[0]}#{origin.Slot}]";
}
