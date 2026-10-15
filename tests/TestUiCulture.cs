using System.Globalization;
using System.Runtime.CompilerServices;
internal static class TestUiCulture
{
    [ModuleInitializer] internal static void Initialize() => CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("tr");
}
