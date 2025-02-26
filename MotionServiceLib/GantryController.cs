using ACS.SPiiPlusNET;
using MotionServiceLib;
using Serilog;
using Serilog.Core;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MotionServiceLib
{
    /// <summary>
    /// Controller for Gantry devices that implements the unified IMotionController interface
    /// </summary>
    public class GantryController : IMotionController
    {
        private readonly MotionDevice _device;
        private AcsLib _acs;
        private bool _disposed;

        // Constants for axis indices
        private const int AXIS_X = 0;
        private const int AXIS_Y = 1;
        private const int AXIS_Z = 2;

        // Event to track position updates
        public event Action<double[]> PositionUpdated;

        /// <summary>
        /// Gets whether the controller is connected to the physical device
        /// </summary>
        public bool IsConnected { get; private set; }
        private readonly ILogger _logger;
        /// <summary>
        /// Creates a new instance of the GantryController
        /// </summary>
        /// <param name="device">The device configuration</param>
        /// <param name="logger">The logger instance</param>
        public GantryController(MotionDevice device)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _logger = Log.ForContext<GantryController>();
        }

        /// <summary>
        /// Initializes the connection to the gantry device
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                _logger.Information("Initializing Gantry {DeviceName} connection to {IpAddress}",
                    _device.Name, _device.IpAddress);

                // Create the ACSController instance
                _acs = new AcsLib(_device.Name);

                // Subscribe to controller events
                SubscribeToControllerEvents();

                // Connect to the device
                await Task.Run(() => _acs.Connect("Ethernet", _device.IpAddress));

                // Check if connection was successful
                IsConnected = _acs.bConnected;

                if (IsConnected)
                {
                    _logger.Information("Successfully connected to Gantry {DeviceName}", _device.Name);

                    // Initialize axes
                    await InitializeAxesAsync();

                    // Log initial position
                    LogInitialStatus();

                    // Start position monitoring
                    StartPositionMonitoring();
                }
                else
                {
                    _logger.Error("Failed to connect to Gantry {DeviceName} at {IpAddress}",
                        _device.Name, _device.IpAddress);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error initializing Gantry {DeviceName}", _device.Name);
                IsConnected = false;
                throw;
            }
        }

        /// <summary>
        /// Moves the gantry to the specified absolute position
        /// </summary>
        /// <param name="position">The target position</param>
        public async Task MoveToPositionAsync(Position position)
        {
            ValidateConnection();

            try
            {
                _logger.Information("Moving Gantry {DeviceName} to position: X={X}, Y={Y}, Z={Z}",
                    _device.Name, position.X, position.Y, position.Z);

                // Move each axis separately
                await MoveAxisToPositionAsync(AXIS_X, position.X);
                await MoveAxisToPositionAsync(AXIS_Y, position.Y);
                await MoveAxisToPositionAsync(AXIS_Z, position.Z);

                // Wait for all axes to finish moving
                await _acs.WaitForAllAxesIdleAsync();

                _logger.Information("Gantry {DeviceName} successfully moved to target position", _device.Name);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error moving Gantry {DeviceName} to position", _device.Name);
                throw;
            }
        }

        /// <summary>
        /// Moves the gantry by the specified relative amounts
        /// </summary>
        /// <param name="relativeMove">The relative movement values [X,Y,Z]</param>
        public async Task MoveRelativeAsync(double[] relativeMove)
        {
            ValidateConnection();

            try
            {
                _logger.Information("Moving Gantry {DeviceName} relative: X={X}, Y={Y}, Z={Z}",
                    _device.Name, relativeMove[0], relativeMove[1], relativeMove[2]);

                // Move each axis relatively
                await MoveAxisRelativeAsync(AXIS_X, relativeMove[0]);
                await MoveAxisRelativeAsync(AXIS_Y, relativeMove[1]);
                await MoveAxisRelativeAsync(AXIS_Z, relativeMove[2]);

                // Wait for all axes to finish moving
                await _acs.WaitForAllAxesIdleAsync();

                _logger.Information("Gantry {DeviceName} successfully moved relative", _device.Name);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error performing relative move for Gantry {DeviceName}", _device.Name);
                throw;
            }
        }

        /// <summary>
        /// Stops all movement of the gantry
        /// </summary>
        public async Task StopAsync()
        {
            ValidateConnection();

            try
            {
                _logger.Information("Stopping Gantry {DeviceName}", _device.Name);

                // Stop all motors
                await Task.Run(() => _acs.StopAllMotors());

                _logger.Information("Gantry {DeviceName} successfully stopped", _device.Name);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error stopping Gantry {DeviceName}", _device.Name);
                throw;
            }
        }

        /// <summary>
        /// Gets the current position of the gantry
        /// </summary>
        /// <returns>The current position</returns>
        public async Task<Position> GetCurrentPositionAsync()
        {
            ValidateConnection();

            try
            {
                // Get the current position of all axes
                double[] positions = await Task.Run(() => _acs.GetCurrentACSPosition());

                // Create a new Position object
                return new Position
                {
                    X = positions[AXIS_X],
                    Y = positions[AXIS_Y],
                    Z = positions[AXIS_Z],
                    // Gantry doesn't use U, V, W - set to 0
                    U = 0,
                    V = 0,
                    W = 0
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting current position for Gantry {DeviceName}", _device.Name);
                throw;
            }
        }

        /// <summary>
        /// Moves the gantry to its home position
        /// </summary>
        public async Task HomeAsync()
        {
            ValidateConnection();

            try
            {
                _logger.Information("Homing Gantry {DeviceName}", _device.Name);

                // Check if "Home" position is defined in the device's positions
                if (_device.Positions.TryGetValue("Home", out var homePosition))
                {
                    // Move to the predefined Home position
                    await MoveToPositionAsync(homePosition);
                    _logger.Information("Gantry {DeviceName} successfully homed to predefined position", _device.Name);
                }
                else
                {
                    // Create a default home position
                    var defaultHome = new Position { X = 3.0, Y = 3.0, Z = 12.0 }; // Default based on your config
                    await MoveToPositionAsync(defaultHome);
                    _logger.Information("Gantry {DeviceName} successfully homed to default position", _device.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error homing Gantry {DeviceName}", _device.Name);
                throw;
            }
        }

        /// <summary>
        /// Run a buffer program on the ACS controller
        /// </summary>
        /// <param name="bufferNumber">The buffer number to run</param>
        /// <param name="labelName">Optional label name to start from</param>
        public async Task RunBufferAsync(int bufferNumber, string labelName = null)
        {
            ValidateConnection();

            try
            {
                _logger.Information("Running buffer {BufferNumber} {LabelDetails}",
                    bufferNumber,
                    labelName != null ? $"from label {labelName}" : "from start");

                if (!string.IsNullOrEmpty(labelName))
                {
                    // Convert label to uppercase for validation
                    string upperLabel = labelName.Trim().ToUpper();

                    // Validate label name (must start with underscore or A-Z)
                    if (upperLabel[0] != '_' && (upperLabel[0] < 'A' || upperLabel[0] > 'Z'))
                    {
                        var error = "Invalid label name. Label must start with underscore or letter A-Z";
                        _logger.Error(error);
                        throw new ArgumentException(error);
                    }

                    // Run buffer from specified label
                    await Task.Run(() => _acs.Ch.RunBuffer(
                        (ACS.SPiiPlusNET.ProgramBuffer)bufferNumber,
                        upperLabel));
                }
                else
                {
                    // Run buffer from beginning
                    await Task.Run(() => _acs.Ch.RunBuffer(
                        (ACS.SPiiPlusNET.ProgramBuffer)bufferNumber,
                        null));
                }

                _logger.Information("Successfully started buffer {BufferNumber}", bufferNumber);
            }
            catch (COMException ex)
            {
                _logger.Error(ex, "COM error running buffer {BufferNumber}", bufferNumber);
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error running buffer {BufferNumber}", bufferNumber);
                throw;
            }
        }

        /// <summary>
        /// Stop a running buffer program
        /// </summary>
        /// <param name="bufferNumber">The buffer number to stop</param>
        public async Task StopBufferAsync(int bufferNumber)
        {
            ValidateConnection();

            try
            {
                _logger.Information("Stopping buffer {BufferNumber}", bufferNumber);

                await Task.Run(() => _acs.Ch.StopBuffer(
                    (ACS.SPiiPlusNET.ProgramBuffer)bufferNumber));

                _logger.Information("Successfully stopped buffer {BufferNumber}", bufferNumber);
            }
            catch (COMException ex)
            {
                _logger.Error(ex, "COM error stopping buffer {BufferNumber}", bufferNumber);
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error stopping buffer {BufferNumber}", bufferNumber);
                throw;
            }
        }

        /// <summary>
        /// Disposes of resources
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Protected implementation of Dispose pattern
        /// </summary>
        /// <param name="disposing">Whether to dispose managed resources</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                try
                {
                    if (_acs != null)
                    {
                        // Stop all motors
                        _acs.StopAllMotors();

                        // Disconnect from the device
                        _acs.Disconnect();

                        // ACSController itself doesn't implement IDisposable,
                        // but we can set it to null to allow GC to clean it up
                        _acs = null;
                    }

                    _logger.Information("Gantry {DeviceName} resources disposed", _device.Name);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error disposing Gantry {DeviceName} resources", _device.Name);
                }
            }

            _disposed = true;
        }

        #region Helper Methods

        /// <summary>
        /// Subscribe to the ACSController events
        /// </summary>
        private void SubscribeToControllerEvents()
        {
            if (_acs != null)
            {
                _acs.ConnectionStatusChanged += OnConnectionStatusChanged;
                _acs.ErrorOccurred += OnErrorOccurred;
                _acs.MotorStateChanged += OnMotorStateChanged;
                _acs.MotorEnabled += OnMotorEnabled;
                _acs.MotorDisabled += OnMotorDisabled;
                _acs.MotorStopped += OnMotorStopped;
                _acs.AllAxesIdle += OnAllAxesIdle;
            }
        }

        /// <summary>
        /// Initialize axes of the gantry (enable motors if needed)
        /// </summary>
        private async Task InitializeAxesAsync()
        {
            try
            {
                // Check motor status
                var (xPos, xEnabled, _) = _acs.GetAxisStatus(AXIS_X);
                var (yPos, yEnabled, _) = _acs.GetAxisStatus(AXIS_Y);
                var (zPos, zEnabled, _) = _acs.GetAxisStatus(AXIS_Z);

                // Enable motors if they're not already enabled
                if (!xEnabled)
                {
                    _acs.SetCurrentAxis(AXIS_X);
                    await Task.Run(() => _acs.EnableMotor());
                }

                if (!yEnabled)
                {
                    _acs.SetCurrentAxis(AXIS_Y);
                    await Task.Run(() => _acs.EnableMotor());
                }

                if (!zEnabled)
                {
                    _acs.SetCurrentAxis(AXIS_Z);
                    await Task.Run(() => _acs.EnableMotor());
                }

                _logger.Information("Gantry {DeviceName} axes initialized", _device.Name);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error initializing axes for Gantry {DeviceName}", _device.Name);
                throw;
            }
        }

        /// <summary>
        /// Moves a specific axis to an absolute position
        /// </summary>
        /// <param name="axis">The axis index (0=X, 1=Y, 2=Z)</param>
        /// <param name="position">The target position</param>
        private async Task MoveAxisToPositionAsync(int axis, double position)
        {
            try
            {
                _logger.Debug("Moving Gantry {DeviceName} axis {Axis} to position {Position}",
                    _device.Name, axis, position);

                _acs.SetCurrentAxis(axis);
                await Task.Run(() => _acs.MoveMotorToAbsolute(position));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error moving axis {Axis} for Gantry {DeviceName}", axis, _device.Name);
                throw;
            }
        }

        /// <summary>
        /// Moves a specific axis by a relative amount
        /// </summary>
        /// <param name="axis">The axis index (0=X, 1=Y, 2=Z)</param>
        /// <param name="increment">The relative increment</param>
        private async Task MoveAxisRelativeAsync(int axis, double increment)
        {
            try
            {
                _logger.Debug("Moving Gantry {DeviceName} axis {Axis} by increment {Increment}",
                    _device.Name, axis, increment);

                _acs.SetCurrentAxis(axis);
                await Task.Run(() => _acs.MoveMotor(increment));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error moving axis {Axis} relatively for Gantry {DeviceName}",
                    axis, _device.Name);
                throw;
            }
        }

        /// <summary>
        /// Starts periodically monitoring positions from the controller
        /// </summary>
        private void StartPositionMonitoring()
        {
            try
            {
                Task.Run(async () =>
                {
                    while (!_disposed && IsConnected)
                    {
                        try
                        {
                            // Get current positions
                            double[] positions = _acs.GetCurrentACSPosition();

                            // Fire the event with the positions
                            PositionUpdated?.Invoke(positions);

                            // Wait before next update
                            await Task.Delay(100);  // Update every 100ms
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Error in position monitoring for Gantry {DeviceName}", _device.Name);
                            await Task.Delay(1000);  // Wait longer on error
                        }
                    }
                });

                _logger.Debug("Started position monitoring for Gantry {DeviceName}", _device.Name);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error starting position monitoring for Gantry {DeviceName}", _device.Name);
            }
        }

        /// <summary>
        /// Logs initial status information from the gantry
        /// </summary>
        private void LogInitialStatus()
        {
            try
            {
                // Get current positions
                var (xPos, xEnabled, xMoving) = _acs.GetAxisStatus(AXIS_X);
                var (yPos, yEnabled, yMoving) = _acs.GetAxisStatus(AXIS_Y);
                var (zPos, zEnabled, zMoving) = _acs.GetAxisStatus(AXIS_Z);

                _logger.Information("Gantry {DeviceName} initial status: " +
                    "X={X} (Enabled={XEnabled}, Moving={XMoving}), " +
                    "Y={Y} (Enabled={YEnabled}, Moving={YMoving}), " +
                    "Z={Z} (Enabled={ZEnabled}, Moving={ZMoving})",
                    _device.Name, xPos, xEnabled, xMoving, yPos, yEnabled, yMoving, zPos, zEnabled, zMoving);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error logging initial status for Gantry {DeviceName}", _device.Name);
                // Continue even if logging fails - non-critical
            }
        }

        /// <summary>
        /// Validates that the controller is connected
        /// </summary>
        private void ValidateConnection()
        {
            if (!IsConnected || _acs == null || !_acs.bConnected)
            {
                throw new InvalidOperationException($"Gantry {_device.Name} is not connected");
            }
        }

        #endregion

        #region Event Handlers

        private void OnConnectionStatusChanged(bool connected)
        {
            IsConnected = connected;
            _logger.Information("Gantry {DeviceName} connection status changed to {Status}",
                _device.Name, connected);
        }

        private void OnErrorOccurred(string error)
        {
            _logger.Error("Gantry {DeviceName} error: {ErrorMessage}", _device.Name, error);
        }

        private void OnMotorStateChanged(int axis, double position, bool isEnabled, bool isMoving)
        {
            // This event occurs frequently - log at debug level
            _logger.Debug("Gantry {DeviceName} motor state changed - Axis: {Axis}, Position: {Position}, Enabled: {Enabled}, Moving: {Moving}",
                _device.Name, axis, position, isEnabled, isMoving);
        }

        private void OnMotorEnabled()
        {
            _logger.Information("Gantry {DeviceName} motor enabled", _device.Name);
        }

        private void OnMotorDisabled()
        {
            _logger.Information("Gantry {DeviceName} motor disabled", _device.Name);
        }

        private void OnMotorStopped()
        {
            _logger.Information("Gantry {DeviceName} motor stopped", _device.Name);
        }

        private void OnAllAxesIdle()
        {
            _logger.Information("Gantry {DeviceName} all axes are now idle", _device.Name);
        }

        #endregion
    }
}