import { useEffect, useState } from 'react';
import type { GeneratedCover } from '../../../types/manifest';
import { heroGenerate } from '../api/heroGenerate';

interface HeroForgeProps {
  selfPrompt: any;
  onAddGeneration: (cover: GeneratedCover) => void;
}

const HeroForgeComponent: React.FC<HeroForgeProps> = ({ selfPrompt, onAddGeneration }) => {
  const [faceGallery, setFaceGallery] = useState<GeneratedCover[]>([]);
  const [bodyGallery, setBodyGallery] = useState<GeneratedCover[]>([]);
  const [selectedFaceUrl, setSelectedFaceUrl] = useState<string | null>(null);
  const [lightboxImage, setLightboxImage] = useState<string | null>(null);
  const [generatingFace, setGeneratingFace] = useState(false);
  const [generatingBody, setGeneratingBody] = useState(false);
  const [miraLog, setMiraLog] = useState<string>('Мира готова ковать портреты в строгом стиле.');
  const [karaLog, setKaraLog] = useState<string>('Кара ожидает активного лица для съемки в полный рост.');
  const [promptText, setPromptText] = useState<string>(selfPrompt.formData.raw_prompt || '');

  useEffect(() => {
    setPromptText(selfPrompt.formData.raw_prompt || '');
  }, [selfPrompt.formData.raw_prompt]);

  const updatePromptText = (value: string) => {
    setPromptText(value);
    selfPrompt.formData.raw_prompt = value;
  };

  const generateFace = async () => {
    if (generatingFace || !promptText.trim()) return;
    setGeneratingFace(true);
    setMiraLog('Мира выковывает черты лица и заглядывает в архив.');

    try {
      const payload = {
        UserDescription: promptText,
        IsFullBody: false,
        FaceReferenceUrl: null,
      };

      const result: any = await heroGenerate(payload as any);
      const newCover: GeneratedCover = {
        art_id: crypto.randomUUID(),
        url: result.imageUrl,
        timestamp: new Date().toISOString(),
        technical_prompt_used: payload.UserDescription,
      };

      setFaceGallery((prev) => [newCover, ...prev]);
      setMiraLog('Новый лик добавлен в галерею. Выбери его для следующего шага.');
      onAddGeneration(newCover);
    } catch (error) {
      console.error(error);
      setMiraLog('Не удалось создать лицо. Проверь запрос и попробуй снова.');
    } finally {
      setGeneratingFace(false);
    }
  };

  const handleSelectFace = (url: string) => {
    setSelectedFaceUrl(url);
    setLightboxImage(null);
    setKaraLog('Выбранный лик активирован. Кара готова к FaceSwap.');

    setTimeout(() => {
      const bodyBlock = document.getElementById('kara-body-block');
      bodyBlock?.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }, 120);
  };

  const generateFullBody = async () => {
    if (generatingBody || !selectedFaceUrl || !promptText.trim()) return;
    setGeneratingBody(true);
    setKaraLog('Кара настраивает кадр и переносит лицо в полный рост.');

    try {
      const payload = {
        UserDescription: promptText,
        IsFullBody: true,
        FaceReferenceUrl: selectedFaceUrl,
      };

      const result: any = await heroGenerate(payload as any);
      const newCover: GeneratedCover = {
        art_id: crypto.randomUUID(),
        url: result.imageUrl,
        timestamp: new Date().toISOString(),
        technical_prompt_used: payload.UserDescription,
      };

      setBodyGallery((prev) => [newCover, ...prev]);
      setKaraLog('Ростовой снимок готов. Он добавлен в нижнюю ленту.');
      onAddGeneration(newCover);
    } catch (error) {
      console.error(error);
      setKaraLog('Ошибка генерации. Попробуй скорректировать prompt или выбрать другое лицо.');
    } finally {
      setGeneratingBody(false);
    }
  };

  const lightboxIsFace = lightboxImage ? faceGallery.some((cover) => cover.url === lightboxImage) : false;
  const lightboxIsBody = lightboxImage ? bodyGallery.some((cover) => cover.url === lightboxImage) : false;

  return (
      <div className="hero-forge-layout w-full flex flex-col gap-8 p-6 bg-slate-950/90 border border-slate-800/80 rounded-[32px] text-slate-100 shadow-[0_24px_70px_-40px_rgba(15,23,42,0.85)]">
      <div className="forge-block overflow-hidden rounded-[28px] border border-slate-800/80 bg-slate-950 shadow-[0_24px_60px_-36px_rgba(15,23,42,0.8)]">
        <div className="flex flex-col gap-3 px-6 py-5 border-b border-slate-800 bg-slate-900/80">
          <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.32em] text-indigo-400/80">Этап 1: Кузница лиц</p>
              <h2 className="mt-2 text-lg font-semibold text-slate-100">Формируй промпт и собирай галерею лиц</h2>
            </div>
            <span className="text-[11px] text-slate-500">Лиц в галерее: {faceGallery.length}</span>
          </div>
        </div>

        <div className="grid gap-6 lg:grid-cols-[1.1fr_0.9fr] p-6">
          <div className="flex flex-col gap-4 rounded-3xl border border-slate-800 bg-slate-900/80 p-4 shadow-inner">
            <div className="flex items-center justify-between gap-3">
              <label className="text-xs font-semibold uppercase tracking-[0.28em] text-slate-500">Промпт</label>
              <span className="text-[11px] text-slate-500">Более подробный ввод — выше шанс точности</span>
            </div>
            <textarea
              className="min-h-[210px] w-full rounded-2xl border border-slate-700/80 bg-slate-950/80 px-4 py-4 text-sm leading-6 text-slate-100 placeholder:text-slate-600 shadow-inner transition-all duration-200 focus:outline-none focus:border-indigo-500/60 focus:ring-2 focus:ring-indigo-500/25 resize-none"
              value={promptText}
              onChange={(e) => updatePromptText(e.target.value)}
              placeholder="Опиши внешность: 'Суровый дворф с рыжей бородой, шрам на щеке, броня из темных металлов...'"
            />
            <div className="rounded-3xl border border-slate-800 bg-slate-950/80 p-3 text-xs font-mono leading-6 text-slate-300">
              {miraLog}
            </div>
          </div>

          <div className="flex flex-col gap-4 rounded-3xl border border-slate-800 bg-slate-900/80 p-4 shadow-inner">
            <div className="flex items-center justify-between gap-3">
              <label className="text-xs font-semibold uppercase tracking-[0.28em] text-slate-500">Лента лиц</label>
              <span className="text-[11px] text-slate-500">Клик — открыть полноразмер</span>
            </div>
            <div className="flex-1 rounded-3xl border border-slate-800 bg-slate-950/80 p-3 overflow-y-auto max-h-[304px] custom-scrollbar">
              {faceGallery.length === 0 ? (
                <div className="flex h-full min-h-[220px] flex-col items-center justify-center gap-3 text-slate-500">
                  <span className="text-3xl">🎭</span>
                  <p className="text-sm">Галерея пустая. Сгенерируй первый лик.</p>
                </div>
              ) : (
                <div className="grid grid-cols-3 gap-3">
                  {faceGallery.map((face) => {
                    const isActive = selectedFaceUrl === face.url;
                    return (
                      <button
                        key={face.art_id}
                        type="button"
                        onClick={() => setLightboxImage(face.url)}
                        className={`group relative aspect-[4/3] overflow-hidden rounded-2xl border transition duration-200 ${
                          isActive
                            ? 'border-indigo-400/60 bg-slate-900/90 shadow-[0_18px_48px_-26px_rgba(148,163,184,0.35)]'
                            : 'border-slate-700/80 bg-slate-950/90 hover:border-slate-500 hover:scale-[1.02]'
                        }`}
                      >
                        <img src={face.url} alt="Лицо" className="h-full w-full object-cover" />
                        <div className="pointer-events-none absolute inset-0 bg-gradient-to-t from-slate-950/60 via-transparent opacity-0 transition-opacity duration-200 group-hover:opacity-100" />
                        <span className="pointer-events-none absolute bottom-3 left-3 rounded-full border border-slate-700 bg-slate-900/90 px-2 py-1 text-[10px] uppercase tracking-[0.24em] text-slate-300">
                          Просмотр
                        </span>
                        {isActive && (
                          <span className="absolute top-3 right-3 rounded-full bg-slate-950/95 px-2 py-1 text-[9px] uppercase tracking-[0.24em] text-slate-300 border border-slate-700">
                            Выбрано
                          </span>
                        )}
                      </button>
                    );
                  })}
                </div>
              )}
            </div>
          </div>
        </div>

        <div className="border-t border-slate-800 bg-slate-900/80 px-6 py-4 flex justify-center">
          <button
            onClick={generateFace}
            disabled={generatingFace || !promptText.trim()}
            className="w-full max-w-sm rounded-2xl border border-slate-700/80 bg-slate-900/80 px-6 py-3 text-sm font-semibold uppercase tracking-[0.18em] text-slate-100 transition-all duration-200 hover:border-slate-500 hover:bg-slate-800 disabled:cursor-not-allowed disabled:border-slate-700/50 disabled:bg-slate-900/60"
          >
            {generatingFace ? 'Генерация лица...' : 'Создать лицо'}
          </button>
        </div>
      </div>

      <div
        id="kara-body-block"
        className={`forge-block overflow-hidden rounded-[28px] border bg-slate-950 shadow-[0_28px_80px_-48px_rgba(15,23,42,0.95)] transition-all duration-300 ${
          selectedFaceUrl ? 'border-slate-800 opacity-100' : 'border-slate-700/70 opacity-80'
        }`}>
        <div className="flex flex-col gap-3 px-6 py-5 border-b border-slate-800 bg-slate-900/80">
          <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.32em] text-slate-500">Этап 2: Полный рост</p>
              <h2 className="mt-2 text-lg font-semibold text-slate-100">Собери корпус на основе выбранного лица</h2>
            </div>
            <span className="text-[11px] text-slate-500">Готовых персонажей: {bodyGallery.length}</span>
          </div>
        </div>

        <div className="grid gap-6 lg:grid-cols-[0.94fr_1.06fr] p-6">
          <div className="flex flex-col gap-5 rounded-3xl border border-slate-700/80 bg-slate-950/80 p-4 shadow-inner">
            <div className="flex flex-col items-center gap-4 rounded-3xl border border-slate-700/80 bg-slate-950/90 p-4 text-center">
              <span className="text-2xl">💬</span>
              <p className="text-xs font-semibold uppercase tracking-[0.28em] text-indigo-400/80">Диалог Агентов</p>
              <div className="w-full rounded-3xl border border-slate-700/80 bg-slate-900/80 p-4 text-xs font-mono leading-6 text-slate-300 shadow-inner">
                {karaLog}
              </div>
            </div>

            <div className="rounded-3xl border border-slate-800 bg-slate-950/90 p-4 text-center">
              <p className="text-[10px] font-semibold uppercase tracking-[0.28em] text-slate-500 mb-3">Текущий референс</p>
              <div className="mx-auto flex h-24 w-24 items-center justify-center overflow-hidden rounded-3xl border border-slate-700/80 bg-slate-900/80 shadow-inner">
                {selectedFaceUrl ? (
                  <img src={selectedFaceUrl} alt="Выбранный лик" className="h-full w-full object-cover" />
                ) : (
                  <span className="text-xl text-slate-500">—</span>
                )}
              </div>
              <p className="mt-3 text-[11px] text-slate-500">Выбери лицо в верхней галерее для запуска Кара.</p>
            </div>
          </div>

          <div className="flex flex-col gap-4 rounded-3xl border border-slate-800 bg-slate-900/80 p-4 shadow-inner">
            <div className="flex items-center justify-between gap-3">
              <label className="text-xs font-semibold uppercase tracking-[0.28em] text-slate-500">Лента готовых персонажей</label>
              <span className="text-[11px] text-slate-500">Клик — просмотр</span>
            </div>
            <div className="flex-1 rounded-3xl border border-slate-800 bg-slate-950/80 p-3 overflow-y-auto max-h-[304px] custom-scrollbar">
              {bodyGallery.length === 0 ? (
                <div className="flex h-full min-h-[220px] flex-col items-center justify-center gap-3 text-slate-500">
                  <span className="text-3xl">🧍</span>
                  <p className="text-sm">Ни одного полного тела пока нет.</p>
                </div>
              ) : (
                <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
                  {bodyGallery.map((body) => (
                    <button
                      key={body.art_id}
                      type="button"
                      onClick={() => setLightboxImage(body.url)}
                      className="group relative aspect-[3/4] overflow-hidden rounded-2xl border border-slate-700/80 bg-slate-950/90 transition-all duration-200 hover:border-slate-500 hover:scale-[1.02]"
                    >
                      <img src={body.url} alt="Полный рост" className="h-full w-full object-cover" />
                      <div className="pointer-events-none absolute inset-0 bg-gradient-to-t from-slate-950/75 via-transparent opacity-0 transition-opacity duration-200 group-hover:opacity-100" />
                      <span className="pointer-events-none absolute bottom-3 left-3 rounded-full border border-slate-700 bg-slate-900/85 px-2 py-1 text-[10px] uppercase tracking-[0.24em] text-slate-300">
                        Открыть
                      </span>
                    </button>
                  ))}
                </div>
              )}
            </div>
          </div>
        </div>

        <div className="border-t border-slate-800 bg-slate-900/80 px-6 py-4 flex justify-center">
          <button
            onClick={generateFullBody}
            disabled={generatingBody || !selectedFaceUrl || !promptText.trim()}
            className="w-full max-w-sm rounded-2xl border border-slate-700/80 bg-slate-900/80 px-6 py-3 text-sm font-semibold uppercase tracking-[0.18em] text-slate-100 transition-all duration-200 hover:border-slate-500 hover:bg-slate-800 disabled:cursor-not-allowed disabled:border-slate-700/50 disabled:bg-slate-900/60"
          >
            {generatingBody ? 'Генерация тела...' : 'Сделать фото'}
          </button>
        </div>
      </div>

      {lightboxImage && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 lightbox-backdrop--enter">
          <div className="absolute inset-0 bg-slate-950/90 backdrop-blur-sm" onClick={() => setLightboxImage(null)} />
          <div className="relative z-10 w-full max-w-5xl overflow-hidden rounded-[32px] border border-slate-700/80 bg-slate-950/95 shadow-[0_30px_80px_-30px_rgba(15,23,42,0.9)] lightbox-panel--enter">
            <div className="relative h-[min(76vh,560px)] bg-slate-950">
              <img src={lightboxImage} alt="Обзор" className="h-full w-full object-contain lightbox-image--enter" />
            </div>

            <div className="flex flex-col gap-3 border-t border-slate-700/80 bg-slate-900/90 p-4 sm:flex-row sm:items-center sm:justify-between">
              <button
                type="button"
                onClick={() => setLightboxImage(null)}
                className="w-full sm:w-auto rounded-2xl border border-slate-700/80 bg-slate-950/90 px-4 py-3 text-xs font-semibold uppercase tracking-[0.2em] text-slate-200 transition-all duration-200 hover:border-slate-500 hover:bg-slate-900"
              >
                Закрыть
              </button>
              {lightboxIsFace && !lightboxIsBody && (
                <button
                  type="button"
                  onClick={() => handleSelectFace(lightboxImage)}
                  className="w-full sm:w-auto rounded-2xl border border-indigo-500/30 bg-indigo-600/10 px-6 py-3 text-xs font-semibold uppercase tracking-[0.2em] text-indigo-100 transition-all duration-200 hover:border-indigo-400/60 hover:bg-indigo-600/20"
                >
                  Выбрать этот лик для фото
                </button>
              )}
            </div>
          </div>
        </div>
      )}

      <style>{`
        .custom-scrollbar::-webkit-scrollbar { width: 6px; }
        .custom-scrollbar::-webkit-scrollbar-track { background: rgba(15, 23, 42, 0.45); border-radius: 999px; }
        .custom-scrollbar::-webkit-scrollbar-thumb { background: rgba(71, 85, 105, 0.8); border-radius: 999px; }
        .custom-scrollbar::-webkit-scrollbar-thumb:hover { background: rgba(100, 116, 139, 1); }
      `}</style>
    </div>
  );
};

export default HeroForgeComponent;
