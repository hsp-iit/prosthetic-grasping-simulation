# Imports objects from Google Scanned Objects (GSO) into the Unity project, in the
# same format as the YCB objects:
#   Assets/3Dmodels/GSO/$name$/google_16k/textured.obj
#   Assets/3Dmodels/GSO/$name$/google_16k/textured.mtl
#   Assets/3Dmodels/GSO/$name$/google_16k/texture_map.png
#   Assets/3Dmodels/GSO/$name$/grasp_spec.json   (category and grasps, read by Unity)
#   Assets/3Dmodels/GSO/$name$/metadata.pbtxt    (GSO metadata, with the license)
# The prefabs (Assets/Prefabs/GSO/$name$.prefab) are then built in Unity from the
# top bar menu: Tools -> Grasp Objects -> Build GSO Prefabs.
#
# The objects to import are listed in gso_objects.json (the manifest), together with
# their category and grasps. The objects can be selected according to the training
# needs, e.g. only the ones providing a pinch_3 preshape or only the mugs.
#
# Usage (run from the repository root or from anywhere, paths are resolved
# w.r.t. this file):
#   python3 python_scripts/GSO/import_gso.py search mug
#   python3 python_scripts/GSO/import_gso.py add Cole_Hardware_Mug_Classic_Blue \
#       --category mug --grasp "large_diameter[susd]:left" --grasp "sphere_4fingers[nops]:top"
#   python3 python_scripts/GSO/import_gso.py list --preshapes pinch_3
#   python3 python_scripts/GSO/import_gso.py download --categories mug dispenser
#   python3 python_scripts/GSO/import_gso.py download --preshapes pinch_3 --max-per-category 2

import argparse
import json
import os
import re
import shutil
import sys
import tempfile
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from collections import Counter, OrderedDict

FUEL_URL = 'https://fuel.gazebosim.org/1.0'
FUEL_OWNER = 'GoogleResearch'

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_DIR = os.path.abspath(os.path.join(SCRIPT_DIR, '..', '..'))
MANIFEST_PATH = os.path.join(SCRIPT_DIR, 'gso_objects.json')
MODELS_DIR = os.path.join(REPO_DIR, 'Assets', '3Dmodels', 'GSO')
PREFABS_DIR = os.path.join(REPO_DIR, 'Assets', 'Prefabs', 'GSO')
GRASP_CATALOG_PATH = os.path.join(REPO_DIR, 'Assets', 'Scripts', 'Data_collection', 'GraspCatalog.cs')
# The catalog of the GSO models is cached here, since it takes ~1 minute to fetch it
CATALOG_CACHE_PATH = os.path.join(os.path.expanduser('~'), '.cache', 'gso_fuel_catalog.json')

APPROACHES = ['top', 'bottom', 'left', 'right', 'front', 'back']
GRASP_TAGS = ['[nops]', '[usd]', '[susd]', '[border]', '[left]', '[right]']


def read_grasp_type2preshape():
    # The table is read from GraspCatalog.cs, such that it is defined only once
    with open(GRASP_CATALOG_PATH) as f:
        source = f.read()
    block = re.search(r'graspType2preshape\s*=\s*new Dictionary<string, string>\(\)\s*\{(.*?)\};',
                      source, re.S).group(1)
    block = re.sub(r'//.*', '', block)
    return dict(re.findall(r'\{\s*"([^"]+)"\s*,\s*"([^"]+)"\s*\}', block))


GRASP_TYPE2PRESHAPE = read_grasp_type2preshape()


def grasp_type(grasp_name):
    for tag in GRASP_TAGS:
        grasp_name = grasp_name.replace(tag, '')
    return grasp_name


def preshape(grasp_name):
    return GRASP_TYPE2PRESHAPE.get(grasp_type(grasp_name))


def load_manifest():
    with open(MANIFEST_PATH) as f:
        return json.load(f, object_pairs_hook=OrderedDict)


def save_manifest(manifest):
    text = json.dumps(manifest, indent=2)
    # Keep the objects having only scalar values (i.e. the grasps) on one line
    text = re.sub(r'\{\n((?:[ ]+"[^"\n]+": (?:"[^"\n]*"|[^\n\[\]{}"]+),?\n)+)[ ]*\}',
                  lambda m: '{' + ' '.join(line.strip() for line in m.group(1).splitlines()) + '}', text)
    with open(MANIFEST_PATH, 'w', newline='\n') as f:
        f.write(text + '\n')


