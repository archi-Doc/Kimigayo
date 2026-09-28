// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Linq;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 3.3.6, 15.3.1: a trailing during after a named Type binds the only slot of a one-slot schema, like a binding
/// set with an equality relation. It adds no borrow layer; several slots, no slots and unknown schemas are rejected.</summary>
public class SingleSlotBindingTest
{
    private const string View = "struct V<T> {source}\n    public let value: ref/T during source\n    public init(value: ref/T during source) => self.value = value\n";
    private const string Pair = "struct P {left, right}\n    public let first: ref/i32 during left\n    public let second: ref/i32 during right\n";

    [Theory]
    [InlineData("func identity(x: V<i32> during a) -> V<i32> during a => x@move")]
    [InlineData("func identity(x: V<i32>) -> V<i32> during x.source => x@move")]
    [InlineData("func identity(x: V<i32>{v}) -> V<i32> during v.source => x@move")]
    [InlineData("func wrap(x: ref/i32 during a) -> V<i32> during a => V<i32>.init(x)")]
    [InlineData("func maybe(x: V<i32>) -> V<i32>? during x.source => .Some(x@move)")]
    [InlineData("func tail(values: Slice<i32> during source) -> Slice<i32> during source => values[1..]")]
    [InlineData("struct W {s}\n    let value: V<i32> during s")]
    [InlineData("struct Headerless\n    let value: V<i32> during s")]
    [InlineData("func f(x: ref/i32)\n    let v: V<i32> during x = V<i32>.init(x)")]
    [InlineData("func nested(x: ref/(V<i32> during b) during a) -> ref/V<i32> during a => x@follow@ref")]
    public void BindsTheOnlySlot(string source)
    {
        var c = MinimalEmissionTest.Analyze(View + source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void TheBoundSlotIsTheWrittenOrigin()
    {
        var c = MinimalEmissionTest.Analyze(View + "func identity(x: V<i32> during a) -> V<i32> during a => x@move");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "identity");
        var input = function.Parameters[0].Type.BoundType!;
        var result = function.ReturnType!.BoundType!;
        Assert.Same(Assert.Single(input.OriginArguments), Assert.Single(result.OriginArguments));
        Assert.Equal(SemanticsKind.Owner, result.Semantics);
    }

    [Theory]
    [InlineData(Pair + "func f(x: P during a) => ()")]
    [InlineData("struct Plain\nfunc f(x: Plain during a) => ()")]
    [InlineData("func f<T>(x: T during a) => ()")]
    [InlineData("func f<T>(x: Option<ref/T during b> during a) => ()")]
    [InlineData("struct S {source}\n    let value: ref/i32 during source\n    func f(self: Self) -> Self during source => self")]
    public void RejectsOtherSchemas(string source)
    {
        var c = MinimalEmissionTest.Analyze(View + source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidOriginBinding_Kd);
    }

    private const string ExecutionSource =
        "struct View {source}\n    public let value: ref/i32 during source\n    public init(value: ref/i32 during source) => self.value = value\n" +
        "func make(value: ref/i32 during a) -> View during a => View.init(value)\n" +
        "func tail(values: Slice<i32> during source) -> Slice<i32> during source => values[1..]\n" +
        "let n: i32 = 5\nlet v = make(n@ref)\nlet numbers: Array<i32> = [1, 2, 3]\nlet rest = tail(numbers[..])\n" +
        "require v.value == 5 and rest.length == 2 and rest[0] == 2 else => $abort(\"slot\")\nConsole.writeLine(\"ok\")";

    [Fact]
    public void SlotBoundValuesExecute()
        => ScalarEmissionTest.EmitFixture("SingleSlotBinding", ExecutionSource, "ok\n");
}
