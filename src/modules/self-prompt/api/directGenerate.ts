import type { TechnicalSpec } from '../../../types/manifest';

const API_BASE = import.meta.env.VITE_API_BASE_URL ?? 'http://127.0.0.1:5057';

export type DirectPayload =
  | {
      mode: 'raw-parse';
      rawPrompt: string; // Мапим snake_case на camelCase для .NET контроллера
      renderSettings: unknown;
      analysisModel?: string;
      hasReference?: boolean;
    }
  | {
      mode: 'self-prompt';
      technicalSpec: unknown;
      renderSettings: unknown;
      analysisModel?: string;
      hasReference?: boolean;
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
    renderSettings: payload.renderSettings,
    analysisModel: payload.analysisModel,
    hasReference: payload.hasReference
  };

  // В зависимости от режима подкидываем нужные поля
  if (payload.mode === 'raw-parse') {
    body.rawPrompt = payload.rawPrompt;
  } else if (payload.mode === 'self-prompt') {
    body.technicalSpec = payload.technicalSpec;
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

