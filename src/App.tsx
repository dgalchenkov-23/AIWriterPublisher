import { useCallback, useEffect, useState } from 'react';
import Sidebar from './components/Sidebar';
import { loadManifestFromStorage, persistManifest } from './lib/manifestStorage';
import { applyAspectRatioToRenderSettings } from './lib/applyConceptToSelfPrompt';
import BrainstormPanel from './modules/brainstorm/components/BrainstormPanel';
import SelfPromptForm from './modules/self-prompt/components/SelfPromptForm';
import HeroForgeComponent from './modules/self-prompt/components/HeroForgeComponent';
import type { ArtConcept } from './types/artConcept';
import type {
  AppMode,
  GeneratedCover,
  ProjectManifest,
  RenderSettings,
  SelfPromptState,
  TechnicalSpec,
} from './types/manifest';

const modeLabels: Record<AppMode, string> = {
  Brainstorm: 'Гримуар идей',
  'Self-Prompt': 'Direct Prompt',
  SceneMaker: 'Scene Maker',
  Typography: 'Типографика',
  HeroesForge: 'Кузница Героев',
};

export default function App() {
  const [manifest, setManifest] = useState<ProjectManifest>(loadManifestFromStorage);

  useEffect(() => {
    persistManifest(manifest);
  }, [manifest]);

  const handleModeChange = useCallback((mode: AppMode) => {
    setManifest((prev) => ({ ...prev, current_mode: mode }));
  }, []);

  const handleTechnicalSpecChange = useCallback((updatedSpec: TechnicalSpec) => {
    setManifest((prev) => ({ ...prev, technical_spec: updatedSpec }));
  }, []);

  const handleRenderSettingsChange = useCallback((settings: RenderSettings) => {
    setManifest((prev) => ({ ...prev, render_settings: settings }));
  }, []);

  const handleSelfPromptChange = useCallback((selfPrompt: SelfPromptState) => {
    setManifest((prev) => ({ ...prev, self_prompt: selfPrompt }));
  }, []);

  const handleTransferConceptToForge = useCallback((concept: ArtConcept) => {
    setManifest((prev) => ({
      ...prev,
      current_mode: 'Self-Prompt',
      self_prompt: {
        ...prev.self_prompt,
        tz_tab: 'magic-flow',
        formData: {
          ...prev.self_prompt.formData,
          raw_prompt: concept.rawPrompt,
        },
      },
      render_settings: applyAspectRatioToRenderSettings(
        concept.aspectRatio,
        prev.render_settings,
      ),
      style_presets: {
        ...prev.style_presets,
        aesthetic: concept.title,
      },
    }));
  }, []);

  const addGeneration = useCallback((cover: GeneratedCover) => {
    setManifest((prev) => ({
      ...prev,
      generation_history: [cover, ...prev.generation_history],
    }));
  }, []);

  const handleDemoGeneration = () => {
    const cover: GeneratedCover = {
      art_id: crypto.randomUUID(),
      url: `https://picsum.photos/seed/${Date.now()}/400/600`,
      timestamp: new Date().toISOString(),
      technical_prompt_used: manifest.technical_spec.subject || undefined,
    };
    addGeneration(cover);
  };

  const { current_mode, generation_history } = manifest;
  const isSelfPrompt = current_mode === 'Self-Prompt';
  const isBrainstorm = current_mode === 'Brainstorm';
  const isHeroesForge = current_mode === 'HeroesForge';

  return (
    <div className="flex h-full min-h-screen grimoire-shell">
      <Sidebar activeMode={current_mode} onModeChange={handleModeChange} />

      <main className="flex-1 flex flex-col min-w-0">
        <header className="border-b border-violet-900/30 px-8 py-5 flex items-center justify-between gap-4 bg-black/20 backdrop-blur-sm">
          <div>
            <h1 className="grimoire-display text-2xl font-semibold text-violet-100">
              {modeLabels[current_mode]}
            </h1>
            <p className="text-xs text-slate-500 mt-1 font-mono truncate">
              project: {manifest.project_id.slice(0, 8)}…
            </p>
          </div>
          {!isSelfPrompt && !isBrainstorm && !isHeroesForge && (
            <button
              type="button"
              onClick={handleDemoGeneration}
              className="px-4 py-2 rounded-lg bg-indigo-600 hover:bg-indigo-500 text-sm font-semibold transition-colors shrink-0"
            >
              + Тестовая генерация
            </button>
          )}
        </header>

        <section className="flex-1 p-6 md:p-8 overflow-auto">
          {isBrainstorm ? (
            <BrainstormPanel onTransferToForge={handleTransferConceptToForge} />
          ) : isSelfPrompt ? (
            <SelfPromptForm
              projectId={manifest.project_id}
              technicalSpec={manifest.technical_spec}
              renderSettings={manifest.render_settings}
              selfPrompt={manifest.self_prompt}
              generationHistory={generation_history}
              onAddGeneration={addGeneration}
              onTechnicalSpecChange={handleTechnicalSpecChange}
              onRenderSettingsChange={handleRenderSettingsChange}
              onSelfPromptChange={handleSelfPromptChange}
            />
          ) : isHeroesForge ? (
            <HeroForgeComponent
              selfPrompt={manifest.self_prompt}
              technicalSpec={manifest.technical_spec}
              renderSettings={manifest.render_settings}
              onAddGeneration={addGeneration}
            />
          ) : (
            <div className="max-w-3xl">
              <p className="text-slate-400 text-sm mb-6">
                Рабочая область модуля{' '}
                <span className="text-indigo-300 font-medium">{current_mode}</span>. Компоненты
                подключатся из{' '}
                <code className="text-xs bg-slate-800 px-1.5 py-0.5 rounded">src/modules/…</code>.
              </p>

              <div className="rounded-xl border border-slate-800 bg-slate-950/60 p-5">
                <h2 className="text-sm font-bold text-slate-300 mb-3">
                  История генераций ({generation_history.length})
                </h2>
                {generation_history.length === 0 ? (
                  <p className="text-sm text-slate-600">
                    Пока нет обложек. Нажмите «Тестовая генерация».
                  </p>
                ) : (
                  <ul className="flex flex-col gap-2">
                    {generation_history.map((item) => (
                      <li
                        key={item.art_id}
                        className="flex items-center gap-3 text-xs border border-slate-800 rounded-lg p-2 bg-slate-900/50"
                      >
                        <img
                          src={item.url}
                          alt=""
                          className="w-10 h-14 object-cover rounded border border-slate-700"
                        />
                        <div className="min-w-0">
                          <p className="font-mono text-slate-400 truncate">{item.art_id}</p>
                          <p className="text-slate-600">
                            {new Date(item.timestamp).toLocaleString('ru-RU')}
                          </p>
                        </div>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            </div>
          )}
        </section>
      </main>
    </div>
  );
}
