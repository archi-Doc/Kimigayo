// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Xunit;

namespace XunitTest;

// SPEC 21.3.5: a Type that contains itself by value without indirection is a semantic error of the
// declaration (InvalidInlineLayout_Kd); exceeding the inline nesting depth bound is a resource limit.
public class InlineLayoutTest
{
    private const string Box = "struct Box<T>\n    let value: T\n    public init(value: T) => self.value = value\n";

    [Theory]
    [InlineData("struct Node\n    let value: i32\n    let next: Node\n", "Node")]
    [InlineData("struct A\n    let b: B\nstruct B\n    let a: A\n", "A,B")]
    [InlineData(Box + "struct E\n    let b: Box<E>\n", "E")]
    [InlineData("struct Tree\n    let left: Option<Tree>\n", "Tree")]
    [InlineData("struct Pair\n    let items: [2 of (i32, Pair)]\n", "Pair")]
    [InlineData("enum E\n    Empty\n    Again(E)\nlet x = E.Empty", "E")]
    [InlineData("enum E<T>\n    Empty\n    Again(E<E<T>>)\nlet x = E<i32>.Empty", "E")]
    [InlineData("enum A\n    Empty\n    Again(B)\nenum B\n    Again(A)\nlet x = A.Empty", "A,B")]
    public void SelfContainingDeclarationsAreDiagnosed(string source, string cyclic)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        var diagnosed = c.Binding.Issues.Where(x => x.Code == DiagnosticCode.InvalidInlineLayout_Kd).Select(x => ((Kimi.Compiler.Parsing.DeclarationContainerKoto)x.Node).Name).Order(StringComparer.Ordinal);
        Assert.Equal(cyclic.Split(','), diagnosed);
    }

    [Fact]
    public void IndirectionsAndFiniteNestingAreAccepted()
    {
        // A reference layer, an object layer and a Slice are indirections; by-value nesting of distinct Types is finite.
        var c = MinimalEmissionTest.Analyze(Box + "struct Pair\n    let a: Box<i32>\n    let b: (i32, [2 of bool])\n    public init(a: Box<i32>, b: (i32, [2 of bool])) => (self.a, self.b) = (a, b)\nstruct View {source}\n    let items: Slice<View>{source}\nlet p = Pair.init(Box<i32>.init(1), (2, [true, false]))");
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidInlineLayout_Kd);
    }

    [Fact]
    public void NestingBeyondTheDepthBoundIsAResourceLimit()
    {
        var source = new StringBuilder("struct S0\n    let v: i32\n");
        for (var i = 1; i <= 70; i++)
        {
            source.Append("struct S").Append(i).Append("\n    let v: S").Append(i - 1).Append('\n');
        }

        source.Append("func f(s: S70) -> i32 => 1\nConsole.writeLine(\"deep\")\n");
        var c = MinimalEmissionTest.Analyze(source.ToString());
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}")));
        Assert.False(c.Emission.TryPrepare(out _, out var error));
        Assert.True(c.Emission.FailureIsResourceLimit, error);
    }

    [Theory]
    [InlineData("[2147483648 of u8]")]
    [InlineData("[1073741824 of i32]")]
    [InlineData("(bool, [2147483647 of u8])")]
    public void FiniteLayoutsBeyondTheInternalSizeOrCountBoundAreResourceLimits(string type)
    {
        var c = MinimalEmissionTest.Analyze($"func inspect(value: {type}) -> i32 => 1\nConsole.writeLine(\"finite\")");
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Emission.TryPrepare(out _, out var error));
        Assert.True(c.Emission.FailureIsResourceLimit, error);
        Assert.Contains("2147483647", error);
    }

    [Fact]
    public void EnumTagAndAlignmentCountTowardsTheSizeLimit()
    {
        var c = MinimalEmissionTest.Analyze("enum Big\n    A([2147483647 of u8])\n    B\nfunc inspect(value: Big) -> i32 => 1\nConsole.writeLine(\"finite\")");
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Emission.TryPrepare(out _, out var error));
        Assert.True(c.Emission.FailureIsResourceLimit, error);
        Assert.Contains("2147483647", error);
    }
}
