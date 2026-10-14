namespace AkelEtiket.Design.Tools;

/// <summary>Araç hata kodları (ajanların programatik olarak ayırt edebilmesi için sabit, İngilizce).</summary>
public static class ToolErrorCodes
{
    public const string InvalidArgument = "INVALID_ARGUMENT";
    public const string NotFound = "NOT_FOUND";
    public const string VersionConflict = "VERSION_CONFLICT";
    public const string PermissionDenied = "PERMISSION_DENIED";
    public const string ApprovalRequired = "APPROVAL_REQUIRED";
    public const string Locked = "ELEMENT_LOCKED";
    public const string InvalidBarcode = "INVALID_BARCODE";
    public const string Unavailable = "UNAVAILABLE";
    public const string Internal = "INTERNAL_ERROR";
}

/// <summary>Araç çağrısının beklenen bir nedenle başarısız olması (mesaj Türkçe, ajana aynen iletilir).</summary>
public sealed class ToolException : Exception
{
    public ToolException(string code, string message, object? details = null) : base(message)
    {
        Code = code;
        Details = details;
    }

    public string Code { get; }

    /// <summary>Ek makine okunur bilgi (ör. güncel sürüm).</summary>
    public object? Details { get; }
}
