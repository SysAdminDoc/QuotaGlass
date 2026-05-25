using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace QuotaGlass.Widget.Services;

/// <summary>
/// L-04 / R4-N2 — registers <see cref="ToastActivator"/> with COM so
/// Windows Action Center can call our process when the user clicks a
/// toast action button.
///
/// Two registrations:
/// 1. HKCU CLSID under <c>Software\Classes\CLSID\{guid}\LocalServer32</c>
///    pointing at the widget EXE. Stable across launches; the installer
///    writes this. Idempotent re-write here is fine on every startup so
///    portable / xcopy-launched users also work.
/// 2. <c>CoRegisterClassObject</c> at runtime so the currently-running
///    process receives the activation instead of cold-launching a new one.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
internal static class ToastActivatorRegistration
{
    private const uint CLSCTX_LOCAL_SERVER = 0x4;
    private const uint REGCLS_MULTIPLEUSE = 0x1;

    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject(
        ref Guid rclsid,
        [MarshalAs(UnmanagedType.IUnknown)] object pUnk,
        uint dwClsContext,
        uint flags,
        out uint lpdwRegister);

    [DllImport("ole32.dll")]
    private static extern int CoRevokeClassObject(uint dwRegister);

    private static uint _cookie;
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        WriteHkcuRegistration();
        RegisterRunningProcess();
        _registered = true;
    }

    private static void WriteHkcuRegistration()
    {
        var exePath = Environment.ProcessPath
                      ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath)) return;

        var clsidKey = $@"Software\Classes\CLSID\{{{ToastActivator.Clsid}}}";
        using var clsid = Registry.CurrentUser.CreateSubKey(clsidKey, writable: true);
        clsid?.SetValue(string.Empty, "QuotaGlass Toast Activator", RegistryValueKind.String);
        clsid?.SetValue("AppId", $"{{{ToastActivator.Clsid}}}", RegistryValueKind.String);
        using var local = clsid?.CreateSubKey("LocalServer32", writable: true);
        local?.SetValue(string.Empty, $"\"{exePath}\" --toast-activator", RegistryValueKind.String);
    }

    private static void RegisterRunningProcess()
    {
        try
        {
            var factory = new ToastActivatorFactory();
            var clsid = new Guid(ToastActivator.Clsid);
            CoRegisterClassObject(ref clsid, factory, CLSCTX_LOCAL_SERVER, REGCLS_MULTIPLEUSE, out _cookie);
        }
        catch
        {
            // Best-effort. Activation falls back to cold-launching the EXE
            // with --toast-activator, which is still functional.
        }
    }

    public static void Unregister()
    {
        if (_cookie == 0) return;
        try { CoRevokeClassObject(_cookie); } catch { }
        _cookie = 0;
        _registered = false;
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class ToastActivatorFactory : IClassFactory
    {
        public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
        {
            if (pUnkOuter != IntPtr.Zero)
            {
                ppvObject = IntPtr.Zero;
                return unchecked((int)0x80040110); // CLASS_E_NOAGGREGATION
            }
            var activator = new ToastActivator();
            ppvObject = Marshal.GetComInterfaceForObject(activator, typeof(INotificationActivationCallback));
            return 0;
        }

        public int LockServer(bool fLock) => 0;
    }

    [ComImport]
    [Guid("00000001-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IClassFactory
    {
        [PreserveSig]
        int CreateInstance([MarshalAs(UnmanagedType.IUnknown)] IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);
        [PreserveSig]
        int LockServer([MarshalAs(UnmanagedType.Bool)] bool fLock);
    }
}
