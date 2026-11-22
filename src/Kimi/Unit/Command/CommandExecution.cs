// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Build;
using Kimi.Diagnostics;

namespace Kimi.Command;

// The command layer's one entry for every command but test and lsp (PLAN G984). Each failure is classified where it is decided: the
// command's own Input error (CommandInputException, SPEC 23.2.3) and a toolchain call's failure (Toolchain, around the call only;
// SPEC 23.2.2: a toolchain, output or launch failure) print their message and fail the command (IMPL 20.3: exit 1); Project's results
// render their own faults. An exception no result owns ends the command's own result as Faulted: one CheckFaulted_Kd (Exception), no
// location (SPEC 23.2.3: an internal selection failure is Faulted with an Internal error; DIAGNOSTICS rule 9), exit 1. Cancellation
// returns 130. No exception type decides a fault here. A removed option among the arguments the parser handed the command (Tatara
// B6b4, KimiOptions.RemovedOption) is the command's input error before its action runs: each occurrence's text on standard error, so
// check --Format json writes no document (SPEC 23.3), and exit 1.
internal static class CommandExecution
{
    internal static async Task<int> Execute(Kimigayo kimigayo, string[] arguments, Func<Task<int>> action)
    {
        var removed = false;
        foreach (var argument in arguments)
        {
            if (KimiOptions.RemovedOption(argument) is { } text)
            {
                Console.Error.WriteLine(text);
                removed = true;
            }
        }

        if (removed)
        {
            return 1;
        }

        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            kimigayo.WriteLine(DiagnosticSeverity.Error, "Command cancelled.");
            return 130;
        }
        catch (CommandInputException ex)
        {
            kimigayo.WriteLine(DiagnosticSeverity.Error, ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            kimigayo.Render(DiagnosticFaults.Create(DiagnosticFault.Exception, ex.Message, null), string.Empty);
            return 1;
        }
    }

    // A command's own toolchain call (build --Manifest, run <exe>, toolchain verify): the toolchain's classification applies around the
    // call only, and its failure fails the command with the toolchain's message.
    internal static async Task<int> Toolchain(Kimigayo kimigayo, Func<Task<int>> call)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (Exception ex) when (NativeToolchain.IsToolchainFailure(ex))
        {
            kimigayo.WriteLine(DiagnosticSeverity.Error, ex.Message);
            return 1;
        }
    }
}
