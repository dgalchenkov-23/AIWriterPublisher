import type { MouseEvent } from 'react';
import MagicBorder from '../../../components/magic/MagicBorder';
import type { ArtConcept } from '../../../types/artConcept';

const variants: Array<'violet' | 'emerald' | 'amber'> = ['violet', 'emerald', 'amber'];

interface ConceptCardProps {
  concept: ArtConcept;
  index: number;
  delayMs?: number;
  isSelected: boolean;
  onSelect: (concept: ArtConcept, index: number) => void;
  onTransferToForge: (concept: ArtConcept) => void;
}

export default function ConceptCard({
  concept,
  index,
  delayMs = 0,
  isSelected,
  onSelect,
  onTransferToForge,
}: ConceptCardProps) {
  const variant = variants[index % variants.length];
  const canTransfer = Boolean(concept.rawPrompt.trim()) && isSelected;

  const handleCardClick = () => {
    onSelect(concept, index);
  };

  const handleForgeClick = (e: MouseEvent) => {
    e.stopPropagation();
    if (!isSelected) return;
    if (!concept.rawPrompt.trim()) return;
    onTransferToForge(concept);
  };

  return (
    <div
      className={`concept-card-enter h-full cursor-pointer transition-transform duration-200 ${
        isSelected ? 'concept-card--selected scale-[1.02]' : 'hover:scale-[1.01]'
      }`}
      style={{ animationDelay: `${delayMs}ms` }}
      onClick={handleCardClick}
      role="button"
      tabIndex={0}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault();
          handleCardClick();
        }
      }}
      aria-pressed={isSelected}
      aria-label={`Выбрать концепт ${index + 1}: ${concept.title}`}
    >
      <MagicBorder
        variant={variant}
        className={`h-full ${isSelected ? 'concept-border--selected' : ''}`}
        innerClassName="h-full p-5 md:p-6 flex flex-col gap-4"
      >
        <div className="flex items-start justify-between gap-3">
          <div className="flex flex-col gap-1">
            <span className="grimoire-kicker">
              Концепт {index + 1}
              {isSelected && (
                <span className="ml-2 text-emerald-400/90 normal-case tracking-normal text-[10px]">
                  ✓ выбран
                </span>
              )}
            </span>
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
          onClick={handleForgeClick}
          disabled={!canTransfer}
          className="forge-transfer-btn w-full mt-1"
          title={
            !isSelected
              ? 'Сначала выбери концепт и послушай, что думает Мира!'
              : canTransfer
                ? 'Открыть Self-Prompt с готовым техническим промптом'
                : 'Технический промпт ещё не сгенерирован'
          }
        >
          <span className="forge-transfer-btn__glow" aria-hidden />
          <span className="forge-transfer-btn__shimmer" aria-hidden />
          <span className="forge-transfer-btn__label">
            {isSelected ? '⚒️ Передать в кузницу (Self-Prompt)' : '👆 Сначала выбери концепт'}
          </span>
        </button>
      </MagicBorder>
    </div>
  );
}
