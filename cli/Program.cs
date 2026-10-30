using GpStudio;

if (args.Length == 0 || args is ["--help"] or ["-h"])
{
    Console.WriteLine("""
        GP Project Generator — GP Core 1.3.1 / Client 1.0

        Usage:
          ./gp-project-generator --describe catalog.json
          ./gp-project-generator --validate project.json validate-report.json
          ./gp-project-generator --generate project.json generated generate-report.json
          ./gp-project-generator --example > project.json
          ./gp-project-generator --licenses

        Only this executable is required to run. Use --example to get started.
        Read the separate AI_GUIDE.md supplied with the program for AI usage.
        Use a new output directory. Keep reports outside the generated project.
        Generation creates source code; build the CA and TA with your target toolchains.
        Exit codes: 0 = success, 1 = validation, generation, or usage error.
        """);
    return 0;
}
if (args is ["--version"])
{
    Console.WriteLine("GP Project Generator 1.0.0 (GP Core 1.3.1, Linux CLI)");
    return 0;
}
try
{
    if (args is ["--example"])
    {
        Console.Write(BundledFiles.Text("example-project.json"));
        return 0;
    }
    if (args is ["--licenses"])
    {
        foreach (string name in new[] { "LICENSE", "NOTICE.md", "LLVM-LICENSE.md", "clang-format-COPYING.md",
                                       "formatter-source.json", "DOTNET-LICENSE.txt", "DOTNET-ThirdPartyNotices.txt" })
        {
            Console.WriteLine("--- " + name + " ---");
            Console.WriteLine(name == "LICENSE" ? Data.Asset("project_license.txt") : BundledFiles.Text(name));
        }
        return 0;
    }
    int result = CommandLine.Run(args);
    if (result != 0)
        Console.Error.WriteLine($"Generation or validation failed. See the JSON report: {args[^1]}");
    return result;
}
catch (Exception error)
{
    Console.Error.WriteLine("Error: " + error.Message);
    Console.Error.WriteLine("Run ./gp-project-generator --help for usage and examples.");
    return 1;
}
