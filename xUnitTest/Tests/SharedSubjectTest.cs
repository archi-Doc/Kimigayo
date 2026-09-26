// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 14.6.2 and 15.1.6 subject rule: a bare match Subject or for iterable is shared-borrowed, @move transfers
/// it, and an explicit or existing exclusive borrow keeps its exclusive mode.</summary>
public class SharedSubjectTest
{
    private const string Message = "enum Message\n    Write(string)\n    Quit\n";

    private const string RefArraySource =
        "func total(values: ref/Array<i32>) -> i32\n    var sum: i32 = 0\n    for v in values\n        sum += v\n    return sum\n" +
        "let values: Array<i32> = [1, 2, 3]\nvar direct: i32 = 0\nfor v in values@ref\n    direct += v\nfor v in (values@ref)\n    direct += v\n" +
        "require total(values) == 6 and direct == 12 and values.length == 3 else => $abort(\"shared\")\nConsole.writeLine(\"ok\")";

    private const string UniqArraySource =
        "func grow(values: uniq/Array<i32>) -> i32\n    var sum: i32 = 0\n    for v in values\n        sum += v\n    values.append(4)\n    for v in values\n        sum += v\n    return sum\n" +
        "var values: Array<i32> = [1, 2, 3]\nrequire grow(values@uniq) == 16 and values.length == 4 else => $abort(\"reborrow\")\nConsole.writeLine(\"ok\")";

    private const string UniqArrayTransferSource =
        "func total(values: uniq/Array<i32>) -> i32\n    var sum: i32 = 0\n    for v in values@move\n        sum += v\n    return sum\n" +
        "var values: Array<i32> = [1, 2, 3]\nrequire total(values@uniq) == 6 and values.length == 3 else => $abort(\"transfer\")\nConsole.writeLine(\"ok\")";

    private const string FixedArraySource =
        "func total(values: ref/[3 of i32]) -> i32\n    var sum: i32 = 0\n    for v in values\n        sum += v\n    return sum\n" +
        "let fixed: [3 of i32] = [1, 2, 3]\nlet grid: [2 of [2 of i32]] = [[1, 2], [3, 4]]\nvar nested: i32 = 0\nfor row in grid\n    for v in row\n        nested += v\n" +
        "require total(fixed) == 6 and nested == 10 else => $abort(\"fixed\")\nConsole.writeLine(\"ok\")";

    private const string StringsSource =
        "func pick(names: ref/Array<string>) -> ref/string\n    var best: ref/string = names[0]@ref\n    for name in names\n        if name == \"bb\" => best = name\n    return best\n" +
        "let names: Array<string> = [\"a\", \"bb\", \"c\"]\nConsole.writeLine(pick(names))\nlet pairs: Array<(i32, string)> = [(1, \"x\"), (2, \"y\")]\nlet view = pairs@ref\n" +
        "for (n, text) in view\n    require n > 0 else => $abort(\"pair\")\n    Console.writeLine(text)\nConsole.writeLine(names[1])";

    private const string UniqMatchSource =
        Message +
        "func show(m: uniq/Message) -> i32\n    match m\n        .Write(let text) => Console.writeLine(text)\n        .Quit => ()\n    match m\n        .Write(_) => return 1\n        .Quit => return 0\n" +
        "func keep(m: uniq/Message) -> ref/string during m\n    match m\n        .Write(let text) => return text\n        .Quit => $abort(\"quit\")\n" +
        "var message: Message = .Write(\"hi\")\nrequire show(message@uniq) == 1 else => $abort(\"show\")\nConsole.writeLine(keep(message@uniq))\n" +
        "let r = message@uniq\nmatch r\n    .Write(let text) => Console.writeLine(text)\n    .Quit => ()\nmatch message@move\n    .Write(let text) => Console.writeLine(text@move)\n    .Quit => ()";

    private const string TaskDeclarations =
        "struct Inner\n    public let label: string\n    public init(label: string) => self.label = label@move\n" +
        "struct Task\n    public let id: i32\n    public let name: string\n    public let inner: Inner\n" +
        "    public init(id: i32, name: string)\n        self.id = id\n        self.name = name@move\n        self.inner = Inner.init(\"inner\")\n" +
        "func same(s: ref/string) -> ref/string during s => s\n";

