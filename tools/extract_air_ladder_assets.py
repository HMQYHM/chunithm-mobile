"""Extract AirLadder plate/frame geometry and DDS layers from original AVTS SVOs.
The PNCT payload is position (3 floats), packed normal, packed colour, UV (2 floats).
The plate has three horizontal UV sections; the frame uses the indexed triangle
strip whose vertex indices start at 8. No guessed faces are taken from another note.
"""
import argparse, io, struct, uuid
from pathlib import Path
from PIL import Image


def entries(data):
    assert data[:4] == b'AVTS'
    for i in range(struct.unpack_from('<I', data, 4)[0]):
        start = 128 + 1024*i
        name = data[start:start+512].split(b'\0')[0].decode()
        size, offset = struct.unpack_from('<II', data, start+520)
        yield name, data[offset:offset+size]


def write_obj(path, vertices, triangles, source):
    lines = [f'# Original AirLadder PNCT geometry from {source}', f'o {path.stem}']
    lines += ['v ' + ' '.join(format(v, '.9g') for v in row[:3]) for row in vertices]
    lines += ['vt ' + ' '.join(format(v, '.9g') for v in row[5:7]) for row in vertices]
    lines += ['f ' + ' '.join(f'{i+1}/{i+1}' for i in tri) for tri in triangles]
    path.write_text('\n'.join(lines)+'\n')


def extract(source, project):
    meshes = project/'Assets/Resources/OriginalMeshes'; textures = project/'Assets/Resources/OriginalNotes/field'
    for kind in ('airlad', 'airladtop'):
        for width in range(1,17):
            name = f'ntt_{kind}_{width:02d}'
            records = list(entries((source/f'{name}.svo').read_bytes()))
            payload = next(data for key,data in records if key.endswith('PNCT_0000.VBO'))
            assert len(payload) % 28 == 0, (name,len(payload))
            vs = [struct.unpack_from('<3fII2f',payload,i*28) for i in range(len(payload)//28)]
            plate_count = len(vs) - 16
            indices = next(data for key,data in records if key.endswith('.IBO'))
            idx = struct.unpack('<'+'H'*(len(indices)//2),indices)
            # The frame strip starts at the first frame-only index. Retain its
            # original winding and skip only strip-degenerate triangles.
            # The first 10 indices address the separate additive PNCTT mesh.
            bar_start = 35 if kind == "airladtop" else 10
            start = next(i for i in range(bar_start,len(idx)) if idx[i] >= plate_count)
            frame=[]; plate=[]
            for begin,end,base,dest in [(bar_start,start,0,plate),(start,len(idx),plate_count,frame)]:
                for i in range(begin,end-2):
                    tri=(idx[i],idx[i+1],idx[i+2]) if (i-begin)%2==0 else (idx[i+1],idx[i],idx[i+2])
                    if min(tri)>=base and max(tri)<(plate_count if base==0 else len(vs)) and len(set(tri))==3:
                        dest.append(tuple(x-base for x in tri))
            assert plate and frame, name
            write_obj(meshes/f'{name}_bar.obj',vs[:plate_count],plate,name)
            write_obj(meshes/f'{name}_frame.obj',vs[plate_count:],frame,name)
            if width==4:
                for key,data in records:
                    if key.endswith(('_bar.dds','_waku.dds','_effEx.dds')):
                        texture_name = key.replace('__HndToSvo__ntt_','').replace('.dds','.png')
                        Image.open(io.BytesIO(data)).convert('RGBA').save(textures/texture_name)
    # Preserve Unity importer settings and assign deterministic GUIDs so copies
    # of the project refer to the same extracted assets.
    for path in list(meshes.glob('ntt_airlad*_*.obj'))+list(textures.glob('airlad*.png')):
        template = meshes/'ntt_ahact_04.obj.meta' if path.suffix=='.obj' else textures.parent/'ahact_04.png.meta'
        meta=template.read_text(); import re
        meta=re.sub(r'^guid: .*$',f'guid: {uuid.uuid5(uuid.NAMESPACE_URL,"chumobile/"+path.name).hex}',meta,flags=re.M)
        if path.suffix=='.png':
            meta=meta.replace('enableMipMap: 1','enableMipMap: 0').replace('textureCompression: 1','textureCompression: 0')
            meta=meta.replace('wrapU: 0','wrapU: 1').replace('wrapV: 0','wrapV: 1')
        Path(str(path)+'.meta').write_text(meta)

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('svo_directory',type=Path);p.add_argument('project',type=Path)
    a=p.parse_args();extract(a.svo_directory,a.project)
