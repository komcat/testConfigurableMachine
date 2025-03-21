using Serilog;
using System;
using System.Text;
using System.Threading.Tasks;
using PI;
using MotionServiceLib;
using System.Windows;

namespace MotionServiceLib
{
    /// <summary>
    /// Controller for Hexapod devices that implements the unified IMotionController interface,
    /// directly interfacing with the PI GCS2 DLL for controlling hexapod motion platforms
    /// </summary>
    public class HexapodController : IMotionController
    {
        private readonly MotionDevice _device;

        private int _controllerId = -1;
        public int ControllerId => _controllerId;
        public string ControllerName => _device.Name;

        private bool _disposed;
        private System.Timers.Timer _positionUpdateTimer;

        // Define the axis identifier string for the hexapod
        private const string AXIS_IDENTIFIER = "X Y Z U V W";
        private const string PIVOT_AXIS_IDENTIFIER = "X Y Z";
        private const int PI_RESULT_SUCCESS = 1;
        private const int PI_RESULT_FAILURE = 0;

        /// <summary>
        /// Event fired when the position is updated
        /// </summary>
        public event Action<double[]> PositionUpdated;

        /// <summary>
        /// Gets whether the controller is connected to the physical device
        /// </summary>
        public bool IsConnected { get; private set; }
        private readonly ILogger _logger;
        /// <summary>
        /// Creates a new instance of the HexapodController
        /// </summary>
        /// <param name="device">The device configuration</param>
        /// <param name="logger">The logger instance</param>
        public HexapodController(MotionDevice device)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            // Get a contextualized logger
            _logger = Log.ForContext<HexapodController>();

        }
        
        /// <summary>
        /// Initializes the connection to the hexapod device
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                _logger.Information("Initializing Hexapod {DeviceName} connection to {IpAddress}:{Port}",
                    _device.Name, _device.IpAddress, _device.Port);

                // Connect to the device using the PI library
                // This is a synchronous call, wrap in Task.Run to make it asynchronous
                await Task.Run(() =>
                {
                    _controllerId = GCS2.ConnectTCPIP(_device.IpAddress, _device.Port);

                    // Check if connection was successful
                    IsConnected = _controllerId >= 0;
                });

