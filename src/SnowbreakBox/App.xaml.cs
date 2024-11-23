using System;
using System.Windows;

namespace SnowbreakBox {
	public partial class App : Application {
		App() {
			AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
		}

		private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e) {
			MessageBox.Show(e.ExceptionObject.ToString(),
				"未处理异常", MessageBoxButton.OK, MessageBoxImage.Error);
		}
	}
}
