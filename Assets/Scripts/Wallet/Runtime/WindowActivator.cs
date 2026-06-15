using System;
using System.Runtime.InteropServices;
using UnityEngine;

// Brings the wallet window to the foreground while a link request is handled and
// then hands focus back to whatever window held it before. Implemented per
// desktop OS: the Win32 foreground API on Windows, the Cocoa/AppKit runtime on
// macOS, and the xdotool window-manager helper on Linux/X11. On Android and iOS
// these are intentional no-ops - a single app owns the screen, the OS manages
// focus, and control returns to the calling dApp through a deeplink. Every native
// path is guarded so an unsupported environment (Wayland, a missing tool, no
// window handle) degrades to a no-op instead of failing.
public class WindowActivator : MonoBehaviour
{
    public static WindowActivator Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        CaptureOwnWindow();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);

    private IntPtr _ownWindow;
    private IntPtr _previousWindow;

    private void CaptureOwnWindow()
    {
        _ownWindow = GetActiveWindow();
        if (_ownWindow == IntPtr.Zero)
        {
            Debug.LogWarning("WindowActivator: could not resolve the wallet window handle.");
        }
    }

    public void Activate()
    {
        if (_ownWindow == IntPtr.Zero)
        {
            return;
        }

        _previousWindow = GetForegroundWindow();
        if (_previousWindow != _ownWindow)
        {
            SetForegroundWindow(_ownWindow);
        }
    }

    public void Restore()
    {
        if (_previousWindow != IntPtr.Zero && _previousWindow != _ownWindow)
        {
            SetForegroundWindow(_previousWindow);
            _previousWindow = IntPtr.Zero;
        }
    }

#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
    private const string ObjC = "/usr/lib/libobjc.dylib";

    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void objc_msgSend_bool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void objc_msgSend_ulong(IntPtr receiver, IntPtr selector, ulong value);

    // NSApplicationActivateIgnoringOtherApps from NSApplicationActivationOptions.
    private const ulong ActivateIgnoringOtherApps = 1UL << 1;

    // Retained NSRunningApplication that was frontmost before we activated.
    private IntPtr _previousApp;

    private void CaptureOwnWindow()
    {
        // macOS activates the running application, so there is no handle to cache.
    }

    private static IntPtr Send(IntPtr receiver, string selector)
    {
        return receiver == IntPtr.Zero ? IntPtr.Zero : objc_msgSend(receiver, sel_registerName(selector));
    }

    public void Activate()
    {
        if (_previousApp != IntPtr.Zero)
        {
            objc_msgSend(_previousApp, sel_registerName("release"));
            _previousApp = IntPtr.Zero;
        }

        IntPtr workspace = Send(objc_getClass("NSWorkspace"), "sharedWorkspace");
        IntPtr frontmost = Send(workspace, "frontmostApplication");
        if (frontmost != IntPtr.Zero)
        {
            // Retain so the handle stays valid until Restore() runs.
            _previousApp = objc_msgSend(frontmost, sel_registerName("retain"));
        }

        IntPtr app = Send(objc_getClass("NSApplication"), "sharedApplication");
        if (app != IntPtr.Zero)
        {
            objc_msgSend_bool(app, sel_registerName("activateIgnoringOtherApps:"), true);
        }
    }

    public void Restore()
    {
        if (_previousApp == IntPtr.Zero)
        {
            return;
        }

        objc_msgSend_ulong(_previousApp, sel_registerName("activateWithOptions:"), ActivateIgnoringOtherApps);
        objc_msgSend(_previousApp, sel_registerName("release"));
        _previousApp = IntPtr.Zero;
    }

#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
    // Focus is delegated to xdotool: it locates our top-level window by PID and
    // asks the window manager to activate it. Under Wayland the compositor blocks
    // programmatic focus changes, and when xdotool is absent we skip - both cases
    // degrade to a no-op instead of failing.
    private string _previousWindowId;
    private static bool _xdotoolUnavailable;

    private void CaptureOwnWindow()
    {
        // The window id is resolved per request via xdotool, nothing to cache.
    }

    public void Activate()
    {
        _previousWindowId = RunXdotool("getactivewindow", capture: true);
        int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
        RunXdotool($"search --pid {pid} --onlyvisible windowactivate", capture: false);
    }

    public void Restore()
    {
        if (string.IsNullOrEmpty(_previousWindowId))
        {
            return;
        }

        RunXdotool($"windowactivate {_previousWindowId}", capture: false);
        _previousWindowId = null;
    }

    private static string RunXdotool(string arguments, bool capture)
    {
        if (_xdotoolUnavailable)
        {
            return null;
        }

        try
        {
            var info = new System.Diagnostics.ProcessStartInfo("xdotool", arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = capture,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using (var process = System.Diagnostics.Process.Start(info))
            {
                string output = capture ? process.StandardOutput.ReadToEnd().Trim() : null;
                process.WaitForExit(2000);
                return output;
            }
        }
        catch (Exception)
        {
            // xdotool is not installed; stop trying for the rest of the session.
            _xdotoolUnavailable = true;
            return null;
        }
    }

#else
    private void CaptureOwnWindow()
    {
        // Mobile and other platforms manage window focus at the OS level.
    }

    public void Activate()
    {
        // The OS owns window focus on this platform; control returns via deeplink.
    }

    public void Restore()
    {
    }
#endif
}