                if (IsConnected)
                {
                    _logger.Information("Successfully connected to Hexapod {DeviceName} with ID {ControllerId}",
                        _device.Name, _controllerId);

                    // Log device identification
                    await LogDeviceIdentification();

                    // Start position updates
                    StartPositionUpdates();

                    // Log initial position and settings
                    await LogInitialStatus();
                }
                else
                {
                    _logger.Error("Failed to connect to Hexapod {DeviceName} at {IpAddress}:{Port}",
                        _device.Name, _device.IpAddress, _device.Port);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error initializing Hexapod {DeviceName}", _device.Name);
                IsConnected = false;
                throw;
            }
        }

        /// <summary>
        /// Moves the hexapod to the specified absolute position
        /// </summary>
        /// <param name="position">The target position</param>
        public async Task MoveToPositionAsync(Position position)
        {
            ValidateConnection();

            try
            {
                _logger.Information("Moving Hexapod {DeviceName} to position: X={X}, Y={Y}, Z={Z}, U={U}, V={V}, W={W}",
                    _device.Name, position.X, position.Y, position.Z, position.U, position.V, position.W);

                // Convert the position to an array
                double[] targetPos = new double[6] { position.X, position.Y, position.Z, position.U, position.V, position.W };

                // Perform the move operation
                await Task.Run(() =>
                {
                    int result = GCS2.MOV(_controllerId, AXIS_IDENTIFIER, targetPos);
                    if (result == PI_RESULT_FAILURE)
                    {
                        throw new InvalidOperationException($"Failed to move Hexapod {_device.Name} to target position");
                    }
                });

                // Wait for the move to complete
                await WaitForMotionDoneAsync();

                _logger.Information("Hexapod {DeviceName} successfully moved to target position", _device.Name);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error moving Hexapod {DeviceName} to position", _device.Name);
                throw;
            }
        }

        /// <summary>
        /// Moves the hexapod by the specified relative amounts
        /// </summary>
        /// <param name="relativeMove">The relative movement values [X,Y,Z,U,V,W]</param>
        public async Task MoveRelativeAsync(double[] relativeMove)
        {
            ValidateConnection();

            try
            {
                _logger.Information("Moving Hexapod {DeviceName} relative: X={X}, Y={Y}, Z={Z}, U={U}, V={V}, W={W}",
                    _device.Name, relativeMove[0], relativeMove[1], relativeMove[2],
                    relativeMove[3], relativeMove[4], relativeMove[5]);

                // Perform the relative move operation
                await Task.Run(() =>
                {
                    int result = GCS2.MVR(_controllerId, AXIS_IDENTIFIER, relativeMove);
                    if (result == PI_RESULT_FAILURE)
                    {
                        throw new InvalidOperationException($"Failed to move Hexapod {_device.Name} relatively");
                    }
                });

                // Wait for the move to complete
                await WaitForMotionDoneAsync();

                _logger.Information("Hexapod {DeviceName} successfully moved relative", _device.Name);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error performing relative move for Hexapod {DeviceName}", _device.Name);
                throw;
            }
        }

        /// <summary>
        /// Stops all movement of the hexapod
        /// </summary>
        public async Task StopAsync()
        {
            ValidateConnection();

            try
            {
                _logger.Information("Stopping Hexapod {DeviceName}", _device.Name);

                // Perform the stop operation
                await Task.Run(() =>
                {
                    // Use the STP command to stop all axes
                    int result = GCS2.STP(_controllerId);
                    if (result == PI_RESULT_FAILURE)
                    {
                        throw new InvalidOperationException($"Failed to stop Hexapod {_device.Name}");
                    }
                });

                _logger.Information("Hexapod {DeviceName} successfully stopped", _device.Name);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error stopping Hexapod {DeviceName}", _device.Name);
                throw;
            }
        }

        /// <summary>
        /// Gets the current position of the hexapod
        /// </summary>
        /// <returns>The current position</returns>
        public async Task<Position> GetCurrentPositionAsync()
        {
            ValidateConnection();

            try
            {
                // Get the current position
                double[] positions = new double[6];

                await Task.Run(() =>
                {
                    int result = GCS2.qPOS(_controllerId, AXIS_IDENTIFIER, positions);
                    if (result == PI_RESULT_FAILURE)
                    {
                        throw new InvalidOperationException($"Failed to get position of Hexapod {_device.Name}");
                    }
                });

                // Create a position object
                return new Position
                {
                    X = positions[0],
                    Y = positions[1],
                    Z = positions[2],
                    U = positions[3],
                    V = positions[4],
                    W = positions[5]
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting current position for Hexapod {DeviceName}", _device.Name);
                throw;
            }
        }

        /// <summary>
        /// Moves the hexapod to its home position
        /// </summary>
        public async Task HomeAsync()
        {
            ValidateConnection();

            try
            {
                _logger.Information("Homing Hexapod {DeviceName}", _device.Name);

                // Check if "Home" position is defined in the device's positions
                if (_device.Positions.TryGetValue("Home", out var homePosition))
                {
                    // Move to the predefined Home position
                    await MoveToPositionAsync(homePosition);
                    _logger.Information("Hexapod {DeviceName} successfully homed to predefined position", _device.Name);
                }
                else
                {
                    // Create a default home position (all zeros except for X which is set to 4.64)
                    // This is based on the typical home position seen in your config files
                    var defaultHome = new Position { X = 4.64, Y = 0, Z = 0, U = 0, V = 0, W = 0 };
                    await MoveToPositionAsync(defaultHome);
                    _logger.Information("Hexapod {DeviceName} successfully homed to default position", _device.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error homing Hexapod {DeviceName}", _device.Name);
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
                    // Stop position updates
                    StopPositionUpdates();

                    // Close the connection to the device
                    if (_controllerId >= 0 && IsConnected)
                    {
                        GCS2.CloseConnection(_controllerId);
                        _logger.Information("Closed connection to Hexapod {DeviceName}", _device.Name);
                    }

                    // Dispose the timer
                    if (_positionUpdateTimer != null)
                    {
                        _positionUpdateTimer.Dispose();
                        _positionUpdateTimer = null;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error during Hexapod {DeviceName} disposal", _device.Name);
                }
            }

            _disposed = true;
        }

        #region Helper Methods

        /// <summary>
        /// Starts regular position updates
        /// </summary>
        private void StartPositionUpdates()
        {
            // Create and configure the timer
            _positionUpdateTimer = new System.Timers.Timer(100); // Update every 100 ms
            _positionUpdateTimer.Elapsed += OnPositionUpdateTimerElapsed;
            _positionUpdateTimer.AutoReset = true;
            _positionUpdateTimer.Start();

            _logger.Debug("Started position updates for Hexapod {DeviceName}", _device.Name);
        }

        /// <summary>
        /// Stops position updates
        /// </summary>
        private void StopPositionUpdates()
        {
            if (_positionUpdateTimer != null)
            {
                _positionUpdateTimer.Stop();
                _positionUpdateTimer.Elapsed -= OnPositionUpdateTimerElapsed;
                _logger.Debug("Stopped position updates for Hexapod {DeviceName}", _device.Name);
            }
        }

        /// <summary>
        /// Handler for the position update timer
        /// </summary>
        private void OnPositionUpdateTimerElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                if (IsConnected && _controllerId >= 0)
                {
                    double[] positions = new double[6];

                    int result = GCS2.qPOS(_controllerId, AXIS_IDENTIFIER, positions);
                    if (result == PI_RESULT_SUCCESS)
                    {
                        PositionUpdated?.Invoke(positions);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in position update timer for Hexapod {DeviceName}", _device.Name);
            }
        }

        /// <summary>
        /// Logs device identification information
        /// </summary>
        private async Task LogDeviceIdentification()
        {
            try
            {
                StringBuilder idnBuffer = new StringBuilder(256);

                await Task.Run(() =>
                {
                    int result = GCS2.qIDN(_controllerId, idnBuffer, idnBuffer.Capacity);
                    if (result == PI_RESULT_FAILURE)
                    {
                        throw new InvalidOperationException($"Failed to get identification for Hexapod {_device.Name}");
                    }
                });

                _logger.Information("Hexapod {DeviceName} identification: {Identification}",
                    _device.Name, idnBuffer.ToString());
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error getting identification for Hexapod {DeviceName}", _device.Name);
                // Continue even if this fails
            }
        }

        /// <summary>
        /// Logs the initial status of the device
        /// </summary>
        private async Task LogInitialStatus()
        {
            try
            {
                // Get position
                var position = await GetCurrentPositionAsync();
                _logger.Information("Hexapod {DeviceName} initial position: X={X}, Y={Y}, Z={Z}, U={U}, V={V}, W={W}",
                    _device.Name, position.X, position.Y, position.Z, position.U, position.V, position.W);

                // Get position limits
                await LogPositionLimits();

                // Get pivot point
                await LogPivotPoint();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error logging initial status for Hexapod {DeviceName}", _device.Name);
                // Continue even if this fails
            }
        }

        /// <summary>
        /// Logs the position limits of the device
        /// </summary>
        private async Task LogPositionLimits()
        {
            try
            {
                double[] minLimits = new double[6];
                double[] maxLimits = new double[6];

                await Task.Run(() =>
                {
                    int minResult = GCS2.qTMN(_controllerId, AXIS_IDENTIFIER, minLimits);
                    int maxResult = GCS2.qTMX(_controllerId, AXIS_IDENTIFIER, maxLimits);

                    if (minResult == PI_RESULT_FAILURE || maxResult == PI_RESULT_FAILURE)
                    {
                        throw new InvalidOperationException($"Failed to get position limits for Hexapod {_device.Name}");
                    }
                });

                _logger.Information("Hexapod {DeviceName} position limits - Min: [{MinX},{MinY},{MinZ},{MinU},{MinV},{MinW}], Max: [{MaxX},{MaxY},{MaxZ},{MaxU},{MaxV},{MaxW}]",
                    _device.Name,
                    minLimits[0], minLimits[1], minLimits[2], minLimits[3], minLimits[4], minLimits[5],
                    maxLimits[0], maxLimits[1], maxLimits[2], maxLimits[3], maxLimits[4], maxLimits[5]);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error getting position limits for Hexapod {DeviceName}", _device.Name);
                // Continue even if this fails
            }
        }

        /// <summary>
        /// Logs the pivot point of the device
        /// </summary>
        private async Task LogPivotPoint()
        {
            try
            {
                double[] pivotPoint = new double[3];

                await Task.Run(() =>
                {
                    int result = GCS2.qSPI(_controllerId, PIVOT_AXIS_IDENTIFIER, pivotPoint);
                    if (result == PI_RESULT_FAILURE)
                    {
                        throw new InvalidOperationException($"Failed to get pivot point for Hexapod {_device.Name}");
                    }
                });

                _logger.Information("Hexapod {DeviceName} pivot point: X={X}, Y={Y}, Z={Z}",
                    _device.Name, pivotPoint[0], pivotPoint[1], pivotPoint[2]);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error getting pivot point for Hexapod {DeviceName}", _device.Name);
                // Continue even if this fails
            }
        }

        /// <summary>
        /// Waits for motion to complete on all axes
        /// </summary>
        private async Task WaitForMotionDoneAsync()
        {
            int[] isMoving = new int[6] { 1, 1, 1, 1, 1, 1 }; // Start with assumption that all axes are moving

            _logger.Debug("Waiting for Hexapod {DeviceName} movement to complete", _device.Name);

            await Task.Run(() =>
            {
                // Keep checking until all axes have stopped moving
                while (Array.Exists(isMoving, val => val != 0))
                {
                    int result = GCS2.IsMoving(_controllerId, AXIS_IDENTIFIER, isMoving);
                    if (result == PI_RESULT_FAILURE)
                    {
                        throw new InvalidOperationException($"Failed to check motion status for Hexapod {_device.Name}");
                    }

                    // Brief delay before checking again
                    System.Threading.Thread.Sleep(50);
                }
            });

            _logger.Debug("Hexapod {DeviceName} movement completed", _device.Name);
        }

        /// <summary>
        /// Validates that the controller is connected
        /// </summary>
        private void ValidateConnection()
        {
            if (!IsConnected || _controllerId < 0)
            {
                throw new InvalidOperationException($"Hexapod {_device.Name} is not connected");
            }
        }

        /// <summary>
        /// Sets the pivot point for the hexapod
        /// </summary>
        /// <param name="x">X coordinate of pivot point</param>
        /// <param name="y">Y coordinate of pivot point</param>
        /// <param name="z">Z coordinate of pivot point</param>
        public async Task SetPivotPointAsync(double x, double y, double z)
        {
            ValidateConnection();

            try
            {
                _logger.Information("Setting pivot point for Hexapod {DeviceName} to X={X}, Y={Y}, Z={Z}",
                    _device.Name, x, y, z);

                double[] pivotPoint = new double[] { x, y, z };

                await Task.Run(() =>
                {
                    int result = GCS2.SPI(_controllerId, PIVOT_AXIS_IDENTIFIER, pivotPoint);
                    if (result == PI_RESULT_FAILURE)
                    {
                        _logger.Error($"Failed to set pivot point for Hexapod {_device.Name}");
                    }
                });

                _logger.Information("Successfully set pivot point for Hexapod {DeviceName}", _device.Name);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error setting pivot point for Hexapod {DeviceName}", _device.Name);
                throw;
            }
        }

        /// <summary>
        /// Gets the current pivot point (rotation center) of the hexapod
        /// </summary>
        /// <returns>The current pivot point coordinates as a Position object, or null if retrieval fails</returns>
        public async Task<Position> GetPivotPoint()
        {
            ValidateConnection();

            try
            {
                _logger.Information("Getting pivot point for Hexapod {DeviceName}", _device.Name);

                // Initialize buffer for pivot point values
                double[] pivotValues = new double[3]; // X, Y, Z coordinates

                bool success = await Task.Run(() =>
                {
                    // Use qSPI to query the pivot point (rotation center)
                    int result = GCS2.qSPI(_controllerId, "ROT", pivotValues);
                    return result > 0;
                });

                if (success)
                {
                    _logger.Information("Hexapod {DeviceName} pivot point: X={X}, Y={Y}, Z={Z}",
                        _device.Name, pivotValues[0], pivotValues[1], pivotValues[2]);

                    // Return as a Position object to be consistent with other methods
                    return new Position
                    {
                        X = pivotValues[0],
                        Y = pivotValues[1],
                        Z = pivotValues[2],
                        U = 0, // Pivot point doesn't include rotation values
                        V = 0,
                        W = 0
                    };
                }
                else
                {
                    _logger.Error("Failed to get pivot coordinates for Hexapod {DeviceName}", _device.Name);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting pivot point for Hexapod {DeviceName}", _device.Name);
                throw;
            }
        }


        // Add this to HexapodController.cs
        public Task SetSpeedAsync(double speed)
        {
            return SetSystemVelocityAsync(speed);
        }// Add this to HexapodController.cs
        public Task<double> GetSpeedAsync()
        {
            return GetSystemVelocityAsync();
        }
        /// <summary>
        /// Gets the system velocity
        /// </summary>
        public async Task<double> GetSystemVelocityAsync()
        {
            ValidateConnection();

            try
            {
                double velocity = 0;

                await Task.Run(() =>
                {
                    int result = GCS2.qVLS(_controllerId, ref velocity);
                    if (result == PI_RESULT_FAILURE)
                    {
                        throw new InvalidOperationException($"Failed to get system velocity for Hexapod {_device.Name}");
                    }
                });

                //_logger.Debug("Hexapod {DeviceName} system velocity: {Velocity}", _device.Name, velocity);
                return velocity;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting system velocity for Hexapod {DeviceName}", _device.Name);
                throw;
            }
        }

        /// <summary>
        /// Sets the system velocity
        /// </summary>
        /// <param name="velocity">The system velocity</param>
        public async Task SetSystemVelocityAsync(double velocity)
        {
            ValidateConnection();

            try
            {
                _logger.Information("Setting system velocity for Hexapod {DeviceName} to {Velocity}",
                    _device.Name, velocity);

                await Task.Run(() =>
                {
                    int result = GCS2.VLS(_controllerId, velocity);
                    if (result == PI_RESULT_FAILURE)
                    {
                        Log.Error($"Failed to set system velocity for Hexapod {_device.Name}");
                        MessageBox.Show($"Failed to set system velocity for Hexapod {_device.Name}");
                    }
                });

                _logger.Information("Successfully set system velocity for Hexapod {DeviceName}", _device.Name);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error setting system velocity for Hexapod {DeviceName}", _device.Name);
                throw;
            }
        }


        


        /// <summary>
        /// Helper method to get a description for PI error codes
        /// </summary>
        private string GetErrorDescription(int errorCode)
        {
            StringBuilder errorBuffer = new StringBuilder(1024);
            PI.GCS2.TranslateError(errorCode, errorBuffer, errorBuffer.Capacity);
            return errorBuffer.ToString();
        }

        #endregion
    }
}