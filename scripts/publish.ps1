param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$engine = Join-Path $projectRoot 'tools/7zip/7za.exe'
$expectedHash = 'DD3757F31C97E33776F2590317D4FB12F2DA075DB3DBB1E3F94D0F2A01D84FFA'
if (!(Test-Path -LiteralPath $engine)) { throw '缺少 tools/7zip/7za.exe，请下载完整项目。' }
if ((Get-FileHash -LiteralPath $engine -Algorithm SHA256).Hash -ne $expectedHash) { throw '7-Zip 引擎 SHA256 校验失败。' }

if (!$SkipTests) {
    if (!$IsWindows) { throw '非 Windows 构建请先用本机 7-Zip 执行测试，再传入 -SkipTests 进行交叉发布。' }
    $previousEngine = $env:AUTO_PACK_7ZIP
    try {
        $env:AUTO_PACK_7ZIP = $engine
        & dotnet run --project (Join-Path $projectRoot 'tests/AutoPack.Tests') -c Release
        if ($LASTEXITCODE -ne 0) { throw '测试失败，未发布。' }
    }
    finally { $env:AUTO_PACK_7ZIP = $previousEngine }
}

$destination = Join-Path $projectRoot 'artifacts/AutoPack-win-x64'
if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Recurse -Force }
& dotnet publish (Join-Path $projectRoot 'src/AutoPack.WinForms') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None -o $destination
if ($LASTEXITCODE -ne 0) { throw 'Windows 发布失败。' }
$toolDestination = Join-Path $destination 'tools'
New-Item -ItemType Directory -Path $toolDestination -Force | Out-Null
Copy-Item -LiteralPath $engine -Destination (Join-Path $toolDestination '7za.exe')
Copy-Item -LiteralPath (Join-Path $projectRoot 'tools/7zip/License.txt') -Destination (Join-Path $toolDestination '7zip-License.txt')
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $destination

# 复制本次实际还原的运行时许可证，而不是假设单文件包能展示内置声明。
$assets = Get-Content -LiteralPath (Join-Path $projectRoot 'src/AutoPack.WinForms/obj/project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
$runtimePackages = @('Microsoft.NETCore.App.Runtime.win-x64', 'Microsoft.WindowsDesktop.App.Runtime.win-x64')
foreach ($packageName in $runtimePackages) {
    $dependency = $assets.project.frameworks.Values.downloadDependencies | Where-Object { $_.name -eq $packageName } | Select-Object -First 1
    if (!$dependency) { throw "无法找到运行时许可来源：$packageName" }
    $version = $dependency.version.Trim('[', ']').Split(',')[0].Trim()
    $packageDirectory = $null
    foreach ($packageRoot in $assets.packageFolders.Keys) {
        $candidate = Join-Path $packageRoot ($packageName.ToLowerInvariant() + '/' + $version)
        if (Test-Path -LiteralPath $candidate) { $packageDirectory = $candidate; break }
    }
    if (!$packageDirectory) { throw "找不到还原的运行时包：$packageName $version" }
    $licenseDestination = Join-Path $destination ('licenses/' + $packageName)
    New-Item -ItemType Directory -Path $licenseDestination -Force | Out-Null
    Get-ChildItem -LiteralPath $packageDirectory -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)' } | Copy-Item -Destination $licenseDestination
}
$archive = Join-Path $projectRoot 'artifacts/AutoPack-win-x64.zip'
Compress-Archive -Path $destination -DestinationPath $archive -Force
Write-Host "已生成：$archive"
Get-FileHash -LiteralPath $archive -Algorithm SHA256
