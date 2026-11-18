// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Command;

/// <summary>The command's own Input error: its inputs cannot be selected or its options do not fit (SPEC 23.2.3, IMPL 20.3, 20.3.1). Only
/// the command layer throws it, where it decides the failure (a selection read classifies its own failure there);
/// <see cref="CommandExecution"/> prints its message and fails the command.</summary>
/// <param name="message">The error.</param>
internal sealed class CommandInputException(string message) : Exception(message);
