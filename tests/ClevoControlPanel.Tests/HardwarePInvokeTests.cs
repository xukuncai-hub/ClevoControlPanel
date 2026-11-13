using System;
using Xunit;
using ClevoControlPanel.Tests.Mocks;

namespace ClevoControlPanel.Tests
{
    public static class SafeDchuWrapper
    {
        public const int RequiredTelemetryBufferSize = 256;

        public static void ValidateTelemetryBuffer(byte[] buffer)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer), "Buffer cannot be null.");
            if (buffer.Length < RequiredTelemetryBufferSize)
                throw new ArgumentException($"Buffer must be at least {RequiredTelemetryBufferSize} bytes, got {buffer.Length}.", nameof(buffer));
        }

        public static (int Page, int Offset) GetPageAndOffset(int flatIndex)
        {
            if (flatIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(flatIndex), "Flat key index cannot be negative.");
            return (flatIndex / 256, flatIndex % 256);
        }

        public static int GetFlatIndex(int page, int offset)
        {
            if (page < 0) throw new ArgumentOutOfRangeException(nameof(page));
            if (offset < 0 || offset >= 256) throw new ArgumentOutOfRangeException(nameof(offset));
            return page * 256 + offset;
        }
    }

    [Trait("Tier", "1")]
    [Trait("Tier", "2")]
    public class HardwarePInvokeTests
    {
        [Fact]
        public void BufferSafety_NullBuffer_ThrowsArgumentNullException()
        {
            byte[]? nullBuffer = null;
            var ex = Assert.Throws<ArgumentNullException>(() => SafeDchuWrapper.ValidateTelemetryBuffer(nullBuffer!));
            Assert.Contains("buffer", ex.ParamName, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BufferSafety_ZeroLengthBuffer_ThrowsArgumentException()
        {
            byte[] emptyBuffer = Array.Empty<byte>();
            var ex = Assert.Throws<ArgumentException>(() => SafeDchuWrapper.ValidateTelemetryBuffer(emptyBuffer));
            Assert.Contains("256", ex.Message);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(64)]
        [InlineData(128)]
        [InlineData(255)]
        public void BufferSafety_UndersizedBuffers_ThrowsArgumentException(int size)
        {
            byte[] undersized = new byte[size];
            var ex = Assert.Throws<ArgumentException>(() => SafeDchuWrapper.ValidateTelemetryBuffer(undersized));
            Assert.Contains("256", ex.Message);
        }

        [Fact]
        public void BufferSafety_Exact256Bytes_PassesValidation()
        {
            byte[] buffer = new byte[256];
            var exception = Record.Exception(() => SafeDchuWrapper.ValidateTelemetryBuffer(buffer));
            Assert.Null(exception);
        }

        [Fact]
        public void BufferSafety_512ByteOversized_SucceedsSafelyWithoutCorruption()
        {
            byte[] oversized = new byte[512];
            for (int i = 0; i < oversized.Length; i++) oversized[i] = 0xAA;

            SafeDchuWrapper.ValidateTelemetryBuffer(oversized);

            // Verify trailing bytes remain unaffected
            Assert.Equal(0xAA, oversized[300]);
            Assert.Equal(0xAA, oversized[511]);
        }

        [Theory]
        [InlineData(257, 1, 1)]      // PowerMode: Page 1, Offset 1
        [InlineData(1029, 4, 5)]    // FanMode: Page 4, Offset 5
        [InlineData(1031, 4, 7)]    // FanOffset: Page 4, Offset 7
        [InlineData(1032, 4, 8)]    // TurboFan: Page 4, Offset 8
        [InlineData(1033, 4, 9)]    // NoiselessFan: Page 4, Offset 9
        [InlineData(1094, 4, 70)]   // FanMode_Quiet
        [InlineData(1095, 4, 71)]   // FanMode_PowerSaving
        [InlineData(1096, 4, 72)]   // FanMode_Performance
        [InlineData(1097, 4, 73)]   // FanMode_Entertainment
        [InlineData(1098, 4, 74)]   // FanOffset_Quiet
        [InlineData(1099, 4, 75)]   // FanOffset_PowerSaving
        [InlineData(1100, 4, 76)]   // FanOffset_Performance
        [InlineData(1101, 4, 77)]   // FanOffset_Entertainment
        [InlineData(544, 2, 32)]    // Keyboard Mode
        [InlineData(547, 2, 35)]    // Keyboard Brightness
        public void AppSettingsAddressing_FlatKeyMapping_ComputesAccuratePageAndOffset(int flatKey, int expectedPage, int expectedOffset)
        {
            var (page, offset) = SafeDchuWrapper.GetPageAndOffset(flatKey);
            Assert.Equal(expectedPage, page);
            Assert.Equal(expectedOffset, offset);

            int roundTripKey = SafeDchuWrapper.GetFlatIndex(page, offset);
            Assert.Equal(flatKey, roundTripKey);
        }

        [Fact]
        public void AppSettingsAddressing_NegativeKey_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SafeDchuWrapper.GetPageAndOffset(-1));
        }

        [Fact]
        public void AppSettingsAddressing_Offset256_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SafeDchuWrapper.GetFlatIndex(1, 256));
        }

        [Fact]
        public void NativeBridge_VirtualAppSettings_ReadWriteIntegrity()
        {
            var mock = new MockHwService();
            int flatKey = 1031; // FanOffset
            var (page, offset) = SafeDchuWrapper.GetPageAndOffset(flatKey);

            mock.VirtualAppSettings[page * 256 + offset] = 75;

            byte readValue = mock.VirtualAppSettings[flatKey];
            Assert.Equal(75, readValue);
        }

        [Fact]
        public void NativeBridge_VirtualAppSettings_CrossPageIsolation()
        {
            var mock = new MockHwService();

            // Write to Page 1 Offset 1 (Flat 257)
            mock.VirtualAppSettings[257] = 0x55;
            // Write to Page 2 Offset 1 (Flat 513)
            mock.VirtualAppSettings[513] = 0xAA;

            Assert.Equal(0x55, mock.VirtualAppSettings[257]);
            Assert.Equal(0xAA, mock.VirtualAppSettings[513]);
            Assert.NotEqual(mock.VirtualAppSettings[257], mock.VirtualAppSettings[513]);
        }

        [Fact]
        public void NativeBridge_CommandPackets_4ByteBoundaryIntegrity()
        {
            var mock = new MockHwService();
            mock.SetPowerMode(PowerMode.Performance);

            Assert.Equal(4, mock.LastDispatchedWmiBuffer.Length);
            Assert.Equal(121, mock.LastDispatchedWmiCommand);
            Assert.Equal((byte)PowerMode.Performance, mock.LastDispatchedWmiBuffer[0]);
            Assert.Equal(0x19, mock.LastDispatchedWmiBuffer[3]);
        }

        [Fact]
        public void NativeBridge_IntegerCommandSimulation_DispatchesSafely()
        {
            var mock = new MockHwService();
            mock.SetTurboFan(true);

            Assert.True(mock.CurrentTurboFan);
            Assert.Equal(1, mock.VirtualAppSettings[1032]);
            Assert.Equal((byte)(2 | (1 << 6)), mock.LastDispatchedWmiBuffer[0]);
        }
    }
}
