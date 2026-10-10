[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $LiteralPath,
    [Parameter(Mandatory = $true)] [ValidatePattern('^[0-9a-fA-F]{64}$')] [string] $ExpectedSha256
)

# Read-only input-byte preflight. A matching declared checksum is neither a trusted signature,
# a valid database/schema/content identity nor authorization to promote a migration candidate.
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
try {
    $sourceItem = [System.IO.FileInfo]::new([System.IO.Path]::GetFullPath($LiteralPath))
    $sourceStream = $null
    $sourceHasher = $null
    try {
        # .NET APIs work even when a spawning pwsh host supplied a PSModulePath without
        # WindowsPowerShell's Get-FileHash module. The read share also excludes mutation.
        $sourceStream = [System.IO.File]::Open($sourceItem.FullName, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
        $sourceBytes = $sourceStream.Length
        $sourceHasher = [System.Security.Cryptography.SHA256]::Create()
        $sourceHash = [System.BitConverter]::ToString($sourceHasher.ComputeHash($sourceStream)).Replace('-', '').ToLowerInvariant()
    }
    finally {
        if ($null -ne $sourceHasher) { $sourceHasher.Dispose() }
        if ($null -ne $sourceStream) { $sourceStream.Dispose() }
    }
    $matchesExpected = $sourceHash -ceq $ExpectedSha256.ToLowerInvariant()
    [ordered]@{
        task = 'CX-076'
        result = $(if ($matchesExpected) { 'source_checksum_matches' } else { 'source_checksum_mismatch' })
        source_file = $sourceItem.Name
        source_bytes = $sourceBytes
        expected_sha256 = $ExpectedSha256.ToLowerInvariant()
        actual_sha256 = $sourceHash
        checksum_matches = $matchesExpected
        source_scope = 'read-only actual file checksum preflight; source bytes are not uploaded or rewritten'
        database_validation = 'not_run'
        trusted_signature_validation = 'not_run'
        migration_execution = 'not_run'
        promotion_authorized = $false
        human_signoff = $null
        release_gate_accepted = $false
    } | ConvertTo-Json -Depth 4
    if ($matchesExpected) { exit 0 } else { exit 3 }
}
catch {
    [ordered]@{
        task = 'CX-076'; result = 'source_unavailable'; checksum_matches = $false
        error_type = $_.Exception.GetType().Name
        error_message = $_.Exception.Message
        database_validation = 'not_run'; migration_execution = 'not_run'; promotion_authorized = $false
        human_signoff = $null; release_gate_accepted = $false
    } | ConvertTo-Json -Depth 4
    exit 2
}
