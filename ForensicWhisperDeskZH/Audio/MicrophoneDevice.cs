using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ForensicWhisperDeskZH.Audio
{
    /// <summary>
    /// Represents a microphone device with enhanced identification
    /// </summary>
    public class MicrophoneDevice
    {
        public int DeviceNumber { get; }
        public string Name { get; }
        public string DeviceId { get; }

        public MicrophoneDevice(int deviceNumber, string name) : this(deviceNumber, name, null)
        {
        }

        public MicrophoneDevice(int deviceNumber, string name, string deviceId)
        {
            DeviceNumber = deviceNumber;
            Name = name;
            DeviceId = deviceId;
        }

        /// <summary>
        /// Gets a display-friendly device name that includes the device number for clarity
        /// </summary>
        public string DisplayName => $"[{DeviceNumber}] {Name}";

        /// <summary>
        /// Determines if this device matches another device by ID or name
        /// </summary>
        public bool Matches(MicrophoneDevice other)
        {
            if (other == null) return false;
            
            // First try to match by device ID (most reliable)
            if (!string.IsNullOrEmpty(DeviceId) && !string.IsNullOrEmpty(other.DeviceId))
            {
                return string.Equals(DeviceId, other.DeviceId, StringComparison.OrdinalIgnoreCase);
            }
            
            // Fallback to name matching
            return string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);
        }

        public override string ToString()
        {
            return $"Device {DeviceNumber}: {Name} (ID: {DeviceId ?? "N/A"})";
        }
    }
}
