// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class PatternCoverageCompletionTest
{
    [Fact]
    public void LateInvalidTypeSuppressesMissingCoverageCascade()
    {
        var c = MinimalEmissionTest.Analyze(Source("internal"));
        Assert.Empty(c.Kimigayo.GetOrAddDiagnosticCollection("Hello.kimi").GetArray());
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.Access);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.NonExhaustiveMatch);
        Assert.False(c.Bind().IsComplete);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.NonExhaustiveMatch);
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.DoesNotContain(restored.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.NonExhaustiveMatch);
    }

    [Fact]
    public void ValidTypeStillRequiresMissingCaseCoverage()
    {
        var c = MinimalEmissionTest.Analyze(Source("public"));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.NonExhaustiveMatch);
        Assert.False(c.Binding.CheckBound().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.NonExhaustiveMatch);
        var restored = Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.Contains(restored.Binding.Issues, x => x.Node.BindingFailure == BindingFailure.NonExhaustiveMatch);
    }

    [Fact]
    public void AddingMissingCaseRestoresValidMatch()
    {
        var c = MinimalEmissionTest.Analyze(Source("public") + "\n        .B => ()");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.True(c.Bind().IsComplete);
    }

    [Fact]
    public void WarmCoverageFailureAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(Source("public"));
        for (var i = 0; i < 100; i++)
        {
            Assert.False(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Nonexhaustive match was accepted.");
            }
        }));
    }

    private static Compilation Reload(Compilation c)
    {
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        Tinyhand.TinyhandSerializer.DeserializeObject(Tinyhand.TinyhandSerializer.Serialize(c.Kotonoha), ref tree);
        Assert.NotNull(tree);
        tree.OnDeserialized(restored);
        return restored;
    }

    private static string Source(string access)
        => access + " contract Hidden\npublic struct Value\n    Self is Hidden\npublic enum E<T>\n    T is Hidden\n    A\n    B\ngroup Consumer\n    func inspect(value: E<Value>) => match value@move\n        .A => ()";
}
