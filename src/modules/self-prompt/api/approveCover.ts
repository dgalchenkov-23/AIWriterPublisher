import type { GeneratedCover } from '../../../types/manifest';

const API_BASE = import.meta.env.VITE_API_BASE_URL ?? 'http://127.0.0.1:5057';

export async function approveCoverForExport(
  projectId: string,
  cover: GeneratedCover,
): Promise<void> {
  const response = await fetch(`${API_BASE}/api/CoverGenerator/approve`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      project_id: projectId,
      art_id: cover.art_id,
      url: cover.url,
      timestamp: cover.timestamp,
      status: 'approved_for_print',
    }),
  });

  if (!response.ok) {
    const text = await response.text().catch(() => '');
    throw new Error(text || `Ошибка сервера (${response.status})`);
  }
}
