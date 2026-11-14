using System;
using NexusRobloxManager;
class VisibilityProbe {
 static void Main() {
  foreach(var w in WindowEngine.ScanRobloxWindows()) {
   uint color, flags; byte alpha;
   bool layered=Native.GetLayeredWindowAttributes(w.Hwnd,out color,out alpha,out flags);
   int ex=Native.GetWindowLong(w.Hwnd,Native.GWL_EXSTYLE);
   Console.WriteLine("PID="+w.Pid+" hidden="+WindowVisibility.IsStealthStyleApplied(w.Hwnd)+" toolWindow="+((ex&Native.WS_EX_TOOLWINDOW)!=0)+" appWindow="+((ex&Native.WS_EX_APPWINDOW)!=0)+" alpha="+(layered?alpha.ToString():"none")+" minimized="+Native.IsIconic(w.Hwnd));
  }
 }
}
