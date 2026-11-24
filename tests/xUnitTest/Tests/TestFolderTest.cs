// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Xunit;

namespace XunitTest;

// TestFolder.Delete (RunImage design §4.2) on a folder whose sub/Program.exe, a copy of the test apphost, is mapped as an image without a
// process (LOAD_LIBRARY_AS_IMAGE_RESOURCE): the hold W-321 met after a row had awaited its executable's exit (design probe A4).
public sealed class TestFolderTest : IDisposable
{
    private const string Windows = "Image mappings are Windows'.";

    private const uint LoadLibraryAsImageResource = 0x20;

    private readonly string folder = Path.Combine(Path.GetTempPath(), "kimi-test-folder-" + Guid.NewGuid().ToString("N"));

    private readonly string executable;

    private nint image;

    public TestFolderTest()
    {
        this.executable = Path.Combine(this.folder, "sub", "Program.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(this.executable)!);
        File.Copy(Path.ChangeExtension(typeof(TestFolderTest).Assembly.Location, OperatingSystem.IsWindows() ? ".exe" : null), this.executable);
    }

    public void Dispose()
    {
        this.Release();
        if (Directory.Exists(this.folder))
        {
            File.SetAttributes(this.executable, FileAttributes.Normal);
            Directory.Delete(this.folder, true);
        }
    }

    // While the image is mapped, Directory.Delete fails as W-321's Dispose did and TestFolder.Delete waits; once it is released, the
    // removal ends and the folder is gone. Fail-before: TestFolder.Delete as Directory.Delete ended at once with that failure.
    [Fact]
    public async Task AHeldImageIsWaitedForAndThenDeleted()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        var token = TestContext.Current.CancellationToken;
        this.Map();
        Assert.Throws<UnauthorizedAccessException>(() => Directory.Delete(this.folder, true));
        var removal = Task.Run(() => TestFolder.Delete(this.folder), token);
        await Task.Delay(200, token);
        Assert.False(removal.IsCompleted, $"The removal ended while the image was held: {removal.Exception?.InnerException}");
        Assert.True(File.Exists(this.executable));
        this.Release();
        await removal.WaitAsync(TimeSpan.FromSeconds(10), token);
        Assert.False(Directory.Exists(this.folder));
    }

    // A hold past the bound fails the removal, naming the executable, with the last sharing violation inside; nothing is deleted.
    [Fact]
    public void AnImageHeldPastTheBoundFailsTheRemoval()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        this.Map();
        var start = Stopwatch.GetTimestamp();
        var error = Assert.Throws<IOException>(() => TestFolder.Delete(this.folder));
        Assert.True(Stopwatch.GetElapsedTime(start) >= TestFolder.Bound);
        Assert.Contains(this.executable, error.Message, StringComparison.Ordinal);
        Assert.Equal(unchecked((int)0x80070020), error.InnerException?.HResult); // ERROR_SHARING_VIOLATION.
        Assert.True(File.Exists(this.executable));
    }

    // Any other failure of the open is not waited for: a read-only executable's propagates at once.
    [Fact]
    public void AnyOtherFailurePropagatesAtOnce()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), Windows);
        File.SetAttributes(this.executable, FileAttributes.ReadOnly);
        var start = Stopwatch.GetTimestamp();
        Assert.Throws<UnauthorizedAccessException>(() => TestFolder.Delete(this.folder));
        Assert.True(Stopwatch.GetElapsedTime(start) < TestFolder.Bound / 2);
    }

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint LoadLibraryExW(string path, nint file, uint flags);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(nint module);

    private void Map()
    {
        this.image = LoadLibraryExW(this.executable, 0, LoadLibraryAsImageResource);
        Assert.True(this.image != 0, $"LoadLibraryExW failed: {Marshal.GetLastPInvokeError()}");
    }

    private void Release()
    {
        if (this.image != 0)
        {
            Assert.True(FreeLibrary(this.image));
            this.image = 0;
        }
    }
}
