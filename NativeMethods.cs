using System;
using System.Runtime.InteropServices;

namespace SnowbreakBox {
    internal class NativeMethods {
		private enum WindowThemeAttributeType {
			NonClient = 1,
		}

		[Flags]
		public enum WindowThemeNonClientAttributes {
			NoDrawCaption = 1,
			NoDrawIcon = 2,
			NoSysMenu = 4,
			NoMirrorHelp = 8,
		}

		private struct WTA_OPTIONS {
			public WindowThemeNonClientAttributes Flags;
			public int Mask;
		}

		[DllImport("uxtheme.dll")]
		private static extern int SetWindowThemeAttribute(
			  IntPtr hWnd,
			  WindowThemeAttributeType wtype,
			  ref WTA_OPTIONS attributes,
			  int size
		);

		public static int SetWindowThemeAttribute(
			IntPtr hWnd,
			WindowThemeNonClientAttributes ncAttrs,
			int ncAttrMasks = 2147483647
		) {
            WTA_OPTIONS attributes = new WTA_OPTIONS() {
				Flags = ncAttrs,
				Mask = ncAttrMasks == int.MaxValue ? (int)ncAttrs : ncAttrMasks
			};
			return SetWindowThemeAttribute(hWnd, WindowThemeAttributeType.NonClient, ref attributes, Marshal.SizeOf((object)attributes));
		}

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
    }
}
