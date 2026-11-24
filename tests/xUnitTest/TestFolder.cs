// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;

namespace XunitTest;

/// <summary>The one removal of a test's folder that may hold executables the test started (directly, or through a run of Kimi's): a
/// test whose folder holds an executable it started removes the folder with <see cref="Delete"/>, every other test with
/// <c>Directory.Delete</c>. An image of a just-run executable can stay mapped by another process after the test awaited its exit, and
/// <c>Directory.Delete</c> then fails with <c>UnauthorizedAccessException</c> (W-321; RunImage design §3).
/// <para>Input: the folder. Output: the folder removed, or the first failure. Invariants: it never swallows a failure, deletes nothing
/// outside the folder, and with no executable in the folder it is exactly <c>Directory.Delete(path, true)</c>. Prohibitions: it does not
/// decide when a test's processes end (the test awaits them) and waits for nothing but each executable's exclusive open.</para></summary>
internal static class TestFolder
{
    /// <summary>Gets how long one removal waits for its executables, from its start, for the whole folder. A hold this long after a test's
    /// end is a leak worth a failure, not a wait.</summary>
    internal static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    private const int SharingViolation = unchecked((int)0x80070020); // ERROR_SHARING_VIOLATION.

    /// <summary>Removes a test's folder: each executable (<c>*.exe</c>, every subfolder, listed once before any delete) is opened for
    /// reading and writing sharing only deletion, which waits out every other open and every mapped image of it, and is deleted under
    /// that handle, so no new holder can arrive between the wait and the delete; then the folder.</summary>
    /// <param name="path">The test's folder.</param>
    /// <exception cref="IOException">An executable was still held past <see cref="Bound"/>; the last sharing violation is its inner
    /// exception. Every other failure of the open, of the file's delete or of the folder's delete propagates at once, unchanged.</exception>
    internal static void Delete(string path)
    {
        var start = Stopwatch.GetTimestamp();
        foreach (var executable in Directory.GetFiles(path, "*.exe", SearchOption.AllDirectories))
        {
            using var hold = Hold(executable, start);
            File.Delete(executable);
        }

        Directory.Delete(path, true);
    }

    // The exclusive open, retried only on its sharing violation every 10 ms until the bound.
    private static FileStream Hold(string executable, long start)
    {
        while (true)
        {
            try
            {
                return new FileStream(executable, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete);
            }
            catch (IOException held) when (held.HResult == SharingViolation)
            {
                var waited = Stopwatch.GetElapsedTime(start);
                if (waited >= Bound)
                {
                    throw new IOException($"{executable} was still held after {waited.TotalSeconds:0.0} s: no test may leave an executable of its folder open.", held);
                }

                Thread.Sleep(10);
            }
        }
    }
}
