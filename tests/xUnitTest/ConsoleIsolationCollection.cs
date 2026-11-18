// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>
/// Isolates in-process tests that replace the process-global <see cref="Console.Out"/> (the <c>kimi test</c> command redirects it while
/// it runs) from every other test collection, so no parallel test writes through a swapped writer (PLAN G984).
/// </summary>
[CollectionDefinition(DisableParallelization = true)]
public sealed class ConsoleIsolationCollection;
