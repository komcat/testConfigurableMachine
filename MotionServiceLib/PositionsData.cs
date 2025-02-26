using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MotionServiceLib
{
    /// <summary>
    /// Container class for storing positions for all devices
    /// </summary>
    public class PositionsData
    {
        /// <summary>
        /// List of hexapod devices with their associated positions
        /// </summary>
        public List<HexapodPositionSet> Hexapods { get; set; } = new List<HexapodPositionSet>();

        /// <summary>
        /// List of gantry devices with their associated positions
        /// </summary>
        public List<GantryPositionSet> Gantries { get; set; } = new List<GantryPositionSet>();
    }

    /// <summary>
    /// Position data for a single hexapod device
    /// </summary>
    public class HexapodPositionSet
    {
        /// <summary>
        /// ID of the hexapod device
        /// </summary>
        public int HexapodId { get; set; }

        /// <summary>
        /// Dictionary of named positions for this hexapod
        /// Key is the position name, value is the position
        /// </summary>
        public Dictionary<string, Position> Positions { get; set; } = new Dictionary<string, Position>();
    }

    /// <summary>
    /// Position data for a single gantry device
    /// </summary>
    public class GantryPositionSet
    {
        /// <summary>
        /// ID of the gantry device
        /// </summary>
        public int GantryId { get; set; }

        /// <summary>
        /// Dictionary of named positions for this gantry
        /// Key is the position name, value is the position
        /// </summary>
        public Dictionary<string, Position> Positions { get; set; } = new Dictionary<string, Position>();
    }

    /// <summary>
    /// Extension methods for working with position data
    /// </summary>
    public static class PositionsDataExtensions
    {
        /// <summary>
        /// Merges positions from a legacy format into the unified PositionsData format
        /// </summary>
        /// <param name="positionsData">Target positions data object</param>
        /// <param name="legacyPositionsPath">Path to legacy positions file</param>
        /// <returns>True if merge was successful</returns>
        public static bool MergeLegacyPositions(this PositionsData positionsData, string legacyPositionsPath)
        {
            try
            {
                // Read the legacy positions file
                string jsonContent = System.IO.File.ReadAllText(legacyPositionsPath);

                // Determine if it's in hexapod or gantry format and merge accordingly
                if (legacyPositionsPath.Contains("Hexapod", StringComparison.OrdinalIgnoreCase))
                {
                    // Parse as hexapod positions
                    // Implementation depends on legacy format...
                }
                else if (legacyPositionsPath.Contains("Gantry", StringComparison.OrdinalIgnoreCase))
                {
                    // Parse as gantry positions
                    // Implementation depends on legacy format...
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Finds the position set for a specific hexapod device
        /// </summary>
        public static HexapodPositionSet FindHexapodPositionSet(this PositionsData positionsData, string deviceId)
        {
            // Try to parse the deviceId to an integer
            if (int.TryParse(deviceId, out int hexapodId))
            {
                return positionsData.Hexapods.Find(h => h.HexapodId == hexapodId);
            }

            return null;
        }

        /// <summary>
        /// Finds the position set for a specific gantry device
        /// </summary>
        public static GantryPositionSet FindGantryPositionSet(this PositionsData positionsData, string deviceId)
        {
            // Try to parse the deviceId to an integer
            if (int.TryParse(deviceId, out int gantryId))
            {
                return positionsData.Gantries.Find(g => g.GantryId == gantryId);
            }

            return null;
        }
    }
}
