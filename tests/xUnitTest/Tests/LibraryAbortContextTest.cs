// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class LibraryAbortContextTest
{
    [Fact]
    public void SourceContextDeclarationsAreValidated()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete, string.Join("; ", c.Library.Declarations.ToArray().Where(x => x.State != KimiDeclarationState.Validated).Select(x => $"{x.Id}: {x.State}")));
        Assert.True(KimiLibraryCatalog.RequiresCallerLocation(c.Library.GetSymbol(KimiDeclarationId.RangeIteratorStarting)));
    }

    [Fact]
    public void RemovingTheOwnerInvalidatesItsSourceContextDeclarations()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        var owner = Assert.IsAssignableFrom<DeclarationContainerKoto>(c.Library.GetSymbol(KimiDeclarationId.RangeIteratorStarting)!.Scope.Owner);
        Assert.True(Assert.IsType<List<DeclarationContainerKoto>>(c.Library.Kotonoha.RootKoto.NestedContainers).Remove(owner));
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(KimiDeclarationState.Invalid, c.Library.GetDeclarationState(KimiDeclarationId.RangeIteratorStarting));
    }

    [Theory]
    [InlineData("HalfOpenShared", "let r = 3..1\nlet it = r.iterate()", "Hello.kimi:2:10")]
    [InlineData("HalfOpenExclusive", "var r = 3..1\nlet it = r.iterateUniq()", "Hello.kimi:2:10")]
    [InlineData("HalfOpenOwned", "let r = 3..1\nlet it = r.intoIterator()", "Hello.kimi:2:10")]
    [InlineData("ClosedShared", "let r = 3..=1\nlet it = r.iterate()", "Hello.kimi:2:10")]
    [InlineData("ClosedExclusive", "var r = 3..=1\nlet it = r.iterateUniq()", "Hello.kimi:2:10")]
    [InlineData("ClosedOwned", "let r = 3..=1\nlet it = r.intoIterator()", "Hello.kimi:2:10")]
    [InlineData("Position", "let p = ^4\nlet value = p.resolve(3)", "Hello.kimi:2:13")]
    [InlineData("Start", "let r = ..\nlet value = r.start.resolve(-1)", "Hello.kimi:2:13")]
    [InlineData("End", "let r = ..\nlet value = r.end.resolve(-1)", "Hello.kimi:2:13")]
    [InlineData("Range", "let r = 3..1\nlet value = r.resolve(4)", "Hello.kimi:2:13")]
    [InlineData("ClosedRange", "let r = 3..=1\nlet value = r.resolve(4)", "Hello.kimi:2:13")]
    [InlineData("ResolvedRange", "let r = (0..1).resolve(1)\nlet value = r.resolve(0)", "Hello.kimi:2:13")]
    [InlineData("Truncate", "var values = [1, 2]\nvalues.truncate(-1)", "Hello.kimi:2:1")]
    [InlineData("Generic", "func start<T>(range: Range<T, T>)\n    T is PrimitiveInteger\n    let it = range.iterate()\nstart(3..1)", "Hello.kimi:3:14")]
    public void StandardPreconditionsReportTheSourceOperation(string name, string source, string location)
        => ScalarEmissionTest.EmitFixture("LibraryAbortContext" + name, source, string.Empty, 1, location + ": abort KIMI_E_ARG_RANGE: Argument out of range\n");

    [Fact]
    public void UserAbortAndSameSpelledHelpersKeepTheirOwnMeaning()
        => ScalarEmissionTest.EmitFixture("LibraryAbortContextUser", "func argumentOutOfRange() -> i32 => 42\nrequire argumentOutOfRange() == 42 else => $abort(\"helper\")\n$abort(\"user\")", string.Empty, 1, "Hello.kimi:3:1: abort KIMI_E_ABORT: user\n");

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmSourceContextPreparationAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("let r = 0..3\nvar total = 0\nfor value in r => total += value\nrequire total == 3 else => $abort(\"sum\")");
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var failure), MinimalEmissionTest.Describe(c, failure));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid);
    }
}
