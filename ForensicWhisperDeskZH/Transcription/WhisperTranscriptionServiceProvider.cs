using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ForensicWhisperDeskZH.Audio;
using ForensicWhisperDeskZH.Utils;

namespace ForensicWhisperDeskZH.Transcription
{
    /// <summary>
    /// Provider for Whisper-based transcription services
    /// </summary>
    public class WhisperTranscriptionServiceProvider : ITranscriptionServiceProvider
    {
        /// <summary>
        /// Gets a list of available microphone devices using WASAPI (consistent with NAudioCapture)
        /// </summary>
        public List<MicrophoneDevice> GetAvailableMicrophones()
        {
            var devices = new List<MicrophoneDevice>();

            try
            {
                var deviceEnumerator = new MMDeviceEnumerator();
                var audioDevices = deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

                for (int i = 0; i < audioDevices.Count; i++)
                {
                    try
                    {
                        var device = audioDevices[i];
                        
                        // Create device with enhanced identification including device ID
                        var micDevice = new MicrophoneDevice(i, device.FriendlyName, device.ID);
                        devices.Add(micDevice);
                        
                        // Enhanced logging with device ID
                        LoggingService.LogMessage($"Found mic: {device.FriendlyName}\nIndex: {i}\nDevice ID: {device.ID}", "Get Microphones", true);
                        
                        // Special logging for Speechmike devices
                        if (device.FriendlyName.ToLower().Contains("speechmike"))
                        {
                            LoggingService.LogMessage($"Speechmike detected: {device.FriendlyName}\nIndex: {i}\nDevice ID: {device.ID}", "Speechmike Detection", true);
                        }
                        
                        // Debug logging to verify consistency
                        System.Diagnostics.Debug.WriteLine($"WASAPI Device {i}: {device.FriendlyName} (ID: {device.ID})");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error accessing WASAPI device {i}: {ex.Message}");
                        LoggingService.LogError($"Error accessing WASAPI device {i}", ex, "GetAvailableMicrophones");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error enumerating WASAPI devices: {ex.Message}");
                
                // Enhanced error message with troubleshooting guidance
                string userMessage = "Audio device enumeration failed. This may be due to:\n" +
                                   "• Missing or outdated audio drivers\n" +
                                   "• Insufficient permissions\n" +
                                   "• Audio devices in exclusive mode\n" +
                                   "• Professional audio hardware (like Speechmike3) requiring specific drivers\n\n" +
                                   "Please ensure your audio drivers are up to date and restart the application.";
                
                LoggingService.LogError("WASAPI enumeration failed. Cannot provide consistent device numbering.", ex, "GetAvailableMicrophones");
                throw new InvalidOperationException(userMessage, ex);
            }

            return devices;
        }

        /// <summary>
        /// Gets a specific device by its ID for more reliable identification
        /// </summary>
        public MicrophoneDevice GetDeviceById(string deviceId)
        {
            try
            {
                var devices = GetAvailableMicrophones();
                return devices.Find(d => string.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Error getting device by ID: {deviceId}", ex, "GetDeviceById");
                return null;
            }
        }

        /// <summary>
        /// Finds a device by name with fuzzy matching (useful for Speechmike variants)
        /// </summary>
        public MicrophoneDevice FindDeviceByName(string deviceName, bool fuzzyMatch = false)
        {
            try
            {
                var devices = GetAvailableMicrophones();
                
                // Exact match first
                var exactMatch = devices.Find(d => string.Equals(d.Name, deviceName, StringComparison.OrdinalIgnoreCase));
                if (exactMatch != null) return exactMatch;
                
                // Fuzzy match if requested
                if (fuzzyMatch)
                {
                    var fuzzyMatch = devices.Find(d => 
                        d.Name.ToLower().Contains(deviceName.ToLower()) || 
                        deviceName.ToLower().Contains(d.Name.ToLower()));
                    return fuzzyMatch;
                }
                
                return null;
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Error finding device by name: {deviceName}", ex, "FindDeviceByName");
                return null;
            }
        }

        /// <summary>
        /// Validates that a device number is still valid and corresponds to the expected device
        /// </summary>
        public bool ValidateDeviceSelection(int deviceNumber, string expectedDeviceId = null, string expectedDeviceName = null)
        {
            try
            {
                var devices = GetAvailableMicrophones();
                
                // Check if device number is in valid range
                if (deviceNumber < 0 || deviceNumber >= devices.Count)
                {
                    LoggingService.LogMessage($"Device number {deviceNumber} is out of range (0-{devices.Count - 1})", "Device Validation", true);
                    return false;
                }
                
                var device = devices[deviceNumber];
                
                // Validate against expected device ID if provided
                if (!string.IsNullOrEmpty(expectedDeviceId))
                {
                    if (!string.Equals(device.DeviceId, expectedDeviceId, StringComparison.OrdinalIgnoreCase))
                    {
                        LoggingService.LogMessage($"Device ID mismatch at index {deviceNumber}. Expected: {expectedDeviceId}, Found: {device.DeviceId}", "Device Validation", true);
                        return false;
                    }
                }
                
                // Validate against expected device name if provided
                if (!string.IsNullOrEmpty(expectedDeviceName))
                {
                    if (!string.Equals(device.Name, expectedDeviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        LoggingService.LogMessage($"Device name mismatch at index {deviceNumber}. Expected: {expectedDeviceName}, Found: {device.Name}", "Device Validation", true);
                        return false;
                    }
                }
                
                LoggingService.LogMessage($"Device validation successful for index {deviceNumber}: {device.Name}", "Device Validation", true);
                return true;
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"Error validating device selection {deviceNumber}", ex, "ValidateDeviceSelection");
                return false;
            }
        }

        /// <summary>
        /// Debug method to validate device consistency
        /// </summary>
        public void DebugDeviceConsistency()
        {
            try 
            {
                var devices = GetAvailableMicrophones();
                LoggingService.LogMessage($"=== DEVICE CONSISTENCY CHECK ===", "Debug", true);
                LoggingService.LogMessage($"Found {devices.Count} WASAPI devices:", "Debug", true);
                
                for (int i = 0; i < devices.Count; i++)
                {
                    var device = devices[i];
                    LoggingService.LogMessage($"Device {i}: {device.Name}\nID: {device.DeviceId}", "Debug", true);
                    
                    // Test if NAudioCapture can actually use this device
                    try 
                    {
                        using (var capture = new NAudioCapture(i))
                        {
                            LoggingService.LogMessage($"Device {i} - NAudioCapture initialization: SUCCESS", "Debug", true);
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogMessage($"Device {i} - NAudioCapture initialization: FAILED - {ex.Message}", "Debug", true);
                    }
                }
                LoggingService.LogMessage($"=== END CONSISTENCY CHECK ===", "Debug", true);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Debug device consistency failed", ex, "DebugDeviceConsistency");
            }
        }

        /// <summary>
        /// Creates a new transcription service with the specified settings
        /// </summary>
        public async Task<ITranscriptionService> CreateTranscriptionServiceAsync(TranscriptionSettings settings, EventHandler<bool> dictiationStateChanged)
        {
            // Create and return the service on a background thread to satisfy async requirements
            return await Task.Run(() => new TranscriptionService(settings, dictiationStateChanged));
        }
    }
}