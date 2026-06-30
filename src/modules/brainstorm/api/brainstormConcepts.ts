import type { ArtConcept, BookDescriptionRequest } from '../../../types/artConcept';

const API_BASE = import.meta.env.VITE_API_BASE_URL ?? 'http://127.0.0.1:5000';

function normalizeConcept(raw: unknown, index: number): ArtConcept | null {
  if (!raw || typeof raw !== 'object') return null;
  const item = raw as Record<string, unknown>;
  const title = typeof item.title === 'string' ? item.title : '';
  const description = typeof item.description === 'string' ? item.description : '';
  const visualElements =
    typeof item.visualElements === 'string'
      ? item.visualElements
      : typeof item.visual_elements === 'string'
        ? item.visual_elements
        : '';

  const rawPrompt =
    typeof item.rawPrompt === 'string'
      ? item.rawPrompt
      : typeof item.raw_prompt === 'string'
        ? item.raw_prompt
        : '';

  const aspectRatio =
    typeof item.aspectRatio === 'string'
      ? item.aspectRatio
      : typeof item.aspect_ratio === 'string'
        ? item.aspect_ratio
        : '2:3';

  if (!title && !description) return null;

  const id = typeof item.id === 'number' ? item.id : index + 1;
  return { id, title, description, visualElements, rawPrompt, aspectRatio };
}

export async function brainstormConcepts(
  request: BookDescriptionRequest,
): Promise<ArtConcept[]> {
  const response = await fetch(`${API_BASE}/api/CoverGenerator/brainstorm`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      genre: request.genre,
      description: request.description,
    }),
  });

  const text = await response.text();
  if (!response.ok) {
    let message = `Ошибка сервера (${response.status})`;
    try {
      const err = JSON.parse(text) as { error?: string };
      if (err.error) message = err.error;
    } catch {
      if (text) message = text;
    }
    throw new Error(message);
  }

  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch {
    throw new Error('Бэкенд вернул некорректный JSON');
  }

  const list = Array.isArray(parsed) ? parsed : [];
  const concepts = list
    .map((item, index) => normalizeConcept(item, index))
    .filter((item): item is ArtConcept => item !== null);

  return concepts.slice(0, 3);
}
