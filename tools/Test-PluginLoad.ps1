<#
.SYNOPSIS
    Loads the built (or installed) Noumenon VST2 plugins the way a native host does, without a DAW.

.DESCRIPTION
    LoadLibraryEx on the native bridge -> GetProcAddress("VSTPluginMain") -> call it with an
    audioMaster callback -> walk the plugin through a host's first minute: check the AEffect magic,
    flags and counts, effOpen, set the rate and block size, mains on, read names and a parameter,
    send a MIDI note, render blocks and make sure they are not silent, ask for the state chunk and
    hand it back, read the editor rectangle, close. With -Editor the editor is also opened inside a
    window for a moment and a screenshot is saved.

    This must run under **Windows PowerShell 5.1**, which is a .NET *Framework* process: CoreCLR is
    not already loaded there, so ijwhost has to locate hostfxr and boot the runtime itself, exactly
    as FL Studio makes it. (Under PowerShell 7 a CoreCLR is already in-process and the test would
    prove less; the script relaunches itself under 5.1 if started there.)

    ijwhost finds the runtime through DOTNET_ROOT or the machine-wide install registered in
    HKLM\SOFTWARE\dotnet. On a machine with only a user-local SDK (%USERPROFILE%\.dotnet) the script
    sets DOTNET_ROOT to it for this process and says so: a DAW would need the .NET 10 Desktop
    Runtime installed, or the same variable in the user's environment.

.EXAMPLE
    .\tools\Test-PluginLoad.ps1                               # both deploy folders, Debug
    .\tools\Test-PluginLoad.ps1 -Configuration Release -Editor
    .\tools\Test-PluginLoad.ps1 -Dll "C:\Program Files\Common Files\VST2\Noumenon\Noumenon.Vst2.dll"
#>
[CmdletBinding()]
param(
    [string[]]$Dll,
    [string]$Configuration = 'Debug',
    [switch]$Editor,
    [int]$Blocks = 32,
    [string]$ScreenshotFolder = $env:TEMP
)

$ErrorActionPreference = 'Stop'

if (-not $Dll -or $Dll.Count -eq 0)
{
    $Dll = @(
        (Join-Path $PSScriptRoot "..\Noumenon.Vst2\bin\x64\$Configuration\net10.0-windows\deploy\Noumenon.Vst2.dll"),
        (Join-Path $PSScriptRoot "..\Noumenon.Vst2.Fx\bin\x64\$Configuration\net10.0-windows\deploy\NoumenonFx.Vst2.dll")
    )
}
$Dll = @($Dll | ForEach-Object { [IO.Path]::GetFullPath($_) })
foreach ($path in $Dll)
{
    if (-not (Test-Path $path))
    {
        throw "Plugin not found: $path"
    }
}

# --- the runtime ijwhost will look for --------------------------------------------------------