def validate_object(obj):
    errors = []
    if not obj.get('category'):
        errors.append('missing category')
    # Without automatic grasps, the grasp parallelepipeds are added by hand in the prefab
    if obj.get('auto_grasps', True) and not obj.get('grasps'):
        errors.append('no grasps (use manual grasps to add them by hand in the prefab)')
    if obj.get('farthest_to') not in (None, 'left', 'right', 'front', 'back'):
        errors.append('farthest_to must be one of left, right, front, back')
    for grasp in obj.get('grasps', []):
        if preshape(grasp['name']) is None:
            errors.append('unknown grasp type [{}] (known: {})'.format(
                grasp_type(grasp['name']), ', '.join(sorted(GRASP_TYPE2PRESHAPE))))
        if grasp.get('approach', 'top') not in APPROACHES:
            errors.append('grasp {}: approach must be one of {}'.format(grasp['name'], APPROACHES))
    return errors


def select_objects(manifest, args):
    selected = []
    per_category = Counter()
    for obj in manifest['objects']:
        if args.names and obj['name'] not in args.names:
            continue
        if args.categories and obj['category'] not in args.categories:
            continue
        grasp_names = [g['name'] for g in obj['grasps']]
        if args.preshapes and not any(preshape(g) in args.preshapes for g in grasp_names):
            continue
        if args.grasp_types and not any(grasp_type(g) in args.grasp_types for g in grasp_names):
            continue
        if args.max_per_category and per_category[obj['category']] >= args.max_per_category:
            continue
        per_category[obj['category']] += 1
        selected.append(obj)
    if args.names:
        missing = set(args.names) - set(o['name'] for o in manifest['objects'])
        if missing:
            sys.exit('Not in the manifest (add them with the add command): ' + ', '.join(sorted(missing)))
    return selected


def print_summary(objects):
    pairs = Counter()
    for obj in objects:
        imported = os.path.isfile(os.path.join(MODELS_DIR, obj['name'], 'grasp_spec.json'))
        grasps = ', '.join('{} ({})'.format(g['name'], preshape(g['name'])) for g in obj['grasps'])
        if not obj.get('auto_grasps', True):
            grasps = 'grasps placed by hand in the prefab'
        print('{} {:<75} {:<12} {}'.format('*' if imported else ' ', obj['name'], obj['category'], grasps))
        for g in obj['grasps']:
            pairs[preshape(g['name'])] += 1
    print('\n{} objects, {} object-grasp pairs placed automatically ({}). * = already imported'.format(
        len(objects), sum(pairs.values()),
        ', '.join('{}: {}'.format(k, v) for k, v in sorted(pairs.items()))))


def fetch_json(url):
    with urllib.request.urlopen(url, timeout=60) as response:
        return json.load(response)


def fuel_catalog(refresh=False):
    if not refresh and os.path.isfile(CATALOG_CACHE_PATH):
        with open(CATALOG_CACHE_PATH) as f:
            return json.load(f)
    models = []
    page = 1
    while True:
        try:
            batch = fetch_json('{}/{}/models?per_page=100&page={}'.format(FUEL_URL, FUEL_OWNER, page))
        except urllib.error.HTTPError as e:
            # Fuel answers 404 after the last page
            if e.code == 404:
                break
            raise
        if not batch:
            break
        models += batch
        page += 1
        print('\rFetched {} models'.format(len(models)), end='', file=sys.stderr)
    print(file=sys.stderr)
    os.makedirs(os.path.dirname(CATALOG_CACHE_PATH), exist_ok=True)
    with open(CATALOG_CACHE_PATH, 'w') as f:
        json.dump(models, f)
    return models


def cmd_search(args):
    manifest_names = set(o['name'] for o in load_manifest()['objects'])
    pattern = re.compile(args.pattern, re.I)
    found = 0
    for model in fuel_catalog(args.refresh):
        categories = model.get('categories', [])
        if args.fuel_category and args.fuel_category not in categories:
            continue
        if not (pattern.search(model['name']) or pattern.search(model.get('description', ''))):
            continue
        found += 1
        print('{} {:<80} {:<28} {:5.1f} MB'.format(
            'M' if model['name'] in manifest_names else ' ', model['name'],
            ', '.join(categories), model.get('filesize', 0) / 1e6))
    print('\n{} models found. M = already in the manifest'.format(found))


