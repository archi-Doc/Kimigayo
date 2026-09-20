// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ReceiverShorthandTest
{
    [Theory]
    [InlineData("struct S", " => self")]
    [InlineData("struct S<T> {source}", " => self")]
    [InlineData("enum S\n    A", " => self")]
    [InlineData("contract S", "")]
    public void BareSelfMatchesExplicitSharedReceiverAndResultOrigin(string header, string body)
    {
        var c = Parse($"{header}\n    func shortForm(self) -> ref/Self{body}\n    func explicitForm(self: ref/Self) -> ref/Self{body}");
        AssertComplete(c);
        var functions = Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Where(x => x.Name is "shortForm" or "explicitForm").ToArray();
        Assert.Equal(2, functions.Length);
        foreach (var function in functions)
        {
            Assert.Equal(0, function.BoundSymbol!.ReceiverIndex);
            var receiver = function.Parameters[0].Type.BoundType!;
            Assert.Equal(SemanticsKind.Ref, receiver.Semantics);
            Assert.Same(function, receiver.Origin!.Binder);
            Assert.Equal(0, receiver.Origin.Slot);
            Assert.Same(receiver, function.ReturnType!.BoundType);
        }

        Assert.Same(functions[0].Parameters[0].Type.BoundType!.Components[0], functions[1].Parameters[0].Type.BoundType!.Components[0]);
    }

    [Fact]
    public void MemberAndUnboundCallsKeepTheWrittenReceiverPosition()
    {
        var c = Parse("struct S\n    public func read(x?: i32, self) -> i32 => x\n    public func constant() -> i32 => 7\nfunc use(s?: ref/S) -> i32 => s.read(1) + S.read(2, s) + S.constant()");
        AssertComplete(c);
        var functions = Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().ToArray();
        Assert.Equal(1, functions.Single(x => x.Name == "read").BoundSymbol!.ReceiverIndex);
        Assert.Equal(-1, functions.Single(x => x.Name == "constant").BoundSymbol!.ReceiverIndex);
        var calls = Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().ToArray();
        Assert.Equal(3, calls.Length);
        Assert.All(calls, call => Assert.NotNull(call.BoundCall));
    }

    [Theory]
    [InlineData("func f(self) -> () => ()")]
    [InlineData("group G\n    func f(self) -> () => ()")]
    [InlineData("struct S\n    func outer(self)\n        func inner(self) => ()")]
    public void RejectsBareSelfOutsideAValidReceiver(string source)
    {
        var c = Parse(source);
        Assert.False(c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("struct S\n    func f(self, self) => ()")]
    [InlineData("struct S\n    func f(self = missing) => ()")]
    [InlineData("struct S\n    func f(self?) => ()")]
    [InlineData("struct S\n    func f(other => self) => ()")]
    [InlineData("struct S\n    init(self) => ()")]
    [InlineData("struct S\n    func f(value) => ()")]
    public void ShorthandDoesNotRelaxOtherParameterRules(string source)
    {
        var c = Compilation.CreateForTest();
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        Assert.NotEmpty(c.Kotonoha.DiagnosticCollection.GetArray());
    }

    [Theory]
    [InlineData("get() -> i32 => storage", true)]
    [InlineData("set(value: i32) -> () => storage = value", true)]
    [InlineData("get() -> i32 => true", false)]
    [InlineData("set(value: i64) -> () => ()", false)]
    [InlineData("get(self: uniq/Self) -> i32 => storage", false)]
    public void StoredAccessorsRetainSignatureChecks(string accessor, bool valid)
    {
        var c = Parse($"struct S\n    var item: i32\n        {accessor}");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Fact]
    public void ComputedAccessorsBindSelfAndPreserveExplicitReceiverOverrides()
    {
        var c = Parse("struct S\n    var measured: i32\n    computed item: i32\n        get() -> i32 => self.measured\n        set(value: i32) -> () => self.measured = value\n    computed exclusive: i32\n        get(self: uniq/Self) -> i32 => self.measured");
        AssertComplete(c);
        var properties = Walk(c.Kotonoha.RootKoto).OfType<PropertyKoto>().ToArray();
        var property = properties.Single(x => x.NameKoto.IdentifierName == "item").BoundSymbol!.Property!;
        AssertReceiver(property.Getter, SemanticsKind.Ref);
        AssertReceiver(property.Setter, SemanticsKind.Uniq);
        AssertReceiver(properties.Single(x => x.NameKoto.IdentifierName == "exclusive").BoundSymbol!.Property!.Getter, SemanticsKind.Uniq);
        Assert.All(Walk(property.Declaration).OfType<IdentifierNameKoto>().Where(x => x.IdentifierName == "self"), self => Assert.Equal(BindingSymbolKind.Parameter, self.BoundSymbol!.Kind));
        var receiver = property.Getter.Receiver;
        AssertComplete(c);
        Assert.Same(receiver, property.Getter.Receiver);
    }

    [Fact]
    public void OmittedAccessorReceiverParticipatesInOriginsAndConformance()
    {
        var c = Parse("contract C\n    func read(self) -> i32\n    property item: i32\n        get() -> i32\n        set(value: i32) -> ()\nstruct S\n    Self is C\n    public var item: i32\n        get() -> i32 => storage\n        set(value: i32) -> () => storage = value\n    public func read(self: ref/Self) -> i32 => 1\n    func view(self) -> ref{self}/Self => self");
        AssertComplete(c);
        foreach (var property in Walk(c.Kotonoha.RootKoto).OfType<PropertyKoto>())
        {
            Assert.True(property.BoundSymbol!.Property!.IsVerified);
            AssertReceiver(property.BoundSymbol.Property.Getter, SemanticsKind.Ref);
            AssertReceiver(property.BoundSymbol.Property.Setter, SemanticsKind.Uniq);
        }
    }

    [Fact]
    public void OmittedReceiverCompletesBorrowedAccessorResults()
    {
        var c = Parse("struct S {source}\n    var item: ref{source}/i32\n        get() -> ref{self.source}/i32 => storage\n    computed view: ref/Self\n        get() -> ref{self}/Self => self");
        AssertComplete(c);
        var getter = Walk(c.Kotonoha.RootKoto).OfType<PropertyKoto>().Single(x => x.NameKoto.IdentifierName == "view").BoundSymbol!.Property!.Getter;
        Assert.Same(getter.Receiver, getter.Result);
    }

    [Fact]
    public void WarmShorthandBindingAllocatesNothing()
    {
        var c = Parse("struct S\n    var item: i32\n        get() -> i32 => storage\n        set(value: i32) -> () => storage = value\n    func view(self) -> ref/Self => self");
        AssertComplete(c);
        for (var i = 0; i < 8; i++)
        {
            c.Binding.Bind(BindingMode.Final);
        }

        var allocated = AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final));
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Equal(0, allocated);
    }

    private static void AssertReceiver(BoundAccessor accessor, SemanticsKind semantics)
    {
        Assert.Equal(semantics, accessor.Receiver!.Semantics);
        Assert.Same(accessor.Declaration, accessor.Receiver.Origin!.Binder);
        Assert.Equal(0, accessor.Receiver.Origin.Slot);
    }

    private static Compilation Parse(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare("x86_64-pc-windows-msvc"));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        Assert.Empty(c.Kotonoha.DiagnosticCollection.GetArray());
        return c;
    }

    private static void AssertComplete(Compilation c)
        => Assert.True(c.Bind().IsComplete, string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}")));

    private static IEnumerable<Koto> Walk(Koto node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
        {
            foreach (var descendant in Walk(child))
            {
                yield return descendant;
            }
        }
    }
}
