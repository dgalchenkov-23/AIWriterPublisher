import { CANVAS_PRESETS, type CanvasPresetId, type RenderSettings } from '../types/manifest';

/** Применяет aspectRatio из концепта к render_settings манифеста. */
export function applyAspectRatioToRenderSettings(
  aspectRatio: string,
  settings: RenderSettings,
): RenderSettings {
  const ratio = aspectRatio.trim() || '2:3';

  if (ratio === '1:1') {
    const preset = CANVAS_PRESETS['1024x1024'];
    return {
      ...settings,
      canvas_preset: '1024x1024',
      aspect_ratio: preset.aspect_ratio,
      dimensions: { ...preset.dimensions },
    };
  }

  if (ratio === '2:3' || ratio === '3:4') {
    const preset = CANVAS_PRESETS['512x768'];
    return {
      ...settings,
      canvas_preset: '512x768',
      aspect_ratio: ratio,
      dimensions: { ...preset.dimensions },
    };
  }

  // Для 3:2, 16:9 и прочих — сохраняем ratio, оставляем текущий пресет
  return { ...settings, aspect_ratio: ratio };
}

export function canvasPresetFromAspectRatio(aspectRatio: string): CanvasPresetId {
  const ratio = aspectRatio.trim();
  if (ratio === '1:1') return '1024x1024';
  if (ratio === '2:3' || ratio === '3:4') return '512x768';
  return '512x768';
}
