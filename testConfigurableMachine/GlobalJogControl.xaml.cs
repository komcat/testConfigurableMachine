using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MotionServiceLib;
using Serilog;
using System.IO;
using System.Text.Json;

namespace testConfigurableMachine
{
    /// <summary>
    /// Interaction logic for GlobalJogControl.xaml
    /// </summary>
    public partial class GlobalJogControl : UserControl
    {
        private MotionKernel _motionKernel;
        private double _currentStepSize = 0.01; // Default step size
        private readonly ILogger _logger;
        private Dictionary<string, TransformationMatrix> _deviceTransformations;

        public ObservableCollection<DeviceViewModel> Devices { get; set; } = new ObservableCollection<DeviceViewModel>();

        public GlobalJogControl(MotionKernel kernel)
        {
            InitializeComponent();

            _motionKernel = kernel;
            _logger = Log.ForContext<GlobalJogControl>();

            // Load transformation matrices
            LoadTransformationMatrices();

            // Initialize the devices list
            InitializeDevicesList();

            // Select default step size
            SelectDefaultStepSize();
        }

        private void LoadTransformationMatrices()
        {
            try
            {
                _deviceTransformations = new Dictionary<string, TransformationMatrix>();

                string filePath = Path.Combine("Config", "DeviceTransformations.json");
                if (File.Exists(filePath))
                {
                    string jsonContent = File.ReadAllText(filePath);
                    var transformations = JsonSerializer.Deserialize<List<DeviceTransformation>>(jsonContent);

                    foreach (var device in transformations)
                    {
                        _deviceTransformations[device.DeviceId] = device.Matrix;
                    }

                    _logger.Information("Loaded transformation matrices for {Count} devices", _deviceTransformations.Count);
                }
                else
                {
                    // Create default transformation matrices (identity matrices)
                    _logger.Warning("Transformation matrix file not found. Using identity matrices for all devices.");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading transformation matrices");
                // Continue with empty dictionary - will use identity matrices
                _deviceTransformations = new Dictionary<string, TransformationMatrix>();
            }
        }

        private void InitializeDevicesList()
        {
            try
            {
                Devices.Clear();

                var connectedDevices = _motionKernel.GetConnectedDevices();
                foreach (var device in connectedDevices)
                {
                    Devices.Add(new DeviceViewModel
                    {
                        Id = device.Id,
                        Name = $"{device.Name} ({device.Type})",
                        Type = device.Type,
                        IsSelected = false
                    });
                }

                DevicesListBox.ItemsSource = Devices;
                _logger.Information("Initialized devices list with {Count} devices", Devices.Count);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error initializing devices list");
            }
        }

        private void SelectDefaultStepSize()
        {
            // Select the default step size (0.01)
            foreach (ListBoxItem item in StepSizeListBox.Items)
            {
                if (item.Tag.ToString() == "0.01")
                {
                    item.IsSelected = true;
                    break;
                }
            }
        }

        #region Event Handlers

        private void DevicesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // No action needed - selection is handled by the IsSelected property binding
        }

        private void StepSizeListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItem = StepSizeListBox.SelectedItem as ListBoxItem;
            if (selectedItem != null && double.TryParse(selectedItem.Tag.ToString(), out double stepSize))
            {
                _currentStepSize = stepSize;
                _logger.Debug("Step size changed to {StepSize}", _currentStepSize);
            }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            bool allSelected = Devices.All(d => d.IsSelected);

            foreach (var device in Devices)
            {
                device.IsSelected = !allSelected;
            }

            // Force UI refresh
            DevicesListBox.Items.Refresh();
        }

        private async void Left_Click(object sender, RoutedEventArgs e)
        {
            await MoveSelectedDevices(Direction.Left);
        }

        private async void Right_Click(object sender, RoutedEventArgs e)
        {
            await MoveSelectedDevices(Direction.Right);
        }

        private async void Up_Click(object sender, RoutedEventArgs e)
        {
            await MoveSelectedDevices(Direction.Up);
        }

        private async void Down_Click(object sender, RoutedEventArgs e)
        {
            await MoveSelectedDevices(Direction.Down);
        }

        private async void In_Click(object sender, RoutedEventArgs e)
        {
            await MoveSelectedDevices(Direction.In);
        }

        private async void Out_Click(object sender, RoutedEventArgs e)
        {
            await MoveSelectedDevices(Direction.Out);
        }

        #endregion

        #region Movement Methods