    private const string FieldsSource =
        TaskDeclarations +
        "func label(t: ref/Task) -> ref/string during t => same(t.inner.label)\nfunc show(t: uniq/Task) => Console.writeLine(t.name)\n" +
        "var tasks: Array<Task> = [Task.init(1, \"one\"), Task.init(2, \"two\")]\nfor task in tasks\n    let kept = same(task.name)\n    Console.writeLine(kept)\n    Console.writeLine(task.inner.label)\n" +
        "let pairs: [2 of (i32, string)] = [(1, \"a\"), (2, \"b\")]\nfor pair in pairs\n    Console.writeLine(pair.1)\n" +
        "var single = Task.init(3, \"three\")\nConsole.writeLine(label(single))\nshow(single@uniq)\ntasks.append(single@move)\nrequire tasks.length == 3 else => $abort(\"length\")";

    private const string DictionarySource =
        "func total(d: ref/Dictionary<i32, i32>) -> i32\n    var sum: i32 = 0\n    for (k, v) in d\n        sum += k + v\n    return sum\n" +
        "func grow(d: uniq/Dictionary<i32, i32>) -> i32\n    var sum: i32 = 0\n    for (k, v) in d\n        sum += v\n    return sum\n" +
        "var d: Dictionary<i32, i32> = [:]\n_ = d.tryInsert(1, 10)\n_ = d.tryInsert(2, 20)\nvar s: i32 = 0\nfor (k, v) in d\n    s += v\nfor (k, v) in d@ref\n    s += v\n" +
        "let fixed: [2 of i32] = [1, 2]\nfor v in fixed@ref\n    s += v\n" +
        "require s == 63 and total(d) == 33 and grow(d@uniq) == 30 and d.length == 2 else => $abort(\"dictionary\")\nConsole.writeLine(\"ok\")";

    // SPEC 15.1.6: a Subject is acquired as written and only a bare Place is borrowed. Temporaries are owned
    // ByValue Subjects, E@owner copies a Copy Place, and a transferred uniq value keeps its Exclusive mode.
    private const string SubjectAcquisitionSource =
        Message + "func make() -> Message => .Write(\"made\")\nfunc makeAll() -> Array<string> => [\"a\", \"b\"]\n" +
        "func same(a: ref/string, b: ref/string) -> bool => a == b\n" +
        "func total(values: uniq/Array<i32>) -> i32\n    var sum: i32 = 0\n    for v in values@move\n        v@deref += 1\n        sum += v\n    return sum\n" +
        "match make()\n    .Write(let text)\n        let owned: string = text@move\n        Console.writeLine(owned)\n    .Quit => ()\n" +
        "var count: i32 = 5\nmatch count@owner\n    var n\n        n += 1\n        require n == 6 else => $abort(\"copy\")\nrequire count == 5 else => $abort(\"count\")\n" +
        "for s in makeAll()\n    let owned: string = s@move\n    Console.writeLine(owned)\n" +
        "match \"hello\"\n    let text if same(text, \"hello\") => Console.writeLine(text)\n    _ => ()\n" +
        "var numbers: Array<i32> = [1, 2]\nrequire total(numbers@uniq) == 5 and numbers[0] == 2 and numbers[1] == 3 else => $abort(\"exclusive\")\nConsole.writeLine(\"ok\")";

    private const string UniqLiteralSource =
        "func f(x: uniq/i32) -> i32 => match x\n    0 => 10\n    _ => 20\n" +
        "var n: i32 = 0\nrequire f(n@uniq) == 10 else => $abort(\"zero\")\nn = 3\nrequire f(n@uniq) == 20 and n == 3 else => $abort(\"other\")\nConsole.writeLine(\"ok\")";

