param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$mistDictionaryRoot=[System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $mistDictionaryRoot -Force | Out-Null
$mistDictionaryVoice=New-Object -ComObject SAPI.SpVoice
$mistDictionaryToken=@($mistDictionaryVoice.GetVoices() | Where-Object { $_.GetDescription() -eq 'Microsoft Zira Desktop - English (United States)' })
if($mistDictionaryToken.Count -ne 1){throw 'Declared English voice unavailable.'}
$mistDictionaryVoice.Voice=$mistDictionaryToken[0]
$mistDictionaryTracks=@()
foreach($mistDictionaryWord in @('draught','draft','subject','bracket','isolate','isolated','isolation','operational','supersede','supersedes','interlock','reserve','beacon')){
 $mistDictionaryFile='dictionary-'+$mistDictionaryWord+'.wav'
 $mistDictionaryPath=[System.IO.Path]::Combine($mistDictionaryRoot,$mistDictionaryFile)
 $mistDictionaryStream=New-Object -ComObject SAPI.SpFileStream
 try{$mistDictionaryStream.Open($mistDictionaryPath,3,$false);$mistDictionaryVoice.AudioOutputStream=$mistDictionaryStream;$null=$mistDictionaryVoice.Speak($mistDictionaryWord,0)}finally{$mistDictionaryStream.Close()}
 $mistDictionaryTracks+=@{word=$mistDictionaryWord;audio_path='audio/'+$mistDictionaryFile;audio_sha256=(Get-FileHash -LiteralPath $mistDictionaryPath -Algorithm SHA256).Hash.ToLowerInvariant();bytes=(Get-Item -LiteralPath $mistDictionaryPath).Length;voice=$mistDictionaryToken[0].GetDescription();review_status='local_tts_candidate';rights_status='pending_HV-004'}
}
$mistDictionaryTracks | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath ([System.IO.Path]::Combine($mistDictionaryRoot,'dictionary_generation.json')) -Encoding utf8NoBOM
Write-Output ('Generated '+$mistDictionaryTracks.Count+' actual dictionary clips.')
