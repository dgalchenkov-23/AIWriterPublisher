export type AppMode = 'Brainstorm' | 'Self-Prompt' | 'SceneMaker' | 'Typography' | 'HeroesForge';

export type AnalysisModel = 'gpt-oss-120b' | 'gemini-flash' | 'ollama-qwen-2-7b';

export type SelfPromptTzTab = 'magic-flow' | 'strict-layers';

export type CanvasPresetId = '1024x1024' | '512x768';

export interface StylePresets {
  aesthetic: string;
  color_palette: string[];
  modifiers: string;
}

export interface TechnicalSpec {
  subject: string;
  environment: string;
  style: string;
  composition: string;
  visual_reference: string;
  negative_constraints: string;
}

export interface RenderSettings {
  provider: 'local' | 'huggingface' |  'pollination';
  model: string;
  aspect_ratio: string;
  dimensions: { width: number; height: number };
  canvas_preset: CanvasPresetId;
}

export interface SelfPromptFormData {
  raw_prompt: string;
  uncensored_raw_prompt: string;
}

export interface SelfPromptState {
  analysis_model: AnalysisModel;
  reference_image_url: string | null;
  tz_tab: SelfPromptTzTab;
  formData: SelfPromptFormData;
}

export interface TextLayer {
  text: string;
  font: string;
  size: number;
  x: number;
  y: number;
  color: string;
}

export interface TypographyLayers {
  title: TextLayer;
  author: TextLayer;
}

export interface GeneratedCover {
  art_id: string;
  url: string;
  timestamp: string;
  technical_prompt_used?: string;
}

export interface GeneratedFace {
  face_id: string;
  url: string;
  timestamp: string;
  comfy_prompt: string;       // Сюда маппим result.generatedPrompt от бэка
  user_description: string;   // Сюда маппим исходный payload.UserDescription
  agent_review: string;       // Сюда маппим отзыв Миры (result.agentReview)
}

export interface ProjectManifest {
  project_id: string;
  user_id: string;
  current_mode: AppMode;
  style_presets: StylePresets;
  technical_spec: TechnicalSpec;
  render_settings: RenderSettings;
  self_prompt: SelfPromptState;
  typography_layers: TypographyLayers;
  generation_history: GeneratedCover[];
}

export const FLUX_DEV_MODEL = 'black-forest-labs/FLUX.1-dev';
export const LOCAL_MODEL = 'EventHorizon';

export const CANVAS_PRESETS: Record<
  CanvasPresetId,
  { label: string; dimensions: { width: number; height: number }; aspect_ratio: string }
> = {
  '1024x1024': {
    label: '1024×1024',
    dimensions: { width: 1024, height: 1024 },
    aspect_ratio: '1:1',
  },
  '512x768': {
    label: '512×768 (книжная обложка)',
    dimensions: { width: 512, height: 768 },
    aspect_ratio: '2:3',
  },
};
