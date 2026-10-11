// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 15.2.3, 7.6.4, 23.3.6.5: a common Function conversion whose signature matches and whose subject is not Owned fails a Constraint,
// never a Type: a Refuted proof (a capture that depends on a body-local Borrow) is UnsatisfiedConstraint_Kd and a proof still Unknown
// (a bound Type parameter) is UnprovenConstraint_Kd. The Reason names the subject, the member through which the Origin enters
// OwnedOrigins and, when one is displayable, that Origin, which is related at its syntax. These were TypeMismatch_Kd with a Note.
public class ErasureOwnedDiagnosticTest
{
    private const string Main = "public func main() -> ()\n";

    // A let annotation, a default, a call argument and a captured reference value give the same record at the converted value.
    [Theory]
    [InlineData(Main + "    let text = \"a\"\n    let f: () -> () = func [text@ref] () => ()\n    f()\n", "func [text@ref] () => ()", "text", "text@ref", "text@ref")]
    [InlineData("func runH(text: string, action: () -> () = func [text@ref] () => ()) -> () => action()\n\n" + Main + "    runH(\"a\")\n", "func [text@ref] () => ()", "text", "text@ref", "text@ref")]
    [InlineData("func call(action: (i32) -> i32, x: i32) -> i32\n    return action(x)\n\n" + Main + "    let y: i32 = 2\n    require call(func [y@ref] (n) => n + y@follow, 1) == 3 else => $abort(\"k1\")\n", "func [y@ref] (n) => n + y@follow", "y", "y@ref", "y@ref")]
    [InlineData(Main + "    var n = 7\n    let view = n@uniq\n    let f = func [view] () => view@follow@ref\n    let e: () -> ref/i32 = f@move\n", "f@move", "view", "n@uniq", "n@uniq")]
    [InlineData(Main + "    let y: i32 = 2\n    let f = func [y@ref] (n: i32) => n + y@follow\n    let g: (i32) -> i32 = f\n", "f", "y", "y@ref", "y@ref")]
    public void ACaptureOverABorrowIsAnUnsatisfiedConstraint(string source, string text, string member, string origin, string related)
    {
        var output = DiagnosticCorpus.Check(source);
        var error = Assert.Single(output.Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnsatisfiedConstraint_Kd), DiagnosticCategory.Language, text), (error.Code, error.Category, Text(source, error.Span)));
        Assert.Equal(related, Text(source, Assert.Single(error.Related!, static x => x.Role == "origin").Span));
        Assert.Contains($"the capture {member} depends on the borrow {origin}, which is not static", error.Note, StringComparison.Ordinal);
        var json = JsonSerializer.Serialize(new DiagnosticResult(output.Diagnostics, output.Sources), DiagnosticJsonContext.Default.DiagnosticResult);
        Assert.Contains($"{{\"name\":\"member\",\"kind\":\"Text\",\"value\":\"{member}\",\"elided\":false}}", json, StringComparison.Ordinal);
        Assert.Contains($"{{\"name\":\"origin\",\"kind\":\"Origin\",\"value\":\"{origin}\",\"elided\":false,\"origin\":\"borrow\"}}", json, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(DiagnosticCode.TypeMismatch_Kd), json, StringComparison.Ordinal);
    }

    // A bound Type parameter that is not proven Owned leaves the proof Unknown; its member is the Item's bound Type argument, with no
    // Origin. A default and a let annotation agree.
    [Theory]
    [InlineData("func identity<T>(value: T) -> T => value@move\n\nfunc runT<T>(value: T, action: (T) -> T = identity) -> T => action(value@move)\n\n" + Main + "    require runT(4) == 4 else => $abort(\"t\")\n")]
    [InlineData("func identity<T>(value: T) -> T => value@move\n\nfunc runT<T>(value: T) -> T\n    let f: (T) -> T = identity\n    return f(value@move)\n\n" + Main + "    require runT(4) == 4 else => $abort(\"t\")\n")]
    public void AnUnprovenTypeArgumentIsAnUnprovenConstraint(string source)
    {
        var output = DiagnosticCorpus.Check(source);
        var error = Assert.Single(output.Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenConstraint_Kd), DiagnosticCategory.Proof, "identity"), (error.Code, error.Category, Text(source, error.Span)));
        var json = JsonSerializer.Serialize(new DiagnosticResult(output.Diagnostics, output.Sources), DiagnosticJsonContext.Default.DiagnosticResult);
        Assert.Contains("{\"name\":\"member\",\"kind\":\"Text\",\"value\":\"1st Type argument\",\"elided\":false}", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"name\":\"origin\"", json, StringComparison.Ordinal);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(new DiagnosticResult(output.Diagnostics, output.Sources), string.Empty);
        Assert.Contains("the 1st Type argument is not proven Owned", console.Text, StringComparison.Ordinal);
    }

    // The counterparts: an Owned Constraint proves the conversion, and the receiver condition keeps its Type mismatch with the Note.
    [Fact]
    public void AnOwnedConstraintProvesTheConversion()
        => Assert.Empty(DiagnosticCorpus.Check("func identity<T>(value: T) -> T => value@move\n\nfunc keepOwned<T>(value: T) -> (T) -> T\n    T is Owned\n    let f: (T) -> T = identity\n    return f@move\n\n" + Main + "    let f = keepOwned(1)\n    require f(2) == 2 else => $abort(\"f\")\n").Diagnostics);

    [Fact]
    public void AnExclusiveClosureStaysATypeMismatch()
    {
        var error = Assert.Single(DiagnosticCorpus.Check("func call(action: (i32) -> i32, x: i32) -> i32\n    return action(x)\n\n" + Main + "    let c: i32 = 0\n    require call(func [var c] (n) -> i32\n        c += 1\n        return n + c\n    , 1) == 2 else => $abort(\"k2\")\n").Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.TypeMismatch_Kd), "This closure requires an Exclusive call; a common Function value permits Shared calls only"), (error.Code, error.Note));
    }

    private static string Text(string source, SourceSpan? span) => span is { } at ? source.Substring(at.Start, at.Length) : string.Empty;
}
