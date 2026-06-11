import {
  CANVAS_PRESETS,
  FLUX_DEV_MODEL,
  LOCAL_MODEL,
  type AnalysisModel,
  type CanvasPresetId,
  type SelfPromptTzTab,
  type ProjectManifest,
  type RenderSettings,
  type SelfPromptState,
  type TechnicalSpec,
} from '../types/manifest';

export const STORAGE_KEY = 'ai-cover-studio:manifest';

const defaultTextLayer = {
  text: '',
  font: 'Inter',
  size: 48,
  x: 0,
  y: 0,
  color: '#f8fafc',
};

export function createDefaultManifest(): ProjectManifest {
  return {
    project_id: crypto.randomUUID(),
    user_id: 'local-user',
    current_mode: 'Brainstorm',
    style_presets: {
      aesthetic: '',
      color_palette: [],
      modifiers: '',
    },
    technical_spec: {
      subject: '',
      environment: '',
      style: '',
      composition: '',
      visual_reference: '',
      negative_constraints: '',
    },
    render_settings: {
      provider: 'local',
      model: LOCAL_MODEL,
      aspect_ratio: CANVAS_PRESETS['1024x1024'].aspect_ratio,
      dimensions: { ...CANVAS_PRESETS['1024x1024'].dimensions },
      canvas_preset: '1024x1024',
    },
    self_prompt: {
      analysis_model: 'gpt-oss-120b',
      reference_image_url: null,
      tz_tab: 'magic-flow',
      formData: { raw_prompt: '' },
    },
    typography_layers: {
      title: { ...defaultTextLayer, size: 64, y: 120 },
      author: { ...defaultTextLayer, size: 28, y: 200 },
    },
    generation_history: [],
  };
}

function ensureString(value: unknown, fallback = ''): string {
  if (typeof value === 'string') return value;
  if (value == null) return fallback;
  return fallback;
}

function sanitizeTechnicalSpec(raw: unknown, defaults: TechnicalSpec): TechnicalSpec {
  const record = raw && typeof raw === 'object' ? (raw as Record<string, unknown>) : {};
  return {
    subject: ensureString(record.subject, defaults.subject),
    environment: ensureString(record.environment, defaults.environment),
    style: ensureString(
      record.style ?? record.style ?? record.style_light,
      defaults.style,
    ),
    composition: ensureString(record.composition, defaults.composition),
    visual_reference: ensureString(record.visual_reference, defaults.visual_reference),
    negative_constraints: ensureString(record.negative_constraints, defaults.negative_constraints),
  };
}

function isCanvasPreset(value: unknown): value is CanvasPresetId {
  return value === '1024x1024' || value === '512x768';
}

function sanitizeRenderSettings(raw: unknown, defaults: RenderSettings): RenderSettings {
  const record = raw && typeof raw === 'object' ? (raw as Record<string, unknown>) : {};
  const dims =
    record.dimensions && typeof record.dimensions === 'object'
      ? (record.dimensions as Record<string, unknown>)
      : {};

  let canvasPreset = defaults.canvas_preset;
  if (isCanvasPreset(record.canvas_preset)) {
    canvasPreset = record.canvas_preset;
  } else {
    const width = typeof dims.width === 'number' ? dims.width : defaults.dimensions.width;
    const height = typeof dims.height === 'number' ? dims.height : defaults.dimensions.height;
    if (width === 512 && height === 768) canvasPreset = '512x768';
    else if (width === 1024 && height === 1024) canvasPreset = '1024x1024';
  }

  const preset = CANVAS_PRESETS[canvasPreset];

  return {
    provider: 'local',
    model: ensureString(record.model, defaults.model) || FLUX_DEV_MODEL,
    aspect_ratio: ensureString(record.aspect_ratio, preset.aspect_ratio),
    dimensions: { ...preset.dimensions },
    canvas_preset: canvasPreset,
  };
}

function isAnalysisModel(value: unknown): value is AnalysisModel {
  return value === 'groq-llama-3' || value === 'gemini-flash';
}

function isTzTab(value: unknown): value is SelfPromptTzTab {
  return value === 'magic-flow' || value === 'strict-layers';
}

function sanitizeSelfPrompt(raw: unknown, defaults: SelfPromptState): SelfPromptState {
  const record = raw && typeof raw === 'object' ? (raw as Record<string, unknown>) : {};
  const nested =
    record.self_prompt && typeof record.self_prompt === 'object'
      ? (record.self_prompt as Record<string, unknown>)
      : record;

  const analysisRaw = nested.analysis_model ?? record.analysis_model;
  const referenceRaw = nested.reference_image_url ?? record.reference_image_url;
  const tzTabRaw = nested.tz_tab ?? record.tz_tab;

  const formDataRaw = nested.formData ?? record.formData;
  const formRecord =
    formDataRaw && typeof formDataRaw === 'object'
      ? (formDataRaw as Record<string, unknown>)
      : {};
  const rawPrompt =
    ensureString(formRecord.raw_prompt, defaults.formData.raw_prompt) ||
    ensureString(nested.raw_prompt ?? record.raw_prompt, defaults.formData.raw_prompt);

  return {
    analysis_model: isAnalysisModel(analysisRaw) ? analysisRaw : defaults.analysis_model,
    reference_image_url:
      typeof referenceRaw === 'string' && referenceRaw.length > 0 ? referenceRaw : null,
    tz_tab: isTzTab(tzTabRaw) ? tzTabRaw : defaults.tz_tab,
    formData: { raw_prompt: rawPrompt },
  };
}

export function mergeManifest(
  defaults: ProjectManifest,
  parsed: Partial<ProjectManifest>,
): ProjectManifest {
  return {
    ...defaults,
    ...parsed,
    project_id: typeof parsed.project_id === 'string' ? parsed.project_id : defaults.project_id,
    user_id: typeof parsed.user_id === 'string' ? parsed.user_id : defaults.user_id,
    current_mode: parsed.current_mode ?? defaults.current_mode,
    style_presets: {
      ...defaults.style_presets,
      ...(parsed.style_presets ?? {}),
      color_palette: Array.isArray(parsed.style_presets?.color_palette)
        ? parsed.style_presets.color_palette.filter((c): c is string => typeof c === 'string')
        : defaults.style_presets.color_palette,
    },
    technical_spec: sanitizeTechnicalSpec(parsed.technical_spec, defaults.technical_spec),
    render_settings: sanitizeRenderSettings(parsed.render_settings, defaults.render_settings),
    self_prompt: sanitizeSelfPrompt(parsed.self_prompt, defaults.self_prompt),
    typography_layers: {
      title: { ...defaults.typography_layers.title, ...(parsed.typography_layers?.title ?? {}) },
      author: { ...defaults.typography_layers.author, ...(parsed.typography_layers?.author ?? {}) },
    },
    generation_history: Array.isArray(parsed.generation_history)
      ? parsed.generation_history
      : defaults.generation_history,
  };
}

export function loadManifestFromStorage(): ProjectManifest {
  const defaults = createDefaultManifest();
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return defaults;
    const parsed = JSON.parse(raw) as Partial<ProjectManifest>;
    return mergeManifest(defaults, parsed);
  } catch {
    return defaults;
  }
}

export function persistManifest(manifest: ProjectManifest): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(manifest));
  } catch(ex) {
    // Игнорируем ошибки, например, если пользователь отключил localStorage
  }
}
