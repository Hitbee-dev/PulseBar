using System.Runtime.InteropServices;

namespace PulseBar.Windows.Interop;

/// <summary>One physical adapter reported by DXGI.</summary>
public sealed record GpuAdapterInfo(
    string LuidKey,
    string Description,
    ulong DedicatedVideoMemoryBytes,
    bool IsSoftware);

/// <summary>
/// Enumerates GPU adapters via DXGI to obtain dedicated VRAM totals and names.
/// LuidKey format matches PDH GPU counter instance names: "luid_0x{high:X8}_0x{low:X8}".
/// </summary>
public static class DxgiAdapters
{
    private const uint DxgiErrorNotFound = 0x887A0002;
    private const uint DxgiAdapterFlagSoftware = 2;

    public static IReadOnlyList<GpuAdapterInfo> Enumerate()
    {
        var results = new List<GpuAdapterInfo>();
        var iid = typeof(IDXGIFactory1).GUID;
        if (CreateDXGIFactory1(ref iid, out var factoryPtr) != 0)
        {
            return results;
        }

        var factory = (IDXGIFactory1)Marshal.GetObjectForIUnknown(factoryPtr);
        try
        {
            for (uint i = 0; ; i++)
            {
                var hr = factory.EnumAdapters1(i, out var adapter);
                if ((uint)hr == DxgiErrorNotFound || hr != 0 || adapter is null)
                {
                    break;
                }

                try
                {
                    if (adapter.GetDesc1(out var desc) != 0)
                    {
                        continue;
                    }

                    var luidKey = FormatLuid(desc.AdapterLuidHigh, desc.AdapterLuidLow);
                    results.Add(new GpuAdapterInfo(
                        luidKey,
                        desc.Description,
                        (ulong)desc.DedicatedVideoMemory,
                        (desc.Flags & DxgiAdapterFlagSoftware) != 0));
                }
                finally
                {
                    Marshal.ReleaseComObject(adapter);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
            Marshal.Release(factoryPtr);
        }

        return results;
    }

    public static string FormatLuid(int high, uint low)
        => $"luid_0x{high:X8}_0x{low:X8}";

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint AdapterLuidLow;
        public int AdapterLuidHigh;
        public uint Flags;
    }

    [ComImport]
    [Guid("770aae78-f26f-4dba-a829-253c83d1b387")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDXGIFactory1
    {
        // IDXGIObject
        void SetPrivateData_();
        void SetPrivateDataInterface_();
        void GetPrivateData_();
        void GetParent_();
        // IDXGIFactory
        void EnumAdapters_();
        void MakeWindowAssociation_();
        void GetWindowAssociation_();
        void CreateSwapChain_();
        void CreateSoftwareAdapter_();
        // IDXGIFactory1
        [PreserveSig]
        int EnumAdapters1(uint index, out IDXGIAdapter1? adapter);
        [PreserveSig]
        int IsCurrent();
    }

    [ComImport]
    [Guid("29038f61-3839-4626-91fd-086879011a05")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDXGIAdapter1
    {
        // IDXGIObject
        void SetPrivateData_();
        void SetPrivateDataInterface_();
        void GetPrivateData_();
        void GetParent_();
        // IDXGIAdapter
        void EnumOutputs_();
        void GetDesc_();
        void CheckInterfaceSupport_();
        // IDXGIAdapter1
        [PreserveSig]
        int GetDesc1(out DXGI_ADAPTER_DESC1 desc);
    }
}
