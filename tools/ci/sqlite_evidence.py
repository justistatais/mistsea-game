"""Read-only evidence URIs, including real extended Windows file paths."""
import os
from pathlib import Path
from urllib.parse import quote

def readonly_uri(path, *, immutable=False):
    p=Path(path).resolve()
    if os.name=='nt':
        native=str(p)
        if not native.startswith('\\\\?\\'):
            native='\\\\?\\UNC\\'+native[2:] if native.startswith('\\\\') else '\\\\?\\'+native
        uri='file:'+quote(native,safe='')+'?mode=ro&vfs=win32-longpath'
    else:
        uri=p.as_uri()+'?mode=ro'
    return uri+('&immutable=1' if immutable else '')
