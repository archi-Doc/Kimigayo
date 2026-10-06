// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 7.1.1: a function publishes a Place with place ref/T or place uniq/T. The call designates that Place, so
/// a Copy value is read bare, a reference comes from @ref/@uniq or an expected reference, an exclusive Place is an
/// assignment target, and no Take is offered.</summary>
public class PlaceResultTest
{
    private const string First = "func first<T>(values: ref/Array<T>) -> place ref/T during values\n    return values[0]\n";
    private const string Slot = "func slot(values: uniq/Array<i32>) -> place uniq/i32 during values\n    return values[0]\n";
    private const string Cell =
        "struct Cell\n    var count: i32\n    var name: string\n    public init(count: i32, name: string)\n        self.count = count\n        self.name = name@move\n" +
        "    public func counter(self: uniq/Self) -> place uniq/i32 during self => self.count\n" +
        "    public func label(self) -> place ref/string during self => self.name\n";

    public static TheoryData<string, string, string> Fixtures => new()
    {
        { "CopyRead", First + "let values: Array<i32> = [42, 7]\nlet copied = first(values)\nrequire copied == 42 else => $abort(\"copy\")\nConsole.writeLine(\"ok\")", "ok\n" },
        { "SharedBorrow", First + "let names: Array<string> = [\"first\", \"second\"]\nConsole.writeLine(first(names))\nlet view = first(names)@ref\nConsole.writeLine(view)\nlet annotated: ref/string = first(names)\nConsole.writeLine(annotated)", "first\nfirst\nfirst\n" },
        { "ExclusiveWrite", Slot + "var values: Array<i32> = [1, 2]\nslot(values@uniq) = 41\nslot(values@uniq) += 1\nrequire values[0] == 42 and slot(values@uniq) == 42 else => $abort(\"write\")\nConsole.writeLine(\"ok\")", "ok\n" },
        { "ExclusiveBorrow", Slot + "var values: Array<i32> = [1, 2]\nlet exclusive = slot(values@uniq)@uniq\nexclusive@follow = 42\nrequire values[0] == 42 else => $abort(\"borrow\")\nConsole.writeLine(\"ok\")", "ok\n" },
        { "StructPlaces", Cell + "var cell = Cell.init(1, \"cell\")\ncell.counter() = 5\ncell.counter() += 1\nConsole.writeLine(cell.label())\nrequire cell.counter() == 6 else => $abort(\"counter\")", "cell\n" },
        { "Forwarded", First + "func second<T>(values: ref/Array<T>) -> place ref/T during values\n    return first(values)\nlet names: Array<string> = [\"only\"]\nConsole.writeLine(second(names))", "only\n" },
        { "TemporaryReceiver", First + "func make() -> Array<string> => [\"made\"]\nConsole.writeLine(first(make()))", "made\n" },
        // SPEC 4.6.9: an exclusive borrow of a dynamic Array element, owned or through uniq/Array, writes the element itself.
        { "ArrayElementUniq", "var a: Array<i32> = [1, 2]\nlet r = a[1]@uniq\nr@follow = 5\nrequire a[1] == 5 else => $abort(\"element\")\nConsole.writeLine(\"ok\")", "ok\n" },
        { "ArrayElementUniqThroughReference", "func bump(values: uniq/Array<i32>)\n    let r = values[0]@uniq\n    r@follow += 40\nvar a: Array<i32> = [2]\nbump(a@uniq)\nrequire a[0] == 42 else => $abort(\"element\")\nConsole.writeLine(\"ok\")", "ok\n" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Executes(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("PlaceResult" + name, source, stdout);

    [Theory]
    [InlineData(First + "let names: Array<string> = [\"a\"]\nlet taken = first(names)@move", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(Slot + "var values: Array<i32> = [1]\nlet taken = slot(values@uniq)@move", DiagnosticCode.ExclusivePathTake_Kd)]
    [InlineData(First + "var names: Array<string> = [\"a\"]\nfirst(names) = \"b\"", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData(First + "var names: Array<string> = [\"a\"]\nlet exclusive = first(names)@uniq", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData("func bad(values: ref/Array<i32>) -> place uniq/i32 during values\n    return values[0]", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData("func bad(values: ref/Array<i32>) -> place ref/i32 during values\n    return if true => values[0] else => values[1]", DiagnosticCode.PlaceRequired_Kd)]
    [InlineData("func bad() -> place ref/i32 during static => 42", DiagnosticCode.PlaceRequired_Kd)]
    [InlineData("func make() -> Array<i32> => [1]\nfunc bad() -> place ref/i32 during static\n    return make()[0]", DiagnosticCode.UnsatisfiedOriginRelation_Kd)]
    public void RejectsAtBinding(string source, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
    }

    [Theory]
    [InlineData(First + "let names: Array<string> = [\"a\"]\nlet bare = first(names)")]
    [InlineData("func local() -> place ref/i32 during static\n    let value: i32 = 1\n    return value")]
    [InlineData(First + "var names: Array<string> = [\"a\"]\nlet view = first(names)@ref\nnames@uniq.clear()\nConsole.writeLine(view)")]
    [InlineData("var a: Array<i32> = [1]\nlet r = a[0]@uniq\nlet s = a[0]\nr@follow = 2")]
    [InlineData("var a: Array<i32> = [1]\nlet r = a[0]@uniq\na@uniq.clear()\nr@follow = 2")]
    [InlineData("let a: Array<i32> = [1]\nlet r = a[0]@uniq")]
    public void RejectsAtOwnership(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
    }

    private const string ViaCallable = "func first(values: ref/Array<i32>) -> place ref/i32 during values\n    return values[0]\n\n" +
        "func viaCallable<F>(action: ref/F, values: ref/Array<i32>) -> i32\n    F is Callable<(ref/Array<i32>) -> place ref/i32>\n    return action(values)\n\n";

    [Theory]
    [InlineData(ViaCallable + "public func main() -> ()\n    let a: Array<i32> = [3, 4]\n    Console.writeLine(\"\\(first(a@ref))\")\n    let item = first\n    Console.writeLine(\"\\(item(a@ref))\")\n    Console.writeLine(\"\\(viaCallable(first, a@ref))\")\n    let f: (ref/Array<i32>) -> place ref/i32 = first\n    Console.writeLine(\"\\(f(a@ref))\")\n")]
    [InlineData(ViaCallable + "public func main() -> ()\n    let values: Array<i32> = [4]\n    require viaCallable(first, values) == 4 else => $abort(\"p\")\n")]
    [InlineData("public func main() -> ()\n    let values: Array<i32> = [4, 5]\n    let first = func (v: ref/Array<i32>) -> place ref/i32 => v[0]\n    require first(values@ref) == 4 else => $abort(\"a\")\n")]
    public void PlaceResultSignaturesAreSupported(string source)
        => Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);

    // A misspelled Type name in a Callable clause keeps its MissingName record as the clause's prerequisite.
    [Fact]
    public void AMisspelledCallableSignatureNameStaysThePrerequisite()
    {
        var source = "func apply<F>(action: ref/F) -> i32\n    F is Callable<(Strng) -> i32>\n    return action(\"a\")\n\npublic func main() => ()\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(("Strng", DiagnosticCategory.Language), (error.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty, error.Category));
    }
}
