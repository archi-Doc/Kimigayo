// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Tinyhand;
using Xunit;

namespace XunitTest;

internal static class CompilationTestHelper
{
    // Preparation and parsing only: callers choose when to Bind and analyze ownership.
    internal static Compilation Parse(string source, string? path = null)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        if (path is null)
        {
            c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        }
        else
        {
            c.Kotonoha.AddSource(new SourceDocument(path, source));
        }

        return c;
    }

    internal static Compilation ParseSuccess(string source, string? path = null)
    {
        var c = Parse(source, path);
        Assert.Empty(TestDiagnostics.Of(c));
        return c;
    }

    internal static Compilation BindSuccess(string source)
    {
        var c = ParseSuccess(source);
        Assert.True(c.Bind().IsComplete, string.Join('\n', c.Binding.Issues));
        return c;
    }

    // Restore syntax without Binding or ownership analysis so invalidation stays observable.
    internal static Compilation Reload(Compilation c)
    {
        var bytes = TinyhandSerializer.Serialize(c.Kotonoha);
        var restored = Compilation.CreateForTest();
        Assert.True(restored.Prepare(WindowsProfile.Target));
        var tree = restored.Kotonoha;
        TinyhandSerializer.DeserializeObject(bytes, ref tree);
        Assert.NotNull(tree);
        Assert.Same(restored.Kotonoha, tree);
        tree.OnDeserialized(restored);
        return restored;
    }

    internal static string WriteIr(Compilation c)
    {
        using var writer = new StringWriter();
        if (!c.Emission.WriteIr(writer, out var error))
        {
            Assert.Fail(MinimalEmissionTest.Describe(c, error));
        }

        return writer.ToString();
    }
}
