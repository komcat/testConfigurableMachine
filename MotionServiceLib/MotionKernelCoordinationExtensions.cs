using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace MotionServiceLib
{
    /// <summary>
    /// Extension methods for the MotionKernel class to add multi-device coordination capabilities
    /// </summary>
    public static class MotionKernelCoordinationExtensions
    {
        private static readonly ILogger _logger = Log.ForContext(typeof(MotionKernelCoordinationExtensions));
        private static Dictionary<MotionKernel, MultiDevicePathCoordinator> _coordinators = new Dictionary<MotionKernel, MultiDevicePathCoordinator>();

        /// <summary>
        /// Gets or creates a path coordinator for the provided motion kernel
        /// </summary>
        /// <param name="kernel">The motion kernel</param>
        /// <returns>The associated path coordinator</returns>
        private static MultiDevicePathCoordinator GetPathCoordinator(this MotionKernel kernel)
        {
            if (!_coordinators.TryGetValue(kernel, out var coordinator))
            {
                coordinator = new MultiDevicePathCoordinator(kernel);
                _coordinators[kernel] = coordinator;
                _logger.Debug("Created new multi-device path coordinator for motion kernel");
            }
            return coordinator;
        }

        /// <summary>
        /// Extension method to add cancellation token support to MoveAlongPathAsync
        /// </summary>
        /// <param name="kernel">The motion kernel</param>
        /// <param name="deviceId">The device ID</param>
        /// <param name="path">The list of position names to visit</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>True if successful, false otherwise</returns>
        public static async Task<bool> MoveAlongPathAsync(this MotionKernel kernel, string deviceId,
            List<string> path, CancellationToken cancellationToken = default)
        {
            if (path == null || path.Count == 0)
            {
                _logger.Warning("Cannot move along path: Path is empty for device {DeviceId}", deviceId);
                return false;
            }

            _logger.Information("Moving device {DeviceId} along path: {Path}",
                deviceId, string.Join(" -> ", path));

            try
            {
                // Move to each position in sequence
                for (int i = 0; i < path.Count; i++)
                {
                    // Check for cancellation
                    cancellationToken.ThrowIfCancellationRequested();

                    string positionName = path[i];
                    _logger.Information("Moving to waypoint {Index}/{Total}: {Position}",
                        i + 1, path.Count, positionName);

                    bool success = await kernel.MoveToPositionAsync(deviceId, positionName);
                    if (!success)
                    {
                        _logger.Error("Failed to move to position {Position}", positionName);
                        return false;
                    }
                }

                _logger.Information("Device {DeviceId} successfully moved along path", deviceId);
                return true;
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("Path execution for device {DeviceId} was cancelled", deviceId);
                await kernel.StopDeviceAsync(deviceId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error moving device {DeviceId} along path", deviceId);
                return false;
            }
        }

        /// <summary>
        /// Executes multiple device path movements in parallel
        /// </summary>
        /// <param name="kernel">The motion kernel</param>
        /// <param name="devicePaths">Dictionary mapping device IDs to their respective paths</param>
        /// <returns>Dictionary mapping device IDs to their movement success status</returns>
        public static async Task<Dictionary<string, bool>> ExecuteParallelPathsAsync(
            this MotionKernel kernel, Dictionary<string, List<string>> devicePaths)
        {
            return await kernel.GetPathCoordinator().ExecuteParallelPathsAsync(devicePaths);
        }

        /// <summary>
        /// Executes multiple device path movements sequentially
        /// </summary>
        /// <param name="kernel">The motion kernel</param>
        /// <param name="devicePaths">Ordered list of (device ID, path) pairs to execute in sequence</param>
        /// <returns>Dictionary mapping device IDs to their movement success status</returns>
        public static async Task<Dictionary<string, bool>> ExecuteSequentialPathsAsync(
            this MotionKernel kernel, List<(string DeviceId, List<string> Path)> devicePaths)
        {
            return await kernel.GetPathCoordinator().ExecuteSequentialPathsAsync(devicePaths);
        }

        /// <summary>
        /// Executes a coordinated multi-device operation
        /// </summary>
        /// <param name="kernel">The motion kernel</param>
        /// <param name="operation">The coordinated operation to execute</param>
        /// <returns>True if all steps completed successfully, false otherwise</returns>
        public static async Task<bool> ExecuteCoordinatedOperationAsync(
            this MotionKernel kernel, CoordinatedOperation operation)
        {
            return await kernel.GetPathCoordinator().ExecuteCoordinatedOperationAsync(operation);
        }

        /// <summary>
        /// Cancels any ongoing coordinated operations
        /// </summary>
        /// <param name="kernel">The motion kernel</param>
        public static void CancelCoordinatedOperation(this MotionKernel kernel)
        {
            kernel.GetPathCoordinator().CancelOperation();
        }
    }
}