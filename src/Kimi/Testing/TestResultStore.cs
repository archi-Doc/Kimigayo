// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Testing;

internal static class TestResultStore
{
    internal static void Prune(string root)
    {
        // Serialize pruning across invocations. An in-use lock is an ordinary concurrent run.
        FileStream gate;
        try
        {
            gate = new(Path.Combine(root, ".retention.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33)
        {
            return;
        }

        using (gate)
        {
            var runs = new List<(DirectoryInfo Directory, long Size)>();
            long total = 0;
            foreach (var directory in new DirectoryInfo(root).EnumerateDirectories("run-*"))
            {
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0 || File.Exists(Path.Combine(directory.FullName, ".active")) ||
                    !File.Exists(Path.Combine(directory.FullName, ".kimi-test-run")))
                {
                    continue;
                }

                var size = Size(directory);
                runs.Add((directory, size));
                total = checked(total + size);
            }

            runs.Sort(static (a, b) => a.Directory.CreationTimeUtc.CompareTo(b.Directory.CreationTimeUtc));
            for (var i = 0; i < runs.Count && (runs.Count - i > 10 || total > 1073741824); i++)
            {
                // Enumerated immediate child, recognized marker, no active run or root junction.
                runs[i].Directory.Delete(true);
                total -= runs[i].Size;
            }
        }
    }

    private static long Size(DirectoryInfo directory)
    {
        long length = 0;
        foreach (var item in directory.EnumerateFileSystemInfos())
        {
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            length = checked(length + (item is FileInfo file ? file.Length : Size((DirectoryInfo)item)));
        }

        return length;
    }
}
