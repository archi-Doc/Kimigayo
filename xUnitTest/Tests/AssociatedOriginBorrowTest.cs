// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class AssociatedOriginBorrowTest
{
    private const string SharedFamily = "contract C\n    associate Item(a) is ref/i32 during a\nstruct S\n    Self is C\nfunc f(x: ref/i32 during source) -> S.(C).Item(source) => x\n";

    [Theory]
    [InlineData("contract C\n    associate Item(a) is ref/i32 during a\nstruct S\n    Self is C")]
    [InlineData("contract C\n    associate Item(a)\nstruct S\n    Self is C\n    associate C.Item(b) is ref/i32 during b")]
    public void BorrowFamilySubstitutesApplicationOrigin(string declarations)
    {
        var c = MinimalEmissionTest.Analyze(declarations + "\nfunc f(x: ref/i32 during source) -> S.(C).Item(source) => x");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var f = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "f");
        Assert.Same(f.Parameters[0].Type.BoundType, f.ReturnType!.BoundType);
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("(a and b)")]
    public void GenericFixedFamilySubstitutesOriginExpressions(string argument)
    {
        var c = MinimalEmissionTest.Analyze("contract C\n    associate Item(step) is ref/i32 during step\nfunc f<T>(x: ref/i32 during a, y: ref/i32 during b) -> T.(C).Item(" + argument + ")\n    T is C\n    return x");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void EmitsSharedBorrow()
        => ScalarEmissionTest.EmitFixture("AssociatedOriginShared", SharedFamily + "var value = 7\nlet borrowed = f(value@ref)\nrequire borrowed == 7 else => $abort(\"shared\")\nConsole.writeLine(\"shared\")", "shared\n");

    [Fact]
    public void BorrowedResultKeepsSourceLoan()
    {
        var c = MinimalEmissionTest.Analyze(SharedFamily + "var value = 7\nlet borrowed = f(value@ref)\nvalue = 9\nlet observed: i32 = borrowed");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.NotEmpty(c.Ownership.Issues);
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void ApplicationCannotExtendInputLifetime()
    {
        var c = MinimalEmissionTest.Analyze(SharedFamily.Replace("Item(source)", "Item(static)", StringComparison.Ordinal));
        Assert.False(c.Binding.Result.IsComplete);
    }

    [Fact]
    public void EmitsExclusiveBorrow()
    {
        const string source = "contract C\n    associate Item(a) is uniq/i32 during a\nstruct S\n    Self is C\nfunc f(x: uniq/i32 during a) -> S.Item(a) => x@move\nvar value = 7\nlet borrowed = f(value@uniq)\nborrowed@follow = 9\nrequire value == 9 else => $abort(\"exclusive\")\nConsole.writeLine(\"exclusive\")";
        ScalarEmissionTest.EmitFixture("AssociatedOriginExclusive", source, "exclusive\n");
    }

    [Fact]
    public void EmitsTwoOriginTuple()
    {
        const string source = "contract C\n    associate Item(a, b)\nstruct S\n    Self is C\n    associate C.Item(left, right) is (ref/i32 during left, ref/i32 during right)\nfunc f(x: ref/i32 during a, y: ref/i32 during b) -> S.Item(a, b) => (x, y)\nlet x = 7\nlet y = 9\nlet pair = f(x@ref, y@ref)\nrequire pair.0 == 7 and pair.1 == 9 else => $abort(\"tuple\")\nConsole.writeLine(\"tuple\")";
        ScalarEmissionTest.EmitFixture("AssociatedOriginTuple", source, "tuple\n");
    }

    [Fact]
    public void EmitsBorrowedFixedArray()
    {
        const string source = "contract C\n    associate Item(a) is [1 of ref/i32 during a]\nstruct S\n    Self is C\nfunc f(x: [1 of ref/i32 during a]) -> S.Item(a) => x\nlet value = 7\nlet input: [1 of ref/i32] = [value@ref]\nlet result = f(input)\nrequire result[0] == 7 else => $abort(\"array\")\nConsole.writeLine(\"array\")";
        ScalarEmissionTest.EmitFixture("AssociatedOriginArray", source, "array\n");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" is ref/i32 during step")]
    public void EmitsGenericBorrowRequirement(string definition)
    {
        var source = "contract C\n    associate Item(step)" + definition + "\n    func borrow(self: ref/Self, value: ref/i32 during a) -> Self.Item(a)\nstruct S\n    Self is C\n    associate C.Item(b) is ref/i32 during b\n    public init() => ()\n    public func borrow(self: ref/Self, value: ref/i32 during source) -> ref/i32 during source => value\nfunc relay<T>(source: ref/T, value: ref/i32 during a) -> T.(C).Item(a)\n    T is C\n    return source.borrow(value)\nlet source = S.init()\nvar value = 7\nlet borrowed = relay(source@ref, value@ref)\nrequire borrowed == 7 else => $abort(\"generic borrow\")\nConsole.writeLine(\"generic borrow\")";
        ScalarEmissionTest.EmitFixture("AssociatedOriginGenericBorrow" + (definition.Length == 0 ? "Open" : "Fixed"), source, "generic borrow\n");
    }

    [Theory]
    [InlineData("contract C<T>\n    associate Item(a) is ref/T during a")]
    [InlineData("contract C\n    associate Item(a, b) is ref/(ref/i32 during a) during b")]
    [InlineData("contract C\n    associate Item(a) is ref/i32 during a\nstruct S\n    Self is C\n    associate C.Item(b) is uniq/i32 during b")]
    public void UnimplementedDomainsAndIncompatibleDefinitionsAreRejected(string source)
    {
        ParseTestHelper.ParseSuccess(source);
        Assert.False(MinimalEmissionTest.Analyze(source).Binding.Result.IsComplete);
    }

    [Fact]
    public void EditingApplicationRevokesAndRestoresTheOriginCertificate()
    {
        var c = MinimalEmissionTest.Analyze(SharedFamily + "let value = 7\nlet result = f(value@ref)");
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var application = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<OriginApplicationKoto>().Single(x => x.Type is MemberAccessKoto);
        var original = application.ArgumentNodes.Single();
        var donor = ParseTestHelper.ParseSuccess(SharedFamily.Replace("Item(source)", "Item(static)", StringComparison.Ordinal));
        var replacement = KotoTree.Walk(donor.RootKoto).OfType<OriginApplicationKoto>().Single(x => x.Type is MemberAccessKoto).ArgumentNodes.Single();
        Assert.True(KotoHelper.Replace(application, original, replacement));
        using var writer = new StringWriter();
        Assert.False(c.Emission.WriteIr(writer, out _));
        Assert.Empty(writer.ToString());
        Assert.False(c.Bind().IsComplete);
        Assert.True(KotoHelper.Replace(application, replacement, original));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
    }
}
