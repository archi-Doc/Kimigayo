// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimigayo.Lsp;
using SimpleCommandLine;

namespace Kimigayo;

public class CommandUnit : UnitBase, IUnitPreparable, IUnitExecutable
{
    private ILogger<CommandUnit> logger;
    private UnitOptions options;

    private static void ConfigureBase(IUnitConfigurationContext context)
    {
        context.AddScoped<IConsoleService, ConsoleService>();
        context.AddTransient<Solution>();
        context.AddTransient<Project>();
    }

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
                ConfigureBase(context);

                context.AddSingleton<CommandUnit>();
                context.RegisterDefaultInstantiableType<CommandUnit>();
                context.AddSingleton<LspServer>();

                // Command
                context.AddCommand(typeof(DefaultCommand));
                context.AddCommand(typeof(LspCommand));

                // Logger
                context.ClearLoggerResolver();
                if (Program.SuppressConsoleOutput)
                {
                    context.AddLoggerResolver(x =>
                    {
                        x.SetOutput<FileLogger<FileLoggerOptions>>();
                    });
                }
                else
                {
                    context.AddLoggerResolver(x =>
                    {// Log source/level -> Resolver() -> Output/filter
                        if (x.LogLevel <= LogLevel.Debug)
                        {
                            x.SetOutput<ConsoleLogger>();
                            return;
                        }

                        x.SetOutput<ConsoleAndFileLogger>();
                    });
                }
            });

            this.PostConfigure(context =>
            {
                var logfile = "Logs/Log.txt";
                context.SetOptions(context.GetOptions<FileLoggerOptions>() with
                {
                    Path = Path.Combine(context.DataDirectory, logfile),
                    MaxLogCapacity = 2,
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

            await this.Context.SendPrepare();
            await this.Context.SendStart();

            var parserOptions = SimpleParserOptions.Standard with
            {
                ServiceProvider = this.Context.ServiceProvider,
                RequireStrictCommandName = false,
                RequireStrictOptionName = false,
                SuppressConsoleOutput = Program.SuppressConsoleOutput,
            };

            // Main
            await SimpleParser.ParseAndExecute(this.Context.Commands, param.Args, parserOptions, this.Context.Root.CancellationToken);

            await this.Context.SendStop();
            await this.Context.SendTerminate();
        }
    }

    public CommandUnit(UnitContext context, ILogger<CommandUnit> logger, UnitOptions options)
        : base(context)
    {
        this.logger = logger;
        this.options = options;
    }

    async Task IUnitPreparable.Prepare(UnitContext unitContext, CancellationToken cancellationToken)
    {
        // this.logger.GetWriter()?.Write("Unit prepared.");
        // this.logger.GetWriter()?.Write($"Program: {this.options.ProgramDirectory}");
        // this.logger.GetWriter()?.Write($"Data: {this.options.DataDirectory}");
    }

    async Task IUnitExecutable.Start(UnitContext unitContext, CancellationToken cancellationToken)
    {
        // this.logger.GetWriter()?.Write("Unit started.");
    }

    async Task IUnitExecutable.Stop(UnitContext unitContext, CancellationToken cancellationToken)
    {
        // this.logger.GetWriter()?.Write("Unit stopped.");
    }

    async Task IUnitExecutable.Terminate(UnitContext unitContext, CancellationToken cancellationToken)
    {
        // this.logger.GetWriter()?.Write("Exit");
    }

    [SimpleCommand("Default", Default = true)]
    public class DefaultCommand : ISimpleCommand
    {
        private readonly UnitContext unitContext;

        public DefaultCommand(UnitContext unitContext, ILogger<DefaultCommand> logger)
        {
            this.unitContext = unitContext;
            // logger.GetWriter()?.Write("Default command");
        }

        public async Task Execute(string[] args, CancellationToken cancellationToken)
        {
            Console.WriteLine($"Kimigayo ({Arc.VersionHelper.VersionString}) by archi-Doc");
        }
    }
}
