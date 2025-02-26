using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MotionServiceLib;
using Serilog;

namespace testConfigurableMachine
{
    /// <summary>
    /// Interaction logic for DeviceControl.xaml
    /// </summary>
    public partial class DeviceControl : UserControl
    {
        private MotionKernel _motionKernel;
        private MotionDevice _device;
        private string _deviceId;
        private bool _isHexapod;
        private ILogger _logger;

        public ObservableCollection<PositionItem> Positions { get; private set; }

        // Constructor
        public DeviceControl(MotionKernel kernel, MotionDevice device)
        {
            InitializeComponent();

            _motionKernel = kernel;
            _device = device;
            _deviceId = device.Id;
            _isHexapod = device.Type == MotionDeviceType.Hexapod;
            _logger = Log.ForContext<DeviceControl>().ForContext("DeviceId", _deviceId);

            // Configure UI based on device type
            HexapodAxesGroup.Visibility = _isHexapod ? Visibility.Visible : Visibility.Collapsed;

            // Update device info
            DeviceIdTextBlock.Text = $"{_device.Id} ({_device.Name})";
            UpdateConnectionStatus(_motionKernel.IsDeviceConnected(_deviceId));

            // Initialize positions collection
            Positions = new ObservableCollection<PositionItem>();
            if (_device.Positions != null)
            {
                foreach (var position in _device.Positions)
                {
                    Positions.Add(new PositionItem { Name = position.Key, Position = position.Value });
                }
            }

            PositionsListView.ItemsSource = Positions;

            // Start position updates
            StartPositionUpdates();
        }

        // Position update timer
        private async void StartPositionUpdates()
        {
            try
            {
                while (true)
                {
                    await UpdateCurrentPosition();
                    await Task.Delay(100); // Update every 100ms
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in position update loop");
            }
        }

        private async Task UpdateCurrentPosition()
        {
            try
            {
                if (_motionKernel.IsDeviceConnected(_deviceId))
                {
                    var position = await _motionKernel.GetCurrentPositionAsync(_deviceId);
                    if (position != null)
                    {
                        // Update UI on the UI thread
                        Dispatcher.Invoke(() =>
                        {
                            XPositionTextBlock.Text = position.X.ToString("F3");
                            YPositionTextBlock.Text = position.Y.ToString("F3");
                            ZPositionTextBlock.Text = position.Z.ToString("F3");

                            if (_isHexapod)
                            {
                                UPositionTextBlock.Text = position.U.ToString("F3");
                                VPositionTextBlock.Text = position.V.ToString("F3");
                                WPositionTextBlock.Text = position.W.ToString("F3");
                            }

                            UpdateConnectionStatus(true);
                        });
                    }
                }
                else
                {
                    Dispatcher.Invoke(() => UpdateConnectionStatus(false));
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error updating position");
                Dispatcher.Invoke(() => UpdateConnectionStatus(false));
            }
        }

        private void UpdateConnectionStatus(bool isConnected)
        {
            ConnectionStatusTextBlock.Text = isConnected ? "Connected" : "Disconnected";
            ConnectionStatusTextBlock.Foreground = isConnected ?
                System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Red;
        }

        #region Axis Control Event Handlers

        private async void XPlus_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(XStepTextBox.Text, out double step))
            {
                await MoveAxisRelativeAsync(0, step);
            }
        }

