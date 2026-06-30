import { useCallback, useMemo, useRef, useState, type ReactNode } from 'react';
import { sendFlowToPipeline, sendImageToEdit, type DirectPayload } from '../api/directGenerate';
import { approveCoverForExport } from '../api/approveCover';
import GenerationLightbox from './GenerationLightbox';
import {
  CANVAS_PRESETS,
  FLUX_DEV_MODEL,
  LOCAL_MODEL,
  type AnalysisModel,
  type CanvasPresetId,
  type GeneratedCover,
  type RenderSettings,
  type SelfPromptState,
  type SelfPromptTzTab,
  type TechnicalSpec,
} from '../../../types/manifest';

const tzTabs: { id: SelfPromptTzTab; label: string }[] = [
  { id: 'magic-flow', label: '🔮 Магический поток' },
  { id: 'strict-layers', label: '⚙️ Строгие слои' },
];

const MAGIC_PLACEHOLDER =
  'Опиши свою обложку во всех деталях... Наш ИИ-директор послушно разложит твой творческий поток по строгим техническим слоям без капли лишней фантазии.';

export interface SelfPromptFormProps {
  projectId: string;
  technicalSpec: TechnicalSpec;
  renderSettings: RenderSettings;
  selfPrompt: SelfPromptState;
  generationHistory: GeneratedCover[];
  onAddGeneration: (cover: GeneratedCover) => void;
  onTechnicalSpecChange: (spec: TechnicalSpec) => void;
  onRenderSettingsChange: (settings: RenderSettings) => void;
  onSelfPromptChange: (state: SelfPromptState) => void;
}

const textareaClassName =
  'w-full rounded-xl border border-slate-700/80 bg-slate-950/80 px-4 py-3 text-sm text-slate-100 ' +
  'placeholder:text-slate-600 shadow-inner resize-y transition-all duration-200 ' +
  'focus:outline-none focus:border-indigo-500/60 focus:ring-2 focus:ring-indigo-500/25 ' +
  'hover:border-slate-600';

const inputClassName =
  'w-full rounded-xl border border-slate-700/80 bg-slate-950/80 px-4 py-2.5 text-sm text-slate-100 ' +
  'placeholder:text-slate-600 shadow-inner transition-all duration-200 ' +
  'focus:outline-none focus:border-indigo-500/60 focus:ring-2 focus:ring-indigo-500/25 ' +
  'hover:border-slate-600';

const selectClassName =
  'w-full rounded-xl border border-slate-700/80 bg-slate-950 px-4 py-2.5 text-sm text-slate-100 ' +
  'focus:outline-none focus:border-indigo-500/60 focus:ring-2 focus:ring-indigo-500/25 cursor-pointer';

type SpecField = keyof TechnicalSpec;

const technicalFields: {
  key: SpecField;
  label: string;
  sublabel: string;
  placeholder: string;
  rows: number;
  multiline: boolean;
}[] = [
  {
    key: 'subject',
    label: 'Subject',
    sublabel: 'Объекты',
    placeholder: 'Что в фокусе сцены...',
    rows: 4,
    multiline: true,
  },
  {
    key: 'environment',
    label: 'Environment',
    sublabel: 'Окружение',
    placeholder: 'Задний план, атмосфера, геометрия...',
    rows: 3,
    multiline: true,
  },
  {
    key: 'style',
    label: 'Style & Light',
    sublabel: 'Стиль и свет',
    placeholder: 'Материалы, свет, текстуры...',
    rows: 3,
    multiline: true,
  },
  {
    key: 'composition',
    label: 'Composition',
    sublabel: 'Зонирование',
    placeholder: 'Где оставить место под текст...',
    rows: 2,
    multiline: true,
  },
  {
    key: 'visual_reference',
    label: 'Visual Reference',
    sublabel: 'Визуальная справка',
    placeholder: 'Ссылка на референс или описание...',
    rows: 2,
    multiline: true,
  },
  {
    key: 'negative_constraints',
    label: 'Negative Constraints',
    sublabel: 'Ограничения',
    placeholder: 'Что исключить (plastic, neon...)',
    rows: 3,
    multiline: true,
  },
];