    // SPEC 14.6.2: values@uniq and an exclusive borrow value enumerate uniq/E items; Tuple items decompose into
    // uniq components; a Dictionary yields (ref/K, uniq/V); reborrows inside the body suspend the item.
    private const string UniqIterationSource =
        "func bump(values: uniq/Array<i32>)\n    for v in values\n        v@deref += 100\n" +
        "var values: Array<i32> = [1, 2, 3]\nfor v in values@uniq\n    v@deref += 1\n    let w = v@deref@ref\n    require w == v else => $abort(\"reborrow\")\n" +
        "bump(values@uniq)\nvar fixed: [2 of i32] = [10, 20]\nfor v in fixed@uniq\n    v@deref *= 2\n" +
        "var pairs: Array<(i32, i32)> = [(1, 2), (3, 4)]\nfor (a, b) in pairs@uniq\n    a@deref += b\nfor pair in pairs@uniq\n    pair.1 += pair.0\n" +
        "var named: [2 of (i32, string)] = [(1, \"a\"), (2, \"b\")]\nfor (n, text) in named@uniq\n    n@deref += 1\n    Console.writeLine(text)\n" +
        "var d: Dictionary<i32, i32> = [:]\n_ = d.tryInsert(1, 10)\n_ = d.tryInsert(2, 20)\nfor (k, v) in d@uniq\n    v@deref += k\n" +
        "var total: i32 = 0\nfor v in values\n    total += v\nfor v in fixed\n    total += v\nfor (a, b) in pairs\n    total += a + b\nfor (n, _) in named\n    total += n\nfor (_, v) in d\n    total += v\n" +
        "for var m in values@move\n    m += 1\n    total += m\n" +
        "require total == 309 + 60 + 26 + 5 + 33 + 312 else => $abort(\"total\")\nConsole.writeLine(\"ok\")";

    [Theory]
    [InlineData("RefArray", RefArraySource, "ok\n")]
    [InlineData("UniqArray", UniqArraySource, "ok\n")]
    [InlineData("UniqArrayTransfer", UniqArrayTransferSource, "ok\n")]
    [InlineData("FixedArray", FixedArraySource, "ok\n")]
    [InlineData("Strings", StringsSource, "bb\nx\ny\nbb\n")]
    [InlineData("Fields", FieldsSource, "one\ninner\ntwo\ninner\na\nb\ninner\nthree\n")]
    [InlineData("Dictionary", DictionarySource, "ok\n")]
    public void SharedIterationReadsThroughBorrowValuesAndFields(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("SharedSubjectIteration" + name, source, stdout);

    [Theory]
    [InlineData("UniqMatch", UniqMatchSource, "hi\nhi\nhi\nhi\n")]
    [InlineData("UniqLiteral", UniqLiteralSource, "ok\n")]
    [InlineData("Acquisition", SubjectAcquisitionSource, "made\na\nb\nhello\nok\n")]
    [InlineData("UniqIteration", UniqIterationSource, "a\nb\nok\n")]
    public void ExclusiveBorrowValuesKeepTheirMode(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("SharedSubject" + name, source, stdout);

    [Theory]
    [InlineData(Message + "var message: Message = .Write(\"hi\")\nmatch message\n    .Write(let text)\n        let owned: string = text@move\n    .Quit => ()")]
    [InlineData("func makeAll() -> Array<string> => [\"a\"]\nlet names = makeAll()\nfor s in names\n    let owned: string = s@move")]
    public void BarePlaceSubjectsStayBorrowed(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.TypeMismatch_Kd);
    }

    [Theory]
    [InlineData("var values: Array<i32> = [1, 2, 3]\nfor v in values@uniq\n    values.append(4)")]
    [InlineData("var values: Array<i32> = [1, 2, 3]\nfor v in values@uniq\n    let first = values[0]")]
    [InlineData("var d: Dictionary<i32, i32> = [:]\nfor (k, v) in d@uniq\n    let n = d.length")]
    public void ExclusiveIterationKeepsItsLoan(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
    }

    [Theory]
    [InlineData("var values: Array<i32> = [1, 2, 3]\nfor v in values\n    v@deref += 1")]
    [InlineData("func f(values: ref/Array<i32>)\n    for v in values\n        v@deref = 1")]
    [InlineData("let values: Array<i32> = [1, 2, 3]\nfor v in values@uniq\n    v@deref += 1")]
    public void SharedItemsCannotBeWritten(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code is DiagnosticCode.SharedPathAccess_Kd or DiagnosticCode.InvalidAssignment_Kd);
    }

