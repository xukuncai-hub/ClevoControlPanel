using System;

namespace ClevoControlPanel.Core.Services;

/// <summary>
/// Probes the native driver and instantiates the live controller, falling back to
/// the simulator when the machine is not a supported Clevo or the bridge fails.
/// </summary>
public static class HwServiceFactory
{
    public static IHwService Create(out string statusMessage) =>
        Create(DchuNative.Instance, out statusMessage);

    public static IHwService Create(IDchuNative native, out string statusMessage)
    {
        if (native == null)
            throw new ArgumentNullException(nameof(native));

        try
        {
            if (!native.Probe())
            {
                statusMessage = "未检测到 Clevo 硬件驱动 (InsydeDCHU.dll)，已启用模拟模式。";
                return new MockHwService();
            }

            var service = new ClevoHwService(native);
            var telemetry = service.GetTelemetry();

            bool plausible =
                telemetry.CpuTemperatureCelsius is > 0 and < 120 ||
                telemetry.Fan1Rpm > 0 ||
                telemetry.Fan2Rpm > 0;

            if (!plausible)
            {
                service.Dispose();
                statusMessage = "硬件驱动无有效遥测数据，已回退到模拟模式。";
                return new MockHwService();
            }

            statusMessage = "已连接 Clevo 硬件 (InsydeDCHU.dll / ACPI Bridge)。";
            return service;
        }
        catch (Exception ex)
        {
            statusMessage = $"硬件初始化失败，已回退到模拟模式：{ex.Message}";
            return new MockHwService();
        }
    }

    public static IHwService CreateMock() => new MockHwService();
}
