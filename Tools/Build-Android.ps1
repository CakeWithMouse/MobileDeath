param(
    [string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\6000.6.5f1\Editor\Unity.exe',
    [string]$WorkRoot = 'C:\Temp\BlinopekAndroid',
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repository 'UnityPart\PancakeCooker'
if (!$OutputDirectory) { $OutputDirectory = Join-Path $source 'Builds\Android' }
if (!(Test-Path -LiteralPath $UnityEditor)) { throw "Unity editor not found: $UnityEditor" }
$androidPlayer = Join-Path (Split-Path -Parent $UnityEditor) 'Data\PlaybackEngines\AndroidPlayer'
if (!(Test-Path -LiteralPath (Join-Path $androidPlayer 'UnityEditor.Android.Extensions.dll'))) {
    throw 'Install Android Build Support, Android SDK & NDK Tools and OpenJDK for the selected editor.'
}
if ($WorkRoot -match '[^\x00-\x7F]') { throw 'WorkRoot must use an ASCII-only path for the Android native toolchain.' }

# A fresh copy includes uncommitted assets and leaves the open editor's project alone.
$run = Join-Path $WorkRoot ('build-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$project = Join-Path $run 'project'
$build = Join-Path $run 'output'
$temporary = Join-Path $run 'temp'
New-Item -ItemType Directory -Path $project,$build,$temporary,$OutputDirectory -Force | Out-Null
foreach ($folder in @('Assets','Packages','ProjectSettings')) {
    & robocopy (Join-Path $source $folder) (Join-Path $project $folder) /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
    if ($LASTEXITCODE -ge 8) { throw "Could not copy $folder" }
}

$oldTemp = $env:TEMP
$oldTmp = $env:TMP
$oldGradle = $env:GRADLE_USER_HOME
$oldOutput = $env:BLINOPEK_ANDROID_APK
$log = Join-Path $build 'unity-android.log'
try {
    $env:TEMP = $temporary
    $env:TMP = $temporary
    $env:GRADLE_USER_HOME = Join-Path $WorkRoot 'gradle-cache'
    $env:BLINOPEK_ANDROID_APK = Join-Path $build 'Blinopek.apk'
    $arguments = @('-batchmode', '-nographics', '-quit', '-projectPath', ('"' + $project + '"'),
        '-buildTarget', 'Android', '-executeMethod', 'Blinopek.Editor.AndroidSetup.BuildPlayer',
        '-logFile', ('"' + $log + '"'))
    Write-Output "Building Android APK. Log: $log"
    $process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    if (Test-Path -LiteralPath $log) { Copy-Item -LiteralPath $log -Destination $OutputDirectory -Force }
    if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $env:BLINOPEK_ANDROID_APK)) {
        throw "Android build failed (exit $($process.ExitCode)). See $log"
    }
    Get-ChildItem -LiteralPath $build -File | Copy-Item -Destination $OutputDirectory -Force
    $apk = Join-Path $OutputDirectory 'Blinopek.apk'
    (Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash | Set-Content -LiteralPath (Join-Path $OutputDirectory 'Blinopek.apk.sha256')
    Write-Output "APK ready: $apk"
}
finally {
    $env:TEMP = $oldTemp
    $env:TMP = $oldTmp
    $env:GRADLE_USER_HOME = $oldGradle
    $env:BLINOPEK_ANDROID_APK = $oldOutput
}
