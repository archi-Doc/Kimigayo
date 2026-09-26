// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 4.6.9, 8.4: a Contract declares Type parameters, and receiver[key] on a user Type resolves through its
/// Indexable&lt;Key&gt; conformance: index for reads and shared borrows, indexUniq for updates and exclusive borrows.</summary>
public class IndexableContractTest
{
    private const string Pair =
        "struct Pair<T>\n    Self is UniqIndexable<isize>\n    associate Element is T\n    var first: T\n    var second: T\n" +
        "    public init(first: T, second: T)\n        self.first = first@move\n        self.second = second@move\n" +
        "    public func index(self, key: ref/isize) -> place ref/T during self\n        if key == 0 => return self.first\n        return self.second\n" +
        "    public func indexUniq(self: uniq/Self, key: ref/isize) -> place uniq/T during self\n        if key == 0 => return self.first\n        return self.second\n";

    private const string Shared =
        "struct View\n    Self is Indexable<isize>\n    associate Element is i32\n    var value: i32\n    public init(value: i32) => self.value = value\n" +
        "    public func index(self, key: ref/isize) -> place ref/i32 during self => self.value\n";

    // SPEC 8.4.2: a Constraint on a bound Contract reference indexes a Type parameter through the requirement.
    private const string FirstPlace =
        "func firstPlace<S>(items: ref/S) -> place ref/S.Element during items\n    S is Indexable<isize>\n    return items[0]\n";

