using System;
using System.Collections.Generic;
using ClevoControlPanel.Core.Native;
using ClevoControlPanel.Core.Services;

namespace ClevoControlPanel.Core.Tests;

/// <summary>
/// In-memory IDchuNative transport used to verify live controller logic without hardware.
/// </summary>
internal sealed class FakeDchuNative : IDchuNative
{
    public bool Supported { get; set; } = true;
    public bool FailWrites { get; set; }
    public bool FailCommands { get; set; }
    public int TelemetryStatus { get; set; } = 12;
    public byte[] Settings { get; } = new byte[1280];
    public byte[] TelemetryBuffer { get; } = new byte[256];
    public List<(int Command, byte[] Payload)> Commands { get; } = new();

    public bool Probe() => Supported;

    public int ReadTelemetry(byte[] buffer)
    {
        Array.Copy(TelemetryBuffer, buffer, buffer.Length);
        return TelemetryStatus;
    }

    public bool TryReadSettings(int key, int length, out byte[] data)
    {
        if (key < 0 || key + length > Settings.Length)
        {
            data = Array.Empty<byte>();
            return false;
        }

        data = new byte[length];
        Array.Copy(Settings, key, data, 0, length);
        return true;
    }

    public bool TryWriteSettings(int key, byte[] data)
    {
        if (FailWrites || key < 0 || key + data.Length > Settings.Length)
            return false;

        Array.Copy(data, 0, Settings, key, data.Length);
        return true;
    }

    public bool TrySendCommand(int command, byte[] buffer)
    {
        if (FailCommands)
            return false;

        Commands.Add((command, (byte[])buffer.Clone()));
        return true;
    }

    public Dictionary<int, int> WmiValues { get; } = new();
    public Dictionary<int, byte[]> WmiBuffers { get; } = new();

    public bool TryReadWmi(int command, out int value)
    {
        if (WmiValues.TryGetValue(command, out value))
            return true;

        value = 0;
        return false;
    }

    public bool TryReadWmiBuffer(int command, byte[] buffer)
    {
        if (!WmiBuffers.TryGetValue(command, out byte[]? data))
            return false;

        Array.Copy(data, buffer, Math.Min(data.Length, buffer.Length));
        return true;
    }

    public static FakeDchuNative CreateInitialized()
    {
        var fake = new FakeDchuNative();

        fake.Settings[NativeConstants.AppSettings.PowerMode] = (byte)ClevoControlPanel.Core.Models.PowerMode.Entertainment;
        fake.Settings[NativeConstants.AppSettings.FanMode] = (byte)ClevoControlPanel.Core.Models.FanMode.Auto;
        fake.Settings[NativeConstants.AppSettings.FanOffset] = 10;
        fake.Settings[NativeConstants.AppSettings.TurboFan_Status] = 0;
        fake.Settings[NativeConstants.AppSettings.NoiselessFan_status] = 0;
        fake.Settings[NativeConstants.AppSettings.DttStatus] = 0;

        fake.Settings[NativeConstants.AppSettings.FanMode_Quiet] = (byte)ClevoControlPanel.Core.Models.FanMode.Silent;
        fake.Settings[NativeConstants.AppSettings.FanMode_PowerSaving] = (byte)ClevoControlPanel.Core.Models.FanMode.Auto;
        fake.Settings[NativeConstants.AppSettings.FanMode_Performance] = (byte)ClevoControlPanel.Core.Models.FanMode.Maximum;
        fake.Settings[NativeConstants.AppSettings.FanMode_Entertainment] = (byte)ClevoControlPanel.Core.Models.FanMode.Auto;
        fake.Settings[NativeConstants.AppSettings.FanOffset_Quiet] = 0;
        fake.Settings[NativeConstants.AppSettings.FanOffset_PowerSaving] = 0;
        fake.Settings[NativeConstants.AppSettings.FanOffset_Performance] = 50;
        fake.Settings[NativeConstants.AppSettings.FanOffset_Entertainment] = 0;

        fake.Settings[NativeConstants.AppSettings.KbEffect] = (byte)ClevoControlPanel.Core.Models.RgbEffectMode.Static;
        fake.Settings[NativeConstants.AppSettings.KbSpeed] = 5;
        fake.Settings[NativeConstants.AppSettings.KbBrightness] = 4;
        fake.Settings[NativeConstants.AppSettings.KbSleepTimerTrigger] = 1;
        fake.Settings[NativeConstants.AppSettings.KbSleepTimerHour] = 0;
        fake.Settings[NativeConstants.AppSettings.KbSleepTimerMinute] = 0;
        fake.Settings[NativeConstants.AppSettings.KbSleepTimerSecond] = 30;
        fake.Settings[NativeConstants.AppSettings.KbPowerStatus] = 1;
        SetColor(fake, NativeConstants.AppSettings.KbAllColor, 0, 0, 200);
        SetColor(fake, NativeConstants.AppSettings.KbLeftColor, 0, 0, 200);
        SetColor(fake, NativeConstants.AppSettings.KbMidColor, 0, 0, 200);
        SetColor(fake, NativeConstants.AppSettings.KbRightColor, 0, 0, 200);

        // CPU tach 287 -> 7513 RPM, GPU tach 293 -> 7359 RPM, CPU 87 C, GPU 76 C.
        fake.TelemetryBuffer[2] = 0x01;
        fake.TelemetryBuffer[3] = 0x1F;
        fake.TelemetryBuffer[4] = 0x01;
        fake.TelemetryBuffer[5] = 0x25;
        fake.TelemetryBuffer[16] = 255;
        fake.TelemetryBuffer[18] = 87;
        fake.TelemetryBuffer[19] = 255;
        fake.TelemetryBuffer[21] = 76;

        return fake;
    }

    private static void SetColor(FakeDchuNative fake, int key, byte r, byte g, byte b)
    {
        fake.Settings[key] = r;
        fake.Settings[key + 1] = g;
        fake.Settings[key + 2] = b;
    }
}
