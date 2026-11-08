"""Check native projects for missing explicit source, definition and local project inputs."""
import argparse
import json
from pathlib import Path
import xml.etree.ElementTree as ET


def inspect(root, templates):
    """Return concrete missing inputs separately from SDK templates and MSBuild expressions."""
    root = root.resolve()
    template_paths = {(root / path).resolve() for path in templates}
    missing, evaluated, skipped = [], [], []
    projects = list(root.rglob('*.vcxproj'))
    for project in sorted(projects):
        if any(part in ('.git', 'artifacts', 'node_modules') for part in project.relative_to(root).parts):
            continue
        if project.resolve() in template_paths:
            skipped.append(project.relative_to(root).as_posix())
            continue
        for item in ET.parse(project).getroot().iter():
            kind = item.tag.rsplit('}', 1)[-1]
            value = item.get('Project') if kind == 'Import' else item.get('Include')
            if kind not in ('ClCompile', 'ClInclude', 'ProjectReference', 'Import', 'None') or not value:
                continue
            value = value.replace('$(ProjectDir)', str(project.parent) + '/').replace('$(MSBuildThisFileDirectory)', str(project.parent) + '/')
            row = {'project': project.relative_to(root).as_posix(), 'kind': kind, 'input': value}
            if '$(' in value or '%(' in value or '*' in value:
                evaluated.append(row)
                continue
            target = (project.parent / value.replace('\\', '/')).resolve()
            if not target.is_relative_to(root) or not target.is_file():
                missing.append(row)
    if template_paths - {project.resolve() for project in projects}:
        raise ValueError('A declared template project does not exist under the source root')
    return {'missing': missing, 'requires_msbuild_evaluation': evaluated, 'template_projects': skipped,
        'checked_projects': len(projects) - len(skipped)}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument('--template-project', action='append', default=[])
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    result = inspect(args.root, args.template_project)
    if args.output:
        args.output.write_text(json.dumps(result, indent=2), encoding='utf-8')
    for row in result['missing']:
        print(f"MISSING {row['project']}: {row['kind']} {row['input']}")
    print(f"Checked {result['checked_projects']} native projects; {len(result['missing'])} missing explicit inputs; "
        f"{len(result['template_projects'])} export templates; {len(result['requires_msbuild_evaluation'])} MSBuild expressions.")
    raise SystemExit(1 if result['missing'] else 0)
