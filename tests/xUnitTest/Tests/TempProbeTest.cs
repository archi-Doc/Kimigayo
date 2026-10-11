// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

// TEMP: not committed. Dumps bound Origins and ownership operations for a probe source.
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

        output.WriteLine(MinimalEmissionTest.Describe(c, null));
        foreach (var body in c.Ownership.Bodies)
        {
            output.WriteLine("BODY " + body.Function?.ToString()?.Split('\n')[0]);
            for (var i = 0; i < body.Places.Count; i++)
            {
                output.WriteLine($"  place {i}: {body.Places[i].Kind} {Show(body.Places[i].Type)}");
            }

            for (var i = 0; i < body.Operations.Count; i++)
            {
                var o = body.Operations[i];
                output.WriteLine($"  op {i}: {o.Kind} place={o.Place} src=`{o.Source?.ToString()?.Split('\n')[0]}`");
            }
        }
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
