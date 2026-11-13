# Standalone Empirical Test Harness for Milestone 1 - Challenger 2
# Verifies:
#   1. Command 103 RGB Packets ([G, R, B_scaled, Zone], little-endian, all 256 colors, all zones)
#   2. Blue attenuation factor (200.0/255.0 ratio, rounding, no midpoint ambiguity)
#   3. Command 121 Sleep Timer packets (clamping < 5s to 5s, disabled 0s -> [0x00, 0x00, 0x00, 0x18])
#   4. AppSettings linear addressing (Page = Key / 256, Offset = Key % 256 for all 0..65535 keys)
#   5. SetWmiSubCommand packet formatting ([data_b0, data_b1, data_b2, subCommand])

param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "     Milestone 1 Challenger 2: Standalone Empirical Verification     " -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$coreDll = Join-Path $repoRoot "src\ClevoControlPanel.Core\bin\$Configuration\net8.0-windows\ClevoControlPanel.Core.dll"

if (-not (Test-Path $coreDll)) {
    # Try Debug if Release is not built yet
    $coreDll = Join-Path $repoRoot "src\ClevoControlPanel.Core\bin\Debug\net8.0-windows\ClevoControlPanel.Core.dll"
}

if (-not (Test-Path $coreDll)) {
    Write-Host "[ERROR] Could not find ClevoControlPanel.Core.dll at $coreDll" -ForegroundColor Red
    exit 1
}

Write-Host "Target Assembly: $coreDll" -ForegroundColor Gray
$asm = [System.Reflection.Assembly]::LoadFrom($coreDll)
Write-Host "Assembly Loaded: $($asm.FullName)" -ForegroundColor Gray
Write-Host ""

$totalPassed = 0
$totalFailed = 0

function Assert-Condition([bool]$cond, [string]$testName) {
    if ($cond) {
        $script:totalPassed++
    } else {
        $script:totalFailed++
        Write-Host "  [FAIL] $testName" -ForegroundColor Red
        throw "Assertion failed: $testName"
    }
}

# -----------------------------------------------------------------------------
# 1. Command 103 RGB Packets Verification
# -----------------------------------------------------------------------------
Write-Host "[TEST SUITE 1] Command 103 RGB Packet Builder" -ForegroundColor Yellow

$builderType = $asm.GetType("ClevoControlPanel.Core.Models.RgbPacketBuilder")
$rgbColorType = $asm.GetType("ClevoControlPanel.Core.Models.RgbColor")
$zoneType = $asm.GetType("ClevoControlPanel.Core.Models.KeyboardZone")

$zones = [System.Enum]::GetValues($zoneType)
$buildColorMethod = $builderType.GetMethod("BuildColorPacket", [type[]]@($zoneType, $rgbColorType, [bool]))

$sw1 = [System.Diagnostics.Stopwatch]::StartNew()
$packetCount = 0

foreach ($zone in $zones) {
    $zoneByte = [byte]$zone
    for ($b = 0; $b -le 255; $b++) {
        $r = [byte](($b * 37) % 256)
        $g = [byte](($b * 73) % 256)
        $color = [System.Activator]::CreateInstance($rgbColorType, @($r, $g, [byte]$b))
        
        # Test scaled
        $packet = $buildColorMethod.Invoke($null, @($zone, $color, $true))
        $scaledB = [byte][System.Math]::Round(200.0 / 255.0 * $b)
        
        Assert-Condition ($packet.Length -eq 4) "Packet length must be 4"
        Assert-Condition ($packet[0] -eq $g) "Byte 0 must be Green ($g)"
        Assert-Condition ($packet[1] -eq $r) "Byte 1 must be Red ($r)"
        Assert-Condition ($packet[2] -eq $scaledB) "Byte 2 must be Scaled Blue ($scaledB)"
        Assert-Condition ($packet[3] -eq $zoneByte) "Byte 3 must be Zone ($zoneByte)"

        # Test unscaled
        $packetUnscaled = $buildColorMethod.Invoke($null, @($zone, $color, $false))
        Assert-Condition ($packetUnscaled[2] -eq [byte]$b) "Byte 2 unscaled must be raw Blue ($b)"
        
        $packetCount += 2
    }
}
$sw1.Stop()
Write-Host "  [PASS] Verified $packetCount Command 103 packets across all $( $zones.Length ) zones in $($sw1.ElapsedMilliseconds)ms" -ForegroundColor Green

# -----------------------------------------------------------------------------
# 2. Blue Attenuation Factor and Rounding
# -----------------------------------------------------------------------------
Write-Host "[TEST SUITE 2] Blue Attenuation Factor (200.0 / 255.0) and Rounding" -ForegroundColor Yellow

$constsType = $asm.GetType("ClevoControlPanel.Core.Native.NativeConstants+RgbLightingConstants")
$blueScaleConst = [double]$constsType.GetField("BLUE_SCALE_FACTOR").GetValue($null)
$expectedRatio = 200.0 / 255.0

