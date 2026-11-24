// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Build;
using Kimi.Diagnostics;

namespace Kimi.Command;

// The command layer's one entry for every command but test and lsp (PLAN G984). Each failure is classified where it is decided: the
// command's own Input error (CommandInputException, SPEC 23.2.3) and a toolchain call's failure (Toolchain, around the call only;
// SPEC 23.2.2: a toolchain, output or launch failure) print their message and fail the command (IMPL 20.3: exit 1); Project's results
// render their own faults. An exception no result owns ends the command's own result as Faulted: one CheckFaulted_Kd (Exception), no
// location (SPEC 23.2.3: an internal selection failure is Faulted with an Internal error; DIAGNOSTICS rule 9), exit 1. Cancellation
// returns 130. No exception type decides a fault here. The command's arguments are read by KimiOptions.Read: each invalid option, a
// removed one by its removal (Tatara B6b4; IMPL 20.3.2), is the command's input error before its action runs, its text on standard
// error, so check --Format json writes no document (SPEC 23.3), and exit 1; the action receives the options and the inputs.
internal static class CommandExecution
{
    internal static async Task<int> Execute(Kimigayo kimigayo, string command, string[] arguments, Func<KimiOptions, string[], Task<int>> action)
    {
        var values = KimiOptions.Read(command, arguments, out var inputs, out var errors);
        foreach (var error in errors)
        {
            Console.Error.WriteLine(error);
        }

        if (errors.Count != 0)
        {
            return 1;
        }

        try
        {
            return await action(KimiOptions.From(values), inputs.ToArray()).ConfigureAwait(false);
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
            kimigayo.Render(DiagnosticFaults.Create(DiagnosticFault.Exception, ex, null), string.Empty);
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
