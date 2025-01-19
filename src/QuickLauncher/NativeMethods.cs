using System;
using System.Runtime.InteropServices;

namespace QuickLauncher {
	internal class NativeMethods {
		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		public static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

		public const uint MB_OK = 0x00000000;
		public const uint MB_ICONERROR = 0x00000010;
	}
}
