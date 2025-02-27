using MotionServiceLib;
using Serilog;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace PathTest;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
/// <summary>
/// Interaction logic for PathTestWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private MotionKernel _motionKernel;
    private readonly ILogger _logger;
    private string _hexapodId = "0"; // ID for hexapod left

    public MainWindow()
    {
        InitializeComponent();

        // Get a contextualized logger
        _logger = Log.ForContext<MainWindow>();

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

            // Check if the left hexapod is connected
            bool isHexapodConnected = _motionKernel.IsDeviceConnected(_hexapodId);
            if (!isHexapodConnected)
            {
                StatusTextBlock.Text = "Error: Hexapod Left (Device 0) is not connected";
                MessageBox.Show("Hexapod Left (Device 0) could not be connected. Please check the device.",
                    "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Log path planner debug info to verify graph data is loaded
            _motionKernel.LogPathPlannerDebugInfo();

            // Enable the test button
            TestPathButton.IsEnabled = true;
            StatusTextBlock.Text = "System initialized successfully. Ready to run path test.";

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

    private async void TestPathButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Disable button
            TestPathButton.IsEnabled = false;
            StatusTextBlock.Text = "Running path test...";

            // Get the hexapod device
            var device = _motionKernel.GetDevices().Find(d => d.Id == _hexapodId);
            if (device == null)
            {
                StatusTextBlock.Text = "Error: Hexapod Left (Device 0) not found";
                TestPathButton.IsEnabled = true;
                return;
            }

            // First ensure we're at home position
            StatusTextBlock.Text = "Moving to Home position...";
            bool moveToHomeSuccess = await _motionKernel.MoveToPositionAsync(_hexapodId, "Home");
            if (!moveToHomeSuccess)
            {
                StatusTextBlock.Text = "Error: Failed to move to Home position";
                TestPathButton.IsEnabled = true;
                return;
            }

            await Task.Delay(1000); // Wait a bit for stability

            // Create a custom path for our test
            // We could use FindPath here, but we'll explicitly define the path for clarity
            var testPath = new List<string>
                {
                    "Home",
                    "ApproachLensGrip",
                    "LensGrip",
                    "ApproachLensGrip",
                    "Home"
                };

            StatusTextBlock.Text = $"Executing path: {string.Join(" → ", testPath)}";

            // Move along the path
            bool pathSuccess = await _motionKernel.MoveAlongPathAsync(_hexapodId, testPath);

            if (pathSuccess)
            {
                StatusTextBlock.Text = "Path test completed successfully";
                _logger.Information("Path test completed successfully");

                MessageBox.Show("The hexapod successfully completed the path:\n" +
                    "Home → ApproachLensGrip → LensGrip → ApproachLensGrip → Home",
                    "Test Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusTextBlock.Text = "Error: Path test failed";
                _logger.Warning("Path test failed");

                MessageBox.Show("The hexapod failed to complete the path. Check the logs for details.",
                    "Test Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during path test");
            StatusTextBlock.Text = "Error: " + ex.Message;

            MessageBox.Show($"Error during path test: {ex.Message}",
                "Test Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            // Re-enable button
            TestPathButton.IsEnabled = true;
        }
    }
}