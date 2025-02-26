using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using MotionServiceLib;
using Serilog;

namespace testConfigurableMachine
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private MotionKernel _motionKernel;
        private readonly ILogger _logger;

        public MainWindow()
        {
            InitializeComponent();

            // Configure Serilog for logging
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console(outputTemplate:
                    "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File("logs/motion_test.log",
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                    rollingInterval: RollingInterval.Day)
                .CreateLogger();

            // Get a contextualized logger
            _logger = Log.ForContext<MainWindow>();

            _logger.Information("Application started");
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Initialize UI state
            StatusTextBlock.Text = "Status: Ready to initialize motion system";
            StatusBarTextBlock.Text = "Ready";
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

                // Clear any existing tabs
                DevicesTabControl.Items.Clear();

                // Create tabs for each connected device
                foreach (var device in _motionKernel.GetConnectedDevices())
                {
                    try
                    {
                        // Create a tab for this device
                        var deviceControl = new DeviceControl(_motionKernel, device);

                        var tabItem = new TabItem
                        {
                            Header = $"{device.Name} ({device.Type})",
                            Content = deviceControl
                        };

                        DevicesTabControl.Items.Add(tabItem);
                        _logger.Information("Created tab for device {DeviceId} ({DeviceName})", device.Id, device.Name);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error creating tab for device {DeviceId}", device.Id);
                    }
                }

                // Update UI state
                StatusTextBlock.Text = "Status: Motion system initialized";
                StatusBarTextBlock.Text = "Ready";

                if (DevicesTabControl.Items.Count == 0)
                {
                    MessageBox.Show("No devices were connected. Check the configuration and try again.",
                        "No Devices", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    DevicesTabControl.SelectedIndex = 0;  // Select the first tab
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
                    MessageBox.Show("All devices have been stopped.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusTextBlock.Text = "Status: Failed to stop some devices";
                    StatusBarTextBlock.Text = "Warning";
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
                _logger.Information("Application shut down cleanly");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during application shutdown");
            }
            finally
            {
                base.OnClosed(e);

                // Close the Serilog logger
                Log.CloseAndFlush();
            }
        }
    }
}