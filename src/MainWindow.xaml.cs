using SnowbreakBox.Properties;
using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;

namespace SnowbreakBox {
	public partial class MainWindow : INotifyPropertyChanged {
		private string _localizationTxtPath;
		private string _engineIniPath;

		public string GameFolder { get; set; }

		public bool IsCensorDisabled {
			get {
				try {
					using (StreamReader sr = new StreamReader(_localizationTxtPath)) {
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
					File.WriteAllText(_localizationTxtPath, value ? "localization = 1" : "localization = 0");
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

		// userSelectedFolder 可能是尘白启动器根目录、西山居启动器根目录或游戏根目录
		private string FindPaths(string testFolder) {
			if (!Directory.Exists(testFolder)) {
				return "路径不存在";
			}

			string gameFolder;
			if (File.Exists(Path.Combine(testFolder, "data\\game\\Game\\Binaries\\Win64\\game.exe"))) {
				// 尘白启动器
				gameFolder = Path.Combine(testFolder, "data");
			} else if (File.Exists(Path.Combine(testFolder, "Game\\cbjq\\game\\Game\\Binaries\\Win64\\game.exe"))) {
				// 西山居启动器，且尘白安装在启动器内
				gameFolder = Path.Combine(testFolder, "Game\\cbjq");
			} else if (File.Exists(Path.Combine(testFolder, "game\\Game\\Binaries\\Win64\\game.exe"))) {
				// 游戏根目录
				gameFolder = testFolder;
			} else {
				return "未找到游戏";
			}

			// 不检查 localization.txt 是否存在，如果不存在我们便创建一个
			_localizationTxtPath = Path.Combine(gameFolder, "localization.txt");
			
			// 不同启动器/版本 Engine.ini 位置也不同
			// 必须启动一次游戏才能保证 Engine.ini 存在！
			_engineIniPath = Path.Combine(gameFolder, "game\\Saved\\Config\\WindowsNoEditor\\Engine.ini");
			if (!File.Exists(_engineIniPath)) {
				// Saved 目录可能不在 game 目录里
				_engineIniPath = Path.Combine(gameFolder, "Saved\\Config\\WindowsNoEditor\\Engine.ini");
				if (!File.Exists(_engineIniPath)) {
					// Engine.ini 也可能在 AppData 里
					_engineIniPath = Path.Combine(
						Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
						"Game\\Saved\\Config\\WindowsNoEditor\\Engine.ini"
					);
					if (!File.Exists(_engineIniPath)) {
						return "未找到 Engine.ini";
					}
				}
			}

			GameFolder = gameFolder;
			OnPropertyChanged(nameof(GameFolder));
			OnPropertyChanged(nameof(IsCensorDisabled));
			OnPropertyChanged(nameof(GraphicState));

			Settings.Default["GameFolder"] = GameFolder;
			Settings.Default.Save();

			return null;
		}

		private bool GuessGameFolder() {
			string gameFolder = Settings.Default["GameFolder"] as string;
			if (FindPaths(gameFolder) == null) {
				return true;
			}

			// 尘白启动器
			gameFolder = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Snow");
			if (FindPaths(gameFolder) == null) {
				return true;
			}

			// 西山居启动器
			// 不确定盘符，只测试 C 盘
			if (FindPaths("C:\\SeasunCBJQos") == null) {
				return true;
			}

			return false;
		}

		private bool SelectGameFolder() {
			FolderBrowserDialog dialog = new FolderBrowserDialog {
				RootFolder = Environment.SpecialFolder.MyComputer,
				Description = "未检测到游戏，请手动选择启动器或游戏根目录"
			};

			while (true) {
				// 使弹窗位于前台
				NativeWindow win32Parent = new NativeWindow();
				win32Parent.AssignHandle(new WindowInteropHelper(this).Handle);
				if (dialog.ShowDialog(win32Parent) != System.Windows.Forms.DialogResult.OK) {
					return false;
				}

				string msg = FindPaths(dialog.SelectedPath);
				if (msg == null) {
					return true;
				}
				dialog.Description = "路径无效，请重新选择启动器或游戏根目录：" + msg;
			}
		}

		private bool IsGameRunning() {
			return NativeMethods.FindWindow("UnrealWindow", "尘白禁区") != IntPtr.Zero;
		}

		private static void ShowError(string msg) {
			System.Windows.MessageBox.Show(
				msg, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
		}

		public MainWindow() {
			if (IsGameRunning()) {
				ShowError("游戏正在运行！");
				App.Current.Shutdown();
				return;
			}

			GuessGameFolder();

			// 隐藏标题栏图标
			SourceInitialized += delegate {
				NativeMethods.SetWindowThemeAttribute(
					new WindowInteropHelper(this).Handle,
					NativeMethods.WindowThemeNonClientAttributes.NoSysMenu | NativeMethods.WindowThemeNonClientAttributes.NoDrawIcon
				);

				if (string.IsNullOrEmpty(GameFolder) && !SelectGameFolder()) {
					Close();
					return;
				}
			};

			Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);

			DataContext = this;

			InitializeComponent();
		}

		public event PropertyChangedEventHandler PropertyChanged;

		protected virtual void OnPropertyChanged(string propertyName) {
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		private void EditGameFolderButton_Click(object sender, RoutedEventArgs e) {
			SelectGameFolder();
		}

		private void FluentWindow_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) {
			// 取消控件的焦点
			// https://stackoverflow.com/a/2914599
			FocusManager.SetFocusedElement(FocusManager.GetFocusScope(this), this);
		}
	}
}
