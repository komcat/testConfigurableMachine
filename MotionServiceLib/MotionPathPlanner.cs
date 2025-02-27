using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;

namespace MotionServiceLib
{
    /// <summary>
    /// Handles path planning and traversal between named positions using graph definitions
    /// </summary>
    public class MotionPathPlanner
    {
        private readonly MotionKernel _motionKernel;
        private readonly ILogger _logger;
        private GraphData _graphData;

        /// <summary>
        /// Creates a new instance of the MotionPathPlanner
        /// </summary>
        /// <param name="motionKernel">Reference to the MotionKernel instance</param>
        public MotionPathPlanner(MotionKernel motionKernel)
        {
            _motionKernel = motionKernel ?? throw new ArgumentNullException(nameof(motionKernel));
            _logger = Log.ForContext<MotionPathPlanner>();
            LoadGraphData();
        }

        /// <summary>
        /// Loads the graph data from the configuration file
        /// </summary>
        private void LoadGraphData()
        {
            try
            {
                string filePath = Path.Combine("Config", "WorkingGraphs.json");
                _logger.Information("Attempting to load graph data from {FilePath}", filePath);

                if (!File.Exists(filePath))
                {
                    _logger.Warning("Graphs file not found at {FilePath}", filePath);
                    _graphData = new GraphData();
                    return;
                }

                string jsonContent = File.ReadAllText(filePath);
                _logger.Debug("First 100 chars of JSON content: {Content}",
                    jsonContent.Length > 100 ? jsonContent.Substring(0, 100) + "..." : jsonContent);

                try
                {
                    // First try to deserialize as the root structure
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };

                    var rootData = JsonSerializer.Deserialize<GraphDataRoot>(jsonContent, options);

                    if (rootData != null && rootData.Graphs != null && rootData.Graphs.Count > 0)
                    {
                        _logger.Information("Successfully parsed JSON with root 'graphs' property");
                        _graphData = new GraphData { Graphs = rootData.Graphs };
                    }
                    else
                    {
                        // Try direct deserialization
                        _logger.Warning("Could not parse using root structure, trying direct deserialization");
                        _graphData = JsonSerializer.Deserialize<GraphData>(jsonContent, options);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error deserializing JSON graph data");
                    _graphData = new GraphData();
                    return;
                }

                if (_graphData == null || _graphData.Graphs == null)
                {
                    _logger.Warning("Failed to deserialize graph data from {FilePath}", filePath);
                    _graphData = new GraphData();
                    return;
                }

                // Log the loaded graphs for debugging
                var graphNames = _graphData.Graphs.Keys.ToList();
                _logger.Information("Loaded {Count} graphs: {GraphNames}",
                    graphNames.Count, string.Join(", ", graphNames));

                // Log nodes and edges for each graph
                foreach (var kvp in _graphData.Graphs)
                {
                    _logger.Debug("Graph {GraphName}: {NodeCount} nodes, {EdgeCount} edges",
                        kvp.Key, kvp.Value.Nodes?.Count ?? 0, kvp.Value.Edges?.Count ?? 0);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading graph data");
                _graphData = new GraphData();
            }
        }
        /// <summary>
        /// Gets a list of available graph IDs
        /// </summary>
        /// <returns>List of graph IDs</returns>
        public List<string> GetAvailableGraphs()
        {
            return _graphData.Graphs.Keys.ToList();
        }

        /// <summary>
        /// Checks if a graph exists for the specified device type
        /// </summary>
        /// <param name="graphId">The graph ID to check</param>
        /// <returns>True if the graph exists, false otherwise</returns>
        public bool HasGraph(string graphId)
        {
            return _graphData.Graphs.ContainsKey(graphId);
        }

        /// <summary>
        /// Gets a list of available destination positions for a device from its current position
        /// </summary>
        /// <param name="deviceId">The device ID</param>
        /// <returns>List of available positions, or empty list if no graph exists</returns>
        public async Task<List<string>> GetAvailableDestinationsAsync(string deviceId)
        {
            var device = _motionKernel.GetDevices().FirstOrDefault(d => d.Id == deviceId);
            if (device == null)
            {
                _logger.Warning("Cannot get available destinations: Device {DeviceId} not found", deviceId);
                return new List<string>();
            }

            // Determine graph ID for this device
            string graphId = GetGraphIdForDevice(device);
            if (string.IsNullOrEmpty(graphId) || !_graphData.Graphs.ContainsKey(graphId))
            {
                _logger.Warning("Cannot get available destinations: No graph found for device {DeviceId}", deviceId);
                return new List<string>();
            }

            // Get the current position name
            string currentPositionName = await GetCurrentPositionNameAsync(deviceId);
            if (string.IsNullOrEmpty(currentPositionName))
            {
                _logger.Warning("Cannot get available destinations: Current position unknown for device {DeviceId}", deviceId);
                return new List<string>();
            }

            // Get available destinations from current position
            var graph = _graphData.Graphs[graphId];
            var edges = graph.Edges.Where(e => e.From.Equals(currentPositionName, StringComparison.OrdinalIgnoreCase)).ToList();

            return edges.Select(e => e.To).Distinct().ToList();
        }

        /// <summary>
        /// Finds the shortest path between two positions in a graph
        /// </summary>
        /// <param name="graphId">The graph ID</param>
        /// <param name="startPosition">The starting position name</param>
        /// <param name="endPosition">The destination position name</param>
        /// <returns>List of position names in the path, or empty list if no path exists</returns>
        public List<string> FindPath(string graphId, string startPosition, string endPosition)
        {
            if (!_graphData.Graphs.TryGetValue(graphId, out var graph))
            {
                _logger.Warning("Cannot find path: Graph {GraphId} not found", graphId);
                return new List<string>();
            }

            // Convert graph to adjacency list for Dijkstra's algorithm
            Dictionary<string, Dictionary<string, double>> adjacencyList = BuildAdjacencyList(graph);

            // Check if both positions exist in the graph
            if (!adjacencyList.ContainsKey(startPosition) || !adjacencyList.ContainsKey(endPosition))
            {
                _logger.Warning("Cannot find path: Start position {Start} or end position {End} not found in graph {GraphId}",
                    startPosition, endPosition, graphId);
                return new List<string>();
            }

            // Find the shortest path using Dijkstra's algorithm
            return FindShortestPath(adjacencyList, startPosition, endPosition);
        }

        /// <summary>
        /// Moves a device along a path of named positions
        /// </summary>
        /// <param name="deviceId">The device ID</param>
        /// <param name="path">The list of position names to visit</param>
        /// <returns>True if successful, false otherwise</returns>
        public async Task<bool> MoveAlongPathAsync(string deviceId, List<string> path)
        {
            if (path == null || path.Count == 0)
            {
                _logger.Warning("Cannot move along path: Path is empty for device {DeviceId}", deviceId);
                return false;
            }

            _logger.Information("Moving device {DeviceId} along path: {Path}",
                deviceId, string.Join(" -> ", path));

            // Move to each position in sequence
            for (int i = 0; i < path.Count; i++)
            {
                string positionName = path[i];
                _logger.Information("Moving to waypoint {Index}/{Total}: {Position}",
                    i + 1, path.Count, positionName);

                bool success = await _motionKernel.MoveToPositionAsync(deviceId, positionName);
                if (!success)
                {
                    _logger.Error("Failed to move to position {Position}", positionName);
                    return false;
                }
            }

            _logger.Information("Device {DeviceId} successfully moved along path", deviceId);
            return true;
        }

        /// <summary>
        /// Moves a device from its current position to a destination position using the shortest path
        /// </summary>
        /// <param name="deviceId">The device ID</param>
        /// <param name="destinationPosition">The destination position name</param>
        /// <returns>True if successful, false otherwise</returns>
        public async Task<bool> MoveToDestinationAsync(string deviceId, string destinationPosition)
        {
            var device = _motionKernel.GetDevices().FirstOrDefault(d => d.Id == deviceId);
            if (device == null)
            {
                _logger.Warning("Cannot move to destination: Device {DeviceId} not found", deviceId);
                return false;
            }

            // Determine graph ID for this device
            string graphId = GetGraphIdForDevice(device);
            if (string.IsNullOrEmpty(graphId) || !_graphData.Graphs.ContainsKey(graphId))
            {
                _logger.Warning("Cannot move to destination: No graph found for device {DeviceId}", deviceId);
                return false;
            }

            // Get the current position name
            string currentPositionName = await GetCurrentPositionNameAsync(deviceId);
            if (string.IsNullOrEmpty(currentPositionName))
            {
                _logger.Warning("Cannot move to destination: Current position unknown for device {DeviceId}", deviceId);
                return false;
            }

            // Special case - already at destination
            if (currentPositionName.Equals(destinationPosition, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Information("Device {DeviceId} is already at position {Position}", deviceId, destinationPosition);
                return true;
            }

            // Find the path
            List<string> path = FindPath(graphId, currentPositionName, destinationPosition);
            if (path.Count == 0)
            {
                _logger.Warning("Cannot move to destination: No path found from {Start} to {End} for device {DeviceId}",
                    currentPositionName, destinationPosition, deviceId);
                return false;
            }

            // Move along the path
            return await MoveAlongPathAsync(deviceId, path);
        }

        /// <summary>
        /// Gets the name of the position that best matches the device's current position
        /// </summary>
        /// <param name="deviceId">The device ID</param>
        /// <returns>The name of the current position, or null if no match is found</returns>
        private async Task<string> GetCurrentPositionNameAsync(string deviceId)
        {
            try
            {
                // Get the current position
                var currentPosition = await _motionKernel.GetCurrentPositionAsync(deviceId);
                if (currentPosition == null)
                {
                    return null;
                }

                // Get the device
                var device = _motionKernel.GetDevices().FirstOrDefault(d => d.Id == deviceId);
                if (device == null || device.Positions == null || device.Positions.Count == 0)
                {
                    return null;
                }

                // Find the position with the smallest distance to the current position
                string closestPositionName = null;
                double minDistance = double.MaxValue;

                foreach (var position in device.Positions)
                {
                    double distance = CalculatePositionDistance(currentPosition, position.Value, device.Type);

                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        closestPositionName = position.Key;
                    }
                }

                // Tolerance for position matching - device is considered at a position if within this tolerance
                double tolerance = device.Type == MotionDeviceType.Hexapod ? 0.05 : 0.5; // tighter tolerance for hexapods

                if (minDistance <= tolerance)
                {
                    return closestPositionName;
                }
                else
                {
                    _logger.Debug("Current position does not match any named position, closest is {Position} at distance {Distance}",
                        closestPositionName, minDistance);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error determining current position name for device {DeviceId}", deviceId);
                return null;
            }
        }



        public void LogDebugInfo()
        {
            _logger.Information("Debug info for MotionPathPlanner:");

            // Log all available graphs
            _logger.Information("Available graphs: {Graphs}",
                string.Join(", ", _graphData.Graphs.Keys));

            // If there are no graphs loaded
            if (_graphData.Graphs.Count == 0)
            {
                _logger.Warning("No graphs were loaded from the configuration file");
            }
        }

        /// <summary>
        /// Calculates the Euclidean distance between two positions
        /// </summary>
        /// <param name="pos1">First position</param>
        /// <param name="pos2">Second position</param>
        /// <param name="deviceType">The device type, to determine which coordinates to consider</param>
        /// <returns>The distance between the positions</returns>
        private double CalculatePositionDistance(Position pos1, Position pos2, MotionDeviceType deviceType)
        {
            double dx = pos1.X - pos2.X;
            double dy = pos1.Y - pos2.Y;
            double dz = pos1.Z - pos2.Z;

            // For hexapods, also consider rotation
            if (deviceType == MotionDeviceType.Hexapod)
            {
                double du = pos1.U - pos2.U;
                double dv = pos1.V - pos2.V;
                double dw = pos1.W - pos2.W;

                // Weighted distance, translation has higher weight than rotation
                return Math.Sqrt(dx * dx + dy * dy + dz * dz + 0.1 * (du * du + dv * dv + dw * dw));
            }

            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>
        /// Gets the graph ID for a device based on its type and name
        /// </summary>
        /// <param name="device">The device</param>
        /// <returns>The graph ID, or null if no graph is found</returns>
        // Update your GetGraphIdForDevice method with more detailed logging

        private string GetGraphIdForDevice(MotionDevice device)
        {
            _logger.Debug("Determining graph ID for device {DeviceId} ({DeviceName})",
                device.Id, device.Name);

            // If the device has an explicit graph ID set, use that
            if (!string.IsNullOrEmpty(device.GraphId) && _graphData.Graphs.ContainsKey(device.GraphId))
            {
                _logger.Debug("Using explicit graph ID from device: {GraphId}", device.GraphId);
                return device.GraphId;
            }

            // Otherwise, try to determine from the device name
            if (device.Type == MotionDeviceType.Hexapod)
            {
                _logger.Debug("Device is a Hexapod, looking for appropriate graph based on name");

                if (device.Name.Contains("left", StringComparison.OrdinalIgnoreCase))
                {
                    string graphId = "HexapodLeft";
                    bool exists = _graphData.Graphs.ContainsKey(graphId);
                    _logger.Debug("Checking for graph '{GraphId}': {Exists}", graphId, exists ? "Found" : "Not Found");
                    return exists ? graphId : null;
                }
                else if (device.Name.Contains("right", StringComparison.OrdinalIgnoreCase))
                {
                    string graphId = "HexapodRight";
                    bool exists = _graphData.Graphs.ContainsKey(graphId);
                    _logger.Debug("Checking for graph '{GraphId}': {Exists}", graphId, exists ? "Found" : "Not Found");
                    return exists ? graphId : null;
                }
                else if (device.Name.Contains("bottom", StringComparison.OrdinalIgnoreCase))
                {
                    string graphId = "HexapodBottom";
                    bool exists = _graphData.Graphs.ContainsKey(graphId);
                    _logger.Debug("Checking for graph '{GraphId}': {Exists}", graphId, exists ? "Found" : "Not Found");
                    return exists ? graphId : null;
                }
                else
                {
                    // Try all hexapod graphs if the name doesn't match
                    _logger.Debug("No specific hexapod type found in name, trying all hexapod graphs");
                    foreach (var graphName in new[] { "HexapodLeft", "HexapodRight", "HexapodBottom" })
                    {
                        if (_graphData.Graphs.ContainsKey(graphName))
                        {
                            _logger.Debug("Using first available hexapod graph: {GraphName}", graphName);
                            return graphName;
                        }
                    }
                }
            }
            else if (device.Type == MotionDeviceType.Gantry)
            {
                string graphId = "Gantry";
                bool exists = _graphData.Graphs.ContainsKey(graphId);
                _logger.Debug("Device is Gantry, checking for graph '{GraphId}': {Exists}",
                    graphId, exists ? "Found" : "Not Found");
                return exists ? graphId : null;
            }

            _logger.Warning("Could not determine a graph ID for device {DeviceId} ({DeviceName})",
                device.Id, device.Name);
            return null;
        }

        // Add this to expose graph checking to MotionKernel extensions
        public bool HasGraphForDevice(MotionDevice device)
        {
            string graphId = GetGraphIdForDevice(device);
            bool hasGraph = !string.IsNullOrEmpty(graphId);
            _logger.Information("Device {DeviceId} ({DeviceName}) has graph: {HasGraph} ({GraphId})",
                device.Id, device.Name, hasGraph, graphId ?? "none");
            return hasGraph;
        }

        // Add this method to expose graph data for debugging
        public Graph GetGraphData(string graphId)
        {
            if (string.IsNullOrEmpty(graphId) || !_graphData.Graphs.ContainsKey(graphId))
            {
                _logger.Warning("Requested graph data for unknown graph ID: {GraphId}", graphId ?? "null");
                return null;
            }

            return _graphData.Graphs[graphId];
        }

        /// <summary>
        /// Builds an adjacency list from a graph for path finding
        /// </summary>
        /// <param name="graph">The graph</param>
        /// <returns>Adjacency list representation of the graph</returns>
        private Dictionary<string, Dictionary<string, double>> BuildAdjacencyList(Graph graph)
        {
            var adjacencyList = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);

            // Add all explicit nodes
            foreach (var node in graph.Nodes)
            {
                adjacencyList[node] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            }

            // Add edges and ensure all nodes in edges exist
            foreach (var edge in graph.Edges)
            {
                // Make sure source node exists
                if (!adjacencyList.ContainsKey(edge.From))
                {
                    _logger.Warning("Edge refers to source node not in nodes list: {From}", edge.From);
                    adjacencyList[edge.From] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                }

                // Add the edge
                adjacencyList[edge.From][edge.To] = edge.Weight;

                // Ensure destination node exists (for path finding)
                if (!adjacencyList.ContainsKey(edge.To))
                {
                    _logger.Warning("Edge refers to destination node not in nodes list: {To}", edge.To);
                    adjacencyList[edge.To] = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                }
            }

            return adjacencyList;
        }
        /// <summary>
        /// Finds the shortest path using Dijkstra's algorithm
        /// </summary>
        /// <param name="graph">The graph as an adjacency list</param>
        /// <param name="start">The starting node</param>
        /// <param name="end">The ending node</param>
        /// <returns>List of nodes in the path, or empty list if no path exists</returns>
        private List<string> FindShortestPath(Dictionary<string, Dictionary<string, double>> graph, string start, string end)
        {
            var distances = new Dictionary<string, double>();
            var previous = new Dictionary<string, string>();
            var nodes = new List<string>();

            // Initialize distances and collect all nodes, including those only in edges
            var allNodeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in graph.Keys)
            {
                allNodeNames.Add(node);
            }
            // Add nodes that are only destinations (not sources)
            foreach (var sourceNode in graph.Values)
            {
                foreach (var destNode in sourceNode.Keys)
                {
                    allNodeNames.Add(destNode);
                }
            }

            // Now set up distances using the complete node list
            foreach (var node in allNodeNames)
            {
                if (node.Equals(start, StringComparison.OrdinalIgnoreCase))
                {
                    distances[node] = 0;
                }
                else
                {
                    distances[node] = double.MaxValue;
                }

                nodes.Add(node);
            }

            while (nodes.Count > 0)
            {
                // Find the node with the smallest distance
                string smallest = null;
                foreach (var node in nodes)
                {
                    if (smallest == null || distances[node] < distances[smallest])
                    {
                        smallest = node;
                    }
                }

                if (smallest == null || distances[smallest] == double.MaxValue)
                {
                    break;
                }

                // Remove the smallest node from the unvisited set
                nodes.Remove(smallest);

                // If we reached the end node, build the path and return it
                if (smallest.Equals(end, StringComparison.OrdinalIgnoreCase))
                {
                    var path = new List<string>();
                    while (previous.ContainsKey(smallest))
                    {
                        path.Add(smallest);
                        smallest = previous[smallest];
                    }
                    path.Add(smallest); // Add the start node
                    path.Reverse();
                    return path;
                }

                // Update distances to adjacent nodes
                if (graph.ContainsKey(smallest))
                {
                    foreach (var neighbor in graph[smallest])
                    {
                        var alt = distances[smallest] + neighbor.Value;
                        if (alt < distances[neighbor.Key])
                        {
                            distances[neighbor.Key] = alt;
                            previous[neighbor.Key] = smallest;
                        }
                    }
                }
            }

            // No path found
            return new List<string>();
        }
    }

    /// <summary>
    /// Root structure of the WorkingGraphs.json file
    /// </summary>
    public class GraphDataRoot
    {
        public Dictionary<string, Graph> Graphs { get; set; } = new Dictionary<string, Graph>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Contains the graph data loaded from the WorkingGraphs.json file
    /// </summary>
    public class GraphData
    {
        public Dictionary<string, Graph> Graphs { get; set; } = new Dictionary<string, Graph>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Represents a graph with nodes and edges
    /// </summary>
    public class Graph
    {
        public List<string> Nodes { get; set; } = new List<string>();
        public List<Edge> Edges { get; set; } = new List<Edge>();
    }

    /// <summary>
    /// Represents an edge in a graph
    /// </summary>
    public class Edge
    {
        public string From { get; set; }
        public string To { get; set; }
        public double Weight { get; set; }
    }

}