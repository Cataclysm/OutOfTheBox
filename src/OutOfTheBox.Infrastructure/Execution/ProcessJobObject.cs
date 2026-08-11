// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace OutOfTheBox.Infrastructure.Execution;

/// <summary>
/// A Windows job object configured with <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>, so that closing
/// this handle terminates every process ever assigned to it - including further children a spawned
/// process itself spawns, no matter how deeply nested. This exists because
/// <see cref="Process.Kill(bool)"/> with <c>entireProcessTree: true</c> walks a live snapshot of
/// running processes matched by parent-PID and start time; a descendant still being created at the
/// instant of that snapshot can be missed entirely, leaving it running with no parent left to kill
/// it (confirmed live: a BehaviorTests run against the deliberately deep-chained HangingFixture
/// project - <c>dotnet test</c> -> <c>vstest.console</c> -> <c>testhost.exe</c> - left an orphaned
/// <c>testhost.exe</c> running long after both the test that spawned it and this service's own
/// process under test had already exited). A job object's kill-on-close is atomic and handle-based
/// rather than snapshot-based: Windows automatically adds every process a job member creates to the
/// same job unless the child explicitly requests breakaway, which nothing here grants - so there is
/// no window in which a fast-forking descendant can escape it.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ProcessJobObject : SafeHandleZeroOrMinusOneIsInvalid
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    internal ProcessJobObject()
        : base(ownsHandle: true)
    {
    }

    /// <summary>Creates a new job object with kill-on-close already configured.</summary>
    public static ProcessJobObject Create()
    {
        var job = CreateJobObjectW(IntPtr.Zero, null);
        if (job.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var extended = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION { LimitFlags = JobObjectLimitKillOnJobClose },
        };

        var length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(extended, buffer, fDeleteOld: false);
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, buffer, (uint)length))
            {
                var error = Marshal.GetLastWin32Error();
                job.Dispose();
                throw new Win32Exception(error);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return job;
    }

    /// <summary>Assigns <paramref name="process"/> to this job - from this point on, it (and anything it ever spawns) dies when this handle closes.</summary>
    public void Assign(Process process)
    {
        if (!AssignProcessToJobObject(this, process.SafeHandle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    /// <inheritdoc />
    protected override bool ReleaseHandle() => CloseHandle(handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ProcessJobObject CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(ProcessJobObject hJob, int jobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(ProcessJobObject hJob, SafeHandle hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