const analysisOptions: { id: AnalysisModel; title: string; desc: string; badge?: string }[] = [
  {
    id: 'gpt-oss-120b',
    title: 'OpenAI GPT-OSS 120B',
    desc: 'Базовый, но умный и бесплатный режим без ограничений',
  },
  {
    id: 'gemini-flash',
    title: 'Gemini Flash',
    desc: 'Режим Супер-Силы ⚡, Vision/Анализ графики',
    badge: '⚡',
  },
  {
    id: 'ollama-qwen-2-7b',
    title: 'Ollama Qwen 2 7B',
    desc: 'Локальный режим, Без цензуры!',
    badge: '🚫',
  }
];

function SectionTitle({ children }: { children: ReactNode }) {
  return (
    <h3 className="text-xs font-bold uppercase tracking-widest text-indigo-400/90 border-b border-slate-800 pb-2 mb-4">
      {children}
    </h3>
  );
}

export default function SelfPromptForm({
  projectId,
  technicalSpec,
  renderSettings,
  selfPrompt,
  generationHistory,
  onAddGeneration,
  onTechnicalSpecChange,
  onRenderSettingsChange,
  onSelfPromptChange,
}: SelfPromptFormProps) {
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [isDragging, setIsDragging] = useState(false);
  const [lightboxCover, setLightboxCover] = useState<GeneratedCover | null>(null);
  const [isApproving, setIsApproving] = useState(false);
  const [approveMessage, setApproveMessage] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [generateMessage, setGenerateMessage] = useState<string | null>(null);

  const isMagicFlow = selfPrompt.tz_tab === 'magic-flow';

  const livePayload = useMemo(() => {
  const base = {
      render_settings: renderSettings,
      analysis_model: selfPrompt.analysis_model,
      has_reference: Boolean(selfPrompt.reference_image_url),
    };

    if (isMagicFlow) {
      return {
        mode: 'raw-parse' as const,
        raw_prompt: selfPrompt.formData.raw_prompt,
        render_settings: base.render_settings,        // явно
        analysis_model: base.analysis_model,          // явно
        has_reference: base.has_reference,            // явно
      };
    }

    return {
      mode: 'self-prompt' as const,
      technical_spec: technicalSpec,
      render_settings: base.render_settings,          // явно
      analysis_model: base.analysis_model,            // явно
      has_reference: base.has_reference,              // явно
    };
  }, [
    isMagicFlow,
    technicalSpec,
    renderSettings,
    selfPrompt.analysis_model,
    selfPrompt.reference_image_url,
    selfPrompt.formData.raw_prompt,
  ]);

  const liveJson = useMemo(() => JSON.stringify(livePayload, null, 2), [livePayload]);

  const patchSpec = useCallback(
    (key: SpecField, value: string) => {
      onTechnicalSpecChange({ ...technicalSpec, [key]: value });
    },
    [technicalSpec, onTechnicalSpecChange],
  );

  const setTzTab = (tab: SelfPromptTzTab) => {
    onSelfPromptChange({ ...selfPrompt, tz_tab: tab });
  };

  const setRawPrompt = (raw_prompt: string) => {
    onSelfPromptChange({
      ...selfPrompt,
      formData: { ...selfPrompt.formData, raw_prompt },
    });
  };

  const handleCanvasPreset = (presetId: CanvasPresetId) => {
    const preset = CANVAS_PRESETS[presetId];
    onRenderSettingsChange({
      ...renderSettings,
      canvas_preset: presetId,
      aspect_ratio: preset.aspect_ratio,
      dimensions: { ...preset.dimensions },
    });
  };

  const loadReferenceFile = (file: File | undefined) => {
    if (!file || !file.type.startsWith('image/')) return;
    const reader = new FileReader();
    reader.onload = () => {
      const url = typeof reader.result === 'string' ? reader.result : null;
      onSelfPromptChange({ ...selfPrompt, reference_image_url: url });
    };
    reader.readAsDataURL(file);
  };

  const onDrop = (e: React.DragEvent) => {
    e.preventDefault();
    setIsDragging(false);
    loadReferenceFile(e.dataTransfer.files[0]);
  };

  const setReferenceFromUrl = (imageUrl: string) => {
    onSelfPromptChange({ ...selfPrompt, reference_image_url: imageUrl });
  };

  const handleApproveExport = async (cover: GeneratedCover) => {
    setIsApproving(true);
    setApproveMessage(null);
    try {
      await approveCoverForExport(projectId, cover);
      setApproveMessage('Статус «утверждено в печать» отправлен на бэкенд.');
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Не удалось отправить статус';
      setApproveMessage(`⚠️ ${msg}`);
    } finally {
      setIsApproving(false);
    }
  };

  const handleDirectGenerate = async () => {
    setLoading(true);
    setGenerateMessage('Инициализация пайплайна...');
  
    try {
     const isMagicFlow = selfPrompt.tz_tab === 'magic-flow'; // Проверяем текущий режим
      setGenerateMessage(isMagicFlow 
        ? 'Деконструируем метафоры через GPT-OSS 120B...' 
        : 'Склеиваем слои Манифеста...'
      );

      const basePayload = {
        render_settings: renderSettings,
        analysis_model: selfPrompt.analysis_model,
        has_reference: Boolean(selfPrompt.reference_image_url),
      };

      const livePayload: DirectPayload = isMagicFlow
        ? {
            ...basePayload,
            mode: 'raw-parse' as const,
            raw_prompt: selfPrompt.formData.raw_prompt
          }
        : {
            ...basePayload,
            mode: 'self-prompt' as const,
            technical_spec: technicalSpec,
          };
      
      let res;

      if (basePayload.has_reference) {
        res = await sendImageToEdit(livePayload, selfPrompt.reference_image_url);
      } else {
        // Вызываем наш обновленный пайплайн
        res = await sendFlowToPipeline(livePayload, selfPrompt.reference_image_url);
      }
      
      // 2. Формируем объект обложки для ленты генераций из b64/url ответа бэкенда
      const strictLayersPrompt = technicalFields
        .map(field => technicalSpec[field.key])
        .filter(value => value && value.trim() !== '')
        .join(' | ');

      const cover: GeneratedCover = {
        art_id: crypto.randomUUID(),
        url: res.imageUrl,
        timestamp: new Date().toISOString(),
        technical_prompt_used: isMagicFlow ? selfPrompt.formData.raw_prompt : strictLayersPrompt,
      };
  
      // Добавляем обложку в ленту (Катя сразу видит результат рендера Flux)
      onAddGeneration(cover);
      setGenerateMessage('Готово. Добавил результат в ленту генераций.');
  
      // 3. САМЫЙ СОК: Если это был Магический поток, ловим разобранный JSON от 120B модели
      if (isMagicFlow && res.parsedSpec) {
        // Мапим поля бэкенда (из TechnicalSpecDto) на стейт фронтенда
        onTechnicalSpecChange({
          ...technicalSpec,
          subject: res.parsedSpec.subject || '',
          environment: res.parsedSpec.environment || '',
          style: res.parsedSpec.style || res.parsedSpec.style || '',
          composition: res.parsedSpec.composition || '',
          visual_reference: res.parsedSpec.visual_reference || '',
          negative_constraints: res.parsedSpec.negative_constraints || '',
        });
        
        setGenerateMessage('Магический поток успешно распарсен. Слои интерфейса синхронизированы!');
      }
  
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Крах при отправке в пайплайн';
      setGenerateMessage(`⚠️ ${msg}`);
      // eslint-disable-next-line no-console
      console.error('Direct generate failed:', err);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="flex flex-col gap-8 w-full max-w-6xl mx-auto">
      <div className="rounded-xl border border-indigo-500/20 bg-indigo-600/5 px-4 py-3">
        <p className="text-sm font-semibold text-indigo-200">Direct Prompt — инженерная панель</p>
        <p className="text-xs text-slate-500 mt-1">
          {isMagicFlow
            ? 'Магический поток → formData.raw_prompt, режим JSON: raw-parse.'
            : 'Строгие слои → technical_spec, режим JSON: self-prompt.'}{' '}
          Anti-F5: автосохранение в localStorage.
        </p>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-[minmax(260px,320px)_1fr] gap-8 items-start">
        {/* Референс-модификатор (только исходник) */}
        <div className="flex flex-col gap-3 lg:sticky lg:top-4">
          <SectionTitle>Референс-модификатор</SectionTitle>
          <div className="aspect-[2/3] rounded-xl border border-dashed border-slate-600/80 bg-slate-950/80 overflow-hidden flex items-center justify-center relative">
            {selfPrompt.reference_image_url ? (
              <img
                src={selfPrompt.reference_image_url}
                alt="Референс для редактирования"
                className="w-full h-full object-contain bg-black/40"
              />
            ) : (
              <div className="text-center px-6 text-slate-600">
                <p className="text-3xl mb-2 opacity-40">📎</p>
                <p className="text-xs leading-relaxed">
                  Исходное изображение для правок.
                  <br />
                  Генерации — в ленте внизу.
                </p>
              </div>
            )}
          </div>

          <input
            ref={fileInputRef}
            type="file"
            accept="image/*"
            className="hidden"
            onChange={(e) => loadReferenceFile(e.target.files?.[0])}
          />
          <button
            type="button"
            onClick={() => fileInputRef.current?.click()}
            onDragOver={(e) => {
              e.preventDefault();
              setIsDragging(true);
            }}
            onDragLeave={() => setIsDragging(false)}
            onDrop={onDrop}
            className={`w-full rounded-xl border-2 border-dashed px-4 py-5 text-left transition-all ${
              isDragging
                ? 'border-indigo-400 bg-indigo-600/10 text-indigo-200'
                : 'border-slate-700 text-slate-400 hover:border-indigo-500/50 hover:bg-slate-900/80 hover:text-slate-200'
            }`}
          >
            <p className="text-sm font-semibold text-slate-200">
              Загрузить референс для редактирования
            </p>
            <p className="text-[11px] text-slate-500 mt-1">
              Drag-and-drop или клик · для Vision выберите Gemini Flash
            </p>
          </button>

          {selfPrompt.reference_image_url && (
            <button
              type="button"
              onClick={() => onSelfPromptChange({ ...selfPrompt, reference_image_url: null })}
              className="text-xs text-slate-500 hover:text-rose-400 transition-colors text-left"
            >
              Убрать референс
            </button>
          )}
        </div>

        {/* Форма */}
        <div className="flex flex-col gap-8 min-w-0">
          <section>
            <SectionTitle>Техническое ТЗ (technical_spec)</SectionTitle>

            <div
              className="flex flex-col sm:flex-row gap-1.5 p-1 rounded-xl bg-slate-950/90 border border-slate-800 mb-5"
              role="tablist"
              aria-label="Режим ввода ТЗ"
            >
              {tzTabs.map((tab) => {
                const active = selfPrompt.tz_tab === tab.id;
                return (
                  <button
                    key={tab.id}
                    type="button"
                    role="tab"
                    aria-selected={active}
                    onClick={() => setTzTab(tab.id)}
                    className={`flex-1 rounded-lg px-4 py-2.5 text-sm font-semibold transition-all ${
                      active
                        ? tab.id === 'magic-flow'
                          ? 'bg-gradient-to-r from-emerald-600/25 to-violet-600/25 border border-emerald-500/40 text-emerald-100 shadow-[0_0_12px_rgba(16,185,129,0.15)]'
                          : 'bg-indigo-600/20 border border-indigo-500/50 text-indigo-200 shadow-inner'
                        : 'border border-transparent text-slate-500 hover:text-slate-300 hover:bg-slate-900/80'
                    }`}
                  >
                    {tab.label}
                  </button>
                );
              })}
            </div>

            {isMagicFlow ? (
              <div className="relative group">
                <div
                  className="pointer-events-none absolute -inset-px rounded-2xl bg-gradient-to-br from-emerald-500/30 via-cyan-500/20 to-violet-600/30 opacity-60 group-focus-within:opacity-100 group-focus-within:animate-pulse blur-[1px]"
                  aria-hidden
                />
                <textarea
                  value={selfPrompt.formData.raw_prompt}
                  onChange={(e) => setRawPrompt(e.target.value)}
                  placeholder={MAGIC_PLACEHOLDER}
                  rows={14}
                  spellCheck={false}
                  className={
                    'elf-magic-field relative w-full min-h-[18rem] rounded-2xl border border-emerald-500/35 ' +
                    'bg-[#030712] px-5 py-4 text-sm leading-relaxed text-emerald-50/95 ' +
                    'placeholder:text-emerald-600/50 placeholder:italic placeholder:leading-relaxed ' +
                    'shadow-[inset_0_2px_24px_rgba(0,0,0,0.55)] resize-y ' +
                    'transition-all duration-300 outline-none ' +
                    'hover:border-cyan-400/40 ' +
                    'focus:border-violet-400/50 focus:ring-2 focus:ring-emerald-400/25 ' +
                    'focus:shadow-[0_0_15px_rgba(34,197,94,0.35),0_0_24px_rgba(139,92,246,0.28)]'
                  
                  }
                />
                </div>    
            ) : (
              <div className="flex flex-col gap-5" role="tabpanel">
                {technicalFields.map((field) => (
                  <label key={field.key} className="flex flex-col gap-2 group">
                    <span className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5">
                      <span className="text-sm font-bold text-slate-200 group-focus-within:text-indigo-300 transition-colors">
                        {field.label}
                      </span>
                      <span className="text-[11px] text-slate-500">({field.sublabel})</span>
                    </span>
                    {field.multiline ? (
                      <textarea
                        value={technicalSpec[field.key]}
                        onChange={(e) => patchSpec(field.key, e.target.value)}
                        placeholder={field.placeholder}
                        rows={field.rows}
                        className={textareaClassName}
                        spellCheck={false}
                      />
                    ) : (
                      <input
                        type="text"
                        value={technicalSpec[field.key]}
                        onChange={(e) => patchSpec(field.key, e.target.value)}
                        placeholder={field.placeholder}
                        className={inputClassName}
                        spellCheck={false}
                      />
                    )}
                  </label>
                ))}
              </div>
            )}

            <>
              <style>{`
                @keyframes gradientShift {
                  0% { background-position: 0% 50%; }
                  50% { background-position: 100% 50%; }
                  100% { background-position: 0% 50%; }
                }
              `}</style>

              <button
                type="button"
                onClick={handleDirectGenerate}
                disabled={
                  loading ||
                  (isMagicFlow
                    ? !selfPrompt.formData.raw_prompt.trim()
                    : !technicalSpec.subject.trim() &&
                      !technicalSpec.environment.trim() &&
                      !technicalSpec.style.trim())
                }
                style={
                  !loading
                    ? {
                        backgroundImage:
                          'linear-gradient(90deg, #4f46e5 0%, #8b5cf6 25%, #ec4899 50%, #f97316 75%, #4f46e5 100%)',
                        backgroundSize: '300% 100%',
                        animation: 'gradientShift 6s ease infinite',
                        cursor: 'pointer',
                      }
                    : undefined
                }
                className={`w-full mt-5 rounded-xl px-5 py-4 text-base font-extrabold tracking-wide transition-all border ${
                  loading
                    ? 'bg-slate-800 text-slate-400 border-slate-700 cursor-not-allowed'
                    : 'text-white border-indigo-400/30 hover:from-indigo-500 hover:via-violet-500 hover:to-fuchsia-500 shadow-[0_8px_22px_rgba(99,102,241,0.35)] hover:shadow-[0_10px_30px_rgba(168,85,247,0.32)]'
                } disabled:opacity-60 disabled:cursor-not-allowed`}
              >
                {loading ? '🔮 Ллама колдует, Флакс рисует...' : '⚡ ЗАПУСТИТЬ МАГИЧЕСКИЙ ПОТОК'}
              </button>
            </>

            {generateMessage && (
              <p className="text-xs text-slate-400 mt-2">{generateMessage}</p>
            )}
          </section>

          <section>
            <SectionTitle>Настройки рендера (render_settings)</SectionTitle>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <label className="flex flex-col gap-2">
                <span className="text-sm font-bold text-slate-200">Модель генерации</span>
                <select
                  value={renderSettings.model}
                  onChange={(e) =>
                    onRenderSettingsChange({ ...renderSettings, model: e.target.value })
                  }
                  className={selectClassName}
                >
                  <option value={FLUX_DEV_MODEL}>Flux 1.dev (Hugging Face)</option>
                  <option value={LOCAL_MODEL}>EventHorizon (Local)</option>
                </select>
              </label>
              <label className="flex flex-col gap-2">
                <span className="text-sm font-bold text-slate-200">Размер холста</span>
                <select
                  value={renderSettings.canvas_preset}
                  onChange={(e) => handleCanvasPreset(e.target.value as CanvasPresetId)}
                  className={selectClassName}
                >
                  {(Object.keys(CANVAS_PRESETS) as CanvasPresetId[]).map((id) => (
                    <option key={id} value={id}>
                      {CANVAS_PRESETS[id].label}
                    </option>
                  ))}
                </select>
              </label>
            </div>
          </section>

          <section>
            <SectionTitle>Выбор ИИ-интеллекта</SectionTitle>
            <div className="flex flex-col sm:flex-row gap-2 p-1 rounded-xl bg-slate-950/80 border border-slate-800">
              {analysisOptions.map((opt) => {
                const active = selfPrompt.analysis_model === opt.id;
                return (
                  <button
                    key={opt.id}
                    type="button"
                    onClick={() =>
                      onSelfPromptChange({ ...selfPrompt, analysis_model: opt.id })
                    }
                    className={`flex-1 rounded-lg px-4 py-3 text-left transition-all ${
                      active
                        ? 'bg-indigo-600/20 border border-indigo-500/50 shadow-inner'
                        : 'border border-transparent hover:bg-slate-900'
                    }`}
                  >
                    <p
                      className={`text-sm font-bold flex items-center gap-1.5 ${
                        active ? 'text-indigo-200' : 'text-slate-300'
                      }`}
                    >
                      {opt.title}
                      {opt.badge && <span className="text-base">{opt.badge}</span>}
                    </p>
                    <p className="text-[11px] text-slate-500 mt-1 leading-snug">{opt.desc}</p>
                  </button>
                );
              })}
            </div>
            {selfPrompt.analysis_model === 'gemini-flash' && (
              <div className="mt-3 rounded-lg border border-amber-500/30 bg-amber-500/10 px-3 py-2.5 text-[11px] text-amber-100/90 leading-relaxed">
                ⚠️ Внимание: Использует лимитированные запросы (до 20 в минуту). Рекомендуется для
                анализа готовых изображений и сложных правок.
              </div>
            )}
            {selfPrompt.analysis_model === 'ollama-qwen-2-7b' && (
              <div className="mt-3 rounded-lg border border-amber-500/30 bg-amber-500/10 px-3 py-2.5 text-[11px] text-amber-100/90 leading-relaxed">
                ⚠️ Внимание: Модель не имеет встроенной цензуры кроме минимальной, использовать с осторожностью!
              </div>
            )}
          </section>
        </div>
      </div>

      {/* Live JSON */}
      <section>
        <SectionTitle>Live-preview JSON</SectionTitle>
        <pre className="rounded-xl border border-slate-800 bg-slate-950 p-4 overflow-x-auto text-xs leading-relaxed">
          <code className="font-mono text-emerald-400/90">{liveJson}</code>
        </pre>
      </section>

      {/* Лента генераций сессии */}
      <section className="w-full -mx-2 md:-mx-0">
        <h3 className="text-sm font-bold text-slate-200 mb-1">🎨 История генераций сессии</h3>
        <p className="text-[11px] text-slate-500 mb-2">
          Новые — слева. Клик по миниатюре открывает студийный лайтбокс.
        </p>
        {approveMessage && (
          <p className="text-xs text-emerald-400/90 mb-2 px-1">{approveMessage}</p>
        )}
        <div className="flex flex-row overflow-x-auto gap-4 py-4 px-1 scroll-smooth">
          {generationHistory.length === 0 ? (
            <p className="text-sm text-slate-600 px-2 py-8">
              Пока нет генераций в этой сессии. Запустите тестовую генерацию из шапки.
            </p>
          ) : (
            generationHistory.map((item) => (
              <button
                key={item.art_id}
                type="button"
                onClick={() => setLightboxCover(item)}
                className="group shrink-0 w-28 sm:w-32 rounded-xl overflow-hidden border border-slate-700/80 bg-slate-900 shadow-lg shadow-black/30 transition-transform duration-200 hover:scale-105 hover:border-indigo-500/50 hover:shadow-indigo-900/20 focus:outline-none focus:ring-2 focus:ring-indigo-500/40"
              >
                <div className="aspect-[2/3] overflow-hidden">
                  <img
                    src={item.url}
                    alt=""
                    className="w-full h-full object-cover transition-transform duration-300 group-hover:brightness-110"
                  />
                </div>
                <p className="text-[9px] font-mono text-slate-500 truncate px-2 py-1.5 bg-slate-950/90">
                  {new Date(item.timestamp).toLocaleTimeString('ru-RU', {
                    hour: '2-digit',
                    minute: '2-digit',
                  })}
                </p>
              </button>
            ))
          )}
        </div>
      </section>

      {lightboxCover && (
        <GenerationLightbox
          cover={lightboxCover}
          onClose={() => setLightboxCover(null)}
          onUseAsReference={setReferenceFromUrl}
          onApproveExport={handleApproveExport}
          isApproving={isApproving}
        />
      )}
    </div>
  );
}
