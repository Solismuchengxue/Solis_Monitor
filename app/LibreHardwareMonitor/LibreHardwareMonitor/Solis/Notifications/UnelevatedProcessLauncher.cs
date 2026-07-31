using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace LibreHardwareMonitor.Solis.Notifications;

internal static class UnelevatedProcessLauncher
{
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint LogonWithProfile = 0x00000001;
    private const uint TokenAssignPrimary = 0x0001;
    private const uint TokenDuplicate = 0x0002;
    private const uint TokenQuery = 0x0008;

    public static bool TryStart(string executablePath, string argument)
    {
        Process explorer = FindCurrentSessionExplorer();
        if (explorer is null)
            return false;

        IntPtr token = IntPtr.Zero;
        ProcessInformation processInformation = default;
        try
        {
            if (!OpenProcessToken(
                    explorer.Handle,
                    TokenAssignPrimary | TokenDuplicate | TokenQuery,
                    out token))
            {
                return false;
            }

            var startupInformation = new StartupInformation
            {
                Size = Marshal.SizeOf<StartupInformation>()
            };
            var commandLine = new StringBuilder(
                $"\"{executablePath}\" \"{argument}\"");

            return CreateProcessWithTokenW(
                token,
                LogonWithProfile,
                executablePath,
                commandLine,
                CreateUnicodeEnvironment,
                IntPtr.Zero,
                System.IO.Path.GetDirectoryName(executablePath),
                ref startupInformation,
                out processInformation);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return false;
        }
        finally
        {
            if (processInformation.Process != IntPtr.Zero)
                CloseHandle(processInformation.Process);
            if (processInformation.Thread != IntPtr.Zero)
                CloseHandle(processInformation.Thread);
            if (token != IntPtr.Zero)
                CloseHandle(token);
            explorer.Dispose();
        }
    }

    private static Process FindCurrentSessionExplorer()
    {
        int sessionId = Process.GetCurrentProcess().SessionId;
        foreach (Process process in Process.GetProcessesByName("explorer"))
        {
            try
            {
                if (process.SessionId == sessionId)
                    return process;
            }
            catch (InvalidOperationException)
            {
                // The process exited while being inspected.
            }

            process.Dispose();
        }

        return null;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(
        IntPtr processHandle,
        uint desiredAccess,
        out IntPtr tokenHandle);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessWithTokenW(
        IntPtr token,
        uint logonFlags,
        string applicationName,
        StringBuilder commandLine,
        uint creationFlags,
        IntPtr environment,
        string currentDirectory,
        ref StartupInformation startupInformation,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInformation
    {
        public int Size;
        public string Reserved;
        public string Desktop;
        public string Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2;
        public IntPtr Reserved2Pointer;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public int ProcessId;
        public int ThreadId;
    }
}
