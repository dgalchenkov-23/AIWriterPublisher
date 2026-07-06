import type { CharacterGenerationRequest } from '../types/characterGeneration';

const API_BASE = import.meta.env.VITE_API_BASE_URL ?? 'http://127.0.0.1:5057';

function normalizeImage(imageUrl: string): string {
  const v = (imageUrl ?? '').trim();
  if (!v) return '';
  if (v.startsWith('data:image/')) return v;
  if (v.startsWith('http://') || v.startsWith('https://')) return v;
  return `data:image/png;base64,${v}`;
}

export type HeroPayload = CharacterGenerationRequest;
export interface HeroResponse {
  imageUrl: string;
  generatedPrompt: string;
  agentReview: string;
}

export async function heroGenerate(
  payload: HeroPayload,
): Promise<HeroResponse> {
  const body: Record<string, unknown> = { ...payload };

  const res = await fetch(`${API_BASE}/api/hero-forge/generate`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });

  const data = (await res.json().catch(() => ({}))) as Record<string, unknown>;
  if (!res.ok) {
    const errText =
      (typeof data.error === 'string' && data.error) ||
      (typeof data.message === 'string' && data.message) ||
      `HeroForge error (${res.status})`;
    throw new Error(errText);
  }

  const rawImage = (data.imageUrl as string) || (data.ImageUrl as string) || '';
  const imageUrl = normalizeImage(rawImage);
  if (!imageUrl) throw new Error('HeroForge did not return an image URL');

  const generatedPrompt = (data.generatedPrompt as string) || (data.GeneratedPrompt as string) || '';
  const agentReview = (data.agentReview as string) || (data.AgentReview as string) || '';

  return { imageUrl, generatedPrompt, agentReview };
}

export default heroGenerate;