    private const string FirstUniq =
        "func firstUniq<S>(items: uniq/S) -> place uniq/S.Element during items\n    S is UniqIndexable<isize>\n    return items[0]\n";

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "CopyRead", Pair + "var pair = Pair<i32>.init(1, 2)\nlet second = pair[1]\nlet key: isize = 0\nrequire second == 2 and pair[key] == 1 else => $abort(\"read\")\npair[key] = 5\nrequire pair[0] == 5 else => $abort(\"write\")\nConsole.writeLine(\"ok\")", "ok\n" },
        { "SharedBorrow", Pair + "var pair = Pair<string>.init(\"Pair first.\", \"Pair second.\")\nConsole.writeLine(pair[1])\nlet view = pair[0]@ref\nConsole.writeLine(view)\nlet annotated: ref/string = pair[1]\nConsole.writeLine(annotated)", "Pair second.\nPair first.\nPair second.\n" },
        { "ExclusiveWrite", Pair + "var counts = Pair<i32>.init(1, 2)\ncounts[1] += 40\nlet exclusive = counts[0]@uniq\nexclusive@follow = 7\nrequire counts[1] == 42 and counts[0] == 7 else => $abort(\"write\")\ncounts[0] = counts[1] + 1\nrequire counts[0] == 43 else => $abort(\"replace\")\nConsole.writeLine(\"Pair replaced.\")\nvar names = Pair<string>.init(\"Pair first.\", \"Pair second.\")\nlet lent = names[1]@uniq\nConsole.writeLine(lent)", "Pair replaced.\nPair second.\n" },
        { "ThroughReferences", Pair + "func show(pair: ref/Pair<string>) => Console.writeLine(pair[1])\nfunc bump(pair: uniq/Pair<i32>) => pair[0] += 1\nvar names = Pair<string>.init(\"a\", \"b\")\nshow(names)\nvar counts = Pair<i32>.init(41, 0)\nbump(counts@uniq)\nrequire counts[0] == 42 else => $abort(\"bump\")\nConsole.writeLine(\"ok\")", "b\nok\n" },
        { "SharedOnly", Shared + "let view = View.init(42)\nrequire view[0] == 42 else => $abort(\"shared\")\nConsole.writeLine(\"ok\")", "ok\n" },
        { "GenericShared", Pair + Shared + FirstPlace + "var pair = Pair<string>.init(\"Pair first.\", \"Pair second.\")\nConsole.writeLine(firstPlace(pair))\nlet counts = Pair<i32>.init(7, 8)\nlet view = View.init(9)\nrequire firstPlace(counts) == 7 and firstPlace(view) == 9 else => $abort(\"generic\")\nlet lent = firstPlace(pair)@ref\nConsole.writeLine(lent)", "Pair first.\nPair first.\n" },
        { "GenericExclusive", Pair + FirstUniq + "var counts = Pair<i32>.init(1, 2)\nfirstUniq(counts@uniq) = 40\nfirstUniq(counts@uniq) += 2\nrequire counts[0] == 42 and counts[1] == 2 else => $abort(\"uniq\")\nConsole.writeLine(\"ok\")", "ok\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Executes(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("Indexable" + name, source, stdout);

    [Theory]
    [InlineData(Pair + "let pair = Pair<i32>.init(1, 2)\npair[0] = 3", DiagnosticCode.InvalidAssignment_Kd)]
    [InlineData(Pair + "func f(pair: ref/Pair<i32>) => pair[0] = 3", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(Pair + "var pair = Pair<string>.init(\"a\", \"b\")\nlet taken = pair[0]@move", DiagnosticCode.ExclusivePathTake_Kd)]
    [InlineData(Shared + "var view = View.init(1)\nview[0] = 2", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(Shared + "var view = View.init(1)\nlet exclusive = view[0]@uniq", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData("struct Box\n    var value: i32\n    public init(value: i32) => self.value = value\n    public func index(self, key: ref/isize) -> place ref/i32 during self => self.value\nlet box = Box.init(1)\nlet read = box[0]", DiagnosticCode.UnsupportedBinding_Kd)]
    [InlineData("struct Wrong\n    Self is Indexable<isize>\n    associate Element is string\n    var value: i32\n    public init(value: i32) => self.value = value\n    public func index(self, key: ref/isize) -> place ref/i32 during self => self.value", DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("struct Missing\n    Self is UniqIndexable<isize>\n    associate Element is i32\n    var value: i32\n    public init(value: i32) => self.value = value\n    public func index(self, key: ref/isize) -> place ref/i32 during self => self.value", DiagnosticCode.MissingContractImplementation_Kd)]
    [InlineData("struct Value\n    Self is Indexable<isize>\n    associate Element is i32\n    var value: i32\n    public init(value: i32) => self.value = value\n    public func index(self, key: ref/isize) -> i32 => self.value", DiagnosticCode.IncompatibleContractImplementation_Kd)]
    [InlineData("func f<S>(items: uniq/S) -> place uniq/S.Element during items\n    S is Indexable<isize>\n    return items[0]", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(Shared + FirstUniq + "var view = View.init(1)\nfirstUniq(view@uniq) = 2", DiagnosticCode.NoApplicableOverload_Kd)]
    public void RejectsAtBinding(string source, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
    }

    [Theory]
    [InlineData(Pair + "var pair = Pair<string>.init(\"a\", \"b\")\nlet bare = pair[0]")]
    [InlineData(Pair + "var pair = Pair<i32>.init(1, 2)\nlet view = pair[0]@ref\npair[0] = 3\nrequire view == 1 else => $abort(\"x\")")]
    [InlineData(Pair + "var pair = Pair<string>.init(\"a\", \"b\")\npair[0] = \"c\"")] // Non-Copy replacement through a Place shares the reference-write boundary (STATUS).
    public void RejectsAtOwnership(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
    }

    [Fact]
    public void BoundContractReferencesKeepDistinctIdentities()
    {
        var c = MinimalEmissionTest.Analyze(Pair + "var pair = Pair<i32>.init(1, 2)\nrequire pair[0] == 1 else => $abort(\"x\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var uniqIndexable = c.Library.GetSymbol(KimiDeclarationId.UniqIndexable)!;
        Assert.Contains(uniqIndexable.Contract!.Ancestors, x => ReferenceEquals(x.Declaration, c.Library.GetSymbol(KimiDeclarationId.Indexable)!.Declaration) && !ReferenceEquals(x, c.Library.GetSymbol(KimiDeclarationId.Indexable)));
    }
}
