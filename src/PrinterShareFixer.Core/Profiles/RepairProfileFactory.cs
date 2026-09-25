using PrinterShareFixer.Core.Models;
using PrinterShareFixer.Core.Runtime;

namespace PrinterShareFixer.Core.Profiles;

/// <summary>按“本机角色 + 本机系统”组合出四套修复方案。</summary>
internal static class RepairProfileFactory
{
    public static RepairProfile Create(RepairRole role, string osKey)
    {
        var isWindows11 = osKey.Equals("win11", StringComparison.OrdinalIgnoreCase);
        return role == RepairRole.Provider
            ? CreateProvider(role, osKey, isWindows11)
            : CreateConsumer(role, osKey, isWindows11);
    }

    private static RepairProfile CreateProvider(RepairRole role, string osKey, bool isWindows11)
    {
        var services = isWindows11
            ? ServiceCatalog.CommonRequired
            : ServiceCatalog.CommonRequired.Concat(ServiceCatalog.Windows10Extra).ToArray();

        var steps = new List<RepairStep>
        {
            CommonSteps.Elevation(),
            CommonSteps.RegistryBackup(),
            CommonSteps.Services(services, $"启动并配置 {DisplayOs(osKey)} 服务端共享所需服务"),
            CommonSteps.NetworkProfile(),
            CommonSteps.Netbios(),
            CommonSteps.Firewall(allowAnyRemoteAddress: false, allowRpcDynamicPorts: true),
            CommonSteps.GuestAccess(),
            CommonSteps.SmbCompatibility(),
            CommonSteps.PrintRpcPrivacy(),
            CommonSteps.PointAndPrintPolicy(),
        };

        if (isWindows11)
        {
            steps.Add(CommonSteps.Win11PrintRpcPolicy());
            steps.Add(CommonSteps.ProtectedPrintMode());
        }
        else
        {
            steps.Add(CommonSteps.EnableSmb1(supported: true));
        }

        steps.Add(RoleSteps.ProviderShareCheck());
        steps.Add(CommonSteps.RestartServices());
        steps.Add(CommonSteps.Verify(role));

        return new RepairProfile
        {
            Key = $"{osKey}-provider",
            Role = role,
            OsKey = osKey,
            DisplayName = DisplayOs(osKey),
            ActionTitle = $"为 {DisplayOs(osKey)} 电脑开放共享",
            Description = $"本机接有打印机，按 {DisplayOs(osKey)} 的服务端配置开放共享，让其他电脑能连上来。",
            Steps = steps,
        };
    }

    private static RepairProfile CreateConsumer(RepairRole role, string osKey, bool isWindows11)
    {
        var steps = new List<RepairStep>
        {
            CommonSteps.Elevation(),
            CommonSteps.RegistryBackup(),
            CommonSteps.Services(ServiceCatalog.ClientRequired, $"启动并配置 {DisplayOs(osKey)} 客户端连接所需服务"),
            CommonSteps.NetworkProfile(),
            CommonSteps.Netbios(),
            CommonSteps.Firewall(allowAnyRemoteAddress: false, allowRpcDynamicPorts: true),
            CommonSteps.SmbCompatibility(),
            RoleSteps.ConsumerGuestPolicy(),
            CommonSteps.PrintRpcPrivacy(),
            CommonSteps.PointAndPrintPolicy(),
            RoleSteps.ConsumerCleanStale(),
        };

        if (isWindows11)
        {
            steps.Add(CommonSteps.Win11PrintRpcPolicy());
            steps.Add(CommonSteps.ProtectedPrintMode());
        }
        else
        {
            steps.Add(CommonSteps.EnableSmb1(supported: true));
        }

        steps.Add(CommonSteps.RestartServices());
        steps.Add(RoleSteps.ConsumerConnectivity());
        steps.Add(CommonSteps.Verify(role));

        return new RepairProfile
        {
            Key = $"{osKey}-consumer",
            Role = role,
            OsKey = osKey,
            DisplayName = DisplayOs(osKey),
            ActionTitle = $"连接 {DisplayOs(osKey)} 电脑上的打印机",
            Description = $"本机没有打印机，按 {DisplayOs(osKey)} 的客户端配置修复，解决连不上别人共享打印机的问题。",
            Steps = steps,
        };
    }

    private static string DisplayOs(string osKey) =>
        osKey.Equals("win11", StringComparison.OrdinalIgnoreCase) ? "Windows 11" : "Windows 10";
}
