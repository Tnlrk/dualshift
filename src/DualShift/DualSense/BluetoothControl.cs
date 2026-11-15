using System.Runtime.InteropServices;

namespace DualShift.DualSense;

/// <summary>
/// Odpojení Bluetooth zařízení přes Windows API. DualSense po ztrátě spojení s počítačem sám
/// zhasne a vypne se – stejně jako po nečinnosti na PS5. Zapne se znovu tlačítkem PS.
/// </summary>
internal static class BluetoothControl
{
    private const uint IOCTL_BTH_DISCONNECT_DEVICE = 0x41000C;

    /// <summary>Vrací true, pokud odpojení přijal aspoň jeden Bluetooth adaptér.</summary>
    public static bool Disconnect(ulong address)
    {
        var p = new BLUETOOTH_FIND_RADIO_PARAMS { dwSize = (uint)Marshal.SizeOf<BLUETOOTH_FIND_RADIO_PARAMS>() };
        IntPtr find = BluetoothFindFirstRadio(ref p, out IntPtr radio);
        if (find == IntPtr.Zero) return false;
        bool ok = false;
        try
        {
            do
            {
                try
                {
                    ulong addr = address;
                    if (DeviceIoControl(radio, IOCTL_BTH_DISCONNECT_DEVICE, ref addr, sizeof(ulong), IntPtr.Zero, 0, out _, IntPtr.Zero))
                        ok = true;
                }
                finally
                {
                    CloseHandle(radio);
                }
            }
            while (!ok && BluetoothFindNextRadio(find, out radio));
        }
        finally
        {
            BluetoothFindRadioClose(find);
        }
        return ok;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLUETOOTH_FIND_RADIO_PARAMS
    {
        public uint dwSize;
    }

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstRadio(ref BLUETOOTH_FIND_RADIO_PARAMS pbtfrp, out IntPtr phRadio);

    [DllImport("bthprops.cpl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindNextRadio(IntPtr hFind, out IntPtr phRadio);

    [DllImport("bthprops.cpl")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindRadioClose(IntPtr hFind);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, ref ulong lpInBuffer, int nInBufferSize,
        IntPtr lpOutBuffer, int nOutBufferSize, out int lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
