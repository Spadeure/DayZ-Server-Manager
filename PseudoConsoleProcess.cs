using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DayZServerManager.Services;

/// <summary>
/// Lance un programme dans une console Windows invisible (ConPTY).
/// SteamCMD croit alors écrire dans une vraie console : il affiche tout en direct
/// (progression, demande de code Steam Guard…) au lieu de tout garder en mémoire jusqu'à la fin.
/// Tous les processus lancés sont regroupés dans un « job » pour pouvoir attendre ou arrêter l'ensemble.
/// </summary>
internal sealed class PseudoConsoleProcess : IDisposable
{
    private IntPtr _pseudoConsole;
    private IntPtr _attributeList;
    private IntPtr _job;
    private PROCESS_INFORMATION _process;
    private bool _consoleClosed;

    public Stream Output { get; private set; } = Stream.Null;
    public Stream Input { get; private set; } = Stream.Null;

    private PseudoConsoleProcess() { }

    public static PseudoConsoleProcess Start(string exePath, string arguments, string workingDirectory)
    {
        var p = new PseudoConsoleProcess();
        try
        {
            p.StartCore(exePath, arguments, workingDirectory);
            return p;
        }
        catch
        {
            p.Dispose();
            throw;
        }
    }

    private void StartCore(string exePath, string arguments, string workingDirectory)
    {
        if (!CreatePipe(out var inputRead, out var inputWrite, IntPtr.Zero, 0)) throw new Win32Exception();
        if (!CreatePipe(out var outputRead, out var outputWrite, IntPtr.Zero, 0)) throw new Win32Exception();

        int hr = CreatePseudoConsole(new COORD { X = 250, Y = 60 }, inputRead, outputWrite, 0, out _pseudoConsole);
        if (hr != 0) throw new Win32Exception(hr, "Impossible de créer la console invisible.");

        // La console garde ses propres copies de ces extrémités.
        inputRead.Dispose();
        outputWrite.Dispose();
        Output = new FileStream(outputRead, FileAccess.Read, 4096, false);
        Input = new FileStream(inputWrite, FileAccess.Write, 1, false);

        var size = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        _attributeList = Marshal.AllocHGlobal(size);
        if (!InitializeProcThreadAttributeList(_attributeList, 1, 0, ref size)) throw new Win32Exception();
        if (!UpdateProcThreadAttribute(_attributeList, 0, (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                _pseudoConsole, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
            throw new Win32Exception();

        _job = CreateJobObjectW(IntPtr.Zero, null);
        if (_job == IntPtr.Zero) throw new Win32Exception();

        var startupInfo = new STARTUPINFOEX();
        startupInfo.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
        startupInfo.lpAttributeList = _attributeList;

        var commandLine = new StringBuilder($"\"{exePath}\" {arguments}");
        if (!CreateProcessW(null, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                EXTENDED_STARTUPINFO_PRESENT | CREATE_SUSPENDED, IntPtr.Zero, workingDirectory,
                ref startupInfo, out _process))
            throw new Win32Exception();

        // Le processus démarre en pause, on le range dans le job avant de le laisser tourner.
        AssignProcessToJobObject(_job, _process.hProcess);
        ResumeThread(_process.hThread);
    }

    /// <summary>Attend que le programme et tous ceux qu'il a lancés soient terminés.</summary>
    public async Task WaitForAllExitAsync()
    {
        while (ActiveProcessCount() > 0)
            await Task.Delay(500).ConfigureAwait(false);
    }

    public int ExitCode => GetExitCodeProcess(_process.hProcess, out var code) ? (int)code : -1;

    public void Write(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        Input.Write(bytes, 0, bytes.Length);
        Input.Flush();
    }

    public void Kill()
    {
        if (_job != IntPtr.Zero) TerminateJobObject(_job, 1);
    }

    /// <summary>Ferme la console : la lecture de la sortie se termine alors.</summary>
    public void CloseConsole()
    {
        if (_consoleClosed || _pseudoConsole == IntPtr.Zero) return;
        _consoleClosed = true;
        ClosePseudoConsole(_pseudoConsole);
    }

    private int ActiveProcessCount()
    {
        if (!QueryInformationJobObject(_job, JobObjectBasicAccountingInformation, out var info,
                Marshal.SizeOf<JOBOBJECT_BASIC_ACCOUNTING_INFORMATION>(), IntPtr.Zero))
            return 0;
        return (int)info.ActiveProcesses;
    }

    public void Dispose()
    {
        CloseConsole();
        try { Input.Dispose(); } catch { /* déjà fermé */ }
        try { Output.Dispose(); } catch { /* déjà fermé */ }
        if (_attributeList != IntPtr.Zero)
        {
            DeleteProcThreadAttributeList(_attributeList);
            Marshal.FreeHGlobal(_attributeList);
            _attributeList = IntPtr.Zero;
        }
        if (_process.hProcess != IntPtr.Zero) CloseHandle(_process.hProcess);
        if (_process.hThread != IntPtr.Zero) CloseHandle(_process.hThread);
        if (_job != IntPtr.Zero) CloseHandle(_job);
        _process = default;
        _job = IntPtr.Zero;
    }

    // ===== Fonctions Windows =====

    private const int PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;
    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private const uint CREATE_SUSPENDED = 0x00000004;
    private const int JobObjectBasicAccountingInformation = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_ACCOUNTING_INFORMATION
    {
        public long TotalUserTime, TotalKernelTime, ThisPeriodTotalUserTime, ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount, TotalProcesses, ActiveProcesses, TotalTerminatedProcesses;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe,
        IntPtr lpPipeAttributes, int nSize);

    [DllImport("kernel32.dll")]
    private static extern int CreatePseudoConsole(COORD size, SafeFileHandle hInput, SafeFileHandle hOutput,
        uint dwFlags, out IntPtr phPC);

    [DllImport("kernel32.dll")]
    private static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount,
        int dwFlags, ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr attribute,
        IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(string? lpApplicationName, StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags,
        IntPtr lpEnvironment, string? lpCurrentDirectory, ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(IntPtr hJob, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(IntPtr hJob, int jobObjectInfoClass,
        out JOBOBJECT_BASIC_ACCOUNTING_INFORMATION lpJobObjectInfo, int cbJobObjectInfoLength, IntPtr lpReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
