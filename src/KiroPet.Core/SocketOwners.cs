using System.Runtime.InteropServices;

namespace KiroPet;

internal static class SocketOwners
{
    public static IReadOnlyDictionary<int, int> Read()
    {
        var owners = new Dictionary<int, int>();
        if (!OperatingSystem.IsWindows()) return owners;
        ReadFamily(2, 24, 8, 20, owners);
        ReadFamily(23, 56, 20, 52, owners);
        return owners;
    }

    private static void ReadFamily(int family, int rowSize, int portOffset, int pidOffset, Dictionary<int, int> owners)
    {
        int size = 0;
        if (GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, 3, 0) != 122 || size <= 4) return;
        IntPtr table = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(table, ref size, false, family, 3, 0) != 0) return;
            int count = Marshal.ReadInt32(table);
            for (int index = 0; index < count && 4L + (index + 1L) * rowSize <= size; index++)
            {
                IntPtr row = IntPtr.Add(table, 4 + index * rowSize);
                bool local;
                if (family == 2)
                {
                    int address = Marshal.ReadInt32(row, 4);
                    local = address is 0 or 0x0100007f;
                }
                else
                {
                    bool zero = true;
                    for (int i = 0; i < 15; i++) zero &= Marshal.ReadByte(row, i) == 0;
                    local = zero && Marshal.ReadByte(row, 15) <= 1;
                }
                if (!local) continue;
                int raw = Marshal.ReadInt32(row, portOffset);
                int port = (raw & 255) * 256 + ((raw >> 8) & 255);
                int pid = Marshal.ReadInt32(row, pidOffset);
                if (pid <= 0) continue;
                if (!owners.TryAdd(port, pid) && owners[port] != pid) owners[port] = 0;
            }
        }
        finally { Marshal.FreeHGlobal(table); }
    }

    [DllImport("iphlpapi.dll")]
    private static extern int GetExtendedTcpTable(IntPtr table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool sorted, int family, int tableClass, int reserved);
}
