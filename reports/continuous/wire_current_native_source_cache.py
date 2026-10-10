from pathlib import Path
r=Path(__file__).resolve().parents[2];p=r/'tools/ci/build.py';s=p.read_text(encoding='utf8')
start=s.index('            route_output=run("actual_vessel_route_source",source_argv)')
end=s.index('        if action in {"smoke",',start)
block=s[start:end];line=next(l for l in block.splitlines() if l.startswith('            source_name='))
rest=block.replace(line+'\n','');assert 'source_name=' not in rest
s=s[:start]+line+'\n'+'''            if os.environ.get("MISTSEA_NATIVE_SOURCE_SUITE"):
                from native_source_cache import copy_current_suite_entry
                copy_current_suite_entry(os.environ["MISTSEA_NATIVE_SOURCE_SUITE"],out,source_task,source_marker,source_name,current_inputs(),compiled_binaries())
            else:
'''+''.join('    '+l+'\n' for l in rest.splitlines())+s[end:]
p.write_text(s,encoding='utf8')
compile(s,str(p),'exec');print('Current-suite untouched source reuse wired; actual final-suite execution required.')
