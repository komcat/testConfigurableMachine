using MotionServiceLib;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace PathTest
{

    /// <summary>
    /// Interaction logic for MultiDevicePathTestWindow.xaml
    /// </summary>
    public partial class MultiDevicePathTestWindow : Window
    {
        private MotionKernel _motionKernel;
        private readonly ILogger _logger;
        private string _hexapodLeftId = "0";  // ID for hexapod left
        private string _hexapodRightId = "2"; // ID for hexapod right

        public MultiDevicePathTestWindow()
        {
            InitializeComponent();

            // Get a contextualized logger
            _logger = Log.ForContext<MultiDevicePathTestWindow>();

            // Set initial status
            StatusTextBlock.Text = "Ready to initialize";
        }

        private async void InitButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Disable button
                InitButton.IsEnabled = false;
                StatusTextBlock.Text = "Initializing motion system...";

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

                    string errorMessage = "The following devices could not be connected:\n";
                    if (!isLeftHexapodConnected) errorMessage += "- Hexapod Left (Device 0)\n";
                    if (!isRightHexapodConnected) errorMessage += "- Hexapod Right (Device 2)\n";
                    errorMessage += "\nPlease check these devices.";

                    MessageBox.Show(errorMessage, "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Log path planner debug info to verify graph data is loaded
                _motionKernel.LogPathPlannerDebugInfo();

                // Enable the test buttons
                TestSimultaneousPathButton.IsEnabled = true;
                StatusTextBlock.Text = "System initialized successfully. Ready to run path tests.";

                _logger.Information("Motion system initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error initializing motion system");
                StatusTextBlock.Text = "Error: " + ex.Message;

                MessageBox.Show($"Failed to initialize motion system: {ex.Message}",
                    "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private async void TestSimultaneousPathButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Disable button
                TestSimultaneousPathButton.IsEnabled = false;
                StatusTextBlock.Text = "Running simultaneous path test...";

                // First ensure both hexapods are at home position
                StatusTextBlock.Text = "Moving both hexapods to Home position...";

                // Move both hexapods to home simultaneously
                var homeLeftTask = _motionKernel.MoveToPositionAsync(_hexapodLeftId, "Home");
                var homeRightTask = _motionKernel.MoveToPositionAsync(_hexapodRightId, "Home");

                // Wait for both to complete
                await Task.WhenAll(homeLeftTask, homeRightTask);

                // Check if both moves were successful
                if (!homeLeftTask.Result || !homeRightTask.Result)
                {
                    StatusTextBlock.Text = "Error: Failed to move one or both hexapods to Home position";
                    TestSimultaneousPathButton.IsEnabled = true;
                    return;
                }

                await Task.Delay(1000); // Wait a bit for stability

                // Create paths for both hexapods
                var leftPath = new List<string> { "Home", "ApproachLensGrip", "LensGrip" };
                var rightPath = new List<string> { "Home", "ApproachLensGrip", "LensGrip" };

                StatusTextBlock.Text = "Executing simultaneous paths...";

                // Start both path movements simultaneously
                var leftPathTask = _motionKernel.MoveAlongPathAsync(_hexapodLeftId, leftPath);
                var rightPathTask = _motionKernel.MoveAlongPathAsync(_hexapodRightId, rightPath);

                // Wait for both to complete
                await Task.WhenAll(leftPathTask, rightPathTask);

                // Check results
                bool leftPathSuccess = leftPathTask.Result;
                bool rightPathSuccess = rightPathTask.Result;

                if (leftPathSuccess && rightPathSuccess)
                {
                    StatusTextBlock.Text = "Both hexapods completed their paths successfully";
                    _logger.Information("Simultaneous path test completed successfully");

                    MessageBox.Show("Both hexapods successfully completed their paths simultaneously.",
                        "Test Successful", MessageBoxButton.OK, MessageBoxImage.Information);

                    // Now move them back to home
                    StatusTextBlock.Text = "Moving both hexapods back to Home...";

                    var returnLeftPath = new List<string> { "LensGrip", "ApproachLensGrip", "Home" };
                    var returnRightPath = new List<string> { "LensGrip", "ApproachLensGrip", "Home" };

                    var returnLeftTask = _motionKernel.MoveAlongPathAsync(_hexapodLeftId, returnLeftPath);
                    var returnRightTask = _motionKernel.MoveAlongPathAsync(_hexapodRightId, returnRightPath);

                    await Task.WhenAll(returnLeftTask, returnRightTask);

                    StatusTextBlock.Text = "Both hexapods returned to Home";
                }
                else
                {
                    StatusTextBlock.Text = "Error: One or both path tests failed";
                    _logger.Warning("Simultaneous path test failed");

                    string errorMessage = "The following path movements failed:\n";
                    if (!leftPathSuccess) errorMessage += "- Hexapod Left\n";
                    if (!rightPathSuccess) errorMessage += "- Hexapod Right\n";

                    MessageBox.Show(errorMessage, "Test Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during simultaneous path test");
                StatusTextBlock.Text = "Error: " + ex.Message;

                MessageBox.Show($"Error during simultaneous path test: {ex.Message}",
                    "Test Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Re-enable button
                TestSimultaneousPathButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Example method showing how to implement parallel operations with dependencies
        /// </summary>
        private async Task AdvancedParallelOperationsExample()
        {
            // Advanced example - coordinated multi-device operations with dependencies
            try
            {
                // 1. First move both devices to their starting positions
                var initTasks = new List<Task<bool>>
                {
                    _motionKernel.MoveToPositionAsync(_hexapodLeftId, "Home"),
                    _motionKernel.MoveToPositionAsync(_hexapodRightId, "Home")
                };

                bool[] initResults = await Task.WhenAll(initTasks);
                if (initResults.Any(r => !r))
                {
                    _logger.Error("Failed to initialize device positions");
                    return;
                }

                // 2. Move left hexapod to approach position
                bool leftApproachResult = await _motionKernel.MoveToPositionAsync(_hexapodLeftId, "ApproachLensGrip");
                if (!leftApproachResult)
                {
                    _logger.Error("Left hexapod failed to reach approach position");
                    return;
                }

                // 3. Now run two operations in parallel:
                // - Left hexapod grips the lens
                // - Right hexapod moves to its approach position
                var parallelTasks = new List<Task<bool>>
                {
                    _motionKernel.MoveToPositionAsync(_hexapodLeftId, "LensGrip"),
                    _motionKernel.MoveToPositionAsync(_hexapodRightId, "ApproachLensGrip")
                };

                bool[] parallelResults = await Task.WhenAll(parallelTasks);
                if (parallelResults.Any(r => !r))
                {
                    _logger.Error("Parallel operations failed");
                    return;
                }

                // 4. Wait for user confirmation before continuing
                // (In a real application, you might wait for sensor input or another signal)
                var userConfirmed = MessageBox.Show(
                    "Left hexapod has gripped the lens and right hexapod is in position. Continue?",
                    "Confirm Next Step", MessageBoxButton.YesNo) == MessageBoxResult.Yes;

                if (!userConfirmed)
                {
                    _logger.Information("Operation cancelled by user");
                    return;
                }

                // 5. Complete the operation: Left returns to approach, right moves to grip
                var finalTasks = new List<Task<bool>>
                {
                    _motionKernel.MoveToPositionAsync(_hexapodLeftId, "ApproachLensGrip"),
                    _motionKernel.MoveToPositionAsync(_hexapodRightId, "LensGrip")
                };

                bool[] finalResults = await Task.WhenAll(finalTasks);
                if (finalResults.Any(r => !r))
                {
                    _logger.Error("Final operations failed");
                    return;
                }

                _logger.Information("Complex coordinated operation completed successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during advanced parallel operations");
            }
        }
    }
}
