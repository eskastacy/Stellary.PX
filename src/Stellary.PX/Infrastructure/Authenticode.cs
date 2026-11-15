using System.Runtime.InteropServices;

namespace Stellary.PX.Infrastructure;

/// <summary>WinVerifyTrust wrapper. Returns whether the file carries a trusted Authenticode chain.</summary>
public static class Authenticode
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, ref Guid actionId, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        public IntPtr pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    public static bool IsTrusted(string path)
    {
        if (!File.Exists(path)) return false;
        var pathPtr = IntPtr.Zero;
        var filePtr = IntPtr.Zero;
        var dataPtr = IntPtr.Zero;
        try
        {
            pathPtr = Marshal.StringToHGlobalUni(path);
            var file = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
                pcwszFilePath = pathPtr
            };
            filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
            Marshal.StructureToPtr(file, filePtr, false);

            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = 2,             // WTD_UI_NONE
                fdwRevocationChecks = 0,    // WTD_REVOKE_NONE
                dwUnionChoice = 1,          // WTD_CHOICE_FILE
                pFile = filePtr,
                dwStateAction = 0,
                dwProvFlags = 0
            };
            dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_DATA>());
            Marshal.StructureToPtr(data, dataPtr, false);

            var action = GenericVerifyV2;
            return WinVerifyTrust(IntPtr.Zero, ref action, dataPtr) == 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (dataPtr != IntPtr.Zero) Marshal.FreeHGlobal(dataPtr);
            if (filePtr != IntPtr.Zero) Marshal.FreeHGlobal(filePtr);
            if (pathPtr != IntPtr.Zero) Marshal.FreeHGlobal(pathPtr);
        }
    }
}
