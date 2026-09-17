// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ConstantLengthBindingTest
{
    [Theory]
    [InlineData("let Width: isize = 4\nlet row: [Width of u8] = [1, 2, 3, 4]", 4)]
    [InlineData("let Small = 4\nlet row: [(Small * 2) of u8]", 8)]
    [InlineData("let Width: isize = 4\nlet Height = Width - 1\nlet row: [(Width * Height) of u8]", 12)]
    [InlineData("group Dimensions\n    public let Width: isize = 4\nfunc f(row: [Dimensions.Width of u8]) => ()", 4)]
    [InlineData("let Max: u128 = 340282366920938463463374607431768211455\nlet row: [(Max - Max) of u8]", 0)]
    [InlineData("let Min: i128 = -170141183460469231731687303715884105728\nlet row: [(Min - Min) of u8]", 0)]
    [InlineData("let Small: u8 = 1\nlet row: [((200 + 54) + Small) of u8]", 255)]
    [InlineData("let row: [((-9223372036854775808 + 9223372036854775807) + 1) of u8]", 0)]
    [InlineData("group Dimensions\n    private let Width: isize = 4\n    public func f(row: [Width of u8]) => ()", 4)]
    [InlineData("group Dimensions\n    public let Width: isize = Height + 1\n    private let Height: isize = 3\nfunc f(row: [Dimensions.Width of u8]) => ()", 4)]
    public void ConstantReadableBindingsFormLengths(string source, long expected)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var array = Assert.Single(Nodes(c.Kotonoha.RootKoto).OfType<FixedArrayTypeKoto>());
        Assert.Equal(expected, array.BoundType!.Length);
    }

    [Theory]
    [InlineData("var N = 4\nlet row: [N of u8]")]
    [InlineData("let N: i32\nlet row: [N of u8]")]
    [InlineData("let row: [N of u8]\nlet N = 4")]
    [InlineData("func f(N: i32)\n    let row: [N of u8]")]
    [InlineData("func get() -> i32 => 4\nlet N = get()\nlet row: [N of u8]")]
    [InlineData("let N = 4\nlet M: isize = 2\nlet row: [(N + M) of u8]")]
    [InlineData("let N = 2147483647\nlet row: [((N + 1) - N) of u8]")]
    [InlineData("let N: u8 = 1\nlet row: [((255 + N) - N) of u8]")]
    [InlineData("let N: u128 = 340282366920938463463374607431768211455\nlet row: [((N + 1) - N) of u8]")]
    [InlineData("let N: i128 = -170141183460469231731687303715884105728\nlet row: [(-N) of u8]")]
    [InlineData("let N: isize = -9223372036854775808\nlet row: [(N % -1) of u8]")]
    [InlineData("let N = 4\nlet row: [(N / 0) of u8]")]
    [InlineData("let N = 4\nlet row: [(N % 0) of u8]")]
    [InlineData("let N = -1\nlet row: [N of u8]")]
    [InlineData("let N: u64 = 9223372036854775808\nlet row: [N of u8]")]
    [InlineData("let N = true\nlet row: [N of u8]")]
    [InlineData("group Dimensions\n    private let N = 4\nfunc f(row: [Dimensions.N of u8]) => ()")]
    [InlineData("group Dimensions\n    public let N: i32 = 4\n        private get\nfunc f(row: [Dimensions.N of u8]) => ()")]
    [InlineData("group Dimensions\n    public let N: i32 = 4\n        get() -> i32 => storage\nfunc f(row: [Dimensions.N of u8]) => ()")]
    [InlineData("group Dimensions\n    public let N: i32 = M\n    public let M: i32 = N\nfunc f(row: [Dimensions.N of u8]) => ()")]
    [InlineData("struct S\n    let N: i32\n    func f(self: ref/Self)\n        let row: [N of u8]")]
    public void NonconstantOrInvalidArithmeticCannotFormLengths(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, issue => issue.Node.BindingFailure == BindingFailure.InvalidTypeFormation);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void AdaptationSyntaxIsNotPartOfLengthExpressions()
    {
        var c = MinimalEmissionTest.Analyze("let N = 4\nlet row: [(N@isize) of u8]");
        Assert.Contains(Nodes(c.Kotonoha.RootKoto), x => x.Akind == KotoKind.Error);
        Assert.False(c.Binding.Result.IsComplete);
    }

    [Fact]
    public void LocalConstantsProduceExecutableFixedArrays()
        => ScalarEmissionTest.EmitFixture("ConstantLengthLocal", "let N: isize = 2\nlet M = N + 1\nlet row: [M of string] = [\"one\", \"two\", \"three\"]\nwriteLine(row[2])", "three\n");

    [Fact]
    public void SymbolicLengthsRetainExpandedPrivateConstants()
    {
        var c = MinimalEmissionTest.Analyze("group Dimensions\n    private let Width: isize = 4\n    public func f<length N>(row: [(N + Width) of u8]) => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var array = Assert.Single(Nodes(c.Kotonoha.RootKoto).OfType<FixedArrayTypeKoto>());
        var expression = array.BoundType!.LengthExpression!;
        Assert.Equal(KotoKind.Plus, expression.Operation);
        var constant = expression.Left!.IsConstant ? expression.Left : expression.Right!;
        Assert.Equal(4, constant.Value);
        Assert.Null(constant.Parameter);
        Assert.Contains(c.Binding.Obligations, x => x.Kind == BindingObligationKind.TypeFormation && ReferenceEquals(x.Use, array.Length));
    }

    [Theory]
    [InlineData("i8")]
    [InlineData("u8")]
    [InlineData("i16")]
    [InlineData("u16")]
    [InlineData("i32")]
    [InlineData("u32")]
    [InlineData("i64")]
    [InlineData("u64")]
    [InlineData("isize")]
    [InlineData("usize")]
    [InlineData("i128")]
    [InlineData("u128")]
    public void EveryIntegerTypeChecksItsOwnIntermediateRange(string name)
    {
        var type = BoundType.Primitives[name];
        var width = ScalarTypes.Width(type, 64);
        var max = (System.Numerics.BigInteger.One << (width - (ScalarTypes.Signed(type) ? 1 : 0))) - 1;
        var declaration = $"let Max: {name} = {max}\n";
        var valid = MinimalEmissionTest.Analyze(declaration + "let row: [(Max - Max) of u8]");
        Assert.True(valid.Binding.Result.IsComplete, MinimalEmissionTest.Describe(valid, null));
        foreach (var expression in new[] { "(Max + 1) - Max", "(Max * 2) - Max" })
        {
            var invalid = MinimalEmissionTest.Analyze(declaration + $"let row: [({expression}) of u8]");
            Assert.False(invalid.Binding.Result.IsComplete);
            Assert.Contains(invalid.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.InvalidTypeFormation);
        }
    }

    [Fact]
    public void RebindingRecomputesChangedInitializerValues()
    {
        var c = MinimalEmissionTest.Analyze("let N: isize = 2\nlet row: [N of u8]");
        var variable = Nodes(c.Kotonoha.RootKoto).OfType<VariableKoto>().Single(x => x.NameKoto.IdentifierName == "N");
        var array = Assert.Single(Nodes(c.Kotonoha.RootKoto).OfType<FixedArrayTypeKoto>());
        var oldType = array.BoundType;
        var replacementSource = MinimalEmissionTest.Analyze("3");
        var replacement = Assert.Single(Nodes(replacementSource.Kotonoha.RootKoto).OfType<NumberLiteralKoto>());
        Assert.True(KotoHelper.Replace(variable, variable.InitializerKoto!, replacement));
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(3, array.BoundType!.Length);
        Assert.NotSame(oldType, array.BoundType);
    }

    [Fact]
    public void ReloadAndWarmRebindingPreserveConstantLengths()
    {
        var c = MinimalEmissionTest.Analyze("let N: isize = 2\nlet row: [N of string] = [\"one\", \"two\"]\nwriteLine(row[1])");
        using var original = new StringWriter();
        Assert.True(c.Emission.WriteIr(original, out var error), error);
        var bytes = Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var kotonoha = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(bytes, ref kotonoha);
        Assert.NotNull(kotonoha);
        kotonoha.OnDeserialized(restored);
        restored.Bind();
        restored.Binding.CheckStartup(OutputKind.Application);
        restored.Ownership.Analyze();
        using var writer = new StringWriter();
        Assert.True(restored.Emission.WriteIr(writer, out error), error);
        Assert.Equal(original.ToString(), writer.ToString());
        ScalarEmissionTest.WriteFixture("ConstantLengthReload", writer.ToString(), "two\n");

        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Constant length Binding failed.");
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