$machineWide = $false
try
{
    $location = (Get-ItemProperty 'HKLM:\SOFTWARE\dotnet\Setup\InstalledVersions\x64' -ErrorAction Stop).InstallLocation
    if ($location -and (Test-Path (Join-Path $location 'host\fxr')))
    {
        $machineWide = $true
    }
}
catch
{
}
if (-not $machineWide -and (Test-Path (Join-Path $env:ProgramFiles 'dotnet\host\fxr')))
{
    $machineWide = $true
}
if (-not $machineWide -and -not $env:DOTNET_ROOT)
{
    $userLocal = Join-Path $env:USERPROFILE '.dotnet'
    if (Test-Path (Join-Path $userLocal 'host\fxr'))
    {
        $env:DOTNET_ROOT = $userLocal
        Write-Warning "No machine-wide .NET runtime is registered. Using DOTNET_ROOT=$userLocal for this process only; a DAW needs the .NET 10 Desktop Runtime (x64) installed, or DOTNET_ROOT set in the user environment (setx DOTNET_ROOT `"$userLocal`")."
    }
}

# Each plugin is loaded in its OWN process. Two VST.NET native bridges (two Ijwhost copies) in one
# process deadlock in the second bridge's CoreCLR bootstrap — a known VST.NET / .NET-host limitation,
# not a Noumenon bug — so a shared-process run would hang. A DAW that loads both identities at once
# must bridge or sandbox them; that is verified in a real host, not here. We also re-launch under
# Windows PowerShell 5.1 (a .NET Framework process) so ijwhost does the full runtime bootstrap, as in
# FL Studio, rather than attaching to an already-loaded CoreCLR.
if ($PSVersionTable.PSEdition -eq 'Core' -or $Dll.Count -gt 1)
{
    $ps51 = "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe"
    $anyFailed = 0
    foreach ($one in $Dll)
    {
        Write-Host ""
        Write-Host "=== $([IO.Path]::GetFileName($one)) (fresh Windows PowerShell 5.1 process) ===" -ForegroundColor Cyan
        $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath, '-Configuration', $Configuration, '-Blocks', $Blocks, '-ScreenshotFolder', $ScreenshotFolder, '-Dll', $one)
        if ($Editor)
        {
            $arguments += '-Editor'
        }
        & $ps51 @arguments
        if ($LASTEXITCODE -ne 0)
        {
            $anyFailed = 1
        }
    }
    if ($anyFailed -eq 0)
    {
        Write-Host ""
        Write-Host "PASS - every plugin loaded, rendered and round-tripped its state (each in its own process)." -ForegroundColor Green
    }
    exit $anyFailed
}

# --- a minimal VST2 host in C# 5 (Add-Type compiles with the .NET Framework compiler) -----------

$source = @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class VstHost
{
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadLibraryExW(string path, IntPtr reserved, uint flags);
    [DllImport("kernel32", SetLastError = true)]
    public static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool SetDllDirectoryW(string path);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr AudioMasterDelegate(IntPtr effect, int opcode, int index, IntPtr value, IntPtr ptr, float opt);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr VstMainDelegate(IntPtr audioMaster);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr DispatcherDelegate(IntPtr effect, int opcode, int index, IntPtr value, IntPtr ptr, float opt);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ProcessReplacingDelegate(IntPtr effect, IntPtr inputs, IntPtr outputs, int frames);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void SetParameterDelegate(IntPtr effect, int index, float value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate float GetParameterDelegate(IntPtr effect, int index);

    public static AudioMasterDelegate KeepAlive;
    public static int SizeWindowCalls;
    public static IntPtr TimeInfo;

    // A VstTimeInfo (88 bytes) the host hands back for audioMasterGetTime: 120 BPM, playing, so the
    // plugin's transport path runs. A host that returns null here makes VST.NET's interop throw.
    static VstHost()
    {
        TimeInfo = Marshal.AllocHGlobal(88);
        for (int i = 0; i < 88; i++) Marshal.WriteByte(TimeInfo, i, 0);
        Marshal.WriteInt64(TimeInfo, 8, BitConverter.DoubleToInt64Bits(48000.0));   // sampleRate
        Marshal.WriteInt64(TimeInfo, 32, BitConverter.DoubleToInt64Bits(120.0));    // tempo
        Marshal.WriteInt32(TimeInfo, 64, 4);      // timeSigNumerator
        Marshal.WriteInt32(TimeInfo, 68, 4);      // timeSigDenominator
        Marshal.WriteInt32(TimeInfo, 84, 1026);   // kVstTransportPlaying (2) | kVstTempoValid (1024)
    }

    public static IntPtr AudioMaster(IntPtr effect, int opcode, int index, IntPtr value, IntPtr ptr, float opt)
    {
        switch (opcode)
        {
            case 1: return (IntPtr)2400;            // audioMasterVersion
            case 7: return TimeInfo;                // audioMasterGetTime
            case 15: SizeWindowCalls++; return (IntPtr)1;   // audioMasterSizeWindow
            case 16: return (IntPtr)48000;          // audioMasterGetSampleRate
            case 17: return (IntPtr)512;            // audioMasterGetBlockSize
            default: return IntPtr.Zero;
        }
    }

    public static IntPtr Load(string dll)
    {
        SetDllDirectoryW(System.IO.Path.GetDirectoryName(dll));
        IntPtr module = LoadLibraryExW(dll, IntPtr.Zero, 0x00000008 /* LOAD_WITH_ALTERED_SEARCH_PATH */);
        if (module == IntPtr.Zero)
            throw new Exception("LoadLibraryEx failed, win32=" + Marshal.GetLastWin32Error());

        IntPtr entry = GetProcAddress(module, "VSTPluginMain");
        if (entry == IntPtr.Zero) entry = GetProcAddress(module, "main");
        if (entry == IntPtr.Zero)
            throw new Exception("No VSTPluginMain/main export, win32=" + Marshal.GetLastWin32Error());

        KeepAlive = new AudioMasterDelegate(AudioMaster);
        VstMainDelegate main = (VstMainDelegate)Marshal.GetDelegateForFunctionPointer(entry, typeof(VstMainDelegate));
        return main(Marshal.GetFunctionPointerForDelegate(KeepAlive));
    }

    // AEffect layout (x64): magic(4@0) dispatcher(8@8) process(8@16) setParameter(8@24) getParameter(8@32)
    // numPrograms(4@40) numParams(4@44) numInputs(4@48) numOutputs(4@52) flags(4@56) resvd1(8@64)
    // resvd2(8@72) initialDelay(4@80) ... uniqueID(4@112) version(4@116) processReplacing(8@120)
    public static int Magic(IntPtr e)        { return Marshal.ReadInt32(e, 0); }
    public static int NumPrograms(IntPtr e)  { return Marshal.ReadInt32(e, 40); }
    public static int NumParams(IntPtr e)    { return Marshal.ReadInt32(e, 44); }
    public static int NumInputs(IntPtr e)    { return Marshal.ReadInt32(e, 48); }
    public static int NumOutputs(IntPtr e)   { return Marshal.ReadInt32(e, 52); }
    public static int Flags(IntPtr e)        { return Marshal.ReadInt32(e, 56); }
    public static int InitialDelay(IntPtr e) { return Marshal.ReadInt32(e, 80); }
    public static int UniqueId(IntPtr e)     { return Marshal.ReadInt32(e, 112); }
    public static int Version(IntPtr e)      { return Marshal.ReadInt32(e, 116); }

    public static string FourCC(int id)
    {
        return new string(new char[] { (char)((id >> 24) & 0xFF), (char)((id >> 16) & 0xFF), (char)((id >> 8) & 0xFF), (char)(id & 0xFF) });
    }

    public static IntPtr Dispatch(IntPtr e, int opcode, int index, IntPtr value, IntPtr ptr, float opt)
    {
        DispatcherDelegate dispatcher = (DispatcherDelegate)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(e, 8), typeof(DispatcherDelegate));
        return dispatcher(e, opcode, index, value, ptr, opt);
    }

    public static string DispatchString(IntPtr e, int opcode, int index, int capacity)
    {
        IntPtr buffer = Marshal.AllocHGlobal(capacity);
        try
        {
            for (int i = 0; i < capacity; i++) Marshal.WriteByte(buffer, i, 0);
            Dispatch(e, opcode, index, IntPtr.Zero, buffer, 0f);
            return Marshal.PtrToStringAnsi(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static int CanDo(IntPtr e, string what)
    {
        IntPtr text = Marshal.StringToHGlobalAnsi(what);
        try { return (int)(long)Dispatch(e, 51, 0, IntPtr.Zero, text, 0f); }
        finally { Marshal.FreeHGlobal(text); }
    }

    public static void SetParameter(IntPtr e, int index, float value)
    {
        SetParameterDelegate set = (SetParameterDelegate)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(e, 24), typeof(SetParameterDelegate));
        set(e, index, value);
    }

    public static float GetParameter(IntPtr e, int index)
    {
        GetParameterDelegate get = (GetParameterDelegate)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(e, 32), typeof(GetParameterDelegate));
        return get(e, index);
    }

    // One VstMidiEvent (32 bytes) inside a VstEvents block (numEvents, reserved, events[]).
    public static void SendMidi(IntPtr e, byte status, byte data1, byte data2, int deltaFrames)
    {
        IntPtr midi = Marshal.AllocHGlobal(32);
        IntPtr events = Marshal.AllocHGlobal(32);
        try
        {
            for (int i = 0; i < 32; i++) { Marshal.WriteByte(midi, i, 0); Marshal.WriteByte(events, i, 0); }
            Marshal.WriteInt32(midi, 0, 1);           // kVstMidiType
            Marshal.WriteInt32(midi, 4, 32);          // byteSize
            Marshal.WriteInt32(midi, 8, deltaFrames);
            Marshal.WriteByte(midi, 24, status);
            Marshal.WriteByte(midi, 25, data1);
            Marshal.WriteByte(midi, 26, data2);
            Marshal.WriteInt32(events, 0, 1);         // numEvents
            Marshal.WriteIntPtr(events, 16, midi);    // events[0]
            Dispatch(e, 25, 0, IntPtr.Zero, events, 0f);   // effProcessEvents
        }
        finally
        {
            Marshal.FreeHGlobal(events);
            Marshal.FreeHGlobal(midi);
        }
    }

    // Renders blocks of silence-in and returns the peak absolute output sample.
    public static float Render(IntPtr e, int frames, int blocks)
    {
        ProcessReplacingDelegate process = (ProcessReplacingDelegate)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(e, 120), typeof(ProcessReplacingDelegate));
        IntPtr inL = Marshal.AllocHGlobal(frames * 4), inR = Marshal.AllocHGlobal(frames * 4);
        IntPtr outL = Marshal.AllocHGlobal(frames * 4), outR = Marshal.AllocHGlobal(frames * 4);
        IntPtr inputs = Marshal.AllocHGlobal(16), outputs = Marshal.AllocHGlobal(16);
        float[] samples = new float[frames];
        float peak = 0f;
        try
        {
            for (int i = 0; i < frames; i++) { Marshal.WriteInt32(inL, i * 4, 0); Marshal.WriteInt32(inR, i * 4, 0); }
            Marshal.WriteIntPtr(inputs, 0, inL); Marshal.WriteIntPtr(inputs, 8, inR);
            Marshal.WriteIntPtr(outputs, 0, outL); Marshal.WriteIntPtr(outputs, 8, outR);
            for (int b = 0; b < blocks; b++)
            {
                process(e, inputs, outputs, frames);
                Marshal.Copy(outL, samples, 0, frames);
                for (int i = 0; i < frames; i++) { float a = Math.Abs(samples[i]); if (a > peak) peak = a; if (float.IsNaN(samples[i])) throw new Exception("NaN in the output"); }
                Marshal.Copy(outR, samples, 0, frames);
                for (int i = 0; i < frames; i++) { float a = Math.Abs(samples[i]); if (a > peak) peak = a; }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(inL); Marshal.FreeHGlobal(inR); Marshal.FreeHGlobal(outL); Marshal.FreeHGlobal(outR);
            Marshal.FreeHGlobal(inputs); Marshal.FreeHGlobal(outputs);
        }
        return peak;
    }

    public static byte[] GetChunk(IntPtr e)
    {
        IntPtr holder = Marshal.AllocHGlobal(8);
        try
        {
            Marshal.WriteIntPtr(holder, IntPtr.Zero);
            int size = (int)(long)Dispatch(e, 23, 0, IntPtr.Zero, holder, 0f);   // effGetChunk, bank
            IntPtr data = Marshal.ReadIntPtr(holder);
            if (size <= 0 || data == IntPtr.Zero) return new byte[0];
            byte[] bytes = new byte[size];
            Marshal.Copy(data, bytes, 0, size);
            return bytes;
        }
        finally
        {
            Marshal.FreeHGlobal(holder);
        }
    }

    public static int SetChunk(IntPtr e, byte[] bytes)
    {
        IntPtr data = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, data, bytes.Length);
            return (int)(long)Dispatch(e, 24, 0, (IntPtr)bytes.Length, data, 0f);   // effSetChunk, bank
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }
    }

    // ERect: short top, left, bottom, right.
    public static int[] EditorRect(IntPtr e)
    {
        IntPtr holder = Marshal.AllocHGlobal(8);
        try
        {
            Marshal.WriteIntPtr(holder, IntPtr.Zero);
            Dispatch(e, 13, 0, IntPtr.Zero, holder, 0f);   // effEditGetRect
            IntPtr rect = Marshal.ReadIntPtr(holder);
            if (rect == IntPtr.Zero) return new int[] { 0, 0 };
            int top = Marshal.ReadInt16(rect, 0), left = Marshal.ReadInt16(rect, 2), bottom = Marshal.ReadInt16(rect, 4), right = Marshal.ReadInt16(rect, 6);
            return new int[] { right - left, bottom - top };
        }
        finally
        {
            Marshal.FreeHGlobal(holder);
        }
    }
}
"@

Write-Host "Host   : $([System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription) ($([IntPtr]::Size * 8)-bit)"
Add-Type -TypeDefinition $source -Language CSharp | Out-Null
if ($Editor)
{
    Add-Type -AssemblyName System.Windows.Forms, System.Drawing
}

$failures = 0
function Check($condition, $message)
{
    if ($condition)
    {
        Write-Host "   ok    $message" -ForegroundColor Green
    }
    else
    {
        Write-Host "   FAIL  $message" -ForegroundColor Red
        $script:failures++
    }
}

foreach ($path in $Dll)
{
    Write-Host ''
    Write-Host "Plugin : $path"
    try
    {
        $effect = [VstHost]::Load($path)
    }
    catch
    {
        Write-Host "FAIL - $($_.Exception.Message)" -ForegroundColor Red
        Write-Host '       Usually a missing .NET 10 Desktop Runtime (x64), a missing VC++ 2015-2022 x64 redist, or an incomplete deploy folder.'
        $failures++
        continue
    }
    if ($effect -eq [IntPtr]::Zero)
    {
        Write-Host 'FAIL - VSTPluginMain returned NULL (the runtime started but the plugin did not).' -ForegroundColor Red
        $failures++
        continue
    }

    $magic = [VstHost]::Magic($effect)
    Check ($magic -eq 0x56737450) ("AEffect magic 'VstP' (0x{0:X8})" -f $magic)
    if ($magic -ne 0x56737450)
    {
        continue
    }

    $flags = [VstHost]::Flags($effect)
    $inputs = [VstHost]::NumInputs($effect)
    $outputs = [VstHost]::NumOutputs($effect)
    $params = [VstHost]::NumParams($effect)
    $id = [VstHost]::FourCC([VstHost]::UniqueId($effect))
    Write-Host ("   id {0}, version {1}, {2} programs, {3} parameters, {4} in / {5} out, flags 0x{6:X}, initial delay {7}" -f $id, [VstHost]::Version($effect), [VstHost]::NumPrograms($effect), $params, $inputs, $outputs, $flags, [VstHost]::InitialDelay($effect))
    $isSynth = ($flags -band 256) -ne 0
    Check (($flags -band 1) -ne 0) 'has an editor'
    Check (($flags -band 16) -ne 0) 'processReplacing'
    Check (($flags -band 32) -ne 0) 'program chunks'
    Check ($outputs -eq 2) 'two outputs'
    Check (($inputs -eq 0 -and $isSynth -and $id -eq 'Noum') -or ($inputs -eq 2 -and -not $isSynth -and $id -eq 'NouF')) "identity is consistent (synth without inputs 'Noum', or effect with two inputs 'NouF')"
    Check ($params -gt 300) 'the whole parameter table is published'

    [void][VstHost]::Dispatch($effect, 0, 0, [IntPtr]::Zero, [IntPtr]::Zero, 0)          # effOpen
    [void][VstHost]::Dispatch($effect, 10, 0, [IntPtr]::Zero, [IntPtr]::Zero, 48000)     # effSetSampleRate
    [void][VstHost]::Dispatch($effect, 11, 0, [IntPtr]512, [IntPtr]::Zero, 0)            # effSetBlockSize
    [void][VstHost]::Dispatch($effect, 12, 0, [IntPtr]1, [IntPtr]::Zero, 0)              # effMainsChanged on

    $name = [VstHost]::DispatchString($effect, 45, 0, 64)
    $vendor = [VstHost]::DispatchString($effect, 47, 0, 64)
    $vstVersion = [int][long][VstHost]::Dispatch($effect, 58, 0, [IntPtr]::Zero, [IntPtr]::Zero, 0)
    Write-Host "   name '$name', vendor '$vendor', VST $vstVersion"
    Check (($name -eq 'Noumenon' -and $id -eq 'Noum') -or ($name -eq 'Noumenon FX' -and $id -eq 'NouF')) 'effect name matches the identity'
    Check ($vstVersion -eq 2400) 'VST 2.4'
    Check ([VstHost]::CanDo($effect, 'receiveVstMidiEvent') -eq 1) 'canDo receiveVstMidiEvent'
    Check ([VstHost]::CanDo($effect, 'receiveVstTimeInfo') -eq 1) 'canDo receiveVstTimeInfo'

    $firstName = [VstHost]::DispatchString($effect, 8, 0, 64)
    $firstDisplay = [VstHost]::DispatchString($effect, 7, 0, 64)
    $firstValue = [VstHost]::GetParameter($effect, 0)
    Write-Host "   parameter 0: '$firstName' = '$firstDisplay' ($firstValue)"
    Check ($firstName -eq 'A1 On' -and $firstDisplay -eq 'On' -and $firstValue -eq 1) "parameter 0 reads 'A1 On' = 'On' (1)"
    [VstHost]::SetParameter($effect, 0, 0)
    Check (([VstHost]::GetParameter($effect, 0)) -eq 0) 'setParameter/getParameter round trip'
    Check (([VstHost]::DispatchString($effect, 7, 0, 64)) -eq 'Off') "display follows the host's value"
    [VstHost]::SetParameter($effect, 0, 1)

    # At least 16 blocks (~170 ms) so the init patch's Drift through-delay has filled before the peak
    # is measured — below ~12 blocks a perfectly good patch still reads as silence.
    $renderBlocks = [Math]::Max(16, $Blocks)
    [VstHost]::SendMidi($effect, 0x90, 60, 100, 0)
    $peak = [VstHost]::Render($effect, 512, $renderBlocks)
    Write-Host ("   rendered {0} blocks of 512, peak {1:0.000}" -f $renderBlocks, $peak)
    Check ($peak -gt 0.001 -and $peak -le 1.0) 'output is sound, not silence, and within the ceiling'
    [VstHost]::SendMidi($effect, 0x80, 60, 0, 0)

    # Save the state (parameter 0 is On), change it and let a block carry the change into the bank
    # as a real host does, then restore the state: the chunk really drives the plugin (hosts ignore
    # setChunk's return value and always process between setting and reading a parameter).
    $chunk = [VstHost]::GetChunk($effect)
    $json = [System.Text.Encoding]::UTF8.GetString($chunk)
    Write-Host ("   chunk {0} bytes, starts {1}" -f $chunk.Length, ($json -replace '\s+', ' ').Substring(0, [Math]::Min(60, $json.Length)))
    Check ($chunk.Length -gt 100 -and $json.TrimStart().StartsWith('{') -and $json.Contains('"Version"')) 'state chunk is the snapshot JSON'
    [VstHost]::SetParameter($effect, 0, 0)
    [void][VstHost]::Render($effect, 512, 1)
    Check (([VstHost]::GetParameter($effect, 0)) -eq 0) 'parameter changed away from the saved state'
    [void][VstHost]::SetChunk($effect, $chunk)
    Check (([VstHost]::GetParameter($effect, 0)) -eq 1) 'setChunk restored the saved parameter value'

    $rect = [VstHost]::EditorRect($effect)
    Write-Host ("   editor rect {0} x {1}" -f $rect[0], $rect[1])
    Check ($rect[0] -eq 1000 -and $rect[1] -eq 664) 'editor rect is the 1000 x 664 panel'

    if ($Editor)
    {
        # Open the editor into a real foreign window under the Framework host, idle it, screenshot it
        # for manual inspection, close it: the host-process reparent and message handling must not
        # crash. The screenshot is an artifact, not an assertion — CopyFromScreen of a window driven
        # by a manual DoEvents pump races with whatever else has the foreground, so it is unreliable
        # to assert on; the editor's rendering and reparenting are pinned by the in-process xUnit test
        # Editor_Opens_Inside_A_Host_Window_And_Keeps_Its_Size instead.
        $form = New-Object System.Windows.Forms.Form
        $form.Text = "Noumenon VST2 load test - $name"
        $form.FormBorderStyle = 'FixedSingle'
        $form.MaximizeBox = $false
        $form.StartPosition = 'CenterScreen'
        $form.TopMost = $true
        $form.ClientSize = New-Object System.Drawing.Size($rect[0], $rect[1])
        $form.Show()
        $form.Activate()
        $opened = [int][long][VstHost]::Dispatch($effect, 14, 0, [IntPtr]::Zero, $form.Handle, 0)   # effEditOpen
        Check ($opened -eq 1) 'effEditOpen returned 1'
        $until = [DateTime]::UtcNow.AddMilliseconds(1500)
        while ([DateTime]::UtcNow -lt $until)
        {
            [System.Windows.Forms.Application]::DoEvents()
            [void][VstHost]::Dispatch($effect, 19, 0, [IntPtr]::Zero, [IntPtr]::Zero, 0)   # effEditIdle
            Start-Sleep -Milliseconds 20
        }
        try
        {
            $origin = $form.PointToScreen([System.Drawing.Point]::Empty)
            $bitmap = New-Object System.Drawing.Bitmap($form.ClientSize.Width, $form.ClientSize.Height)
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            $graphics.CopyFromScreen($origin, [System.Drawing.Point]::Empty, $form.ClientSize)
            $graphics.Dispose()
            $shot = Join-Path $ScreenshotFolder ("noumenon-vst2-" + ($name -replace '[^A-Za-z0-9]', '') + ".png")
            $bitmap.Save($shot, [System.Drawing.Imaging.ImageFormat]::Png)
            $bitmap.Dispose()
            Write-Host "   editor opened; screenshot for manual inspection: $shot"
        }
        catch
        {
            Write-Host "   editor opened; screenshot skipped ($($_.Exception.Message))"
        }
        [void][VstHost]::Dispatch($effect, 15, 0, [IntPtr]::Zero, [IntPtr]::Zero, 0)   # effEditClose
        [System.Windows.Forms.Application]::DoEvents()
        Check $true 'editor opened and closed without crashing the host'
        $form.Close()
        $form.Dispose()
    }

    [void][VstHost]::Dispatch($effect, 12, 0, [IntPtr]0, [IntPtr]::Zero, 0)   # effMainsChanged off
    [void][VstHost]::Dispatch($effect, 1, 0, [IntPtr]::Zero, [IntPtr]::Zero, 0)   # effClose
}

Write-Host ''
if ($failures -eq 0)
{
    Write-Host "PASS - $($Dll.Count) plugin(s) loaded, rendered and round-tripped their state." -ForegroundColor Green
    exit 0
}
Write-Host "FAIL - $failures check(s) failed." -ForegroundColor Red
exit 1
