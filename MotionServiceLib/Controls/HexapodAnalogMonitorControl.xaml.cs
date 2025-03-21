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

        // Add a debounce mechanism for very frequent updates
        private DateTime _lastUIUpdate = DateTime.MinValue;
        private readonly TimeSpan _minUpdateInterval = TimeSpan.FromMilliseconds(100); // 10fps max refresh rate
                                                                                       // Add retry logic for monitoring failures
        private int _monitoringRetryCount = 0;
        private const int MAX_RETRY_COUNT = 3;

        /// <summary>
        /// Creates a new instance of the HexapodAnalogMonitorControl
        /// </summary>
        public HexapodAnalogMonitorControl()
        {
            InitializeComponent();
            ChannelsDataGrid.ItemsSource = _channelViewModels;
            _analogUpdateHandler = OnAnalogUpdate;

            // Connect the Unloaded event
            this.Unloaded += UserControl_Unloaded;
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
        private async void StartMonitoring()
        {
            // Reset retry counter
            _monitoringRetryCount = 0;
            await StartMonitoringWithRetry();
        }

        private async Task StartMonitoringWithRetry()
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

                    // Reset retry counter on success
                    _monitoringRetryCount = 0;
                }
                else if (_monitoringRetryCount < MAX_RETRY_COUNT)
                {
                    _monitoringRetryCount++;
                    StatusTextBlock.Text = $"Retry {_monitoringRetryCount}/{MAX_RETRY_COUNT}...";
                    StatusTextBlock.Foreground = System.Windows.Media.Brushes.Orange;

                    // Wait a moment and retry
                    await Task.Delay(1000);
                    await StartMonitoringWithRetry();
                }
                else
                {
                    MessageBox.Show($"Failed to start monitoring after {MAX_RETRY_COUNT} attempts.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);

                    // Reset UI to non-monitoring state
                    _isMonitoring = false;
                    StartStopButton.Content = "Start Monitoring";
                    StatusTextBlock.Text = "Not Monitoring";
                    StatusTextBlock.Foreground = System.Windows.Media.Brushes.Gray;
                    UpdateRateTextBox.IsEnabled = true;
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
        // Modify RefreshAnalogValues to always update UI on the UI thread
        private async Task RefreshAnalogValues()
        {
            if (_controller == null || _monitoredChannels == null || _monitoredChannels.Length == 0)
                return;

            try
            {
                // Get values off the UI thread
                Dictionary<int, double> values = await Task.Run(() =>
                    _controller.GetAnalogVoltagesAsync(_monitoredChannels).GetAwaiter().GetResult());

                // Update UI on UI thread (we're already on UI thread in this method, but being explicit)
                await Dispatcher.InvokeAsync(() =>
                {
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
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Error reading analog values: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }
        /// <summary>
        /// Event handler for analog updates during monitoring
        /// </summary>
        private void OnAnalogUpdate(object sender, AnalogChannelUpdateEventArgs e)
        {
            // Only update UI at a reasonable rate to prevent freezing
            if (DateTime.Now - _lastUIUpdate < _minUpdateInterval)
            {
                // Skip this update to avoid UI thread overload
                return;
            }

            Dispatcher.InvokeAsync(() =>
            {
                UpdateAnalogValues(e.ChannelValues);
                _lastUIUpdate = DateTime.Now;

                // Forward the event to subscribers
                AnalogDataUpdated?.Invoke(this, e);
            });
        }
        /// <summary>
        /// Updates the view models with the latest values
        /// </summary>
        // Modify the UpdateAnalogValues method to avoid unnecessary refreshes
        private void UpdateAnalogValues(Dictionary<int, double> values)
        {
            if (values == null || values.Count == 0)
                return;

            DateTime updateTime = DateTime.Now;
            string timeString = updateTime.ToString("HH:mm:ss.fff");

            // Use batch updates if possible
            // If using .NET 4.5+, you can use:
            // ChannelsDataGrid.BeginInit();

            foreach (var vm in _channelViewModels)
            {
                if (values.TryGetValue(vm.ChannelId, out double value))
                {
                    vm.Value = value;
                    vm.Status = $"Updated at {timeString}";
                }
            }

            // ChannelsDataGrid.EndInit();

            // Only refresh if absolutely necessary - INotifyPropertyChanged should
            // handle individual cell updates automatically
            // The following line should ideally be removed:
            // ChannelsDataGrid.Items.Refresh();
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