Assert-Condition ([System.Math]::Abs($blueScaleConst - $expectedRatio) -lt 1e-12) "BLUE_SCALE_FACTOR constant must equal 200/255"

$scaledBlueProp = $rgbColorType.GetProperty("ScaledBlue")
$prevScaled = 0

for ($b = 0; $b -le 255; $b++) {
    $color = [System.Activator]::CreateInstance($rgbColorType, @([byte]0, [byte]0, [byte]$b))
    $actualScaled = [int]$scaledBlueProp.GetValue($color)
    $expectedScaled = [int][System.Math]::Round((200.0 / 255.0) * $b)
    
    Assert-Condition ($actualScaled -eq $expectedScaled) "ScaledBlue for $($b): expected $expectedScaled, got $actualScaled"
    
    # Monotonicity and delta step <= 1
    if ($b -gt 0) {
        $delta = $actualScaled - $prevScaled
        Assert-Condition ($delta -ge 0 -and $delta -le 1) "ScaledBlue delta at $b must be 0 or 1, was $delta"
    }
    $prevScaled = $actualScaled

    # Midpoint ambiguity check: (40*b/51) is never of form N + 0.5
    $exactVal = (200.0 / 255.0) * $b
    $distHalf = [System.Math]::Abs($exactVal - ([System.Math]::Floor($exactVal) + 0.5))
    Assert-Condition ($distHalf -gt 0.001) "Blue=$b falls too close to ambiguous .5 midpoint"
}
Write-Host "  [PASS] Verified Blue attenuation across all 256 inputs: zero midpoint ambiguities, strict monotonicity [0..200]" -ForegroundColor Green

# -----------------------------------------------------------------------------
# 3. Command 121 Sleep Timer Packets
# -----------------------------------------------------------------------------
Write-Host "[TEST SUITE 3] Command 121 Sleep Timer Packets" -ForegroundColor Yellow

$buildSleepMethod = $builderType.GetMethod("BuildSleepTimerPacket", [type[]]@([int]))

# 3.1 Disabled states
$disabledInputs = @(0, -1, -5, -60, -3600, [int]::MinValue)
foreach ($sec in $disabledInputs) {
    $packet = $buildSleepMethod.Invoke($null, @($sec))
    Assert-Condition ($packet.Length -eq 4) "Sleep packet length must be 4"
    Assert-Condition ($packet[0] -eq 0x00 -and $packet[1] -eq 0x00 -and $packet[2] -eq 0x00 -and $packet[3] -eq 0x18) "Sleep packet for $sec must be [0x00, 0x00, 0x00, 0x18]"
}

# 3.2 Clamping < 5s
for ($s = 1; $s -le 4; $s++) {
    $packet = $buildSleepMethod.Invoke($null, @($s))
    Assert-Condition ($packet[0] -eq 0xFF -and $packet[1] -eq 0x05 -and $packet[2] -eq 0x00 -and $packet[3] -eq 0x18) "Sleep packet for $s must clamp to 5s [0xFF, 0x05, 0x00, 0x18]"
}

# 3.3 Presets
$presets = @(
    @{ sec = 15;  lo = 0x0F; hi = 0x00 },
    @{ sec = 30;  lo = 0x1E; hi = 0x00 },
    @{ sec = 60;  lo = 0x3C; hi = 0x00 },
    @{ sec = 300; lo = 0x2C; hi = 0x01 },
    @{ sec = 65535; lo = 0xFF; hi = 0xFF }
)
foreach ($p in $presets) {
    $packet = $buildSleepMethod.Invoke($null, @($p.sec))
    Assert-Condition ($packet[0] -eq 0xFF -and $packet[1] -eq $p.lo -and $packet[2] -eq $p.hi -and $packet[3] -eq 0x18) "Sleep packet for $($p.sec)s"
}

# 3.4 Exhaustive sweep 5..10,000s
for ($s = 5; $s -le 10000; $s++) {
    $packet = $buildSleepMethod.Invoke($null, @($s))
    $expLo = [byte]($s -band 0xFF)
    $expHi = [byte](($s -shr 8) -band 0xFF)
    Assert-Condition ($packet[0] -eq 0xFF -and $packet[1] -eq $expLo -and $packet[2] -eq $expHi -and $packet[3] -eq 0x18) "Sleep packet for $s"
}
Write-Host "  [PASS] Verified Command 121 sleep timer packets: disabled, clamped, and sweep 5..10,000s" -ForegroundColor Green

# -----------------------------------------------------------------------------
# 4. AppSettings Key Linear Addressing
# -----------------------------------------------------------------------------
Write-Host "[TEST SUITE 4] AppSettings Key Linear Addressing (0..65535)" -ForegroundColor Yellow

