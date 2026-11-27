# Smoke test: launches NicPad against a throwaway data folder (NICPAD_DATA_DIR, so the real session and
# settings are never touched), checks it responds, screenshots the window (WebView2 included), closes it
# and checks for a clean exit with no crash.log.
# Usage: powershell -File tools/smoke-test.ps1 -Exe bin/Release/net10.0-windows/NicPad.exe -OutPng smoke.png [-Theme Dark] [-Preview 2]

param(
    [Parameter(Mandatory)] [string] $Exe,
    [Parameter(Mandatory)] [string] $OutPng,
    [string] $Theme = "Dark",
    [int] $Preview = 2,
    [int] $WaitSeconds = 8
)
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
"@

$data = Join-Path ([IO.Path]::GetTempPath()) ("nicpad-smoke-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $data | Out-Null
$doc = Join-Path $data "sample.md"
@"
# NicPad smoke test

Some **bold**, *italic*, <u>underline</u> and ``inline code``.

- [x] Task done
- [ ] Task to do

| Feature | Status |
|---------|-------:|
| Preview | ok |
| Sanitizer | ok |

> A quote with a [safe link](https://example.com) and a [bad link](javascript:alert(1)).

![remote image](https://example.com/tracker.png)

``````csharp
var x = 1 < 2;
``````
"@ | Set-Content -Path $doc -Encoding utf8

@{ Theme = $Theme; SingleInstance = $false; Window = @{ X = 80; Y = 80; Width = 1200; Height = 760; Maximized = $false } } |
    ConvertTo-Json | Set-Content (Join-Path $data "settings.json") -Encoding utf8
@{ Version = 2; SelectedTabIndex = 0; OpenFiles = @(@{ FilePath = $doc; FontFamily = "Consolas"; FontSize = 11; Preview = $Preview }) } |
    ConvertTo-Json -Depth 4 | Set-Content (Join-Path $data "session.json") -Encoding utf8

$env:NICPAD_DATA_DIR = $data
$p = Start-Process -FilePath $Exe -PassThru
try {
    $deadline = (Get-Date).AddSeconds(20)
    while ($p.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 250; $p.Refresh() }
    if ($p.MainWindowHandle -eq 0) { throw "No main window after 20 s" }
    Start-Sleep -Seconds $WaitSeconds
    $p.Refresh()
    if ($p.HasExited) { throw "Exited early with code $($p.ExitCode)" }
    if (-not $p.Responding) { throw "Window is not responding" }

    $h = $p.MainWindowHandle
    [Win]::SetForegroundWindow($h) | Out-Null
    Start-Sleep -Milliseconds 500
    $r = New-Object Win+RECT
    [Win]::GetWindowRect($h, [ref]$r) | Out-Null
    $bmp = New-Object Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [Win]::PrintWindow($h, $hdc, 2) | Out-Null   # PW_RENDERFULLCONTENT captures WebView2 too
    $g.ReleaseHdc($hdc); $g.Dispose()
    $bmp.Save($OutPng, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    "title=" + $p.MainWindowTitle

    $p.CloseMainWindow() | Out-Null
    if (-not $p.WaitForExit(10000)) { throw "Did not exit within 10 s of closing" }
    "exit=" + $p.ExitCode
    $crash = Join-Path $data "crash.log"
    if (Test-Path $crash) { "CRASH LOG:"; Get-Content $crash } else { "crash.log: none" }
    if (Test-Path (Join-Path $data "session.json")) { "session saved: yes" }
}
finally {
    if (-not $p.HasExited) { $p.Kill() }
    Remove-Item Env:NICPAD_DATA_DIR
    Remove-Item -Recurse -Force $data -ErrorAction SilentlyContinue
}
