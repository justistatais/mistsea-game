from pathlib import Path
p=Path(__file__).resolve().parents[2]/'game/Diagnostics/Root.R3ShiftWorkClientTests.cs'
s=p.read_text(encoding='utf8');assert 'Environment.GetEnvironmentVariable' in s;s=s.replace('Environment.GetEnvironmentVariable','System.Environment.GetEnvironmentVariable').replace('"MISTSEA_TEST_OUTPUT"','"MISTSEA_CLIENT_TEST_OUTPUT"');p.write_text(s,encoding='utf8')
