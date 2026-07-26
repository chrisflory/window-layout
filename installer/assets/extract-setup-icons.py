import struct
import pathlib
import subprocess
import sys

path = pathlib.Path(r"c:\Users\cflor\Cursor\scripts\window-layout-kit\dist\WindowLayoutSetup.exe")
out = pathlib.Path(r"c:\Users\cflor\Cursor\scripts\window-layout-kit\installer\assets\_setup-icons")
out.mkdir(parents=True, exist_ok=True)

try:
    import pefile
except ImportError:
    subprocess.check_call([sys.executable, "-m", "pip", "install", "pefile", "-q"])
    import pefile

pe = pefile.PE(str(path))
RT_GROUP_ICON = pefile.RESOURCE_TYPE["RT_GROUP_ICON"]
RT_ICON = pefile.RESOURCE_TYPE["RT_ICON"]
icons = {}
groups = []

for entry in pe.DIRECTORY_ENTRY_RESOURCE.entries:
    if entry.id == RT_ICON:
        for e2 in entry.directory.entries:
            for e3 in e2.directory.entries:
                data_rva = e3.data.struct.OffsetToData
                size = e3.data.struct.Size
                blob = pe.get_data(data_rva, size)
                icons[e2.id] = blob
                kind = "PNG" if blob[:4] == b"\x89PNG" else "DIB"
                print(f"RT_ICON id={e2.id} {kind} size={size}")
                if kind == "PNG":
                    (out / f"icon-{e2.id}.png").write_bytes(blob)
    if entry.id == RT_GROUP_ICON:
        for e2 in entry.directory.entries:
            for e3 in e2.directory.entries:
                data_rva = e3.data.struct.OffsetToData
                size = e3.data.struct.Size
                blob = pe.get_data(data_rva, size)
                name = e2.name.string if e2.name else str(e2.id)
                groups.append((name, blob))
                print(f"RT_GROUP_ICON name={name} size={size}")

print("icon count", len(icons), "groups", len(groups))

for gid, grp in groups:
    if len(grp) < 6:
        continue
    _idReserved, _idType, idCount = struct.unpack_from("<HHH", grp, 0)
    entries = []
    off = 6
    for _ in range(idCount):
        bWidth, bHeight, bColorCount, bReserved, wPlanes, wBitCount, dwBytesInRes, nID = struct.unpack_from(
            "<BBBBHHIH", grp, off
        )
        off += 14
        w = 256 if bWidth == 0 else bWidth
        h = 256 if bHeight == 0 else bHeight
        entries.append((w, h, wPlanes, wBitCount, dwBytesInRes, nID))

    ico = bytearray()
    ico += struct.pack("<HHH", 0, 1, len(entries))
    data_offset = 6 + 16 * len(entries)
    blobs = []
    for w, h, planes, bitcount, nbytes, nid in entries:
        blob = icons.get(nid, b"")
        ico += struct.pack(
            "<BBBBHHII",
            0 if w == 256 else w,
            0 if h == 256 else h,
            0,
            0,
            planes,
            bitcount,
            len(blob),
            data_offset,
        )
        blobs.append(blob)
        data_offset += len(blob)
    for b in blobs:
        ico += b
    ip = out / f"group-{gid}.ico"
    ip.write_bytes(ico)
    print("wrote", ip.name, "frames", [(e[0], e[1], e[5]) for e in entries])
