// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class TimeLibraryTest
{
    [Theory]
    [InlineData(0, 3, 1, 10, "FrequencyFailure", 13, "Performance counter frequency query failed")]
    [InlineData(1, 0, 1, 10, "FrequencyZero", 14, "Invalid performance counter frequency")]
    [InlineData(1, 3, 0, 10, "CounterFailure", 7, "Performance counter query failed")]
    [InlineData(1, 3, 1, -1, "CounterNegative", 8, "Negative performance counter")]
    [InlineData(1, 3, 1, 10, "CounterBackwards", 18, "Performance counter moved backwards")]
    public void InvalidClockAborts(int frequencyResult, long frequency, int counterResult, long counter, string name, int line, string message)
    {
        var c = MinimalEmissionTest.Analyze("var watch = Kimi.Time.Stopwatch.init()\nwatch.start()\nwatch.stop()");
        var ir = CompilationTestHelper.WriteIr(c);
        var nativeCounter = "@test.counter = internal global i64 " + counter + "\ndefine i32 @QueryPerformanceCounter(ptr %out) { %value = load i64, ptr @test.counter store i64 %value, ptr %out %next = sub i64 %value, 1 store i64 %next, ptr @test.counter ret i32 " + counterResult + " }";
        var nativeFrequency = "define i32 @QueryPerformanceFrequency(ptr %out) { store i64 " + frequency + ", ptr %out ret i32 " + frequencyResult + " }";
        ir = ir.Replace("declare dllimport i32 @QueryPerformanceCounter(ptr)", nativeCounter, StringComparison.Ordinal)
            .Replace("declare dllimport i32 @QueryPerformanceFrequency(ptr)", nativeFrequency, StringComparison.Ordinal);
        var column = line switch { 7 => 74, 13 => 76, 18 => 38, 14 => 35, _ => 36 };
        var stderr = "compiler://Kimi/" + Compilation.CurrentLanguageVersion + "/Time.kimi:" + line + ":" + column + ": abort KIMI_E_ABORT: " + message + "\n";
        ScalarEmissionTest.WriteFixture("TimeLibrary" + name, ir, string.Empty, 1, stderr);
    }

    [Fact]
    public void UnusedClockHasNoNativeReferencesOrInitializer()
    {
        var ir = CompilationTestHelper.WriteIr(MinimalEmissionTest.Analyze("Console.writeLine(\"hello\")"));
        Assert.DoesNotContain("QueryPerformance", ir);
        Assert.DoesNotContain(".state =", ir);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmTimeAnalysisAndEmissionReuseStorage()
    {
        var c = MinimalEmissionTest.Analyze("var watch = Kimi.Time.Stopwatch.init()\nwatch.start()\nwatch.stop()\nlet duration = watch.elapsed()");
        var valid = true;
        var bytes = AllocationMeasurement.Measure(
            () =>
            {
                valid &= c.Ownership.Analyze().IsVerified;
                valid &= c.Emission.WriteIr(TextWriter.Null, out _);
            },
            iterations: 128,
            warmupIterations: 100);
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
        const string Counter = """
            @test.counter.index = internal global i64 0
            @test.counter.values = internal constant [7 x i64] [i64 10, i64 14, i64 15, i64 20, i64 24, i64 35, i64 42]
            define i32 @QueryPerformanceCounter(ptr %out) {
              %index = load i64, ptr @test.counter.index
              %valid = icmp ult i64 %index, 7
              br i1 %valid, label %read, label %failure
            read:
              %slot = getelementptr [7 x i64], ptr @test.counter.values, i64 0, i64 %index
              %value = load i64, ptr %slot
              store i64 %value, ptr %out
              %next = add i64 %index, 1
              store i64 %next, ptr @test.counter.index
              ret i32 1
            failure:
              ret i32 0
            }
            """;
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
        ir = ir.Replace("declare dllimport i32 @QueryPerformanceCounter(ptr)", Counter, StringComparison.Ordinal)
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
        c.Ownership.Analyze();
        ScalarEmissionTest.WriteFixture("TimeLibraryConversion" + ticks + "_" + frequency, CompilationTestHelper.WriteIr(c), string.Empty);
    }
}
