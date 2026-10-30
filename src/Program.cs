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
            return CommandLine.Run(args);
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "gp-project-generator-error.txt"), exception.ToString());
            if (args.Length == 0) MessageBox.Show(exception.Message, "GP Project Generator", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }

}
