using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace AkelEtiket.Editor.Ai;

/// <summary>
/// macOS Anahtar Zinciri'nde (kullanıcının oturum zinciri) genel parola öğesi olarak küçük bir gizli değer saklar.
/// Security.framework'ün SecItem çağrıları kullanılır; öğeyi yalnızca oluşturan uygulama sormadan okuyabilir.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacKeychain
{
    private const string SecurityLibrary = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundationLibrary = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const int Success = 0;
    private const int ItemNotFound = -25300;
    private const int DuplicateItem = -25299;
    private const uint Utf8Encoding = 0x08000100;

    private static readonly IntPtr Security = NativeLibrary.Load(SecurityLibrary);
    private static readonly IntPtr CoreFoundation = NativeLibrary.Load(CoreFoundationLibrary);

    /// <summary>Saklı değer; öğe yoksa null. Zincire erişilemezse <see cref="SecretStorageException"/>.</summary>
    public static byte[]? Find(string service, string account)
    {
        IntPtr query = CreateQuery(service, account);
        try
        {
            CFDictionarySetValue(query, Constant(Security, "kSecReturnData"), Constant(CoreFoundation, "kCFBooleanTrue"));
            CFDictionarySetValue(query, Constant(Security, "kSecMatchLimit"), Constant(Security, "kSecMatchLimitOne"));
            int status = SecItemCopyMatching(query, out IntPtr data);
            if (status == ItemNotFound) return null;
            if (status != Success) throw SecretStorageException.Keychain(status);
            try
            {
                var bytes = new byte[(int)CFDataGetLength(data)];
                Marshal.Copy(CFDataGetBytePtr(data), bytes, 0, bytes.Length);
                return bytes;
            }
            finally
            {
                CFRelease(data);
            }
        }
        finally
        {
            CFRelease(query);
        }
    }

    /// <summary>Yeni öğe ekler. Aynı öğe zaten varsa false döner ve var olana dokunmaz.</summary>
    public static bool TryAdd(string service, string account, byte[] secret)
    {
        IntPtr attributes = CreateQuery(service, account);
        IntPtr data = CFDataCreate(IntPtr.Zero, secret, secret.Length);
        try
        {
            CFDictionarySetValue(attributes, Constant(Security, "kSecValueData"), data);
            int status = SecItemAdd(attributes, IntPtr.Zero);
            if (status == DuplicateItem) return false;
            if (status != Success) throw SecretStorageException.Keychain(status);
            return true;
        }
        finally
        {
            CFRelease(data);
            CFRelease(attributes);
        }
    }

    /// <summary>Öğeyi siler; öğe yoksa false.</summary>
    public static bool Delete(string service, string account)
    {
        IntPtr query = CreateQuery(service, account);
        try
        {
            int status = SecItemDelete(query);
            if (status == ItemNotFound) return false;
            if (status != Success) throw SecretStorageException.Keychain(status);
            return true;
        }
        finally
        {
            CFRelease(query);
        }
    }

    /// <summary>Saklı değeri döndürür; yoksa rastgele bir değer üretip saklar (aynı anda başlayan iki işlem aynı değeri alır).</summary>
    public static byte[] GetOrCreate(string service, string account, int length)
    {
        if (Find(service, account) is { } existing) return existing;
        byte[] created = RandomNumberGenerator.GetBytes(length);
        if (TryAdd(service, account, created)) return created;
        return Find(service, account) ?? throw SecretStorageException.Keychain(ItemNotFound);
    }

    private static IntPtr CreateQuery(string service, string account)
    {
        IntPtr query = CFDictionaryCreateMutable(IntPtr.Zero, 0,
            NativeLibrary.GetExport(CoreFoundation, "kCFTypeDictionaryKeyCallBacks"),
            NativeLibrary.GetExport(CoreFoundation, "kCFTypeDictionaryValueCallBacks"));
        CFDictionarySetValue(query, Constant(Security, "kSecClass"), Constant(Security, "kSecClassGenericPassword"));
        SetString(query, "kSecAttrService", service);
        SetString(query, "kSecAttrAccount", account);
        return query;
    }

    private static void SetString(IntPtr dictionary, string key, string value)
    {
        IntPtr text = CFStringCreateWithCString(IntPtr.Zero, value, Utf8Encoding);
        try
        {
            // Sözlük değeri kendisi tutar (retain); buradaki başvuru hemen bırakılır.
            CFDictionarySetValue(dictionary, Constant(Security, key), text);
        }
        finally
        {
            CFRelease(text);
        }
    }

    /// <summary>Çerçevenin dışa açtığı sabit (ör. kSecClass): simge bir işaretçi değişkenidir, değeri okunur.</summary>
    private static IntPtr Constant(IntPtr library, string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(library, name));

    [DllImport(SecurityLibrary)]
    private static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);

    [DllImport(SecurityLibrary)]
    private static extern int SecItemAdd(IntPtr attributes, IntPtr result);

    [DllImport(SecurityLibrary)]
    private static extern int SecItemDelete(IntPtr query);

    [DllImport(CoreFoundationLibrary)]
    private static extern IntPtr CFDictionaryCreateMutable(IntPtr allocator, nint capacity, IntPtr keyCallBacks, IntPtr valueCallBacks);

    [DllImport(CoreFoundationLibrary)]
    private static extern void CFDictionarySetValue(IntPtr dictionary, IntPtr key, IntPtr value);

    [DllImport(CoreFoundationLibrary)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, uint encoding);

    [DllImport(CoreFoundationLibrary)]
    private static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);

    [DllImport(CoreFoundationLibrary)]
    private static extern nint CFDataGetLength(IntPtr data);

    [DllImport(CoreFoundationLibrary)]
    private static extern IntPtr CFDataGetBytePtr(IntPtr data);

    [DllImport(CoreFoundationLibrary)]
    private static extern void CFRelease(IntPtr value);
}
