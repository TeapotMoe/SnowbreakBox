using SnowbreakBox.Core;
using System;

namespace QuickLauncher {
	internal class Program {
		static void Main() {
			try {
				new GameEnv().Launch(AppConfig.LoginChannel);
			} catch (Exception ex) {
				NativeMethods.MessageBox(IntPtr.Zero, ex.Message, "错误",
					NativeMethods.MB_OK | NativeMethods.MB_ICONERROR);
			}
		}
	}
}