        private async Task MoveSelectedDevices(Direction direction)
        {
            var selectedDevices = Devices.Where(d => d.IsSelected).ToList();

            if (selectedDevices.Count == 0)
            {
                MessageBox.Show("Please select at least one device to move.",
                    "No Device Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Create a base vector for the desired motion direction
            double[] baseVector = GetBaseVectorForDirection(direction);

            // Move each selected device
            List<Task> moveTasks = new List<Task>();
            foreach (var device in selectedDevices)
            {
                moveTasks.Add(MoveDevice(device.Id, baseVector));
            }

            // Wait for all moves to complete
            await Task.WhenAll(moveTasks);

            _logger.Information("Completed {Direction} movement for {Count} devices",
                direction.ToString(), selectedDevices.Count);
        }

        // Update the GetBaseVectorForDirection method in GlobalJogControl.xaml.cs
        private double[] GetBaseVectorForDirection(Direction direction)
        {
            // Create a base vector representing the global coordinate movement
            // Left = X-, Right = X+
            // In = Y-, Out = Y+
            // Up = Z+, Down = Z-
            switch (direction)
            {
                case Direction.Left:
                    return new double[] { -_currentStepSize, 0, 0 }; // X-
                case Direction.Right:
                    return new double[] { _currentStepSize, 0, 0 };  // X+
                case Direction.In:
                    return new double[] { 0, -_currentStepSize, 0 }; // Y-
                case Direction.Out:
                    return new double[] { 0, _currentStepSize, 0 };  // Y+
                case Direction.Up:
                    return new double[] { 0, 0, _currentStepSize };  // Z+
                case Direction.Down:
                    return new double[] { 0, 0, -_currentStepSize }; // Z-
                default:
                    return new double[] { 0, 0, 0 };
            }
        }
        private async Task MoveDevice(string deviceId, double[] globalVector)
        {
            try
            {
                // Get the device-specific transformation
                TransformationMatrix transform = GetTransformationMatrix(deviceId);

                // Apply transformation to convert global coordinates to device-specific coordinates
                double[] deviceVector;

                if (transform != null)
                {
                    deviceVector = ApplyTransformation(globalVector, transform);
                    _logger.Debug("Device {DeviceId}: Transformed {GlobalVector} to {DeviceVector}",
                        deviceId, string.Join(",", globalVector), string.Join(",", deviceVector));
                }
                else
                {
                    // Use global vector directly if no transformation is defined
                    deviceVector = globalVector;
                }

                // Extend the vector if needed for hexapods (add rotation components)
                var device = _motionKernel.GetDevices().FirstOrDefault(d => d.Id == deviceId);
                if (device?.Type == MotionDeviceType.Hexapod)
                {
                    // For hexapods, extend to 6 elements (X, Y, Z, U, V, W)
                    var extendedVector = new double[6];
                    Array.Copy(deviceVector, extendedVector, Math.Min(deviceVector.Length, 3)); // Copy XYZ
                    deviceVector = extendedVector;
                }

                // Execute the move
                bool success = await _motionKernel.MoveRelativeAsync(deviceId, deviceVector);

                if (!success)
                {
                    _logger.Warning("Failed to move device {DeviceId}", deviceId);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error moving device {DeviceId}", deviceId);
            }
        }

        private TransformationMatrix GetTransformationMatrix(string deviceId)
        {
            if (_deviceTransformations.TryGetValue(deviceId, out var matrix))
            {
                return matrix;
            }

            // Return an identity matrix if no transformation is defined for this device
            return new TransformationMatrix
            {
                M11 = 1,
                M12 = 0,
                M13 = 0,
                M21 = 0,
                M22 = 1,
                M23 = 0,
                M31 = 0,
                M32 = 0,
                M33 = 1
            };
        }

        private double[] ApplyTransformation(double[] vector, TransformationMatrix matrix)
        {
            // Apply 3x3 transformation matrix to a 3D vector
            double[] result = new double[3];

            result[0] = matrix.M11 * vector[0] + matrix.M12 * vector[1] + matrix.M13 * vector[2];
            result[1] = matrix.M21 * vector[0] + matrix.M22 * vector[1] + matrix.M23 * vector[2];
            result[2] = matrix.M31 * vector[0] + matrix.M32 * vector[1] + matrix.M33 * vector[2];

            return result;
        }

        #endregion
    }

    #region Helper Classes

    public enum Direction
    {
        Left,
        Right,
        Up,
        Down,
        In,
        Out
    }

    public class DeviceViewModel
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public MotionDeviceType Type { get; set; }
        public bool IsSelected { get; set; }
    }

    public class DeviceTransformation
    {
        public string DeviceId { get; set; }
        public TransformationMatrix Matrix { get; set; }
    }


    /* Transformation Matrix explained:
Row 1 (M11, M12, M13): Device's X-axis

M11: Contribution of global X to device's X
M12: Contribution of global Y to device's X
M13: Contribution of global Z to device's X

Row 2 (M21, M22, M23): Device's Y-axis

M21: Contribution of global X to device's Y
M22: Contribution of global Y to device's Y
M23: Contribution of global Z to device's Y

Row 3 (M31, M32, M33): Device's Z-axis

M31: Contribution of global X to device's Z
M32: Contribution of global Y to device's Z
M33: Contribution of global Z to device's Z

    No transformation (identity matrix):
[ 1  0  0 ]
[ 0  1  0 ]
[ 0  0  1 ]

    90° Rotation around Z axis:
Copy[ 0 -1  0 ]
[ 1  0  0 ]
[ 0  0  1 ]

Global X maps to device Y
Global Y maps to negative device X
Global Z remains device Z


Z-axis as X-axis:
Copy[ 0  0  1 ]
[ 0  1  0 ]
[ 1  0  0 ]

Global X maps to device Z
Global Y remains device Y
Global Z maps to device X

    When a global movement vector [x, y, z] is multiplied by this matrix, it produces the corresponding movement in the device's coordinate system. For example, if we have the transformation matrix for Device 1 and want to move in the global +X direction [1, 0, 0], the resulting device-specific movement would be:
Copy[ M11  M12  M13 ] [ 1 ]   [ M11 ]
[ M21  M22  M23 ] [ 0 ] = [ M21 ]
[ M31  M32  M33 ] [ 0 ]   [ M31 ]
This is how the system knows which direction to move each device when you press a directional button in the global control.
     */

    public class TransformationMatrix
    {
        // 3x3 Matrix for transforming XYZ coordinates
        public double M11 { get; set; }
        public double M12 { get; set; }
        public double M13 { get; set; }
        public double M21 { get; set; }
        public double M22 { get; set; }
        public double M23 { get; set; }
        public double M31 { get; set; }
        public double M32 { get; set; }
        public double M33 { get; set; }
    }

    #endregion
}