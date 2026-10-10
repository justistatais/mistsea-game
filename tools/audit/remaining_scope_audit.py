"""Read-only inventory of design budgets, authored candidates and execution evidence.

Source discovery does not certify implementation or human approval. Only output files
under reports/continuous are written; this tool does not alter plans, source or profiles.
"""
from __future__ import annotations

import argparse
from collections import Counter
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[2]


def read_json(path: str | Path):
    return json.loads((ROOT / path).read_text(encoding="utf-8-sig"))


def text(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def file_fact(path: str | Path) -> dict:
    p = ROOT / path
    data = p.read_bytes()
    return {"path": p.relative_to(ROOT).as_posix(), "bytes": len(data),
            "sha256": hashlib.sha256(data).hexdigest()}


def inventory() -> dict:
    layout = read_json("reference/part03/Exploration_Layout_Reference_v0.1.json")
    puzzles = read_json("reference/part06/data/Puzzle_Reference_v0.1.json")
    combat = read_json("reference/part08/data/Combat_Reference_v0.1.json")
    design_scenes = {s["id"] for region in layout["regions"] for s in region["scenes"]}
    scene_candidates: dict[str, list[str]] = {}
    materials: dict[str, dict] = {}
    chapter_facts = []
    # Skip imported reference registries and author-only files. They register budgets,
    # which cannot count as new authored geometry or player paragraphs.
    for path in sorted((ROOT / "content/source").rglob("*.json")):
        if path.name.startswith("part") or "author_only" in path.name:
            continue
        try:
            value = read_json(path)
        except (ValueError, UnicodeError):
            continue
        if not isinstance(value, dict) or not isinstance(value.get("scenes"), list):
            continue
        relative = path.relative_to(ROOT).as_posix()
        for scene in value["scenes"]:
            if not all(k in scene for k in ("id", "walk", "ports", "spots")):
                continue
            scene_candidates.setdefault(scene["id"], []).append(relative)
        for m in value.get("materials", []):
            paragraphs = m.get("paragraphs", [])
            en = " ".join(p.get("en", "") for p in paragraphs) or m.get("english", "")
            zh = " ".join(p.get("zh", "") for p in paragraphs) or m.get("chinese", "")
            if not en.strip() or not zh.strip():
                continue
            mid = m["id"]
            record = materials.setdefault(mid, {"id": mid, "paths": [], "english_sha256": hashlib.sha256(en.encode()).hexdigest(), "english_words": len(re.findall(r"\b\w+(?:['’-]\w+)*\b", en)), "kind": m.get("kind", m.get("modality", "unknown")), "review": m.get("human_review", "not_recorded")})
            record["paths"].append(relative)
        chapter_facts.append({"path": relative, "geometry_candidates": len(value["scenes"]), "bilingual_material_candidates": len(value.get("materials", [])), "formal_runtime_or_human_completion": "not_inferred_from_json"})
    runtime = read_json("game/content/runtime_catalog.json") if (ROOT / "game/content/runtime_catalog.json").exists() else {}
    # Q102 has explicit geometry separate from the scene catalogue; catalogue scenes
    # only have IDs/actions and therefore cannot count as playable geometry.
    geometry = runtime.get("q102_geometry_json")
    if isinstance(geometry, str):
        geometry = json.loads(geometry)
    if isinstance(geometry, dict):
        for scene_id, scene in geometry.items():
            if "walk_rect" in scene and "ports" in scene:
                scene_candidates.setdefault(scene_id, []).append("game/content/runtime_catalog.json:q102_geometry_json")
    elif isinstance(geometry, list):
        for scene in geometry:
            scene_candidates.setdefault(scene["id"], []).append("game/content/runtime_catalog.json:q102_geometry_json")
    runtime_literals = {}
    for directory in (ROOT / "src/Mist.Application", ROOT / "src/Mist.Core"):
        for path in directory.rglob("*.cs"):
            if any(part in {"bin", "obj"} for part in path.parts):
                continue
            ids = sorted(set(re.findall(r'"(ENC-[A-Z0-9]+-\d+)"', path.read_text(encoding="utf-8"))))
            if ids:
                runtime_literals[path.relative_to(ROOT).as_posix()] = ids
    seen_enc = sorted({id for ids in runtime_literals.values() for id in ids})
    plan = read_json("planning/plan.json")
    execution = read_json("planning/state/execution.json")["tasks"]
    tasks = []
    for task in plan["tasks"]:
        state = execution.get(task["id"], {})
        report = ROOT / task["evidence_path"]
        row = {"id": task["id"], "kind": task["kind"], "title": task["title"], "execution_status": state.get("status", "missing"), "report_exists": report.exists(), "report": task["evidence_path"]}
        if report.exists():
            evidence = read_json(report)
            row.update(result=evidence.get("result"), not_run=evidence.get("not_run", []))
        tasks.append(row)
    local_lookup = text("src/Mist.Application/Materials/MaterialJournal.cs")
    words = sorted(set(re.findall(r'"([a-z]+)"', local_lookup)))
    original_budget = read_json("content/source/part05.json")["content_budget"]
    lexicon_paths = sorted((ROOT / "content/source/lexicon").glob("*.json"))
    lexical_entries = []
    for path in lexicon_paths:
        data = read_json(path)
        lexical_entries.extend(data.get("entries", []) if isinstance(data, dict) else data)
    source_files = ["planning/plan.json", "planning/state/execution.json", "docs/integration/MASTER_GDD_v0.1.md", "docs/integration/Integration_Decisions_v0.1.md", "reports/continuous/Scope_20261009.md", "reference/part03/Exploration_Layout_Reference_v0.1.json", "reference/part06/data/Puzzle_Reference_v0.1.json", "reference/part08/data/Combat_Reference_v0.1.json", "src/Mist.Application/Materials/MaterialJournal.cs", "tests/Mist.Tests/Finale/FinaleBattleCases.cs"]
    return {
        "generated_utc": datetime.now(timezone.utc).isoformat(),
        "scope": "read_only_source_inventory_not_acceptance_not_a_playthrough",
        "design": {"spaces": len(design_scenes), "puzzle_budget": puzzles["budget"], "registered_puzzle_reference_units": len(puzzles["units"]), "encounter_slots": len(combat["encounters"]), "benchmark_scenarios": len(combat["scenarios"]), "learning_budget": original_budget},
        "geometry": {"authored_candidate_distinct_ids": len(scene_candidates), "candidate_paths_by_id": scene_candidates, "design_ids_without_discovered_geometry": sorted(design_scenes-set(scene_candidates)), "scene_catalogue_ids": len(runtime.get("scenes", [])), "native_completion": "not_inferred_from_catalogue_or_geometry"},
        "authored_materials": {"distinct_bilingual_ids": len(materials), "materials": list(materials.values()), "packages": chapter_facts, "calibrated_completion": "not_inferred"},
        "encounters": {"source_literal_ids": seen_enc, "literal_locations": runtime_literals, "designed_ids_without_literal": sorted({x["id"] for x in combat["encounters"]}-set(seen_enc)), "literal_is_implementation": False, "finished_level_count": None, "reason": "A literal may name a slot, guard or fixture; real scene, mechanism, source, persistence, tests and human QA must be linked separately."},
        "lexicon": {"lookup_word_forms": words, "lookup_word_form_count": len(words), "separate_entries_in_lexicon_directory": len(lexical_entries), "budget": {"core": 360, "extension": 360}, "registered_count_is_complete_course": False},
        "task_status_counts": dict(Counter(row["execution_status"] for row in tasks)),
        "tasks": tasks, "source_snapshot": [file_fact(path) for path in source_files],
        "human_gates": "not_self_signed; explicit human reports and applicable versions required",
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", default="reports/continuous/remaining_scope_inventory_20261010.json")
    args = parser.parse_args()
    output = (ROOT / args.output).resolve()
    allowed = (ROOT / "reports/continuous").resolve()
    if not output.is_relative_to(allowed):
        raise ValueError("Audit output must stay under reports/continuous.")
    result = inventory()
    output.parent.mkdir(parents=True, exist_ok=True)
    pending = output.with_suffix(output.suffix+".pending")
    pending.write_text(json.dumps(result, ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
    pending.replace(output)
    print(json.dumps({"scope": result["scope"], "design_spaces": result["design"]["spaces"], "geometry_candidates": result["geometry"]["authored_candidate_distinct_ids"], "missing_geometry_ids": result["geometry"]["design_ids_without_discovered_geometry"], "puzzle_budget": result["design"]["puzzle_budget"]["total"], "encounter_slots": result["design"]["encounter_slots"], "lookup_word_forms": result["lexicon"]["lookup_word_form_count"], "lexicon_entries": result["lexicon"]["separate_entries_in_lexicon_directory"], "output": output.relative_to(ROOT).as_posix()}, ensure_ascii=False))


if __name__ == "__main__":
    main()