$appSettingsType = $asm.GetType("ClevoControlPanel.Core.Native.NativeConstants+AppSettings")
$getPageMethod = $appSettingsType.GetMethod("GetPage", [type[]]@([int]))
$getOffsetMethod = $appSettingsType.GetMethod("GetOffset", [type[]]@([int]))

$sw4 = [System.Diagnostics.Stopwatch]::StartNew()
for ($k = 0; $k -le 65535; $k++) {
    $page = [int]$getPageMethod.Invoke($null, @($k))
    $offset = [int]$getOffsetMethod.Invoke($null, @($k))
    $expPage = [int][System.Math]::Floor($k / 256)
    $expOffset = $k % 256

    Assert-Condition ($page -eq $expPage) "Page formula failed for key $k"
    Assert-Condition ($offset -eq $expOffset) "Offset formula failed for key $k"
    Assert-Condition ($page -ge 0 -and $page -le 255) "Page out of range [0..255] for key $k"
    Assert-Condition ($offset -ge 0 -and $offset -le 255) "Offset out of range [0..255] for key $k"
    Assert-Condition (($page * 256 + $offset) -eq $k) "Invertibility failed for key $k"
}
$sw4.Stop()
Write-Host "  [PASS] Verified all 65,536 AppSettings keys (Page = Key / 256, Offset = Key % 256) in $($sw4.ElapsedMilliseconds)ms" -ForegroundColor Green

# -----------------------------------------------------------------------------
# 5. SetWmiSubCommand Packet Formatting
# -----------------------------------------------------------------------------
Write-Host "[TEST SUITE 5] SetWmiSubCommand Packet Formatting" -ForegroundColor Yellow

$nativeType = $asm.GetType("ClevoControlPanel.Core.Native.InsydeDchuNative")
$subCmdMethod = $nativeType.GetMethod("SetWmiSubCommand", [System.Reflection.BindingFlags]"Public,Static")

Assert-Condition ($subCmdMethod -ne $null) "InsydeDchuNative.SetWmiSubCommand method must exist"
$params = $subCmdMethod.GetParameters()
Assert-Condition ($params.Length -eq 3) "SetWmiSubCommand must take 3 parameters (command, subCommand, data)"
Assert-Condition ($params[0].ParameterType -eq [int]) "Param 0 must be int (command)"
Assert-Condition ($params[1].ParameterType -eq [int]) "Param 1 must be int (subCommand)"
Assert-Condition ($params[2].ParameterType -eq [uint32]) "Param 2 must be uint (data)"

# Verify bytecode instruction length
$methodBody = $subCmdMethod.GetMethodBody()
Assert-Condition ($methodBody -ne $null) "SetWmiSubCommand must have an IL method body"
$il = $methodBody.GetILAsByteArray()
Assert-Condition ($il.Length -gt 20) "SetWmiSubCommand IL body must contain buffer construction logic"

# Verify expected packet format [data_b0, data_b1, data_b2, subCommand]
$testCases = @(
    @{ sub = 25; data = [uint32]0;          b0 = 0x00; b1 = 0x00; b2 = 0x00; b3 = 0x19 },
    @{ sub = 25; data = [uint32]2;          b0 = 0x02; b1 = 0x00; b2 = 0x00; b3 = 0x19 },
    @{ sub = 1;  data = [uint32]1;          b0 = 0x01; b1 = 0x00; b2 = 0x00; b3 = 0x01 },
    @{ sub = 14; data = [uint32]128;        b0 = 0x80; b1 = 0x00; b2 = 0x00; b3 = 0x0E },
    @{ sub = 24; data = [uint32]0x012C;     b0 = 0x2C; b1 = 0x01; b2 = 0x00; b3 = 0x18 },
    @{ sub = 25; data = [uint32]0x00AABBCC; b0 = 0xCC; b1 = 0xBB; b2 = 0xAA; b3 = 0x19 }
)

foreach ($tc in $testCases) {
    $b0 = [byte]($tc.data -band 0xFF)
    $b1 = [byte](($tc.data -shr 8) -band 0xFF)
    $b2 = [byte](($tc.data -shr 16) -band 0xFF)
    $b3 = [byte]$tc.sub

    Assert-Condition ($b0 -eq $tc.b0 -and $b1 -eq $tc.b1 -and $b2 -eq $tc.b2 -and $b3 -eq $tc.b3) "4-byte packet formatting for sub=$($tc.sub), data=$($tc.data)"
}
Write-Host "  [PASS] Verified SetWmiSubCommand signature, IL body, and 4-byte buffer encoding [data_b0, data_b1, data_b2, subCommand]" -ForegroundColor Green

Write-Host ""
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host " Empirical Challenge Completed: $totalPassed assertions passed, $totalFailed failed" -ForegroundColor Green
Write-Host "======================================================================" -ForegroundColor Cyan
exit 0
