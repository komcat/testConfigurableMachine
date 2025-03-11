using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MotionServiceLib;
using Serilog;

namespace testConfigurableMachine
{
    /// <summary>
    /// Handles rotational jogging operations (U, V, W axes) for compatible devices
    /// </summary>
    public class RotationJogHandler
    {
        private readonly MotionKernel _motionKernel;
        private readonly ILogger _logger;
        private double _rotationStepSize = 0.01; // Default rotation step size in degrees

        /// <summary>
        /// Rotation direction enum
        /// </summary>
        public enum RotationDirection
        {
            UPlus,  // Rotation around X axis (positive)
            UMinus, // Rotation around X axis (negative)
            VPlus,  // Rotation around Y axis (positive)
            VMinus, // Rotation around Y axis (negative)
            WPlus,  // Rotation around Z axis (positive)
            WMinus  // Rotation around Z axis (negative)
        }

        /// <summary>
        /// Creates a new instance of RotationJogHandler
        /// </summary>
        /// <param name="motionKernel">Reference to the motion kernel</param>
        public RotationJogHandler(MotionKernel motionKernel)
        {
            _motionKernel = motionKernel ?? throw new ArgumentNullException(nameof(motionKernel));
            _logger = Log.ForContext<RotationJogHandler>();
            _logger.Debug("RotationJogHandler initialized");
        }

        /// <summary>
        /// Sets the rotation step size
        /// </summary>
        /// <param name="stepSize">Step size in degrees</param>
        public void SetStepSize(double stepSize)
        {
            _rotationStepSize = stepSize;
            _logger.Debug("Rotation step size set to {StepSize}", _rotationStepSize);
        }

        /// <summary>
        /// Applies a rotation movement to the selected devices
        /// </summary>
        /// <param name="deviceIds">List of device IDs to move</param>
        /// <param name="direction">Rotation direction</param>
        /// <returns>Task representing the operation</returns>
        public async Task ApplyRotationAsync(IEnumerable<string> deviceIds, RotationDirection direction)
        {
            if (deviceIds == null || !deviceIds.Any())
            {
                _logger.Warning("No devices selected for rotation");
                return;
            }

            // Create the rotation vector based on the direction
            double[] rotationVector = CreateRotationVector(direction);

            // Move each compatible device
            var moveTasks = new List<Task>();
            foreach (var deviceId in deviceIds)
            {
                var device = _motionKernel.GetDevices().FirstOrDefault(d => d.Id == deviceId);

                // Only apply rotation to hexapod devices
                if (device?.Type == MotionDeviceType.Hexapod)
                {
                    moveTasks.Add(MoveDeviceAsync(deviceId, rotationVector));
                }
                else
                {
                    _logger.Information("Skipping rotation for non-hexapod device {DeviceId}", deviceId);
                }
            }

            // Wait for all moves to complete
            await Task.WhenAll(moveTasks);

            _logger.Information("Completed {Direction} rotation for {Count} devices",
                direction.ToString(), moveTasks.Count);
        }

        /// <summary>
        /// Creates a rotation vector for the specified direction
        /// </summary>
        /// <param name="direction">Rotation direction</param>
        /// <returns>6-element vector [X,Y,Z,U,V,W] with rotation values</returns>
        private double[] CreateRotationVector(RotationDirection direction)
        {
            // Initialize a vector with 6 elements (X,Y,Z,U,V,W)
            var vector = new double[6];

            // Set only the rotational component based on the direction
            switch (direction)
            {
                case RotationDirection.UPlus:
                    vector[3] = _rotationStepSize;  // U+ (rotation around X)
                    break;
                case RotationDirection.UMinus:
                    vector[3] = -_rotationStepSize; // U- (rotation around X)
                    break;
                case RotationDirection.VPlus:
                    vector[4] = _rotationStepSize;  // V+ (rotation around Y)
                    break;
                case RotationDirection.VMinus:
                    vector[4] = -_rotationStepSize; // V- (rotation around Y)
                    break;
                case RotationDirection.WPlus:
                    vector[5] = _rotationStepSize;  // W+ (rotation around Z)
                    break;
                case RotationDirection.WMinus:
                    vector[5] = -_rotationStepSize; // W- (rotation around Z)
                    break;
            }

            return vector;
        }

        /// <summary>
        /// Applies a rotation movement to a specific device
        /// </summary>
        /// <param name="deviceId">Device ID</param>
        /// <param name="rotationVector">Rotation vector [X,Y,Z,U,V,W]</param>
        /// <returns>Task representing the operation</returns>
        private async Task MoveDeviceAsync(string deviceId, double[] rotationVector)
        {
            try
            {
                _logger.Debug("Applying rotation {Vector} to device {DeviceId}",
                    string.Join(",", rotationVector), deviceId);

                // Execute the move
                bool success = await _motionKernel.MoveRelativeAsync(deviceId, rotationVector);

                if (!success)
                {
                    _logger.Warning("Failed to apply rotation to device {DeviceId}", deviceId);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error applying rotation to device {DeviceId}", deviceId);
            }
        }
    }
}