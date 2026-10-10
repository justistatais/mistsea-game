"""Identify the actual build and player-content inputs for an engineering run."""
import hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
def current_inputs():
 paths=set()
 for base in ['src','services','tests','game','tools/Mist.ContentCli','tools/Mist.ReviewCalibration','schemas/sql']:
  for p in (ROOT/base).rglob('*'):
   if not p.is_file() or any(part in {'bin','obj','.godot'} for part in p.relative_to(ROOT).parts):continue
   if p.suffix in {'.cs','.csproj','.sql'} or p.name=='packages.lock.json':paths.add(p)
 for p in (ROOT/'game/content').rglob('*'):
  if p.is_file() and p.suffix in {'.json','.wav','.png','.svg','.ttf','.otf'}:paths.add(p)
 for p in (ROOT/'content/source').rglob('*.json'):
  if p.is_file() and not any('author_only' in part for part in p.relative_to(ROOT).parts):paths.add(p)
 for p in (ROOT/'tools/ci').glob('*'):
  if p.is_file() and p.suffix in {'.py','.txt'}:paths.add(p)
 for name in ['global.json','Directory.Build.props','Directory.Build.targets','MistSea.sln','game/project.godot','game/export_presets.cfg']:
  p=ROOT/name
  if p.is_file():paths.add(p)
 return [{'path':p.relative_to(ROOT).as_posix(),'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p in sorted(paths)]
