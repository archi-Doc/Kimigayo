// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;

namespace Kimi.Command;

/// <summary>
/// <c>kimi check --Format json</c> (SPEC 23.3.6.8): checks one check unit through the shared check entry, as the language server does
/// for the same inputs, and writes its <see cref="CheckDocument"/> to standard output; everything else goes to standard error.
/// </summary>
internal static class CheckJsonOutput
{
    /// <summary>Runs the check and writes the document.</summary>
    /// <param name="options">The command options.</param>
    /// <param name="args">The input: a project file, a directory holding one, or a source file; the current directory by default.</param>
    /// <param name="cancellationToken">Cancels the check; a cancelled check writes nothing.</param>
    /// <returns>The exit code: zero when the result is accepted.</returns>
    public static int Run(KimiOptions options, string[] args, CancellationToken cancellationToken)
    {
        var stdout = Console.Out;
        Console.SetOut(Console.Error);
        var document = Create(options, args, cancellationToken);
        stdout.WriteLine(JsonSerializer.Serialize(document, DiagnosticJsonContext.Default.CheckDocument));
        stdout.Flush();
        return document.Accepted ? 0 : 1;
    }

    /// <summary>Checks one unit and forms its document.</summary>
    /// <param name="options">The command options; <c>Target</c> selects the unit's target when the project configures several.</param>
    /// <param name="args">The input.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The document; a project that cannot be found, loaded or given one target is Blocked (SPEC 23.3.3).</returns>
    internal static CheckDocument Create(KimiOptions options, string[] args, CancellationToken cancellationToken)
    {
        var inputs = new HashingInputSource(CheckInputSource.Disk);
        var kimigayo = Kimigayo.CreateSilent();
        var target = options.Target;
        if (!TryResolveInput(args, out var input, out var failure))
        {
            var missing = CheckOutput.Blocked(DiagnosticCode.ProjectLoadFailed_Kd, input is null ? default : SourceIdentity.FromPath(input), failure);
            return CheckDocument.Create(missing, input ?? string.Empty, target, CheckMode.Product, options.Debug, inputs.Hashes);
        }

        Project project;
        if (input.EndsWith(Constants.KimiExtension, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                project = Project.CreateFromSource(kimigayo, input, options);
            }
            catch (PlatformNotSupportedException ex)
            {
                return CheckDocument.Create(CheckOutput.Blocked(DiagnosticCode.ProjectPreparationFailed_Kd, SourceIdentity.FromPath(input), ex.Message), input, target, CheckMode.Product, options.Debug, inputs.Hashes);
            }

            target = project.ProjectFile.Targets[0];
        }
        else if (!Project.TryCreate(kimigayo, null, input, inputs, out project!, out var loadFailure))
        {
            return CheckDocument.Create(CheckOutput.Blocked(DiagnosticCode.ProjectLoadFailed_Kd, SourceIdentity.FromPath(input), loadFailure), input, target, CheckMode.Product, options.Debug, inputs.Hashes);
        }
        else if (target.Length == 0)
        {
            // SPEC 23.3.1: one unit has one target; a project with several needs --Target.
            var targets = project.ProjectFile.Targets;
            if (targets.Length != 1)
            {
                return CheckDocument.Create(CheckOutput.Blocked(DiagnosticCode.TargetSelectionRequired_Kd, SourceIdentity.FromPath(input)), input, target, CheckMode.Product, options.Debug, inputs.Hashes);
            }

            target = targets[0];
        }

        var output = CheckService.Run(project, target, CheckMode.Product, options.Debug, inputs, cancellationToken);
        return CheckDocument.Create(output, input, target, CheckMode.Product, options.Debug, inputs.Hashes);
    }

    // The one input of the unit: a project file, a directory that holds exactly one, or a source file for an implicit project.
    private static bool TryResolveInput(string[] args, [NotNullWhen(true)] out string? input, out string? failure)
    {
        failure = null;
        input = null;
        if (args.Length > 1)
        {
            failure = "JSON output checks one project; give one project file, directory or source file.";
            return false;
        }

        var path = Path.GetFullPath(args.Length == 0 ? Directory.GetCurrentDirectory() : args[0]);
        if (Directory.Exists(path))
        {
            var projects = Directory.GetFiles(path, "*" + Constants.KimiProjectExtension, SearchOption.TopDirectoryOnly);
            if (projects.Length != 1)
            {
                input = path;
                failure = projects.Length == 0 ? $"No project file in '{path}'." : $"Several project files in '{path}'; give one.";
                return false;
            }

            input = Path.GetFullPath(projects[0]);
            return true;
        }

        input = path;
        if (!path.EndsWith(Constants.KimiProjectExtension, StringComparison.OrdinalIgnoreCase) && !path.EndsWith(Constants.KimiExtension, StringComparison.OrdinalIgnoreCase))
        {
            failure = $"'{path}' is neither a project file nor a source file.";
            return false;
        }

        if (!File.Exists(path))
        {
            failure = $"'{path}' does not exist.";
            return false;
        }

        return true;
    }
}
