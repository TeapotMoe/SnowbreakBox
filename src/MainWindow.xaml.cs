using Microsoft.Win32;
using SnowbreakBox.Properties;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;

namespace SnowbreakBox {
	public partial class MainWindow : INotifyPropertyChanged {
		private string _engineIniPath;

		private enum LauncherType {
			Classic,
			Seasun
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
				int setting = (int)Settings.Default["GraphicState"];
				if (setting == 0) {
					return 0;
				}

				try {
					// 判断有没有画质补丁相对容易
					using (StreamReader sr = new StreamReader(_engineIniPath)) {
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
					string iniText = value > 0 ?
						Properties.Resources.ResourceManager.GetString("Profile" + value) : string.Empty;
					File.WriteAllText(_engineIniPath, iniText);

					Settings.Default["GraphicState"] = value;
					Settings.Default.Save();
				} catch (Exception ex) {
					ShowError(ex.Message);
				}
			}
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
			bool checkTruncatedPath,
			out string launcherPath,
			out string gameFolder,
			out string engineIniPath
		) {
			gameFolder = null;
			engineIniPath = null;

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
			} catch (Exception) {
				gameFolder = null;
				return false;
			}

			try {
				// 检查存档，可能位于游戏文件夹内或 %LocalAppData%，应优先检查游戏文件夹
				engineIniPath = Path.Combine(gameFolder, "game\\Saved\\Config\\WindowsNoEditor\\Engine.ini");
				if (!File.Exists(engineIniPath)) {
					// Saved 目录可能不在 game 目录里，旧版西山居启动器使用这个路径
					engineIniPath = Path.Combine(gameFolder, "Saved\\Config\\WindowsNoEditor\\Engine.ini");
					if (!File.Exists(engineIniPath)) {
						if (checkTruncatedPath) {
							// 西山居启动器 v1.7.7 存在 bug，游戏路径中如果存在空格会被截断，比如如果游戏安装在
							// C:\Program Files\Snow，存档会被保存在 C:\Program\Saved。
							int spaceIdx = gameFolder.IndexOf(' ');
							engineIniPath = Path.Combine(
								spaceIdx == -1 ? gameFolder : gameFolder.Substring(0, spaceIdx),
								"Saved",
								"Config\\WindowsNoEditor\\Engine.ini"
							);
						}

						if (!(checkTruncatedPath && File.Exists(engineIniPath))) {
							// 检查 %LocalAppData%，如果游戏路径中如果存在空格，经典启动器会把存档保存在这里
							engineIniPath = Path.Combine(
								Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
								"Game\\Saved\\Config\\WindowsNoEditor\\Engine.ini"
							);
							if (!File.Exists(engineIniPath)) {
								engineIniPath = null;
								return false;
							}
						}
					}
				}
			} catch (Exception) {
				engineIniPath = null;
				return false;
			}

			return true;
		}

		private bool DetectGame() {
			// 检测经典启动器
			bool classicDetected = FindGame(
				"HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\ProjectSnow",
				"HKEY_LOCAL_MACHINE\\SOFTWARE\\Kingsoft\\cbjq",
				false,
				out string classicLauncherPath,
				out string classicGameFolder,
				out string classicEngineIniPath
			);
			// 检测西山居启动器
			bool seasunDetected = FindGame(
				"HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\SeasunGameCBJQos",
				"HKEY_LOCAL_MACHINE\\SOFTWARE\\Kingsoft\\SeasunGameSSG\\cbjq",
				true,
				out string seasunLauncherPath,
				out string seasunGameFolder,
				out string seasunEngineIniPath
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
				GameFolder = classicGameFolder;
				_engineIniPath = classicEngineIniPath;
			} else {
				_launcherType= LauncherType.Seasun;
				GameFolder = seasunGameFolder;
				_engineIniPath = seasunEngineIniPath;
			}

			return true;
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

		private void LaunchButton_Click(object sender, RoutedEventArgs e) {
			string arguments = "-FeatureLevelES31 -ChannelID=jinshan ";
			// 两个启动器传递路径的做法都是错的，这导致了存档路径的混乱。为了使用现有存档，我们只能将错就错。
			if (_launcherType == LauncherType.Classic) {
				// 经典启动器在路径两边加反斜杠和双引号
				arguments += "\"-userdir=\\\"";
				arguments += Path.Combine(GameFolder, "game");
				arguments += "\\\"\"";
			} else {
				// 西山居启动器不加双引号
				arguments += "-userdir=";
				arguments += Path.Combine(GameFolder, "game");
			}

			ProcessStartInfo startInfo = new ProcessStartInfo {
				WorkingDirectory = Path.Combine(GameFolder, "game\\Game\\Binaries\\Win64"),
				FileName = "game.exe",
				Arguments = arguments,
				UseShellExecute = true
			};

			try {
				Process.Start(startInfo);
			} catch (Exception ex) {
				ShowError(ex.Message);
			}
		}
	}
}
