using System.Diagnostics;
using Microsoft.VisualStudio.Setup.Configuration;

namespace cxx;

public static class VSWhere
{
    // public static async Task<int> Run(params string[]? args)
    // {
    //     var startInfo = new ProcessStartInfo
    //     {
    //         FileName = VisualStudio.VSWherePath,
    //         UseShellExecute = false,
    //         RedirectStandardOutput = false,
    //         RedirectStandardError = false,
    //         CreateNoWindow = false
    //     };

    //     return await ExternalCommand.Run(startInfo, args);
    // }

    // public static async Task<int> Print()
    // {
    //     var setupConfig = new SetupConfiguration();
    //     var enumInstances = setupConfig.EnumAllInstances(); // returns IEnumSetupInstances

    //     ISetupInstance[] instances = new ISetupInstance[1];
    //     int fetched = 0;

    //     ISetupInstance? latestInstance = null;

    //     // Loop until Next returns 0 items
    //     do
    //     {
    //         enumInstances.Next(1, instances, out fetched); // returns void, fetched tells how many items
    //         if (fetched == 0) break;

    //         var instance = instances[0];
    //         if (latestInstance == null ||
    //             string.Compare(instance.GetInstallationVersion(), latestInstance.GetInstallationVersion(), StringComparison.Ordinal) > 0)
    //         {
    //             latestInstance = instance;
    //         }

    //     } while (fetched > 0);

    //     if (latestInstance != null)
    //     {
    //         Console.WriteLine($"Latest VS install path: {latestInstance.GetInstallationPath()}");
    //     }

    //     return 0;
    // }
}
