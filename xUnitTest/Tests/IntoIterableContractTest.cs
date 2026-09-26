// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 22.1.2: Kimi.IntoIterable is declared in Kimigayo source and validated by shape and identity; the Item
/// requirement of Kimi.Iterator is the implementation's only complete-Type associated Type with Semantics.</summary>
public class IntoIterableContractTest
{
    private const string Counter =
        "struct Counter\n    Self is Iterator\n    associate Iterator.Item is i32\n    var n: i32 = 0\n" +
        "    public func next(self: uniq/Self) -> Option<i32>\n        require self.n < 3 else => return .None\n        self.n = self.n + 1\n        return .Some(self.n)\n";

    private const string Drain =
        "struct Drain<T>\n    Self is Iterator\n    associate Iterator.Item is T\n    var first: Option<T>\n" +
        "    public init(a: T) => self.first = .Some(a@move)\n    public func next(self: uniq/Self) -> Option<T> => Kimi.Intrinsics.exchange(self.first@uniq, with: .None)\n";

    private const string ConcreteSource =
        Counter +
        "struct Three\n    Self is IntoIterable\n    associate IntoIterable.IteratorType is Counter\n    public func intoIterator(self: Self) -> Counter => Counter.init()\n" +
        "var it = Three.init().intoIterator()\nvar total: i32 = 0\nloop\n    match it.next()\n        .Some(let v) => total += v\n        .None => exit\n" +
        "require total == 6 else => $abort(\"total\")\nConsole.writeLine(\"ok\")";

    [Fact]
    public void IntoIterableIsDeclaredInSourceAndValidated()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete, string.Join('\n', c.Binding.Issues));
        Assert.Equal(KimiDeclarationState.Validated, c.Library.GetDeclarationState(KimiDeclarationId.IntoIterable));
        Assert.Same(c.Library.IntoIterable, c.Library.GetSymbol(KimiDeclarationId.IntoIterable));
        var declaration = Assert.IsType<ContractKoto>(c.Library.IntoIterable.Declaration);
        Assert.EndsWith("/IntoIterable.kimi", declaration.CodeContext.SourceDocument!.Path);
    }

    [Theory]
    [InlineData("func extra(self: ref/Self) -> i32")]
    [InlineData("associate Extra")]
    public void ChangedIntoIterableShapesAreRejected(string member)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        c.Library.Kotonoha.CreateCodeContext().Parse((ContractKoto)c.Library.IntoIterable.Declaration, member);
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidKimiLibrary_Kd);
        Assert.Equal(KimiDeclarationState.Invalid, c.Library.GetDeclarationState(KimiDeclarationId.IntoIterable));
    }

    [Fact]
    public void ConcreteConformanceIteratesThroughItsMapping()
        => ScalarEmissionTest.EmitFixture("IntoIterableContractConcrete", ConcreteSource, "ok\n");

    [Fact]
    public void GenericItemUsesTheIteratorCompleteTypeException()
    {
        var kimi = MinimalEmissionTest.Analyze(Drain + "struct Batch<T>\n    Self is IntoIterable\n    associate IntoIterable.IteratorType is Drain<T>\n    let cursor: Drain<T>\n    public init(a: T) => self.cursor = Drain<T>.init(a@move)\n    public func intoIterator(self: Self) -> Drain<T> => self.cursor@move");
        Assert.True(kimi.Binding.Result.IsComplete, MinimalEmissionTest.Describe(kimi, null));

        // A user Contract of the same shape has no Item exception in this implementation (STATUS).
        var user = MinimalEmissionTest.Analyze(
            "public contract Source\n    associate Item\n    func next(self: uniq/Self) -> Option<Self.Item>\n" +
            "struct Pass<T>\n    Self is Source\n    associate Source.Item is T\n    var first: Option<T>\n    public func next(self: uniq/Self) -> Option<T> => Kimi.Intrinsics.exchange(self.first@uniq, with: .None)");
        Assert.False(user.Binding.Result.IsComplete);
        Assert.Contains(user.Binding.Issues, x => x.Code == DiagnosticCode.InvalidAssociatedType_Kd);
    }

    [Theory]
    [InlineData("struct Broken\n    Self is IntoIterable\n    public func intoIterator(self: Self) -> i32 => 0", DiagnosticCode.InvalidAssociatedType_Kd)]
    [InlineData(Counter + "struct Broken\n    Self is IntoIterable\n    associate IntoIterable.IteratorType is Counter", DiagnosticCode.MissingContractImplementation_Kd)]
    [InlineData("struct Broken\n    Self is IntoIterable\n    associate IntoIterable.IteratorType is i32\n    public func intoIterator(self: Self) -> i32 => 0", DiagnosticCode.UnprovenConstraint_Kd)]
    public void IncompleteConformancesAreRejected(string source, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
    }
}
