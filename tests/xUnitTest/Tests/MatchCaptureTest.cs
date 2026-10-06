// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Text.Json;
using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 7.6.2, 9.3, 14.8: Pattern bindings introduce lexical scopes, not new accessibility declarations.
public class MatchCaptureTest
{
    private const string Source = "let k = 7\nmatch 5\n    let m => func [k] () => k\n";

    [Theory]
    [InlineData("Discarded", Source)]
    [InlineData("Returned", "let k = 7\nlet f: () -> i32 = match 5\n    let m => func [k, m] () => k + m\nrequire f() == 12 else => $abort(\"capture\")")]
    [InlineData("Guarded", "let k = 7\nlet f: () -> i32 = match 5\n    let m if m == 5 => func [k, m] () => k + m\n    _ => func [] () => 0\nrequire f() == 12 else => $abort(\"capture\")")]
    [InlineData("Block", "let k = 7\nmatch 5\n    let m\n        let f = func [k, m] () => k + m\n        require f() == 12 else => $abort(\"capture\")")]
    [InlineData("Nested", "let k = 7\nmatch 5\n    let m\n        match 3\n            let n\n                let f = func [k, m, n] () => k + m + n\n                require f() == 15 else => $abort(\"capture\")")]
    [InlineData("LocalFunction", "match 5\n    let m\n        func read() -> i32 => 7\n        require read() == 7 else => $abort(\"local\")")]
    [InlineData("Borrowed", "let k = 7\nmatch 5\n    let m\n        let f = func [k@ref] () => k@follow\n        require f() == 7 else => $abort(\"capture\")")]
    [InlineData("Implicit", "let k = 7\nmatch 5\n    let m\n        let f = func () => k + m\n        require f() == 12 else => $abort(\"capture\")")]
    public void CapturesInBindingArmsExecute(string name, string source)
        => ScalarEmissionTest.EmitFixture("MatchCapture" + name, source, string.Empty);

    [Fact]
    public void CaptureErrorsRemainLocated()
    {
        const string Broken = "let k = 7\nmatch 5\n    let m => func [k, k] () => k\n";
        var error = Assert.Single(DiagnosticCorpus.Check(Broken).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.DuplicateBinding_Kd), error.Code);
        Assert.Equal(Broken.IndexOf("k]", StringComparison.Ordinal), error.Span!.Value.Start);
    }

    [Fact]
    public async Task CheckOfABindingArmTerminates()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kimi-match-capture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Kimi.exe" : "Kimi"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = directory,
        };
        var path = Path.Combine(directory, "main.kimi");
        File.WriteAllText(path, Source);
        foreach (var argument in new[] { "check", path, "--Format", "json" })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            using var document = JsonDocument.Parse(await stdout);
            Assert.Equal("Completed", document.RootElement.GetProperty("outcome").GetString());
            Assert.True(document.RootElement.GetProperty("accepted").GetBoolean());
            Assert.Equal(0, process.ExitCode);
            Assert.Empty(await stderr);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(true);
                await process.WaitForExitAsync(CancellationToken.None);
            }

            Directory.Delete(directory, true);
        }
    }

    [TestClass(DisableParallelization = true)]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests
    {
        [Fact]
        public void RebindingAndReloadRetainTheCaptureScope()
        {
            var c = MinimalEmissionTest.Analyze(Source);
            Assert.True(c.Binding.Result.IsComplete);
            c = CompilationTestHelper.Reload(c);
            Assert.True(c.Bind().IsComplete);
            var valid = true;
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
            Assert.True(valid);
        }
    }
}
