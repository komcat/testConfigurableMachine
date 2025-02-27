using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MotionServiceLib;
using Serilog;

namespace testConfigurableMachine
{
    /// <summary>
    /// Interaction logic for PathControl.xaml
    /// </summary>
    public partial class PathControl : UserControl
    {
        private MotionKernel _motionKernel;
        private MotionDevice _device;
        private string _deviceId;
        private string _graphId;
        private System.Windows.Threading.DispatcherTimer _positionUpdateTimer;
        private readonly ILogger _logger;

        public PathControl(MotionKernel kernel, MotionDevice device)
        {
            InitializeComponent();

            _motionKernel = kernel;
            _device = device;
            _deviceId = device.Id;
            _logger = Log.ForContext<PathControl>().ForContext("DeviceId", _deviceId);

            // Update device info
            DeviceIdTextBlock.Text = $"{_device.Id} ({_device.Name})";

            // Determine the graph ID for this device
            _graphId = DetermineGraphId();
            GraphIdTextBlock.Text = _graphId ?? "No graph found";

            // Start position updates
            StartPositionUpdates();

            // Initialize UI asynchronously after control is loaded
            Loaded += (s, e) => InitializeAsync();
        }

        private async void InitializeAsync()
        {
            try
            {
                // Initially disable UI elements until we've loaded the data
                DestinationsListView.IsEnabled = false;
                RefreshButton.IsEnabled = false;
                MoveToDestinationButton.IsEnabled = false;
                StatusTextBlock.Text = "Loading available destinations...";

                // Fill the destinations list
                await RefreshDestinationsAsync();

                // Enable UI elements
                DestinationsListView.IsEnabled = true;
                RefreshButton.IsEnabled = true;
                StatusTextBlock.Text = "Ready";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error initializing path control");
                StatusTextBlock.Text = "Error initializing";
                MessageBox.Show($"Error initializing path control: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StartPositionUpdates()
        {
            // Create a timer to update the current position display
            _positionUpdateTimer = new System.Windows.Threading.DispatcherTimer();
            _positionUpdateTimer.Interval = TimeSpan.FromMilliseconds(500); // Update every 500ms
            _positionUpdateTimer.Tick += async (s, e) => await UpdateCurrentPositionAsync();
            _positionUpdateTimer.Start();
        }

        private async Task UpdateCurrentPositionAsync()
        {
            try
            {
                string currentPositionName = await GetCurrentPositionNameAsync();

                // Update UI on dispatcher thread (should be unnecessary due to DispatcherTimer, but just to be safe)
                Dispatcher.Invoke(() =>
                {
                    CurrentPositionTextBlock.Text = currentPositionName ?? "Unknown";
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error updating current position");
            }
        }

        private async Task<string> GetCurrentPositionNameAsync()
        {
            try
            {
                // Get the current position
                var currentPosition = await _motionKernel.GetCurrentPositionAsync(_deviceId);
                if (currentPosition == null)
                {
                    return null;
                }

                // Find the closest matching position
                string closestPositionName = null;
                double minDistance = double.MaxValue;

                foreach (var position in _device.Positions)
                {
                    double distance = CalculatePositionDistance(currentPosition, position.Value);

                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        closestPositionName = position.Key;
                    }
                }

                // Position tolerance - consider the device at a position if within this tolerance
                double tolerance = _device.Type == MotionDeviceType.Hexapod ? 0.05 : 0.5;

                if (minDistance <= tolerance)
                {
                    return closestPositionName;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error determining current position name");
                return null;
            }
        }

        private double CalculatePositionDistance(Position pos1, Position pos2)
        {
            double dx = pos1.X - pos2.X;
            double dy = pos1.Y - pos2.Y;
            double dz = pos1.Z - pos2.Z;

            // For hexapods, also consider rotation
            if (_device.Type == MotionDeviceType.Hexapod)
            {
                double du = pos1.U - pos2.U;
                double dv = pos1.V - pos2.V;
                double dw = pos1.W - pos2.W;

                // Weighted distance, translation has higher weight than rotation
                return Math.Sqrt(dx * dx + dy * dy + dz * dz + 0.1 * (du * du + dv * dv + dw * dw));
            }

            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }


        // Modify your DetermineGraphId method to fix the issue with HexapodLeft vs HexapodLeft
        private string DetermineGraphId()
        {
            // First try the standard approach
            string graphId = null;

            // If the device has an explicit graph ID set, use that
            if (!string.IsNullOrEmpty(_device.GraphId))
            {
                graphId = _device.GraphId;
                _logger.Debug("Using explicit graph ID from device: {GraphId}", graphId);
                return graphId;
            }

            // For the left hexapod (device 0)
            if (_device.Id == "0" && _device.Type == MotionDeviceType.Hexapod)
            {
                graphId = "HexapodLeft";
                _logger.Debug("Using HexapodLeft graph for device 0");
                return graphId;
            }

            // For the right hexapod (device 2)
            if (_device.Id == "2" && _device.Type == MotionDeviceType.Hexapod)
            {
                graphId = "HexapodRight";
                _logger.Debug("Using HexapodRight graph for device 2");
                return graphId;
            }

            // For the bottom hexapod (device 1)
            if (_device.Id == "1" && _device.Type == MotionDeviceType.Hexapod)
            {
                graphId = "HexapodBottom";
                _logger.Debug("Using HexapodBottom graph for device 1");
                return graphId;
            }

            // For gantry
            if (_device.Type == MotionDeviceType.Gantry)
            {
                graphId = "Gantry";
                _logger.Debug("Using Gantry graph for gantry device");
                return graphId;
            }

            // Otherwise try to determine from the device name
            if (_device.Type == MotionDeviceType.Hexapod)
            {
                if (_device.Name.Contains("left", StringComparison.OrdinalIgnoreCase))
                {
                    return "HexapodLeft";
                }
                else if (_device.Name.Contains("right", StringComparison.OrdinalIgnoreCase))
                {
                    return "HexapodRight";
                }
                else if (_device.Name.Contains("bottom", StringComparison.OrdinalIgnoreCase))
                {
                    return "HexapodBottom";
                }
            }

            _logger.Warning("Could not determine graph ID for device {DeviceId} ({DeviceName})",
                _deviceId, _device.Name);
            return null;
        }
        private async Task RefreshDestinationsAsync()
        {
            try
            {
                // Clear the current list
                DestinationsListView.Items.Clear();

                // Get available destinations
                var destinations = await _motionKernel.GetAvailableDestinationsAsync(_deviceId);

                // Add debugging info
                _logger.Information("Available destinations: {DestinationCount}", destinations.Count);
                foreach (var dest in destinations)
                {
                    _logger.Information("  - Destination: {Destination}", dest);
                }


                if (destinations.Count == 0)
                {
                    // No destinations available - either no graph or not at a known position
                    NoDestinationsTextBlock.Visibility = Visibility.Visible;
                    DestinationsListView.Visibility = Visibility.Collapsed;
                }
                else
                {
                    // Found destinations - show them in the list
                    NoDestinationsTextBlock.Visibility = Visibility.Collapsed;
                    DestinationsListView.Visibility = Visibility.Visible;

                    foreach (var destination in destinations)
                    {
                        DestinationsListView.Items.Add(destination);
                    }
                }

                _logger.Information("Refreshed destinations list with {Count} items", destinations.Count);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error refreshing destinations");
                MessageBox.Show($"Error refreshing destinations: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DestinationsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Enable/disable the move button based on selection
            MoveToDestinationButton.IsEnabled = DestinationsListView.SelectedItem != null;
        }

        // Add this method to your PathControl.xaml.cs class
        private void DebugGraphInfo()
        {
            try
            {
                // Log path planner debug info
                _motionKernel.LogPathPlannerDebugInfo();

                // Check if the device has a graph
                bool hasGraph = _motionKernel.HasGraphForDevice(_device);
                _logger.Information("Device {DeviceId} ({DeviceName}) has graph: {HasGraph}",
                    _deviceId, _device.Name, hasGraph);

                // Get the graph ID for the device
                string graphId = _motionKernel.GetGraphIdForDevice(_device);
                _logger.Information("Graph ID for device {DeviceId}: {GraphId}",
                    _deviceId, graphId ?? "none");

                // Check if we're at a known position
                string currentPos = CurrentPositionTextBlock.Text;
                _logger.Information("Current position: {Position}", currentPos);

                // Add a debug button to the UI
                if (RefreshButton != null)
                {
                    RefreshButton.Content = "Debug and Refresh";
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in DebugGraphInfo");
            }
        }

        // Modify your RefreshButton_Click method to include debugging
        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshButton.IsEnabled = false;
            StatusTextBlock.Text = "Debugging and refreshing...";

            try
            {
                // Run debug first
                DebugGraphInfo();

                // Try updating the graph ID
                _graphId = DetermineGraphId();
                GraphIdTextBlock.Text = _graphId ?? "No graph found";

                // Then refresh
                await RefreshDestinationsAsync();
                StatusTextBlock.Text = "Refresh complete";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during refresh");
                StatusTextBlock.Text = "Refresh failed";
            }
            finally
            {
                RefreshButton.IsEnabled = true;
            }
        }
        private async void MoveToDestinationButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedDestination = DestinationsListView.SelectedItem as string;
            if (string.IsNullOrEmpty(selectedDestination))
            {
                MessageBox.Show("Please select a destination", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Ask for confirmation
            var result = MessageBox.Show($"Move {_device.Name} to {selectedDestination}?",
                "Confirm Movement", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            // Disable UI during movement
            DestinationsListView.IsEnabled = false;
            RefreshButton.IsEnabled = false;
            MoveToDestinationButton.IsEnabled = false;
            StatusTextBlock.Text = $"Moving to {selectedDestination}...";

            try
            {
                bool success = await _motionKernel.MoveToDestinationViaPathAsync(_deviceId, selectedDestination);

                if (success)
                {
                    StatusTextBlock.Text = $"Successfully moved to {selectedDestination}";
                    _logger.Information("Device {DeviceId} successfully moved to {Destination}",
                        _deviceId, selectedDestination);

                    // Refresh the destinations after successful movement
                    await RefreshDestinationsAsync();
                }
                else
                {
                    StatusTextBlock.Text = $"Failed to move to {selectedDestination}";
                    _logger.Warning("Device {DeviceId} failed to move to {Destination}",
                        _deviceId, selectedDestination);

                    MessageBox.Show($"Failed to move to {selectedDestination}.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error moving to destination {Destination}", selectedDestination);
                StatusTextBlock.Text = "Movement error";

                MessageBox.Show($"Error during movement: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Re-enable UI
                DestinationsListView.IsEnabled = true;
                RefreshButton.IsEnabled = true;
                MoveToDestinationButton.IsEnabled = DestinationsListView.SelectedItem != null;
            }
        }

        // Clean up when control is unloaded
        ~PathControl()
        {
            // Stop the timer to prevent memory leaks
            if (_positionUpdateTimer != null)
            {
                _positionUpdateTimer.Stop();
                _positionUpdateTimer = null;
            }
        }
    }
}