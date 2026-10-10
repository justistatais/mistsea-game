"""Run reproducible local contract checks and write a bounded validation report."""
from __future__ import annotations
import io
import json
import platform
import unittest
from pathlib import Path
from datetime import datetime, timezone
from puzzle_reference import BASE, model_report


def main() -> int:
    import test_puzzles
    suite = unittest.defaultTestLoader.loadTestsFromModule(test_puzzles)
    log = io.StringIO()
    result = unittest.TextTestRunner(stream=log, verbosity=2).run(suite)
    (BASE/'validation/run_log.txt').write_text(log.getvalue(),encoding='utf-8')
    data = dict(
        generated_utc=datetime.now(timezone.utc).isoformat(),
        python=platform.python_version(),
        tests_run=result.testsRun, failures=len(result.failures), errors=len(result.errors),
        passed=result.wasSuccessful(),
        metadata=dict(puzzle_units=9,families=8,parent_task_bindings=32,
                      constraint_source_records=27,local_materials=12,
                      local_reading_materials=9,local_audio_scripts=3,rendered_audio_assets=0,
                      product_acceptance_cases=44),
        bounded_models=model_report(),
        excluded=['game_engine','collision_and_actual_paths','full_combat_and_economy',
                  'world_transactions_and_real_disk_crashes','free_text_semantic_parser',
                  'audio_quality','IELTS_6_5_empirical_calibration','learning_effectiveness'],
        interpretation='Passing checks establish only the encoded reference contracts. They are not a full-game or language-level certificate.'
    )
    (BASE/'validation/validation_report.json').write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:data[k] for k in ('tests_run','failures','errors','passed')},ensure_ascii=False))
    return 0 if result.wasSuccessful() else 1

if __name__=='__main__':
    raise SystemExit(main())
