using System;
using System.Threading;
using System.Threading.Tasks;
using ClevoControlPanel.Core.Models;

namespace ClevoControlPanel.Core.Services;

/// <summary>
/// Shared telemetry polling engine. Samples on a dedicated background task with a
/// drift-free <see cref="PeriodicTimer"/> and publishes immutable snapshots.
/// </summary>
public abstract class HwServiceBase : IHwService
{
    private readonly object _pollGate = new();
    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private TelemetryData _latest = new();
    private int _hasSample;

    public event EventHandler<TelemetryData>? TelemetryUpdated;

    public abstract bool IsLiveHardware { get; }
    public abstract HardwareCapabilities Capabilities { get; }

    public bool IsTelemetryRunning
    {
        get
        {
            lock (_pollGate)
            {
                return _pollTask is { IsCompleted: false };
            }
        }
    }

    protected abstract TelemetryData ReadTelemetryCore();

    public TelemetryData GetTelemetry()
    {
        if (Volatile.Read(ref _hasSample) == 0)
        {
            try
            {
                Publish(ReadTelemetryCore());
            }
            catch
            {
                // Keep the default snapshot; the UI will retry on the next poll.
            }
        }

        return Volatile.Read(ref _latest);
    }

    public void StartTelemetry(int intervalMs = 1000)
    {
        int interval = Math.Max(100, intervalMs);

        lock (_pollGate)
        {
            if (_pollTask is { IsCompleted: false })
                return;

            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            _pollTask = Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            Publish(ReadTelemetryCore());
                        }
                        catch
                        {
                            // Transient ACPI failures must never terminate the polling loop.
                        }

                        await Task.Delay(interval, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                }
            }, token);
        }
    }

    public void StopTelemetry()
    {
        lock (_pollGate)
        {
            if (_cts == null)
                return;

            try
            {
                _cts.Cancel();
            }
            catch
            {
            }

            _cts.Dispose();
            _cts = null;
            _pollTask = null;
        }
    }

    private void Publish(TelemetryData data)
    {
        Volatile.Write(ref _latest, data);
        Volatile.Write(ref _hasSample, 1);

        try
        {
            TelemetryUpdated?.Invoke(this, data);
        }
        catch
        {
            // A faulty subscriber must not break sampling.
        }
    }

    public abstract PowerMode GetPowerMode();
    public abstract void SetPowerMode(PowerMode mode);
    public abstract FanMode GetFanMode();
    public abstract void SetFanMode(FanMode mode);
    public abstract byte GetFanOffset();
    public abstract void SetFanOffset(byte offsetPercent);
    public abstract bool GetTurboFan();
    public abstract void SetTurboFan(bool enabled);

    public abstract void SetKeyboardRgbColor(RgbColor color, KeyboardZone zone = KeyboardZone.All);
    public abstract void SetKeyboardRgbEffect(RgbEffectMode mode, byte speed = 5);
    public abstract void SetKeyboardLiveColors(RgbColor left, RgbColor mid, RgbColor right);
    public abstract void SetKeyboardBrightness(byte level);
    public abstract void SetKeyboardPower(bool on);
    public abstract void SetKeyboardSleepTimeout(int seconds);
    public abstract KeyboardLightingState GetKeyboardState();
    public abstract RgbColor GetKeyboardZoneColor(KeyboardZone zone);

    public virtual void Dispose()
    {
        StopTelemetry();
        GC.SuppressFinalize(this);
    }
}
