using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SnowbreakBox.Core {
	internal class NativeMethods {
		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

		[DllImport("kernel32", CharSet = CharSet.Unicode)]
		public static extern int WritePrivateProfileString(
			string lpAppName, string lpKeyName, string lpString, string lpFileName);

		[DllImport("kernel32", CharSet = CharSet.Unicode)]
		public static extern int GetPrivateProfileString(
			string lpAppName, string lpKeyName, string lpDefault,
			StringBuilder lpReturnedString, int nSize, string lpFileName);
	}
}
