from pathlib import Path
p=Path(__file__).resolve().parents[2]/'src/Mist.Application/R3ShiftWork/R3ShiftWorkService.cs'
s=p.read_text(encoding='utf8');assert 'post with{Accepted=true}' in s;p.write_text(s.replace('post with{Accepted=true}','post! with{Accepted=true}'),encoding='utf8')