def cmd_add(args):
    manifest = load_manifest()
    existing = [i for i, o in enumerate(manifest['objects']) if o['name'] == args.name]
    if existing and not args.replace:
        sys.exit(args.name + ' is already in the manifest, use --replace or edit ' + MANIFEST_PATH)
    try:
        fetch_json('{}/{}/models/{}'.format(FUEL_URL, FUEL_OWNER, urllib.parse.quote(args.name)))
    except urllib.error.HTTPError:
        sys.exit(args.name + ' not found on Gazebo Fuel, use the search command to find the model name')
    obj = OrderedDict([('name', args.name), ('category', args.category)])
    if args.yaw:
        obj['yaw'] = args.yaw
    if args.farthest_to:
        obj['farthest_to'] = args.farthest_to
    if args.manual_grasps:
        obj['auto_grasps'] = False
    obj['grasps'] = []
    for grasp in args.grasp or []:
        name, _, approach = grasp.rpartition(':')
        if not name:
            sys.exit('--grasp must be NAME:APPROACH, e.g. "large_diameter[left]:left"')
        obj['grasps'].append(OrderedDict([('name', name), ('approach', approach)]))
    errors = validate_object(obj)
    if errors:
        sys.exit('Invalid object: ' + '; '.join(errors))
    if existing:
        manifest['objects'][existing[0]] = obj
    else:
        manifest['objects'].append(obj)
    save_manifest(manifest)
    # Keep the imported object in sync with the manifest
    if os.path.isdir(os.path.join(MODELS_DIR, args.name)):
        write_grasp_spec(obj)
    print(('Replaced ' if existing else 'Added ') + args.name)


def remove_asset(path):
    # Together with its Unity .meta file
    if os.path.isdir(path):
        shutil.rmtree(path)
    elif os.path.isfile(path):
        os.remove(path)
    if os.path.isfile(path + '.meta'):
        os.remove(path + '.meta')


def cmd_remove(args):
    manifest = load_manifest()
    objects = [o for o in manifest['objects'] if o['name'] != args.name]
    if len(objects) != len(manifest['objects']):
        manifest['objects'] = objects
        save_manifest(manifest)
        print('Removed {} from the manifest'.format(args.name))
    if not args.keep_files:
        remove_asset(os.path.join(MODELS_DIR, args.name))
        remove_asset(os.path.join(PREFABS_DIR, args.name + '.prefab'))
        print('Deleted the model and the prefab of ' + args.name)


def cmd_list(args):
    print_summary(select_objects(load_manifest(), args))


def download_model(name, destination):
    # Use the latest version of the model
    info = fetch_json('{}/{}/models/{}'.format(FUEL_URL, FUEL_OWNER, urllib.parse.quote(name)))
    url = '{}/{}/models/{}/{}/{}.zip'.format(
        FUEL_URL, FUEL_OWNER, urllib.parse.quote(name), info.get('version', 1), urllib.parse.quote(name))
    with urllib.request.urlopen(url, timeout=300) as response, open(destination, 'wb') as f:
        shutil.copyfileobj(response, f)


def convert_model(zip_path, obj):
    # Only the needed members are read, by name, hence the archive content is never
    # extracted as is
    with zipfile.ZipFile(zip_path) as archive:
        obj_text = archive.read('meshes/model.obj').decode('utf-8')
        mtl_text = archive.read('meshes/model.mtl').decode('utf-8')
        texture = archive.read('materials/textures/texture.png')
        metadata = archive.read('metadata.pbtxt')

    # Same file names as the YCB objects
    obj_text = re.sub(r'^mtllib .*$', 'mtllib textured.mtl', obj_text, count=1, flags=re.M)
    mtl_text = re.sub(r'^map_Kd .*$', 'map_Kd texture_map.png', mtl_text, flags=re.M)

    object_dir = os.path.join(MODELS_DIR, obj['name'])
    mesh_dir = os.path.join(object_dir, 'google_16k')
    os.makedirs(mesh_dir, exist_ok=True)
    with open(os.path.join(mesh_dir, 'textured.obj'), 'w') as f:
        f.write(obj_text)
    with open(os.path.join(mesh_dir, 'textured.mtl'), 'w') as f:
        f.write(mtl_text)
    with open(os.path.join(mesh_dir, 'texture_map.png'), 'wb') as f:
        f.write(texture)
    with open(os.path.join(object_dir, 'metadata.pbtxt'), 'wb') as f:
        f.write(metadata)
    write_grasp_spec(obj)


def write_grasp_spec(obj):
    with open(os.path.join(MODELS_DIR, obj['name'], 'grasp_spec.json'), 'w', newline='\n') as f:
        json.dump(obj, f, indent=2)
        f.write('\n')


