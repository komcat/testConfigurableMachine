using System;
using System.Windows;
using MotionServiceLib;
using Serilog;
using System.IO;
using System.Text.Json;

namespace testConfigurableMachine
{
    /// <summary>
    /// Interaction logic for TestGlobalJogWindow.xaml
    /// </summary>
    public partial class TestGlobalJogWindow : Window
    {
        private MotionKernel _motionKernel;
        private readonly ILogger _logger;
        private GlobalJogControl _globalJogControl;

        public TestGlobalJogWindow()
        {
            InitializeComponent();

            // Configure Serilog if not already configured
            if (Log.Logger is Serilog.Core.Logger)
            {
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.Console(outputTemplate:
                        "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
                    .WriteTo.File("logs/global_jog_test.log",
                        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                        rollingInterval: RollingInterval.Day)
                    .CreateLogger();
            }

            // Get a contextualized logger
            _logger = Log.ForContext<TestGlobalJogWindow>();
            _logger.Information("Global Jog Test Window started");
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Initialize UI state
            StatusTextBlock.Text = "Status: Ready to initialize motion system";
            StatusBarTextBlock.Text = "Ready";

            // Ensure config directory exists
            EnsureConfigDirectory();
        }

        private void EnsureConfigDirectory()
        {
            try
            {
                // Create Config directory if it doesn't exist
                string configDir = "Config";
                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                    _logger.Information("Created Config directory");
                }

                // Check if transformation file exists, create it if not
                string transformFile = Path.Combine(configDir, "DeviceTransformations.json");
                if (!File.Exists(transformFile))
                {
                    // Create default transformation file
                    string defaultJson = CreateDefaultTransformationJson();
                    File.WriteAllText(transformFile, defaultJson);
                    _logger.Information("Created default transformation file");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error ensuring config directory and files");
            }
        }

        private string CreateDefaultTransformationJson()
        {
            // Create a default transformation matrix for each device
            var defaultTransformations = new[]
            {
                new
                {
                    DeviceId = "0",
                    Matrix = new
                    {
                        M11 = 1.0, M12 = 0.0, M13 = 0.0,
                        M21 = 0.0, M22 = 1.0, M23 = 0.0,
                        M31 = 0.0, M32 = 0.0, M33 = 1.0
                    }
                },
                new
                {
                    DeviceId = "1",
                    Matrix = new
                    {
                        M11 = 0.0, M12 = 1.0, M13 = 0.0,
                        M21 = -1.0, M22 = 0.0, M23 = 0.0,
                        M31 = 0.0, M32 = 0.0, M33 = 1.0
                    }
                },
                new
                {
                    DeviceId = "2",
                    Matrix = new
                    {
                        M11 = -1.0, M12 = 0.0, M13 = 0.0,
                        M21 = 0.0, M22 = -1.0, M23 = 0.0,
                        M31 = 0.0, M32 = 0.0, M33 = 1.0
                    }
                },
                new
                {
                    DeviceId = "3",
                    Matrix = new
                    {
                        M11 = 0.0, M12 = -1.0, M13 = 0.0,
                        M21 = 1.0, M22 = 0.0, M23 = 0.0,
                        M31 = 0.0, M32 = 0.0, M33 = 1.0
                    }
                }
            };

            return JsonSerializer.Serialize(defaultTransformations, new JsonSerializerOptions { WriteIndented = true });
        }

        private async void InitializeMotionSystem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Update UI state
                StatusTextBlock.Text = "Status: Initializing motion system...";
                StatusBarTextBlock.Text = "Initializing...";

                // Create and initialize the motion kernel
                _motionKernel = new MotionKernel();
                await _motionKernel.InitializeAsync();

                // Create the global jog control
                _globalJogControl = new GlobalJogControl(_motionKernel);
                GlobalJogContentPresenter.Content = _globalJogControl;

                // Update UI state
                StatusTextBlock.Text = "Status: Motion system initialized";
                StatusBarTextBlock.Text = "Ready";

                int connectedDeviceCount = _motionKernel.GetConnectedDevices().Count;
                if (connectedDeviceCount == 0)
                {
                    MessageBox.Show("No devices were connected. Check the configuration and try again.",
                        "No Devices", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    _logger.Information("Motion system initialized with {Count} connected devices", connectedDeviceCount);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error initializing motion system");
                StatusTextBlock.Text = "Status: Initialization failed";
                StatusBarTextBlock.Text = "Error";

                MessageBox.Show($"Failed to initialize motion system: {ex.Message}",
                    "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void StopAllDevices_Click(object sender, RoutedEventArgs e)
        {
            if (_motionKernel == null)
            {
                MessageBox.Show("Please initialize the motion system first.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // Update UI state
                StatusTextBlock.Text = "Status: Stopping all devices...";
                StatusBarTextBlock.Text = "Stopping...";

                bool success = await _motionKernel.StopAllDevicesAsync();

                // Update UI state
                if (success)
                {
                    StatusTextBlock.Text = "Status: All devices stopped";
                    StatusBarTextBlock.Text = "Ready";
                    _logger.Information("All devices stopped successfully");
                    MessageBox.Show("All devices have been stopped.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusTextBlock.Text = "Status: Failed to stop some devices";
                    StatusBarTextBlock.Text = "Warning";
                    _logger.Warning("Failed to stop one or more devices");
                    MessageBox.Show("Failed to stop one or more devices.", "Warning",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error stopping all devices");
                StatusTextBlock.Text = "Status: Error stopping devices";
                StatusBarTextBlock.Text = "Error";

                MessageBox.Show($"Error stopping devices: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                // Dispose of the motion kernel to clean up resources
                _motionKernel?.Dispose();
                _logger.Information("Window closed, resources disposed properly");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during window shutdown");
            }
            finally
            {
                base.OnClosed(e);
            }
        }
    }
}