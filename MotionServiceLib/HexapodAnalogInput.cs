using PI;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MotionServiceLib
{
    /// <summary>
    /// Extension methods for the HexapodController class to handle analog input functionality
    /// </summary>
    public static class HexapodAnalogInputExtensions
    {
        private static readonly ILogger _logger = Log.ForContext(typeof(HexapodAnalogInputExtensions));

        /// <summary>
        /// Gets the number of available analog input channels on the hexapod controller
        /// </summary>
        /// <param name="controller">The hexapod controller</param>
        /// <returns>Number of available analog input channels, or -1 if operation failed</returns>
        public static async Task<int> GetAnalogChannelCountAsync(this HexapodController controller)
        {
            if (controller == null)
                throw new ArgumentNullException(nameof(controller));

            if (!controller.IsConnected)
                return -1;

            int channelCount = -1;
            await Task.Run(() =>
            {
                // Use the GetPrivateField helper to access the controller ID
                int controllerId = GetControllerID(controller);
                int result = GCS2.qTAC(controllerId, ref channelCount);
                if (result == 0) // PI_RESULT_FAILURE
                {
                    channelCount = -1;
                }
            });

            return channelCount;
        }

        /// <summary>
        /// Gets the raw ADC values from the specified analog input channels
        /// </summary>
        /// <param name="controller">The hexapod controller</param>
        /// <param name="channelIds">Array of channel IDs to read from</param>
        /// <returns>Dictionary mapping channel IDs to their ADC values, or null if operation failed</returns>
        public static async Task<Dictionary<int, int>> GetRawAnalogValuesAsync(this HexapodController controller, int[] channelIds)
        {
            if (controller == null)
                throw new ArgumentNullException(nameof(controller));

            if (!controller.IsConnected || channelIds == null || channelIds.Length == 0)
                return null;

            Dictionary<int, int> channelValues = null;
            await Task.Run(() =>
            {
                int controllerId = GetControllerID(controller);
                int[] valueArray = new int[channelIds.Length];

                int result = GCS2.qTAD(controllerId, channelIds, valueArray, channelIds.Length);
                if (result != 0) // PI_RESULT_SUCCESS
                {
                    channelValues = new Dictionary<int, int>();
                    for (int i = 0; i < channelIds.Length; i++)
                    {
                        channelValues[channelIds[i]] = valueArray[i];
                    }
                }
            });

            return channelValues;
        }

        /// <summary>
        /// Gets the voltage values from the specified analog input channels
        /// </summary>
        /// <param name="controller">The hexapod controller</param>
        /// <param name="channelIds">Array of channel IDs to read from</param>
        /// <returns>Dictionary mapping channel IDs to their voltage values, or null if operation failed</returns>
        public static async Task<Dictionary<int, double>> GetAnalogVoltagesAsync(this HexapodController controller, int[] channelIds)
        {
            if (controller == null)
                throw new ArgumentNullException(nameof(controller));

            if (!controller.IsConnected || channelIds == null || channelIds.Length == 0)
                return null;

            Dictionary<int, double> channelValues = null;
            await Task.Run(() =>
            {
                int controllerId = GetControllerID(controller);
                double[] valueArray = new double[channelIds.Length];

                int result = GCS2.qTAV(controllerId, channelIds, valueArray, channelIds.Length);
                if (result != 0) // PI_RESULT_SUCCESS
                {
                    channelValues = new Dictionary<int, double>();
                    for (int i = 0; i < channelIds.Length; i++)
                    {
                        channelValues[channelIds[i]] = valueArray[i];
                    }
                }
            });

            return channelValues;
        }

        /// <summary>
        /// Starts continuous monitoring of analog channels on a background thread
        /// </summary>
        /// <param name="controller">The hexapod controller</param>
        /// <param name="channelIds">Array of channel IDs to monitor</param>
        /// <param name="updateInterval">Update interval in milliseconds</param>
        /// <param name="handler">Event handler to receive updates</param>
        /// <returns>True if monitoring started successfully, false otherwise</returns>
        public static bool StartAnalogMonitoring(this HexapodController controller, int[] channelIds, int updateInterval, EventHandler<AnalogChannelUpdateEventArgs> handler)
        {
            if (controller == null)
                throw new ArgumentNullException(nameof(controller));

            if (!controller.IsConnected || channelIds == null || channelIds.Length == 0 || handler == null)
                return false;

            // Get or create the analog monitor for this controller
            var monitor = GetOrCreateAnalogMonitor(controller);
            if (monitor == null)
                return false;

            // Start monitoring
            return monitor.StartMonitoring(channelIds, updateInterval, handler);
        }

        /// <summary>
        /// Stops continuous monitoring of analog channels
        /// </summary>
        /// <param name="controller">The hexapod controller</param>
        /// <returns>True if monitoring was stopped successfully, false otherwise</returns>
        public static bool StopAnalogMonitoring(this HexapodController controller)
        {
            if (controller == null)
                throw new ArgumentNullException(nameof(controller));

            var monitor = GetOrCreateAnalogMonitor(controller);
            if (monitor == null)
                return false;

            monitor.StopMonitoring();
            return true;
        }

        /// <summary>
        /// Gets or creates an analog monitor for the specified controller
        /// </summary>
        /// <param name="controller">The hexapod controller</param>
        /// <returns>The analog monitor</returns>
        private static HexapodAnalogMonitor GetOrCreateAnalogMonitor(HexapodController controller)
        {
            return HexapodAnalogMonitor.GetOrCreateMonitor(controller);
        }

        /// <summary>
        /// Helper to get the controller ID
        /// </summary>
        private static int GetControllerID(HexapodController controller)
        {
            // From looking at the HexapodController source code, we know it has a field named _controllerId
            var field = controller.GetType().GetField("_controllerId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (field != null)
            {
                return (int)field.GetValue(controller);
            }

            throw new InvalidOperationException("Could not access controller ID field. Please update implementation.");
        }
    }

    /// <summary>
    /// Class that handles continuous monitoring of analog input channels
    /// </summary>
    public class HexapodAnalogMonitor
    {
        private readonly HexapodController _controller;
        private readonly ILogger _logger;
        private CancellationTokenSource _cancellationSource;
        private Task _monitoringTask;
        private int[] _channelIds;
        private int _updateInterval;
        private EventHandler<AnalogChannelUpdateEventArgs> _updateHandler;
        private static readonly Dictionary<HexapodController, HexapodAnalogMonitor> _monitors =
            new Dictionary<HexapodController, HexapodAnalogMonitor>();

        /// <summary>
        /// Creates a new instance of the HexapodAnalogMonitor
        /// </summary>
        /// <param name="controller">The hexapod controller to monitor</param>
        public HexapodAnalogMonitor(HexapodController controller)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _logger = Log.ForContext<HexapodAnalogMonitor>();
        }

        /// <summary>
        /// Gets or creates a monitor for the specified controller
        /// </summary>
        internal static HexapodAnalogMonitor GetOrCreateMonitor(HexapodController controller)
        {
            if (controller == null)
                throw new ArgumentNullException(nameof(controller));

            lock (_monitors)
            {
                if (!_monitors.TryGetValue(controller, out var monitor))
                {
                    monitor = new HexapodAnalogMonitor(controller);
                    _monitors[controller] = monitor;
                }
                return monitor;
            }
        }

        /// <summary>
        /// Starts monitoring the specified channels
        /// </summary>
        /// <param name="channelIds">Array of channel IDs to monitor</param>
        /// <param name="updateInterval">Update interval in milliseconds</param>
        /// <param name="handler">Event handler to receive updates</param>
        /// <returns>True if monitoring started successfully, false otherwise</returns>
        public bool StartMonitoring(int[] channelIds, int updateInterval, EventHandler<AnalogChannelUpdateEventArgs> handler)
        {
            if (channelIds == null || channelIds.Length == 0)
                throw new ArgumentNullException(nameof(channelIds));

            if (updateInterval <= 0)
                throw new ArgumentOutOfRangeException(nameof(updateInterval), "Update interval must be greater than zero.");

            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            // Stop existing monitoring if any
            StopMonitoring();

            _channelIds = channelIds;
            _updateInterval = updateInterval;
            _updateHandler = handler;
            _cancellationSource = new CancellationTokenSource();

            try
            {
                // Start monitoring task
                _monitoringTask = Task.Run(() => MonitoringLoop(_cancellationSource.Token), _cancellationSource.Token);
                _logger.Information("Started analog channel monitoring for channels: {Channels} with interval {Interval}ms",
                    string.Join(", ", _channelIds), _updateInterval);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error starting analog channel monitoring");
                _cancellationSource?.Dispose();
                _cancellationSource = null;
                return false;
            }
        }

        /// <summary>
        /// Stops the monitoring
        /// </summary>
        public void StopMonitoring()
        {
            if (_cancellationSource != null)
            {
                _logger.Information("Stopping analog channel monitoring");
                _cancellationSource.Cancel();
                try
                {
                    _monitoringTask?.Wait(1000);  // Wait up to 1 second for graceful shutdown
                }
                catch (AggregateException)
                {
                    // Task was canceled, this is expected
                }
                _cancellationSource.Dispose();
                _cancellationSource = null;
                _monitoringTask = null;
            }
        }

        /// <summary>
        /// Main monitoring loop
        /// </summary>
        private async void MonitoringLoop(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        // Read analog values
                        var values = await _controller.GetAnalogVoltagesAsync(_channelIds);

                        if (values != null && _updateHandler != null)
                        {
                            // Raise the event with the new values
                            _updateHandler?.Invoke(this, new AnalogChannelUpdateEventArgs(values));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error reading analog channels");
                    }

                    // Wait for the next interval
                    await Task.Delay(_updateInterval, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Monitoring was canceled, this is expected
                _logger.Information("Analog monitoring canceled");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in analog monitoring loop");
            }
            finally
            {
                _logger.Information("Analog monitoring stopped");
            }
        }
    }

    /// <summary>
    /// Event arguments for analog channel updates
    /// </summary>
    public class AnalogChannelUpdateEventArgs : EventArgs
    {
        /// <summary>
        /// Dictionary mapping channel IDs to voltage values
        /// </summary>
        public Dictionary<int, double> ChannelValues { get; }

        /// <summary>
        /// Creates a new instance of the AnalogChannelUpdateEventArgs
        /// </summary>
        /// <param name="values">Dictionary of channel values</param>
        public AnalogChannelUpdateEventArgs(Dictionary<int, double> values)
        {
            ChannelValues = values ?? throw new ArgumentNullException(nameof(values));
        }
    }
}