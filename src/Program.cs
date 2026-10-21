using System.IO;
using System.Text.Json;
using System.Windows;

namespace GpStudio;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--self-test-ui")
            {
                var application = new Application();
                var window = new MainWindow();
                Data.Save(args[1], window.VerifyUi(args[1]));
                window.Close();
                application.Shutdown();
                return 0;
            }
            if (args.Length == 0)
            {
                var app = new Application();
                return app.Run(new MainWindow());
            }
            return RunCommand(args);
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "gp-project-generator-error.txt"), exception.ToString());
            if (args.Length == 0) MessageBox.Show(exception.Message, "GP Project Generator", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }

    private static int RunCommand(string[] args)
    {
        if (args.Length != (args[0] == "--describe" ? 2 : args[0] == "--validate" ? 3 : args[0] == "--generate" ? 4 : 0))
            throw new ArgumentException("Usage: --describe catalog.json | --validate project.json report.json | --generate project.json output-directory report.json");
        string report = Path.GetFullPath(args[^1]);
        if (args[0] != "--describe" && string.Equals(Path.GetFullPath(args[1]), report, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Report must not replace the configuration.");
        if (args[0] == "--generate")
        {
            string directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[2]));
            if (report.Equals(directory, StringComparison.OrdinalIgnoreCase) || report.StartsWith(directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Store the report outside the generated project.");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        try
        {
            if (args[0] == "--describe")
            {
                Data.Save(report, new
                {
                    SchemaVersion = 1, Success = true, GeneratorVersion = "1.0.0",
                    Documents = Data.Catalog.Sources, Excluded = new[] { "Core chapter 9: Peripheral and Event APIs", "Diagnostics: TEE_Panic", "Deprecated compatibility APIs and algorithm aliases" },
                    RequiredFields = new[] { "FormatVersion", "Name", "Uuid", "Features" },
                    PublicExampleKeys = ExampleInputs.Keys,
                    ProjectDefaults = new Project { Uuid = "REPLACE-WITH-NEW-UUID" },
                    CaOutputChoices = new[] { "executable", "shared", "static" },
                    LoginMethods = Generator.LoginMethods, PropertyPresets = PropertyPresets.All,
                    UseCases = Workflows.All, Scenarios = Scenarios.All, Algorithms = Data.Catalog.Algorithms, Curves = Data.Catalog.Curves, Apis = Data.Catalog.Apis,
                    Rules = new[] { "Feature.Kind selects a scenario. Feature.Algorithm selects a matching family.",
                        "Feature.Options values are strings; unknown fields and choices are rejected.",
                        "Version is a nonempty string without control characters, for example 1.0.1; legacy uint32 values are also accepted.",
                        "Defaults depend on the selected algorithm. Validate before generation.",
                        "Algorithm declarations are separate from target runtime support.",
                        "Generation does not compile, deploy or run the project." }
                });
                return 0;
            }
            Project project = Data.Read(args[1]);
            Generated generated = Generator.Generate(project);
            string? output = null;
            if (args[0] == "--generate")
            {
                output = Path.GetFullPath(args[2]);
                Generator.Export(generated, output);
            }
            Data.Save(report, new
            {
                SchemaVersion = 1, Success = true, Command = args[0], Compiled = false,
                ProjectName = project.Name, Uuid = project.Uuid, OutputDirectory = output,
                Files = generated.Files.Keys, Warnings = generated.Warnings, Errors = Array.Empty<string>()
            });
            return 0;
        }
        catch (Exception exception)
        {
            Data.Save(report, new { SchemaVersion = 1, Success = false, Command = args[0], Compiled = false,
                Errors = new[] { exception.Message }, ErrorType = exception.GetType().Name });
            return 1;
        }
    }
}
