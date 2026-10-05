using System.Runtime.InteropServices;
using ComFileTime = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace DayZServerManager.Services;

/// <summary>Utilisation du processeur et de la mémoire de tout le PC.</summary>
public static class SystemMetrics
{
    private static ulong _lastIdle, _lastKernel, _lastUser;

    /// <summary>Pourcentage d'utilisation du processeur depuis le dernier appel (null au premier appel).</summary>
    public static double? GetCpuUsage()
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime)) return null;
        ulong idle = ToUlong(idleTime), kernel = ToUlong(kernelTime), user = ToUlong(userTime);

        double? usage = null;
        if (_lastKernel != 0)
        {
            ulong idleDelta = idle - _lastIdle;
            ulong total = (kernel - _lastKernel) + (user - _lastUser); // le temps « kernel » inclut le temps inactif
            if (total > 0) usage = Math.Clamp((total - idleDelta) * 100.0 / total, 0, 100);
        }

        _lastIdle = idle;
        _lastKernel = kernel;
        _lastUser = user;
        return usage;
    }

    /// <summary>Mémoire totale et disponible du PC, en octets.</summary>
    public static (ulong Total, ulong Available)? GetMemory()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref status) ? (status.ullTotalPhys, status.ullAvailPhys) : null;
    }

    private static ulong ToUlong(ComFileTime time) =>
        ((ulong)(uint)time.dwHighDateTime << 32) | (uint)time.dwLowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out ComFileTime idleTime, out ComFileTime kernelTime, out ComFileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
}
