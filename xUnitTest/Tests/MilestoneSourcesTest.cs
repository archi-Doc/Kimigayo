// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// Every authored Milestone Program source binds completely, except the programs whose milestones
// are still TODO in PLAN 4; a program of a DONE or IN_PROGRESS milestone that stops binding is a
// regression or an unrecorded SPEC conflict (a G issue), never a silent status change.
public class MilestoneSourcesTest
{
    private static readonly HashSet<int> Pending = [21, 23, 24, 29];

    [Fact]
    public void AuthoredProgramsBindUnlessTheirMilestoneIsPending()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "../../../../milestones");
        var failures = new List<string>();
        var count = 0;
        foreach (var path in Directory.GetFiles(directory, "Milestone*.kimi").OrderBy(x => x, StringComparer.Ordinal))
        {
            var number = int.Parse(Path.GetFileNameWithoutExtension(path)["Milestone".Length..], System.Globalization.CultureInfo.InvariantCulture);
            var c = Compilation.CreateForTest();
            Assert.True(c.Prepare(WindowsProfile.Target));
            c.Kotonoha.AddSource(new SourceDocument(Path.GetFileName(path), File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal)));
            var complete = c.Bind().IsComplete;
            count++;
            if (complete == Pending.Contains(number))
            {
                failures.Add($"Milestone{number}: binding {(complete ? "complete" : "incomplete")} ({string.Join("; ", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}"))})");
            }
        }

        Assert.True(count >= 21, $"Only {count} program sources found.");
        Assert.Empty(failures);
    }
}
