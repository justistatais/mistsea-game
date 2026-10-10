from pathlib import Path
import json,shutil
R=Path(__file__).resolve().parents[2]
source=R/'reports/execution/CX-066/r1-life-20261010T051032919483Z/content'
new=json.loads((source/'audio/regional_generation.json').read_text(encoding='utf-8-sig'))
p=R/'game/content/audio/regional_generation.json';old=json.loads(p.read_text(encoding='utf-8-sig'))
assert len(new)==2 and all(b['material_version']=='R1-LIFE-20261010-v2' for b in new)
ids={b['material_id'] for b in new};all_bindings=[b for b in old if b['material_id'] not in ids]+new
assert len(all_bindings)==32
for b in new:shutil.copyfile(source/b['audio_path'],R/'game/content'/b['audio_path'])
p.write_text(json.dumps(all_bindings,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
p=R/'tools/audio/prepare_regional.py';s=p.read_text(encoding='utf8');s=s.replace('("chapter_two","chapter_three"','("r1_life","chapter_two","chapter_three"');p.write_text(s,encoding='utf8')
p=R/'tools/Mist.ContentCli/Validation/ContentCompiler.cs';s=p.read_text(encoding='utf8');s=s.replace('.Concat(finale?.Materials??[]).ToDictionary(m=>m.Id,StringComparer.Ordinal)','.Concat(finale?.Materials??[]).Concat(r1Life.Materials).ToDictionary(m=>m.Id,StringComparer.Ordinal)');s=s.replace('r1_life_original_editorial_language_calibration_and_two_audio_tracks_pending','r1_life_original_editorial_language_calibration_and_audio_audition_pending');p.write_text(s,encoding='utf8')
p=R/'tests/Mist.Tests/Assets/RegionalAudioCases.cs';s=p.read_text(encoding='utf8');s=s.replace('new[]{"chapter_two","chapter_three"','new[]{"r1_life","chapter_two","chapter_three"');s=s.replace('Thirty actual','Thirty-two actual').replace('bindings.Length==30&&bindings.Select(b=>b.MaterialId).Distinct().Count()==30','bindings.Length==32&&bindings.Select(b=>b.MaterialId).Distinct().Count()==32');p.write_text(s,encoding='utf8')
print('Two exact-source multi-role candidate recordings merged; validation still required')
