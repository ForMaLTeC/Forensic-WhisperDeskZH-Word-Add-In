using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ForensicWhisperDeskZH.Audio
{
    public static class NativeDllManager
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr LoadLibrary(string dllToLoad);

        static NativeDllManager()
        {
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
        }

        public static void InitializeNativeLibraries()
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            string assemblyDirectory = Path.GetDirectoryName(assemblyPath);

            // Try to load from different locations
            var possiblePaths = new[]
            {
                Path.Combine(assemblyDirectory, "WebRtcVad.dll"), 
                Path.Combine(assemblyDirectory, "x86", "WebRtcVad.dll"),
                Path.Combine(assemblyDirectory, "x64", "WebRtcVad.dll")
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path) && LoadLibrary(path) != IntPtr.Zero)
                {
                    System.Diagnostics.Debug.WriteLine($"Successfully loaded WebRtcVad.dll from {path}");
                    break;
                }
            }
        }

        private static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            if (!args.Name.StartsWith("WebRtcVad"))
                return null;

            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            string assemblyDirectory = Path.GetDirectoryName(assemblyPath);
            string dllPath = Path.Combine(assemblyDirectory, "WebRtcVad.dll");

            if (File.Exists(dllPath))
            {
                return Assembly.LoadFrom(dllPath);
            }

            return null;
        }
    }
}