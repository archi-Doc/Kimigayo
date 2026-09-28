// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;

namespace Kimi.Command;

internal static class CommandExecution
{
    internal static async Task<int> Execute(Kimigayo kimigayo, Func<Task<int>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            kimigayo.WriteLine(DiagnosticSeverity.Error, "Command cancelled.");
            return 130;
        }
        catch (Exception ex) when (Compiler.NativeToolchain.IsToolchainFailure(ex))
        {
            kimigayo.WriteLine(DiagnosticSeverity.Error, ex.Message);
            return 1;
        }
    }
}
