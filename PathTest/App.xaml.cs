using Serilog;
using System.Configuration;
using System.Data;
using System.Windows;

namespace PathTest;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Configure Serilog for logging
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File("logs/motion_path_test.log",
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                rollingInterval: RollingInterval.Day)
            .CreateLogger();

        try
        {
            // Handle unhandled exceptions
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                var exception = args.ExceptionObject as Exception;
                Log.Error(exception, "Unhandled application exception");
                MessageBox.Show($"An unhandled exception occurred: {exception?.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            // Show the main window
            var mainWindow = new MultiDeviceCoordinationWindow();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application failed to start");
            MessageBox.Show($"Application failed to start: {ex.Message}",
                "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Close and flush the Serilog logger
        Log.CloseAndFlush();

        base.OnExit(e);
    }
}

