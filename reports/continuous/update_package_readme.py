from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
path=ROOT/'tools/ci/package_windows.py'
source=path.read_text(encoding='utf8')
start=source.index('        (package / "README_中文.txt").write_text(')
end=source.index('\n        files = ',start)
replacement='''        shutil.copyfile(ROOT / "tools/ci/windows_README_中文.txt", package / "README_中文.txt")
        verification = {"packaged_runtime_checks":before,"named_checks":[],"human_signoff":None}
        latest_by_action = {}
        for path in sorted((ROOT / "reports/build").glob("*/result.json")):
            entry = json.loads(path.read_text(encoding="utf8"))
            if entry.get("result")=="verified":latest_by_action[entry["action"]]=path.parent
        for action, directory in latest_by_action.items():
            for filename in ["test_results.json","client_results.json","campaign_client_results.json","vessel_client_results.json","chapter_two_client_results.json","chapter_three_client_results.json","chapter_four_client_results.json","chapter_five_client_results.json","chapter_six_client_results.json","finale_client_results.json"]:
                candidate=directory / filename
                if candidate.is_file():verification["named_checks"].append({"action":action,"report":candidate.relative_to(ROOT).as_posix(),"data":json.loads(candidate.read_text(encoding="utf8"))})
        write(package / "VERIFICATION.json", verification)'''
path.write_text(source[:start]+replacement+source[end:],encoding='utf8')
print('Updated package README and exact named engineering evidence collection.')
