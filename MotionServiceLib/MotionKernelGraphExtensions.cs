using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Serilog;

namespace MotionServiceLib
{
    /// <summary>
    /// Extension methods for the MotionKernel class to add path planning capabilities
    /// </summary>
    public static class MotionKernelGraphExtensions
    {
        private static readonly ILogger _logger = Log.ForContext(typeof(MotionKernelGraphExtensions));
        private static Dictionary<MotionKernel, MotionPathPlanner> _pathPlanners = new Dictionary<MotionKernel, MotionPathPlanner>();

        /// <summary>
        /// Gets or creates a path planner for the provided motion kernel
        /// </summary>
        /// <param name="kernel">The motion kernel</param>
        /// <returns>The associated path planner</returns>
        private static MotionPathPlanner GetPathPlanner(this MotionKernel kernel)
        {
            if (!_pathPlanners.TryGetValue(kernel, out var planner))
            {
                planner = new MotionPathPlanner(kernel);
                _pathPlanners[kernel] = planner;
                _logger.Debug("Created new path planner instance for motion kernel");
            }
            return planner;
        }

        /// <summary>
        /// Gets a list of available graph IDs
        /// </summary>
        /// <param name="kernel">The motion kernel</param>
        /// <returns>List of graph IDs</returns>
        public static List<string> GetAvailableGraphs(this MotionKernel kernel)
        {
            return kernel.GetPathPlanner().GetAvailableGraphs();
        }

        /// <summary>
        /// Gets a list of available destination positions for a device from its current position
        /// </summary>
        /// <param name="kernel">The motion kernel</param>
        /// <param name="deviceId">The device ID</param>
        /// <returns>List of available positions, or empty list if no graph exists</returns>
        public static async Task<List<string>> GetAvailableDestinationsAsync(this MotionKernel kernel, string deviceId)
        {
            return await kernel.GetPathPlanner().GetAvailableDestinationsAsync(deviceId);
        }

        /// <summary>
        /// Finds the path between two positions in a graph
        /// </summary>
        /// <param name="kernel">The motion kernel</param>
        /// <param name="graphId">The graph ID</param>
        /// <param name="startPosition">The starting position name</param>
        /// <param name="endPosition">The destination position name</param>
        /// <returns>List of position names in the path, or empty list if no path exists</returns>
        public static List<string> FindPath(this MotionKernel kernel, string graphId, string startPosition, string endPosition)
        {
            return kernel.GetPathPlanner().FindPath(graphId, startPosition, endPosition);
        }

        /// <summary>
        /// Moves a device along a path of named positions
        /// </summary>
        /// <param name="kernel">The motion kernel</param>
        /// <param name="deviceId">The device ID</param>
        /// <param name="path">The list of position names to visit</param>
        /// <returns>True if successful, false otherwise</returns>
        public static async Task<bool> MoveAlongPathAsync(this MotionKernel kernel, string deviceId, List<string> path)
        {
            return await kernel.GetPathPlanner().MoveAlongPathAsync(deviceId, path);
        }

        /// <summary>
        /// Moves a device from its current position to a destination position using the shortest path
        /// </summary>
        /// <param name="kernel">The motion kernel</param>
        /// <param name="deviceId">The device ID</param>
        /// <param name="destinationPosition">The destination position name</param>
        /// <returns>True if successful, false otherwise</returns>
        public static async Task<bool> MoveToDestinationViaPathAsync(this MotionKernel kernel, string deviceId, string destinationPosition)
        {
            return await kernel.GetPathPlanner().MoveToDestinationAsync(deviceId, destinationPosition);
        }

        /// <summary>
        /// Refreshes the path planner by disposing the current one and creating a new one
        /// This is useful after updating the graph data
        /// </summary>
        /// <param name="kernel">The motion kernel</param>
        public static void RefreshPathPlanner(this MotionKernel kernel)
        {
            if (_pathPlanners.TryGetValue(kernel, out var planner))
            {
                _pathPlanners.Remove(kernel);
                _logger.Information("Path planner refreshed");
            }
        }

        // Add these debugging methods to your MotionKernelGraphExtensions class

        /// <summary>
        /// Logs debug information about available graphs
        /// </summary>
        public static void LogPathPlannerDebugInfo(this MotionKernel kernel)
        {
            kernel.GetPathPlanner().LogDebugInfo();
        }

        /// <summary>
        /// Gets graph data for a specific graph ID
        /// </summary>
        public static Graph GetGraphData(this MotionKernel kernel, string graphId)
        {
            return kernel.GetPathPlanner().GetGraphData(graphId);
        }

        /// <summary>
        /// Checks if a device has a graph associated with it
        /// </summary>
        public static bool HasGraphForDevice(this MotionKernel kernel, MotionDevice device)
        {
            return kernel.GetPathPlanner().HasGraphForDevice(device);
        }

        /// <summary>
        /// Gets the graph ID for a specific device
        /// </summary>
        public static string GetGraphIdForDevice(this MotionKernel kernel, MotionDevice device)
        {
            var planner = kernel.GetPathPlanner();
            // Use reflection to access the private method - only for debugging
            var method = planner.GetType().GetMethod("GetGraphIdForDevice",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (method != null)
            {
                return method.Invoke(planner, new object[] { device }) as string;
            }

            return null;
        }
    }
}