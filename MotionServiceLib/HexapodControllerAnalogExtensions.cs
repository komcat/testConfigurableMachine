using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MotionServiceLib
{
    /// <summary>
    /// Add convenience methods for analog input to the HexapodController
    /// </summary>
    public static class HexapodControllerAnalogExtensions
    {
        private static readonly ILogger _logger = Log.ForContext(typeof(HexapodControllerAnalogExtensions));

        /// <summary>
        /// Gets a single analog voltage value from a specific channel
        /// </summary>
        /// <param name="controller">The HexapodController</param>
        /// <param name="channelId">The channel ID to read from</param>
        /// <returns>The voltage value, or double.NaN if operation failed</returns>
        public static async Task<double> GetAnalogVoltageAsync(this HexapodController controller, int channelId)
        {
            var values = await controller.GetAnalogVoltagesAsync(new[] { channelId });
            if (values != null && values.TryGetValue(channelId, out double value))
            {
                return value;
            }
            return double.NaN;
        }

        /// <summary>
        /// Convenience method to get specific analog channels for sensors
        /// </summary>
        /// <param name="controller">The HexapodController</param>
        /// <returns>Tuple containing (success, channel5Value, channel6Value)</returns>
        public static async Task<(bool success, double channel5, double channel6)> GetAnalogInputValuesAsync(this HexapodController controller)
        {
            int[] channels = { 5, 6 };
            var values = await controller.GetAnalogVoltagesAsync(channels);

            if (values != null &&
                values.TryGetValue(5, out double ch5) &&
                values.TryGetValue(6, out double ch6))
            {
                return (true, ch5, ch6);
            }

            return (false, 0, 0);
        }

        /// <summary>
        /// Compatibility method to match the original code signature
        /// </summary>
        /// <param name="controller">The HexapodController</param>
        /// <param name="ch5val">Output parameter for channel 5 value</param>
        /// <param name="ch6val">Output parameter for channel 6 value</param>
        public static void GetAnalogInputValue(this HexapodController controller, out double ch5val, out double ch6val)
        {
            Task<Dictionary<int, double>> task = controller.GetAnalogVoltagesAsync(new[] { 5, 6 });

            // Wait synchronously for the result
            task.Wait();
            var result = task.Result;

            if (result != null &&
                result.TryGetValue(5, out double ch5) &&
                result.TryGetValue(6, out double ch6))
            {
                ch5val = ch5;
                ch6val = ch6;
            }
            else
            {
                ch5val = 0;
                ch6val = 0;
            }
        }
    }
}