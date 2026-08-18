namespace Playground;

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Arc.Unit;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Unit;
using Microsoft.Extensions.DependencyInjection;
using SimplePrompt;
using Tinyhand;



internal class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Hello, World!");

        DiagnosticEntries.TryGet(KimiDiagnostic.ConditionMustBeBool_Kd, out var e);

        var compilation = Compilation.CreateForTest();
        var kotonoha = compilation.Kotonoha;
        var codeContext = kotonoha.CreateCodeContext();
        codeContext.Parse(kotonoha.RootKoto, $"""
            #If (true)
            public struct TestStruct: @Ia
                let x = 1
            """);

        /*var unit = new KimiUnit.Builder().Build();
        var serviceProvider = unit.Context.ServiceProvider;

        var kimigayo = serviceProvider.GetRequiredService<Kimigayo>();
        var solution = serviceProvider.GetRequiredService<Solution>();
        solution.TryReadFile("aaa");
        // var tree = CodeTree.Parse("");

        var project = serviceProvider.GetRequiredService<Project>();
        Test1();

        project.AddSource("test", """
            namespace Playground // Single-line comment
            use Kimi.Crypto 

            #If(Os=="Linux") // Attribute-next rule.
            use Kimi.Base.Linux

            public const string Name = "Test Program"

            public group Helper // namespace - use
                public const i32 Id = 123
                public Method1() => int32 // use PackageName, Helper
                    var i = [1..]
                    var j = [..=4]
                    return 1
            """);
        var result = await project.Build();

        var file = new ProjectFile();
        file.Targets = ["Windows", "Linux"];
        file.Alias = ["Kimi.Base"];
        var kotonoha = new KotonohaIdentifier() with
        {
            Name = "tinyhand",
            Version = "1.2",
        };

        file.KotonohaArray = [kotonoha];
        var st = TinyhandSerializer.SerializeToString(file);

        var file2 = TinyhandSerializer.DeserializeFromString<ProjectFile>("""
            Targets=
              Windows
              Linux
            Packages=
              + Name="tinyhand", Version="1.2"
              + Name="valuelink", Version="1.2"
            Use=
              "Kimi.Base"
            """);*/

        // kimigayo.DumpToConsole();

    }

    private static unsafe void Test1()
    {
    }
}
