[CmdletBinding()]
param(
    [string]$ApiHost = 'md3h2ew6qe.re.qweatherapi.com',
    [double]$Longitude = 121.504751,
    [double]$Latitude = 38.837286
)

$ErrorActionPreference = 'Stop'

$secureApiKey = Read-Host '请输入和风天气 API Key（输入内容不会回显）' -AsSecureString
$apiKeyPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureApiKey)
try {
    $apiKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($apiKeyPointer)
    if ([string]::IsNullOrWhiteSpace($apiKey)) {
        throw 'API Key 不能为空。'
    }

    $settingsDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SolisMonitor'
    $settingsPath = Join-Path $settingsDirectory 'weather.json'
    [IO.Directory]::CreateDirectory($settingsDirectory) | Out-Null

    $settings = [ordered]@{
        schema = 1
        enabled = $true
        apiHost = $ApiHost.Trim()
        apiKey = $apiKey.Trim()
        location = ''
        locationId = $null
        longitude = $Longitude
        latitude = $Latitude
    }
    $json = $settings | ConvertTo-Json
    [IO.File]::WriteAllText($settingsPath, $json, [Text.UTF8Encoding]::new($false))
    Write-Host "天气配置已保存到：$settingsPath"
    Write-Host '请重启 Solis Monitor 使配置生效。'
}
finally {
    if ($apiKeyPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($apiKeyPointer)
    }
    $apiKey = $null
}
