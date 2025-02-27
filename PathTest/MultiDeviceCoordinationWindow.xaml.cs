using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MotionServiceLib;
using Serilog;

namespace PathTest
{
    /// <summary>
    /// Interaction logic for MultiDeviceCoordinationWindow.xaml
    /// </summary>
    public partial class MultiDeviceCoordinationWindow : Window
    {
        private MotionKernel _motionKernel;
        private readonly ILogger _logger;
        private string _hexapodLeftId = "0";  // ID for hexapod left
        private string _hexapodRightId = "2"; // ID for hexapod right
        private DispatcherTimer _positionUpdateTimer;

        public MultiDeviceCoordinationWindow()
        {
            InitializeComponent();

            // Get a contextualized logger
            _logger = Log.ForContext<MultiDeviceCoordinationWindow>();

            // Set initial status
            StatusTextBlock.Text = "Ready to initialize";

            // Log initial message
            LogAction("Application started. Please initialize the motion system.");

            // Set up position update timer
            _positionUpdateTimer = new DispatcherTimer();
            _positionUpdateTimer.Interval = TimeSpan.FromMilliseconds(500);
            _positionUpdateTimer.Tick += PositionUpdateTimer_Tick;
        }

        private async void PositionUpdateTimer_Tick(object sender, EventArgs e)
        {
            if (_motionKernel == null) return;

            try
            {
                // Update left hexapod position
                if (_motionKernel.IsDeviceConnected(_hexapodLeftId))
                {
                    var position = await _motionKernel.GetCurrentPositionAsync(_hexapodLeftId);
                    if (position != null)
                    {
                        LeftHexapodPositionText.Text = $"Position: X={position.X:F2}, Y={position.Y:F2}, Z={position.Z:F2}";
                    }
                }

                // Update right hexapod position
                if (_motionKernel.IsDeviceConnected(_hexapodRightId))
                {
                    var position = await _motionKernel.GetCurrentPositionAsync(_hexapodRightId);
                    if (position != null)
                    {
                        RightHexapodPositionText.Text = $"Position: X={position.X:F2}, Y={position.Y:F2}, Z={position.Z:F2}";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error updating positions");
            }
        }

        private async void InitButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Disable button
                InitButton.IsEnabled = false;
                StatusTextBlock.Text = "Initializing motion system...";
                LogAction("Initializing motion system...");

                // Create and initialize the motion kernel
                _motionKernel = new MotionKernel();
                await _motionKernel.InitializeAsync();

                // Check if both hexapods are connected
                bool isLeftHexapodConnected = _motionKernel.IsDeviceConnected(_hexapodLeftId);
                bool isRightHexapodConnected = _motionKernel.IsDeviceConnected(_hexapodRightId);

                // Update the UI to reflect connection status
                LeftHexapodStatusText.Text = isLeftHexapodConnected ? "Connected" : "Not Connected";
                RightHexapodStatusText.Text = isRightHexapodConnected ? "Connected" : "Not Connected";

                if (!isLeftHexapodConnected || !isRightHexapodConnected)
                {
                    StatusTextBlock.Text = "Error: One or both hexapods are not connected";
                    LogAction("Error: One or both hexapods are not connected");

                    string errorMessage = "The following devices could not be connected:\n";
                    if (!isLeftHexapodConnected) errorMessage += "- Hexapod Left (Device 0)\n";
                    if (!isRightHexapodConnected) errorMessage += "- Hexapod Right (Device 2)\n";
                    errorMessage += "\nPlease check these devices.";

                    LogAction(errorMessage);

                    MessageBox.Show(errorMessage, "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);

                    // Enable init button so they can try again
                    InitButton.IsEnabled = true;
                    return;
                }

                // Start position updates
                _positionUpdateTimer.Start();

                // Enable all test buttons once initialization is successful
                ParallelTestButton.IsEnabled = true;
                SequentialTestButton.IsEnabled = true;
                CoordinatedTestButton.IsEnabled = true;
                CancelOperationButton.IsEnabled = true;
                HomeAllButton.IsEnabled = true;

                StatusTextBlock.Text = "System initialized successfully. Ready to run tests.";
                LogAction("Motion system initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error initializing motion system");
                StatusTextBlock.Text = "Error: " + ex.Message;
                LogAction($"Error initializing motion system: {ex.Message}");

                MessageBox.Show($"Failed to initialize motion system: {ex.Message}",
                    "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);

                // Enable init button so they can try again
                InitButton.IsEnabled = true;
            }
        }

        private async void ParallelTestButton_Click(object sender, RoutedEventArgs e)
        {
            DisableAllButtons();
            StatusTextBlock.Text = "Running parallel path test...";
            LogAction("Starting parallel path test...");

            try
            {
                // First ensure both hexapods are at home position
                await EnsureDevicesAtHomeAsync();

                // Define paths for both hexapods
                var devicePaths = new Dictionary<string, List<string>>
                {
                    [_hexapodLeftId] = new List<string> { "Home", "ApproachLensGrip", "LensGrip" },
                    [_hexapodRightId] = new List<string> { "Home", "ApproachLensGrip", "LensGrip" }
                };

                // Execute paths in parallel
                LogAction("Executing paths in parallel");
                var results = await _motionKernel.ExecuteParallelPathsAsync(devicePaths);

                // Display results
                bool allSucceeded = results.Values.All(success => success);
                if (allSucceeded)
                {
                    StatusTextBlock.Text = "Parallel test completed successfully";
                    LogAction("Parallel test completed successfully");
                    MessageBox.Show("All devices completed their paths successfully.",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusTextBlock.Text = "One or more devices failed to complete their paths";
                    LogAction("Parallel test failed for some devices");

                    string failedDevices = string.Join(", ",
                        results.Where(r => !r.Value).Select(r => $"Device {r.Key}"));

                    LogAction($"Failed devices: {failedDevices}");

                    MessageBox.Show($"The following devices failed: {failedDevices}",
                        "Test Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // Return devices to home
                await ReturnDevicesToHomeAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during parallel test");
                StatusTextBlock.Text = "Error during parallel test";
                LogAction($"Error: {ex.Message}");

                MessageBox.Show($"Error during parallel test: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                EnableAllButtons();
            }
        }

        private async void SequentialTestButton_Click(object sender, RoutedEventArgs e)
        {
            DisableAllButtons();
            StatusTextBlock.Text = "Running sequential path test...";
            LogAction("Starting sequential path test...");

            try
            {
                // First ensure both hexapods are at home position
                await EnsureDevicesAtHomeAsync();

                // Define ordered sequence of paths
                var devicePathsSequence = new List<(string DeviceId, List<string> Path)>
                {
                    (_hexapodLeftId, new List<string> { "Home", "ApproachLensGrip", "LensGrip" }),
                    (_hexapodRightId, new List<string> { "Home", "ApproachLensGrip", "LensGrip" })
                };

                // Execute paths sequentially
                LogAction("Executing paths sequentially");
                var results = await _motionKernel.ExecuteSequentialPathsAsync(devicePathsSequence);

                // Display results
                bool allSucceeded = results.Values.All(success => success);
                if (allSucceeded)
                {
                    StatusTextBlock.Text = "Sequential test completed successfully";
                    LogAction("Sequential test completed successfully");
                    MessageBox.Show("All devices completed their paths successfully.",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusTextBlock.Text = "One or more devices failed to complete their paths";
                    LogAction("Sequential test failed for some devices");

                    string failedDevices = string.Join(", ",
                        results.Where(r => !r.Value).Select(r => $"Device {r.Key}"));

                    LogAction($"Failed devices: {failedDevices}");

                    MessageBox.Show($"The following devices failed: {failedDevices}",
                        "Test Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // Return devices to home
                await ReturnDevicesToHomeAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during sequential test");
                StatusTextBlock.Text = "Error during sequential test";
                LogAction($"Error: {ex.Message}");

                MessageBox.Show($"Error during sequential test: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                EnableAllButtons();
            }
        }

        private async void CoordinatedTestButton_Click(object sender, RoutedEventArgs e)
        {
            DisableAllButtons();
            StatusTextBlock.Text = "Running coordinated operation test...";
            LogAction("Starting coordinated operation test...");

            try
            {
                // First ensure both hexapods are at home position
                await EnsureDevicesAtHomeAsync();

                // Define a complex coordinated operation
                var coordinatedOperation = new CoordinatedOperation
                {
                    Name = "Lens Transfer Operation",
                    Steps = new List<CoordinationStep>
                    {
                        // Step 1: Move both hexapods to their approach positions in parallel
                        new CoordinationStep
                        {
                            Description = "Move to approach positions",
                            ExecutionType = StepExecutionType.Parallel,
                            DevicePaths = new Dictionary<string, List<string>>
                            {
                                [_hexapodLeftId] = new List<string> { "Home", "ApproachLensGrip" },
                                [_hexapodRightId] = new List<string> { "Home", "ApproachLensGrip" }
                            },
                            OnCompletion = async () =>
                            {
                                LogAction("Both hexapods reached approach positions");
                                await Task.Delay(1000); // Wait for stability
                            }
                        },
                        
                        // Step 2: Left hexapod grips the lens
                        new CoordinationStep
                        {
                            Description = "Left hexapod grips lens",
                            ExecutionType = StepExecutionType.Sequential,
                            DevicePaths = new Dictionary<string, List<string>>
                            {
                                [_hexapodLeftId] = new List<string> { "ApproachLensGrip", "LensGrip" }
                            },
                            OnCompletion = async () =>
                            {
                                LogAction("Left hexapod has gripped the lens");
                                await Task.Delay(1000); // Wait for grip to stabilize
                                
                                // In a real application, we might wait for a sensor or operator confirmation here
                                var result = MessageBox.Show(
                                    "Left hexapod has gripped the lens. Continue with operation?",
                                    "Continue Operation",
                                    MessageBoxButton.YesNo,
                                    MessageBoxImage.Question);

                                if (result != MessageBoxResult.Yes)
                                {
                                    LogAction("Operation cancelled by user");
                                    throw new OperationCanceledException("Operation cancelled by user");
                                }
                            }
                        },
                        
                        // Step 3: Left moves back to approach, right moves to grip simultaneously
                        new CoordinationStep
                        {
                            Description = "Complete transfer",
                            ExecutionType = StepExecutionType.Parallel,
                            DevicePaths = new Dictionary<string, List<string>>
                            {
                                [_hexapodLeftId] = new List<string> { "LensGrip", "ApproachLensGrip" },
                                [_hexapodRightId] = new List<string> { "ApproachLensGrip", "LensGrip" }
                            },
                            OnCompletion = async () =>
                            {
                                LogAction("Transfer completed successfully");
                                await Task.Delay(1000); // Wait for stability
                            }
                        }
                    },
                    OnFailure = async (stepIndex, step) =>
                    {
                        LogAction($"Step {stepIndex + 1} failed: {step.Description}");
                        LogAction("Running failure handling...");

                        // In a real application, we might have specific recovery actions
                        await Task.Delay(500);

                        // Return all devices to safe positions
                        if (_motionKernel.IsDeviceConnected(_hexapodLeftId))
                        {
                            await _motionKernel.MoveToPositionAsync(_hexapodLeftId, "ApproachLensGrip");
                        }

                        if (_motionKernel.IsDeviceConnected(_hexapodRightId))
                        {
                            await _motionKernel.MoveToPositionAsync(_hexapodRightId, "ApproachLensGrip");
                        }
                    }
                };

                // Execute the coordinated operation
                LogAction("Executing coordinated operation");
                bool success = await _motionKernel.ExecuteCoordinatedOperationAsync(coordinatedOperation);

                // Display results
                if (success)
                {
                    StatusTextBlock.Text = "Coordinated operation completed successfully";
                    LogAction("Coordinated operation completed successfully");
                    MessageBox.Show("The coordinated operation was completed successfully.",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusTextBlock.Text = "Coordinated operation failed";
                    LogAction("Coordinated operation failed");
                    MessageBox.Show("The coordinated operation failed to complete.",
                        "Operation Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // Return devices to home
                await ReturnDevicesToHomeAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during coordinated test");
                StatusTextBlock.Text = "Error during coordinated test";
                LogAction($"Error: {ex.Message}");

                MessageBox.Show($"Error during coordinated test: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                EnableAllButtons();
            }
        }

        private void CancelOperationButton_Click(object sender, RoutedEventArgs e)
        {
            if (_motionKernel == null) return;

            try
            {
                LogAction("Cancelling current operation");
                _motionKernel.CancelCoordinatedOperation();
                StatusTextBlock.Text = "Operation cancelled by user";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error cancelling operation");
                LogAction($"Error cancelling operation: {ex.Message}");
            }
        }

        private async void HomeAllButton_Click(object sender, RoutedEventArgs e)
        {
            DisableAllButtons();
            StatusTextBlock.Text = "Homing all devices...";
            LogAction("Homing all devices...");

            try
            {
                await EnsureDevicesAtHomeAsync();
                StatusTextBlock.Text = "All devices homed successfully";
                LogAction("All devices homed successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error homing devices");
                StatusTextBlock.Text = "Error homing devices";
                LogAction($"Error homing devices: {ex.Message}");

                MessageBox.Show($"Error homing devices: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                EnableAllButtons();
            }
        }

        #region Helper Methods

        private void LogAction(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            Dispatcher.Invoke(() =>
            {
                LogTextBlock.Text += $"[{timestamp}] {message}\n";
                // Scroll to the bottom
                LogTextBlock.Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() => { })).Wait();
            });
        }

        private void DisableAllButtons()
        {
            InitButton.IsEnabled = false;
            ParallelTestButton.IsEnabled = false;
            SequentialTestButton.IsEnabled = false;
            CoordinatedTestButton.IsEnabled = false;
            HomeAllButton.IsEnabled = false;
        }

        private void EnableAllButtons()
        {
            if (_motionKernel != null)
            {
                // Only enable operation buttons if motion system is initialized
                ParallelTestButton.IsEnabled = true;
                SequentialTestButton.IsEnabled = true;
                CoordinatedTestButton.IsEnabled = true;
                HomeAllButton.IsEnabled = true;
                CancelOperationButton.IsEnabled = true;
            }

            // Always enable init button
            InitButton.IsEnabled = true;
        }

        private async Task EnsureDevicesAtHomeAsync()
        {
            LogAction("Moving devices to home position...");

            // Move both hexapods to home simultaneously
            var tasks = new List<Task<bool>>();

            if (_motionKernel.IsDeviceConnected(_hexapodLeftId))
            {
                tasks.Add(_motionKernel.MoveToPositionAsync(_hexapodLeftId, "Home"));
            }

            if (_motionKernel.IsDeviceConnected(_hexapodRightId))
            {
                tasks.Add(_motionKernel.MoveToPositionAsync(_hexapodRightId, "Home"));
            }

            // Wait for all to complete
            await Task.WhenAll(tasks);

            // Check if all succeeded
            bool allSucceeded = tasks.All(t => t.Result);
            if (!allSucceeded)
            {
                LogAction("Warning: One or more devices failed to reach home position");
            }
            else
            {
                LogAction("All devices are at home position");
            }

            // Small delay for stability
            await Task.Delay(1000);
        }

        private async Task ReturnDevicesToHomeAsync()
        {
            LogAction("Returning devices to home position...");
            await EnsureDevicesAtHomeAsync();
        }

        #endregion

        protected override void OnClosed(EventArgs e)
        {
            // Stop the timer
            _positionUpdateTimer?.Stop();

            // Clean up resources
            _motionKernel?.Dispose();

            base.OnClosed(e);
        }
    }
}