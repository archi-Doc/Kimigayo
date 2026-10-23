// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>
/// Isolates the formerly serial nested AllocationTests from every other test collection.
/// xUnit owns scheduling; functional tests and previously parallel allocation tests remain outside this collection.
/// </summary>
[CollectionDefinition(DisableParallelization = true)]
public sealed class AllocationIsolationCollection;
