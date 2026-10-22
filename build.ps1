param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist')
)

$ErrorActionPreference = 'Stop'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('PCLRestore-Build-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $cecilPackage = Join-Path $temporary 'Mono.Cecil.nupkg'
    $cecilPackageUrl = 'https://api.nuget.org/v3-flatcontainer/mono.cecil/0.11.6/mono.cecil.0.11.6.nupkg'
    Invoke-WebRequest -Uri $cecilPackageUrl -OutFile $cecilPackage
    $cecilHash = (Get-FileHash -LiteralPath $cecilPackage -Algorithm SHA256).Hash
    if ($cecilHash -ne 'D2A23832AAA948BA9A01ACC42B5726E34C5F995958F1B30D45C0E7C70B3A72D5') {
        throw 'Mono.Cecil 下载文件校验失败。'
    }

    $donorArchive = Join-Path $temporary 'PCL2-2.14.zip'
    $donorUrl = 'https://raw.githubusercontent.com/Meloong-Git/PCL/5ef81e4/%E6%9C%80%E6%96%B0%E6%AD%A3%E5%BC%8F%E7%89%88.zip'
    Invoke-WebRequest -Uri $donorUrl -OutFile $donorArchive
    $archiveHash = (Get-FileHash -LiteralPath $donorArchive -Algorithm SHA256).Hash
    if ($archiveHash -ne '0835D346CD0F95C1472A3C0655D66E6CA8B7E4494CA0083768E3316F2CD4E7B1') {
        throw 'PCL 2.14.0.0 官方归档校验失败。'
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $cecilDirectory = Join-Path $temporary 'cecil'
    [IO.Compression.ZipFile]::ExtractToDirectory($cecilPackage, $cecilDirectory)
    $cecil = Join-Path $cecilDirectory 'lib\net40\Mono.Cecil.dll'
    $donor = Join-Path $temporary 'PCL2-2.14-original.exe'
    $archive = [IO.Compression.ZipFile]::OpenRead($donorArchive)
    try {
        $entry = $archive.GetEntry('Plain Craft Launcher 2.exe')
        if ($null -eq $entry) { throw '官方归档中没有 PCL 程序。' }
        $inputStream = $entry.Open()
        $outputStream = [IO.File]::Create($donor)
        try { $inputStream.CopyTo($outputStream) }
        finally { $outputStream.Dispose(); $inputStream.Dispose() }
    }
    finally { $archive.Dispose() }
    if ((Get-FileHash -LiteralPath $donor -Algorithm SHA256).Hash -ne
        '5582952DED3B443503F66C23B810802BD20F1A3E847B54D9E09653A825859A36') {
        throw 'PCL 2.14.0.0 程序校验失败。'
    }

    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path -LiteralPath $compiler)) {
        $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
    }
    if (-not (Test-Path -LiteralPath $compiler)) { throw '找不到 .NET Framework C# 编译器。' }
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    $outputFile = Join-Path $OutputDirectory 'PCL-McDownload-Restore.exe'
    $compilerArguments = @(
        '/nologo', '/target:winexe', '/optimize+',
        "/out:$outputFile", "/reference:$cecil",
        '/reference:System.Windows.Forms.dll', '/reference:System.Core.dll',
        '/reference:System.IO.Compression.dll',
        "/resource:$cecil,Mono.Cecil.dll", "/resource:$donorArchive,PCL2-2.14.zip",
        (Join-Path $PSScriptRoot 'PclRestore.cs')
    )
    & $compiler @compilerArguments
    if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
    Write-Output $outputFile
}
finally {
    if (Test-Path -LiteralPath $temporary) {
        Remove-Item -LiteralPath $temporary -Recurse -Force
    }
}
