from pathlib import Path
r=Path(__file__).resolve().parents[2]
p=r/'tools/ci/build.py';s=p.read_text(encoding='utf8');assert 'actual_zero_XP_r3_shift_native_entry' in s;p.write_text(s.replace('actual_zero_XP_r3_shift_native_entry','Shift native entry has actual zero-XP'),encoding='utf8')
p=r/'game/UI/Campaign/CampaignClient.ReleaseProbe.cs';s=p.read_text(encoding='utf8');assert '"RunChapterFourLocalClientTests"' in s;p.write_text(s.replace('"RunChapterFourLocalClientTests"','"RunChapterFourLocalClientTests","RunVesselLifeClientTests","RunOptionalEncounterClientTests","RunSettlementSupportClientTests","RunLinaPersonalWorkClientTests","RunR3ShiftWorkClientTests"'),encoding='utf8')
