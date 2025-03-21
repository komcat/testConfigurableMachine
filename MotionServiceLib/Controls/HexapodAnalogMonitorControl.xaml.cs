using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MotionServiceLib.Controls
{
    /// <summary>
    /// Interaction logic for HexapodAnalogMonitorControl.xaml
    /// </summary>
    public partial class HexapodAnalogMonitorControl : UserControl
    {
        private HexapodController _controller;
        private bool _isMonitoring = false;
        private EventHandler<AnalogChannelUpdateEventArgs> _analogUpdateHandler;
        private ObservableCollection<AnalogChannelViewModel> _channelViewModels = new ObservableCollection<AnalogChannelViewModel>();
        private int[] _monitoredChannels = new int[] { 5, 6 }; // Default channels, can be customized

        // Add this public event
        public event EventHandler<AnalogChannelUpdateEventArgs> AnalogDataUpdated;


        /// <summary>
        /// Creates a new instance of the HexapodAnalogMonitorControl
        /// </summary>
        public HexapodAnalogMonitorControl()
        {
            InitializeComponent();
            ChannelsDataGrid.ItemsSource = _channelViewModels;
            _analogUpdateHandler = OnAnalogUpdate;
        }

        /// <summary>
        /// Gets or sets the HexapodController for this control
        /// </summary>
        public HexapodController Controller
        {
            get { return _controller; }
            set
            {
                if (_controller != value)
                {
                    // Stop monitoring if active
                    if (_isMonitoring)
                    {
                        StopMonitoring();
                    }

                    _controller = value;
                    UpdateTitle();
                    InitializeChannels();
                }
            }
        }

        /// <summary>
        /// Gets or sets the channels to monitor
        /// </summary>
        public int[] MonitoredChannels
        {
            get { return _monitoredChannels; }
            set
            {
                if (_monitoredChannels != value)
                {
                    // Stop monitoring if active
                    if (_isMonitoring)
                    {
                        StopMonitoring();
                    }

                    _monitoredChannels = value;
                    InitializeChannels();
                }
            }
        }

        /// <summary>
        /// Initialize the channel view models based on the monitored channels
        /// </summary>
        private void InitializeChannels()
        {
            _channelViewModels.Clear();

            if (_monitoredChannels != null)
            {
                foreach (int channelId in _monitoredChannels)
                {
                    _channelViewModels.Add(new AnalogChannelViewModel
                    {
                        ChannelId = channelId,
                        Value = 0,
                        Status = "Not Read"
                    });
                }
            }
        }

        /// <summary>
        /// Updates the title based on the controller
        /// </summary>
        private void UpdateTitle()
        {
            TitleTextBlock.Text = _controller != null
                ? $"Hexapod Analog Monitor - {_controller.ControllerName}"
                : "Hexapod Analog Monitor - No Controller";
        }

        /// <summary>
        /// Event handler for the Start/Stop button
        /// </summary>
        private void StartStopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_controller == null)
            {
                MessageBox.Show("No hexapod controller assigned.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (_isMonitoring)
            {
                StopMonitoring();
            }
            else
            {
                StartMonitoring();
            }
        }

        /// <summary>
        /// Event handler for the Refresh button
        /// </summary>
        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (_controller == null)
            {
                MessageBox.Show("No hexapod controller assigned.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            RefreshButton.IsEnabled = false;
            try
            {
                await RefreshAnalogValues();
            }
            finally
            {
                RefreshButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Starts monitoring analog channels
        /// </summary>
        private void StartMonitoring()
        {
            if (_controller == null || _monitoredChannels == null || _monitoredChannels.Length == 0)
                return;

            try
            {
                // Parse update rate
                if (!int.TryParse(UpdateRateTextBox.Text, out int updateRate) || updateRate < 50)
                {
                    MessageBox.Show("Update rate must be a number and at least 50ms.", "Invalid Input",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                bool success = _controller.StartAnalogMonitoring(_monitoredChannels, updateRate, _analogUpdateHandler);

                if (success)
                {
                    _isMonitoring = true;
                    StartStopButton.Content = "Stop Monitoring";
                    StatusTextBlock.Text = "Monitoring";
                    StatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
                    UpdateRateTextBox.IsEnabled = false;
                }
                else
                {
                    MessageBox.Show("Failed to start monitoring.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting monitoring: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Stops monitoring analog channels
        /// </summary>
        private void StopMonitoring()
        {
            if (_controller == null)
                return;

            try
            {
                _controller.StopAnalogMonitoring();
                _isMonitoring = false;
                StartStopButton.Content = "Start Monitoring";
                StatusTextBlock.Text = "Not Monitoring";
                StatusTextBlock.Foreground = System.Windows.Media.Brushes.Gray;
                UpdateRateTextBox.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error stopping monitoring: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Refreshes analog values one time
        /// </summary>
        private async Task RefreshAnalogValues()
        {
            if (_controller == null || _monitoredChannels == null || _monitoredChannels.Length == 0)
                return;

            try
            {
                Dictionary<int, double> values = await _controller.GetAnalogVoltagesAsync(_monitoredChannels);

                if (values != null)
                {
                    UpdateAnalogValues(values);
                }
                else
                {
                    foreach (var vm in _channelViewModels)
                    {
                        vm.Status = "Read Failed";
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reading analog values: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Event handler for analog updates during monitoring
        /// </summary>
        private void OnAnalogUpdate(object sender, AnalogChannelUpdateEventArgs e)
        {
            // Add debugging to verify this is being called
            //Console.WriteLine($"Received update with {e.ChannelValues.Count} values");

            // Since this event comes from a background thread, we need to use the Dispatcher
            Dispatcher.Invoke(() =>
            {
                UpdateAnalogValues(e.ChannelValues);
            });

            // Forward the event to subscribers
            AnalogDataUpdated?.Invoke(this, e);
        }

        /// <summary>
        /// Updates the view models with the latest values
        /// </summary>
        private void UpdateAnalogValues(Dictionary<int, double> values)
        {
            if (values == null || values.Count == 0)
            {
                Console.WriteLine("UpdateAnalogValues called with empty or null values");
                return;
            }

            //Console.WriteLine($"Updating UI with values: {string.Join(", ", values.Select(kv => $"Ch{kv.Key}={kv.Value}"))}");

            DateTime updateTime = DateTime.Now;
            foreach (var vm in _channelViewModels)
            {
                if (values.TryGetValue(vm.ChannelId, out double value))
                {
                    // Force property change notification
                    vm.Value = value;
                    vm.Status = $"Updated at {updateTime.ToString("HH:mm:ss.fff")}";

                    // Debug
                    //Console.WriteLine($"Updated channel {vm.ChannelId} to {value}V, status: {vm.Status}");
                }
            }

            // Force refresh of the DataGrid if needed
            ChannelsDataGrid.Items.Refresh();
        }
        /// <summary>
        /// Clean up any resources when control is unloaded
        /// </summary>
        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            StopMonitoring();
        }
    }

    /// <summary>
    /// View model for an analog channel
    /// </summary>
    public class AnalogChannelViewModel : INotifyPropertyChanged
    {
        private int _channelId;
        private double _value;
        private string _status;

        public event PropertyChangedEventHandler PropertyChanged;

        public int ChannelId
        {
            get => _channelId;
            set
            {
                if (_channelId != value)
                {
                    _channelId = value;
                    OnPropertyChanged(nameof(ChannelId));
                }
            }
        }

        public double Value
        {
            get => _value;
            set
            {
                if (_value != value)
                {
                    _value = value;
                    OnPropertyChanged(nameof(Value));
                }
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                }
            }
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}