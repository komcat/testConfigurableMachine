using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Serilog;

namespace MotionServiceLib
{
    /// <summary>
    /// Extension methods for the MotionKernel class to add direct position movement capabilities
    /// </summary>
    public static class MotionKernelPositionExtensions
    {
        private static readonly ILogger _logger = Log.ForContext(typeof(MotionKernelPositionExtensions));

        /// <summary>
        /// Moves a device directly to a specific position with XYZ/UVW coordinates
        /// </summary>
        /// <param name="kernel">The motion kernel</param>
        /// <param name="deviceId">The device ID</param>
        /// <param name="x">X coordinate</param>
        /// <param name="y">Y coordinate</param>
        /// <param name="z">Z coordinate</param>
        /// <param name="u">U coordinate (rotation, used for Hexapods)</param>
        /// <param name="v">V coordinate (rotation, used for Hexapods)</param>
        /// <param name="w">W coordinate (rotation, used for Hexapods)</param>
        /// <returns>True if successful, false otherwise</returns>
        public static async Task<bool> MoveToCoordinatesAsync(
            this MotionKernel kernel,
            string deviceId,
            double x, double y, double z,
            double u = 0, double v = 0, double w = 0)
        {
            if (kernel == null)
            {
                throw new ArgumentNullException(nameof(kernel));
            }

            if (string.IsNullOrEmpty(deviceId))
            {
                throw new ArgumentException("Device ID cannot be null or empty", nameof(deviceId));
            }

            try
            {
                // Check if the device exists and is connected
                if (!kernel.IsDeviceConnected(deviceId))
                {
                    _logger.Warning("Cannot move to coordinates: Device {DeviceId} not found or not connected", deviceId);
                    return false;
                }

                // Create a new Position object with the specified coordinates
                var targetPosition = new Position
                {
                    X = x,
                    Y = y,
                    Z = z,
                    U = u,
                    V = v,
                    W = w
                };

                // Get the device to determine its type
                var device = kernel.GetDevices().Find(d => d.Id == deviceId);
                if (device == null)
                {
                    _logger.Warning("Cannot move to coordinates: Device {DeviceId} not found in configuration", deviceId);
                    return false;
                }

                _logger.Information("Moving device {DeviceId} to coordinates: X={X}, Y={Y}, Z={Z}, U={U}, V={V}, W={W}",
                    deviceId, x, y, z, u, v, w);

                // Get the controller for the device
                if (!kernel.HasControllerForDevice(deviceId))
                {
                    _logger.Warning("Cannot move to coordinates: No controller found for device {DeviceId}", deviceId);
                    return false;
                }

                // Get the controller through reflection since we don't have direct access to it
                var controllerField = typeof(MotionKernel).GetField("_controllers",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (controllerField == null)
                {
                    _logger.Error("Cannot move to coordinates: Unable to access controllers field");
                    return false;
                }

                var controllers = controllerField.GetValue(kernel) as Dictionary<string, IMotionController>;
                if (controllers == null || !controllers.TryGetValue(deviceId, out var controller))
                {
                    _logger.Warning("Cannot move to coordinates: Controller for device {DeviceId} not found", deviceId);
                    return false;
                }

                // Call MoveToPositionAsync on the controller
                await controller.MoveToPositionAsync(targetPosition);

                _logger.Information("Device {DeviceId} successfully moved to specified coordinates", deviceId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error moving device {DeviceId} to coordinates", deviceId);
                return false;
            }
        }

        /// <summary>
        /// Moves a device directly to a specified Position object
        /// </summary>
        /// <param name="kernel">The motion kernel</param>
        /// <param name="deviceId">The device ID</param>
        /// <param name="position">The target position</param>
        /// <returns>True if successful, false otherwise</returns>
        public static async Task<bool> MoveToPositionDirectAsync(
            this MotionKernel kernel,
            string deviceId,
            Position position)
        {
            if (kernel == null)
            {
                throw new ArgumentNullException(nameof(kernel));
            }

            if (string.IsNullOrEmpty(deviceId))
            {
                throw new ArgumentException("Device ID cannot be null or empty", nameof(deviceId));
            }

            if (position == null)
            {
                throw new ArgumentNullException(nameof(position));
            }

            return await MoveToCoordinatesAsync(
                kernel,
                deviceId,
                position.X, position.Y, position.Z,
                position.U, position.V, position.W);
        }

        /// <summary>
        /// Creates a temporary named position for a device, moves to it, and then removes it
        /// This is an alternative approach if direct access to the controller is not available
        /// </summary>
        /// <param name="kernel">The motion kernel</param>
        /// <param name="deviceId">The device ID</param>
        /// <param name="position">The target position</param>
        /// <returns>True if successful, false otherwise</returns>
        public static async Task<bool> MoveToTemporaryPositionAsync(
            this MotionKernel kernel,
            string deviceId,
            Position position)
        {
            if (kernel == null || position == null)
            {
                return false;
            }

            try
            {
                // Create a unique temporary position name
                string tempPositionName = $"TempPos_{Guid.NewGuid():N}";

                // Teach the temporary position
                bool teachSuccess = await kernel.TeachPositionAsync(deviceId, tempPositionName, position);
                if (!teachSuccess)
                {
                    _logger.Warning("Failed to create temporary position for device {DeviceId}", deviceId);
                    return false;
                }

                // Move to the temporary position
                bool moveSuccess = await kernel.MoveToPositionAsync(deviceId, tempPositionName);

                // Get the device to remove the temporary position
                var device = kernel.GetDevices().Find(d => d.Id == deviceId);
                if (device != null && device.Positions.ContainsKey(tempPositionName))
                {
                    device.Positions.Remove(tempPositionName);
                }

                return moveSuccess;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error moving device {DeviceId} to temporary position", deviceId);
                return false;
            }
        }
    }
}