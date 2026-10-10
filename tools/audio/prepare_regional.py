"""Prepare exact source slices for local candidate speech; source learner files stay unchanged."""
import hashlib
import json
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
PREFIXES={"Serving witness:","Receiving witness:","Archive clerk:","Local operator:","Review desk:",
          "Custodian:","Freight clerk:","Oren:","Pera:","Player:","Resident representative:","Safety watcher:",
          "Signal clerk:","Stores clerk:","Workshop custodian:","Review officer:","Sian:","Technician:","Voss:",
          "Ada:","Dispatch officer:","Duty officer:","Mara:","Operator:","Receiver:","Receiving officer:",
          "Rook:","Service record:","Station channel:","Visitor:","Finn:","Tess:","Cargo watch:",
          "Local representative:","Technical reviewer:"}
VOICE="Microsoft Zira Desktop - English (United States)"

def sha(text):return hashlib.sha256(text.encode("utf8")).hexdigest()

def main():
    tracks=[]
    campaign=json.loads((ROOT/"content/source/campaign/first_entry.json").read_text(encoding="utf8"))
    campaign_materials=[{"id":m["id"],"kind":m["modality"],"content_version":m["version"],"paragraphs":[{"id":m["id"]+"-original","en":m["english"]}]} for m in campaign["materials"]]
    for directory in ("r1_life","chapter_two","chapter_three","chapter_four","chapter_five","chapter_six","finale"):
        path=ROOT/"content/source"/directory/"learner_package.json"
        if not path.is_file():continue
        package=json.loads(path.read_text(encoding="utf8"))
        for material in package["materials"]+(campaign_materials if directory=="chapter_two" else []):
            if material["kind"]!="listening_script":continue
            segments=[]
            for paragraph in material["paragraphs"]:
                text=paragraph["en"];matches=[p for p in PREFIXES if text.startswith(p)]
                assert len(matches)<=1
                prefix=matches[0] if matches else ""
                # A role label is metadata. The remaining exact source slice is spoken.
                start=len(prefix)
                if prefix:
                    while start<len(text) and text[start].isspace():start+=1
                assert text[start:].strip()
                segments.append({"paragraph_id":paragraph["id"],"literal_prefix":prefix,
                                 "speaker_id":prefix[:-1] if prefix else "narrator",
                                 "source_start_scalar":start,"source_end_scalar":len(text),
                                 "text":text[start:],"sha256":sha(text[start:])})
            full="\n\n".join(p["en"] for p in material["paragraphs"])
            spoken="\n\n".join(s["text"] for s in segments)
            version=material["content_version"]
            assert all(c.isalnum() or c in "-_." for c in material["id"]+version)
            tracks.append({"material_id":material["id"],"material_version":version,"text_sha256":sha(full),
                           "speech_sha256":sha(spoken),"audio_version":"local-tts-1","voice":VOICE,
                           "audio_path":f"audio/regional/{material['id']}-{version}-local-tts-1.wav",
                           "segments":segments,"speech_text":spoken,
                           "review_status":"rendered_candidate_not_auditioned","redistribution_rights":"pending_HV-004"})
    target=ROOT/"content/source/audio/regional_speech_input.json";target.parent.mkdir(parents=True,exist_ok=True)
    target.write_text(json.dumps(tracks,ensure_ascii=False,indent=2)+"\n",encoding="utf8")
    print(json.dumps({"source":str(target.relative_to(ROOT)),"tracks":len(tracks),"scope":"exact source-slice preparation; no sound generated, timing or human review inferred"}))

if __name__=="__main__":main()
