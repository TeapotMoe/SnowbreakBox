using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SnowbreakBox.Core {
	public class AppConfig {
		private static readonly IniFile _iniFile;

		private static readonly string APP_SECTION = "App";
		private static readonly string GAME_SECTION = "Game";

		private static readonly string AUTO_EXIT_KEY = "AutoExit";
		private static readonly string LOGIN_CHANNEL_KEY = "LoginChannel";

		static AppConfig() {
			string configDir = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"SnowbreakBox");
			Directory.CreateDirectory(configDir);

			_iniFile = new IniFile(configDir + "\\config.ini");
		}

		public static bool AutoExit {
			get {
				string value = _iniFile.Read(AUTO_EXIT_KEY, APP_SECTION);
				return string.IsNullOrEmpty(value) || value == "1";
			}
			set => _iniFile.Write(AUTO_EXIT_KEY, APP_SECTION, value ? "1" : "0");
		}

		public static LoginChannel LoginChannel {
			get {
				string value = _iniFile.Read(LOGIN_CHANNEL_KEY, GAME_SECTION);
				if (Enum.TryParse(value, out LoginChannel channel)) {
					return channel;
				} else {
					return LoginChannel.Default;
				}
			}
			set => _iniFile.Write(LOGIN_CHANNEL_KEY , GAME_SECTION , value.ToString());
		}
	}
}
