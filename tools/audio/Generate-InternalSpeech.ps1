param([Parameter(Mandatory=$true)][string]$CatalogPath,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$mistAudioCatalog=Get-Content -LiteralPath $CatalogPath -Raw -Encoding UTF8 | ConvertFrom-Json
$mistAudioRoot=[System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $mistAudioRoot -Force | Out-Null
$mistVoice=New-Object -ComObject SAPI.SpVoice
$mistEnglishVoice=@($mistVoice.GetVoices() | Where-Object { $_.GetDescription() -eq 'Microsoft Zira Desktop - English (United States)' })
if($mistEnglishVoice.Count -ne 1){ throw 'The declared local English voice is unavailable.' }
$mistVoice.Voice=$mistEnglishVoice[0]
$mistVoice.Rate=0
$mistGeneratedTracks=@()
foreach($mistMaterial in $mistAudioCatalog.materials | Where-Object modality -eq 'listening'){
    $mistSpeechText=($mistMaterial.paragraphs | ForEach-Object text) -join "`n"
    $mistFileName=$mistMaterial.id+'-'+$mistMaterial.version+'.wav'
    $mistWavePath=[System.IO.Path]::Combine($mistAudioRoot,$mistFileName)
    $mistStream=New-Object -ComObject SAPI.SpFileStream
    try {
        $mistStream.Open($mistWavePath,3,$false)
        $mistVoice.AudioOutputStream=$mistStream
        $null=$mistVoice.Speak($mistSpeechText,0)
    } finally { $mistStream.Close() }
    $mistGeneratedTracks+=@{material_id=$mistMaterial.id;material_version=$mistMaterial.version;text_sha256=$mistMaterial.body_sha256;audio_path='audio/'+$mistFileName;audio_sha256=(Get-FileHash -LiteralPath $mistWavePath -Algorithm SHA256).Hash.ToLowerInvariant();bytes=(Get-Item -LiteralPath $mistWavePath).Length;voice=$mistEnglishVoice[0].GetDescription();review_status='tts_candidate_not_human_reviewed';redistribution_rights='pending_HV-004'}
}
$mistGeneratedTracks | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath ([System.IO.Path]::Combine($mistAudioRoot,'generation.json')) -Encoding utf8NoBOM
Write-Output ('Generated '+$mistGeneratedTracks.Count+' actual local speech tracks; human listening/calibration/rights remain pending.')
