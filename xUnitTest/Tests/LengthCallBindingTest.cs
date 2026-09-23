// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class LengthCallBindingTest
{
    private const string Keep = "func keep<length N, T>(value: [N of T]) -> [N of T] => value@move\n";

    [Theory]
    [InlineData("keep<2, i32>(a)", 2)]
    [InlineData("keep(a)", 2)]
    [InlineData("keep<(1 + 1), i32>(a)", 2)]
    [InlineData("keep<0, i32>([])", 0)]
    [InlineData("keep([1, 2])", 2)]
    public void SubstitutesLengthsIndependentlyOfTypes(string expression, long length)
    {
        var c = MinimalEmissionTest.Analyze(Keep + "let a: [2 of i32] = [1, 2]\nlet result = " + expression);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var call = Assert.Single(Nodes(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        Assert.Equal(BoundTypeKind.FixedArray, call.BoundType!.Kind);
        Assert.Equal(length, call.BoundType.Length);
        Assert.Same(BoundType.I32, call.BoundType.Components[0]);
        Assert.NotNull(call.BoundCall);
        Assert.Null(call.BoundCall.TypeArguments[0]);
        Assert.Same(BoundType.I32, call.BoundCall.TypeArguments[1]);
        Assert.Equal(length, call.BoundCall.LengthArguments[0]!.Value);
        Assert.Null(call.BoundCall.LengthArguments[1]);
    }

    [Theory]
    [InlineData("let N: isize = 2\nlet a: [2 of i32] = [1, 2]\nlet result = keep<N, i32>(a)")]
    [InlineData("group Sizes\n    public let N: isize = 2\nlet a: [2 of i32] = [1, 2]\nlet result = keep<Sizes.N, i32>(a)")]
    [InlineData("func forward<length M, U>(a: [M of U]) -> [M of U] => keep<M, U>(a@move)")]
    [InlineData("func forward<length M, U>(a: [M of U]) -> [M of U] => keep(a@move)")]
    [InlineData("let result: [0 of i32] = keep([])")]
    public void UsesConstantsCallerSlotsAndExpectedTypes(string source)
    {
        var c = MinimalEmissionTest.Analyze(Keep + source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("func f<length N>(a: [(N + 1) of i32]) -> [(1 + N) of i32] => a\nlet x = f<1>([1, 2])")]
    [InlineData("func f<length N>(a: [(N * 2) of i32], b: [N of i32]) => ()\nlet a: [4 of i32] = [1, 2, 3, 4]\nlet b: [2 of i32] = [1, 2]\nf(a, b)")]
    [InlineData("func f<length N>(a: [(N - 1) of i32]) => ()\nf<1>([])")]
    [InlineData("func f<length N>(a: [(N - 1) of i32])\n    let local: [(N - 1) of i32]\nf<1>([])")]
    public void ChecksFormationAfterAllInputEvidence(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("func f<length N>(a: [(N - 1) of i32]) => ()\nf<0>([])")]
    [InlineData("func f<length N>(a: [(N / 0) of i32]) => ()\nf<2>([])")]
    [InlineData("func f<length N>() -> [(N + 1) of i32] => $abort(\"unused\")\nf<9223372036854775807>()")]
    [InlineData("func f<length N>()\n    let local: [(N - 1) of i32]\nf<2>()")]
    [InlineData("func f<length N>(a: [(N - 1) of i32]) => ()\nfunc bad<length M>(a: [M of i32]) => f<M>(a)")]
    [InlineData("func f<length N>(a: [N of i32], b: [N of i32]) => ()\nf([1], [1, 2])")]
    [InlineData("func f<length N>(a: [(N * 2) of i32]) => ()\nf([1, 2])")]
    [InlineData("func f<length N>(a: [N of i32]) => ()\nlet a = [1, 2]\nf(a)")]
    public void RejectsInvalidOrUnprovedFormation(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Theory]
    [InlineData("keep<1, i32>(a)")]
    [InlineData("keep<i32, 2>(a)")]
    [InlineData("keep<(-1), i32>(a)")]
    [InlineData("keep<(1 / 0), i32>(a)")]
    [InlineData("keep<9223372036854775808, i32>(a)")]
    [InlineData("keep<2>(a)")]
    public void RejectsInvalidLengthArguments(string expression)
    {
        var c = MinimalEmissionTest.Analyze(Keep + "let a: [2 of i32] = [1, 2]\nlet result = " + expression);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Null(Assert.Single(Nodes(c.Kotonoha.RootKoto).OfType<InvocationKoto>()).BoundCall);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Theory]
    [InlineData("func f<length N>(a: [N of i32]) => ()\nfunc f<T>(a: T) => ()\nf<2>([1, 2])")]
    [InlineData("func f<length N>(a: [N of i32]) => ()\nfunc f<T>(a: T) => ()\nf<i32>(1)")]
    [InlineData("func f<length N>(a: [(N - 1) of i32]) => ()\nfunc f<length N>(a: [N of i32]) => ()\nf<0>([])")]
    [InlineData("func f<length N>(a: [N of i32]) => ()\nfunc f<length N>(a: [N of string]) => ()\nf([1, 2])")]
    public void KeepsLengthEvidenceLocalToEachCandidate(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.NotNull(Assert.Single(Nodes(c.Kotonoha.RootKoto).OfType<InvocationKoto>()).BoundCall);
    }

    [Theory]
    [InlineData("func f<length N>(a: [(N - 1) of i32]) => ()\nfunc f<length N>(a: [N of i32]) => ()\nf<1>([])", true)]
    [InlineData("func f<length N, T>(a: [N of T])\n    T is Copy\nf<1, string>([\"x\"])", false)]
    [InlineData("func f<length N, T>(a: [N of T])\n    T is Copy\nf<1, i32>([1])", true)]
    [InlineData("func f<length N>(a: [N of i32]) => ()\nfunc f<length M>(a: [M of i32]) => ()\nf<1>([1])", false)]
    [InlineData("func f<length N>() => ()\nlet N = 2\nf<N>()", true)]
    [InlineData("func f<length N>() => ()\nvar N = 2\nf<N>()", false)]
    [InlineData("func f<length N>() => ()\ngroup Sizes\n    private let N = 2\nf<Sizes.N>()", false)]
    [InlineData("struct Size\nlet Size: isize = 2\nfunc f<T>() => ()\nf<Size>()", true)]
    [InlineData("struct Size\nlet Size: isize = 2\nfunc f<length N>() => ()\nf<Size>()", true)]
    [InlineData("struct Size\nlet Size: isize = 2\nfunc f<T>() => ()\nf<((Size))>()", true)]
    [InlineData("struct Size\nlet Size: isize = 2\nfunc f<length N>() => ()\nf<((Size))>()", true)]
    public void PreservesConstraintsAndLookup(string source, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(valid, c.Binding.Result.IsComplete);
    }

    [Fact]
    public void DoesNotSilentlySelectOneNamespaceAcrossDifferentSlotKinds()
    {
        var c = MinimalEmissionTest.Analyze("struct Size\nlet Size: isize = 2\nfunc f<T>() => ()\nfunc f<length N>() => ()\nf<Size>()");
        Assert.False(c.Binding.Result.IsComplete);
        var call = Assert.Single(Nodes(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        Assert.Null(call.BoundCall);
        Assert.Equal(BindingFailure.Unsupported, call.BindingFailure);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void RebindingRevokesChangedLengthAndReusesCallStorage()
    {
        var c = MinimalEmissionTest.Analyze(Keep + "let a: [2 of i32] = [1, 2]\nkeep<2, i32>(a)");
        Assert.True(c.Binding.Result.IsComplete);
        var call = Assert.Single(Nodes(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        var plan = call.BoundCall;
        var generic = Assert.IsType<GenericsKoto>(call.Method);
        var original = generic.TypeArguments[0];
        var donor = MinimalEmissionTest.Analyze("3");
        var replacement = Assert.Single(Nodes(donor.Kotonoha.RootKoto).OfType<NumberLiteralKoto>());
        Assert.True(KotoHelper.Replace(generic, original, replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.Null(call.BoundCall);
        Assert.True(KotoHelper.Replace(generic, replacement, original));
        Assert.True(c.Bind().IsComplete);
        Assert.Same(plan, call.BoundCall);
        Assert.Equal(2, call.BoundCall!.LengthArguments[0]!.Value);
    }

    [Fact]
    public void ReloadAndWarmRebindingRetainSeparateSubstitutions()
    {
        var c = MinimalEmissionTest.Analyze(Keep + "let a: [2 of i32] = [1, 2]\nkeep<2, i32>(a)\nkeep(a)");
        Assert.True(c.Binding.Result.IsComplete);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha), ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        Assert.True(restored.Bind().IsComplete);
        Assert.All(Nodes(restored.Kotonoha.RootKoto).OfType<InvocationKoto>(), call => Assert.Equal(2, call.BoundCall!.LengthArguments[0]!.Value));
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Length call Binding failed.");
            }
        }));
    }

    private static IEnumerable<Koto> Nodes(Koto node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
        {
            foreach (var descendant in Nodes(child))
            {
                yield return descendant;
            }
        }
    }
}
