param(
    [Parameter(Mandatory=$true)][string]$UnityPath,
    [string]$ProjectPath,
    [string]$PythonPath,
    [switch]$ExtendedPackages
)
$ErrorActionPreference = 'Stop'
if ($PythonPath) { $env:OPTIFUNITY_TEST_PYTHON = $PythonPath }
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$ProjectPath) {
    $hashBytes = [System.Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($repoRoot))
    $repoKey = ([BitConverter]::ToString($hashBytes)).Replace('-', '').Substring(0, 12)
    $ProjectPath = Join-Path ([IO.Path]::GetTempPath()) "Optifunity-Validation-$repoKey"
}
$ProjectPath = [IO.Path]::GetFullPath($ProjectPath)
if ($ProjectPath.Equals($repoRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $ProjectPath.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Validation project must be outside the local UPM package to avoid recursive package/import mappings.'
}
if (Test-Path (Join-Path $ProjectPath 'Assets')) {
    if (!(Test-Path (Join-Path $ProjectPath '.optifunity-fixture'))) {
        throw 'Refusing to modify an existing project without the fixture ownership marker.'
    }
}
New-Item -ItemType Directory -Force -Path "$ProjectPath/Assets","$ProjectPath/Packages","$ProjectPath/ProjectSettings" | Out-Null
Set-Content "$ProjectPath/.optifunity-fixture" 'Disposable validation fixture; never use a game project.'
New-Item -ItemType Directory -Force "$ProjectPath/Assets/OptifunityValidation" | Out-Null
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Fixtures') | ForEach-Object {
    $destination = Join-Path "$ProjectPath/Assets/OptifunityValidation" $_.Name
    New-Item -ItemType Directory -Force $destination | Out-Null
    Get-ChildItem -LiteralPath $_.FullName -File | Copy-Item -Destination $destination -Force
}
$dependencies = @{
    'com.h.optifunity' = 'file:' + $repoRoot.Replace('\','/')
    'com.unity.render-pipelines.universal' = '17.0.4'
    'com.unity.test-framework' = '1.4.5'
}
if ($ExtendedPackages) {
    $dependencies['com.unity.addressables'] = '2.3.16'
    $dependencies['com.unity.memoryprofiler'] = '1.1.11'
}
$manifest = @{
    dependencies = $dependencies
    testables = @('com.h.optifunity')
} | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText("$ProjectPath/Packages/manifest.json", $manifest)
[IO.File]::WriteAllText("$ProjectPath/ProjectSettings/ProjectVersion.txt", "m_EditorVersion: 6000.0.58f2`n")
$result = Join-Path $ProjectPath 'results.xml'
$log = Join-Path $ProjectPath 'validation.log'
if (Test-Path $result) { Remove-Item -LiteralPath $result }
$arguments = '-batchmode -projectPath "{0}" -runTests -testPlatform EditMode -testResults "{1}" -logFile "{2}"' -f $ProjectPath,$result,$log
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output "Unity validation started (PID $($process.Id)). Results: $result; log: $log"
# Return immediately so callers can inspect progress without blocking an interactive session.
