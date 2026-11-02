"""Reproducible, minimal GUID + source-symbol closure of the laser-xr MR scene.

Run from the repository root with Python 3. No Unity process is launched.
"""
from pathlib import Path
import hashlib
import json
import re
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[4]
SOURCE = ROOT / 'helping_sources/laser-xr/Assets'
DEST = ROOT / 'Assets/_SpatialMAS/InteractionLab/Reference'
GUID = re.compile(rb'guid:\s*([0-9a-f]{32})')

def index(folder):
    result = {}
    scan = subprocess.run(['rg', '--no-heading', '--with-filename', '^guid: [0-9a-f]{32}', str(folder), '-g', '*.meta'], capture_output=True, check=False)
    for line in scan.stdout.decode('utf-8-sig').splitlines():
        path, guid = line.rsplit(':guid: ', 1)
        result[guid.strip()] = Path(path[:-5])
    return result

def main():
    print('Indexing reference assets...', flush=True)
    source = index(SOURCE)
    print(f'Indexed {len(source)} reference assets; indexing installed packages...', flush=True)
    available = index(ROOT / 'Library/PackageCache')
    print(f'Indexed {len(available)} package assets; indexing project...', flush=True)
    source.update({g: p for g, p in available.items() if any('~' in s for s in p.parts) and g not in source})
    available.update({g: p for g, p in index(ROOT / 'Assets').items() if DEST not in p.parents})
    # Sample code is not imported from PackageCache/Samples~ by Unity.
    available = {g: p for g, p in available.items() if not any('~' in s for s in p.parts)}
    classes = {}
    for cs in SOURCE.rglob('*.cs'):
        if 'Editor' in cs.parts or 'Tests' in cs.parts:
            continue
        for symbol in re.findall(r'\b(?:class|struct|enum|interface)\s+(\w+)', cs.read_text(encoding='utf-8-sig')):
            classes.setdefault(symbol, []).append(cs)
    selected = set()
    external = {}
    unresolved = set()
    pending = [SOURCE / 'Scenes/MR_HandsInteraction.unity', SOURCE / 'Resources/MR_Hand_Passthrough_Translucent.mat']
    pending.extend(p for p in (SOURCE / 'Samples').rglob('*.asmdef') if 'Editor' not in p.parts)
    while pending:
        path = pending.pop()
        if path in selected or not path.is_file():
            continue
        selected.add(path)
        data = path.read_bytes()
        for raw in GUID.findall(data):
            guid = raw.decode()
            if guid.startswith('0000000000000000'):
                continue
            if guid in available:
                external[guid] = str(available[guid].relative_to(ROOT)).replace('\\', '/')
            elif guid in source:
                pending.append(source[guid])
            else:
                unresolved.add(guid)
        if path.suffix == '.cs':
            for token in set(re.findall(r'\b\w+\b', data.decode('utf-8-sig'))):
                matches = classes.get(token, [])
                if len(matches) == 1:
                    pending.extend(matches)
    # Upstream 3.6.1 simulator UI ships two dangling, optional sprite references.
    known_missing = {'25a7ddcf659944c8d89baff5fcdf9d2e', 'ac2724ca4693e43b8b66f8ef01bd8e3c'}
    if unresolved - known_missing:
        raise RuntimeError(f'Unexpected unresolved GUIDs: {sorted(unresolved - known_missing)}')
    files = []
    for path in sorted(selected):
        relative = path.relative_to(SOURCE) if SOURCE in path.parents else Path('PackageSamples', *[s.replace('~', '') for s in path.relative_to(ROOT / 'Library/PackageCache').parts])
        target = DEST / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, target)
        shutil.copy2(str(path) + '.meta', str(target) + '.meta')
        files.append({'path': str(relative).replace('\\', '/'), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
    adapt_port()
    for entry in files:
        entry['portedSha256'] = hashlib.sha256((DEST / entry['path']).read_bytes()).hexdigest()
    manifest = {'source': 'helping_sources/laser-xr/Assets', 'files': files, 'externalGuids': external, 'unresolvedGuids': sorted(unresolved)}
    (DEST.parent / 'provenance.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(f'Copied {len(selected)} assets, {sum(p.stat().st_size for p in selected):,} bytes; {len(external)} external GUIDs; {len(unresolved)} known optional simulator icon misses.')

def adapt_port():
    simulator_ui = DEST / 'Samples/XR Interaction Toolkit/3.6.1/XR Interaction Simulator/UI/XR Interaction Simulator UI.prefab'
    data = simulator_ui.read_text(encoding='utf-8-sig')
    for guid in ('25a7ddcf659944c8d89baff5fcdf9d2e', 'ac2724ca4693e43b8b66f8ef01bd8e3c'):
        data = data.replace('{fileID: 21300000, guid: ' + guid + ', type: 3}', '{fileID: 0}')
    simulator_ui.write_text(data, encoding='utf-8')

    cube = DEST / 'Scripts/MR/MRCubeInteraction.cs'
    data = cube.read_text(encoding='utf-8-sig')
    data = data.replace('private float m_AttractTravelSpeed = 10.0f;', 'private float m_AttractTravelSpeed = 10.0f;\n\n        [Tooltip("Summon a distant selection immediately. Disable only to compare the original timed attraction.")]\n        [SerializeField] private bool m_InstantSummon = true;')
    marker = 'm_AttractDuration = Mathf.Clamp(travelDist / Mathf.Max(1.0f, m_AttractTravelSpeed), 0.065f, 0.35f);'
    data = data.replace(marker, marker + '''
                    if (m_InstantSummon)
                    {
                        // Remove the far-ray attachment offset before this frame's XRI transform pass.
                        attachController.ResetOffset();
                        Transform objectAttach = grabInteractable.GetAttachTransform(args.interactorObject);
                        if (objectAttach != null)
                            transform.position += handAnchorPos - objectAttach.position;
                        if (rb != null) rb.position = transform.position;
                        m_IsAttractingToHand = false;
                        m_ObjectHistoryHead = m_ObjectHistoryCount = 0;
                    }
''')
    cube.write_text(data, encoding='utf-8')

    optimizer = DEST / 'Scripts/MR/MRPerformanceOptimizer.cs'
    data = optimizer.read_text(encoding='utf-8-sig')
    data = data.replace('        private void Awake()\n        {', '        private void Awake()\n        {\n            // Legacy Default-Line material renders inconsistently with URP stereo through Link.\n            foreach (var visual in FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.CurveVisualController>(FindObjectsInactive.Include, FindObjectsSortMode.None))\n                SpatialMAS.XR.XRInteractionRigSetup.ConfigureRayVisualMaterial(visual);\n')
    optimizer.write_text(data, encoding='utf-8')

    auto = DEST / 'Scripts/MR/MRSimulatorAutoHand.cs'
    data = auto.read_text(encoding='utf-8-sig')
    data = data.replace('public class MRSimulatorAutoHand : MonoBehaviour', '[DefaultExecutionOrder(-31000)]\n    public class MRSimulatorAutoHand : MonoBehaviour')
    data = data.replace('        private void Awake()', '        private static bool HasHardwareXRLoader()\n        {\n            // Presence can be false while Link connects or the headset is unworn.\n            // The initialized hardware loader still owns the hand provider.\n            var settings = UnityEngine.XR.Management.XRGeneralSettings.Instance;\n            return UnityEngine.XR.XRSettings.isDeviceActive ||\n                (settings != null && settings.Manager != null && settings.Manager.activeLoader != null);\n        }\n\n        private void Awake()', 1)
    data = data.replace('            Application.runInBackground = true;', '''            if (HasHardwareXRLoader())
            {
                gameObject.SetActive(false);
                return;
            }
            Application.runInBackground = true;''')
    data = data.replace('        private void Awake()\n        {', '''        private void Awake()
        {
            var lifecycle = GetComponent<UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation.SimulatedDeviceLifecycleManager>();
            if (lifecycle != null) lifecycle.removeOtherHMDDevices = false;''')
    data = data.replace('            EnsureHandMode();\n            HandleRoomNavigationAndShortcuts();', '''            if (HasHardwareXRLoader())
            {
                gameObject.SetActive(false);
                return;
            }
            EnsureHandMode();
            HandleRoomNavigationAndShortcuts();''')
    data = data.replace('DestroyImmediate(gameObject);', 'gameObject.SetActive(false);')
    auto.write_text(data, encoding='utf-8')

if __name__ == '__main__':
    main()
