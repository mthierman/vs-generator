using System.Diagnostics;

public class VCPkg
{
    static VCPkg()
    {
        if (string.IsNullOrEmpty(Paths.vcpkg))
            throw new Exception("VCPKG_ROOT is not set");
    }

    public static void Start(string arguments)
    {
        var process_start_info = new ProcessStartInfo
        {
            FileName = "vcpkg",
            Arguments = arguments
        };

        process_start_info.EnvironmentVariables["VCPKG_DEFAULT_TRIPLET"] = "x64-windows-static-md";
        process_start_info.EnvironmentVariables["VCPKG_DEFAULT_HOST_TRIPLET"] = "x64-windows-static-md";

        Process.Start(process_start_info)?.WaitForExit();
    }
}
