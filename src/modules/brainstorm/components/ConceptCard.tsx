import MagicBorder from '../../../components/magic/MagicBorder';
import type { ArtConcept } from '../../../types/artConcept';

const variants: Array<'violet' | 'emerald' | 'amber'> = ['violet', 'emerald', 'amber'];

interface ConceptCardProps {
  concept: ArtConcept;
  index: number;
  delayMs?: number;
  onTransferToForge: (concept: ArtConcept) => void;
}

export default function ConceptCard({
  concept,
  index,
  delayMs = 0,
  onTransferToForge,
}: ConceptCardProps) {
  const variant = variants[index % variants.length];
  const canTransfer = Boolean(concept.rawPrompt.trim());

  return (
    <div className="concept-card-enter h-full" style={{ animationDelay: `${delayMs}ms` }}>
      <MagicBorder
        variant={variant}
        className="h-full"
        innerClassName="h-full p-5 md:p-6 flex flex-col gap-4"
      >
        <div className="flex items-start justify-between gap-3">
          <div className="flex flex-col gap-1">
            <span className="grimoire-kicker">Концепт {concept.id}</span>
            {concept.aspectRatio && (
              <span className="text-[10px] font-mono text-violet-400/60">
                📐 {concept.aspectRatio}
              </span>
            )}
          </div>
          <span className="text-2xl opacity-70" aria-hidden>
            {index === 0 ? '🌙' : index === 1 ? '✨' : '🔮'}
          </span>
        </div>

        <h3 className="grimoire-title text-2xl leading-tight text-violet-100">{concept.title}</h3>

        <p className="text-sm leading-relaxed text-slate-300/90 flex-1">{concept.description}</p>

        <div className="rounded-xl border border-white/5 bg-black/25 px-4 py-3">
          <p className="text-[10px] font-bold uppercase tracking-widest text-indigo-300/70 mb-2">
            Визуальные элементы
          </p>
          <p className="text-xs leading-relaxed text-slate-400 whitespace-pre-wrap">
            {concept.visualElements}
          </p>
        </div>

        <button
          type="button"
          onClick={() => onTransferToForge(concept)}
          disabled={!canTransfer}
          className="forge-transfer-btn w-full mt-1"
          title={
            canTransfer
              ? 'Открыть Self-Prompt с готовым техническим промптом'
              : 'Технический промпт ещё не сгенерирован'
          }
        >
          <span className="forge-transfer-btn__glow" aria-hidden />
          <span className="forge-transfer-btn__shimmer" aria-hidden />
          <span className="forge-transfer-btn__label">⚒️ Передать в кузницу (Self-Prompt)</span>
        </button>
      </MagicBorder>
    </div>
  );
}
