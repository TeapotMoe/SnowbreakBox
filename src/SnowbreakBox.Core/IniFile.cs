using System.Text;

namespace SnowbreakBox.Core {
	internal class IniFile {
		private readonly string _fileName;

		public IniFile(string fileName) {
			_fileName = fileName;
		}

		public string Read(string key, string section) {
			StringBuilder retVal = new StringBuilder(255);
			NativeMethods.GetPrivateProfileString(section, key, "", retVal, 255, _fileName);
			return retVal.ToString();
		}

		public void Write(string key, string section, string value) {
			NativeMethods.WritePrivateProfileString(section, key, value, _fileName);
		}

		public void DeleteKey(string key, string section) {
			Write(key, section, null);
		}

		public void DeleteSection(string section) {
			Write(null, section, null);
		}

		public bool KeyExists(string key, string section) {
			return Read(key, section).Length > 0;
		}
	}
}
