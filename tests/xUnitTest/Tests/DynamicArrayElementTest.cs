// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class DynamicArrayElementTest
{
    [Theory]
    [InlineData("Owned", "var values: Array<i32> = [10, 20, 12]\nvar total = 0\nfor i in values.indices => total += values[i]\nrequire total == 42 and values.length == 3 else => $abort(\"read\")\nvalues@uniq.append(7)\nrequire values[3] == 7 else => $abort(\"append\")")]
    [InlineData("Shared", "func at(values: ref/Array<i32>, i: isize) -> i32 => values[i]\nlet values: Array<i32> = [10, 42]\nrequire at(values@ref, 1) == 42 else => $abort(\"read\")")]
    [InlineData("Exclusive", "func at(values: uniq/Array<i32>) -> i32 => values[0] + values[1]\nvar values: Array<i32> = [20, 22]\nrequire at(values@uniq) == 42 else => $abort(\"read\")")]
    [InlineData("Temporary", "func make() -> Array<bool> => [false, true]\nrequire make()[1] else => $abort(\"read\")")]
    public void ReadsCopyElements(string name, string source)
        => ScalarEmissionTest.EmitFixture("DynamicArrayRead" + name, source, string.Empty);

    [Theory]
    [InlineData("Copy", "func sum(values: Array<i32>) -> i32\n    var total = 0\n    for i in values.indices => total += values[i]\n    return total\nrequire sum([10, 20, 12]) == 42 and sum([]) == 0 else => $abort(\"sum\")\nConsole.writeLine(\"done\")", "done\n")]
    [InlineData("NonCopy", "struct Task\n    public let id: i32\n    public init(id: i32) => self.id = id\n    drop => Console.writeLine(\"drop\")\nfunc last(values: Array<Task>) -> i32 => values[1].id\nrequire last([Task.init(1), Task.init(42)]) == 42 else => $abort(\"last\")\nConsole.writeLine(\"done\")", "drop\ndrop\ndone\n")]
    [InlineData("Shared", "func count<T>(values: ref/Array<T>) -> isize => values.length\nrequire count([1, 2, 3]) == 3 and count([\"a\"]) == 1 else => $abort(\"count\")\nConsole.writeLine(\"done\")", "done\n")]
    [InlineData("Generic", "struct Task\n    public let id: i32\n    public init(id: i32) => self.id = id\n    drop => Console.writeLine(\"drop\")\nfunc count<T>(values: Array<T>) -> isize => values.length\nlet tasks: Array<Task> = [Task.init(1), Task.init(2)]\nrequire count(tasks@move) == 2 and count([1, 2, 3]) == 3 and count([\"a\"]) == 1 else => $abort(\"count\")\nConsole.writeLine(\"done\")", "drop\ndrop\ndone\n")]
    [InlineData("GenericResult", "struct Task\n    public let id: i32\n    public init(id: i32) => self.id = id\n    drop => Console.writeLine(\"drop\")\nfunc pair<T>(first: T, second: T) -> Array<T> => [first@move, second@move]\nlet tasks = pair(Task.init(1), Task.init(2))\nrequire tasks.length == 2 and tasks[1].id == 2 and pair(3, 4)[0] == 3 else => $abort(\"pair\")\nConsole.writeLine(\"done\")", "done\ndrop\ndrop\n")]
    public void ACallArgumentLiteralConstructsTheParameterArray(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("DynamicArrayArgumentLiteral" + name, source, stdout);

    [Theory]
    [InlineData("Negative", "-1")]
    [InlineData("Length", "1")]
    [InlineData("Maximum", "9223372036854775807@isize")]
    public void BoundsAbort(string name, string index)
        => ScalarEmissionTest.EmitFixture(
            "DynamicArrayReadBounds" + name,
            "let values: Array<i32> = [42]\nlet n = values[" + index + "]",
            string.Empty,
            1,
            "Hello.kimi:2:9: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");

    [Theory]
    [InlineData("var values: Array<i32> = [42]\nlet n = values[(label work: do\n    values@uniq.clear()\n    exit to work 0\n)]")]
    [InlineData("var values: Array<i32> = [42]\nlet borrow = values@ref\nvalues@uniq.clear()\nlet n = borrow[0]")]
    [InlineData("let values: Array<i32> = [42]\nlet moved = values@move\nlet n = values[0]")]
    [InlineData("let values: Array<string> = [\"owned\"]\nlet taken = values[0]@move")]
    [InlineData("let values: Array<i32> = [42]\nlet taken = values[0]@move")]
    [InlineData("var values: Array<i32> = [42]\nlet borrow = values@uniq\nlet n = borrow[(label work: do\n    borrow@uniq.clear()\n    exit to work 0\n)]")]
    public void RejectsInvalidAccess(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        using var output = new StringWriter();
        Assert.False(c.Generate(output));
        Assert.Empty(output.ToString());
        Assert.False(c.Binding.Result.IsComplete && c.Analysis.IsVerified);
    }

    // D-941 (SPEC 4.7.1, 5.2, 15.1.5): a write or an exclusive borrow of a k-deep Array element, through an owned local (v) or a raw
    // pointer ((*p)), decides each prefix of its path once (Binding's PathFold), so binding it takes path steps linear in k; deciding a
    // receiver prefix again from each Array step took 2^k (raw) and F(2k+2) (owned) writability decisions, which no k here finishes;
    // the borrow's lending is read from the same fold (LendFold).
    [Theory]
    [InlineData("write", "v")]
    [InlineData("write", "(*p)")]
    [InlineData("uniq", "v")]
    [InlineData("uniq", "(*p)")]
    public void ANestedArrayElementsPathTakesWorkLinearInItsDepth(string use, string root)
    {
        long[] work = [.. new[] { 30, 60, 120 }.Select(k => Bound(NestedElement(use, root, k)).Binding.PathFoldWork)];
        Assert.True(work[0] < work[1] && MirBorrowsTest.Grows(work, 2), $"{use} {root}: work {string.Join(", ", work)}");
    }

    // D-941 (SPEC 3.4.3, 13.5.5, 15.6.2): a write and an exclusive borrow through k exclusive references each stored in a Tuple below
    // the one before (`r: uniq/(uniq/(...) during a, i32)`, `r.0..0`): each prefix's rules are decided once on every Place query, the
    // receivers' exclusive access included, so binding them takes path steps linear in k (a read of the chain asks no Place query).
    [Theory]
    [InlineData("write")]
    [InlineData("uniq")]
    public void AnExclusiveReferenceChainTakesWorkLinearInItsDepth(string use)
    {
        long[] work = [.. new[] { 30, 60, 120 }.Select(k => Bound(ReferenceChain(use, k)).Binding.PathFoldWork)];
        Assert.True(work[0] < work[1] && MirBorrowsTest.Grows(work, 2), $"{use}: work {string.Join(", ", work)}");
    }

    // G1146 (D-932): a 64-deep Array element, one step past the former 64-step path walk, is written and borrowed exclusively.
    [Theory]
    [InlineData("write", "v")]
    [InlineData("write", "(*p)")]
    [InlineData("uniq", "v")]
    [InlineData("uniq", "(*p)")]
    public void ASixtyFourDeepArrayElementIsWrittenAndBorrowed(string use, string root) => Bound(NestedElement(use, root, 64));

    private static Compilation Bound(string source)
    {
        var c = CompilationTestHelper.Parse(source);
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        return c;
    }

    // `v[0]..[0] = 9` or `let u = v[0]..[0]@uniq` over a k-deep Array of i32, v a local holding a literal or p a raw pointer.
    private static string NestedElement(string use, string root, int k)
    {
        var type = string.Concat(Enumerable.Repeat("Array<", k)) + "i32" + new string('>', k);
        var place = root + string.Concat(Enumerable.Repeat("[0]", k));
        var statement = use == "write" ? place + " = 9" : "let u = " + place + "@uniq";
        return root == "v" ? $"public func main()\n    var v: {type} = {new string('[', k)}7{new string(']', k)}\n    {statement}\n"
            : $"func put(p: raw/{type})\n    unsafe\n        {statement}\npublic func main() => ()\n";
    }

    // `r.0..0` (k selections) written or borrowed exclusively, r: uniq/T_k with T_1 = (i32, i32) and T_j = (uniq/T_(j-1) during a, i32).
    private static string ReferenceChain(string use, int k)
    {
        var type = "(i32, i32)";
        for (var j = 2; j <= k; j++)
        {
            type = $"(uniq/{type} during a, i32)";
        }

        var place = "r" + string.Concat(Enumerable.Repeat(".0", k));
        return $"func put(r: uniq/{type})\n    " + (use == "write" ? place + " = 9" : "let u = " + place + "@uniq") + "\n";
    }
}
