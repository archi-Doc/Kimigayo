// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>PLAN P37: allocation observation of the unchanged Program 37 workload. Every heap allocation is made by the collection
/// runtimes; views, iteration, closures, formatting and removal allocate nothing.</summary>
public class WorkloadCostTest
{
    private const string Expected =
        "No orders to count.\nLarge orders: 3 overall, 2 in the tail.\nFirst order over 40 is 3.\nRunning total is 155.\n" +
        "Category 10 totals 75.\nCategory 20 totals 20.\nCategory 30 totals 60.\nStopped at order 4.\nRemoved order 1.\nOrder 1 destroyed.\n" +
        "Processing finished.\nOrder 5 destroyed.\nOrder 4 destroyed.\nOrder 3 destroyed.\nOrder 2 destroyed.\n";

    // Observed 2026-09-27: the order Array's growth and the category totals Dictionary; nothing else reaches the heap.
    private const int Bound = 3;

    [Fact]
    public void ProcessingWorkloadAllocationsAreBounded()
        => WriteWorkload("WorkloadCostProcessing", Program(), Expected, Bound);

    // The workload's functions over N generated orders: the allocations grow with the Array's geometric growth only, so they stay
    // logarithmic in N while the processing is linear (the Dictionary holds three categories).
    [Theory]
    [InlineData(64)]
    [InlineData(1024)]
    public void ScaledWorkloadAllocationsStayLogarithmic(int count)
    {
        var program = Program();
        var functions = program[..program.IndexOf("public func main()", StringComparison.Ordinal)];
        var source = functions +
            "public func main()\n    var orders: Array<Order> = []\n    var i: i32 = 0\n" +
            "    while i < " + count + "\n        orders.append(Order.init(i + 1, 10 * (i % 3 + 1), i % 50))\n        i += 1\n" +
            "    let any = func [] (order: ref/Order) -> bool => true\n" +
            "    require countMatching(orders[..], any@ref) == " + count + " else => $abort(\"count\")\n" +
            "    var sum: i32 = 0\n    var running = func [var sum] (order: ref/Order) -> i32\n        sum += order.amount\n        return sum\n" +
            "    let last = applyAll(orders[..], running@uniq)\n" +
            "    let sums = totals(orders[..])\n    var categories: i32 = 0\n    for (category, amount) in sums\n        categories += 1\n" +
            "    require categories == 3 else => $abort(\"categories\")\n" +
            "    Console.writeLine(\"Processed.\")\n";
        var expected = new System.Text.StringBuilder("Processed.\n");
        for (var id = count; id > 0; id--)
        {
            expected.Append("Order ").Append(id).Append(" destroyed.\n");
        }

        WriteWorkload("WorkloadCostScaled" + count, source, expected.ToString(), 1 + (int)Math.Ceiling(Math.Log2(count)) + 2);
    }

    private static string Program()
        => File.ReadAllText(Path.Combine(FindRoot(), "tests", "milestones", "Milestone37.kimi")).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static void WriteWorkload(string name, string source, string stdout, int bound)
    {
        var c = MinimalEmissionTest.Analyze(source);
        var ir = CompilationTestHelper.WriteIr(c);
        Assert.Contains("call ptr @HeapAlloc(", ir);
        ir = ir.Replace("call ptr @HeapAlloc(", "call ptr @probe_allocate(", StringComparison.Ordinal);

        // The workload's own exit is replaced by the observation: exit 93 when the allocations exceed the bound.
        const string Exit = "  call void @__kimi_exit(i32 0)\n  unreachable\n}";
        var start = ir.IndexOf("define void @__kimi_start() noreturn #0 {", StringComparison.Ordinal);
        var exit = start < 0 ? -1 : ir.IndexOf(Exit, start, StringComparison.Ordinal);
        Assert.True(exit > start);
        var check = $$"""
          %allocations = load i64, ptr @probe_allocations, align 8
          %bounded = icmp ule i64 %allocations, {{bound}}
          br i1 %bounded, label %passed, label %failed
        passed:
          call void @__kimi_exit(i32 0)
          unreachable
        failed:
          call void @__kimi_exit(i32 93)
          unreachable
        }
        """;
        ir = string.Concat(ir.AsSpan(0, exit), check, ir.AsSpan(exit + Exit.Length));
        ir += """

            @probe_allocations = private global i64 0
            define internal ptr @probe_allocate(ptr %heap, i32 %flags, i64 %size) {
            entry:
              %previous = load i64, ptr @probe_allocations, align 8
              %count = add i64 %previous, 1
              store i64 %count, ptr @probe_allocations, align 8
              %memory = call ptr @HeapAlloc(ptr %heap, i32 %flags, i64 %size)
              ret ptr %memory
            }

            """;
        ScalarEmissionTest.WriteFixture(name, ir, stdout);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kimigayo.slnx")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }
}
