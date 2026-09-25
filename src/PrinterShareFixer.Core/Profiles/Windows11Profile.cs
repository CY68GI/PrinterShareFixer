using PrinterShareFixer.Core.Models;
using PrinterShareFixer.Core.Runtime;

namespace PrinterShareFixer.Core.Profiles;

/// <summary>Windows 11 打印机共享修复方案。</summary>
internal static class Windows11Profile
{
    public static RepairProfile Create()
    {
        return new RepairProfile
        {
            Key = "win11",
            DisplayName = "Windows 11",
            Description = "面向 Windows 11（21H2 – 25H2，含 24H2 打印后台处理程序变更）的共享修复：服务、网络发现、防火墙、来宾访问、打印 RPC 与驱动安装策略。",
            Steps =
            [
                CommonSteps.Elevation(),
                CommonSteps.RegistryBackup(),
                CommonSteps.Services(ServiceCatalog.CommonRequired, "启动并配置 Windows 11 共享所需服务"),
                CommonSteps.NetworkProfile(),
                CommonSteps.Netbios(),
                CommonSteps.Firewall(allowAnyRemoteAddress: false, allowRpcDynamicPorts: true),
                CommonSteps.GuestAccess(),
                CommonSteps.SmbCompatibility(),
                CommonSteps.PrintRpcPrivacy(),
                CommonSteps.PointAndPrintPolicy(),
                CommonSteps.Win11PrintRpcPolicy(),
                CommonSteps.ProtectedPrintMode(),
                CommonSteps.RestartServices(),
                CommonSteps.Verify(),
            ],
        };
    }
}
