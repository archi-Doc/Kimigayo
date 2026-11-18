// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class TimeLibraryTest
{
    private const string Started = "var watch = Kimi.Time.Stopwatch.init()\nwatch.start()\nwatch.stop()";
    private const string Restarted = "var watch = Kimi.Time.Stopwatch.init()\nwatch.restart()";
    private const string Snapshot = "var watch = Kimi.Time.Stopwatch.init()\nwatch.start()\n_ = watch.elapsed()";
    private const string Clock = ": abort KIMI_E_CLOCK: Elapsed-time counter failed\n";

    // SPEC 22.8.2, 22.5.2 and IMPL 21.4.5: a failed counter or frequency query, a nonpositive frequency, a negative counter and a counter
    // going backward Abort with KIMI_E_CLOCK. SPEC 22.5.1 (PLAN G71): each reports the start of the user's Stopwatch call, except the
    // frequency rows: they Abort in the counter frequency's static initializer, whose body receives no location (a library line). The
    // fake counter returns its values in order and fails once they run out.
    [Theory]
    [InlineData("FrequencyFailure", Started, 0, 3, "10,20", "Time.kimi:13:76" + Clock)]
    [InlineData("FrequencyZero", Started, 1, 0, "10,20", "Time.kimi:14:35" + Clock)]
    [InlineData("CounterFailure", Started, 1, 3, "", "Hello.kimi:2:1" + Clock)]
    [InlineData("CounterNegative", Started, 1, 3, "-1", "Hello.kimi:2:1" + Clock)]
    [InlineData("CounterBackwards", Started, 1, 3, "10,9", "Hello.kimi:3:1" + Clock)]
    [InlineData("RestartFailure", Restarted, 1, 3, "", "Hello.kimi:2:1" + Clock)]
    [InlineData("ElapsedFailure", Snapshot, 1, 3, "10", "Hello.kimi:3:5" + Clock)]
    [InlineData("ElapsedBackwards", Snapshot, 1, 3, "10,9", "Hello.kimi:3:5" + Clock)]
    public void InvalidClockAborts(string name, string source, int frequencyResult, long frequency, string counter, string stderr)
    {
        var ir = CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze(source));
        var nativeFrequency = "define i32 @QueryPerformanceFrequency(ptr %out) { store i64 " + frequency + ", ptr %out ret i32 " + frequencyResult + " }";
        ir = ir.Replace("declare dllimport i32 @QueryPerformanceCounter(ptr)", Counter(counter), StringComparison.Ordinal)
            .Replace("declare dllimport i32 @QueryPerformanceFrequency(ptr)", nativeFrequency, StringComparison.Ordinal);
        var library = stderr.StartsWith("Time.kimi", StringComparison.Ordinal) ? "compiler://Kimi/" + Compilation.CurrentLanguageVersion + "/" : string.Empty;
        ScalarEmissionTest.WriteFixture("TimeLibrary" + name, ir, string.Empty, 1, library + stderr);
    }

    [Fact]
    public void UnusedClockHasNoNativeReferencesOrInitializer()
    {
        var ir = CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze("Console.writeLine(\"hello\")"));
        Assert.DoesNotContain("QueryPerformance", ir);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmTimeAnalysisAndEmissionReuseStorage()
    {
        var c = MinimalEmissionTest.Analyze("var watch = Kimi.Time.Stopwatch.init()\nwatch.start()\nwatch.stop()\nlet duration = watch.elapsed()");
        var valid = true;
        var bytes = AllocationMeasurement.Measure(() =>
        {
            valid &= c.Analyze().IsVerified;
            valid &= c.Generate(TextWriter.Null);
        });
        Assert.True(valid);
        Assert.Equal(0, bytes);
    }

    [Fact]
    public void StopsTruncateEachIntervalAndSnapshotsDoNotChangeState()
    {
        const string Source = """
            var watch = Kimi.Time.Stopwatch.init()
            watch.start()
            watch.start()
            require watch.elapsed().rawMicroseconds == 1 else => $abort("Snapshot")
            watch.stop()
            require watch.elapsed().rawMicroseconds == 1 else => $abort("First interval")
            watch.stop()
            watch.start()
            watch.stop()
            require watch.elapsed().rawMicroseconds == 2 else => $abort("Accumulation")
            watch.restart()
            watch.stop()
            require watch.elapsed().rawMicroseconds == 2 else => $abort("Restart")
            watch.reset()
            require watch.elapsed().rawMicroseconds == 0 else => $abort("Reset")
            """;
        var ir = CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze(Source));
        const string Frequency = """
            @test.frequency.read = internal global i1 false
            define i32 @QueryPerformanceFrequency(ptr %out) {
              %read = load i1, ptr @test.frequency.read
              br i1 %read, label %failure, label %initialize
            initialize:
              store i1 true, ptr @test.frequency.read
              store i64 3000000, ptr %out
              ret i32 1
            failure:
              ret i32 0
            }
            """;
        Assert.Contains("declare dllimport i32 @QueryPerformanceCounter(ptr)", ir);
        Assert.Contains("declare dllimport i32 @QueryPerformanceFrequency(ptr)", ir);
        ir = ir.Replace("declare dllimport i32 @QueryPerformanceCounter(ptr)", Counter("10,14,15,20,24,35,42"), StringComparison.Ordinal)
            .Replace("declare dllimport i32 @QueryPerformanceFrequency(ptr)", Frequency, StringComparison.Ordinal);
        ScalarEmissionTest.WriteFixture("TimeLibraryDeterministic", ir, string.Empty);
    }

    [Fact]
    public void DurationPreservesExactMicrosecondsAndConvertsUnits()
    {
        const string Source = """
            let duration = Kimi.Time.Duration.init(microseconds: 1250000)
            require duration.seconds == 1.25 else => $abort("Seconds")
            require duration.milliseconds == 1250.0 else => $abort("Milliseconds")
            require duration.microseconds == 1250000.0 else => $abort("Microseconds")
            require duration.rawMicroseconds == 1250000 else => $abort("Exact value")
            let largest = Kimi.Time.Duration.init(microseconds: 18446744073709551615)
            require largest.rawMicroseconds == 18446744073709551615 else => $abort("Largest duration")
            let copied = duration
            require copied.rawMicroseconds == duration.rawMicroseconds else => $abort("Copy")
            """;
        ScalarEmissionTest.EmitFixture("TimeLibraryDuration", Source, string.Empty);
    }

    [Fact]
    public void StopwatchUsesWindowsClockAndPreservesState()
    {
        const string Source = """
            var watch = Kimi.Time.Stopwatch.init()
            require not watch.isRunning and watch.elapsed().rawMicroseconds == 0 else => $abort("Initial state")
            watch.stop()
            watch.start()
            watch.start()
            require watch.isRunning else => $abort("Running state")
            let snapshot = watch.elapsed()
            watch.stop()
            let stopped = watch.elapsed()
            require stopped.rawMicroseconds >= snapshot.rawMicroseconds else => $abort("Monotonic clock")
            watch.stop()
            require watch.elapsed().rawMicroseconds == stopped.rawMicroseconds else => $abort("Stopped state")
            watch.start()
            watch.stop()
            require watch.elapsed().rawMicroseconds >= stopped.rawMicroseconds else => $abort("Resume")
            watch.restart()
            require watch.isRunning else => $abort("Restart")
            watch.reset()
            require not watch.isRunning and watch.elapsed().rawMicroseconds == 0 else => $abort("Reset")
            """;
        ScalarEmissionTest.EmitFixture("TimeLibraryStopwatch", Source, string.Empty);
    }

    [Theory]
    [InlineData("0", "3", "0")]
    [InlineData("1", "3", "333333")]
    [InlineData("10", "3", "3333333")]
    [InlineData("18446744073709551615", "1000000", "18446744073709551615")]
    [InlineData("9223372036854775806", "9223372036854775807", "999999")]
    [InlineData("18446744073709551615", "9223372036854775807", "2000000")]
    public void ConvertsCounterExactlyWithoutWideArithmetic(string ticks, string frequency, string expected)
    {
        var c = CompilationTestHelper.Parse("Kimi.Time.probe()");
        c.Library.Kotonoha.AddSource(new("TimeProbe.kimi", "public group Time\n    public func probe()\n        require counterMicroseconds(" + ticks + ", " + frequency + ") == " + expected + " else => $abort(\"Conversion\")"));
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Analyze();
        ScalarEmissionTest.WriteFixture("TimeLibraryConversion" + ticks + "_" + frequency, CompilationTestHelper.WriteIr(c), string.Empty);
    }

    // A fake QueryPerformanceCounter: it returns the comma-separated values in order and fails once they run out.
    private static string Counter(string values)
    {
        var items = values.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var count = items.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var initializer = items.Length == 0 ? "zeroinitializer" : "[i64 " + string.Join(", i64 ", items) + "]";
        return $$"""
            @test.counter.index = internal global i64 0
            @test.counter.values = internal constant [{{count}} x i64] {{initializer}}
            define i32 @QueryPerformanceCounter(ptr %out) {
              %index = load i64, ptr @test.counter.index
              %valid = icmp ult i64 %index, {{count}}
              br i1 %valid, label %read, label %failure
            read:
              %slot = getelementptr [{{count}} x i64], ptr @test.counter.values, i64 0, i64 %index
              %value = load i64, ptr %slot
              store i64 %value, ptr %out
              %next = add i64 %index, 1
              store i64 %next, ptr @test.counter.index
              ret i32 1
            failure:
              ret i32 0
            }
            """;
    }
}
