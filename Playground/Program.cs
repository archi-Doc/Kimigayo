namespace Playground;

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Arc.Unit;
using Kimigayo;
using Kimigayo.Language;
using Microsoft.Extensions.DependencyInjection;
using SimplePrompt;
using Tinyhand;



internal class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Hello, World!");

        var unit = new KimiUnit.Builder().Build();
        var serviceProvider = unit.Context.ServiceProvider;

        var kimiControl = serviceProvider.GetRequiredService<KimiControl>();
        var solution = serviceProvider.GetRequiredService<Solution>();
        solution.TryReadFile("aaa");
        // var tree = CodeTree.Parse("");

        var project = serviceProvider.GetRequiredService<Project>();
        Test1();

        /*project.AddSource("test", """
            namespace Test.Program // Comment
            public Main()
                var x = 1.23
                var list = [
                    1,
                    2,]
                var list2 = [
                    1,
                ]
                return
            """);*/
        project.AddSource("test", """
            /* Multi-line comment
            Kimigayo by archi-Doc.
            */
            namespace Playground // Single-line comment
            use Kimi.Crypto 

            Condition(Os=="Linux") // Attribute-next rule.
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
        var package = new ProjectFile.PackageClass() with
        {
            Name = "tinyhand",
            Version = "1.2",
        };
        file.Packages = [package];
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
            """);

        // kimiControl.DumpToConsole();

    }

    private static unsafe void Test1()
    {
    }
}
