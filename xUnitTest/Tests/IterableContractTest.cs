// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 22.1 and 8.4.3: Kimi.Iterable is declared in Kimigayo source, validated by shape and identity,
/// and its Element requirement shares the complete-Type exception of Kimi.Iterator.</summary>
public class IterableContractTest
{
    private const string Counter =
        "struct Counter\n    Self is Iterator\n    associate Iterator.Element is i32\n    var n: i32 = 0\n" +
        "    public func next(self: uniq/Self) -> Option<i32>\n        require self.n < 3 else => return .None\n        self.n = self.n + 1\n        return .Some(self.n)\n";

    private const string Drain =
        "struct Drain<T>\n    Self is Iterator\n    associate Iterator.Element is T\n    var first: Option<T>\n" +
        "    public init(a: T) => self.first = .Some(a@move)\n    public func next(self: uniq/Self) -> Option<T> => Kimi.Intrinsics.exchange(self.first@uniq, with: .None)\n";

    private const string ConcreteSource =
        Counter +
        "struct Three\n    Self is Iterable\n    associate Iterable.Element is i32\n    associate Iterable.Iterator is Counter\n    public func iterate(self: Self) -> Counter => Counter.init()\n" +
        "var it = Three.init().iterate()\nvar total: i32 = 0\nloop\n    match it.next()\n        .Some(let v) => total += v\n        .None => exit\n" +
        "require total == 6 else => $abort(\"total\")\nConsole.writeLine(\"ok\")";

    [Fact]
    public void IterableIsDeclaredInSourceAndValidated()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete, string.Join('\n', c.Binding.Issues));
        Assert.Equal(KimiDeclarationState.Validated, c.Library.GetDeclarationState(KimiDeclarationId.Iterable));
        Assert.Same(c.Library.Iterable, c.Library.GetSymbol(KimiDeclarationId.Iterable));
        var declaration = Assert.IsType<ContractKoto>(c.Library.Iterable.Declaration);
        Assert.EndsWith("/Iterable.kimi", declaration.CodeContext.SourceDocument!.Path);
    }

    [Theory]
    [InlineData("func extra(self: ref/Self) -> i32")]
    [InlineData("associate Extra")]
    public void ChangedIterableShapesAreRejected(string member)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        c.Library.Kotonoha.CreateCodeContext().Parse((ContractKoto)c.Library.Iterable.Declaration, member);
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidKimiLibrary_Kd);
        Assert.Equal(KimiDeclarationState.Invalid, c.Library.GetDeclarationState(KimiDeclarationId.Iterable));
    }

    [Fact]
    public void ConcreteConformanceIteratesThroughItsMapping()
        => ScalarEmissionTest.EmitFixture("IterableContractConcrete", ConcreteSource, "ok\n");

    [Fact]
    public void GenericElementUsesTheIterableCompleteTypeException()
    {
        var kimi = MinimalEmissionTest.Analyze(Drain + "struct Batch<T>\n    Self is Iterable\n    associate Iterable.Element is T\n    associate Iterable.Iterator is Drain<T>\n    let cursor: Drain<T>\n    public init(a: T) => self.cursor = Drain<T>.init(a@move)\n    public func iterate(self: Self) -> Drain<T> => self.cursor@move");
        Assert.True(kimi.Binding.Result.IsComplete, MinimalEmissionTest.Describe(kimi, null));

        // A user Contract of the same shape has no Element exception (SPEC 8.4.3).
        var user = MinimalEmissionTest.Analyze(
            "public contract Source\n    associate Element\n    associate Iterator is ::Kimi.Iterator\n    Self.Iterator.Element is Self.Element\n    func iterate(self: owner/Self) -> Self.Iterator\n" +
            Drain + "struct Batch<T>\n    Self is Source\n    associate Source.Element is T\n    associate Source.Iterator is Drain<T>\n    let cursor: Drain<T>\n    public init(a: T) => self.cursor = Drain<T>.init(a@move)\n    public func iterate(self: Self) -> Drain<T> => self.cursor@move");
        Assert.False(user.Binding.Result.IsComplete);
        Assert.Contains(user.Binding.Issues, x => x.Code == DiagnosticCode.InvalidAssociatedType_Kd);
    }

    [Theory]
    [InlineData("struct Broken\n    Self is Iterable\n    associate Iterable.Element is i32", DiagnosticCode.InvalidAssociatedType_Kd)]
    [InlineData(Counter + "struct Broken\n    Self is Iterable\n    associate Iterable.Element is i32\n    associate Iterable.Iterator is Counter", DiagnosticCode.MissingContractImplementation_Kd)]
    [InlineData(Counter + "struct Broken\n    Self is Iterable\n    associate Iterable.Element is bool\n    associate Iterable.Iterator is Counter\n    public func iterate(self: Self) -> Counter => Counter.init()", DiagnosticCode.UnsatisfiedConstraint_Kd)]
    public void IncompleteConformancesAreRejected(string source, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
    }
}
