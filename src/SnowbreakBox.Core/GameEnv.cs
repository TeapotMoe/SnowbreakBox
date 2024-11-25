using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SnowbreakBox.Core {
    public class GameEnv : INotifyPropertyChanged {
		private static readonly string ENGINE_INI_PATH = "Config\\WindowsNoEditor\\Engine.ini";
		private static readonly string GAME_INI_PATH = "Config\\WindowsNoEditor\\Game.ini";

		private readonly string _launcherPath;
		private string _savedFolder;
		private readonly Version _gameVersion;
		private readonly HttpClient _httpClient = new HttpClient();
		private Task<Version> _fetchRemoteGameVersionTask;

		private enum LauncherType {
			Classic,
			// 西山居启动器在 v1.7.7 更改了游戏启动参数
			Seasun,
			SeasunOld
		}

		private readonly LauncherType _launcherType;

		public string GameFolder { get; private set; }

		private bool _isSavedPathStandard = false;
		public bool IsSavedPathStandard {
			get => _isSavedPathStandard;
			private set {
				if (_isSavedPathStandard != value) {
					_isSavedPathStandard = value;
					OnPropertyChanged(nameof(IsSavedPathStandard));
				}
			}
		}

		public bool IsCensorDisabled {
			get {
				try {
					using (StreamReader sr = new StreamReader(Path.Combine(GameFolder, "localization.txt"))) {
						string line = sr.ReadLine();
						var pair = line.Split('=');
						if (pair.Length != 2) {
							return false;
						}

						return pair[0].Trim() == "localization" && pair[1].Trim() == "1";
					}
				} catch {
					return false;
				}
			}
			set {
				File.WriteAllText(Path.Combine(GameFolder, "localization.txt"), value ? "localization = 1" : "localization = 0");
			}
		}

		public int GraphicState {
			get {
				// 游戏启动时会修改 Engine.ini，删除注释并重新排列条目，很难根据 Engine.ini
				// 判断画质等级。因此我们将画质等级保存在单独的文件里。
				string engineIniPath = Path.Combine(_savedFolder, ENGINE_INI_PATH);
				string storagePath = engineIniPath + ".box";
				if (!File.Exists(storagePath)) {
					return 0;
				}

				try {
					byte[] bytes = File.ReadAllBytes(storagePath);
					if (bytes.Length == 4) {
						int value = BitConverter.ToInt32(bytes, 0);

						// 简单验证 Engine.ini 有没有应用补丁
						if (File.ReadAllText(engineIniPath).IndexOf("[SystemSettings]") == -1) {
							// Engine.ini 和存储的值不一致
							File.Delete(storagePath);
							return 0;
						} else {
							return value;
						}
					}
				} catch { }

				return 0;
			}
			set {
				string engineIniPath = Path.Combine(_savedFolder, ENGINE_INI_PATH);
				string storagePath = engineIniPath + ".box";

				// 无需保留原始文件的内容，游戏启动时会自动添加默认条目
				string iniText = value > 0 ?
					Properties.Resources.ResourceManager.GetString("Profile" + value) : string.Empty;
				File.WriteAllText(engineIniPath, iniText);

				if (value == 0 && File.Exists(storagePath)) {
					File.Delete(storagePath);
					return;
				}

				File.WriteAllBytes(storagePath, BitConverter.GetBytes(value));
			}
		}

		private static readonly string SPLASH_SCREEN_SECTION = "Distribution";
		private static readonly string SPLASH_SCREEN_KEY = "SplashScreen";
		public bool IsSplashScreenDisabled {
			get {
				string gameIniPath = Path.Combine(_savedFolder, GAME_INI_PATH);
				IniFile iniFile = new IniFile(gameIniPath);
				string value = iniFile.Read(SPLASH_SCREEN_KEY, SPLASH_SCREEN_SECTION);
				return value.ToLower() == "false";
			}
			set {
				string gameIniPath = Path.Combine(_savedFolder, GAME_INI_PATH);
				IniFile iniFile = new IniFile(gameIniPath);
				if (value) {
					iniFile.Write(SPLASH_SCREEN_KEY, "False", SPLASH_SCREEN_SECTION);
				} else {
					iniFile.DeleteKey(SPLASH_SCREEN_KEY, SPLASH_SCREEN_SECTION);
				}
			}
		}

		private bool _gameHasUpdate = false;
		public bool GameHasUpdate {
			get => _gameHasUpdate;
			private set {
				if (_gameHasUpdate != value) {
					_gameHasUpdate = value;
					OnPropertyChanged(nameof(GameHasUpdate));
				}
			}
		}

		private void WaitForCheckingGameUpdate() {
			if (_fetchRemoteGameVersionTask == null || _gameVersion == null) {
				return;
			}

			Version remoteVersion = _fetchRemoteGameVersionTask.Result;
			_fetchRemoteGameVersionTask = null;
			if (remoteVersion == null) {
				return;
			}

			GameHasUpdate = remoteVersion > _gameVersion;
		}

		// saveFolderType取值
		// 0: 其他
		// 1: 标准位置 (GameFolder\game\Saved)
		// 2: 旧版西山居启动器位置(没有 game 中间目录)
		private bool FindGame(
			string launcherRegKey,
			string gameRegKey,
			bool isSeasun,
			out string launcherPath,
			out string gameFolder,
			out string savedFolder,
			out int saveFolderType
		) {
			gameFolder = null;
			savedFolder = null;
			saveFolderType = 0;

			try {
				launcherPath = Registry.GetValue(launcherRegKey, "DisplayIcon", null) as string;
				if (!File.Exists(launcherPath)) {
					launcherPath = null;
					return false;
				}

				launcherPath = launcherPath.Replace('/', '\\');
			} catch {
				launcherPath = null;
				return false;
			}

			try {
				gameFolder = Registry.GetValue(gameRegKey, "InstallPath", null) as string;
				if (!File.Exists(Path.Combine(gameFolder, "game\\Game\\Binaries\\Win64\\game.exe"))) {
					gameFolder = null;
					return false;
				}

				// 西山居启动器保存的路径包含正斜杠
				gameFolder = gameFolder.Replace('/', '\\');
			} catch {
				gameFolder = null;
				return false;
			}

			try {
				// 检查存档，可能位于游戏文件夹内或 %LocalAppData%，应优先检查游戏文件夹

				// 优先使用标准位置
				string engineIniPath = Path.Combine(gameFolder, "game\\Saved", ENGINE_INI_PATH);
				if (File.Exists(engineIniPath)) {
					saveFolderType = 1;
					savedFolder = Path.Combine(gameFolder, "game\\Saved");
					return true;
				}

				if (isSeasun) {
					// 旧版西山居启动器没有 game 中间目录
					engineIniPath = Path.Combine(gameFolder, "Saved", ENGINE_INI_PATH);
					if (File.Exists(engineIniPath)) {
						saveFolderType = 2;
						savedFolder = Path.Combine(gameFolder, "Saved");
						return true;
					}

					// 西山居启动器 v1.7.7 存在 bug，游戏路径中如果存在空格会被截断，比如如果游戏安装在
					// C:\Program Files\Snow，存档会被保存在 C:\Program\Saved。
					int spaceIdx = gameFolder.IndexOf(' ');
					string truncatedPath = spaceIdx == -1 ? gameFolder : gameFolder.Substring(0, spaceIdx);
					engineIniPath = Path.Combine(truncatedPath, "Saved", ENGINE_INI_PATH);
					if (File.Exists(engineIniPath)) {
						savedFolder = Path.Combine(truncatedPath, "Saved");
						return true;
					}
				}

				// 检查 %LocalAppData%，如果游戏路径中如果存在空格，经典启动器会把存档保存在这里
				string localAppDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
				engineIniPath = Path.Combine(localAppDataFolder, "Game\\Saved", ENGINE_INI_PATH);
				if (File.Exists(engineIniPath)) {
					savedFolder = Path.Combine(localAppDataFolder, "Game\\Saved");
					return true;
				}
			} catch { }

			savedFolder = null;
			return false;
		}

		// 解析 json，不值得引入新的库
		private string ReadStringFromJson(string json, string key) {
			int idx = json.IndexOf('\"' + key + '\"');
			if (idx < 0) {
				return null;
			}

			json = json.Substring(idx + key.Length + 2);
			json.TrimStart();
			if (json.Length == 0 || json[0] != ':') {
				return null;
			}

			json = json.Substring(1);
			json.TrimStart();
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

		private async Task<Version> FetchRemoteGameVersion() {
			try {
				string response = await _httpClient.GetStringAsync(
					"https://cbjq-client.xoyocdn.com/games/cbjq/SyncEntrys/cbjq_SyncEntry.json")
					.ConfigureAwait(false);
				string versionStr = ReadStringFromJson(response, "GameVersion");
				if (versionStr != null && Version.TryParse(versionStr, out var version)) {
					return version;
				}
			} catch { }

			return null;
		}

		public GameEnv() {
			if (NativeMethods.FindWindow("UnrealWindow", "尘白禁区") != IntPtr.Zero) {
				throw new Exception("游戏正在运行！");
			}

			// 检测经典启动器
			bool classicDetected = FindGame(
				"HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\ProjectSnow",
				"HKEY_LOCAL_MACHINE\\SOFTWARE\\Kingsoft\\cbjq",
				false,
				out string classicLauncherPath,
				out string classicGameFolder,
				out string classicSavedFolder,
				out int clssicSavedFolderType
			);
			// 检测西山居启动器
			bool seasunDetected = FindGame(
				"HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\SeasunGameCBJQos",
				"HKEY_LOCAL_MACHINE\\SOFTWARE\\Kingsoft\\SeasunGameSSG\\cbjq",
				true,
				out string seasunLauncherPath,
				out string seasunGameFolder,
				out string seasunSavedFolder,
				out int seasunSavedFolderType
			);

			if (classicDetected && seasunDetected) {
				// 检测到两个启动器，应选择最近被使用的那个。比较上次访问时间是不可靠的，因此我们比较
				// Engine.ini 的上次被修改时间，因为游戏每次启动时都会修改这个文件。
				string classicEngineIniPath = Path.Combine(classicSavedFolder, ENGINE_INI_PATH);
				string seasunEngineIniPath = Path.Combine(seasunSavedFolder, ENGINE_INI_PATH);
				if (File.GetLastWriteTime(classicEngineIniPath) < File.GetLastWriteTime(seasunEngineIniPath)) {
					classicDetected = false;
				} else {
					seasunDetected = false;
				}
			} else if (!classicDetected && !seasunDetected) {
				// 0: 未安装启动器
				// 1: 未安装游戏
				// 2: 未找到存档
				int classicProgress = classicLauncherPath == null ? 0 : (classicGameFolder == null ? 1 : 2);
				int seasunProgress = seasunLauncherPath == null ? 0 : (seasunGameFolder == null ? 1 : 2);
				int maxProgress = Math.Max(classicProgress, seasunProgress);

				string[] errorMsgs = { "未找到启动器！", "未找到游戏，请先在启动器中安装游戏！", "未找到游戏存档，请先启动一次游戏！" };
				throw new Exception(errorMsgs[maxProgress]);
			}

			if (classicDetected) {
				_launcherType = LauncherType.Classic;
				IsSavedPathStandard = clssicSavedFolderType == 1;

				_launcherPath = classicLauncherPath;
				GameFolder = classicGameFolder;
				_savedFolder = classicSavedFolder;

				// 读取游戏版本
				try {
					// 读取 manifest.json 而不是 version.cfg，启动器使用前者对比本地与云端的文件变更
					string json = File.ReadAllText(Path.Combine(GameFolder, "manifest.json"));
					string versionStr = ReadStringFromJson(json, "version");
					if (versionStr == "--") {
						// 存在更新时经典启动器将 version 字段改为“--”
						_gameVersion = new Version();
					} else if (versionStr == null || !Version.TryParse(versionStr, out _gameVersion)) {
						_gameVersion = null;
					}
				} catch {
					_gameVersion = null;
				}
			} else {
				_launcherType = seasunSavedFolderType == 2 ? LauncherType.SeasunOld : LauncherType.Seasun;
				IsSavedPathStandard = seasunSavedFolderType == 1;

				_launcherPath = seasunLauncherPath;
				GameFolder = seasunGameFolder;
				_savedFolder = seasunSavedFolder;

				// 读取游戏版本
				string versionManagerIniPath = Path.Combine(GameFolder, "Temp\\VersionManager.ini");
				IniFile iniFile = new IniFile(versionManagerIniPath);
				string versionStr = iniFile.Read("GameVersion", "cbjq");
				if (versionStr == null || !Version.TryParse(versionStr, out _gameVersion)) {
					_gameVersion = null;
				}
			}

			_fetchRemoteGameVersionTask = FetchRemoteGameVersion();

			// 请求完成后在主线程更新 GameHasUpdate
			try {
				_fetchRemoteGameVersionTask.ContinueWith(_ => {
					WaitForCheckingGameUpdate();
				}, TaskScheduler.FromCurrentSynchronizationContext());
			} catch {
				// 忽略错误，QuickLauncher 不支持这个操作
			}
		}

		public void LaunchGameOrLauncher() {
			WaitForCheckingGameUpdate();

			if (GameHasUpdate) {
				// 游戏需要更新，启动官方启动器
				Process.Start(new ProcessStartInfo {
					WorkingDirectory = Path.GetDirectoryName(_launcherPath),
					FileName = Path.GetFileName(_launcherPath),
					UseShellExecute = true
				});
				return;
			}

			string arguments = "-FeatureLevelES31 -ChannelID=jinshan ";

			// 两个启动器传递路径的做法都是错的，这导致了存档路径的混乱。为了使用现有存档，我们只能将错就错。
			if (IsSavedPathStandard) {
				// 使用标准存档路径
				arguments += "\"-userdir=";
				arguments += Path.Combine(GameFolder, "game");
				arguments += '\"';
			} else if (_launcherType == LauncherType.Classic) {
				// 经典启动器在路径两边加反斜杠和双引号
				arguments += "\"-userdir=\\\"";
				arguments += Path.Combine(GameFolder, "game");
				arguments += "\\\"\"";
			} else if (_launcherType == LauncherType.Seasun) {
				// 西山居启动器不加双引号
				arguments += "-userdir=";
				arguments += Path.Combine(GameFolder, "game");
			} else {
				// 旧版西山居启动器在路径两侧加双引号，但没有 game 中间目录
				arguments += "-userdir=\"";
				arguments += GameFolder;
				arguments += '\"';
			}

			// 工作目录为程序所在目录
			Process.Start(new ProcessStartInfo {
				WorkingDirectory = Path.Combine(GameFolder, "game\\Game\\Binaries\\Win64"),
				FileName = "game.exe",
				Arguments = arguments,
				UseShellExecute = true
			});
		}

		static void CopyDirectory(string sourceDir, string destinationDir) {
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

		public void FixSavedPath() {
			string standardSavedPath = Path.Combine(GameFolder, "game\\Saved");

			if (Directory.Exists(standardSavedPath)) {
				Directory.Delete(standardSavedPath, true);
			}

			if (string.Compare(Path.GetPathRoot(standardSavedPath),
				Path.GetPathRoot(_savedFolder), StringComparison.OrdinalIgnoreCase) == 0) {
				// 位于同一个驱动器上可以移动
				Directory.Move(_savedFolder, standardSavedPath);
			} else {
				// 复制而不是移动，一来跨驱动不存在移动的概念，二来存档文件被占用时不会造成破坏
				CopyDirectory(_savedFolder, standardSavedPath);
				Directory.Delete(_savedFolder, true);
			}

			try {
				// 删除旧存档路径上的所有空文件夹
				while (true) {
					_savedFolder = Path.GetDirectoryName(_savedFolder);
					// 如果文件夹不为空会抛出异常
					Directory.Delete(_savedFolder);
				}
			} catch { }

			_savedFolder = standardSavedPath;
			IsSavedPathStandard = true;
		}

		public event PropertyChangedEventHandler PropertyChanged;

		protected virtual void OnPropertyChanged(string propertyName) {
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}
}
