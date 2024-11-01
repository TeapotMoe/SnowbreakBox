using SnowbreakBox.Properties;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;

namespace SnowbreakBox {
	public partial class MainWindow : System.Windows.Window {
		private string localizationTxtPath;
		private string engineIniPath;

        // gameFolder 可能是尘白启动器根目录或西山居启动器根目录或游戏根目录
        bool FindPaths(string gameFolder) {
			if (!Directory.Exists(gameFolder)) {
				return false;
			}
            
            string realGameFolder;

			if (File.Exists(Path.Combine(gameFolder, "data\\game\\Game\\Binaries\\Win64\\game.exe"))) {
				// 尘白启动器
				realGameFolder = Path.Combine(gameFolder, "data");
			} else if (File.Exists(Path.Combine(gameFolder, "Game\\cbjq\\game\\Game\\Binaries\\Win64\\game.exe"))) {
				// 西山居启动器，且尘白安装在启动器内
				realGameFolder = Path.Combine(gameFolder, "Game\\cbjq");
			} else if (File.Exists(Path.Combine(gameFolder, "game\\Game\\Binaries\\Win64\\game.exe"))) {
                // 游戏根目录
                realGameFolder = gameFolder;
			} else {
				return false;
			}

            localizationTxtPath = Path.Combine(realGameFolder, "localization.txt");

			engineIniPath = Path.Combine(realGameFolder, "Saved\\Config\\WindowsNoEditor\\Engine.ini");
			if (!File.Exists(engineIniPath)) {
				// 也可能在 AppData 里
				engineIniPath = Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
					"Game\\Saved\\Config\\WindowsNoEditor\\Engine.ini"
				);
				if (!File.Exists(engineIniPath)) {
					return false;
				}
			}

			return true;
		}

		private bool SelectGameFolder() {
			string gameFolder = Settings.Default["GameFolder"] as string;
			if (FindPaths(gameFolder)) {
				return true;
			}

			// 尘白启动器
			gameFolder = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Snow");
			if (FindPaths(gameFolder)) {
				Settings.Default["GameFolder"] = gameFolder;
				Settings.Default.Save();
				return true;
			}

			// 西山居启动器
			// 不确定盘符，只测试 C 盘
            if (FindPaths("C:\\SeasunCBJQos")) {
                Settings.Default["GameFolder"] = gameFolder;
                Settings.Default.Save();
                return true;
            }

            FolderBrowserDialog dialog = new FolderBrowserDialog {
				RootFolder = Environment.SpecialFolder.MyComputer,
				Description = "未检测到游戏，请手动选择启动器或游戏根目录"
			};

			while (true) {
				if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) {
					return false;
				}

				gameFolder = dialog.SelectedPath;
				if (FindPaths(gameFolder)) {
					break;
				} else {
					dialog.Description = "路径无效，请重新选择启动器或游戏根目录";
				}
			}

			Settings.Default["GameFolder"] = gameFolder;
			Settings.Default.Save();
			return true;
		}

		private bool IsGameRunning() {
			return NativeMethods.FindWindow("UnrealWindow", "尘白禁区") != IntPtr.Zero;
		}

		private bool GetCensorState() {
			try {
				using (StreamReader sr = new StreamReader(localizationTxtPath)) {
					string line = sr.ReadLine();
					var pair = line.Split('=');
					if (pair.Length != 2) {
						return true;
					}

					return pair[0].Trim() != "localization" || pair[1].Trim() != "1";
				}
			} catch {
				return true;
			}
		}

		private static void ShowError(string msg) {
			System.Windows.MessageBox.Show(msg, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
		}

		public MainWindow() {
			if (!SelectGameFolder()) {
				App.Current.Shutdown();
				return;
			}

			if (IsGameRunning()) {
				ShowError("游戏正在运行！");
				App.Current.Shutdown();
				return;
			}

			// 隐藏标题栏图标
			SourceInitialized += delegate {
				NativeMethods.SetWindowThemeAttribute(
					new WindowInteropHelper(this).Handle,
					NativeMethods.WindowThemeNonClientAttributes.NoSysMenu | NativeMethods.WindowThemeNonClientAttributes.NoDrawIcon
				);
			};

			InitializeComponent();

			uncensorCheckBox.Checked -= UncensorCheckBox_Checked;
			uncensorCheckBox.Unchecked -= UncensorCheckBox_Unchecked;
			uncensorCheckBox.IsChecked = !GetCensorState();
			uncensorCheckBox.Checked += UncensorCheckBox_Checked;
			uncensorCheckBox.Unchecked += UncensorCheckBox_Unchecked;

			graphicComboBox.SelectionChanged -= GraphicComboBox_SelectionChanged;
			graphicComboBox.SelectedIndex = (int)Settings.Default["GraphicState"];
			graphicComboBox.SelectionChanged += GraphicComboBox_SelectionChanged;
		}

		private void UncensorCheckBox_Checked(object sender, RoutedEventArgs e) {
			try {
				File.WriteAllText(localizationTxtPath, "localization = 1");
			} catch (Exception ex) {
				ShowError("操作失败：" + ex.Message);
			}
		}

		private void UncensorCheckBox_Unchecked(object sender, RoutedEventArgs e) {
			File.WriteAllText(localizationTxtPath, "localization = 0");
		}

		private void GraphicComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) {
			string iniPath = engineIniPath;
			int targetState = graphicComboBox.SelectedIndex;

			try {
				string iniText = targetState > 0 ? 
					Properties.Resources.ResourceManager.GetString("Profile" + targetState) : string.Empty;
				File.WriteAllText(iniPath, iniText);
				Settings.Default["GraphicState"] = targetState;
				Settings.Default.Save();
			} catch (Exception ex) {
				graphicComboBox.SelectionChanged -= GraphicComboBox_SelectionChanged;
				graphicComboBox.SelectedIndex = (int)Settings.Default["GraphicState"];
				graphicComboBox.SelectionChanged += GraphicComboBox_SelectionChanged;
				ShowError("操作失败：" + ex.Message);
			}
		}
	}
}
