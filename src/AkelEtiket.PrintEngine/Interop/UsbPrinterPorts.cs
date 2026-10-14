using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace AkelEtiket.PrintEngine.Interop;

/// <summary>
/// USB yazıcı portlarının (USB001, USB002…) şu anda bağlı bir aygıta karşılık gelip gelmediğini bulur.
/// Windows'un usbmon port izleyicisi de aynı eşlemeyi kullanır: usbprint aygıt arabiriminin
/// "Device Parameters" anahtarındaki "Base Name" + "Port Number" (ör. USB + 1 → USB001).
/// Yalnızca o an TAKILI (DIGCF_PRESENT) aygıtlar listelenir; bu sayede kablo çıkarıldığında anlaşılır.
/// </summary>
internal static partial class UsbPrinterPorts
{
    [GeneratedRegex(@"^USB\d{3}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UsbPortPattern();

    public static bool IsUsbPortName(string? portName) =>
        !string.IsNullOrWhiteSpace(portName) && UsbPortPattern().IsMatch(portName.Trim());

    /// <summary>
    /// Bağlı USB yazıcı portlarının adları. Sorgu başarısız olursa null döner (durum bilinmiyor);
    /// bu durumda çağıran taraf yazıcıyı "bağlı değil" saymamalıdır.
    /// </summary>
    public static IReadOnlySet<string>? GetPresentPortNames()
    {
        var guid = NativeMethods.GuidDevInterfaceUsbPrint;
        IntPtr devInfo = NativeMethods.SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero,
            NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);
        if (devInfo == NativeMethods.InvalidHandleValue || devInfo == IntPtr.Zero) return null;

        var ports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            for (int index = 0; index < 256; index++)
            {
                var data = new NativeMethods.SP_DEVICE_INTERFACE_DATA
                {
                    cbSize = Marshal.SizeOf<NativeMethods.SP_DEVICE_INTERFACE_DATA>(),
                };
                if (!NativeMethods.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref guid, index, ref data))
                    break; // ERROR_NO_MORE_ITEMS

                IntPtr hKey = NativeMethods.SetupDiOpenDeviceInterfaceRegKey(devInfo, ref data, 0, NativeMethods.KEY_READ);
                if (hKey == NativeMethods.InvalidHandleValue || hKey == IntPtr.Zero) continue;

                using var key = RegistryKey.FromHandle(new SafeRegistryHandle(hKey, ownsHandle: true));
                if (key.GetValue("Port Number") is int portNumber && portNumber > 0)
                {
                    string baseName = key.GetValue("Base Name") as string is { Length: > 0 } b ? b : "USB";
                    ports.Add(baseName + portNumber.ToString("000", CultureInfo.InvariantCulture));
                }
            }
            return ports;
        }
        catch (Exception ex) when (ex is ExternalException or UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return null;
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(devInfo);
        }
    }

    /// <summary>USB portu bağlı mı? USB portu değilse veya bilinemiyorsa null.</summary>
    public static bool? IsConnected(string? portName, IReadOnlySet<string>? presentPorts)
    {
        if (!IsUsbPortName(portName) || presentPorts is null) return null;
        return presentPorts.Contains(portName!.Trim());
    }
}
