using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DeskPlan.Services;

public static class LaunchEnvironment
{
    // A desktop app launched inside an MSIX host can inherit its filesystem redirection.
    // Reject that environment before opening a database instead of silently creating an empty one.
    public static bool IsPackaged
    {
        get
        {
            uint length = 0;
            var result = GetCurrentPackageFullName(ref length, IntPtr.Zero);
            return result switch
            {
                15700 => false, // APPMODEL_ERROR_NO_PACKAGE
                122 or 0 => true,
                _ => throw new Win32Exception(result)
            };
        }
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint length, IntPtr name);
}