        private async void XMinus_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(XStepTextBox.Text, out double step))
            {
                await MoveAxisRelativeAsync(0, -step);
            }
        }

        private async void YPlus_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(YStepTextBox.Text, out double step))
            {
                await MoveAxisRelativeAsync(1, step);
            }
        }

        private async void YMinus_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(YStepTextBox.Text, out double step))
            {
                await MoveAxisRelativeAsync(1, -step);
            }
        }

        private async void ZPlus_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(ZStepTextBox.Text, out double step))
            {
                await MoveAxisRelativeAsync(2, step);
            }
        }

        private async void ZMinus_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(ZStepTextBox.Text, out double step))
            {
                await MoveAxisRelativeAsync(2, -step);
            }
        }

        private async void UPlus_Click(object sender, RoutedEventArgs e)
        {
            if (_isHexapod && double.TryParse(UStepTextBox.Text, out double step))
            {
                await MoveAxisRelativeAsync(3, step);
            }
        }

        private async void UMinus_Click(object sender, RoutedEventArgs e)
        {
            if (_isHexapod && double.TryParse(UStepTextBox.Text, out double step))
            {
                await MoveAxisRelativeAsync(3, -step);
            }
        }

        private async void VPlus_Click(object sender, RoutedEventArgs e)
        {
            if (_isHexapod && double.TryParse(VStepTextBox.Text, out double step))
            {
                await MoveAxisRelativeAsync(4, step);
            }
        }

        private async void VMinus_Click(object sender, RoutedEventArgs e)
        {
            if (_isHexapod && double.TryParse(VStepTextBox.Text, out double step))
            {
                await MoveAxisRelativeAsync(4, -step);
            }
        }

        private async void WPlus_Click(object sender, RoutedEventArgs e)
        {
            if (_isHexapod && double.TryParse(WStepTextBox.Text, out double step))
            {
                await MoveAxisRelativeAsync(5, step);
            }
        }

        private async void WMinus_Click(object sender, RoutedEventArgs e)
        {
            if (_isHexapod && double.TryParse(WStepTextBox.Text, out double step))
            {
                await MoveAxisRelativeAsync(5, -step);
            }
        }

        #endregion

        #region Control Button Event Handlers

        private async void HomeDevice_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool success = await _motionKernel.HomeDeviceAsync(_deviceId);
                if (success)
                {
                    MessageBox.Show($"Device {_device.Name} successfully homed.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Failed to home device {_device.Name}.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error homing device");
                MessageBox.Show($"Error homing device: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void StopDevice_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool success = await _motionKernel.StopDeviceAsync(_deviceId);
                if (success)
                {
                    MessageBox.Show($"Device {_device.Name} stopped.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Failed to stop device {_device.Name}.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error stopping device");
                MessageBox.Show($"Error stopping device: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void MoveToPosition_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedItem = PositionsListView.SelectedItem as PositionItem;
                if (selectedItem == null)
                {
                    MessageBox.Show("Please select a position from the list.", "Information",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                bool success = await _motionKernel.MoveToPositionAsync(_deviceId, selectedItem.Name);
                if (success)
                {
                    MessageBox.Show($"Device {_device.Name} moved to position {selectedItem.Name}.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Failed to move device {_device.Name} to position {selectedItem.Name}.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error moving to position");
                MessageBox.Show($"Error moving to position: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void TeachPosition_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string positionName = TeachPositionNameTextBox.Text.Trim();

                // Validate position name
                if (string.IsNullOrWhiteSpace(positionName))
                {
                    MessageBox.Show("Please enter a name for the position.", "Information",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Confirm if position already exists
                if (_device.Positions.ContainsKey(positionName))
                {
                    var result = MessageBox.Show($"Position '{positionName}' already exists. Do you want to overwrite it?",
                        "Confirm Overwrite", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (result != MessageBoxResult.Yes)
                        return;
                }

                // Get current position
                var currentPosition = await _motionKernel.GetCurrentPositionAsync(_deviceId);
                if (currentPosition == null)
                {
                    MessageBox.Show("Failed to get current position.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Add or update the position
                bool success = await _motionKernel.TeachPositionAsync(_deviceId, positionName, currentPosition);

                if (success)
                {
                    // Directly update the UI collection without waiting for refresh
                    var existingItem = Positions.FirstOrDefault(p => p.Name == positionName);
                    if (existingItem != null)
                    {
                        // Update existing item
                        existingItem.Position = currentPosition;
                    }
                    else
                    {
                        // Add new item
                        Positions.Add(new PositionItem { Name = positionName, Position = currentPosition });
                    }

                    // Force ListView to refresh
                    PositionsListView.Items.Refresh();

                    MessageBox.Show($"Position '{positionName}' saved successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);

                    // Clear the name textbox
                    TeachPositionNameTextBox.Text = string.Empty;
                }
                else
                {
                    MessageBox.Show($"Failed to save position '{positionName}'.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error teaching position");
                MessageBox.Show($"Error teaching position: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SavePositions_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool success = await _motionKernel.SavePositionsToJsonAsync();

                if (success)
                {
                    MessageBox.Show("Positions saved to JSON file successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Failed to save positions to JSON file.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error saving positions to JSON");
                MessageBox.Show($"Error saving positions: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RefreshPositions_Click(object sender, RoutedEventArgs e)
        {
            await RefreshPositionsAsync();
        }

        #endregion

        #region Position List Handling

        private void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string filterText = FilterTextBox.Text.ToLower();

            if (string.IsNullOrWhiteSpace(filterText))
            {
                // If filter is empty, show all positions
                PositionsListView.ItemsSource = Positions;
            }
            else
            {
                // Filter positions by name
                var filteredPositions = Positions.Where(p => p.Name.ToLower().Contains(filterText)).ToList();
                PositionsListView.ItemsSource = filteredPositions;
            }

            // Force the ListView to refresh its display
            PositionsListView.Items.Refresh();
        }

        private void PositionsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // This can be used to show detailed information about the selected position
        }

        #endregion

        #region Helper Methods

        private async Task MoveAxisRelativeAsync(int axisIndex, double step)
        {
            try
            {
                // Create an array with all zeros except the targeted axis
                double[] relativeMove;

                if (_isHexapod)
                {
                    relativeMove = new double[6];
                }
                else
                {
                    relativeMove = new double[3];
                }

                // Set the value for the specified axis
                if (axisIndex < relativeMove.Length)
                {
                    relativeMove[axisIndex] = step;
                }
                else
                {
                    _logger.Warning("Invalid axis index: {AxisIndex}", axisIndex);
                    return;
                }

                // Execute the move
                bool success = await _motionKernel.MoveRelativeAsync(_deviceId, relativeMove);

                if (!success)
                {
                    _logger.Warning("Failed to move axis {AxisIndex} by {Step}", axisIndex, step);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error moving axis {AxisIndex} by {Step}", axisIndex, step);
                MessageBox.Show($"Error moving axis: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task RefreshPositionsAsync()
        {
            try
            {
                // Reload positions from JSON file - this reads from disk
                bool reloadSuccess = _motionKernel.ReloadPositionsFromJson();

                // Find the device in the updated list
                var updatedDevice = _motionKernel.GetDevices().FirstOrDefault(d => d.Id == _deviceId);
                if (updatedDevice != null)
                {
                    // Update the device reference
                    _device = updatedDevice;

                    // Clear and repopulate positions
                    Positions.Clear();

                    // Need to add the positions directly from the device object which has the latest data
                    foreach (var position in _device.Positions)
                    {
                        Positions.Add(new PositionItem { Name = position.Key, Position = position.Value });
                    }

                    // Force the ListView to refresh its display
                    PositionsListView.Items.Refresh();

                    // If there was a filter applied, reapply it
                    string currentFilter = FilterTextBox.Text;
                    if (!string.IsNullOrWhiteSpace(currentFilter))
                    {
                        // Filter positions by name
                        var filteredPositions = Positions.Where(p => p.Name.ToLower().Contains(currentFilter.ToLower())).ToList();
                        PositionsListView.ItemsSource = filteredPositions;
                    }
                    else
                    {
                        // Make sure the source is set to the full collection
                        PositionsListView.ItemsSource = Positions;
                    }

                    _logger.Information("Refreshed positions list for device {DeviceId}", _deviceId);
                }
                else
                {
                    _logger.Warning("Device {DeviceId} not found during refresh", _deviceId);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error refreshing positions");
                MessageBox.Show($"Error refreshing positions: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion
    }

    // Class to represent a position item in the list
    public class PositionItem
    {
        public string Name { get; set; }
        public Position Position { get; set; }
    }
}