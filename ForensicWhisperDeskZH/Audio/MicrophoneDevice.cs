using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ForensicWhisperDeskZH.Audio
{

    /// <summary>
    /// Represents a microphone device
    /// </summary>
    public class MicrophoneDevice
    {
        public int DeviceNumber { get; }
        public string Name { get; }

        public MicrophoneDevice(int deviceNumber, string name)
        {
            DeviceNumber = deviceNumber;
            Name = name;
        }
    }
}