    [Theory]
    [InlineData("func total(values: uniq/Array<i32>) -> i32\n    var sum: i32 = 0\n    for v in values@move\n        sum += v\n    values.append(4)\n    return sum")]
    [InlineData(Message + "func show(m: uniq/Message)\n    match m@move\n        .Write(let text) => Console.writeLine(text)\n        .Quit => ()\n    match m\n        _ => ()")]
    public void TransferredBorrowValuesAreConsumed(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.PossiblyMovedUse);
    }

    [Theory]
    [InlineData("var values: Array<i32> = [1]\nfor v in values@uniq\n    Console.writeLine(\"x\")")]
    [InlineData("var values: Array<i32> = [1]\nfor v in (values@uniq)\n    Console.writeLine(\"x\")")]
    [InlineData("var values: Array<i32> = [1]\nfor v in values@uniq/Array<i32>\n    Console.writeLine(\"x\")")]
    [InlineData("var values: [2 of i32] = [1, 2]\nfor v in values@uniq\n    Console.writeLine(\"x\")")]
    [InlineData(Message + "var message: Message = .Write(\"hi\")\nmatch message@uniq\n    .Write(let text) => Console.writeLine(text)\n    .Quit => ()")]
    [InlineData(Message + "var message: Message = .Write(\"hi\")\nmatch (message@uniq)\n    .Write(let text) => Console.writeLine(text)\n    .Quit => ()")]
    [InlineData(Message + "var message: Message = .Write(\"hi\")\nmatch message@uniq/Message\n    .Write(let text) => Console.writeLine(text)\n    .Quit => ()")]
    [InlineData(Message + "func edit(m: uniq/Message)\n    match m@deref@uniq\n        .Write(let text) => Console.writeLine(text)\n        .Quit => ()")]
    [InlineData("struct Cell\n    public var value: i32 = 1\nvar o = Kimi.Intrinsics.makeObj(Cell.init())\nmatch o@objuniq\n    _ => ()")]
    [InlineData("struct Cell\n    public var value: i32 = 1\nvar o = Kimi.Intrinsics.makeObj(Cell.init())\nmatch o@deref@uniq\n    _ => ()")]
    public void ExclusiveBorrowSubjectsAreAccepted(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("func total(values: uniq/Array<i32>) -> i32\n    var sum: i32 = 0\n    for v in values\n        sum += v\n        values.append(4)\n    return sum")]
    [InlineData("func last(values: uniq/Array<i32>) -> ref/i32\n    var kept: ref/i32 = values[0]@ref\n    for v in values\n        kept = v\n    values.append(4)\n    return kept")]
    [InlineData(TaskDeclarations + "var t = Task.init(1, \"one\")\nlet r = t@ref\nlet kept = same(r.name)\nt = Task.init(2, \"two\")\nConsole.writeLine(kept)")]
    [InlineData(TaskDeclarations + "var tasks: Array<Task> = [Task.init(1, \"one\")]\nfor task in tasks\n    let kept = same(task.name)\n    tasks.append(Task.init(2, \"two\"))\n    Console.writeLine(kept)")]
    public void SharedReborrowsKeepTheirLoans(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
    }

    [Fact]
    public void BorrowedSlicesAreReadAsTheirCopyValue()
    {
        var c = MinimalEmissionTest.Analyze("func total(values: ref/Slice<i32>) -> i32\n    var sum: i32 = 0\n    for v in values\n        sum += v\n    return sum");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Binding.ReadsReferent(FindIterable(c)));
    }

    [Fact]
    public void UnsupportedIterablesReportOnlyTheBoundary()
    {
        var c = MinimalEmissionTest.Analyze("struct Box\n    var id: i32 = 0\nlet box = Box.init()\nvar sum: i32 = 0\nfor v in box\n    sum += v");
        Assert.False(c.Binding.Result.IsComplete);
        var issue = Assert.Single(c.Binding.Issues);
        Assert.Equal(DiagnosticCode.UnsupportedBinding_Kd, issue.Code);
    }

    private static Koto FindIterable(Compilation c) => Walk(c.Kotonoha.RootKoto).OfType<ForKoto>().Single().Iterable;

    private static IEnumerable<Koto> Walk(Koto node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
        {
            foreach (var nested in Walk(child))
            {
                yield return nested;
            }
        }
    }
}