def cmd_download(args):
    manifest = load_manifest()
    selected = select_objects(manifest, args)
    if not selected:
        sys.exit('No object selected')
    for obj in selected:
        errors = validate_object(obj)
        if errors:
            sys.exit('Invalid manifest entry {}: {}'.format(obj['name'], '; '.join(errors)))
    print_summary(selected)
    if args.dry_run:
        return

    os.makedirs(MODELS_DIR, exist_ok=True)
    for i, obj in enumerate(selected):
        mesh_path = os.path.join(MODELS_DIR, obj['name'], 'google_16k', 'textured.obj')
        prefix = '[{}/{}] {}'.format(i + 1, len(selected), obj['name'])
        if os.path.isfile(mesh_path) and not args.force:
            # The grasps may have been changed in the manifest
            write_grasp_spec(obj)
            print(prefix + ': already downloaded, grasp_spec.json updated')
            continue
        with tempfile.TemporaryDirectory() as tmp:
            zip_path = os.path.join(tmp, 'model.zip')
            print(prefix + ': downloading', flush=True)
            download_model(obj['name'], zip_path)
            convert_model(zip_path, obj)
    print('\nDone. Now in Unity: Tools -> Grasp Objects -> Build GSO Prefabs')


def add_filter_args(parser):
    parser.add_argument('--names', nargs='+', help='Object names (as in the manifest)')
    parser.add_argument('--categories', nargs='+', help='Object categories, e.g. mug dispenser')
    parser.add_argument('--preshapes', nargs='+',
                        help='Keep the objects having at least one grasp with one of these preshapes: '
                             + ', '.join(sorted(set(GRASP_TYPE2PRESHAPE.values()))))
    parser.add_argument('--grasp-types', nargs='+',
                        help='Keep the objects having at least one grasp of one of these types: '
                             + ', '.join(sorted(GRASP_TYPE2PRESHAPE)))
    parser.add_argument('--max-per-category', type=int, help='At most this many objects per category')


def main():
    parser = argparse.ArgumentParser(description='Import Google Scanned Objects into the Unity project')
    subparsers = parser.add_subparsers(dest='command', required=True)

    search = subparsers.add_parser('search', help='Search the GSO models on Gazebo Fuel by name/description')
    search.add_argument('pattern', help='Regular expression, case insensitive')
    search.add_argument('--fuel-category', help='GSO category, e.g. "Bottles and Cans and Cups"')
    search.add_argument('--refresh', action='store_true', help='Fetch the catalog again instead of using the cache')
    search.set_defaults(func=cmd_search)

    add = subparsers.add_parser('add', help='Add a GSO model to the manifest')
    add.add_argument('name', help='Model name on Gazebo Fuel')
    add.add_argument('--category', required=True, help='Object category used in the labels, e.g. mug')
    add.add_argument('--grasp', action='append',
                     help='NAME:APPROACH, e.g. "large_diameter[left][susd]:left". Approach: ' + ', '.join(APPROACHES))
    add.add_argument('--manual-grasps', action='store_true',
                     help='Do not place the grasp parallelepipeds automatically: they are added by hand in the prefab')
    add.add_argument('--replace', action='store_true', help='Replace the object if already in the manifest')
    add.add_argument('--yaw', type=float, default=0, help='Rotation (degrees) around the vertical axis')
    add.add_argument('--farthest-to', choices=['left', 'right', 'front', 'back'],
                     help='Rotate the model such that its most protruding part (e.g. a mug handle) faces this side')
    add.set_defaults(func=cmd_add)

    remove = subparsers.add_parser('remove', help='Remove an object from the manifest, with its model and prefab')
    remove.add_argument('name')
    remove.add_argument('--keep-files', action='store_true', help='Only remove it from the manifest')
    remove.set_defaults(func=cmd_remove)

    list_ = subparsers.add_parser('list', help='List the manifest objects passing the filters')
    add_filter_args(list_)
    list_.set_defaults(func=cmd_list)

    download = subparsers.add_parser('download', help='Download and convert the manifest objects passing the filters')
    add_filter_args(download)
    download.add_argument('--force', action='store_true', help='Download again the objects already imported')
    download.add_argument('--dry-run', action='store_true', help='Only print the selected objects')
    download.set_defaults(func=cmd_download)

    args = parser.parse_args()
    args.func(args)


if __name__ == '__main__':
    main()
