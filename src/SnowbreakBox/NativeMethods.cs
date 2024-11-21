using System;
using System.Runtime.InteropServices;

namespace SnowbreakBox {
	internal class NativeMethods {
		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
	}
}
