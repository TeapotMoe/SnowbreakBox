using System.Diagnostics;
using System.IO;

namespace SnowbreakBox.Core {
	internal class Utils {
		// 解析 json，不值得引入新的库
		public static string ReadStringFromJson(string json, string key) {
			int idx = json.IndexOf('\"' + key + '\"');
			if (idx < 0) {
				return null;
			}

			json = json.Substring(idx + key.Length + 2).TrimStart();
			if (json.Length == 0 || json[0] != ':') {
				return null;
			}

			json = json.Substring(1).TrimStart();
			if (json.Length == 0 || json[0] != '\"') {
				return null;
			}

			json = json.Substring(1);
			int strEnd = json.IndexOf('\"');
			if (strEnd < 0) {
				return null;
			}

			return json.Substring(0, strEnd);
		}

		public static void CopyDirectory(string sourceDir, string destinationDir) {
			Directory.CreateDirectory(destinationDir);

			// 复制文件
			foreach (string file in Directory.GetFiles(sourceDir)) {
				string destFile = Path.Combine(destinationDir, Path.GetFileName(file));
				File.Copy(file, destFile);
			}

			// 递归复制子文件夹
			foreach (string dir in Directory.GetDirectories(sourceDir)) {
				string destDir = Path.Combine(destinationDir, Path.GetFileName(dir));
				CopyDirectory(dir, destDir);
			}
		}

		public static void LaunchExe(string exePath, string arguments = "") {
			// 委托 shell 启动可执行文件，工作目录为程序所在目录
			Process.Start(new ProcessStartInfo {
				WorkingDirectory = Path.GetDirectoryName(exePath),
				FileName = Path.GetFileName(exePath),
				Arguments = arguments,
				UseShellExecute = true
			});
		}
	}
}
