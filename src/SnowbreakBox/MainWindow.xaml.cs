using SnowbreakBox.Properties;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using SnowbreakBox.Core;

namespace SnowbreakBox {
	public partial class MainWindow : INotifyPropertyChanged {
		private readonly GameEnv _gameEnv;

		public string GameFolder => _gameEnv.GameFolder;

		public bool IsCensorDisabled {
			get => _gameEnv.IsCensorDisabled;
			set {
				try {
					_gameEnv.IsCensorDisabled = value;
				} catch (Exception ex) {
					ShowError(ex.Message);
				}
			}
		}

		public int GraphicState {
			get => _gameEnv.GraphicState;
			set {
				try {
					_gameEnv.GraphicState = value;
				} catch (Exception ex) {
					ShowError(ex.Message);
				}
			}
		}

		public bool IsSplashScreenDisabled {
			get => _gameEnv.IsSplashScreenDisabled;
			set { _gameEnv.IsSplashScreenDisabled = value; }
		}

		public bool AutoExit {
			get => Settings.Default.AutoExit;
			set {
				Settings.Default.AutoExit = value;
				Settings.Default.Save();
			}
		}

		public bool IsFixSavedPathButtonEnabled => !_gameEnv.IsSavedPathStandard;

		public Visibility FixSavedPathWarningInfoBarVisibility => _gameEnv.IsSavedPathStandard ? Visibility.Collapsed : Visibility.Visible;
		public Visibility FixSavedPathSuccessInfoBarVisibility => _gameEnv.IsSavedPathStandard ? Visibility.Visible : Visibility.Collapsed;

		public Visibility LaunchButtonVisibility => _gameEnv.GameHasUpdate ? Visibility.Collapsed : Visibility.Visible;
		public Visibility UpdateButtonVisibility => _gameEnv.GameHasUpdate ? Visibility.Visible : Visibility.Collapsed;

		private static void ShowError(string msg) {
			MessageBox.Show(msg, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
		}


		public MainWindow() {
			try {
				_gameEnv = new GameEnv();
			} catch (Exception ex) {
				ShowError(ex.Message);
				App.Current.Shutdown();
				return;
			}

			_gameEnv.PropertyChanged += GameEnv_PropertyChanged;

			Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);

			DataContext = this;

			InitializeComponent();
		}

		private void GameEnv_PropertyChanged(object sender, PropertyChangedEventArgs e) {
			switch (e.PropertyName) {
				case nameof(GameEnv.GameHasUpdate):
					OnPropertyChanged(nameof(LaunchButtonVisibility));
					OnPropertyChanged(nameof(UpdateButtonVisibility));
					break;
				case nameof(GameEnv.IsSavedPathStandard):
					OnPropertyChanged(nameof(IsFixSavedPathButtonEnabled));
					OnPropertyChanged(nameof(FixSavedPathWarningInfoBarVisibility));
					OnPropertyChanged(nameof(FixSavedPathSuccessInfoBarVisibility));
					break;
			}
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
			try {
				_gameEnv.LaunchGameOrLauncher();
			} catch (Exception ex) {
				ShowError(ex.Message);
				return;
			}

			// 下列情况下退出：
			// 1. 游戏需要更新，已启动官方启动器
			// 2. 已启动游戏，且启用了“启动游戏后退出”选项
			if (_gameEnv.GameHasUpdate || AutoExit) {
				Close();
			}
		}

		private void FixSavedPathButton_Click(object sender, RoutedEventArgs e) {
			if (MessageBox.Show("确定修正存档路径？这个操作是不可逆的。",
				"警告", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.Cancel) {
				return;
			}

			try {
				_gameEnv.FixSavedPath();
			} catch (Exception ex) {
				ShowError(ex.Message);
				return;
			}
		}
	}
}
