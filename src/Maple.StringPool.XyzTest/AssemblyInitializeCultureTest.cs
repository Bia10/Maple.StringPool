using System.Globalization;

namespace Maple.StringPool.XyzTest;

public static class AssemblyInitializeCultureTest
{
    [Before(Assembly)]
    public static void SetInvariantCulture()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
    }
}
