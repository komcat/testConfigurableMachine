
using System.IO;
using System.Text.Json;
using Serilog;



namespace MotionServiceLib
{
    #region Models

    public enum MotionDeviceType
    {
        Hexapod,
        Gantry,
        Unknown
    }

    public class MotionDevice
    {
        public string Id { get; set; }
        public MotionDeviceType Type { get; set; }
        public string Name { get; set; }
        public string GraphId { get; set; }
        public bool IsEnabled { get; set; }
        public string IpAddress { get; set; }
        public int Port { get; set; }

        // Additional properties common to all device types
        public Dictionary<string, Position> Positions { get; set; } = new Dictionary<string, Position>();
    }

    public class Position
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double U { get; set; } // Used for hexapods, optional for gantry
        public double V { get; set; } // Used for hexapods, optional for gantry
        public double W { get; set; } // Used for hexapods, optional for gantry

        // Convert to a device-specific array of doubles
        public double[] ToArray(MotionDeviceType deviceType)
        {
            return deviceType switch
            {
                MotionDeviceType.Hexapod => new double[] { X, Y, Z, U, V, W },
                MotionDeviceType.Gantry => new double[] { X, Y, Z, 0, 0, 0 }, // Gantry only needs X, Y, Z
                _ => throw new ArgumentException($"Unsupported device type: {deviceType}")
            };
        }
    }

    public class MotionSystemConfig
    {
        public List<MotionDevice> Devices { get; set; } = new List<MotionDevice>();
    }

    #endregion

    /// <summary>
    /// Central motion control kernel that manages all motion devices
    /// </summary>
    public class MotionKernel : IDisposable
    {

        private readonly MotionSystemConfig _config;
        private readonly Dictionary<string, IMotionController> _controllers = new Dictionary<string, IMotionController>();
        private bool _disposed;
        private readonly ILogger _logger;
        public MotionKernel()
        {
            // Get a contextualized logger
            _logger = Log.ForContext<MotionKernel>();

            _config = LoadConfiguration();
        }

        #region Configuration Management

        private MotionSystemConfig LoadConfiguration()
        {
            var config = new MotionSystemConfig { Devices = new List<MotionDevice>() };

            try
            {
                string configPath = System.IO.Path.Combine("Config", "MotionSystem.json");
                _logger.Information("Attempting to load configuration from {Path}", configPath);

                if (!File.Exists(configPath))
                {
                    _logger.Warning("Configuration file not found at {Path}", configPath);
                    return config;
                }

                string jsonContent = File.ReadAllText(configPath);
                _logger.Debug("Raw JSON content: {Content}", jsonContent);

                // Create a temporary class to match the JSON structure
                var jsonConfig = JsonSerializer.Deserialize<MotionDevicesData>(jsonContent);

                // Map the dictionary to our list of devices
                if (jsonConfig?.MotionDevices != null)
                {
                    foreach (var devicePair in jsonConfig.MotionDevices)
                    {
                        var device = new MotionDevice
                        {
                            Id = devicePair.Value.Id.ToString(),
                            Name = devicePair.Key,
                            Type = devicePair.Key.Contains("hex") ? MotionDeviceType.Hexapod : MotionDeviceType.Gantry,
                            IsEnabled = devicePair.Value.IsEnabled,
                            IpAddress = devicePair.Value.IpAddress,
                            Port = devicePair.Value.Port,
                            Positions = new Dictionary<string, Position>()
                        };

                        config.Devices.Add(device);
                        _logger.Debug("Added device: {DeviceId} - {DeviceName} ({DeviceType})",
                            device.Id, device.Name, device.Type);
                    }
                }

                _logger.Information("Loaded motion system configuration from {Path} with {DeviceCount} devices",
                    configPath, config.Devices.Count);

                LoadPositionData(config);
                return config;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load motion system configuration");
                return config;
            }
        }

        // Make sure to update LoadPositionData to take the config as a parameter
        private void LoadPositionData(MotionSystemConfig config)
        {
            try
            {
                string positionsPath = Path.Combine("Config", "WorkingPositions.json");
                if (File.Exists(positionsPath))
                {
                    string jsonContent = File.ReadAllText(positionsPath);
                    var positionsData = JsonSerializer.Deserialize<PositionsData>(jsonContent);

                    // Map positions to devices
                    foreach (var device in config.Devices)
                    {
                        if (device.Type == MotionDeviceType.Hexapod)
                        {
                            var hexapodPositions = positionsData.Hexapods?.Find(h => h.HexapodId.ToString() == device.Id)?.Positions;
                            if (hexapodPositions != null)
                            {
                                device.Positions = hexapodPositions;
                            }
                        }
                        else if (device.Type == MotionDeviceType.Gantry)
                        {
                            var gantryPositions = positionsData.Gantries?.Find(g => g.GantryId.ToString() == device.Id)?.Positions;
                            if (gantryPositions != null)
                            {
                                device.Positions = gantryPositions;
                            }
                        }
                    }

                    _logger.Information("Loaded position data from {Path}", positionsPath);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load position data");
            }
        }
        #endregion

        #region Device Management


        public async Task InitializeAsync()
        {
            // Log total devices and how many are enabled
            int totalDevices = _config.Devices.Count;
            int enabledDevices = _config.Devices.Count(d => d.IsEnabled);

            _logger.Information("Initializing motion system with {TotalDevices} total devices ({EnabledDevices} enabled)",
                totalDevices, enabledDevices);

            // Log device details
            foreach (var device in _config.Devices)
            {
                _logger.Debug("Found device in config: ID={DeviceId}, Name={DeviceName}, Type={DeviceType}, Enabled={IsEnabled}",
                    device.Id, device.Name, device.Type, device.IsEnabled);
            }

            // Process each device
            foreach (var device in _config.Devices)
            {
                if (!device.IsEnabled)
                {
                    _logger.Information("Device {DeviceId} ({DeviceName}) is disabled, skipping initialization",
                        device.Id, device.Name);
                    continue;
                }
                try
                {
                    _logger.Information("Initializing device {DeviceId} ({DeviceName}) of type {DeviceType}",
                        device.Id, device.Name, device.Type);

                    IMotionController controller = CreateController(device);
                    await controller.InitializeAsync();

                    if (controller.IsConnected)
                    {
                        _controllers[device.Id] = controller;
                        _logger.Information("Successfully initialized device {DeviceId} ({DeviceName})",
                            device.Id, device.Name);

                        //log device velocity
                        double? speed = await controller.GetSpeedAsync();
                        _logger.Information("device {DeviceId}:{DeviceName} velocity : {speed}", device.Id, device.Name, speed);
                    }
                    else
                    {
                        _logger.Warning("Failed to initialize device {DeviceId} ({DeviceName})",
                            device.Id, device.Name);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error initializing device {DeviceId} ({DeviceName})",
                        device.Id, device.Name);
                }
            }

            // Log summary at the end
            _logger.Information("Initialization complete. Successfully initialized {SuccessCount} of {EnabledCount} enabled devices",
                _controllers.Count, enabledDevices);
        }
        // Part of MotionKernel.cs, showing the updated CreateController method
        // Add this to MotionKernel.cs if it doesn't exist
        public bool IsDeviceConnected(string deviceId)
        {
            if (!_controllers.TryGetValue(deviceId, out var controller))
            {
                return false;
            }

            return controller.IsConnected;
        }
        private IMotionController CreateController(MotionDevice device)
        {
            _logger.Information("Creating controller for device {DeviceId} ({DeviceName}) of type {DeviceType}",
                device.Id, device.Name, device.Type);

            return device.Type switch
            {
                MotionDeviceType.Hexapod => new HexapodController(device),
                MotionDeviceType.Gantry => new GantryController(device),
                _ => throw new ArgumentException($"Unsupported device type: {device.Type}")
            };
        }

        // Method to access a specific Gantry controller
        public GantryController GetGantryController(string deviceId)
        {
            if (!_controllers.TryGetValue(deviceId, out var controller))
            {
                _logger.Warning("Gantry controller not found: {DeviceId}", deviceId);
                return null;
            }

            if (controller is GantryController gantryController)
            {
                return gantryController;
            }

            _logger.Warning("Controller {DeviceId} is not a GantryController", deviceId);
            return null;
        }

        #endregion

        #region Motion Operations

        public async Task<bool> MoveToPositionAsync(string deviceId, string positionName)
        {
            if (!_controllers.TryGetValue(deviceId, out var controller))
            {
                _logger.Warning("Cannot move to position: Device {DeviceId} not found or not enabled", deviceId);
                return false;
            }

            var device = _config.Devices.Find(d => d.Id == deviceId);
            if (device == null)
            {
                _logger.Warning("Cannot move to position: Device {DeviceId} configuration not found", deviceId);
                return false;
            }

            if (!device.Positions.TryGetValue(positionName, out var position))
            {
                _logger.Warning("Cannot move to position: Position {PositionName} not found for device {DeviceId}",
                    positionName, deviceId);
                return false;
            }

            try
            {
                _logger.Information("Moving device {DeviceId} to position {PositionName}", deviceId, positionName);
                await controller.MoveToPositionAsync(position);
                _logger.Information("Device {DeviceId} successfully moved to {PositionName}", deviceId, positionName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error moving device {DeviceId} to position {PositionName}",
                    deviceId, positionName);
                return false;
            }
        }

        public async Task<bool> MoveRelativeAsync(string deviceId, double[] relativeMove)
        {
            if (!_controllers.TryGetValue(deviceId, out var controller))
            {
                _logger.Warning("Cannot move relative: Device {DeviceId} not found or not enabled", deviceId);
                return false;
            }

            try
            {
                _logger.Information("Moving device {DeviceId} relative", deviceId);
                await controller.MoveRelativeAsync(relativeMove);
                _logger.Information("Device {DeviceId} successfully moved relative", deviceId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error moving device {DeviceId} relative", deviceId);
                return false;
            }
        }

        public async Task<bool> StopDeviceAsync(string deviceId)
        {
            if (!_controllers.TryGetValue(deviceId, out var controller))
            {
                _logger.Warning("Cannot stop device: Device {DeviceId} not found or not enabled", deviceId);
                return false;
            }

            try
            {
                _logger.Information("Stopping device {DeviceId}", deviceId);
                await controller.StopAsync();
                _logger.Information("Device {DeviceId} successfully stopped", deviceId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error stopping device {DeviceId}", deviceId);
                return false;
            }
        }

        public async Task<bool> StopAllDevicesAsync()
        {
            bool success = true;
            foreach (var deviceId in _controllers.Keys)
            {
                if (!await StopDeviceAsync(deviceId))
                {
                    success = false;
                }
            }
            return success;
        }

        public async Task<Position> GetCurrentPositionAsync(string deviceId)
        {
            if (!_controllers.TryGetValue(deviceId, out var controller))
            {
                _logger.Warning("Cannot get position: Device {DeviceId} not found or not enabled", deviceId);
                return null;
            }

            try
            {
                return await controller.GetCurrentPositionAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting current position for device {DeviceId}", deviceId);
                return null;
            }
        }


        /// <summary>
        /// Sets the pivot point coordinates for a hexapod device
        /// </summary>
        /// <param name="deviceIdOrName">ID or name of the hexapod device</param>
        /// <param name="x">X coordinate of pivot point</param>
        /// <param name="y">Y coordinate of pivot point</param>
        /// <param name="z">Z coordinate of pivot point</param>
        /// <returns>True if successful, false otherwise</returns>
        public async Task<bool> SetHexapodPivotPointAsync(string deviceIdOrName, double x, double y, double z)
        {
            // First try to find the device directly by ID
            if (_controllers.TryGetValue(deviceIdOrName, out var controller))
            {
                if (!(controller is HexapodController hexapodController))
                {
                    _logger.Warning("Cannot set pivot point: Device {DeviceId} is not a hexapod", deviceIdOrName);
                    return false;
                }

                try
                {
                    _logger.Information("Setting pivot point for device {DeviceId} to X={X}, Y={Y}, Z={Z}",
                        deviceIdOrName, x, y, z);
                    await hexapodController.SetPivotPointAsync(x, y, z);
                    _logger.Information("Successfully set pivot point for device {DeviceId}", deviceIdOrName);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error setting pivot point for device {DeviceId}", deviceIdOrName);
                    return false;
                }
            }

           

            // No matching device found
            _logger.Warning("Cannot set pivot point: No hexapod device found with ID or name '{DeviceIdOrName}'", deviceIdOrName);
            return false;
        }


        /// <summary>
        /// Gets the pivot point (rotation center) of a hexapod device
        /// </summary>
        /// <param name="deviceId">The device ID of the hexapod</param>
        /// <returns>The pivot point as a Position object, or null if retrieval fails</returns>
        public async Task<Position> GetHexapodPivotPointAsync(string deviceId)
        {
            if (!_controllers.TryGetValue(deviceId, out var controller))
            {
                _logger.Warning("Cannot get pivot point: Device {DeviceId} not found or not enabled", deviceId);
                return null;
            }

            // Check if the controller is a hexapod controller
            if (!(controller is HexapodController hexapodController))
            {
                _logger.Warning("Cannot get pivot point: Device {DeviceId} is not a hexapod device", deviceId);
                return null;
            }

            try
            {
                _logger.Information("Getting pivot point for hexapod device {DeviceId}", deviceId);

                // Get the pivot point from the hexapod controller
                var pivotPoint = await hexapodController.GetPivotPoint();

                if (pivotPoint != null)
                {
                    _logger.Information("Hexapod {DeviceId} pivot point: X={X}, Y={Y}, Z={Z}",
                        deviceId, pivotPoint.X, pivotPoint.Y, pivotPoint.Z);

                    return pivotPoint;
                }
                else
                {
                    _logger.Warning("Failed to get pivot point for hexapod {DeviceId}", deviceId);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting pivot point for hexapod {DeviceId}", deviceId);
                return null;
            }
        }


        /// <summary>
        /// Gets the name of the current position of the device, or the closest named position
        /// </summary>
        /// <param name="deviceId">The device ID</param>
        /// <param name="tolerance">Optional tolerance for position matching (default: 0.1)</param>
        /// <returns>The name of the current/closest position, or "Unknown" if not at a named position</returns>
        public async Task<string> GetCurrentPositionNameAsync(string deviceId, double tolerance = 0.1)
        {
            if (!_controllers.TryGetValue(deviceId, out var controller))
            {
                _logger.Warning("Cannot get position name: Device {DeviceId} not found or not enabled", deviceId);
                return "Unknown";
            }

            try
            {
                // Get the current position
                var currentPosition = await controller.GetCurrentPositionAsync();
                if (currentPosition == null)
                {
                    return "Unknown";
                }

                // Get the device from config
                var device = _config.Devices.Find(d => d.Id == deviceId);
                if (device == null || device.Positions.Count == 0)
                {
                    return "Unknown";
                }

                // Find exact or closest match
                string closestName = "Unknown";
                double minDistance = double.MaxValue;

                foreach (var position in device.Positions)
                {
                    // Calculate distance between current position and this named position
                    double distance = CalculatePositionDistance(currentPosition, position.Value, device.Type);

                    // If within tolerance, consider it an exact match
                    if (distance <= tolerance)
                    {
                        return position.Key;
                    }

                    // Track the closest position
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        closestName = position.Key;
                    }
                }

                // If we get here, we're not at an exact match, return the closest one
                // with distance information
                if (minDistance < double.MaxValue)
                {
                    _logger.Debug("Device {DeviceId} is closest to position {PositionName} (distance: {Distance})",
                        deviceId, closestName, minDistance);
                    return closestName;
                }

                return "Unknown";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting current position name for device {DeviceId}", deviceId);
                return "Unknown";
            }
        }

        /// <summary>
        /// Calculates the Euclidean distance between two positions, taking device type into account
        /// </summary>
        private double CalculatePositionDistance(Position position1, Position position2, MotionDeviceType deviceType)
        {
            // For gantry devices, consider only X, Y, Z
            if (deviceType == MotionDeviceType.Gantry)
            {
                return Math.Sqrt(
                    Math.Pow(position1.X - position2.X, 2) +
                    Math.Pow(position1.Y - position2.Y, 2) +
                    Math.Pow(position1.Z - position2.Z, 2)
                );
            }

            // For hexapod devices, consider all coordinates X, Y, Z, U, V, W
            return Math.Sqrt(
                Math.Pow(position1.X - position2.X, 2) +
                Math.Pow(position1.Y - position2.Y, 2) +
                Math.Pow(position1.Z - position2.Z, 2) +
                Math.Pow(position1.U - position2.U, 2) +
                Math.Pow(position1.V - position2.V, 2) +
                Math.Pow(position1.W - position2.W, 2)
            );
        }
        public async Task<bool> HomeDeviceAsync(string deviceId)
        {
            if (!_controllers.TryGetValue(deviceId, out var controller))
            {
                _logger.Warning("Cannot home device: Device {DeviceId} not found or not enabled", deviceId);
                return false;
            }

            try
            {
                _logger.Information("Homing device {DeviceId}", deviceId);
                await controller.HomeAsync();
                _logger.Information("Device {DeviceId} successfully homed", deviceId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error homing device {DeviceId}", deviceId);
                return false;
            }
        }

        // Add these methods to the MotionKernel class in MotionKernel.cs

        /// <summary>
        /// Gets a list of all devices in the system, connected or not
        /// </summary>
        /// <returns>List of devices</returns>
        public List<MotionDevice> GetDevices()
        {
            return _config.Devices;
        }
        // Add these methods to the MotionKernel class in MotionKernel.cs

        /// <summary>
        /// Teaches a new position for a device (saves current position with a name)
        /// </summary>
        /// <param name="deviceId">The device ID</param>
        /// <param name="positionName">The name for the position</param>
        /// <param name="position">The position to save (if null, uses current position)</param>
        /// <returns>True if successful, false otherwise</returns>
        public async Task<bool> TeachPositionAsync(string deviceId, string positionName, Position position = null)
        {
            try
            {
                if (!_controllers.TryGetValue(deviceId, out var controller))
                {
                    _logger.Warning("Cannot teach position: Device {DeviceId} not found or not enabled", deviceId);
                    return false;
                }

                var device = _config.Devices.Find(d => d.Id == deviceId);
                if (device == null)
                {
                    _logger.Warning("Cannot teach position: Device {DeviceId} configuration not found", deviceId);
                    return false;
                }

                // If position is not provided, get the current position
                if (position == null)
                {
                    position = await controller.GetCurrentPositionAsync();
                    if (position == null)
                    {
                        _logger.Warning("Cannot teach position: Failed to get current position for device {DeviceId}", deviceId);
                        return false;
                    }
                }

                // Make a deep copy of the position to avoid reference issues
                Position positionCopy = new Position
                {
                    X = position.X,
                    Y = position.Y,
                    Z = position.Z,
                    U = position.U,
                    V = position.V,
                    W = position.W
                };

                // Add or update the position
                if (device.Positions.ContainsKey(positionName))
                {
                    device.Positions[positionName] = positionCopy;
                    _logger.Information("Updated position {PositionName} for device {DeviceId}", positionName, deviceId);
                }
                else
                {
                    device.Positions.Add(positionName, positionCopy);
                    _logger.Information("Added new position {PositionName} for device {DeviceId}", positionName, deviceId);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error teaching position {PositionName} for device {DeviceId}", positionName, deviceId);
                return false;
            }
        }

        /// <summary>
        /// Saves all positions to a JSON file
        /// </summary>
        /// <returns>True if successful, false otherwise</returns>
        public async Task<bool> SavePositionsToJsonAsync()
        {
            try
            {
                // Create a PositionsData object to match the format of the JSON file
                var positionsData = new PositionsData
                {
                    Hexapods = new List<HexapodPositionSet>(),
                    Gantries = new List<GantryPositionSet>()
                };

                // Add all devices with their positions
                foreach (var device in _config.Devices)
                {
                    if (device.Type == MotionDeviceType.Hexapod)
                    {
                        positionsData.Hexapods.Add(new HexapodPositionSet
                        {
                            HexapodId = int.Parse(device.Id),
                            Positions = device.Positions
                        });
                    }
                    else if (device.Type == MotionDeviceType.Gantry)
                    {
                        positionsData.Gantries.Add(new GantryPositionSet
                        {
                            GantryId = int.Parse(device.Id),
                            Positions = device.Positions
                        });
                    }
                }

                // Serialize to JSON
                string json = System.Text.Json.JsonSerializer.Serialize(positionsData,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

                // Save to file (use Task.Run to make the file I/O asynchronous)
                string filePath = System.IO.Path.Combine("Config", "WorkingPositions.json");
                await Task.Run(() => System.IO.File.WriteAllText(filePath, json));

                _logger.Information("Saved positions to {FilePath}", filePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error saving positions to JSON");
                return false;
            }
        }

        /// <summary>
        /// Reloads positions from the JSON file
        /// </summary>
        /// <returns>True if successful, false otherwise</returns>
        public bool ReloadPositionsFromJson()
        {
            try
            {
                string filePath = System.IO.Path.Combine("Config", "WorkingPositions.json");
                if (!System.IO.File.Exists(filePath))
                {
                    _logger.Warning("Positions file not found at {FilePath}", filePath);
                    return false;
                }

                string jsonContent = System.IO.File.ReadAllText(filePath);
                var positionsData = System.Text.Json.JsonSerializer.Deserialize<PositionsData>(jsonContent);

                if (positionsData == null)
                {
                    _logger.Warning("Failed to deserialize positions data from {FilePath}", filePath);
                    return false;
                }

                // Update positions for all devices
                foreach (var device in _config.Devices)
                {
                    if (device.Type == MotionDeviceType.Hexapod)
                    {
                        int hexapodId;
                        if (int.TryParse(device.Id, out hexapodId))
                        {
                            var hexapodPositions = positionsData.Hexapods?.Find(h => h.HexapodId == hexapodId)?.Positions;
                            if (hexapodPositions != null)
                            {
                                device.Positions = new Dictionary<string, Position>(hexapodPositions);
                            }
                        }
                    }
                    else if (device.Type == MotionDeviceType.Gantry)
                    {
                        int gantryId;
                        if (int.TryParse(device.Id, out gantryId))
                        {
                            var gantryPositions = positionsData.Gantries?.Find(g => g.GantryId == gantryId)?.Positions;
                            if (gantryPositions != null)
                            {
                                device.Positions = new Dictionary<string, Position>(gantryPositions);
                            }
                        }
                    }
                }

                _logger.Information("Reloaded positions from {FilePath}", filePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error reloading positions from JSON");
                return false;
            }
        }


        /// <summary>
        /// Checks if a controller exists for the specified device
        /// </summary>
        /// <param name="deviceId">The device ID to check</param>
        /// <returns>True if a controller exists, false otherwise</returns>
        public bool HasControllerForDevice(string deviceId)
        {
            return _controllers.ContainsKey(deviceId);
        }


        // Add this to MotionKernel.cs
        public async Task<bool> SetDeviceSpeedAsync(string deviceId, double speed)
        {
            if (!_controllers.TryGetValue(deviceId, out var controller))
            {
                _logger.Warning("Cannot set speed: Device {DeviceId} not found or not enabled", deviceId);
                return false;
            }

            try
            {
                _logger.Information("Setting speed for device {DeviceId} to {Speed}", deviceId, speed);
                await controller.SetSpeedAsync(speed);
                _logger.Information("Successfully set speed for device {DeviceId}", deviceId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error setting speed for device {DeviceId}", deviceId);
                return false;
            }
        }

        // Add this to MotionKernel.cs
        public async Task<double?> GetDeviceSpeedAsync(string deviceId)
        {
            if (!_controllers.TryGetValue(deviceId, out var controller))
            {
                _logger.Warning("Cannot get speed: Device {DeviceId} not found or not enabled", deviceId);
                return null;
            }

            try
            {
                //_logger.Debug("Getting speed for device {DeviceId}", deviceId);
                double speed = await controller.GetSpeedAsync();
                //_logger.Debug("Device {DeviceId} current speed: {Speed}", deviceId, speed);
                return speed;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting speed for device {DeviceId}", deviceId);
                return null;
            }
        }

      

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                foreach (var controller in _controllers.Values)
                {
                    try
                    {
                        controller.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error during controller disposal");
                    }
                }
                _controllers.Clear();
            }

            _disposed = true;
        }

        #endregion

        // Add this class to match your JSON structure
        private class MotionDevicesData
        {
            public Dictionary<string, DeviceInfo> MotionDevices { get; set; }
        }

        private class DeviceInfo
        {
            public bool IsEnabled { get; set; }
            public string IpAddress { get; set; }
            public int Port { get; set; }
            public int Id { get; set; }
            public string Name { get; set; }
        }
    }

    #region Controller Interface and Implementations

    public interface IMotionController : IDisposable
    {
        bool IsConnected { get; }
        Task InitializeAsync();
        Task MoveToPositionAsync(Position position);
        Task MoveRelativeAsync(double[] relativeMove);
        Task StopAsync();
        Task<Position> GetCurrentPositionAsync();
        Task HomeAsync();
        Task SetSpeedAsync(double speed); // New method
        Task<double> GetSpeedAsync(); // New method to get the current speed
    }

    // Implementation for Hexapod devices

    // Implementation for Gantry devices


    #endregion

}
