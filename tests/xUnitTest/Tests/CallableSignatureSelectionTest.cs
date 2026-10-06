// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 8.6, 23.3.6.1: the distinct Callable signatures on one F are a call's candidates. Selecting among them is not yet
// implemented, so a call through such an F is one located UnsupportedBinding_Kd and never a Language error such as NotCallable_Kd.
public class CallableSignatureSelectionTest
{
    private const string Main = "public func main() -> ()\n    Console.writeLine(\"done\")\n";

    [Theory]
    [InlineData("func both<F>(action: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    F is Callable<(i64) -> i64>\n    return action(1@i32)\n", "action(1@i32)")]
    [InlineData("func both<F>(action: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    F is Callable<(i64) -> i64>\n    return action(1)\n", "action(1)")]
    [InlineData("func both<F>(action: ref/F) -> ()\n    F is Callable<(i32) -> ()>\n    F is Callable<(ref/i32) -> ()>\n    let n: i32 = 1\n    action(n)\n", "action(n)")]
    [InlineData("func both<F>(action: uniq/F) -> ()\n    F is Callable<ref, (i32) -> ()>\n    F is Callable<uniq, (i64) -> ()>\n    action(1@i32)\n", "action(1@i32)")]
    [InlineData("func generic<T, F>(action: ref/F, value: T) -> T\n    F is Callable<(T) -> T>\n    F is Callable<(i32) -> i32>\n    return action(value)\n", "action(value)")]
    public void SeveralSignaturesAreOneLocatedUnsupported(string body, string text)
    {
        var source = body + Main;
        var output = DiagnosticCorpus.Check(source);
        var error = Assert.Single(output.Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnsupportedBinding_Kd), DiagnosticCategory.Unsupported, text), (error.Code, error.Category, error.Span is { } span ? source.Substring(span.Start, span.Length) : string.Empty));
    }

    // One signature with a shared and an exclusive receiver is one candidate, and forwarding several signatures makes no call.
    [Theory]
    [InlineData("func one<F>(action: ref/F) -> i32\n    F is Callable<(i32) -> i32>\n    F is Callable<uniq, (i32) -> i32>\n    return action(1)\nfunc inc(value: i32) -> i32 => value + 1\npublic func main() -> ()\n    require one(inc) == 2 else => $abort(\"b\")\n    Console.writeLine(\"done\")\n")]
    [InlineData("func both<F>(action: ref/F) -> ()\n    F is Callable<(i32) -> i32>\n    F is Callable<(i64) -> i64>\n    return ()\nfunc outer<G>(action: ref/G) -> ()\n    G is Callable<(i32) -> i32>\n    G is Callable<(i64) -> i64>\n    both(action)\n" + Main)]
    public void OneSignatureOrNoCallIsAccepted(string source)
        => Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
}
