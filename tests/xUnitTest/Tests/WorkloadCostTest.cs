// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>PLAN P35: allocation observation of the unchanged Program 35 workload. Every heap allocation is made by the collection
/// runtimes; views, iteration, closures, formatting and removal allocate nothing.</summary>
public class WorkloadCostTest
{
    private const string Expected =
        "No orders to count.\nLarge orders: 3 overall, 2 in the tail.\nFirst order over 40 is 3.\nRunning total is 155.\n" +
        "Category 10 totals 75.\nCategory 20 totals 20.\nCategory 30 totals 60.\nStopped at order 4.\nRemoved order 1.\nOrder 1 destroyed.\n" +
        "Processing finished.\nOrder 5 destroyed.\nOrder 4 destroyed.\nOrder 3 destroyed.\nOrder 2 destroyed.\n";

    // Observed 2026-09-27: the order Array's growth and the category totals Dictionary; nothing else reaches the heap.
    private const int Bound = 3;

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ProcessingWorkloadAllocationsAreBounded()
        => WriteWorkload("WorkloadCostProcessing", Program(), Expected, Bound);

    // The workload's functions over N generated orders: the allocations grow with the Array's geometric growth only, so they stay
    // logarithmic in N while the processing is linear (the Dictionary holds three categories).
    [Trait("Purpose", "Allocation")]
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
        => Repository.ReadText("tests", "milestones", "Milestone35.kimi");

    // Audits at most bound allocations, each released by exit; exact capacities are implementation-defined (SPEC 4.9.5), so the
    // bytes are not claimed.
    private static void WriteWorkload(string name, string source, string stdout, int bound)
        => NativeAllocationAudit.WriteFixture(name, source, new(1, bound), new(1, bound), NativeAllocationAudit.Bound.Any, stdout);
}
