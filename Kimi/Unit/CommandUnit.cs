// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Command;
using Kimi.Lsp;
using SimpleCommandLine;

namespace Kimi.Unit;

public class CommandUnit : UnitBase, IUnitPreparable, IUnitExecutable
{
    private ILogger<CommandUnit> logger;
    private UnitOptions options;

    public class Builder : UnitBuilder<Product>
    {// Builder class for customizing dependencies.
        public Builder()
            : base()
        {
            this.PreConfigure(context =>
            {
            });

            // Configuration for Unit.
            this.Configure(context =>
            {
                KimiUnit.ConfigureBase(context);

                context.AddSingleton<CommandUnit>();
                context.RegisterInstanceCreation<CommandUnit>();
                context.AddSingleton<LspServer>();

                // Command
                context.AddCommand<DefaultCommand>();
                context.AddCommand<LspCommand, LspCommand.Options>();
                context.AddCommand<BuildCommand, KimiOptions>();
                context.AddCommand<CheckCommand, KimiOptions>();
                context.AddCommand<RestoreCommand, KimiOptions>();
                context.AddCommand<EmitCommand, KimiOptions>();
                context.AddCommand<RunCommand, KimiOptions>();
                context.AddCommand<TestCommand>();

                // Logger
                context.ClearLogOutputResolvers();
                if (Program.SuppressConsoleOutput)
                {
                    context.AddLogOutputResolver(x =>
                    {
                        x.SetOutput<FileLogOutput<FileLogOutputOptions>>();
                    });
                }
                else
                {
                    context.AddLogOutputResolver(x =>
                    {// Log source/level -> Resolver() -> Output/filter
                        if (x.LogLevel <= LogLevel.Debug)
                        {
                            x.SetOutput<ConsoleLogOutput>();
                            return;
                        }

                        // x.SetOutput<ConsoleAndFileLogOutput>();
                        x.SetOutput<ConsoleLogOutput>();
                    });
                }
            });

            this.PostConfigure(context =>
            {
                var logfile = "Logs/Log.txt";
                context.SetOptions(context.GetOrCreateOptions<FileLogOutputOptions>() with
                {
                    FilePath = Path.Combine(context.DataDirectory, logfile),
                    MaxLogCapacityInMegabytes = 2,
                    ClearLogsAtStartup = false,
                });
            });
        }
    }

    public class Product : UnitProduct
    {// Unit class for customizing behaviors.
        public record Param(string Args);

        public Product(UnitContext context)
            : base(context)
        {
        }

        public async Task RunAsync(Param param)
        {
            // Create optional instances
            this.Context.CreateInstances();

            await this.Context.SendPrepareAsync();
            await this.Context.SendStartAsync();

            var parserOptions = SimpleParserOptions.Standard with
            {
                ServiceProvider = this.Context.ServiceProvider,
                RequireCommandName = false,
                RejectUnknownOptionNames = false,
                SuppressConsoleOutput = Program.SuppressConsoleOutput,
            };

            // Main
            var arguments = SimpleParserHelper.SplitArguments(param.Args, default);
            if (arguments.Length != 0 && arguments[0] == "test")
            {
                await this.Context.ServiceProvider.GetRequiredService<TestCommand>().Execute(arguments[1..], this.Context.ExecutionRoot.CancellationToken);
            }
            else
            {
                var parser = this.Context.CreateSimpleParser(parserOptions);
                if (!parser.Parse(KimiOptions.ExpandFlags(param.Args)))
                {
                    Environment.ExitCode = 1;
                }

                await parser.Execute(this.Context.ExecutionRoot.CancellationToken);
            }

            await this.Context.SendStopAsync();
            await this.Context.SendTerminateAsync();
        }
    }

    public CommandUnit(UnitContext context, ILogger<CommandUnit> logger, UnitOptions options)
        : base(context)
    {
        this.logger = logger;
        this.options = options;
    }

    async Task IUnitPreparable.PrepareAsync(UnitContext unitContext, CancellationToken cancellationToken)
    {
        // this.logger.GetWriter()?.Write("Unit prepared.");
        // this.logger.GetWriter()?.Write($"Program: {this.options.ProgramDirectory}");
        // this.logger.GetWriter()?.Write($"Data: {this.options.DataDirectory}");
    }

    async Task IUnitExecutable.StartAsync(UnitContext unitContext, CancellationToken cancellationToken)
    {
        // this.logger.GetWriter()?.Write("Unit started.");
    }

    async Task IUnitExecutable.StopAsync(UnitContext unitContext, CancellationToken cancellationToken)
    {
        // this.logger.GetWriter()?.Write("Unit stopped.");
    }

    async Task IUnitExecutable.TerminateAsync(UnitContext unitContext, CancellationToken cancellationToken)
    {
        // this.logger.GetWriter()?.Write("Exit");
    }
}
