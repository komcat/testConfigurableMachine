using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MotionServiceLib
{
    /// <summary>
    /// Extension methods for the MotionKernel class for path planning
    /// </summary>
    public static class MotionKernelPathPlanningExtensions
    {
        private static readonly ILogger _logger = Log.ForContext(typeof(MotionKernelPathPlanningExtensions));
        private static MotionPathPlanner _pathPlanner;

        /// <summary>
        /// Gets or creates a path planner instance
        /// </summary>
        private static MotionPathPlanner GetPathPlanner(MotionKernel kernel)
        {
            return _pathPlanner ??= new MotionPathPlanner(kernel);
        }

        /// <summary>
        /// Moves a device to a destination using the shortest path found in the device's graph
        /// </summary>
        /// <param name="kernel">The motion kernel</param>
        /// <param name="deviceId">The device ID</param>
        /// <param name="destinationPosition">The destination position name</param>
        /// <returns>True if the movement was successful, false otherwise</returns>
        public static async Task<bool> MoveToDestinationShortestPathAsync(
            this MotionKernel kernel,
            string deviceId,
            string destinationPosition)
        {
            try
            {
                var pathPlanner = GetPathPlanner(kernel);

                // Get the current position name
                string currentPositionName = await kernel.GetCurrentPositionNameAsync(deviceId);
                if (string.IsNullOrEmpty(currentPositionName))
                {
                    _logger.Warning("Cannot move to destination: Current position unknown for device {DeviceId}", deviceId);
                    return false;
                }

                _logger.Information("Moving device {DeviceId} from {CurrentPosition} to {DestinationPosition} using shortest path",
                    deviceId, currentPositionName, destinationPosition);

                // Find the path
                List<string> path = pathPlanner.FindShortestPath(deviceId, currentPositionName, destinationPosition);

                // If no path was found or path is empty, try direct movement
                if (path == null || path.Count == 0)
                {
                    _logger.Warning("No path found from {CurrentPosition} to {DestinationPosition}. " +
                        "Attempting direct movement.", currentPositionName, destinationPosition);
                    return await kernel.MoveToPositionAsync(deviceId, destinationPosition);
                }

                // Move along the path
                return await pathPlanner.MoveAlongPathAsync(deviceId, path);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error moving device {DeviceId} to destination {DestinationPosition}",
                    deviceId, destinationPosition);
                return false;
            }
        }
    }
}
