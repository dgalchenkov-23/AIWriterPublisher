import { useState } from 'react';
import MagicBorder from '../../../components/magic/MagicBorder';
import ShimmerButton from '../../../components/magic/ShimmerButton';
import type { ArtConcept } from '../../../types/artConcept';
import { brainstormConcepts } from '../api/brainstormConcepts';
import ConceptCard from './ConceptCard';

const GENRES = [
  'Фэнтези',
  'Научная фантастика',
  'Детектив',
  'Хоррор',
  'Романтика',
  'Исторический роман',
  'Триллер',
  'Магический реализм',
] as const;

const SYNOPSIS_PLACEHOLDER =
  'Опишите мир, героя и настроение книги… Например: «В туманном портовом городе юная алхимик ищет пропавшего брата, а по ночам по улицам шепчутся тени прошлого…»';

export default function BrainstormPanel({
  onTransferToForge,
}: {
  onTransferToForge: (concept: ArtConcept) => void;
}) {
  const [genre, setGenre] = useState<string>(GENRES[0]);
  const [description, setDescription] = useState('');
  const [loading, setLoading] = useState(false);
  const [concepts, setConcepts] = useState<ArtConcept[]>([]);
  const [error, setError] = useState<string | null>(null);

  const handleBrainstorm = async () => {
    if (!description.trim()) return;

    setLoading(true);
    setError(null);
    setConcepts([]);

    try {
      const result = await brainstormConcepts({ genre, description: description.trim() });
      if (result.length === 0) {
        setError('Арт-директор вернул пустой ответ. Попробуйте расширить описание.');
        return;
      }
      setConcepts(result);
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Не удалось вызвать ритуал брейншторма';
      setError(msg);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="grimoire-page w-full max-w-6xl mx-auto">
      <header className="mb-10 text-center md:text-left">
        <p className="grimoire-kicker mb-2">Ритуал первого акта</p>
        <h2 className="grimoire-display text-4xl md:text-5xl text-transparent bg-clip-text bg-gradient-to-r from-violet-200 via-fuchsia-200 to-indigo-200">
          Гримуар идей обложки
        </h2>
        <p className="mt-3 text-sm md:text-base text-slate-400 max-w-2xl leading-relaxed">
          Расскажите о книге — и студия соткёт три магических направления. Выберите то, что
          откликается сердцу, и переходите к точной генерации.
        </p>
      </header>

      <div className="flex flex-col gap-10">
        {/* Input — The Ritual */}
        <MagicBorder variant="violet" innerClassName="p-6 md:p-7 max-w-3xl">
          <p className="grimoire-kicker mb-4">✦ Вводные данные</p>

          <label className="flex flex-col gap-2 mb-5">
            <span className="text-sm font-semibold text-violet-100">Жанр</span>
            <select
              value={genre}
              onChange={(e) => setGenre(e.target.value)}
              className="grimoire-select"
            >
              {GENRES.map((g) => (
                <option key={g} value={g}>
                  {g}
                </option>
              ))}
            </select>
          </label>

          <label className="flex flex-col gap-2 mb-6">
            <span className="text-sm font-semibold text-violet-100">Синопсис / описание книги</span>
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder={SYNOPSIS_PLACEHOLDER}
              rows={10}
              spellCheck
              className="grimoire-textarea"
            />
          </label>

          <ShimmerButton
            onClick={handleBrainstorm}
            loading={loading}
            disabled={!description.trim()}
            className="w-full"
          >
            {loading ? '🌀 Ткаем магические узоры…' : '✨ Призвать 3 концепции обложки'}
          </ShimmerButton>

          {error && (
            <p className="mt-4 text-sm text-rose-300/90 rounded-lg border border-rose-500/30 bg-rose-950/30 px-3 py-2">
              {error}
            </p>
          )}
        </MagicBorder>

        {/* Output — The Concepts */}
        <div className="flex flex-col gap-5">
          <div className="flex items-center justify-between gap-3">
            <p className="grimoire-kicker">✦ Три пути визуала</p>
            {concepts.length > 0 && (
              <span className="text-xs text-emerald-300/80 font-mono">
                {concepts.length} / 3 готово
              </span>
            )}
          </div>

          {loading && (
            <MagicBorder variant="emerald" innerClassName="p-12 flex flex-col items-center justify-center text-center min-h-[240px]">
              <div className="mana-orb mb-5" aria-hidden />
              <p className="grimoire-display text-2xl text-emerald-100/90 mana-pulse-text">
                Ткаем магические узоры…
              </p>
              <p className="text-sm text-slate-500 mt-2 max-w-md">
                Арт-директор читает синопсис и вызывает три альтернативные вселенные обложки
              </p>
            </MagicBorder>
          )}

          {!loading && concepts.length === 0 && !error && (
            <MagicBorder variant="amber" innerClassName="p-10 flex flex-col items-center justify-center text-center min-h-[200px]">
              <p className="text-4xl mb-3 opacity-50" aria-hidden>
                📖
              </p>
              <p className="text-sm text-slate-500 max-w-lg leading-relaxed">
                Здесь появятся три карточки-концепта с названием, описанием для автора и
                визуальными элементами для иллюстратора.
              </p>
            </MagicBorder>
          )}

          {!loading && concepts.length > 0 && (
            <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-5">
              {concepts.map((concept, index) => (
                <ConceptCard
                  key={`${concept.id}-${concept.title}`}
                  concept={concept}
                  index={index}
                  delayMs={index * 120}
                  onTransferToForge={onTransferToForge}
                />
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
