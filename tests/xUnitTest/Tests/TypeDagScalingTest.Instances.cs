// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>PLAN G243's O1 to O3 (DECISIONS BU-55): R4's V16 chain on instances, <c>D_i&lt;T&gt; { x: D_{i+1}&lt;(T, T)&gt; }</c>, has
/// 2n distinct instance Types whose last Type argument is a Tuple tree of 2^(n-1) leaves. Each row's chain is 40 long, so a walk that
/// re-visits shared parts meets 2^40 paths; Binding and every call's retention (TyCtxt.Retains, which A5 requests) stay linear in the
/// distinct parts. The MIR builder is not run: its own walks over these shapes are open (PLAN).</summary>
public partial class TypeDagScalingTest
{
    // The chain through Tuples, fixed arrays and enum payloads, and with raw leaves: the declarations' inline layouts (VisitInlineLayout,
    // CompleteProjections); a member path to the last instance and a generic callee at it (CheckTypeConstraints, ProveTypeLengths, the
    // callee's atom walk); a comparison of the Tuple tree (ComparisonProof), a Copy requirement on it (DependentType), a called closure
    // capturing it (HasUnsubstitutedInput).
    [Theory]
    [InlineData("tuple", 40)]
    [InlineData("array", 40)]
    [InlineData("enum", 40)]
    [InlineData("raw", 40)]
    public void InstanceChainWalksAreLinearInDistinctParts(string form, int size)
    {
        var c = MirBuildTest.Compile(InstanceChain(form, size));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var calls = new List<string>();
        foreach (var node in KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>())
        {
            if (node.CallOf() is { Kind: not CalleeKind.Value, Target.LibraryDeclaration: null } call)
            {
                var definition = node.Parent;
                while (definition is not FunctionKoto)
                {
                    definition = definition!.Parent;
                }

                var function = (FunctionKoto)definition;
                Assert.NotNull(c.Types.Retains(call.Target, c.Types.SubstOf(call), function.SymbolOf()));
                calls.Add($"{function.Name} {call.Target.Name}");
            }
        }

        Assert.Equal(form == "enum" ? "g f" : "g f, k h, copies cp", string.Join(", ", calls));
    }

    // D1 to Dn (structs, or enums whose Case A holds the part), each storing the next at its Type argument doubled (a Tuple, or a Tuple of
    // two fixed arrays), the last storing the argument; D1 at i32, or at `raw/i32` for `raw`. A struct chain's functions read the path to
    // Dn's argument.
    private static string InstanceChain(string form, int n)
    {
        var argument = form == "array" ? "([2 of T], [2 of T])" : "(T, T)";
        var text = new System.Text.StringBuilder();
        for (var i = 1; i <= n; i++)
        {
            var part = i < n ? $"D{i + 1}<{argument}>" : "T";
            text.Append(form == "enum" ? $"enum D{i}<T>\n    A({part})\n    B\n" : $"struct D{i}<T>\n    public var x: {part}\n");
        }

        var root = form == "raw" ? "D1<raw/i32>" : "D1<i32>";
        text.Append($"func f(d: uniq/{root}, p: raw/i32) => ()\nfunc g(d: uniq/{root}) => f(d, null)\npublic func main() => ()\n");
        if (form == "enum")
        {
            return text.ToString();
        }

        var path = string.Concat(Enumerable.Repeat(".x", n));
        text.Append($"func h<T>(d: uniq/T, p: raw/i32) => ()\nfunc k(d: uniq/{root}) => h(d{path[..^2]}@uniq, null)\n")
            .Append($"func cp<T>(x: T) -> (T, T)\n    T is Copy\n    return (x, x)\nfunc copies(d: ref/{root}) -> ()\n    let p = cp(d{path})\n    return\n")
            .Append($"func captures(d: ref/{root}) -> ()\n    let v = d{path}\n    let f = func [v] () => v\n    let w = f()\n    return\n");
        return form == "tuple" ? text.Append($"func equal(a: ref/{root}, b: ref/{root}) -> bool => a{path} == b{path}\n").ToString() : text.ToString();
    }
}
