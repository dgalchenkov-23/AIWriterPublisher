import type { TechnicalSpec } from '../../../types/manifest';

const API_BASE = import.meta.env.VITE_API_BASE_URL ?? 'http://127.0.0.1:5057';

export type DirectPayload =
  | {
      mode: 'raw-parse';
      raw_prompt: string; // Мапим snake_case на camelCase для .NET контроллера
      render_settings: RenderSettings;
      analysis_model?: AnalysisModel;
      has_reference?: boolean;
    }
  | {
      mode: 'self-prompt';
      technical_spec: TechnicalSpec;
      render_settings: RenderSettings;
      analysis_model?: AnalysisModel;
      has_reference?: boolean;
    };

function normalizeImage(imageBase64: string): string {
  const trimmed = (imageBase64 ?? '').trim();
  if (!trimmed) return '';
  if (trimmed.startsWith('data:image/')) return trimmed;
  if (trimmed.startsWith('http://') || trimmed.startsWith('https://')) return trimmed;
  
  // Если бэкенд вернул сырой base64 без префикса
  return `data:image/png;base64,${trimmed}`;
}

/**
 * Отправляет запрос в пайплайн Direct-генерации (Режим «Творец»).
 * За один проход делает и ИИ-анализ (для raw-parse через gpt-oss-120b), и рендер обложки.
 */
export async function sendFlowToPipeline(
  payload: DirectPayload,
  referenceImageUrl: string | null,
): Promise<{ imageUrl: string; parsedSpec: Partial<TechnicalSpec> | null }> {
  
  // Собираем тело запроса строго по контракту DirectGenerationRequest в .NET
  const body: Record<string, unknown> = {
    mode: payload.mode,
    render_settings: payload.render_settings,
    analysis_model: payload.analysis_model,
    has_reference: payload.has_reference
  };

  // В зависимости от режима подкидываем нужные поля
  if (payload.mode === 'raw-parse') {
    body.raw_prompt = payload.raw_prompt;
  } else if (payload.mode === 'self-prompt') {
    body.technical_spec = payload.technical_spec;
  }

  if (referenceImageUrl) {
    body.referenceImage = referenceImageUrl;
  }

  const response = await fetch(`${API_BASE}/api/CoverGenerator/direct-generate?provider=local`, {
    method: 'POST',
    headers: { 
      'Content-Type': 'application/json' 
    },
    body: JSON.stringify(body),
  });

  const data = (await response.json().catch(() => ({}))) as Record<string, unknown>;
  
  if (!response.ok) {
    const errText =
      typeof data.error === 'string'
        ? data.error
        : typeof data.message === 'string'
          ? data.message
          : `Ошибка сервера (${response.status})`;
    throw new Error(errText);
  }

  // Достаем картинку (проверяем оба кейса именования полей)
  const imageBase64 =
    (typeof data.image_base64 === 'string' && data.image_base64) ||
    (typeof data.imageBase64 === 'string' && data.imageBase64) ||
    '';

  // Достаем распарсенную 120B моделью спецификацию
  const parsed = (data.parsed_spec ?? data.parsedSpec) as unknown;
  const parsedSpec =
    parsed && typeof parsed === 'object' ? (parsed as Partial<TechnicalSpec>) : null;

  const imageUrl = normalizeImage(imageBase64);
  if (!imageUrl) throw new Error('Бэкенд не вернул валидный image_base64');

  return { imageUrl, parsedSpec };
}
export async function sendImageToEdit(
  payload: DirectPayload,
  referenceImageBase64: string | null, // Передаем именно Base64 строку
): Promise<{ imageUrl: string; parsedSpec: Partial<TechnicalSpec> | null }> {  
  
  // Формируем объект строго по контракту DirectGenerationRequest.cs
  const body: Record<string, unknown> = {
    // Поля должны соответствовать C# DTO (с учетом JsonPropertyName)
    mode: payload.mode,
    analysis_model: payload.analysis_model || "nex-agi/nex-n2-pro:free",
    denoising_strength: payload.render_settings?.denoising_strength || 0.45,
    
    // В бэкенде это свойство называется BaseImageBase64
    base_image_base64: referenceImageBase64 
  };

  // Распределяем логику в зависимости от режима (как в контроллере)
  if (payload.mode === 'raw-parse') {
    // "Магический поток" уходит в RawPrompt
    body.raw_prompt = payload.raw_prompt;
  } else {
    // "Строгие слои" уходят в TechnicalSpec
    body.technical_spec = payload.technical_spec;
  }

  const response = await fetch(`${API_BASE}/api/CoverGenerator/edit-reference?provider=local`, {
    method: 'POST',
    headers: { 
      'Content-Type': 'application/json' 
    },
    body: JSON.stringify(body),
  });

  const data = (await response.json().catch(() => ({}))) as Record<string, unknown>;
  
  if (!response.ok) {
    throw new Error(data.error || data.message || `Ошибка сервера (${response.status})`);
  }

  // Бэкенд возвращает DirectGenerationResponse { ImageBase64, ParsedSpec }
  // Используем camelCase, так как ASP.NET Core обычно сериализует так по умолчанию
  const imageBase64 = data.imageBase64 as string || data.image_base64 as string || '';
  const parsedSpec = (data.parsedSpec ?? data.parsed_spec) as Partial<TechnicalSpec> | null;

  // Помним про префикс data:image/png;base64, для отображения на Canvas
  const imageUrl = imageBase64.startsWith('data:') 
    ? imageBase64 
    : `data:image/png;base64,${imageBase64}`;

  if (!imageBase64) throw new Error('Бэкенд не вернул изображение');

  return { imageUrl, parsedSpec };
}

export async function directGenerate(
  payload: DirectPayload,
  referenceImageUrl: string | null,
): Promise<{ imageUrl: string; parsedSpec: Partial<TechnicalSpec> | null }> {
  const body: Record<string, unknown> = { ...payload };
  if (referenceImageUrl) body.reference_image = referenceImageUrl;

  const response = await fetch(`${API_BASE}/api/CoverGenerator/direct-generate`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });

  const data = (await response.json().catch(() => ({}))) as Record<string, unknown>;
  if (!response.ok) {
    const errText =
      typeof data.error === 'string'
        ? data.error
        : typeof data.message === 'string'
          ? data.message
          : `Ошибка сервера (${response.status})`;
    throw new Error(errText);
  }

  const imageBase64 =
    (typeof data.image_base64 === 'string' && data.image_base64) ||
    (typeof data.imageBase64 === 'string' && data.imageBase64) ||
    '';

  const parsed = (data.parsed_spec ?? data.parsedSpec) as unknown;
  const parsedSpec =
    parsed && typeof parsed === 'object' ? (parsed as Partial<TechnicalSpec>) : null;

  const imageUrl = normalizeImage(imageBase64);
  if (!imageUrl) throw new Error('Бэкенд не вернул image_base64');

  return { imageUrl, parsedSpec };
}
