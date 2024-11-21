using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace QuickLauncher {
	internal class Program {
		static void Main(string[] args) {
			ProcessStartInfo startInfo = new ProcessStartInfo {
				WorkingDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location),
				FileName = "SnowbreakBox.exe",
				Arguments = "-q",
				UseShellExecute = true
			};
			Process.Start(startInfo);
		}
	}
}
