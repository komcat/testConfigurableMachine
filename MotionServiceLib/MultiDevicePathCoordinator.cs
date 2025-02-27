using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace MotionServiceLib
{
    /// <summary>
    /// Coordinates path movements across multiple devices
    /// </summary>
    public class MultiDevicePathCoordinator
    {
        private readonly MotionKernel _motionKernel;
        private readonly ILogger _logger;
        private CancellationTokenSource _cancellationTokenSource;

        /// <summary>
        /// Creates a new instance of the MultiDevicePathCoordinator
        /// </summary>
        /// <param name="motionKernel">Reference to the MotionKernel instance</param>
        public MultiDevicePathCoordinator(MotionKernel motionKernel)
        {
            _motionKernel = motionKernel ?? throw new ArgumentNullException(nameof(motionKernel));
            _logger = Log.ForContext<MultiDevicePathCoordinator>();
        }

        /// <summary>
        /// Executes path movements for multiple devices in parallel
        /// </summary>
        /// <param name="devicePaths">Dictionary mapping device IDs to their respective paths</param>
        /// <returns>Dictionary mapping device IDs to their movement success status</returns>
        public async Task<Dictionary<string, bool>> ExecuteParallelPathsAsync(
            Dictionary<string, List<string>> devicePaths)
        {
            if (devicePaths == null || devicePaths.Count == 0)
            {
                _logger.Warning("No device paths provided for parallel execution");
                return new Dictionary<string, bool>();
            }

            _logger.Information("Starting parallel path execution for {DeviceCount} devices",
                devicePaths.Count);

            // Initialize the cancellation token source
            _cancellationTokenSource = new CancellationTokenSource();

            // Track results
            var results = new Dictionary<string, bool>();

            try
            {
                // Create a task for each device
                var tasks = new Dictionary<string, Task<bool>>();
                foreach (var devicePath in devicePaths)
                {
                    string deviceId = devicePath.Key;
                    List<string> path = devicePath.Value;

                    _logger.Information("Adding path for device {DeviceId}: {Path}",
                        deviceId, string.Join(" -> ", path));

                    // Create a task for this device's path
                    tasks[deviceId] = _motionKernel.MoveAlongPathAsync(
                        deviceId, path, _cancellationTokenSource.Token);
                }

                // Wait for all tasks to complete
                await Task.WhenAll(tasks.Values);

                // Collect results
                foreach (var task in tasks)
                {
                    results[task.Key] = task.Value.Result;
                    _logger.Information("Device {DeviceId} path execution result: {Result}",
                        task.Key, task.Value.Result);
                }

                return results;
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("Parallel path execution was cancelled");

                // Mark all remaining devices as failed
                foreach (var deviceId in devicePaths.Keys)
                {
                    if (!results.ContainsKey(deviceId))
                    {
                        results[deviceId] = false;
                    }
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during parallel path execution");

                // Mark all devices as failed
                foreach (var deviceId in devicePaths.Keys)
                {
                    results[deviceId] = false;
                }

                return results;
            }
        }

        /// <summary>
        /// Executes path movements for multiple devices sequentially (one after another)
        /// </summary>
        /// <param name="devicePaths">Ordered list of (device ID, path) pairs to execute in sequence</param>
        /// <returns>Dictionary mapping device IDs to their movement success status</returns>
        public async Task<Dictionary<string, bool>> ExecuteSequentialPathsAsync(
            List<(string DeviceId, List<string> Path)> devicePaths)
        {
            if (devicePaths == null || devicePaths.Count == 0)
            {
                _logger.Warning("No device paths provided for sequential execution");
                return new Dictionary<string, bool>();
            }

            _logger.Information("Starting sequential path execution for {DeviceCount} devices",
                devicePaths.Count);

            // Initialize the cancellation token source
            _cancellationTokenSource = new CancellationTokenSource();

            // Track results
            var results = new Dictionary<string, bool>();

            try
            {
                // Execute each device's path in sequence
                foreach (var (deviceId, path) in devicePaths)
                {
                    _logger.Information("Executing path for device {DeviceId}: {Path}",
                        deviceId, string.Join(" -> ", path));

                    // Move this device along its path
                    bool success = await _motionKernel.MoveAlongPathAsync(
                        deviceId, path, _cancellationTokenSource.Token);

                    results[deviceId] = success;
                    _logger.Information("Device {DeviceId} path execution result: {Result}",
                        deviceId, success);

                    // If this device failed, we might want to stop the sequence
                    if (!success)
                    {
                        _logger.Warning("Device {DeviceId} failed, stopping sequential execution",
                            deviceId);
                        break;
                    }
                }

                return results;
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("Sequential path execution was cancelled");
                return results;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during sequential path execution");
                return results;
            }
        }

        /// <summary>
        /// Executes a coordinated multi-device operation with synchronized waypoints
        /// </summary>
        /// <param name="coordinatedOperation">The coordinated operation definition</param>
        /// <returns>True if all steps completed successfully, false otherwise</returns>
        public async Task<bool> ExecuteCoordinatedOperationAsync(
            CoordinatedOperation coordinatedOperation)
        {
            if (coordinatedOperation == null || coordinatedOperation.Steps.Count == 0)
            {
                _logger.Warning("No steps provided for coordinated operation");
                return false;
            }

            _logger.Information("Starting coordinated operation with {StepCount} steps",
                coordinatedOperation.Steps.Count);

            // Initialize the cancellation token source
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                // Execute each step
                for (int i = 0; i < coordinatedOperation.Steps.Count; i++)
                {
                    var step = coordinatedOperation.Steps[i];
                    _logger.Information("Executing step {StepNumber}: {StepDescription}",
                        i + 1, step.Description);

                    bool stepSuccess = false;
                    switch (step.ExecutionType)
                    {
                        case StepExecutionType.Parallel:
                            // Execute all device movements in parallel
                            var parallelResults = await ExecuteParallelPathsAsync(step.DevicePaths);
                            stepSuccess = parallelResults.Values.All(result => result);
                            break;

                        case StepExecutionType.Sequential:
                            // Convert to ordered list for sequential execution
                            var orderedPaths = step.DevicePaths
                                .Select(kvp => (kvp.Key, kvp.Value))
                                .ToList();
                            var sequentialResults = await ExecuteSequentialPathsAsync(orderedPaths);
                            stepSuccess = sequentialResults.Values.All(result => result);
                            break;
                    }

                    if (!stepSuccess)
                    {
                        _logger.Warning("Step {StepNumber} failed, stopping coordinated operation", i + 1);

                        // Execute cleanup if specified
                        if (coordinatedOperation.OnFailure != null)
                        {
                            _logger.Information("Executing failure handling");
                            await coordinatedOperation.OnFailure(i, step);
                        }

                        return false;
                    }

                    // Call the step completion handler if provided
                    if (step.OnCompletion != null)
                    {
                        _logger.Information("Executing completion handler for step {StepNumber}", i + 1);
                        await step.OnCompletion();
                    }
                }

                _logger.Information("Coordinated operation completed successfully");
                return true;
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("Coordinated operation was cancelled");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during coordinated operation");
                return false;
            }
        }

        /// <summary>
        /// Cancels any ongoing coordinated operations
        /// </summary>
        public void CancelOperation()
        {
            _logger.Information("Cancelling coordinated operation");
            _cancellationTokenSource?.Cancel();
        }
    }

    /// <summary>
    /// Represents a coordinated operation with multiple steps
    /// </summary>
    public class CoordinatedOperation
    {
        /// <summary>
        /// Name of the operation
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// List of steps to execute in order
        /// </summary>
        public List<CoordinationStep> Steps { get; set; } = new List<CoordinationStep>();

        /// <summary>
        /// Action to execute if any step fails
        /// </summary>
        public Func<int, CoordinationStep, Task> OnFailure { get; set; }
    }

    /// <summary>
    /// Represents a single step in a coordinated operation
    /// </summary>
    public class CoordinationStep
    {
        /// <summary>
        /// Description of the step
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// How to execute the device movements in this step
        /// </summary>
        public StepExecutionType ExecutionType { get; set; } = StepExecutionType.Parallel;

        /// <summary>
        /// Mapping of device IDs to their paths for this step
        /// </summary>
        public Dictionary<string, List<string>> DevicePaths { get; set; } = new Dictionary<string, List<string>>();

        /// <summary>
        /// Action to execute after this step completes successfully
        /// </summary>
        public Func<Task> OnCompletion { get; set; }
    }

    /// <summary>
    /// Defines how device movements in a step should be executed
    /// </summary>
    public enum StepExecutionType
    {
        /// <summary>
        /// Execute all device movements simultaneously
        /// </summary>
        Parallel,

        /// <summary>
        /// Execute device movements one after another
        /// </summary>
        Sequential
    }
}