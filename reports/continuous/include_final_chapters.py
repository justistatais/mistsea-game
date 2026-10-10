from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
path=ROOT/'tests/Mist.Tests/ChapterSix/ChapterSixCases.cs.pending'
source=path.read_text(encoding='utf8');start=source.index('    public sealed class CheckpointArchive(');end=source.index('    private static void VesselWalk',start)
source=source[:start]+'''    public sealed class CheckpointArchive(ProfileStore store):IEndingCheckpointArchive
    {
        public ValueTask<ProtectedEndingCheckpoint> SavePreFinalAsync(SessionView expected,long sequence,Guid preparation,CancellationToken cancellationToken=default)=>store.SavePreFinalAsync(expected,sequence,preparation,cancellationToken);
        public ValueTask<ProtectedEndingCheckpoint?> FindAsync(string id,CancellationToken cancellationToken=default)=>store.FindAsync(id,cancellationToken);
    }
'''+source[end:];path.write_text(source,encoding='utf8')
files=list((ROOT/'src/Mist.Application/ChapterSix').glob('*.cs.pending'))+[path,ROOT/'tests/Mist.Tests/Finale/FinaleCases.cs.pending',ROOT/'game/Debug/FinaleBattleCandidate.cs.pending',ROOT/'game/UI/Shell/Root.Finale.cs.pending']
for pending in files:
    target=pending.with_suffix('');assert not target.exists(),str(target);pending.rename(target)
path=ROOT/'tests/Mist.Tests/Mist.Tests.csproj';source=path.read_text(encoding='utf8');assert 'FinaleBattleCandidate' not in source
source=source.replace('  <ItemGroup>\n    <ProjectReference','  <ItemGroup><Compile Include="../../game/Debug/FinaleBattleCandidate.cs" Link="Combat/FinaleBattleCandidate.cs" /></ItemGroup>\n  <ItemGroup>\n    <ProjectReference',1);path.write_text(source,encoding='utf8')
path=ROOT/'tests/Mist.Tests/Program.cs';source=path.read_text(encoding='utf8');assert 'ChapterSixCases' not in source
source=source.replace('Mist.Tests.Finale.FinaleBattleCases.Register();','Mist.Tests.Finale.FinaleBattleCases.Register();\nMist.Tests.ChapterSix.ChapterSixCases.Register();\nMist.Tests.Finale.FinaleCases.Register();');path.write_text(source,encoding='utf8')
print('Included frozen R6 and RF runtime/tests/debug driver/UI; checkpoint helper delegates to production SQLite storage.')
