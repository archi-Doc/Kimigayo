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
            Assert.Contains(c.Binding.DerivedIssues, node => node.BindingFailure == BindingFailure.InvalidTypeFormation);
            Assert.DoesNotContain(c.Binding.Issues, issue => issue.Code == DiagnosticCode.InvalidTypeFormation_Kd);
            Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
        }
    }
}
