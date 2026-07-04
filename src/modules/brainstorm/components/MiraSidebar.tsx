import MagicBorder from '../../../components/magic/MagicBorder';
import type { ArtConcept } from '../../../types/artConcept';

interface MiraSidebarProps {
  loading: boolean;
  hasConcepts: boolean;
  activeConcept: ArtConcept | null;
  selectedConceptIndex: number | null;
}

export default function MiraSidebar({
  loading,
  hasConcepts,
  activeConcept,
  selectedConceptIndex,
}: MiraSidebarProps) {
  const showHint = hasConcepts && selectedConceptIndex === null;

  return (
    <MagicBorder variant="emerald" className="h-full min-h-[320px]" innerClassName="h-full p-5 md:p-6 flex flex-col">
      <div className="flex items-center gap-3 mb-4">
        <div className="mira-avatar" aria-hidden>
          ✦
        </div>
        <div>
          <p className="grimoire-kicker text-emerald-300/80">Литературный разбор</p>
          <h3 className="grimoire-display text-xl text-emerald-50">Мира</h3>
        </div>
      </div>

      <div className="mira-review-panel flex-1 flex flex-col min-h-0">
        {loading && (
          <div className="flex flex-col items-center justify-center flex-1 text-center py-8">
            <div className="mana-orb mb-4" aria-hidden />
            <p className="text-sm text-emerald-200/80 mana-pulse-text leading-relaxed">
              Мира ищет скрытые смыслы в тексте…
            </p>
          </div>
        )}

        {!loading && !hasConcepts && (
          <div className="flex flex-col justify-center flex-1 text-center py-6">
            <p className="text-3xl mb-3 opacity-40" aria-hidden>
              📜
            </p>
            <p className="text-sm text-slate-400 italic leading-relaxed px-2">
              Мира ждёт твой синопсис, чтобы разобрать его по полочкам…
            </p>
          </div>
        )}

        {!loading && hasConcepts && activeConcept && (
          <div key={`${activeConcept.id}-${activeConcept.title}`} className="mira-review-fade flex flex-col flex-1 min-h-0">
            {activeConcept.title && (
              <p className="text-[11px] font-mono text-violet-400/60 mb-3 truncate">
                Угол зрения: «{activeConcept.title}»
              </p>
            )}
            <div className="flex-1 overflow-y-auto pr-1">
              <p className="text-sm leading-relaxed text-slate-300/95 whitespace-pre-wrap">
                {activeConcept.review || 'Мира пока молчит по этому концепту — выбери другой путь визуала.'}
              </p>
            </div>
          </div>
        )}
      </div>

      {showHint && (
        <p className="mt-4 text-[11px] text-amber-200/70 border border-amber-500/25 bg-amber-950/20 rounded-lg px-3 py-2 leading-snug">
          💡 Сначала выбери концепт и послушай, что думает Мира!
        </p>
      )}
    </MagicBorder>
  );
}
