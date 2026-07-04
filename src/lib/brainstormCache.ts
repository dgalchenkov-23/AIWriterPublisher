import type { ArtConcept } from '../types/artConcept';

export const BRAINSTORM_CACHE_KEY = 'cover_studio_brainstorm_cache';

export interface BrainstormCache {
  genre: string;
  description: string;
  concepts: ArtConcept[];
  activeConceptIndex: number | null;
  cachedAt: string;
}

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

  const review = typeof item.review === 'string' ? item.review : '';

  if (!title && !description && !rawPrompt) return null;

  return {
    id: typeof item.id === 'number' ? item.id : index + 1,
    title,
    description,
    visualElements,
    rawPrompt,
    aspectRatio,
    review,
  };
}

export function loadBrainstormCache(): BrainstormCache | null {
  try {
    const raw = localStorage.getItem(BRAINSTORM_CACHE_KEY);
    if (!raw) return null;

    const parsed = JSON.parse(raw) as Partial<BrainstormCache>;
    if (!parsed || typeof parsed !== 'object') return null;

    const concepts = Array.isArray(parsed.concepts)
      ? parsed.concepts
          .map((item, index) => normalizeConcept(item, index))
          .filter((item): item is ArtConcept => item !== null)
      : [];

    return {
      genre: typeof parsed.genre === 'string' ? parsed.genre : '',
      description: typeof parsed.description === 'string' ? parsed.description : '',
      concepts,
      activeConceptIndex:
        typeof parsed.activeConceptIndex === 'number' ? parsed.activeConceptIndex : null,
      cachedAt: typeof parsed.cachedAt === 'string' ? parsed.cachedAt : '',
    };
  } catch {
    return null;
  }
}

export function saveBrainstormCache(cache: BrainstormCache): void {
  localStorage.setItem(
    BRAINSTORM_CACHE_KEY,
    JSON.stringify({
      ...cache,
      cachedAt: cache.cachedAt || new Date().toISOString(),
    }),
  );
}

export function clearBrainstormCache(): void {
  localStorage.removeItem(BRAINSTORM_CACHE_KEY);
}
