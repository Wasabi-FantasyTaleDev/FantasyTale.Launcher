"""Explicit standalone slot-item framing for the private UI sample catalog."""

from dataclasses import asdict

import numpy as np

from bf_item_viewer.catalog.model import Kind
from bf_item_viewer.rendering.camera import ViewSettings, build_camera
from bf_item_viewer.rendering.compose import Composer
from bf_item_viewer.rendering.pose import evaluate, skin
from bf_item_viewer.rendering.scene import render
from bf_item_viewer.resources.textures import TextureResolver
from bf_item_viewer.service import PreviewResult


class PreviewAdapter:
    """Delegate native previews; fit only explicitly declared standalone slot items."""

    def __init__(self, session, definitions):
        self.session = session
        self.modes = {}
        for row in definitions:
            mode = row.get('preview_mode', 'native')
            if mode not in ('native', 'standalone_slot'):
                raise ValueError(f"Unknown preview_mode for {row['id']}: {mode}")
            if mode == 'standalone_slot':
                inset = float(row['preview_inset'])
                if not 0 < inset < .5:
                    raise ValueError(f"preview_inset must be between zero and .5: {row['id']}")
                quaternion = tuple(float(value) for value in row['preview_quaternion'])
                if len(quaternion) != 4 or not np.isfinite(quaternion).all() or np.linalg.norm(quaternion) <= 0:
                    raise ValueError(f"Invalid preview_quaternion: {row['id']}")
                self.modes[row['id']] = (inset, quaternion)
        self.composer = Composer(session.index)
        self.resolver = TextureResolver(session.index, session.profile.raw.get(
            'texture_pools', ['Models/Characters/Texture']))

    def __getattr__(self, name):
        return getattr(self.session, name)

    def render_preview(self, request):
        """Retain the renderer's pose and shading with a declared demo angle and framing."""
        if request.item_id not in self.modes:
            return self.session.render_preview(request)
        item = self.session.item(request.item_id)
        if item.kind != Kind.SLOT_ITEM:
            raise ValueError(f'standalone_slot requires a slot item: {item.item_id}')
        if request.wearer is not None or request.spray_index is not None:
            raise ValueError('Standalone demo samples do not support wearer or spray overrides')
        scene, composition = self.composer.compose(item)
        inset, quaternion = self.modes[item.item_id]
        camera = build_camera(self.session.profile, self.session.camera_record(item.item_id),
                              request.width, request.height,
                              ViewSettings(quaternion), request.canvas_scale)
        pose = evaluate(scene.motion, scene.frame, scene.face_forward) if scene.motion else None
        points = []
        for part in scene.parts:
            part_pose = evaluate(part.motion, 0) if part.motion else pose
            geometry = skin(part.mesh, part_pose, rigid=part.rigid)
            points.append(camera.to_screen(geometry.positions))
            if request.shading and self.session.shading.outline_colour is not None:
                points.append(camera.to_screen(geometry.positions + geometry.normals
                                               * self.session.shading.outline_thickness))
        screen = np.concatenate(points)
        if not np.isfinite(screen).all():
            raise ValueError(f'Non-finite standalone geometry: {item.item_id}')
        low, high = screen[:, :2].min(axis=0), screen[:, :2].max(axis=0)
        size = high - low
        if np.any(size <= 0):
            raise ValueError(f'Empty standalone geometry: {item.item_id}')
        scale = float(min(request.width / size[0], request.height / size[1]) * (1 - 2 * inset))
        centre = (low + high) / 2
        ndc_centre = np.array([centre[0] * 2 / request.width - 1,
                               1 - centre[1] * 2 / request.height])
        adjustment = np.eye(4)
        adjustment[0, 0] = adjustment[1, 1] = scale
        adjustment[3, :2] = -ndc_centre * scale
        camera.projection = camera.projection @ adjustment
        result = render(scene, camera, self.resolver, request.supersample,
                        self.session.shading if request.shading else None)
        if not np.any(result.rgba[:, :, 3]):
            raise ValueError(f'Standalone preview is transparent: {item.item_id}')
        if result.missing_textures or result.missing_bones:
            raise ValueError(f'Incomplete standalone preview: {item.item_id}: '
                             f'{result.missing_textures}, {result.missing_bones}')
        return PreviewResult(result.rgba, {
            'camera': {'preset': 'demo-standalone-fit', 'record_item': item.item_id,
                       'quaternion': list(camera.record.quaternion),
                       'user_quaternion': list(quaternion),
                       'projection_scale': scale, 'source_pixel_bounds': [low.tolist(), high.tolist()],
                       'preview_inset': inset},
            'composition': asdict(composition),
            'sources': [asdict(self.session.index.provenance(path)) for path in scene.sources],
            'warnings': list(item.warnings) + composition.notes
                        + ['Standalone fit is demo framing; native standalone camera is not verified.'],
        })
