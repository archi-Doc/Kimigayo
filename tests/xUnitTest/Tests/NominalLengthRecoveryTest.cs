// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class NominalLengthRecoveryTest
{
    [Theory]
    [InlineData("struct Cursor<length N> {source}\n    let value: ref/i32 during source\n    public func read(self: ref/Self) -> i32 => self.value")]
    [InlineData("enum Cursor<length N> {source}\n    case Some(ref/i32 during source)\n    public func read(self: ref/Self) -> i32 => 0")]
    [InlineData("contract Cursor<length N> {source}\n    func read(self: ref/Self) -> i32")]
    [InlineData("struct Outer<length N> {source}\n    struct Cursor\n        let value: ref/i32 during source\n        public func read(self: ref/Self) -> i32 => self.value")]
    [InlineData("struct Cursor<length N>\n    public init() => ()")]
    public void InvalidNominalLengthsNeverEnterTypeComponents(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        for (var i = 0; i < 3; i++)
        {
            Assert.False(c.Bind().IsComplete);

            // The parser reports the slot (SPEC 4.4); the formation failure of the header rests on that Error and is not published again.
            Assert.Contains(c.Binding.DerivedIssues, node => node.FailureOf() == BindingFailure.InvalidTypeFormation);
            Assert.DoesNotContain(c.Binding.Issues, issue => issue.Code == DiagnosticCode.InvalidTypeFormation_Kd);
            Assert.False(c.Generate(TextWriter.Null));
        }
    }

    // SPEC 4.4.1, 4.4.2 (DECISIONS BU-85): only functions have length slots, so outside a Function scope nothing proves a compound
    // length; its formation fails at the length, and no length obligation is left unproven for Final. A bare slot is a valid length.
    [Theory]
    [InlineData("struct S<length N>\n    public var cells: [(N - 1) of i32]\npublic func main() => ()", "(N - 1)")]
    [InlineData("struct S<length N>\n    public var cells: [(N + 1) of i32]\npublic func main() => ()", "(N + 1)")]
    [InlineData("struct S<length N>\n    public var cells: [N of i32]\npublic func main() => ()", null)]
    public void CompoundLengthOutsideFunctionFailsAtFormation(string source, string? length)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Bind().IsComplete);
        var failed = c.Binding.Issues.Select(static issue => issue.Node).Concat(c.Binding.DerivedIssues)
            .Where(static node => node.FailureOf() == BindingFailure.InvalidTypeFormation)
            .Select(static node => node.ToString()).ToArray();
        if (length is null)
        {
            Assert.DoesNotContain("N", failed);
        }
        else
        {
            Assert.Contains(length, failed);
        }

        Assert.DoesNotContain(c.Binding.Obligations, obligation => obligation.Length is not null && !c.Binding.IsVerifiedLengthObligation(obligation));
    }
}
