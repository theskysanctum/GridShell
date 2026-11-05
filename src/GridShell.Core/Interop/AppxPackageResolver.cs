using System.Linq;
using Windows.Management.Deployment;

namespace GridShell.Core.Interop;

/// <summary>Resolves an MSIX-packaged app's current install folder by its package family name
/// (which stays constant across updates), via the same package catalog Windows itself uses —
/// not filesystem enumeration, since WindowsApps' own ACLs block listing it even though a
/// specific already-known subpath inside it stays readable/executable.</summary>
public static class AppxPackageResolver
{
    public static string? ResolveExePath(string packageFamilyName, string relativeExePath)
    {
        try
        {
            var packageManager = new PackageManager();
            var package = packageManager.FindPackagesForUser(string.Empty)
                .FirstOrDefault(p => string.Equals(p.Id.FamilyName, packageFamilyName, StringComparison.OrdinalIgnoreCase));

            return package is null ? null : System.IO.Path.Combine(package.InstalledLocation.Path, relativeExePath);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }
}
