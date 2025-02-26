using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MotionServiceLib
{
    /// <summary>
    /// Extension methods for the MotionKernel class
    /// </summary>
    public static class MotionKernelExtensions
    {
        /// <summary>
        /// Gets a list of all connected devices
        /// </summary>
        /// <param name="kernel">The motion kernel instance</param>
        /// <returns>List of connected devices</returns>
        public static List<MotionDevice> GetConnectedDevices(this MotionKernel kernel)
        {
            // Get connected devices from the MotionKernel
            return kernel.GetDevices().Where(d => kernel.IsDeviceConnected(d.Id)).ToList();
        }

        /// <summary>
        /// Checks if a device is connected
        /// </summary>
        /// <param name="kernel">The motion kernel instance</param>
        /// <param name="deviceId">The device ID to check</param>
        /// <returns>True if the device is connected, false otherwise</returns>
        public static bool IsDeviceConnected(this MotionKernel kernel, string deviceId)
        {
            // This is a helper method that can be implemented in the MotionKernel class
            // For now, we'll use a simple check to see if the device has a controller
            return kernel.HasControllerForDevice(deviceId);
        }
    }
}
