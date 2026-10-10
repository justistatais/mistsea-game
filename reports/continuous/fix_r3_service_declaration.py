from pathlib import Path
p=Path(__file__).resolve().parents[2]/'src/Mist.Application/R3ShiftWork/R3ShiftWorkService.cs'
s=p.read_text(encoding='utf8')
line=next(x for x in s.splitlines() if 'AvailabilityId(string' in x and ', ' in x)
assert line.count('=>')==3
p.write_text(s.replace(line,line.replace(', ',';\n    private static string ')),encoding='utf8')
