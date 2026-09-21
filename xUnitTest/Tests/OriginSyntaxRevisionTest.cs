// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class OriginSyntaxRevisionTest
{
    private const string View = "struct View<T> {source}\n    let value: ref{source}/T\nstruct Array<T>\n    let item: T\n";

    [Fact]
    public void EmbeddedLibraryUsesCurrentOriginSyntax()
    {
        var c = MinimalEmissionTest.Analyze("let value = 1");
        Assert.True(c.Bind().IsComplete, string.Join("\n", c.Binding.Library.Kotonoha.DiagnosticCollection.GetArray().Select(x => $"{x.SourceDocument?.Path}:{x.Span}: {x.Message}")) + "\n" + c.Binding.Library.InvalidDeclaration);
    }

    [Theory]
    [InlineData("View<i32>")]
    [InlineData("(View<i32>)")]
    [InlineData("owner/View<i32>")]
    [InlineData("Array<View<i32>>")]
    [InlineData("(View<i32>, View<i32>)")]
    [InlineData("ref/View<i32>")]
    [InlineData("[2 of View<i32>]")]
    public void AggregateInputOmissionIsIndependentAndStable(string input)
    {
        var c = MinimalEmissionTest.Analyze(View + $"func inspect(left: {input}, right: {input}) => ()");
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        var function = ParseTestHelper.GetChildren(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single();
        var left = Origins(function.Parameters[0].Type.BoundType!).Where(x => x.Kind == OriginKind.Input).ToArray();
        var right = Origins(function.Parameters[1].Type.BoundType!).Where(x => x.Kind == OriginKind.Input).ToArray();
        Assert.NotEmpty(left);
        Assert.DoesNotContain(left, right.Contains);
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(left, Origins(function.Parameters[0].Type.BoundType!).Where(x => x.Kind == OriginKind.Input));
    }

    [Theory]
    [InlineData("func identity(value: View<i32>) -> View<i32> => value", false)]
    [InlineData("func identity(value: View<i32>) -> View<i32>{r}\n    origin r.source == value.source\n    return value", true)]
    [InlineData("func identity(value: View<i32>{v}) -> View<i32>{r}\n    origin r.source == v.source\n    return value", true)]
    [InlineData("func inspect(callback: (View<i32>) -> ()) => ()", false)]
    [InlineData("func inspect(callback: (View<i32>{c}) -> (), value: View<i32>)\n    origin c.source == value.source\n    ()", true)]
    [InlineData("func inspect(value: ref/ref/i32) => ()", false)]
    [InlineData("func inspect(value: ref/ref{a}/i32) => ()", true)]
    [InlineData("func empty<T>(value: T) -> View<i32> => $abort(\"unreachable\")", false)]
    [InlineData("func empty<T>(value: T) -> View<i32>\n    T is Owned\n    $abort(\"unreachable\")", true)]
    [InlineData("func empty(value: i32) -> View<i32> => $abort(\"unreachable\")", true)]
    public void OmissionKeepsSignatureBoundariesAndResultContracts(string declaration, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(View + declaration);
        Assert.Equal(valid, c.Binding.Result.IsComplete);
    }

    [Theory]
    [InlineData("func f {}() => ()")]
    [InlineData("func f origin a() => ()")]
    [InlineData("func f {a}(value: ref/i32 from a) => ()")]
    [InlineData("func f {a}(value: (ref/i32){a}) => ()")]
    [InlineData("func f {a}(value: obj{a}/i32) => ()")]
    [InlineData("func f {a}(value: ref{source => a}/i32) => ()")]
    [InlineData("func f {a, b}(value: View<i32>{a, b}) => ()")]
    [InlineData("func f {a}(value: View<i32>{a}{a}) => ()")]
    public void RejectsOldAndWrongRoleSyntax(string declaration)
        => Assert.NotEmpty(ParseTestHelper.Parse(View + declaration).DiagnosticCollection.GetArray());

    [Theory]
    [InlineData("struct S\n    init(! value: ref{a,}/i32) => ()")]
    [InlineData("struct S\n    computed item: i32\n        get(self: ref{a}/Self) -> i32 => 1")]
    [InlineData("struct Pair {a, b}\n    let first: ref{a}/i32\n    let second: ref{b}/i32\nfunc f(value: Pair, source: ref/i32)\n    origin value.a == source\n    ()")]
    [InlineData("struct Outer {a}\n    public struct Inner {b}\nfunc f(value: (Outer{outer}).Inner{inner})\n    origin outer.a == static\n    origin inner.b == static\n    ()")]
    [InlineData("func f<s/T, U>(first: s/T, second: s/U)\n    s is ref\n    ()\nlet value: i32 = 1\nf(value@ref, value@ref)")]
    [InlineData("func f<s/T, U>(first: s/T, second: s/U)\n    s is owner\n    ()\nf<i32, i32>(1, 2)")]
    [InlineData("func f<s/T, U>(first: s/T, second: s/U) -> s/U\n    s is ref\n    return second")]
    [InlineData("func f<s/T, U>(first: s/T, second: s/U) -> s/U\n    s is uniq\n    return second")]
    [InlineData("func f<s/T, U>(first: s/T, second: s/U)\n    s is ref\n    let local: s/U = second")]
    [InlineData("func origin(from: i32) -> i32 => from")]
    public void NewDeclarationAndApplicationFormsBind(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        Assert.False(c.Kotonoha.HasSourceErrors);
        Assert.True(c.Bind().IsComplete);
        Assert.True(Reload(c).Bind().IsComplete);

        var builder = default(IndentedStringBuilder);
        try
        {
            c.Kotonoha.RootKoto.UnparseAll(ref builder);
            var written = MinimalEmissionTest.Analyze(builder.ToString());
            Assert.True(written.Binding.Result.IsComplete, builder.ToString());
        }
        finally
        {
            builder.Dispose();
        }
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void ContractComparisonUsesQuantificationNotSpelling(bool requiredStatic, bool implementationStatic, bool valid)
    {
        var required = requiredStatic ? "\n        origin value.source == static" : string.Empty;
        var implementation = implementationStatic ? "\n        origin value.source == static" : string.Empty;
        var source = View + $"contract C\n    func inspect(self, value: View<i32>) -> (){required}\nstruct S\n    Self is C\n    public func inspect(self, value: View<i32>) -> (){implementation}\n        ()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Kotonoha.HasSourceErrors);
        Assert.Equal(valid, c.Binding.Result.IsComplete);
    }

    [Fact]
    public void SpecializationInheritsTheOriginalResultOrigin()
    {
        var c = MinimalEmissionTest.Analyze(View + "group G\n    func identity<T>(value: View<T>) -> View<T>{result}\n        origin result.source == value.source\n        return value\n    specialize func identity<i32>(value: View<i32>) -> View<i32> => value");
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues));
        var functions = ParseTestHelper.GetChildren(c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "G")).OfType<FunctionKoto>().ToArray();
        Assert.Equal(Origins(functions[0].Parameters[0].Type.BoundType!).Select(x => x.Slot), Origins(functions[1].Parameters[0].Type.BoundType!).Select(x => x.Slot));
        Assert.True(c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("func inspect(value: View<i32>) => ()\nlet f: (View<i32>{v}) -> () = inspect\n    origin v.source == static", true)]
    [InlineData("func inspect(value: ref{static}/i32) => ()\nlet f: (ref/i32) -> () = inspect", false)]
    [InlineData("func inspect(value: ref/i32) => ()\nlet f: (ref{static}/i32) -> () = inspect", true)]
    [InlineData("func inspect() -> i32 => 1\nlet f: (i32) -> i32 = inspect", false)]
    [InlineData("func inspect(value: i32) -> i32 => value\nlet f: (i32) -> bool = inspect", false)]
    public void FunctionReferencesCheckCompleteOriginContracts(string source, bool valid)
    {
        var compilation = MinimalEmissionTest.Analyze(View + source);
        Assert.Equal(valid, compilation.Binding.Result.IsComplete);
        Assert.Equal(valid, Reload(compilation).Bind().IsComplete);
    }

    [Fact]
    public void SpecializationWithAnAnonymousAggregateOriginExecutes()
    {
        const string Source = "struct Counter\n    public var value: i32 = 7\nstruct View<T> {source}\n    public let value: ref{source}/T\n    public init(value: ref{source}/T) => self.value = value\nfunc read<T>(view: ref/View<T>) -> i32 => 1\nspecialize func read<Counter>(view: ref/View<Counter>) -> i32 => view.value.value\nlet counter = Counter.init()\nlet view = View<Counter>.init(counter@ref)\nif read(view@ref) != 7 => $abort(\"specialization\")\n";
        var compilation = MinimalEmissionTest.Analyze(Source);
        Assert.True(compilation.Ownership.Result.IsVerified, string.Join("\n", compilation.Binding.Obligations) + "\n" + string.Join("\n", compilation.Ownership.ControlFlow!.PendingBinding));
        ScalarEmissionTest.EmitFixture("AnonymousOriginSpecialization", Source, string.Empty);
    }

    [Fact]
    public void AnonymousAggregateBorrowExecutesAndKeepsItsLoan()
    {
        const string Source = "struct Counter\n    public var value: i32 = 7\nstruct View {source}\n    public let counter: ref{source}/Counter\n    public init(counter: ref{source}/Counter) => self.counter = counter\nfunc read(view: ref/View) -> i32 => view.counter.value\nvar counter = Counter.init()\nlet view = View.init(counter@ref)\nif read(view@ref) != 7 => $abort(\"bad value\")\n";
        var valid = MinimalEmissionTest.Analyze(Source);
        Assert.True(valid.Ownership.Result.IsVerified, string.Join("\n", valid.Binding.Obligations) + "\n" + string.Join("\n", valid.Ownership.ControlFlow!.Issues) + "\n" + string.Join("\n", valid.Ownership.ControlFlow.PendingBinding));
        ScalarEmissionTest.EmitFixture("AnonymousAggregateOrigin", Source, string.Empty);
        valid = Reload(valid);
        for (var i = 0; i < 3; i++)
        {
            Assert.True(valid.Bind().IsComplete);
            valid.Binding.CheckStartup(OutputKind.Application);
            Assert.True(valid.Ownership.Analyze().IsVerified);
            Assert.True(valid.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        for (var i = 0; i < 32; i++)
        {
            valid.Ownership.Analyze();
            valid.Emission.WriteIr(TextWriter.Null, out _);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 16; i++)
        {
            valid.Ownership.Analyze();
            valid.Emission.WriteIr(TextWriter.Null, out _);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        var invalid = MinimalEmissionTest.Analyze(Source.Replace("if read", "counter.value = 8\nif read", StringComparison.Ordinal));
        Assert.False(invalid.Emission.Validate(out _));
    }

    private static Compilation Reload(Compilation source)
    {
        var bytes = Tinyhand.TinyhandSerializer.Serialize(source.Kotonoha);
        var compilation = Compilation.CreateForTest();
        Assert.True(compilation.Prepare(WindowsProfile.Target));
        var tree = compilation.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(bytes, ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(compilation);
        return compilation;
    }

    private static IEnumerable<BoundOrigin> Origins(BoundType type)
    {
        if (type.Origin is { } origin)
        {
            yield return origin;
        }

        foreach (var argument in type.OriginArguments)
        {
            yield return argument;
        }

        foreach (var component in type.Components)
        {
            foreach (var nested in Origins(component))
            {
                yield return nested;
            }
        }
    }
}
