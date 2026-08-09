// Copyright (c) 2026 Dennis Freise <dennis.freise@final-frontier.org>. All rights reserved.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace OutOfTheBox.Infrastructure.Diagnostics;

/// <summary>One process Restart Manager reports as holding a queried file open - raw <c>RM_APP_TYPE</c> int, mapped to <see cref="Application.Diagnostics.FileLockApplicationType"/> by the caller.</summary>
internal readonly record struct RestartManagerProcessInfo(int ProcessId, string ApplicationName, int ApplicationType, bool IsRestartable);

/// <summary>
/// Windows' Restart Manager API (<c>rstrtmgr.dll</c>) - the OS-native mechanism for "what process
/// has this file open," the same one behind Windows Explorer's own "this file is open in..." dialog
/// and every MSI installer's file-in-use prompt, per design.md's "File-lock detection" decision.
/// One call per session (<c>RmStartSession</c> → <c>RmRegisterResources</c> → <c>RmGetList</c> →
/// <c>RmEndSession</c>) - <c>RmEndSession</c> always runs, even on failure, or the session handle
/// leaks. <c>RmGetList</c> needs the documented two-call sizing pattern: the first call (with a
/// zero-length buffer) reports how many entries are needed via <c>ERROR_MORE_DATA</c>, the second
/// call (with a correctly-sized array) returns the actual entries.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class RestartManager
{
    private const int CchRmSessionKey = 32;
    private const int CchRmMaxAppName = 255;
    private const int CchRmMaxSvcName = 63;
    private const int ErrorSuccess = 0;
    private const int ErrorMoreData = 234;

    /// <summary>Returns every process currently holding <paramref name="filePath"/> open - empty if nothing has it locked.</summary>
    public static IReadOnlyList<RestartManagerProcessInfo> GetLockingProcesses(string filePath)
    {
        var sessionKey = new StringBuilder(CchRmSessionKey + 1);
        var startResult = RmStartSession(out var sessionHandle, 0, sessionKey);
        if (startResult != ErrorSuccess)
        {
            throw new InvalidOperationException($"RmStartSession failed (Win32 error {startResult}).");
        }

        try
        {
            var registerResult = RmRegisterResources(sessionHandle, 1, [filePath], 0, null, 0, null);
            if (registerResult != ErrorSuccess)
            {
                throw new InvalidOperationException($"RmRegisterResources failed (Win32 error {registerResult}).");
            }

            uint procInfo = 0;
            var sizingResult = RmGetList(sessionHandle, out var procInfoNeeded, ref procInfo, null, out _);
            if (sizingResult is not ErrorMoreData and not ErrorSuccess)
            {
                throw new InvalidOperationException($"RmGetList (sizing call) failed (Win32 error {sizingResult}).");
            }

            if (procInfoNeeded == 0)
            {
                return [];
            }

            procInfo = procInfoNeeded;
            var processInfo = new RM_PROCESS_INFO[procInfo];
            var listResult = RmGetList(sessionHandle, out procInfoNeeded, ref procInfo, processInfo, out _);
            if (listResult != ErrorSuccess)
            {
                throw new InvalidOperationException($"RmGetList failed (Win32 error {listResult}).");
            }

            var results = new List<RestartManagerProcessInfo>((int)procInfo);
            for (var i = 0; i < procInfo; i++)
            {
                results.Add(new RestartManagerProcessInfo(
                    processInfo[i].Process.dwProcessId,
                    processInfo[i].strAppName,
                    (int)processInfo[i].ApplicationType,
                    processInfo[i].bRestartable));
            }

            return results;
        }
        finally
        {
            RmEndSession(sessionHandle);
        }
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, StringBuilder strSessionKey);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint dwSessionHandle,
        uint nFiles,
        string[] rgsFilenames,
        uint nApplications,
        RM_UNIQUE_PROCESS[]? rgApplications,
        uint nServices,
        string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint dwSessionHandle,
        out uint pnProcInfoNeeded,
        ref uint pnProcInfo,
        [In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
        out uint lpdwRebootReasons);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint dwSessionHandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxAppName + 1)]
        public string strAppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxSvcName + 1)]
        public string strServiceShortName;

        public int ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;

        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }
}
