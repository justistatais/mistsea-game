param([Parameter(Mandatory=$true)][string]$InputPath,[Parameter(Mandatory=$true)][string]$ContentDirectory)
$ErrorActionPreference='Stop'
$mistRegionalInput=[System.IO.File]::ReadAllText([System.IO.Path]::GetFullPath($InputPath),[System.Text.Encoding]::UTF8) | ConvertFrom-Json
$mistRegionalRoot=[System.IO.Path]::GetFullPath($ContentDirectory)
$mistRegionalVoice=New-Object -ComObject SAPI.SpVoice
$mistDeclaredVoice='Microsoft Zira Desktop - English (United States)'
$mistAvailable=@($mistRegionalVoice.GetVoices() | Where-Object { $_.GetDescription() -eq $mistDeclaredVoice })
if($mistAvailable.Count -ne 1){throw 'The exact declared offline English candidate voice is unavailable.'}
$mistRegionalVoice.Voice=$mistAvailable[0]
$mistRegionalVoice.Rate=0
$mistRegionalResults=@()
function MistRegionalHash([byte[]]$Value){$mistHash=[System.Security.Cryptography.SHA256]::Create();try{return [System.BitConverter]::ToString($mistHash.ComputeHash($Value)).Replace('-','').ToLowerInvariant()}finally{$mistHash.Dispose()}}
$mistRegionalIndex=[System.IO.Path]::Combine($mistRegionalRoot,'audio','regional_generation.json')
$mistOldTracks=@()
if([System.IO.File]::Exists($mistRegionalIndex)){$mistOldTracks=([System.IO.File]::ReadAllText($mistRegionalIndex,[System.Text.Encoding]::UTF8)|ConvertFrom-Json)}
foreach($mistTrack in $mistRegionalInput){
    if($mistTrack.voice -ne $mistDeclaredVoice -or $mistTrack.audio_path -notmatch '^audio/regional/[A-Za-z0-9_.-]+\.wav$'){throw 'Invalid declared voice or bounded candidate output path.'}
    $mistSpeechBytes=[System.Text.Encoding]::UTF8.GetBytes($mistTrack.speech_text)
    if((MistRegionalHash $mistSpeechBytes) -ne $mistTrack.speech_sha256){throw 'Exact speech slice hash differs from preparation.'}
    $mistWavePath=[System.IO.Path]::GetFullPath([System.IO.Path]::Combine($mistRegionalRoot,$mistTrack.audio_path))
    if(-not $mistWavePath.StartsWith($mistRegionalRoot+[System.IO.Path]::DirectorySeparatorChar,[System.StringComparison]::OrdinalIgnoreCase)){throw 'Audio target leaves the declared content directory.'}
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($mistWavePath))|Out-Null
    $mistKnown=@($mistOldTracks|Where-Object audio_path -eq $mistTrack.audio_path)
    if([System.IO.File]::Exists($mistWavePath)){
        if($mistKnown.Count -ne 1 -or $mistKnown[0].text_sha256 -ne $mistTrack.text_sha256 -or $mistKnown[0].speech_sha256 -ne $mistTrack.speech_sha256 -or (MistRegionalHash ([System.IO.File]::ReadAllBytes($mistWavePath))) -ne $mistKnown[0].audio_sha256){throw 'An existing audio version has different or unregistered bytes; use a new version, never silently overwrite.'}
        $mistRegionalResults+=$mistKnown[0];continue
    }
    $mistWaveStream=New-Object -ComObject SAPI.SpFileStream
    try{$mistWaveStream.Open($mistWavePath,3,$false);$mistRegionalVoice.AudioOutputStream=$mistWaveStream;$null=$mistRegionalVoice.Speak($mistTrack.speech_text,0)}finally{$mistWaveStream.Close()}
    $mistActualWave=[System.IO.File]::ReadAllBytes($mistWavePath)
    $mistRecord=[ordered]@{}
    foreach($mistProperty in $mistTrack.PSObject.Properties){if($mistProperty.Name -ne 'speech_text'){$mistRecord[$mistProperty.Name]=$mistProperty.Value}}
    $mistRecord.audio_sha256=MistRegionalHash $mistActualWave
    $mistRecord.bytes=$mistActualWave.Length
    $mistRegionalResults+=$mistRecord
    # Persist each actual completed track so an interrupted run can resume without replacing it.
    [System.IO.File]::WriteAllText($mistRegionalIndex,($mistRegionalResults|ConvertTo-Json -Depth 12),[System.Text.UTF8Encoding]::new($false))
}
[System.IO.File]::WriteAllText($mistRegionalIndex,($mistRegionalResults|ConvertTo-Json -Depth 12),[System.Text.UTF8Encoding]::new($false))
Write-Output ('Rendered or verified '+$mistRegionalResults.Count+' actual offline candidate tracks. Human audition, contrastive voices, alignment, calibration and redistribution review remain pending.')
