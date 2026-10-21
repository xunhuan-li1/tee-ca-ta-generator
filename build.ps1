param(
    [string]$DotnetPath = 'dotnet',
    [switch]$SingleFile,
    [string]$OutputDirectory = 'release'
)
$ErrorActionPreference = 'Stop'
$sdk = Get-Command -Name $DotnetPath -CommandType Application -ErrorAction SilentlyContinue
if (-not $sdk) { throw 'Install .NET 10 SDK, or pass -DotnetPath with its dotnet.exe path.' }
$versions = @(& $sdk.Source --list-sdks)
if (-not ($versions -match '^10\.')) { throw '.NET 10 SDK is required to build the generator.' }
if ($SingleFile -and -not $PSBoundParameters.ContainsKey('OutputDirectory')) { $OutputDirectory = 'release-single' }
$destination = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $PSScriptRoot $OutputDirectory }
$arguments = @('publish', (Join-Path $PSScriptRoot 'src\GpStudio.csproj'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $destination, '--nologo')
if ($SingleFile) { $arguments += @('-p:PublishSingleFile=true', '-p:IncludeAllContentForSelfExtract=true', '-p:EnableCompressionInSingleFile=true', '-p:DebugType=None') }
& $sdk.Source @arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
foreach ($document in @('AI_GUIDE.md', 'README.md', 'LICENSE', 'NOTICE.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $document) -Destination $destination
}
$sdkRoot = Split-Path -Parent $sdk.Source
foreach ($document in @('LICENSE.txt', 'ThirdPartyNotices.txt')) {
    $source = Join-Path $sdkRoot $document
    if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination (Join-Path $destination ('DOTNET-' + $document)) }
}
Write-Output (Join-Path $destination 'GP-Project-Generator.exe')

Copy-Item -LiteralPath (Join-Path $PSScriptRoot "tools/formatter/LICENSE.md") -Destination (Join-Path $destination "LICENSE.LLVM.md")
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "tools/formatter/COPYING.md") -Destination (Join-Path $destination "COPYING.clang-format.md")
