using PrinterShareFixer.Core.Models;
using PrinterShareFixer.Core.Runtime;

namespace PrinterShareFixer.Core.Profiles;

/// <summary>Windows 10 打印机共享修复方案。</summary>
internal static class Windows10Profile
{
    public static RepairProfile Create()
    {
        var services = ServiceCatalog.CommonRequired
            .Concat(ServiceCatalog.Windows10Extra)
            .ToArray();

        return new RepairProfile
        {
            Key = "win10",
            DisplayName = "Windows 10",
            Description = "面向 Windows 10（1507 – 22H2）的共享修复：服务、网络发现、防火墙、来宾访问、打印 RPC 兼容性。",
            Steps =
            [
                CommonSteps.Elevation(),
                CommonSteps.RegistryBackup(),
                CommonSteps.Services(services, "启动并配置 Windows 10 共享所需服务"),
                CommonSteps.NetworkProfile(),
                CommonSteps.Netbios(),
                CommonSteps.Firewall(allowAnyRemoteAddress: false, allowRpcDynamicPorts: true),
                CommonSteps.GuestAccess(),
                CommonSteps.SmbCompatibility(),
                CommonSteps.PrintRpcPrivacy(),
                CommonSteps.PointAndPrintPolicy(),
                CommonSteps.EnableSmb1(supported: true),
                CommonSteps.RestartServices(),
                CommonSteps.Verify(),
            ],
        };
    }
}
