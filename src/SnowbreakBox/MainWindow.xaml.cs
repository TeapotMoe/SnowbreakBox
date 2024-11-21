using Microsoft.Win32;
using SnowbreakBox.Properties;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace SnowbreakBox {
	public partial class MainWindow : INotifyPropertyChanged {
		private static readonly string ENGINE_INI_PATH = "Config\\WindowsNoEditor\\Engine.ini";
		private static readonly string GAME_INI_PATH = "Config\\WindowsNoEditor\\Game.ini";
		
		private string _launcherPath;
		private string _savedFolder;
		private Version _gameVersion;
		private readonly HttpClient _httpClient = new HttpClient();
		private Task<Version> _fetchRemoteGameVersionTask;

		private enum LauncherType {
			Classic,
			// 西山居启动器在 v1.7.7 更改了游戏启动参数
			Seasun,
			SeasunOld
		}
		LauncherType _launcherType;

		public string GameFolder { get; set; }

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
				try {
					File.WriteAllText(Path.Combine(GameFolder, "localization.txt"), value ? "localization = 1" : "localization = 0");
				} catch (Exception ex) {
					ShowError(ex.Message);
				}
			}
		}

		public int GraphicState {
			get {
				// 游戏启动时会修改 Engine.ini，删除注释并重新排列条目，很难根据 Engine.ini
				// 判断画质等级。因此我们将画质等级保存为设置。
				int setting = Settings.Default.GraphicState;
				if (setting == 0) {
					return 0;
				}

				try {
					// 判断有没有画质补丁相对容易
					string engineIniPath = Path.Combine(_savedFolder, ENGINE_INI_PATH);
					using (StreamReader sr = new StreamReader(engineIniPath)) {
						string content = sr.ReadToEnd();
						if (content.IndexOf("[SystemSettings]") == -1) {
							return 0;
						} else {
							return setting;
						}
					}
				} catch {
					return 0;
				}
			}
			set {
				try {
					// 无需保留原始文件的内容，游戏启动时会自动添加默认条目
					string engineIniPath = Path.Combine(_savedFolder, ENGINE_INI_PATH);
					string iniText = value > 0 ?
						Properties.Resources.ResourceManager.GetString("Profile" + value) : string.Empty;
					File.WriteAllText(engineIniPath, iniText);

					Settings.Default.GraphicState = value;
					Settings.Default.Save();
				} catch (Exception ex) {
					ShowError(ex.Message);
				}
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

		public bool AutoExit {
			get => Settings.Default.AutoExit;
			set {
				Settings.Default.AutoExit = value;
				Settings.Default.Save();
			}
		}

		private bool _gameHasUpdate = false;
		private bool GameHasUpdate {
			get => _gameHasUpdate;
			set {
				if (_gameHasUpdate != value) {
					_gameHasUpdate = value;
					OnPropertyChanged(nameof(LaunchButtonVisibility));
					OnPropertyChanged(nameof(UpdateButtonVisibility));
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

		public Visibility LaunchButtonVisibility {
			get => _gameHasUpdate ? Visibility.Collapsed : Visibility.Visible;
		}

		public Visibility UpdateButtonVisibility {
			get => _gameHasUpdate ? Visibility.Visible : Visibility.Collapsed;
		}

		private bool IsGameRunning() {
			return NativeMethods.FindWindow("UnrealWindow", "尘白禁区") != IntPtr.Zero;
		}

		private static void ShowError(string msg) {
			System.Windows.MessageBox.Show(
				msg, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
		}

		private bool FindGame(
			string launcherRegKey,
			string gameRegKey,
			bool isSeasun,
			out string launcherPath,
			out string gameFolder,
			out string savedFolder,
			out bool isSeasunOld
		) {
			gameFolder = null;
			savedFolder = null;
			isSeasunOld = false;

			try {
				launcherPath = Registry.GetValue(launcherRegKey, "DisplayIcon", null) as string;
				if (!File.Exists(launcherPath)) {
					launcherPath = null;
					return false;
				}
			} catch (Exception) {
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
			} catch (Exception) {
				gameFolder = null;
				return false;
			}

			try {
				// 检查存档，可能位于游戏文件夹内或 %LocalAppData%，应优先检查游戏文件夹
				string engineIniPath = Path.Combine(gameFolder, "game\\Saved", ENGINE_INI_PATH);
				if (File.Exists(engineIniPath)) {
					savedFolder = Path.Combine(gameFolder, "game\\Saved");
					return true;
				}

				if (isSeasun) {
					// 旧版西山居启动器没有 game 中间目录
					engineIniPath = Path.Combine(gameFolder, "Saved", ENGINE_INI_PATH);
					if (File.Exists(engineIniPath)) {
						savedFolder = Path.Combine(gameFolder, "Saved");
						isSeasunOld = true;
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
			} catch (Exception) {
			}

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

		private bool DetectGame() {
			// 检测经典启动器
			bool classicDetected = FindGame(
				"HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\ProjectSnow",
				"HKEY_LOCAL_MACHINE\\SOFTWARE\\Kingsoft\\cbjq",
				false,
				out string classicLauncherPath,
				out string classicGameFolder,
				out string classicSavedFolder,
				out _
			);
			// 检测西山居启动器
			bool seasunDetected = FindGame(
				"HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\SeasunGameCBJQos",
				"HKEY_LOCAL_MACHINE\\SOFTWARE\\Kingsoft\\SeasunGameSSG\\cbjq",
				true,
				out string seasunLauncherPath,
				out string seasunGameFolder,
				out string seasunSavedFolder,
				out bool isSeasunOld
			);

			if (classicDetected && seasunDetected) {
				// 检测到两个启动器，选上次使用时间更近的那一个
				if (File.GetLastAccessTime(classicLauncherPath) < File.GetLastAccessTime(seasunLauncherPath)) {
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
				ShowError(errorMsgs[maxProgress]);

				return false;
			}

			if (classicDetected) {
				_launcherType = LauncherType.Classic;
				_launcherPath = classicLauncherPath;
				GameFolder = classicGameFolder;
				_savedFolder = classicSavedFolder;

				// 读取游戏版本
				try {
					// 读取 manifest.json 而不是 version.cfg，启动器使用前者对比本地与云端的文件变更
					string json = File.ReadAllText(Path.Combine(GameFolder, "manifest.json"));
					string versionStr = ReadStringFromJson(json, "version");
					if (versionStr == null || !Version.TryParse(versionStr, out _gameVersion)) {
						_gameVersion = null;
					}
				} catch (Exception e) {
					ShowError("读取游戏版本失败：" + e.Message);
				}
			} else {
				_launcherType= isSeasunOld ? LauncherType.SeasunOld : LauncherType.Seasun;
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

			return true;
		}

		private bool LaunchGame() {
			string arguments = "-FeatureLevelES31 -ChannelID=jinshan ";
			// 两个启动器传递路径的做法都是错的，这导致了存档路径的混乱。为了使用现有存档，我们只能将错就错。
			if (_launcherType == LauncherType.Classic) {
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

			try {
				// 工作目录为程序所在目录
				Process.Start(new ProcessStartInfo {
					WorkingDirectory = Path.Combine(GameFolder, "game\\Game\\Binaries\\Win64"),
					FileName = "game.exe",
					Arguments = arguments,
					UseShellExecute = true
				});
			} catch (Exception ex) {
				ShowError(ex.Message);
				return false;
			}

			return true;
		}

		private bool LaunchGameLauncher() {
			try {
				// 工作目录为程序所在目录
				Process.Start(new ProcessStartInfo {
					WorkingDirectory = Path.GetDirectoryName(_launcherPath),
					FileName = Path.GetFileName(_launcherPath),
					UseShellExecute = true
				});
			} catch (Exception ex) {
				ShowError(ex.Message);
				return false;
			}

			return true;
		}

		private async Task<Version> FetchRemoteGameVersion() {
			try {
				string response = await _httpClient.GetStringAsync(
					"https://cbjq-client.xoyocdn.com/games/cbjq/SyncEntrys/cbjq_SyncEntry.json").ConfigureAwait(false);
				string versionStr = ReadStringFromJson(response, "GameVersion");
				if (versionStr != null && Version.TryParse(versionStr, out var version)) {
					return version;
				}
			} catch (HttpRequestException) {
			}

			return null;
		}

		public MainWindow() {
			if (IsGameRunning()) {
				ShowError("游戏正在运行！");
				App.Current.Shutdown();
				return;
			}

			if (!DetectGame()) {
				App.Current.Shutdown();
				return;
			}

			_fetchRemoteGameVersionTask = FetchRemoteGameVersion();
			
			// 解析命令行参数
			string[] args = Environment.GetCommandLineArgs();
			if (args.Length == 2 && args[1] == "-q") {
				// 快速启动
				WaitForCheckingGameUpdate();

				if (GameHasUpdate) {
					LaunchGameLauncher();
				} else {
					LaunchGame();
				}

				Close();
				return;
			}

			// 请求完成后在主线程更新 GameHasUpdate
			_fetchRemoteGameVersionTask.ContinueWith(_ => {
				WaitForCheckingGameUpdate();
			}, TaskScheduler.FromCurrentSynchronizationContext());

			Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);

			DataContext = this;

			InitializeComponent();
		}

		public event PropertyChangedEventHandler PropertyChanged;

		protected virtual void OnPropertyChanged(string propertyName) {
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		private void FluentWindow_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) {
			// 取消控件的焦点
			// https://stackoverflow.com/a/2914599
			FocusManager.SetFocusedElement(FocusManager.GetFocusScope(this), this);
		}

		private void LaunchOrUpdateButton_Click(object sender, RoutedEventArgs e) {
			WaitForCheckingGameUpdate();

			if (GameHasUpdate) {
				if (LaunchGameLauncher()) {
					// 启动游戏启动器后直接退出
					Close();
				}
			} else {
				if (LaunchGame() && AutoExit) {
					Close();
				}
			}
		}
	}
}
