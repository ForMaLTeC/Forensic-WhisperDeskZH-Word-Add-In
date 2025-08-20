using NAudio.Wave;
using NAudio.CoreAudioApi;
using System;
using System.IO;
using System.Linq;

namespace ForensicWhisperDeskZH.Audio
{
    /// <summary>
    /// Diagnostic tool for testing audio capture functionality
    /// </summary>
    public static class AudioDiagnosticTool
    {
        /// <summary>
        /// Lists all available audio input devices using WASAPI
        /// </summary>
        public static void ListAudioDevices()
        {
            System.Diagnostics.Debug.WriteLine("=== Available WASAPI Audio Input Devices ===");
            
            try
            {
                var deviceEnumerator = new MMDeviceEnumerator();
                var devices = deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

                if (devices.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("No audio input devices found!");
                    return;
                }

                for (int i = 0; i < devices.Count; i++)
                {
                    try
                    {
                        var device = devices[i];
                        System.Diagnostics.Debug.WriteLine($"Device {i}: {device.FriendlyName}");
                        System.Diagnostics.Debug.WriteLine($"  ID: {device.ID}");
                        System.Diagnostics.Debug.WriteLine($"  State: {device.State}");
                        
                        // Try to get format information
                        try
                        {
                            using (var wasapi = new WasapiCapture(device))
                            {
                                System.Diagnostics.Debug.WriteLine($"  Format: {wasapi.WaveFormat}");
                            }
                        }
                        catch (Exception formatEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"  Format: Unable to determine - {formatEx.Message}");
                        }
                        
                        System.Diagnostics.Debug.WriteLine("");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Device {i}: Error reading capabilities - {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error enumerating devices: {ex.Message}");
            }
        }

        /// <summary>
        /// Tests basic audio capture from the specified device
        /// </summary>
        public static void TestAudioCapture(int deviceNumber = 0, int durationSeconds = 5)
        {
            System.Diagnostics.Debug.WriteLine($"=== Testing WASAPI Audio Capture from Device {deviceNumber} ===");

            try
            {
                using (var capture = new NAudioCapture(deviceNumber))
                {
                    int totalBytes = 0;
                    int nonSilentChunks = 0;

                    capture.AudioDataAvailable += (sender, e) =>
                    {
                        totalBytes += e.AudioData.Length;

                        // Check for non-silent audio
                        var audioData = e.AudioData.Span;
                        bool hasSound = false;

                        for (int i = 0; i < audioData.Length - 1; i += 2)
                        {
                            short sample = BitConverter.ToInt16(audioData.Slice(i, 2).ToArray(), 0);
                            if (Math.Abs(sample) > 50) // Lower threshold
                            {
                                hasSound = true;
                                break;
                            }
                        }

                        if (hasSound)
                        {
                            nonSilentChunks++;
                        }
                    };

                    capture.Error += (sender, e) =>
                    {
                        System.Diagnostics.Debug.WriteLine($"Audio capture error: {e.Exception.Message}");
                    };

                    System.Diagnostics.Debug.WriteLine("Starting capture...");
                    capture.StartCapture();

                    // Record for specified duration
                    System.Threading.Thread.Sleep(durationSeconds * 1000);

                    System.Diagnostics.Debug.WriteLine("Stopping capture...");
                    capture.StopCapture();

                    System.Diagnostics.Debug.WriteLine($"Test Results:");
                    System.Diagnostics.Debug.WriteLine($"  Total bytes captured: {totalBytes}");
                    System.Diagnostics.Debug.WriteLine($"  Non-silent chunks: {nonSilentChunks}");
                    System.Diagnostics.Debug.WriteLine($"  Bytes per second: {totalBytes / durationSeconds}");

                    if (totalBytes == 0)
                    {
                        System.Diagnostics.Debug.WriteLine("WARNING: No audio data captured!");
                    }
                    else if (nonSilentChunks == 0)
                    {
                        System.Diagnostics.Debug.WriteLine("WARNING: Only silence captured - check microphone!");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("SUCCESS: Audio capture is working!");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Test failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Saves a test WAV file to verify audio format compatibility
        /// </summary>
        public static void SaveTestWavFile(int deviceNumber = 0, string filePath = "test_audio.wav", int durationSeconds = 5)
        {
            System.Diagnostics.Debug.WriteLine($"=== Saving Test WAV File: {filePath} ===");

            try
            {
                var waveFormat = new WaveFormat(16000, 16, 1);
                using (var capture = new NAudioCapture(deviceNumber))
                using (var writer = new WaveFileWriter(filePath, waveFormat))
                {
                    capture.AudioDataAvailable += (sender, e) =>
                    {
                        writer.Write(e.AudioData.Span.ToArray(), 0, e.AudioData.Length);
                        System.Diagnostics.Debug.WriteLine($"Wrote {e.AudioData.Length} bytes to WAV file");
                    };

                    capture.StartCapture();
                    System.Threading.Thread.Sleep(durationSeconds * 1000);
                    capture.StopCapture();
                }

                var fileInfo = new FileInfo(filePath);
                System.Diagnostics.Debug.WriteLine($"WAV file created: {fileInfo.Length} bytes");

                if (fileInfo.Length < 1000)
                {
                    System.Diagnostics.Debug.WriteLine("WARNING: WAV file is very small - likely no audio captured");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save test WAV file: {ex.Message}");
            }
        }
    }
}