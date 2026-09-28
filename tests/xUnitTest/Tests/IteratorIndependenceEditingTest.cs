// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class IteratorIndependenceEditingTest
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void HelperEditRevokesAndRestoresIteratorConformance(int kind)
    {
        var source = "struct Cursor {source}\n    Self is Iterator\n    associate Iterator.Item is ref/i32 during source\n    let value: uniq/i32 during source\n    var count: i32 = 0\n    public init(value: uniq/i32 during source) => self.value = value@move\n    func update(self: uniq/Self) => self.count += 1\n    public func next(self: uniq/Self) -> Option<ref/i32 during source>\n        self.update()\n        return .Some(self.value)\nvar value = 42\nvar cursor = Cursor.init(value@uniq)\nlet item = cursor.next()\nmatch item\n    .Some(let n) => require n == 42 else => $abort(\"value\")\n    .None => $abort(\"empty\")";
        if (kind != 0)
        {
            source = source.Replace("func update(", "func update<T>(", StringComparison.Ordinal).Replace("self.update()", "self.update<i32>()", StringComparison.Ordinal);
        }

        if (kind == 2)
        {
            source = source.Replace("    public func next", "    specialize func update<i32>(self: uniq/Self) => self.count += 1\n    public func next", StringComparison.Ordinal);
        }

        var c = MinimalEmissionTest.Analyze(source);
        using var output = new StringWriter();
        Assert.True(c.Emission.WriteIr(output, out var error), MinimalEmissionTest.Describe(c, error));
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "update" && x.IsSpecialization == (kind == 2));
        var original = function.ExpressionBody!;
        var donor = ParseTestHelper.ParseSuccess(source.Replace("self.count += 1", "self.value@follow += 1", StringComparison.Ordinal));
        var replacement = KotoTree.Walk(donor.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == "update" && x.IsSpecialization == (kind == 2)).ExpressionBody!;
        for (var i = 0; i < 3; i++)
        {
            Assert.True(KotoHelper.Replace(function, original, replacement));
            output.GetStringBuilder().Clear();
            Assert.False(c.Emission.WriteIr(output, out _));
            Assert.Empty(output.ToString());
            Assert.False(c.Bind().IsComplete);
            Assert.Contains(c.Binding.Issues, issue => issue.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
            Assert.True(KotoHelper.Replace(function, replacement, original));
            Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
            Assert.True(c.Emission.WriteIr(output, out error), MinimalEmissionTest.Describe(c, error));
        }
    }
}
