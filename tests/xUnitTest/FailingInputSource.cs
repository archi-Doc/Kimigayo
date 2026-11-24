// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Checking;

namespace XunitTest;

/// <summary>The disk, but one path's read, source read or listing throws the given exception.</summary>
/// <param name="failing">The file or directory whose read throws.</param>
/// <param name="failure">The exception.</param>
internal sealed class FailingInputSource(string failing, Exception failure) : CheckInputSource
{
    public override byte[] ReadAllBytes(string path) => SourceIdentity.PathComparer.Equals(path, failing) ? throw failure : Disk.ReadAllBytes(path);

    public override SourceContent ReadSource(string path) => SourceIdentity.PathComparer.Equals(path, failing) ? throw failure : Disk.ReadSource(path);

    public override string[] GetFiles(string directory, string pattern) => SourceIdentity.PathComparer.Equals(directory, failing) ? throw failure : Disk.GetFiles(directory, pattern);
}
