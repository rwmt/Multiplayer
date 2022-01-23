using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using UnityEngine;
using Verse;

namespace Multiplayer.Client.Util
{
    // From HugsLib

    /// <summary>
    /// A command to open a directory in the systems default file explorer.
    /// Since Unity's OpenUrl() is broken on OS X, we can use a shell to do it correctly.
    /// </summary>
    public static class ShellOpenDirectory {
        public static bool Execute(string directory) {
            var directoryPath = ParsePath(directory);
            if (string.IsNullOrEmpty(directoryPath)) {
                return false;
            }
            if (GetCurrentPlatform() == PlatformType.MacOSX) {
                return Shell.StartProcess(new Shell.ShellCommand {FileName = "open", Args = directory});
            }

            Application.OpenURL(directoryPath);
            return false;
        }

        private static string ParsePath(string path) {
            if (path.StartsWith(@"~\") || path.StartsWith(@"~/"))
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), path.Remove(0, 2));
            if (!path.StartsWith("\"") && !path.EndsWith("\""))
                return SurroundWithDoubleQuotes(path);
            return path;
        }

        /// <summary>
        /// Adds double quotes to the start and end of a string.
        /// </summary>
        private static string SurroundWithDoubleQuotes(this string text) {
            return string.Format("\"{0}\"", text);
        }

        private static PlatformType GetCurrentPlatform() {
            // Will need changing if another platform is supported by RimWorld in the future
            if (UnityData.platform == RuntimePlatform.OSXPlayer ||
                UnityData.platform == RuntimePlatform.OSXEditor)
                return PlatformType.MacOSX;
            else if (UnityData.platform == RuntimePlatform.WindowsPlayer ||
                     UnityData.platform == RuntimePlatform.WindowsEditor)
                return PlatformType.Windows;
            else if (UnityData.platform == RuntimePlatform.LinuxPlayer)
                return PlatformType.Linux;
            else
                return PlatformType.Unknown;
        }

        private enum PlatformType { Linux, MacOSX, Windows, Unknown }
    }

    /// <summary>
	/// Commands start a new process on the target machine using platform specific commands and args to pass to the shell.
	/// Refer to the Microsoft documentation for dotNet 3.5 for more info on a process.
	/// https://msdn.microsoft.com/en-us/library/system.diagnostics.process(v=vs.90).aspx
	/// </summary>
	public static class Shell {
		public static bool StartProcess(ShellCommand shellCommand) {
			var process = new Process();
			return StartProcess(new ProcessStartInfo {
				CreateNoWindow = true,
				FileName = shellCommand.FileName,
				Arguments = shellCommand.Args
			}, ref process);
		}

		public static bool StartProcess(ProcessStartInfo psi, ref Process process) {
			if (process == null) {
				process = new Process();
			}

			if (psi == null) {
				return false;
			}

			try {
                process.StartInfo = psi;
				process.Start();
				return true;
			} catch (Win32Exception e) {
            } catch (Exception e) {
            }
			return false;
		}

		[Serializable]
		public class UnsupportedPlatformException : Exception {
			private static string ExpandCommandName(string commandName) {
				return string.Format("{0} is not compatible with {1}", commandName, UnityData.platform);
			}
			public UnsupportedPlatformException(string commandName) : base(ExpandCommandName(commandName)) { }
			public UnsupportedPlatformException(string commandName, Exception inner) : base(ExpandCommandName(commandName), inner) { }
			protected UnsupportedPlatformException(SerializationInfo info, StreamingContext context) : base(info, context) { }
		}

		public class ShellCommand {
			public string FileName { get; set; }
			public string Args { get; set; }
		}
	}
}
