using System.Text;

namespace SnowbreakBox.Core {
	internal class IniFile {
		private readonly string _fileName;

		public IniFile(string fileName) {
			_fileName = fileName;
		}

		public string Read(string key, string section = null) {
			StringBuilder retVal = new StringBuilder(255);
			NativeMethods.GetPrivateProfileString(section, key, "", retVal, 255, _fileName);
			return retVal.ToString();
		}

		public void Write(string key, string value, string section = null) {
			NativeMethods.WritePrivateProfileString(section, key, value, _fileName);
		}

		public void DeleteKey(string key, string section = null) {
			Write(key, null, section);
		}

		public void DeleteSection(string section = null) {
			Write(null, null, section);
		}

		public bool KeyExists(string key, string section = null) {
			return Read(key, section).Length > 0;
		}
	}
}
