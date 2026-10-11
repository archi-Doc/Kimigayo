// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class StrictBestCandidateTest
{
    [Fact]
    public void EveryFourCandidateAsymmetricRelationMatchesTheAllPairsOracle()
    {
        // All ternary pair states include ties, incomparability, cycles and every declaration order.
        var edges = new bool[4, 4];
        var counts = new int[1];
        for (var graph = 0; graph < 729; graph++)
        {
            var encoded = graph;
            for (var a = 0; a < 4; a++)
            {
                for (var b = a + 1; b < 4; b++)
                {
                    var relation = encoded % 3;
                    encoded /= 3;
                    edges[a, b] = relation == 1;
                    edges[b, a] = relation == 2;
                }
            }

            for (var eligible = 0; eligible < 16; eligible++)
            {
                var expected = -1;
                var eligibleCount = 0;
                for (var a = 0; a < 4; a++)
                {
                    if ((eligible & (1 << a)) == 0)
                    {
                        continue;
                    }

                    eligibleCount++;
                    var wins = true;
                    for (var b = 0; b < 4; b++)
                    {
                        wins &= a == b || (eligible & (1 << b)) == 0 || edges[a, b];
                    }

                    if (wins)
                    {
                        Assert.Equal(-1, expected);
                        expected = a;
                    }
                }

                counts[0] = 0;
                var order = new GraphOrder(edges, eligible, counts);
                Assert.Equal(expected, StrictBestCandidate.Select(4, order));
                Assert.InRange(counts[0], 0, Math.Max(0, 2 * (eligibleCount - 1)));
            }
        }

        Assert.Equal(-1, StrictBestCandidate.Select(0, new GraphOrder(edges, 0, counts)));
    }

    [Fact]
    [Trait("Purpose", "Allocation")]
    public void SelectionReusesFactsWithoutAllocation()
    {
        var edges = new bool[4, 4];
        edges[3, 0] = edges[3, 1] = edges[3, 2] = true;
        var order = new GraphOrder(edges, 15, new int[1]);
        var selected = -1;
        Assert.Equal(0, AllocationMeasurement.Measure(() => selected = StrictBestCandidate.Select(4, order)));
        Assert.Equal(3, selected);
    }

    private readonly record struct GraphOrder(bool[,] Edges, int Eligible, int[] Counts) : IStrictCandidateOrder
    {
        public bool IsEligible(int candidate) => (this.Eligible & (1 << candidate)) != 0;

        public bool Better(int left, int right)
        {
            this.Counts[0]++;
            return this.Edges[left, right];
        }
    }
}
