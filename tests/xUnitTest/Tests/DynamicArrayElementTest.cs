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

    // D-941 (SPEC 3.4.3, 4.7.1, 15.1.5; the Front's B1): a write and an exclusive borrow through k exclusive references each stored in
    // an Array element of a Tuple below the one before (`r: uniq/T_k`, T_j = (Array<uniq/T_(j-1) during a>, i32), `r.0[0]...[0].0`),
    // and with an inline Tuple level below each element (T_j = ((Array<...>, i32), i32), `r.0.0[0]...[0].0.0`). Each element is read
    // in place through a synthesized shared borrow, whose exclusive reading (ExclusiveAccessType) takes the receiver's ReferentAuthority
    // from the fold, so binding them takes path steps linear in k; folding each such receiver again took 2^k fold steps.
    [Theory]
    [InlineData("write", false)]
    [InlineData("uniq", false)]
    [InlineData("write", true)]
    [InlineData("uniq", true)]
    public void AnElementStoredReferenceChainTakesWorkLinearInItsDepth(string use, bool inline)
    {
        long[] work = [.. new[] { 30, 60, 120 }.Select(k => Bound(ElementChain(use, inline, k)).Binding.PathFoldWork)];
        Assert.True(work[0] < work[1] && MirBorrowsTest.Grows(work, 2), $"{use} {inline}: work {string.Join(", ", work)}");
    }

    // D-932 (SPEC 3.4.3, 4.7, 15.6.3, IMPL A.4; the Front's fu-32/33/34 on Q-596): ElementChain's exclusive borrow with fixed arrays,
    // `r.0[0]...[0].0` through k exclusive references each stored in a fixed array (T_j = ([2 of uniq/T_(j-1) during a], i32)), 2k - 1
    // path steps. ContainedOrigin meets every layer up to r's own at every depth (it has no bound of its own: SyntaxNesting's); a
    // 64-step bound left r's layer unmet at k = 33 (65 steps), a false UnprovenOriginRelation_Kd "requires r outlives a".
    [Theory]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(34)]
    public void AFixedArrayStoredReferenceChainIsBorrowedExclusivelyAtEveryDepth(int k)
    {
        var type = "(i32, i32)";
        for (var j = 2; j <= k; j++)
        {
            type = $"([2 of uniq/{type} during a], i32)";
        }

        var source = $"func put(r: uniq/{type})\n    let u = r{string.Concat(Enumerable.Repeat(".0[0]", k - 1))}.0@uniq\npublic func main() => ()\n";
        Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
    }

    // D-941 (SPEC 15.1.5, 3.4.3, 15.6.2; the Front's C2): the nested Array rows' invalid twins at depth. Through a `let` root a write and
    // an exclusive borrow are refused (InvalidAssignment_Kd: a let root grants no Write and no exclusive borrow); through `p:
    // ref/Array^k` they reach a shared layer (SharedPathAccess_Kd). One record each at k = 30, 60 and 120, in path steps linear in k.
    [Theory]
    [InlineData("write", "let", "InvalidAssignment_Kd")]
    [InlineData("uniq", "let", "InvalidAssignment_Kd")]
    [InlineData("write", "ref", "SharedPathAccess_Kd")]
    [InlineData("uniq", "ref", "SharedPathAccess_Kd")]
    public void ANestedArrayElementsInvalidTwinIsRefusedOnce(string use, string root, string code)
    {
        long[] work = [.. new[] { 30, 60, 120 }.Select(k => Refused(NestedElement(use, root, k), code).Binding.PathFoldWork)];
        Assert.True(work[0] < work[1] && MirBorrowsTest.Grows(work, 2), $"{use} {root}: work {string.Join(", ", work)}");
    }

    // D-941 (SPEC 3.4.3, 15.6.2; the Front's C4): a shared borrow of a k-deep Tuple chain below a `let` root (`let u = x.0..0@ref`), a
    // query that writes nothing: its fold decides every prefix's writability too (each through WritableRoot's syntax walk to the let
    // root, no fold), in path steps linear in k.
    [Fact]
    public void ASharedBorrowOfALetTupleChainTakesWorkLinearInItsDepth()
    {
        long[] work = [.. new[] { 30, 60, 120 }.Select(k => Bound(TupleChain(k)).Binding.PathFoldWork)];
        Assert.True(work[0] < work[1] && MirBorrowsTest.Grows(work, 2), $"work {string.Join(", ", work)}");
    }

    // G1146 (D-932): a 64-deep Array element, one step past the former 64-step path walk, is written and borrowed exclusively.
    [Theory]
    [InlineData("write", "v")]
    [InlineData("write", "(*p)")]
    [InlineData("uniq", "v")]
    [InlineData("uniq", "(*p)")]
    public void ASixtyFourDeepArrayElementIsWrittenAndBorrowed(string use, string root) => Bound(NestedElement(use, root, 64));

    // G1146 (D-941; the Middle's RP17 on C-402): a 64-deep owned Array local, whose write Binding now finishes, meets A5's depth bound:
    // one OwnershipDepthLimit_Kd located at the local (DECISIONS R4-M153), never a Binding record; before D-941 Binding never finished.
    [Fact]
    public void ASixtyFourDeepOwnedArrayLocalMeetsTheOwnershipDepthLimit()
    {
        var c = MinimalEmissionTest.Analyze(NestedElement("write", "v", 64));
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var problems = MinimalEmissionTest.Problems(c);
        Assert.StartsWith("OwnershipDepthLimit_Kd var v: Array<", problems);
        Assert.DoesNotContain(" | ", problems);
    }

    private static Compilation Bound(string source)
    {
        var c = CompilationTestHelper.Parse(source);
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c));
        return c;
    }

    // Binding refuses the program with exactly one record, of `code`.
    private static Compilation Refused(string source, string code)
    {
        var c = CompilationTestHelper.Parse(source);
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(code, string.Join(" | ", c.Binding.Issues.Select(static x => x.Code.ToString())));
        return c;
    }

    // `v[0]..[0] = 9` or `let u = v[0]..[0]@uniq` over a k-deep Array of i32: v a `var` local (root "v") or a `let` local ("let")
    // holding a literal, p a raw pointer ("(*p)") or a shared reference ("ref").
    private static string NestedElement(string use, string root, int k)
    {
        var type = string.Concat(Enumerable.Repeat("Array<", k)) + "i32" + new string('>', k);
        var place = root switch { "v" or "let" => "v", "ref" => "p", _ => root } + string.Concat(Enumerable.Repeat("[0]", k));
        var statement = use == "write" ? place + " = 9" : "let u = " + place + "@uniq";
        return root switch
        {
            "v" or "let" => $"public func main()\n    {(root == "v" ? "var" : "let")} v: {type} = {new string('[', k)}7{new string(']', k)}\n    {statement}\n",
            "ref" => $"func put(p: ref/{type})\n    {statement}\npublic func main() => ()\n",
            _ => $"func put(p: raw/{type})\n    unsafe\n        {statement}\npublic func main() => ()\n",
        };
    }

    // `let u = x.0..0@ref` (k selections) over `let x: U_k`, U_1 = (i32, i32) and U_j = (U_(j-1), i32), holding a literal.
    private static string TupleChain(int k)
    {
        var (type, literal) = ("(i32, i32)", "(1, 2)");
        for (var j = 2; j <= k; j++)
        {
            (type, literal) = ($"({type}, i32)", $"({literal}, 3)");
        }

        return $"public func main()\n    let x: {type} = {literal}\n    let u = x{string.Concat(Enumerable.Repeat(".0", k))}@ref\n";
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

    // `r.0[0]...[0].0` written or borrowed exclusively, r: uniq/T_k with T_1 = (i32, i32) and T_j = (Array<uniq/T_(j-1) during a>, i32);
    // inline: T_1 = ((i32, i32), i32), T_j = ((Array<uniq/T_(j-1) during a>, i32), i32) and `r.0.0[0]...[0].0.0`.
    private static string ElementChain(string use, bool inline, int k)
    {
        var type = inline ? "((i32, i32), i32)" : "(i32, i32)";
        for (var j = 2; j <= k; j++)
        {
            type = inline ? $"((Array<uniq/{type} during a>, i32), i32)" : $"(Array<uniq/{type} during a>, i32)";
        }

        var place = "r" + string.Concat(Enumerable.Repeat(inline ? ".0.0[0]" : ".0[0]", k - 1)) + (inline ? ".0.0" : ".0");
        return $"func put(r: uniq/{type})\n    " + (use == "write" ? place + " = 9" : "let u = " + place + "@uniq") + "\n";
    }
}
